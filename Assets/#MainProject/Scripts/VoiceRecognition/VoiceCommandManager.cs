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

    void HandlePartialResult(string jsonResult)
    {
        if (!Application.isFocused) return;
        string currentText = ParsePartialJson(jsonResult).ToLower().Trim();
        if (string.IsNullOrEmpty(currentText)) return;

        string newSegment = currentText.StartsWith(_previousPartialText) 
            ? currentText.Substring(_previousPartialText.Length).Trim() : currentText;

        _previousPartialText = currentText;
        if (!string.IsNullOrEmpty(newSegment)) { ProcessWords(newSegment); if (InputText != null) InputText.text = currentText; }
    }

    void HandleFinalResult(string jsonResult) => _previousPartialText = "";

    void ProcessWords(string segment)
    {
        if (_myCombat.IsHurting || _myCombat.IsDead) return;
        string lowerSegment = segment.ToLower().Trim();
        bool isRhythm = RhythmRoundManager.Instance != null && RhythmRoundManager.Instance.isRoundActive;

        if (GetSimilarity(lowerSegment, "cancel") > 0.8f || lowerSegment == "stop") { _myCombat.RequestCancelAttack(); return; }

        string[] words = lowerSegment.Split(' ');
        foreach (string word in words)
        {
            bool recognized = false;
            float cost = 0f;
            string cmdName = "";
            string trigger = "";
            Vector3 dashDir = Vector3.zero;

            // COMBAT
            if (GetSimilarity(word, "punch") > 0.72f) { cost = 1f; cmdName = "JAB"; trigger = "Jab"; recognized = true; }
            else if (GetSimilarity(word, "cross") > 0.72f) { cost = 2f; cmdName = "CROSS"; trigger = "Cross"; recognized = true; }
            else if (GetSimilarity(word, "hook") > 0.72f) { cost = 3f; cmdName = "HOOK"; trigger = "Hook"; recognized = true; }
            
            // MOVEMENT (Similarity updated for reliability)
            else if (GetSimilarity(word, "forward") > 0.8f) { cost = 0.5f; cmdName = "FWD"; dashDir = Vector3.forward; recognized = true; }
            else if (GetSimilarity(word, "back") > 0.8f) { cost = 0.5f; cmdName = "BCK"; dashDir = Vector3.back; recognized = true; }
            else if (GetSimilarity(word, "left") > 0.8f) { cost = 0.5f; cmdName = "LFT"; dashDir = Vector3.left; recognized = true; }
            else if (GetSimilarity(word, "right") > 0.8f) { cost = 0.5f; cmdName = "RGT"; dashDir = Vector3.right; recognized = true; }

            if (recognized && _myEnergy.TryUseEnergy(cost))
            {
                if (isRhythm) { _myCombat.QueueRhythmMove(trigger, dashDir); LogExecution($"{cmdName} QUEUED"); }
                else
                {
                    if (dashDir != Vector3.zero) _myController.CmdRhythmDash(dashDir);
                    else if (trigger == "Jab") _myCombat.VoiceAttackJab();
                    else if (trigger == "Cross") _myCombat.VoiceAttackCross();
                    else if (trigger == "Hook") _myCombat.VoiceAttackHook();
                    LogExecution($"{cmdName} INSTANT");
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