using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

// GNOMES & GASLIGHT — one suspect's live interrogation. Reuses the proven NPC pipeline
// wholesale (Vosk partials -> Whisper refine -> local LLM -> Piper voice, same pattern as
// CompanionConsole/GrizTestConsole) but the BRAIN here is SuspectBrain, not open chat: a
// classifier reads the player's pressure KIND (comfort/evidence/flattery/blunt), the state
// machine (patience/false-give/broken) decides what's ALLOWED to be said, and only THEN
// does the LLM perform it — same facts-cage principle as Griz's price, GrizNPC's secrets,
// the Sentinel's deck. The model never holds the case; it only ever voices what the code
// says this suspect is allowed to reveal right now.
public class InterrogationConsole : MonoBehaviour
{
    public VoskSpeechToText Vosk;
    public WhisperTranscriber Whisper;
    public LlamaIntentService Llm;
    public PiperVoice Voice;
    public SuspectBrain Brain;
    public CaseBoard Board;
    public CaseRunner Runner;
    [Tooltip("Optional 3D bust (blendshape model) for this suspect — same GrizAnimatorLink " +
             "stack as the GrizNPC scenes (blinking/lipsync/facial expressions). Purely " +
             "cosmetic: the interrogation loop runs identically with or without one.")]
    public GrizAnimatorLink AnimLink;

    [Header("Utterance timing (same tuning as the NPC consoles)")]
    public float silenceToSendSeconds = 1f;
    public float speakingVolume = 0.06f;
    public float minSendPeak = 0.05f;

    [Tooltip("Currently 'held' evidence item id the player can present this turn (set by the UI evidence tray). Cleared after one use.")]
    public string presentedEvidenceId;

    bool _utteranceOpen;
    string _pendingText = "";
    float _pendingVolume, _lastVoiceActivity, _peakVolume;
    string _partial = "";
    readonly List<string> _history = new List<string>();
    int _pendingRequests;

    // Starts FALSE — the console must not accept speech until BeginInterview() runs, or it
    // captures the player (and the narration audio) during the intro and dossier card.
    public bool Active { get; private set; }
    /// <summary>Live in-progress speech-to-text, for a "hearing: ..." UI indicator — mic-only
    /// input has no other feedback that the player is actually being heard.</summary>
    public string LiveHearingText => string.IsNullOrEmpty(_pendingText) ? _partial : (_pendingText + " " + _partial).Trim();
    public event Action<string, string> OnLine; // (who, text) — "YOU" or the suspect's name
    public event Action<bool, SuspectBrain.PressureKind> OnPressureRead; // (axisMatched, kind) — lets the UI say "this isn't working" instead of a silent stall

    void Awake()
    {
        if (Vosk == null) Vosk = FindObjectOfType<VoskSpeechToText>();
        if (Vosk != null) Vosk.FreeDictation = true;
    }

    void Start()
    {
        // Reuse a service already in the scene (e.g. another suspect's) instead of always
        // spinning up a duplicate llama-server/whisper-server — same pattern as
        // CompanionConsole/GrizTestConsole. Unlike those, this console previously only
        // ever LOOKED for an existing one and never created one when none existed, which
        // left Llm/Whisper null forever in a fresh scene (the NullReferenceException this
        // fixes) — every suspect needs its own set only if none exists yet.
        if (Llm == null) Llm = FindObjectOfType<LlamaIntentService>();
        if (Llm == null)
        {
            var go = new GameObject("LlamaIntent");
            go.transform.SetParent(transform, false);
            Llm = go.AddComponent<LlamaIntentService>();
        }
        if (Whisper == null) Whisper = FindObjectOfType<WhisperTranscriber>();
        if (Whisper == null)
        {
            var go = new GameObject("Whisper");
            go.transform.SetParent(transform, false);
            Whisper = go.AddComponent<WhisperTranscriber>();
        }
        if (Voice == null && Brain != null)
        {
            var go = new GameObject($"{Brain.suspectName}Voice");
            go.transform.SetParent(transform, false);
            Voice = go.AddComponent<PiperVoice>();
        }
        if (Voice != null && Brain != null)
        {
            // Per-suspect voice: distinct Piper model + delivery per character (GDD asset
            // list: "4x distinct Piper voice profiles"). Route through englishModelContains
            // + UseLanguage so the resolver re-runs with this suspect's substring — falls
            // back gracefully to whatever voice IS installed if the named one isn't yet.
            Voice.englishModelContains = string.IsNullOrEmpty(Brain.voiceModelContains) ? "en_" : Brain.voiceModelContains;
            Voice.pitch = Brain.voicePitch;
            Voice.lengthScale = Brain.voiceLengthScale;
            Voice.UseLanguage("en");
        }
        if (AnimLink == null) AnimLink = GetComponentInChildren<GrizAnimatorLink>();
        if (AnimLink != null)
        {
            AnimLink.piper = Voice;
            AnimLink.PushPiperToLipSync(); // wires MouthLipSync/FacialExpressions to THIS suspect's voice
        }
        if (Vosk != null)
        {
            Vosk.OnTranscriptionResult += OnFinalResult;
            Vosk.OnPartialResult += OnPartial;
        }
    }

    void OnDestroy()
    {
        if (Vosk != null)
        {
            Vosk.OnTranscriptionResult -= OnFinalResult;
            Vosk.OnPartialResult -= OnPartial;
        }
    }

    bool _openingSaid;

    /// <summary>Called by CaseRunner/UI when this suspect's interrogation slot starts.</summary>
    public void BeginInterview()
    {
        Active = true;
        _history.Clear();
        _recentSuspectLines.Clear();
        _lastSuspectLine = "";
        if (Vosk != null) Vosk.StartRecordingManual();
        // The cold open: every character enters like a scene. Authored, once per case —
        // spoken the moment the voice is actually ready (Start-order safe).
        if (!_openingSaid && Brain != null && !string.IsNullOrEmpty(Brain.openingLine))
        {
            _openingSaid = true;
            StartCoroutine(SpeakOpening());
        }
    }

    IEnumerator SpeakOpening()
    {
        float deadline = Time.time + 12f;
        while (Voice == null && Time.time < deadline) yield return null;
        string line = Brain.openingLine;
        _history.Add($"{Brain.suspectName}: {line}");
        OnLine?.Invoke(Brain.suspectName, line);
        if (Voice != null && Voice.Available) Voice.Speak(line);
        if (AnimLink != null) AnimLink.SetMood("neutral"); // clean slate — no leftover mood from a prior interview
    }

    public void EndInterview()
    {
        Active = false;
        if (Vosk != null) Vosk.StopRecordingManual();
        if (Voice != null) Voice.Stop();
    }

    public void Pause() => EndInterview();
    public void Resume() => BeginInterview();

    void OnPartial(string p)
    {
        if (!Active) return;
        _partial = ExtractText(p);
        if (!string.IsNullOrEmpty(_partial))
        {
            _lastVoiceActivity = Time.time;
            if (!_utteranceOpen) { _utteranceOpen = true; Whisper?.MarkUtteranceStart(); }
        }
    }

    void OnFinalResult(string json)
    {
        if (!Active) return;
        string text = ExtractText(json);
        _partial = "";
        if (string.IsNullOrWhiteSpace(text)) { _peakVolume = 0f; return; }
        _pendingText = string.IsNullOrEmpty(_pendingText) ? text : _pendingText + " " + text;
        _pendingVolume = Mathf.Max(_pendingVolume, _peakVolume);
        _lastVoiceActivity = Time.time;
        _peakVolume = 0f;
    }

    void Update()
    {
        if (!Active) return;
        var vp = Vosk != null ? Vosk.VoiceProcessor : null;
        if (vp != null && vp.IsRecording)
        {
            _peakVolume = Mathf.Max(_peakVolume, vp.CurrentRawVolume);
            if (vp.CurrentRawVolume >= speakingVolume) _lastVoiceActivity = Time.time;
        }
        if (!string.IsNullOrEmpty(_pendingText) && Time.time - _lastVoiceActivity >= silenceToSendSeconds)
        {
            string text = _pendingText; float vol = _pendingVolume;
            _pendingText = ""; _pendingVolume = 0f;
            if (vol < minSendPeak) { _utteranceOpen = false; return; }
            StartCoroutine(RefineThenSend(text));
        }
    }

    IEnumerator RefineThenSend(string voskText)
    {
        string refined = null;
        if (Whisper != null && Whisper.IsReady && _utteranceOpen)
            yield return Whisper.EndUtteranceAndTranscribe(t => refined = t);
        _utteranceOpen = false;
        SendToSuspect(string.IsNullOrWhiteSpace(refined) ? voskText : refined);
    }

    /// <summary>Typed input path — same entrypoint voice uses, for testing/accessibility.</summary>
    public void SendTyped(string text) => SendToSuspect(text);

    // ── pressure-kind classification: cheap keyword read, same spirit as GrizBrain's
    // keyword scorer — good enough to route to the right axis, the LLM never sees this
    // logic, only its outcome via SuspectBrain.BuildFactsCage(). ──
    static SuspectBrain.PressureKind ClassifyPressure(string text, bool hasEvidence)
    {
        if (hasEvidence) return SuspectBrain.PressureKind.Evidence;
        string t = (text ?? "").ToLowerInvariant();
        if (Score(t, "sorry", "must be hard", "i understand", "take your time", "i know this is difficult", "are you okay", "how are you holding up") > 0)
            return SuspectBrain.PressureKind.Comfort;
        if (Score(t, "wonderful", "lovely", "you're so", "youre so", "impressive", "i heard you", "everyone says", "between us") > 0)
            return SuspectBrain.PressureKind.Flattery;
        if (Score(t, "tell me the truth", "you're lying", "youre lying", "i know you", "confess", "admit it", "where were you", "explain yourself") > 0)
            return SuspectBrain.PressureKind.Blunt;
        return SuspectBrain.PressureKind.Blunt; // default: generic questioning reads as blunt pressure
    }

    static int Score(string text, params string[] keys)
    {
        int s = 0;
        foreach (var k in keys) if (text.Contains(k)) s++;
        return s;
    }

    void SendToSuspect(string playerText)
    {
        if (Brain == null) return;
        playerText = (playerText ?? "").Trim();
        if (playerText.Length == 0) return;

        OnLine?.Invoke("YOU", playerText);
        _history.Add($"Player: {playerText}");
        if (_history.Count > 16) _history.RemoveAt(0);

        string evidenceId = presentedEvidenceId;
        presentedEvidenceId = null; // one use per presentation
        bool hasEvidence = !string.IsNullOrEmpty(evidenceId) &&
                           (Board == null || Board.IsEvidenceDiscovered(evidenceId));
        var kind = ClassifyPressure(playerText, hasEvidence);
        var result = Brain.ApplyPressure(kind, hasEvidence ? evidenceId : null);
        OnPressureRead?.Invoke(result.axisMatched, kind);

        if (result.endedEarly)
        {
            OnLine?.Invoke(Brain.suspectName, "(stands, straightens their collar) I think that's quite enough for today.");
            Runner?.EndInterrogation();
            return;
        }

        // Cross-suspect finger-pointing (GDD suspicion system): only fires if the player's
        // OWN words named another suspect — never volunteered unasked. A cheap "asking
        // about someone" phrase check keeps this distinct from ordinary questioning so
        // "where were you" doesn't accidentally trigger it just because a name is nearby.
        var suspicion = MentionsAskingAboutSomeone(playerText) ? Brain.FindSuspicionAbout(playerText) : null;

        _pendingRequests++;
        StartCoroutine(GenerateThenSpeak(playerText, result, suspicion));
    }

    // Drives the suspect's facial expression (FacialExpressions.SetMood via GrizAnimatorLink)
    // from what this turn's pressure result actually was, priority = dramatic weight: her
    // real secret breaking is the biggest beat, then the false give, then whether the
    // player's approach landed or not.
    static string MoodFromPressure(SuspectBrain.PressureResult result)
    {
        if (result.brokenTriggered) return "terrified"; // the mask drops — her real secret is out
        if (result.falseGiveTriggered) return "sad"; // caught out on something embarrassing, not the murder
        if (result.endedEarly) return "angry"; // patience gone, shutting the interview down
        if (result.axisMatched) return "happy"; // her real weakness landed — she's opening up, relaxed
        return "confused"; // wrong approach — she's deflecting, thrown off, not tracking with you
    }

    static bool MentionsAskingAboutSomeone(string text)
    {
        string t = (text ?? "").ToLowerInvariant();
        return Score(t, "what do you think of", "what do you think about", "tell me about", "do you trust",
                        "do you suspect", "what about", "your opinion of", "your opinion on") > 0;
    }

    string _lastSuspectLine = "";

    readonly List<string> _recentSuspectLines = new List<string>();

    IEnumerator GenerateThenSpeak(string playerText, SuspectBrain.PressureResult result, SuspectBrain.Suspicion suspicion = null)
    {
        string facts = Brain.BuildFactsCage(suspicion);
        // Only reminds her not to reuse the SAME WORDING — NOT to dodge. Answering the
        // question is the priority; the earlier "always deflect with a new tangent" wording
        // made her ignore questions and invent random nonsense, which is worse than looping.
        string antiRepeat = _recentSuspectLines.Count > 0
            ? "\n(Don't phrase your answer the same way you did in these earlier lines — say it fresh:\n- " +
              string.Join("\n- ", _recentSuspectLines) + "\n)"
            : "";

        string gen = null;
        yield return Llm.GenerateReply(string.Join("\n", _history), facts + antiRepeat, (t, ok) => { if (ok) gen = t; },
            systemPromptOverride: Brain.persona, npcName: Brain.suspectName,
            closingInstruction:
                "ACTUALLY ANSWER the question you were just asked — directly and specifically, in character. " +
                "Do NOT change the subject or bring up unrelated people/places; respond to what was asked. " +
                "The ONLY thing you guard is your one real secret (see the CONTEXT above) — everything else " +
                "you answer honestly and in colourful detail. Never invent facts that aren't in your context " +
                "or the conversation. Never contradict what you already admitted. 1-3 sentences. " +
                $"Write {Brain.suspectName}'s next line now:",
            maxTokens: 90, temperature: 0.6f); // 7B stays coherent + on-topic here; higher was making it ramble

        // Retry only if it near-repeats a recent line's WORDING — nudge for a fresh phrasing
        // of an ON-TOPIC answer, not a subject change.
        if (!string.IsNullOrWhiteSpace(gen) && RecentlyRepeated(gen))
        {
            string retry = null;
            yield return Llm.GenerateReply(string.Join("\n", _history), facts + antiRepeat, (t, ok) => { if (ok) retry = t; },
                systemPromptOverride: Brain.persona, npcName: Brain.suspectName,
                closingInstruction:
                    "You just phrased that too much like something you already said. Answer the SAME question " +
                    "again but in genuinely different words, still directly on-topic (do not change the " +
                    $"subject). Write {Brain.suspectName}'s next line now:",
                maxTokens: 90, temperature: 0.7f);
            if (!string.IsNullOrWhiteSpace(retry)) gen = retry;
        }
        _pendingRequests--;

        string line = string.IsNullOrWhiteSpace(gen)
            ? "(hesitates, loses their train of thought for a moment) ...I'm sorry, what was the question?"
            : gen.Trim();
        _lastSuspectLine = line;
        _recentSuspectLines.Add(line);
        if (_recentSuspectLines.Count > 4) _recentSuspectLines.RemoveAt(0); // remember her last few lines

        _history.Add($"{Brain.suspectName}: {line}");
        OnLine?.Invoke(Brain.suspectName, line);
        if (Voice != null && Voice.Available) Voice.Speak(line);
        Brain.CheckObjectives(line, result.falseGiveTriggered, result.brokenTriggered);
        if (AnimLink != null) AnimLink.SetMood(MoodFromPressure(result));

        if (Board != null)
        {
            int round = Runner != null ? Runner.RoundIndex : 0;
            // Board cards are SHORT, gamified one-liners — authored where possible (false
            // give / broken / opinions), LLM-compressed for ordinary statements, and only
            // carded at all when they contain an actual claim. The full text lives in the
            // transcript; the board is for scanning, not reading.
            if (result.falseGiveTriggered)
                Board.AddCard(Brain.suspectName, CardText(Brain.falseGiveCardText, Brain.falseGiveContent),
                    "false-give", round, isFalseGive: true);
            if (result.brokenTriggered)
                Board.AddCard(Brain.suspectName, CardText(Brain.secretCardText, Brain.secretContent),
                    "broken-reveal", round, isBrokenReveal: true);
            if (suspicion != null)
                Board.AddCard(Brain.suspectName, $"On {suspicion.aboutWhom}: {CardText(suspicion.beliefCard, suspicion.belief)}",
                    "accusation", round);
            if (!result.falseGiveTriggered && !result.brokenTriggered && suspicion == null)
                StartCoroutine(SummarizeToCard(line, round));
        }
    }

    static string CardText(string shortVersion, string fullVersion) =>
        string.IsNullOrWhiteSpace(shortVersion) ? fullVersion : shortVersion;

    // Ordinary statements get compressed to a one-line claim by the LLM before landing on
    // the board — and statements with no concrete claim (pleasantries, deflection) don't
    // land at all, so the board only ever holds things worth comparing.
    IEnumerator SummarizeToCard(string line, int round)
    {
        string summary = null;
        yield return Llm.GenerateReply(
            $"STATEMENT BY {Brain.suspectName}:\n{line}", "",
            (t, ok) => { if (ok) summary = t; },
            systemPromptOverride:
                "You compress interview statements into case-board cards for a checklist UI — players " +
                "SCAN these, they don't read them. Reply with ONE third-person claim of AT MOST 8 WORDS, " +
                "no filler words, headline style (e.g. \"Was home alone, reading.\" not \"She claims that " +
                "on the night in question she was at home reading a book.\"). If the statement contains " +
                "NO concrete claim (no time, place, person, or action — just pleasantries or deflection), " +
                "reply with exactly: NO CLAIM",
            npcName: "CARD",
            closingInstruction: "\nWrite the card text (8 words max, or NO CLAIM) now:",
            maxTokens: 16, temperature: 0.2f);
        if (string.IsNullOrWhiteSpace(summary)) yield break;
        summary = summary.Trim().Trim('"');
        if (summary.ToUpperInvariant().Contains("NO CLAIM")) yield break;
        foreach (var c in Board.Cards) // don't re-card the same claim twice
            if (c.suspectName == Brain.suspectName && IsNearDuplicate(summary, c.text)) yield break;
        Board.AddCard(Brain.suspectName, summary, "statement", round);
    }

    // Word-overlap near-duplicate check, ported from CompanionConsole's repetition guard.
    // True if the candidate line near-duplicates ANY of her recent lines (catches A/B/A/B
    // cycling that a last-line-only check slips past).
    bool RecentlyRepeated(string candidate)
    {
        foreach (var prev in _recentSuspectLines)
            if (IsNearDuplicate(candidate, prev)) return true;
        return false;
    }

    static bool IsNearDuplicate(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        var wordsA = new HashSet<string>(a.ToLowerInvariant().Split(' '));
        var wordsB = new HashSet<string>(b.ToLowerInvariant().Split(' '));
        if (wordsA.Count < 4 || wordsB.Count < 4) return false;
        int overlap = 0;
        foreach (var w in wordsA) if (wordsB.Contains(w)) overlap++;
        return (float)overlap / Mathf.Min(wordsA.Count, wordsB.Count) > 0.55f;
    }

    static string ExtractText(string json)
    {
        if (string.IsNullOrEmpty(json)) return "";
        var m = Regex.Match(json, "\"(?:text|partial)\"\\s*:\\s*\"([^\"]*)\"");
        return m.Success ? m.Groups[1].Value : json;
    }
}
