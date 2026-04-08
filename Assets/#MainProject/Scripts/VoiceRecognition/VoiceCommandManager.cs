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
    private PlayerController _myController;
    private PlayerCombat _myCombat;
    private PlayerEnergy _myEnergy;
    
    private CardManager _myCards; // Add this reference

    [Header("Parry Settings")]
    public float parryVolumeThreshold = 0.4f; // Adjust this in Inspector
    

    void Start()
    {
        _myController = GetComponent<PlayerController>();
        _myCombat = GetComponent<PlayerCombat>();
        _myEnergy = GetComponent<PlayerEnergy>();
        _myCards = GetComponent<CardManager>(); // Initialize the reference

        if (!_myController.isLocalPlayer) { if (VoskInstance != null) Destroy(VoskInstance); this.enabled = false; return; }

        if (VoskInstance != null)
        {
            VoskInstance.OnPartialResult += HandlePartialResult;
            VoskInstance.OnTranscriptionResult += HandleFinalResult;
            VoskInstance.StartRecordingManual();
        }
    }

    // Update HandlePartialResult in VoiceCommandManager.cs
    void HandlePartialResult(string jsonResult)
    {
        if (!Application.isFocused) return;

        string currentText = ParsePartialJson(jsonResult).ToLower().Trim();
        if (string.IsNullOrEmpty(currentText)) return;

        // Split into arrays to track word-by-word progress
        string[] currentWords = currentText.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
        string[] previousWords = _previousPartialText.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);

        // Only process if we have actually heard more words than before
        if (currentWords.Length > previousWords.Length)
        {
            string newWords = "";
            for (int i = previousWords.Length; i < currentWords.Length; i++)
            {
                newWords += currentWords[i] + " ";
            }

            _previousPartialText = currentText;

            if (!string.IsNullOrEmpty(newWords.Trim()))
            {
                ProcessWords(newWords.Trim());
                if (InputText != null) InputText.text = currentText;
            }
        }
        else if (currentWords.Length < previousWords.Length)
        {
            // Recognition reset or correction happened
            _previousPartialText = currentText;
        }
    }

    [Command]
    void CmdUseCardOnServer(string trigger) 
    { 
        if (_myCards != null) 
        {
            _myCards.DiscardCard(trigger); 
        }
    }

    void HandleFinalResult(string jsonResult) => _previousPartialText = "";

    void ProcessWords(string segment)
    {
        if (_myCombat.IsHurting || _myCombat.IsDead) return;
        string lowerSegment = segment.ToLower().Trim();
        bool isRhythm = RhythmRoundManager.Instance != null && RhythmRoundManager.Instance.isRoundActive;

        string[] words = lowerSegment.Split(' ');
        foreach (string word in words)
        {
            bool recognized = false;
            string trigger = "";
            Vector3 dashDir = Vector3.zero;

            // 1. RECOGNITION MAPPING
            if (GetSimilarity(word, "punch") > 0.72f) { trigger = "Jab"; recognized = true; }
            else if (GetSimilarity(word, "cross") > 0.7f) { trigger = "Cross"; recognized = true; }
            else if (GetSimilarity(word, "hook") > 0.7f) { trigger = "Hook"; recognized = true; }
            else if (GetSimilarity(word, "block") > 0.72f || GetSimilarity(word, "guard") > 0.72f) { trigger = "Block"; recognized = true; }
            else if (GetSimilarity(word, "cage") > 0.70f) { trigger = "ParryIntent"; recognized = true; }
            else if (GetSimilarity(word, "boom") > 0.72f) { trigger = "UnbreakablePunch"; recognized = true; }
            else if (GetSimilarity(word, "left") > 0.8f) { dashDir = Vector3.left; recognized = true; }
            else if (GetSimilarity(word, "right") > 0.8f) { dashDir = Vector3.right; recognized = true; }

            if (recognized)
            {
                bool isMovement = (dashDir != Vector3.zero);

                // --- UPDATED CARD CHECK ---
                // If it's an attack, it MUST be in your hand. 
                // If you didn't add "LEFT/RIGHT" to your 8 cards, this skips the check for movement.
                if (!isMovement && _myCards != null)
                {
                    if (!_myCards.IsCardInHand(trigger))
                    {
                        LogExecution("CARD NOT IN HAND");
                        Debug.Log($"<color=orange>REJECTED:</color> {trigger} not in hand.");
                        continue; 
                    }
                }

                if (_myCombat.HasOpenSlot(isMovement))
                {
                    if (isRhythm)
                    {
                        _myCombat.QueueRhythmMove(trigger, dashDir);
                        // Only discard the card if it was an actual card-based attack
                        if (!isMovement) CmdUseCardOnServer(trigger); 
                        LogExecution("QUEUED: " + (isMovement ? "DASH" : trigger));
                    }
                    else
                    {
                        // FIXED: Corrected the component access for non-rhythm mode
                        if (isMovement) _myController.CmdRhythmDash(dashDir);
                        else 
                        {
                            if (trigger == "ParryIntent") _myCombat.animator.Play("Parry", 0, 0f);
                            else if (trigger == "Jab") _myCombat.VoiceAttackJab();
                            else if (trigger == "Cross") _myCombat.VoiceAttackCross();
                            else if (trigger == "Hook") _myCombat.VoiceAttackHook();
                            else if (trigger == "Block") _myCombat.VoiceAttackBlock();
                            
                            CmdUseCardOnServer(trigger);
                        }
                    }
                }
                else
                {
                    LogExecution("SLOTS FULL");
                }
            }
        }
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
        // Access the actual instance of VoiceProcessor
        VoiceProcessor vp = GetComponent<VoiceProcessor>();
        if (vp != null && vp.IsRecording)
        {
            return vp.CurrentRawVolume >= parryVolumeThreshold;
        }
        return false;
    }
}