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

    private string _previousPartialText = "";
    private PlayerController _myController; // For Movement
    private PlayerCombat _myCombat;         // For Attacks

    void Start()
    {
        _myController = GetComponent<PlayerController>();
        _myCombat = GetComponent<PlayerCombat>();

        // 1. If this is a remote player's body on our screen, just turn off this script.
        // We removed VoskInstance.enabled = false so we don't accidentally mute the Host!
        if (!_myController.isLocalPlayer)
        {
            this.enabled = false; 
            return;
        }

        // Standard UI Setup
        if (InputText == null) InputText = GameObject.Find("InputText")?.GetComponent<Text>();
        if (OutputText == null) OutputText = GameObject.Find("OutputText")?.GetComponent<Text>();

        // 2. Only hook up the events if this is OUR local player
        if (VoskInstance != null)
        {
            VoskInstance.OnPartialResult += HandlePartialResult;
            VoskInstance.OnTranscriptionResult += HandleFinalResult;
            VoskInstance.StartRecordingManual(); 
        }

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
        string lowerSegment = segment.ToLower().Trim();
        
        // Phrase Check
        if (GetSimilarity(lowerSegment, "uppercut") > 0.6f)
        {
            _myCombat.VoiceAttackUppercut(); 
            LogExecution("UPPERCUT");
            return;
        }

        string[] words = lowerSegment.Split(' ');
        foreach (string word in words)
        {
            bool found = true;

            // Combat Actions (PlayerCombat.cs)
            if (GetSimilarity(word, "punch") > 0.72f) _myCombat.VoiceAttackJab();
            else if (GetSimilarity(word, "cross") > 0.72f) _myCombat.VoiceAttackCross();
            else if (GetSimilarity(word, "hook") > 0.72f) _myCombat.VoiceAttackHook();
            
            // Movement Actions (PlayerController.cs)
            else if (word == "forward") _myController.VoiceDashForward();
            else if (word == "back") _myController.VoiceDashBack();
            else if (word == "left") _myController.VoiceDashLeft();
            else if (word == "right") _myController.VoiceDashRight();
            else found = false;

            if (found) LogExecution(word.ToUpper());
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