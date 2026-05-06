using UnityEngine;
using UnityEngine.UI;
using Mirror; // REQUIRED for [Command]

[RequireComponent(typeof(PlayerController), typeof(PlayerCombat), typeof(PlayerEnergy))]
public class VoiceCommandManager : NetworkBehaviour
{
    public VoskSpeechToText VoskInstance;
    public Text InputText;
    public Text OutputText;
    private string _previousPartialText = "";
    private string _lastProcessedWord   = "";
    private string _pendingRetryWord    = ""; // word blocked by dead zone — retried every frame
    private float  _echoGuardUntil      = -999f; // suppresses shout echo after beat execution
    private const float EXECUTION_ECHO_GUARD = 0.6f;
    private PlayerController _myController;
    private PlayerCombat _myCombat;
    private PlayerEnergy _myEnergy;

    private CardManager _myCards; // Add this reference

    [Header("Parry Settings")]
    public float parryVolumeThreshold = 0.25f;

    void Start()
    {
        _myController = GetComponent<PlayerController>();
        _myCombat = GetComponent<PlayerCombat>();
        _myEnergy = GetComponent<PlayerEnergy>();
        _myCards = GetComponent<CardManager>();

        if (!_myController.isLocalPlayer) { if (VoskInstance != null) Destroy(VoskInstance); this.enabled = false; return; }

        if (VoskInstance != null)
        {
            VoskInstance.OnPartialResult += HandlePartialResult;
            VoskInstance.OnTranscriptionResult += HandleFinalResult;
            VoskInstance.StartRecordingManual();
        }
    }

    void Update()
    {
        if (string.IsNullOrEmpty(_pendingRetryWord)) return;
        bool consumed = ProcessWords(_pendingRetryWord);
        if (consumed)
        {
            _lastProcessedWord = _pendingRetryWord;
            _pendingRetryWord = "";
        }
    }

    void HandlePartialResult(string jsonResult)
    {
        if (!Application.isFocused) return;

        string currentText = ParsePartialJson(jsonResult).ToLower().Trim();
        if (string.IsNullOrEmpty(currentText)) return;
        
        // Update the UI instantly so it feels responsive
        if (InputText != null) InputText.text = currentText;

        string[] currentWords = currentText.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
        if (currentWords.Length == 0) return;

        // Grab the very last word Vosk is currently guessing
        string newestWord = currentWords[currentWords.Length - 1];

        // Only mark the word as processed if timing wasn't the blocker.
        // If timing blocked it, keep retrying until the window opens.
        if (newestWord != _lastProcessedWord)
        {
            bool consumed = ProcessWords(newestWord);
            if (consumed)
            {
                _lastProcessedWord = newestWord;
                _pendingRetryWord = "";
            }
            else
            {
                _pendingRetryWord = newestWord; // blocked by dead zone — retry every frame
            }
        }
    }

    void HandleFinalResult(string jsonResult)
    {
        _previousPartialText = "";
        _lastProcessedWord = "";
        _pendingRetryWord = "";
    }

    [Command]
    void CmdUseCardOnServer(string trigger)
    {
        if (_myCards != null)
            _myCards.DiscardCard(trigger);
        // Replacement draw happens automatically at next beat (ExecutePulseImpact)
    }

   

    // Returns true  = word was consumed (timing OK, slot check, or not-in-hand — don't retry)
    // Returns false = timing blocked (dead zone) — keep retrying until the window opens
    bool ProcessWords(string segment)
    {
        if (_myCombat.IsHurting || _myCombat.IsDead || _myCombat.IsStaggered) return true;
        string lowerSegment = segment.ToLower().Trim();
        bool isRhythm = RhythmRoundManager.Instance != null && RhythmRoundManager.Instance.isRoundActive;

        // Echo guard applies in ALL modes — shout tail after any executed command is silently consumed
        if (Time.time < _echoGuardUntil) return true;

        if (isRhythm)
        {
            float trackTime = RhythmRoundManager.Instance.GetCurrentTrackTime();
            float nextBeat  = RhythmRoundManager.Instance.GetNextBeatTime();
            float lastBeat  = RhythmRoundManager.Instance.lastBeatFireTime;

            // Long dead zone only while a combo chain is actively executing (prevents mid-chain input).
            // Single-move mode always gets the short dead zone — 1.5f would consume the entire input window.
            bool comboChainActive = !RhythmRoundManager.Instance.IsSingleMoveMode() && _myCombat._comboBuffer.Count > 0;
            float deadZone = comboChainActive ? 1.5f : 0.35f;

            // Post-beat dead zone — return FALSE so the word can be retried when zone ends
            if (lastBeat > 0f && trackTime - lastBeat < deadZone) return false;

            // Pre-beat shout window — return TRUE (consume but don't queue; this beat is closing)
            if (nextBeat > 0f)
            {
                float timeToNext = nextBeat - trackTime;
                if (timeToNext >= 0f && timeToNext <= 0.5f) return true;
            }
        }

        if (isRhythm)
        {
            var rmm = RhythmRoundManager.Instance;
            if (rmm.IsSingleMoveMode())
            {
                // Single-move mode: one input per beat — lock out once either slot is taken
                if (!_myCombat.HasOpenSlot(false) || !_myCombat.HasOpenSlot(true)) return true;
            }
            else if (_myCombat._comboBuffer.Count > 0) return true; // chain in progress — wait for it to fully execute
        }

        string[] words = lowerSegment.Split(' ');
        foreach (string word in words)
        {
            bool recognized = false;
            string trigger = "";
            Vector3 dashDir = Vector3.zero;

            // 1. RECOGNITION MAPPING
            if (word == "cancel" || word == "clear" || GetSimilarity(word, "cancel") > 0.72f)
            {
                _myCombat.CancelLastInput();
                LogExecution("CANCELLED");
                continue;
            }

            if (GetSimilarity(word, "punch") > 0.7f ) { trigger = "Jab"; recognized = true; }
            else if (word == "flank" || word == "blank" || word == "frank" || GetSimilarity(word, "flank") > 0.75f) { trigger = "Cross"; recognized = true; }
            else if (GetSimilarity(word, "hook") > 0.75f) { trigger = "Hook"; recognized = true; }
            else if (GetSimilarity(word, "block") > 0.75f || GetSimilarity(word, "guard") > 0.75f) { trigger = "Block"; recognized = true; }
            else if (GetSimilarity(word, "cage") > 0.70f || word == "page" || word == "engage") { trigger = "ParryIntent"; recognized = true; }
            else if (word == "crush" || word == "crash" || word == "crushing" || word == "crashing" || GetSimilarity(word, "crush") > 0.75f) { trigger = "UnbreakablePunch"; recognized = true; }
            else if (GetSimilarity(word, "left") > 0.75f) { trigger = "Left"; dashDir = Vector3.left; recognized = true; }
            else if (GetSimilarity(word, "right") > 0.75f) { trigger = "Right"; dashDir = Vector3.right; recognized = true; }
            // Combo card selection — "one/two/three/four"
            
           

            if (recognized)
            {
                bool isComboTrigger = trigger == "Combo1" || trigger == "Combo2" || trigger == "Combo3" || trigger == "Combo4";

                // 2. STRICT CARD CHECK
                if (_myCards != null && !_myCards.IsCardInHand(trigger))
                {
                    if (isComboTrigger)
                        Debug.Log($"<color=red>[COMBO DEBUG]</color> Word='{word}' → trigger='{trigger}' REJECTED — card not in hand. Hand count={_myCards.currentHandIndices.Count}. IsRhythm={isRhythm}. ComboCount={RhythmRoundManager.Instance?.currentComboCount}");
                    else
                        Debug.Log($"<color=red>REJECTED:</color> {trigger} not in hand.");
                    LogExecution("CARD NOT IN HAND");
                    continue;
                }

                // 3. COMBO CARD PATH — queue the entire attack sequence at once
                CombatCard matchedCard = _myCards?.GetCardInHand(trigger);

                if (isComboTrigger)
                    Debug.Log($"<color=cyan>[COMBO DEBUG]</color> Word='{word}' → trigger='{trigger}' | matchedCard={(matchedCard == null ? "NULL" : matchedCard.cardName)} | isCombo={matchedCard?.isCombo} | chainLen={matchedCard?.comboChainLength} | isRhythm={isRhythm} | HasOpenSlot={_myCombat.HasOpenSlot(false)} | ComboCount={RhythmRoundManager.Instance?.currentComboCount}");

                if (matchedCard != null && matchedCard.isCombo && isRhythm)
                {
                    if (_myCombat._comboBuffer.Count == 0) // only queue when buffer is fully empty
                    {
                        foreach (string atk in matchedCard.comboAttacks)
                        {
                            Vector3 dir = atk == "Left" ? Vector3.left : atk == "Right" ? Vector3.right : Vector3.zero;
                            _myCombat.QueueRhythmMove(atk, dir);
                        }
                        CmdUseCardOnServer(trigger);
                        SetEchoGuard();
                        VoskInstance.FlushAudioBuffer();
                        if (SoundManagerMain.Instance != null) SoundManagerMain.Instance.PlayCardAccepted();
                        LogExecution("COMBO QUEUED: " + trigger);
                        Debug.Log($"<color=#FFD700>[COMBO SUCCESS]</color> {trigger} queued: {string.Join(" → ", matchedCard.comboAttacks)}");
                    }
                    else
                    {
                        LogExecution("COMBO SLOTS FULL");
                        Debug.Log($"<color=orange>[COMBO DEBUG]</color> {trigger} rejected — combo buffer already full (bufferCount={_myCombat._comboBuffer.Count}, needed={RhythmRoundManager.Instance?.currentComboCount})");
                    }
                    continue;
                }

                if (isComboTrigger)
                    Debug.Log($"<color=orange>[COMBO DEBUG]</color> {trigger} fell through to normal path — matchedCard.isCombo={matchedCard?.isCombo}, isRhythm={isRhythm}. Card exists but isn't treated as combo.");

                // 4. NORMAL SINGLE-MOVE PATH
                bool isMovement = (dashDir != Vector3.zero);
                if (_myCombat.HasOpenSlot(isMovement))
                {
                    if (isRhythm)
                    {
                        _myCombat.QueueRhythmMove(trigger, dashDir);
                        CmdUseCardOnServer(trigger);
                        SetEchoGuard();
                        VoskInstance.FlushAudioBuffer();
                        if (SoundManagerMain.Instance != null) SoundManagerMain.Instance.PlayCardAccepted();
                        LogExecution("QUEUED: " + trigger);
                    }
                    else
                    {
                        if (isMovement) _myController.CmdRhythmDash(dashDir);
                        else
                        {
                            if (trigger == "ParryIntent") _myCombat.animator.Play("Parry", 0, 0f);
                            else if (trigger == "Jab")   _myCombat.VoiceAttackJab();
                            else if (trigger == "Cross") _myCombat.VoiceAttackCross();
                            else if (trigger == "Hook")  _myCombat.VoiceAttackHook();
                            else if (trigger == "Block") _myCombat.VoiceAttackBlock();
                            CmdUseCardOnServer(trigger);
                            _echoGuardUntil = Time.time + EXECUTION_ECHO_GUARD;
                            VoskInstance.FlushAudioBuffer();
                        }
                    }
                }
                else LogExecution("SLOTS FULL");
            }
        }
        return true;
    }



    private void SetEchoGuard()
    {
        var rmm = RhythmRoundManager.Instance;
        float timeToNext = Mathf.Max(0f, rmm.GetNextBeatTime() - rmm.GetCurrentTrackTime());
        _echoGuardUntil = Time.time + timeToNext + EXECUTION_ECHO_GUARD;
    }

    private float GetSimilarity(string s, string t)
    {
        if (s == t) return 1.0f;
        int d = LevenshteinDistance(s, t);
        int m = Mathf.Max(s.Length, t.Length);
        return 1.0f - ((float)d / m);
    }

    private int LevenshteinDistance(string s, string t)
    {
        int n = s.Length, m = t.Length;
        int[,] d = new int[n + 1, m + 1];
        for (int i = 0; i <= n; d[i, 0] = i++) ;
        for (int j = 0; j <= m; d[0, j] = j++) ;
        for (int i = 1; i <= n; i++)
            for (int j = 1; j <= m; j++)
                d[i, j] = Mathf.Min(Mathf.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + ((t[j - 1] == s[i - 1]) ? 0 : 1));
        return d[n, m];
    }

    private string ParsePartialJson(string j)
    {
        int s = j.IndexOf("partial\" : \"");
        if (s == -1) return "";
        s += 12; int e = j.LastIndexOf("\"");
        return (e > s) ? j.Substring(s, e - s) : "";
    }

    private void LogExecution(string c) { if (OutputText != null) OutputText.text = "EXEC: " + c; }

    public bool IsVocalSpikeDetected()
    {
        VoiceProcessor vp = GetComponent<VoiceProcessor>();
        return vp != null && vp.IsRecording && vp.CurrentRawVolume >= parryVolumeThreshold;
    }
}