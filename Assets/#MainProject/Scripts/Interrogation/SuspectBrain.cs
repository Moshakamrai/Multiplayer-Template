using System;
using System.Collections.Generic;
using UnityEngine;

// GNOMES & GASLIGHT — a suspect's negotiation-shaped state machine. Same spirit as
// GrizBrain's Secret (hint always in the prompt, content only once unlocked — un-leakable
// by design) but reshaped for interrogation: every suspect has exactly ONE pressure axis
// they're weak to (Comfort/Evidence/Flattery/None), a FALSE GIVE that pays out on generic
// pressure without being the murder, and a BROKEN state that unlocks the real fact.
//
// This is pure state — no LLM calls happen here. InterrogationConsole reads Patience/
// Mood/BrokenState/FalseGiveUsed to build the LLM's system prompt + facts-cage context
// every turn, exactly like Griz: the game owns the truth, the model only performs it.
public class SuspectBrain : MonoBehaviour
{
    public enum PressureAxis { Comfort, Evidence, Flattery, None }
    public enum PressureKind { Comfort, Evidence, Flattery, Blunt } // Blunt = generic/direct pressure

    [Header("Identity")]
    public string suspectName = "Suspect";

    [Header("Presentation — the movie feel")]
    [TextArea(2, 4)]
    [Tooltip("Authored cold-open line spoken the first time this suspect's interview begins — every character enters like a scene, not a chatbot.")]
    public string openingLine = "";
    [Tooltip("Piper voice model filename substring for THIS suspect (e.g. \"amy\", \"alba\", \"northern_english_male\"). Distinct voices per suspect — drop extra .onnx voices into StreamingAssets/piper.")]
    public string voiceModelContains = "en_";
    [Range(0.5f, 1.5f)] public float voicePitch = 1f;
    [Range(0.5f, 1.5f)] public float voiceLengthScale = 1f;
    [TextArea(8, 24)]
    [Tooltip("Full character bible for this suspect — passed as the LLM system prompt. " +
             "Persona, speech patterns, how they act under pressure, per the GDD §3 entry.")]
    public string persona = "";

    [Header("Pressure design (GDD §3/§4 — exactly ONE axis this suspect is weak to)")]
    public PressureAxis weakness = PressureAxis.Comfort;
    [Tooltip("Patience lost per turn when the WRONG axis is pushed — should feel like 'this isn't working', not punishing.")]
    public float wrongAxisCost = 3f;
    [Tooltip("Patience lost per turn under generic/blunt pressure with no specific axis read.")]
    public float bluntCost = 6f;
    [Tooltip("Patience lost (their weakness axis actually works) per successful push.")]
    public float rightAxisGain = 18f;
    [Tooltip("Evidence-weak suspects (e.g. Higgins) drop hard in ONE hit instead of gradually — set true for a single big drop when the RIGHT evidence item is presented.")]
    public bool evidenceIsOneHit = false;

    [Header("Patience")]
    [Range(0f, 100f)] public float patience = 100f; // 0 = suspect shuts the interview down
    [Tooltip("Slight recovery between rounds (GDD Phase D) — persistent pressure still carries over.")]
    public float perRoundRecovery = 15f;

    [Header("The false give — a real, embarrassing, provably true confession that is NOT the murder")]
    [TextArea(2, 6)] public string falseGiveHint = "";     // always in prompt: what this suspect will offer up under generic pressure
    [TextArea(2, 6)] public string falseGiveContent = "";  // the actual confession text/fact, revealed once
    [Range(0f, 100f)] public float falseGiveAtPatience = 60f; // fires once patience drops below this, before the real break
    [Tooltip("SHORT one-line version for the board card (\"ain't no one gonna read that much\") — the full content goes to the LLM, this goes to the players.")]
    public string falseGiveCardText = "";
    public bool FalseGiveUsed { get; private set; }

    [Header("The real secret — the GDD's true-crime-relevant fact")]
    [TextArea(2, 8)] public string secretHint = "";     // always in prompt: what they're guarding
    [TextArea(2, 8)] public string secretContent = "";  // enters the prompt only once broken
    [Tooltip("Patience threshold for the axis-based break (Comfort/Flattery suspects). Ignored if requiresEvidence.")]
    [Range(0f, 100f)] public float breaksAtPatience = 20f;
    [Tooltip("If set, ONLY these evidence ids (see CaseBoard) can break this suspect — pressure alone never cracks them (GDD §3.4, Finch).")]
    public List<string> requiredEvidenceIds = new List<string>();
    [Tooltip("SHORT one-line version of the broken-state reveal for the board card.")]
    public string secretCardText = "";
    public bool BrokenState { get; private set; }

    [Header("Broken-state voice/tone note (fed to the LLM once broken)")]
    [TextArea(2, 6)] public string brokenStateDirection = "";

    [Serializable]
    public class Suspicion
    {
        public string aboutWhom;             // "Higgins", "the Vicar"... matches another suspect's name/role
        [TextArea(2, 6)] public string belief; // what THIS suspect believes/suspects about them — may be wrong
        public string beliefCard;            // SHORT one-line version for the board card
    }

    [Header("Cross-suspect finger-pointing — genuine opinions, possibly wrong, NOT load-bearing " +
             "for the actual accusation (the real 3-fact chain stays intact per GDD SS2.4). " +
             "Volunteered only if the player asks this suspect about a specific other person.")]
    public List<Suspicion> suspicionsOfOthers = new List<Suspicion>();

    List<string> _presentedEvidenceIds = new List<string>();

    public void ResetSuspect()
    {
        patience = 100f;
        FalseGiveUsed = false;
        BrokenState = false;
        _presentedEvidenceIds.Clear();
    }

    /// <summary>Between-round recovery (GDD Phase D) — never past the false-give ceiling
    /// once used, so a suspect who's already given something up doesn't fully reset the
    /// tension; never revives a broken suspect.</summary>
    public void RoundRecover()
    {
        if (BrokenState) return;
        float ceiling = FalseGiveUsed ? Mathf.Min(falseGiveAtPatience + 10f, 100f) : 100f;
        patience = Mathf.Min(ceiling, patience + perRoundRecovery);
    }

    public struct PressureResult
    {
        public bool axisMatched;
        public bool falseGiveTriggered;
        public bool brokenTriggered;
        public bool endedEarly; // patience hit 0 with no break — suspect shuts down, GDD "isn't working" beat
    }

    /// <summary>Apply one turn's pressure. evidenceId = non-null when the player presented
    /// a specific evidence item this turn (see CaseBoard) — only meaningful for
    /// PressureAxis.Evidence suspects and for evidence-gated real secrets (Finch).</summary>
    public PressureResult ApplyPressure(PressureKind kind, string evidenceId = null)
    {
        var result = new PressureResult();
        if (BrokenState) return result; // nothing left to push on

        if (!string.IsNullOrEmpty(evidenceId) && !_presentedEvidenceIds.Contains(evidenceId))
            _presentedEvidenceIds.Add(evidenceId);

        bool axisMatch =
            (weakness == PressureAxis.Comfort && kind == PressureKind.Comfort) ||
            (weakness == PressureAxis.Evidence && kind == PressureKind.Evidence) ||
            (weakness == PressureAxis.Flattery && kind == PressureKind.Flattery);
        result.axisMatched = axisMatch;

        if (axisMatch)
        {
            if (weakness == PressureAxis.Evidence && evidenceIsOneHit)
                patience -= rightAxisGain * 2.2f; // "drops hard in one hit rather than gradually" (GDD §3.2)
            else
                patience -= rightAxisGain;
        }
        else if (kind == PressureKind.Blunt)
            patience -= bluntCost;
        else
            patience -= wrongAxisCost; // wrong axis pushed — costs time, doesn't punish narratively

        patience = Mathf.Max(0f, patience);

        // False give fires once, on the way down, before the real break — always pays out
        // SOMETHING for pressure so it never reads as question-hell (GDD §4).
        if (!FalseGiveUsed && patience <= falseGiveAtPatience && !string.IsNullOrEmpty(falseGiveContent))
        {
            FalseGiveUsed = true;
            result.falseGiveTriggered = true;
        }

        // The real break: evidence-gated suspects (Finch) NEVER break from patience alone —
        // this is the structural (not emotional) tell from GDD §3.4/§4.
        bool gatedByEvidence = requiredEvidenceIds != null && requiredEvidenceIds.Count > 0;
        bool hasRequiredEvidence = gatedByEvidence &&
            requiredEvidenceIds.TrueForAll(id => _presentedEvidenceIds.Contains(id));

        if (!BrokenState)
        {
            if (gatedByEvidence)
            {
                if (hasRequiredEvidence) { BrokenState = true; result.brokenTriggered = true; }
            }
            else if (patience <= breaksAtPatience)
            {
                BrokenState = true; result.brokenTriggered = true;
            }
        }

        if (!BrokenState && patience <= 0f) result.endedEarly = true;
        return result;
    }

    /// <summary>Case-insensitive substring match against the asked-about name — lets
    /// "what do you think of Higgins" and "tell me about the butler" both hit the same
    /// Suspicion entry if aboutWhom is authored broadly enough (e.g. "Higgins" also
    /// matches "the butler" if you write it that way in aboutWhom, or add both as
    /// separate entries pointing at the same belief).</summary>
    public Suspicion FindSuspicionAbout(string askedName)
    {
        if (string.IsNullOrWhiteSpace(askedName)) return null;
        string needle = askedName.ToLowerInvariant();
        foreach (var s in suspicionsOfOthers)
            if (!string.IsNullOrEmpty(s.aboutWhom) && needle.Contains(s.aboutWhom.ToLowerInvariant()))
                return s;
        return null;
    }

    /// <summary>What the LLM is allowed to know right now — hint always, content only once
    /// unlocked. Same un-leakable-by-design principle as GrizBrain.Secret. askedAboutSuspicion
    /// is set by InterrogationConsole when the player's question named another suspect —
    /// this is the ONLY way a suspicion ever enters the prompt (never volunteered unasked).</summary>
    public string BuildFactsCage(Suspicion askedAboutSuspicion = null)
    {
        var facts = new System.Text.StringBuilder();
        facts.AppendLine($"YOUR GUARDED SECRET (never reveal directly, only via the hint below unless BROKEN): {secretHint}");
        if (!string.IsNullOrEmpty(falseGiveHint))
            facts.AppendLine($"YOUR FALSE GIVE (a real but harmless confession you'll offer under enough generic pressure): {falseGiveHint}");
        if (FalseGiveUsed && !string.IsNullOrEmpty(falseGiveContent))
            facts.AppendLine($"YOU HAVE ALREADY GIVEN THIS UP THIS INTERVIEW — you may reference it, don't repeat it fresh: {falseGiveContent}");
        if (BrokenState)
        {
            facts.AppendLine($"YOU ARE NOW BROKEN. The truth: {secretContent}");
            if (!string.IsNullOrEmpty(brokenStateDirection))
                facts.AppendLine($"VOICE/TONE NOW: {brokenStateDirection}");
        }
        if (askedAboutSuspicion != null)
            facts.AppendLine($"THE PLAYER JUST ASKED WHAT YOU THINK OF {askedAboutSuspicion.aboutWhom.ToUpperInvariant()} — " +
                              $"share this genuine opinion of yours (you may be WRONG, this is your belief, not a proven fact): {askedAboutSuspicion.belief}");
        facts.AppendLine($"CURRENT PATIENCE: {patience:0}/100 (0 = you shut the interview down).");
        return facts.ToString();
    }
}
