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
    public bool FalseGiveUsed { get; private set; }

    [Header("The real secret — the GDD's true-crime-relevant fact")]
    [TextArea(2, 8)] public string secretHint = "";     // always in prompt: what they're guarding
    [TextArea(2, 8)] public string secretContent = "";  // enters the prompt only once broken
    [Tooltip("Patience threshold for the axis-based break (Comfort/Flattery suspects). Ignored if requiresEvidence.")]
    [Range(0f, 100f)] public float breaksAtPatience = 20f;
    [Tooltip("If set, ONLY these evidence ids (see CaseBoard) can break this suspect — pressure alone never cracks them (GDD §3.4, Finch).")]
    public List<string> requiredEvidenceIds = new List<string>();
    public bool BrokenState { get; private set; }

    [Header("Broken-state voice/tone note (fed to the LLM once broken)")]
    [TextArea(2, 6)] public string brokenStateDirection = "";

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

    /// <summary>What the LLM is allowed to know right now — hint always, content only once
    /// unlocked. Same un-leakable-by-design principle as GrizBrain.Secret.</summary>
    public string BuildFactsCage()
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
        facts.AppendLine($"CURRENT PATIENCE: {patience:0}/100 (0 = you shut the interview down).");
        return facts.ToString();
    }
}
