using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

[RequireComponent(typeof(PlayerController))]
[RequireComponent(typeof(PlayerCombat))]
public class VoiceCommandManager : MonoBehaviour
{
    [Header("Vosk Settings")]
    public VoskSpeechToText VoskInstance;

    [Header("UI Settings")]
    public Text InputText; 
    public Text OutputText;

    [SerializeField] private string _previousPartialText = "";
    [SerializeField] private PlayerController _myController; // For Movement
    [SerializeField] private PlayerCombat _myCombat;         // For Attacks

    private PlayerEnergy _myEnergy; // Add this reference at the top of the script!

    void Start()
    {
        _myController = GetComponent<PlayerController>();
        _myCombat = GetComponent<PlayerCombat>();
        _myEnergy = GetComponent<PlayerEnergy>(); // Get the new script!

        if (!_myController.isLocalPlayer)
        {
            if (VoskInstance != null) Destroy(VoskInstance); 
            this.enabled = false; 
            return;
        }

        if (VoskInstance != null)
        {
            VoskInstance.enabled = true; 
            VoskInstance.OnPartialResult += HandlePartialResult;
            VoskInstance.OnTranscriptionResult += HandleFinalResult;
            VoskInstance.StartRecordingManual(); 
        }

        if (InputText == null) InputText = GameObject.Find("InputText")?.GetComponent<Text>();
        if (OutputText == null) OutputText = GameObject.Find("OutputText")?.GetComponent<Text>();
        if (OutputText != null) OutputText.text = "Voice Ready.";
    }

    void HandlePartialResult(string jsonResult)
    {
        // 3. CRITICAL FOR 1-PC TESTING: 
        // Ignore the microphone if this Unity window is in the background.
        // This stops the Client window from stealing the Host's voice commands!
        if (!Application.isFocused) return;

        string currentText = ParsePartialJson(jsonResult).ToLower().Trim();
        if (string.IsNullOrEmpty(currentText)) return;

        string newSegment = currentText.StartsWith(_previousPartialText) 
            ? currentText.Substring(_previousPartialText.Length).Trim() 
            : currentText;

        _previousPartialText = currentText;

        if (!string.IsNullOrEmpty(newSegment))
        {
             ProcessWords(newSegment);
             if (InputText != null) InputText.text = currentText;
        }
    }

    void HandleFinalResult(string jsonResult) => _previousPartialText = "";

    void ProcessWords(string segment)
    {
        if (_myCombat.IsHurting || _myCombat.IsDead)
        {
            LogExecution("BLOCKED (Stunned)");
            return; 
        }

        string lowerSegment = segment.ToLower().Trim();
        
        // Phrase Check: UPPERCUT (Cost 4)
        if (GetSimilarity(lowerSegment, "upper strike") > 0.6f)
        {
            if (_myEnergy.TryUseEnergy(4f))
            {
                _myCombat.VoiceAttackUppercut(); 
                LogExecution("upper strike (-4)");
            }
            else LogExecution("NO ENERGY FOR UPPERCUT!");
            
            return;
        }

        string lowerSegment2 = segment.ToLower().Trim();

        // NEW: CANCEL LOGIC
        if (GetSimilarity(lowerSegment2, "cancel") > 0.8f || lowerSegment2 == "stop")
        {
            _myCombat.RequestCancelAttack();
            LogExecution("CANCELLED");
            return;
        }

        string[] words = lowerSegment.Split(' ');
        foreach (string word in words)
        {
            bool recognized = false;
            float cost = 0f;
            string cmdName = "";
            System.Action actionToPerform = null; // Stores the command to execute

            // Check dictionary of costs and actions
            if (GetSimilarity(word, "punch") > 0.72f) { cost = 1f; cmdName = "JAB"; actionToPerform = _myCombat.VoiceAttackJab; recognized = true; }
            else if (GetSimilarity(word, "cross") > 0.72f) { cost = 2f; cmdName = "CROSS"; actionToPerform = _myCombat.VoiceAttackCross; recognized = true; }
            else if (GetSimilarity(word, "hook") > 0.72f) { cost = 3f; cmdName = "HOOK"; actionToPerform = _myCombat.VoiceAttackHook; recognized = true; }
            else if (word == "forward") { cost = 0.5f; cmdName = "FWD DASH"; actionToPerform = _myController.VoiceDashForward; recognized = true; }
            else if (word == "back") { cost = 0.5f; cmdName = "BCK DASH"; actionToPerform = _myController.VoiceDashBack; recognized = true; }
            else if (word == "left") { cost = 0.5f; cmdName = "LFT DASH"; actionToPerform = _myController.VoiceDashLeft; recognized = true; }
            else if (word == "right") { cost = 0.5f; cmdName = "RGT DASH"; actionToPerform = _myController.VoiceDashRight; recognized = true; }

            // If a valid word was spoken, try to buy it with energy
            if (recognized)
            {
                if (_myEnergy.TryUseEnergy(cost))
                {
                    actionToPerform.Invoke(); // Executes the punch or dash!
                    LogExecution($"{cmdName} (-{cost})");
                }
                else
                {
                    LogExecution($"NO ENERGY FOR {cmdName}");
                }
            }
        }
    }

    // ==========================================
    // RESTORED HELPER FUNCTIONS
    // ==========================================

    private float GetSimilarity(string source, string target)
    {
        if (source == target) return 1.0f;
        int distance = LevenshteinDistance(source, target);
        int maxLen = Mathf.Max(source.Length, target.Length);
        if (maxLen == 0) return 1.0f;
        return 1.0f - ((float)distance / maxLen);
    }

    private int LevenshteinDistance(string s, string t)
    {
        int n = s.Length;
        int m = t.Length;
        int[,] d = new int[n + 1, m + 1];
        if (n == 0) return m;
        if (m == 0) return n;
        for (int i = 0; i <= n; d[i, 0] = i++) ;
        for (int j = 0; j <= m; d[0, j] = j++) ;
        for (int i = 1; i <= n; i++) {
            for (int j = 1; j <= m; j++) {
                int cost = (t[j - 1] == s[i - 1]) ? 0 : 1;
                d[i, j] = Mathf.Min(Mathf.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
            }
        }
        return d[n, m];
    }

    private string ParsePartialJson(string json)
    {
        int start = json.IndexOf("partial\" : \"");
        if (start == -1) return "";
        start += 12; 
        int end = json.LastIndexOf("\"");
        if (end > start) return json.Substring(start, end - start);
        return "";
    }

    private void LogExecution(string cmd)
    {
        if (OutputText != null)
        {
            OutputText.text = "EXEC: " + cmd;
            OutputText.color = Color.cyan;
        }
    }
}