using System;
using System.Collections.Generic;
using UnityEngine;

// GNOMES & GASLIGHT — the round loop (design doc's Phase A-D, made concrete):
//   ASSIGNMENT (pick a suspect) -> INTERROGATION (timed, private) -> HUDDLE (players only,
//   suspects "AI-deaf") -> round end: forensics drip + partial patience recovery -> repeat.
// Solo-testable: one player = one seat, still cycles suspects round to round.
public class CaseRunner : MonoBehaviour
{
    public enum Phase { Assignment, Interrogation, Huddle, Ended }

    [Header("Case data")]
    public CaseBoard board;
    public List<SuspectBrain> suspects = new List<SuspectBrain>();

    [Header("Round timing")]
    public float interrogationSeconds = 150f; // 2.5 min per GDD's 2-3 min guidance
    [Tooltip("Total rounds before the case forces an accusation (also caps evidenceDripOrder usage).")]
    public int totalRounds = 4;

    [Header("Solo/vertical-slice bootstrap")]
    [Tooltip("Phase 1 vertical slice (one suspect, no assignment UI yet): auto-begin the case " +
             "and jump straight into interrogating soloSuspect on Start.")]
    public bool autoStartSolo = false;
    public SuspectBrain soloSuspect;
    public InterrogationConsole soloConsole;
    [Tooltip("If assigned, autoStartSolo waits for this to finish (or be skipped) before beginning the case — the narrated cold open.")]
    public IntroSequence intro;

    public Phase CurrentPhase { get; private set; } = Phase.Assignment;
    // False until BeginCase() actually runs — while the intro is still playing, CurrentPhase
    // sits at its default (Assignment) even though nothing has started yet. CaseBoardUI uses
    // this to stay hidden during the cold open instead of showing a premature board.
    public bool IsCaseStarted { get; private set; }
    public int RoundIndex { get; private set; } // 0-based
    public SuspectBrain ActiveSuspect { get; private set; }
    public float PhaseTimeRemaining { get; private set; }

    public event Action<Phase> OnPhaseChanged;
    public event Action<int> OnRoundEnded; // roundIndex that just ended

    // Which suspect each "seat" (player) interrogated last round — used to block repeats,
    // per GDD "can't repeat the same suspect twice in a row" assignment rule.
    readonly Dictionary<int, SuspectBrain> _lastAssigned = new Dictionary<int, SuspectBrain>();

    void Start()
    {
        if (!autoStartSolo) return;
        if (intro != null)
        {
            intro.OnIntroComplete += StartSoloCase;
            intro.Begin();
        }
        else StartSoloCase();
    }

    void StartSoloCase()
    {
        BeginCase();
        if (soloSuspect != null)
        {
            BeginInterrogation(soloSuspect);
            soloConsole?.BeginInterview();
        }
    }

    public void BeginCase()
    {
        IsCaseStarted = true;
        RoundIndex = 0;
        board.ResetCase();
        foreach (var s in suspects) if (s != null) s.ResetSuspect();
        _lastAssigned.Clear();
        SetPhase(Phase.Assignment);
    }

    void SetPhase(Phase p)
    {
        CurrentPhase = p;
        PhaseTimeRemaining = p == Phase.Interrogation ? interrogationSeconds : 0f;
        OnPhaseChanged?.Invoke(p);
    }

    /// <summary>Call from UI: seat picks a suspect for this round. Enforces the
    /// no-repeat-from-last-round rule per seat.</summary>
    public bool TryAssign(int seatId, SuspectBrain suspect)
    {
        if (CurrentPhase != Phase.Assignment || suspect == null) return false;
        if (_lastAssigned.TryGetValue(seatId, out var last) && last == suspect) return false;
        _lastAssigned[seatId] = suspect;
        return true;
    }

    /// <summary>Solo/2P convenience: start interrogating a specific suspect directly
    /// (skips a formal multi-seat assignment UI — see GDD Phase 3's "fallback: solo/2P via
    /// taking turns").</summary>
    public void BeginInterrogation(SuspectBrain suspect)
    {
        ActiveSuspect = suspect;
        SetPhase(Phase.Interrogation);
    }

    void Update()
    {
        if (CurrentPhase == Phase.Interrogation)
        {
            PhaseTimeRemaining -= Time.deltaTime;
            if (PhaseTimeRemaining <= 0f || (ActiveSuspect != null && ActiveSuspect.BrokenState))
                EndInterrogation();
        }
    }

    public void EndInterrogation()
    {
        if (CurrentPhase != Phase.Interrogation) return;
        soloConsole?.EndInterview();
        SetPhase(Phase.Huddle);
    }

    /// <summary>Call when the team is done comparing notes — advances the round: drips
    /// this round's evidence, recovers suspects a bit, moves to the next assignment (or
    /// ends the case if out of rounds).</summary>
    public void EndHuddle()
    {
        if (CurrentPhase != Phase.Huddle) return;
        board.RevealEvidenceForRound(RoundIndex);
        foreach (var s in suspects) if (s != null) s.RoundRecover();
        OnRoundEnded?.Invoke(RoundIndex);
        RoundIndex++;
        if (RoundIndex >= totalRounds) { SetPhase(Phase.Ended); return; }
        SetPhase(Phase.Assignment);

        // Phase 1 solo bootstrap: only one suspect exists, so skip the (not-yet-built)
        // assignment UI and jump straight back into interrogating them for the next round.
        if (autoStartSolo && soloSuspect != null && !soloSuspect.BrokenState)
        {
            BeginInterrogation(soloSuspect);
            soloConsole?.BeginInterview();
        }
    }

    /// <summary>Players can call this any round instead of continuing — GDD's "any round,
    /// players can call it" accusation rule.</summary>
    public void ForceEndToAccusation()
    {
        if (CurrentPhase == Phase.Ended) return;
        SetPhase(Phase.Ended);
    }
}
