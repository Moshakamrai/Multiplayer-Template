using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// Ensures we can always find the player controller neighbor
[RequireComponent(typeof(PlayerController))] 
public class VoiceCommandManager : MonoBehaviour
{
    [Header("Vosk Settings")]
    public VoskSpeechToText VoskInstance;

    [Header("UI Settings")]
    public Text InputText; 
    public Text OutputText;

    [Header("Logic Settings")]
    public float CommandCooldown = 0.35f; 

    [System.Serializable]
    public struct VoiceCommand
    {
        public string TriggerPhrase; 
        public UnityEvent OnRecognized; 
    }

    public List<VoiceCommand> Commands = new List<VoiceCommand>();

    private Dictionary<string, UnityEvent> _commandCache = new Dictionary<string, UnityEvent>();
    private float _lastCommandTime = 0f;
    private string _lastProcessedText = "";

    // Reference to the player script on THIS object
    private PlayerController _myPlayer;

    void Start()
    {
        _myPlayer = GetComponent<PlayerController>();

        // ----------------------------------------------------------------
        // CRITICAL FIX: If this is NOT my player, shut down voice control.
        // This prevents the "Ghost" player from listening to your mic.
        // ----------------------------------------------------------------
        if (!_myPlayer.isLocalPlayer)
        {
            if (VoskInstance != null) VoskInstance.enabled = false; // Turn off mic
            this.enabled = false; // Turn off this script
            return;
        }

        // 1. Build Cache
        foreach(var cmd in Commands)
        {
            string cleanKey = cmd.TriggerPhrase.ToLower().Trim();
            if (!_commandCache.ContainsKey(cleanKey))
                _commandCache.Add(cleanKey, cmd.OnRecognized);
        }

        // 2. Subscribe to Vosk
        if (VoskInstance != null)
        {
            VoskInstance.OnPartialResult += HandlePartialResult;
            VoskInstance.OnTranscriptionResult += HandleFinalResult;
            VoskInstance.StartRecordingManual(); 
        }

        if (OutputText != null) OutputText.text = "Listening...";
    }

    void HandlePartialResult(string jsonResult)
    {
        string text = ParsePartialJson(jsonResult);
        if (!string.IsNullOrEmpty(text)) ProcessCommand(text);
    }

    void HandleFinalResult(string jsonResult)
    {
        var result = new RecognitionResult(jsonResult);
        if (result.Phrases != null && result.Phrases.Length > 0)
            ProcessCommand(result.Phrases[0].Text);
    }

    void ProcessCommand(string rawText)
    {
        string text = rawText.ToLower().Trim();

        if (text == _lastProcessedText && Time.time - _lastCommandTime < CommandCooldown)
            return; 

        bool commandExecuted = false;

        // DIRECT CONNECTION: Use the _myPlayer reference
        if (text.Contains("forward")) 
        { 
            _myPlayer.VoiceDashForward(); 
            commandExecuted = true;
        }
        else if (text.Contains("back"))    
        { 
            _myPlayer.VoiceDashBack();    
            commandExecuted = true;
        }
        else if (text.Contains("left"))    
        { 
            _myPlayer.VoiceDashLeft();    
            commandExecuted = true;
        }
        else if (text.Contains("right"))   
        { 
            _myPlayer.VoiceDashRight();   
            commandExecuted = true;
        }

        // Fallback for attacks/other events
        if (!commandExecuted && _commandCache.TryGetValue(text, out UnityEvent action))
        {
            action.Invoke();
            commandExecuted = true;
        }

        if (commandExecuted)
        {
            if (InputText != null) InputText.text = text;
            if (OutputText != null) 
            {
                OutputText.text = "HIT: " + text;
                OutputText.color = Color.green;
            }
            _lastCommandTime = Time.time;
            _lastProcessedText = text;
        }
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
}