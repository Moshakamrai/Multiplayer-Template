using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(PlayerController), typeof(PlayerCombat), typeof(PlayerEnergy))]
public class VoiceCommandManager : MonoBehaviour
{
    public VoskSpeechToText VoskInstance;
    public Text InputText; 
    public Text OutputText;
    private string _previousPartialText = "";
    private PlayerController _myController; 
    private PlayerCombat _myCombat;         
    private PlayerEnergy _myEnergy; 

    void Start()
    {
        _myController = GetComponent<PlayerController>();
        _myCombat = GetComponent<PlayerCombat>();
        _myEnergy = GetComponent<PlayerEnergy>();

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
            float cost = 0f;
            string cmdName = "";
            string trigger = "";
            Vector3 dashDir = Vector3.zero;

            // COMBAT & DEFENSE (Cost: 2)
            if (GetSimilarity(word, "punch") > 0.72f) { cost = 2f; cmdName = "JAB"; trigger = "Jab"; recognized = true; }
            else if (GetSimilarity(word, "cross") > 0.72f) { cost = 2f; cmdName = "CROSS"; trigger = "Cross"; recognized = true; }
            else if (GetSimilarity(word, "hook") > 0.72f) { cost = 2f; cmdName = "HOOK"; trigger = "Hook"; recognized = true; }
            else if (GetSimilarity(word, "block") > 0.72f || GetSimilarity(word, "guard") > 0.72f) { cost = 2f; cmdName = "BLOCK"; trigger = "Block"; recognized = true; }
            
            // MOVEMENT (Cost: 1)
            else if (GetSimilarity(word, "left") > 0.8f) { cost = 1f; cmdName = "LFT"; dashDir = Vector3.left; recognized = true; }
            else if (GetSimilarity(word, "right") > 0.8f) { cost = 1f; cmdName = "RGT"; dashDir = Vector3.right; recognized = true; }

            if (recognized)
            {
                bool isMovement = (dashDir != Vector3.zero);

                // 1. CHECK BEFORE CHARGING: Do we have an open slot for this type of move?
                if (_myCombat.HasOpenSlot(isMovement))
                {
                    // 2. NOW charge the energy
                    if (_myEnergy.TryUseEnergy(cost))
                    {
                        if (isRhythm) 
                        { 
                            _myCombat.QueueRhythmMove(trigger, dashDir); 
                            LogExecution($"{cmdName} QUEUED"); 
                        }
                        else
                        {
                            if (dashDir != Vector3.zero) _myController.CmdRhythmDash(dashDir);
                            else if (trigger == "Jab") _myCombat.VoiceAttackJab();
                            else if (trigger == "Cross") _myCombat.VoiceAttackCross();
                            else if (trigger == "Hook") _myCombat.VoiceAttackHook();
                            else if (trigger == "Block") _myCombat.VoiceAttackBlock(); 
                            LogExecution($"{cmdName} INSTANT");
                        }
                    }
                    else
                    {
                        LogExecution("NO ENERGY"); // Warns player they are gassed out
                    }
                }
                else
                {
                    LogExecution("SLOTS FULL"); // Protects energy from spam
                }
            }
        }
    }

    private float GetSimilarity(string s, string t) {
        if (s == t) return 1.0f;
        int d = LevenshteinDistance(s, t);
        int m = Mathf.Max(s.Length, t.Length);
        return 1.0f - ((float)d / m);
    }

    private int LevenshteinDistance(string s, string t) {
        int n = s.Length, m = t.Length;
        int[,] d = new int[n + 1, m + 1];
        for (int i = 0; i <= n; d[i, 0] = i++) ;
        for (int j = 0; j <= m; d[0, j] = j++) ;
        for (int i = 1; i <= n; i++)
            for (int j = 1; j <= m; j++)
                d[i, j] = Mathf.Min(Mathf.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + ((t[j - 1] == s[i - 1]) ? 0 : 1));
        return d[n, m];
    }

    private string ParsePartialJson(string j) {
        int s = j.IndexOf("partial\" : \"");
        if (s == -1) return "";
        s += 12; int e = j.LastIndexOf("\"");
        return (e > s) ? j.Substring(s, e - s) : "";
    }

    private void LogExecution(string c) { if (OutputText != null) OutputText.text = "EXEC: " + c; }
}