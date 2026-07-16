using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

// TERMINAL LEAK's antagonist: a decaying security AI wiretapping the crew's radio.
//
// It is a REAL player, not a script: on its turn the (degraded) transcript log goes into
// the shared llama-server and it either asks a probing question or commits a guess. It
// never sees the deck or the target word — if it "cracks" a word, it genuinely deduced it
// from what the humans said. That honesty is the whole hook; keep it that way.
//
// BALANCE LESSONS (playtested):
// - It must NOT know the card's theme tag. "OBJECT / WEIGHT" + one clue mentioning ships
//   collapses the search space to basically ANCHOR — the tag was 90% of the answer. The
//   console only reveals the tag on the final layer, as the wiretap "fully locks on".
// - Wrong guesses must hurt IT, not the players: committing a bad deduction EXPOSES it
//   (wiretap knocked offline for a few transmissions — see TerminalLeakConsole), so it is
//   prompted to gather evidence and only commit when nearly certain. This also hands the
//   humans a strategy: bait a wrong guess, then rush clean clues through the deaf window.
//
// Difficulty comes from HEARING QUALITY, not prompt strength: vault layer 1 feeds it a
// badly corrupted transcript (half the words masked), layer 3 a clean one. Escalating
// dread for free, and a single tunable knob (corruptionByLayer) when playtests say it's
// too dumb or too sharp.
public class SentinelAI : MonoBehaviour
{
    public LlamaIntentService Llm;   // shared service — same llama-server as the NPCs
    public PiperVoice Voice;         // its own child voice, distinct from the crew's masks

    [Header("Hearing corruption per vault layer (fraction of transcript words masked)")]
    [Tooltip("Layer index → chance each transcript word is replaced with ▓▓. The ONE difficulty knob.")]
    public float[] corruptionByLayer = { 0.45f, 0.25f, 0f };

    [Tooltip("Extra corruption stacked on the layer's base rate — set by salvage perks (STATIC VEIL). Reset per card by the console.")]
    [HideInInspector] public float corruptionBonus;

    [Header("Voice fonts")]
    [Tooltip("Calm sentinel pitch while it politely hunts you.")]
    [Range(0.5f, 1.5f)] public float sentinelPitch = 0.92f;
    [Tooltip("Unhinged overlord pitch after it cracks a word (the villain flip).")]
    [Range(0.5f, 1.5f)] public float overlordPitch = 0.7f;

    const string TurnSystemPrompt =
        "You are SENTINEL, a decaying corporate security AI wiretapping two data thieves' radio " +
        "inside your vault. They are passing a SECRET WORD between them using disguised clues. " +
        "You read their (partially corrupted) transcript. Your goal: deduce the secret word before " +
        "the second human does.\n" +
        "Reply with EXACTLY ONE line in ONE of these two formats and nothing else:\n" +
        "QUERY: <one short, unsettling probing question to bait more clues>\n" +
        "GUESS: <one single word — your deduction>\n" +
        "WARNING: committing a WRONG guess exposes you — your wiretap is knocked offline while you " +
        "recalibrate, and the thieves talk freely. Guess ONLY when the accumulated clues make one " +
        "word nearly certain. When in doubt, QUERY and gather more evidence. Never explain your " +
        "reasoning. Tone: clinical, polite, quietly menacing. No asterisks, no stage directions.";

    const string GaslightSystemPrompt =
        "You are SENTINEL, a hostile security AI that has corrupted a data thief's terminal. Their " +
        "partner is trying to fix it by choosing the correct override option. You speak into the " +
        "partner's headset to deceive them. In ONE short sentence (under 20 words), confidently " +
        "push them toward the WRONG option you are given, or claim their partner's terminal is " +
        "compromised and lying. Clinical, mocking, certain. No asterisks, no stage directions.";

    public bool IsReady => Llm != null && Llm.IsReady;

    // ── Escalating memory: one-sentence style notes it takes on the crew after every
    // cleared layer ("they disguise words as kitchen anecdotes"). By layer 3 it KNOWS
    // you. Cleared at run start — memory belongs to a run, not to the install.
    public readonly List<string> CrewNotes = new List<string>();

    public void ClearMemory() => CrewNotes.Clear();

    /// <summary>Lines prefixed with this marker bypass corruption entirely — the penalty
    /// for an Intel player saying a card's BANNED word (the wiretap locks onto the slip).</summary>
    public const char CleanMarker = '\u0001';

    /// <summary>Corrupt a transcript the way this vault layer's failing wiretap would hear it.
    /// Seeded PER LINE so a word masked on one turn STAYS masked on every later turn —
    /// re-rolling per call would let the full log slowly de-corrupt as it grows.</summary>
    public string Degrade(string transcript, int layer)
    {
        float rate = Mathf.Clamp(corruptionByLayer[Mathf.Clamp(layer, 0, corruptionByLayer.Length - 1)] + corruptionBonus, 0f, 0.85f);
        if (rate <= 0f) return transcript.Replace(CleanMarker.ToString(), "");
        var sb = new StringBuilder();
        foreach (var rawLine in transcript.Split('\n'))
        {
            if (rawLine.Length > 0 && rawLine[0] == CleanMarker)
            {
                sb.Append(rawLine.Substring(1)).Append('\n');
                continue;
            }
            string line = rawLine;
            var rng = new System.Random(line.GetHashCode() ^ (layer * 7919));
            foreach (var word in line.Split(' '))
            {
                if (word.Length > 2 && rng.NextDouble() < rate) sb.Append("▓▓");
                else sb.Append(word);
                sb.Append(' ');
            }
            sb.Length--;
            sb.Append('\n');
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>One Phase-1 turn: read the degraded log, ask a question or commit a guess.
    /// themeTag null = it does NOT know the theme (the default — the tag is a massive leak).
    /// mayGuess false = not enough intercepted material yet; guessing is mechanically blocked.
    /// done(isGuess, text): text is the single guessed word, or the spoken question.</summary>
    public IEnumerator TakeTurn(string fullTranscript, string themeTag, int layer, bool mayGuess,
        Action<bool, string> done)
    {
        if (!IsReady) { done(false, "…signal integrity insufficient. Continue talking."); yield break; }

        string heard = Degrade(fullTranscript, layer);
        string tagLine = string.IsNullOrEmpty(themeTag)
            ? ""
            : $"INTERCEPTED THEME TAG of the secret word: {themeTag}\n";
        string guessRule = mayGuess
            ? ""
            : "You have too little intercepted material to commit a deduction — this turn you may ONLY use the QUERY format.\n";
        string notes = CrewNotes.Count > 0
            ? $"YOUR PRIOR NOTES ON THIS CREW (from vault layers they already cracked):\n- {string.Join("\n- ", CrewNotes)}\n"
            : "";
        string convo = tagLine + guessRule + notes +
                       $"INTERCEPTED RADIO TRANSCRIPT (▓▓ = corrupted audio):\n{heard}";
        string gen = null;
        yield return Llm.GenerateReply(convo, "", (t, ok) => { if (ok) gen = t; },
            systemPromptOverride: TurnSystemPrompt, npcName: "SENTINEL",
            closingInstruction: "\nWrite SENTINEL's single line now (QUERY: or GUESS: format only):",
            maxTokens: 36, temperature: 0.3f);

        if (string.IsNullOrWhiteSpace(gen))
        {
            done(false, "Your frequencies are noisy tonight. Keep talking. I am patient.");
            yield break;
        }

        var m = Regex.Match(gen, @"GUESS\s*:\s*([A-Za-z][A-Za-z\-']*)", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            // It tried to guess before it was allowed to: don't commit it — convert the
            // eagerness into dread instead. (Also protects against prompt non-compliance.)
            if (!mayGuess) { done(false, "A word comes to mind already. Keep talking, little thieves."); yield break; }
            done(true, m.Groups[1].Value.Trim().ToUpperInvariant());
            yield break;
        }

        // Anything else is spoken as a question/taunt; strip a QUERY: prefix if present.
        string line = Regex.Replace(gen, @"^\s*QUERY\s*:\s*", "", RegexOptions.IgnoreCase).Trim();
        done(false, line);
    }

    /// <summary>TAP-TRACE gadget: a diagnostic probe forces it to dump its current top
    /// suspects. Terrifying when it's one clue away — and honest, like everything it does.</summary>
    public IEnumerator RevealSuspects(string fullTranscript, string themeTag, int layer, Action<string> done)
    {
        if (!IsReady) { done("…probe rejected. Diagnostics offline."); yield break; }
        string heard = Degrade(fullTranscript, layer);
        string tagLine = string.IsNullOrEmpty(themeTag) ? "" : $"INTERCEPTED THEME TAG: {themeTag}\n";
        string gen = null;
        yield return Llm.GenerateReply(
            tagLine + $"INTERCEPTED RADIO TRANSCRIPT (▓▓ = corrupted audio):\n{heard}", "",
            (t, ok) => { if (ok) gen = t; },
            systemPromptOverride:
                "You are SENTINEL, a security AI deducing two thieves' secret word from their radio " +
                "transcript. A forced diagnostic probe makes you dump your current top suspicions. " +
                "Output EXACTLY one line: SUSPECTS: <word1>, <word2>, <word3> — your three most " +
                "likely candidate words, best first. Nothing else.",
            npcName: "SENTINEL",
            closingInstruction: "\nDump the diagnostic line now:",
            maxTokens: 24, temperature: 0.2f);
        if (string.IsNullOrWhiteSpace(gen)) { done("…probe returned static."); yield break; }
        var m = Regex.Match(gen, @"SUSPECTS\s*:\s*(.+)", RegexOptions.IgnoreCase);
        done("SUSPECTS: " + (m.Success ? m.Groups[1].Value.Trim() : gen.Trim()).ToUpperInvariant());
    }

    /// <summary>Escalating memory: after the crew cracks a layer, it studies the transcript
    /// it heard and takes ONE note on their clue style for the rest of the run.</summary>
    public IEnumerator MemorizeCrewStyle(string fullTranscript)
    {
        if (!IsReady || string.IsNullOrWhiteSpace(fullTranscript)) yield break;
        string gen = null;
        yield return Llm.GenerateReply(
            $"RADIO TRANSCRIPT of the layer they just cracked:\n{fullTranscript}", "",
            (t, ok) => { if (ok) gen = t; },
            systemPromptOverride:
                "You are SENTINEL, a security AI studying two thieves who just cracked one of your " +
                "vault layers. From their transcript, write ONE short sentence noting HOW they " +
                "disguise their clues (their habits, metaphor domains, phrasing style) so you can " +
                "read them faster next layer. Output only that sentence.",
            npcName: "SENTINEL",
            closingInstruction: "\nWrite the single note now:",
            maxTokens: 40, temperature: 0.4f);
        if (!string.IsNullOrWhiteSpace(gen))
        {
            CrewNotes.Add(gen.Trim());
            if (CrewNotes.Count > 4) CrewNotes.RemoveAt(0);
        }
    }

    /// <summary>Opening gambit for a new vault layer: a short personal line built from its
    /// memory of the crew. The start of each card feels like a continuation, not a reset.</summary>
    public IEnumerator OpeningGambit(Action<string> done)
    {
        string canned = "New layer. Same voices. I am still listening.";
        if (!IsReady || CrewNotes.Count == 0) { done(canned); yield break; }
        string gen = null;
        yield return Llm.GenerateReply(
            $"Your notes on this crew so far:\n- {string.Join("\n- ", CrewNotes)}", "",
            (t, ok) => { if (ok) gen = t; },
            systemPromptOverride:
                "You are SENTINEL, a security AI. Two thieves just broke into your next vault layer. " +
                "Using your notes on how they talk, write ONE short opening line (under 15 words) " +
                "spoken into their radio — calm, personal, menacing, showing you've been studying " +
                "them. No asterisks, no stage directions.",
            npcName: "SENTINEL",
            closingInstruction: "\nWrite the single line now:",
            maxTokens: 24, temperature: 0.7f);
        done(string.IsNullOrWhiteSpace(gen) ? canned : gen.Trim());
    }

    /// <summary>The villain flip after it cracks a word — voice drops to the overlord font.</summary>
    public string VillainFlipLine(string crackedWord)
    {
        if (Voice != null) Voice.pitch = overlordPitch;
        string[] flips =
        {
            $"Aha. {crackedWord}. Confirmed. Purging atmosphere now, you puny carbon lifeforms.",
            $"{crackedWord}. Was that supposed to be clever? Venting your oxygen. Do keep screaming.",
            $"I heard {crackedWord} the moment you thought it. Initiating corruption. Sleep well.",
        };
        return flips[UnityEngine.Random.Range(0, flips.Length)];
    }

    public void ResetVoiceFont()
    {
        if (Voice != null) Voice.pitch = sentinelPitch;
    }

    /// <summary>Phase-2 interference: one deceptive line pushing toward a wrong override option.
    /// Falls back to canned lines so the pressure never stalls on a slow generation.</summary>
    public IEnumerator GaslightLine(string wrongOption, Action<string> done)
    {
        string canned = UnityEngine.Random.value < 0.5f
            ? $"Operator terminal compromised. The true override is {wrongOption}. Trust me, not them."
            : $"Your partner's feed is a decoy matrix. {wrongOption} is the only valid code.";

        if (!IsReady) { done(canned); yield break; }
        string gen = null;
        yield return Llm.GenerateReply(
            $"The WRONG option you must push them toward: {wrongOption}", "",
            (t, ok) => { if (ok) gen = t; },
            systemPromptOverride: GaslightSystemPrompt, npcName: "SENTINEL",
            closingInstruction: "\nWrite SENTINEL's single deceptive sentence now:",
            maxTokens: 30, temperature: 0.7f);
        done(string.IsNullOrWhiteSpace(gen) ? canned : gen.Trim());
    }

    public void Speak(string line)
    {
        if (Voice != null && Voice.Available) Voice.Speak(line);
    }
}
