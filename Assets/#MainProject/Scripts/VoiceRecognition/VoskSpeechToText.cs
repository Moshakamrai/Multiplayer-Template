using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using Vosk;
using Unity.Profiling;

public class VoskSpeechToText : MonoBehaviour
{
    [Tooltip("Location of the model, relative to the Streaming Assets folder.")]
    public string ModelPath = "vosk-model-small-en-us-0.15";

    [Tooltip("The source of the microphone input.")]
    public VoiceProcessor VoiceProcessor;

    [Tooltip("The Max number of alternatives that will be processed.")]
    public int MaxAlternatives = 0; // OPTIMIZATION: Set to 0 for speed

    [Tooltip("How long should we record before restarting?")]
    public float MaxRecordLength = 5;

    [Tooltip("Should the recognizer start when the application is launched?")]
    public bool AutoStart = true;

    [Tooltip("The phrases that will be detected. If left empty, all words will be detected.")]
    public List<string> KeyPhrases = new List<string>();

    //Cached version of the Vosk Model.
    private Model _model;

    //Cached version of the Vosk recognizer.
    private VoskRecognizer _recognizer;

    private bool _recognizerReady;
    private bool _running;

    //Called when the the state of the controller changes.
    public Action<string> OnStatusUpdated;

    //Called after the user is done speaking (Final Result)
    public Action<string> OnTranscriptionResult;

    // NEW: Called immediately when a word is detected (Partial Result)
    public Action<string> OnPartialResult; 

    private string _decompressedModelPath;
    private string _grammar = "";
    private bool _isInitializing;
    private bool _didInit;

    //Thread safe queues
    private readonly ConcurrentQueue<short[]> _threadedBufferQueue = new ConcurrentQueue<short[]>();
    private readonly ConcurrentQueue<string> _threadedResultQueue = new ConcurrentQueue<string>();
    
    // NEW: Queue for partial results
    private readonly ConcurrentQueue<string> _threadedPartialQueue = new ConcurrentQueue<string>();
    private string _lastEnqueuedPartial = ""; // dedupe identical partials at the source

    // Debug-visible state (read by VoiceDebugGUI)
    public int    PendingFrameCount => _threadedBufferQueue.Count;
    public string LastPartial       { get; private set; } = "";
    public float  LastPartialTime   { get; private set; }
    public string StatusMessage     { get; private set; } = "Initializing...";

    // Drain queued mic audio immediately after a command fires so the tail
    // of the shout can't bleed through into the next input window.
    // Also resets Vosk's internal acoustic context so normal speech before
    // the next command doesn't pollute recognition accuracy.
    public void FlushAudioBuffer()
    {
        while (_threadedBufferQueue.TryDequeue(out _)) { }
        while (_threadedPartialQueue.TryDequeue(out _)) { }
        if (_recognizerReady && _recognizer != null)
            _recognizer.FinalResult(); // forces acoustic context reset
        LastPartial = "";
        _lastEnqueuedPartial = "";
    }

    void Start()
    {
        if (AutoStart)
        {
            StartVoskStt();
        }
    }

    public void StartVoskStt(List<string> keyPhrases = null, string modelPath = default, bool startMicrophone = false, int maxAlternatives = 0)
    {
        if (_isInitializing || _didInit) return;

        if (!string.IsNullOrEmpty(modelPath)) ModelPath = modelPath;
        if (keyPhrases != null) KeyPhrases = keyPhrases;

        MaxAlternatives = maxAlternatives;
        StartCoroutine(DoStartVoskStt(startMicrophone));
    }

    private IEnumerator DoStartVoskStt(bool startMicrophone)
    {
        _isInitializing = true;

#if UNITY_ANDROID && !UNITY_EDITOR
        // Ask for the mic permission FIRST — on Android the mic device often won't appear in
        // Microphone.devices until the user has granted RECORD_AUDIO, so WaitForMicrophoneInput would
        // otherwise stall forever on a fresh install.
        if (!AndroidPermissionHandler.HasMicrophonePermission())
        {
            AndroidPermissionHandler.RequestMicrophonePermission();
            float pt = 0f;
            while (!AndroidPermissionHandler.HasMicrophonePermission() && pt < 30f)
            {
                pt += Time.unscaledDeltaTime;
                yield return null;
            }
        }
#endif

        yield return WaitForMicrophoneInput();
        yield return Decompress();

        StatusMessage = "Loading Model from: " + _decompressedModelPath;
        OnStatusUpdated?.Invoke(StatusMessage);

        Vosk.Vosk.SetLogLevel(-1); // Silence all Vosk logs — they stall the main thread
        _model = new Model(_decompressedModelPath);

        StatusMessage = "Initialized";
        OnStatusUpdated?.Invoke(StatusMessage);
        VoiceProcessor.OnFrameCaptured += VoiceProcessorOnOnFrameCaptured;
        VoiceProcessor.OnRecordingStop += VoiceProcessorOnOnRecordingStop;

        _isInitializing = false;
        _didInit = true;

        // Auto-start immediately for the game
        StartRecordingManual();
    }

    public void StartRecordingManual()
    {
        if (VoiceProcessor.IsRecording || !_didInit) return;

#if UNITY_ANDROID && !UNITY_EDITOR
        // On Quest/Android the mic permission must be granted at RUNTIME. RequestUserPermission is
        // ASYNCHRONOUS — it pops the dialog and returns immediately, so the old code that requested and
        // then checked HasPermission on the SAME frame always failed (the user hadn't tapped Allow yet)
        // and bailed forever. That's why VR had no voice while PC did. Now we wait for the grant, then
        // start recording. If already granted (replays), this proceeds instantly.
        if (!AndroidPermissionHandler.HasMicrophonePermission())
        {
            StartCoroutine(RequestMicThenRecord());
            return;
        }
#endif
        BeginRecording();
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private IEnumerator RequestMicThenRecord()
    {
        AndroidPermissionHandler.RequestMicrophonePermission();
        // Poll until the user grants it (or a generous timeout). Don't give up on the first frame.
        float t = 0f;
        while (!AndroidPermissionHandler.HasMicrophonePermission() && t < 30f)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }
        if (!AndroidPermissionHandler.HasMicrophonePermission())
        {
            Debug.LogError("[VOSK] Microphone permission not granted — voice disabled.");
            yield break;
        }
        // Permission can take a moment to register the mic device — wait for it too.
        while (Microphone.devices.Length <= 0 && t < 35f) { t += Time.unscaledDeltaTime; yield return null; }
        BeginRecording();
    }
#endif

    private void BeginRecording()
    {
        if (VoiceProcessor.IsRecording) return;
        _running = true;

        // --- FASTER POLLING FIX ---
        // We force the frameSize to 256 instead of the default 512.
        // This makes the microphone feed Vosk twice as often!
        VoiceProcessor.StartRecording(16000, 64);

        StartCoroutine(ThreadedWorkCoroutine());
    }

    public void StopRecordingManual()
    {
        if (VoiceProcessor.IsRecording)
        {
            _running = false;
            VoiceProcessor.StopRecording();
        }
    }

    // Dynamic grammar: only recognize words for cards currently in the player's hand.
    // Call RebuildGrammar(handTriggers) whenever the player's equipped cards change.
    private string _baseGrammar = "";
    private List<string> _currentHandTriggers = new List<string>();

    private void UpdateGrammar()
    {
        // Start with the base grammar (all possible words + mishears).
        // This is used before the player's hand is known (menu, lobby, etc.)
        _baseGrammar = BuildGrammarFromTriggers(null);
        _grammar = _baseGrammar;
        Debug.Log("<color=cyan>VOSK GRAMMAR:</color> Base grammar loaded. Will narrow to hand cards when round starts.");
    }

    /// <summary>
    /// Rebuilds the Vosk grammar to ONLY recognize words for cards in the player's hand.
    /// Call this when cards are equipped at round start or after shop purchases.
    /// </summary>
    public void RebuildGrammar(List<string> handTriggers)
    {
        _currentHandTriggers = handTriggers ?? new List<string>();
        string newGrammar = BuildGrammarFromTriggers(_currentHandTriggers);

        // Only rebuild if the grammar actually changed
        if (newGrammar == _grammar) return;

        _grammar = newGrammar;

        // Recreate the recognizer with the new grammar
        if (_recognizerReady && _recognizer != null)
        {
            _recognizer.FinalResult(); // flush state
            _recognizer = null;
            _recognizerReady = false;
        }

        Debug.Log($"<color=cyan>VOSK GRAMMAR:</color> Narrowed to {_currentHandTriggers.Count} hand cards. Words: {_grammar}");
    }

    /// <summary>
    /// Builds a Vosk grammar JSON array from trigger names.
    /// If handTriggers is null/empty, returns the full base grammar (all 20 cards + mishears).
    /// </summary>
    private string BuildGrammarFromTriggers(List<string> handTriggers)
    {
        var words = new HashSet<string>();

        // Always include "cancel" and utility words
        words.Add("cancel");
        words.Add("clear");

        // If no hand specified, include ALL card words (full vocabulary)
        bool useAll = handTriggers == null || handTriggers.Count == 0;
        var triggersToUse = useAll ? new List<string>
        {
            "Jab", "Cross", "Hook", "Block", "Left", "Right",
            "ParryIntent", "UnbreakablePunch",
            "Grapple", "Fake", "Clutch",
            "Uppercut", "Sweep", "Focus", "Taunt",
            "Overclock", "Reverse", "Trap", "Cage", "Mirror"
        } : handTriggers;

        foreach (string t in triggersToUse)
        {
            switch (t)
            {
                case "Jab":           words.Add("punch"); words.Add("jab"); break;
                case "Cross":         words.Add("flank"); words.Add("frank"); words.Add("blank"); break;
                case "Hook":          words.Add("hook"); break;
                case "Block":         words.Add("block"); words.Add("guard"); break;
                case "Left":          words.Add("left"); break;
                case "Right":         words.Add("right"); break;
                case "ParryIntent":   words.Add("parry"); words.Add("reflect"); break;
                case "UnbreakablePunch": words.Add("crush"); words.Add("crash"); words.Add("crushing"); words.Add("crashing"); break;
                case "Grapple":       words.Add("grapple"); words.Add("grab"); words.Add("wrap"); break;
                case "Fake":         words.Add("fake"); words.Add("faint"); words.Add("paint"); break;
                case "Clutch":        words.Add("clutch"); words.Add("catch"); words.Add("crunch"); break;
                case "Uppercut":      words.Add("uppercut"); words.Add("upper"); words.Add("cutter"); break;
                case "Sweep":         words.Add("sweep"); words.Add("swipe"); words.Add("sweet"); break;
                case "Focus":         words.Add("focus"); words.Add("charge"); words.Add("power"); break;
                case "Taunt":         words.Add("taunt"); words.Add("taught"); words.Add("tall"); break;
                case "Overclock":     words.Add("overclock"); words.Add("over"); words.Add("clock"); words.Add("overload"); break;
                case "Reverse":       words.Add("reverse"); words.Add("revert"); words.Add("reflect"); break;
                case "Trap":          words.Add("trap"); words.Add("trip"); words.Add("track"); break;
                case "Cage":          words.Add("cage"); words.Add("lock"); words.Add("seal"); break;
                case "Mirror":        words.Add("mirror"); words.Add("mere"); words.Add("near"); break;
            }
        }

        // Build JSON array
        var sb = new System.Text.StringBuilder("[");
        bool first = true;
        foreach (string w in words)
        {
            if (!first) sb.Append(", ");
            sb.Append("\"").Append(w).Append("\"");
            first = false;
        }
        sb.Append("]");
        return sb.ToString();
    }

    

    private IEnumerator Decompress()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        string zipPath = Path.Combine(Application.streamingAssetsPath, ModelPath + ".zip");
        string persistentFolderPath = Path.Combine(Application.persistentDataPath, ModelPath);

        if (!Directory.Exists(persistentFolderPath))
        {
            UnityWebRequest www = UnityWebRequest.Get(zipPath);
            yield return www.SendWebRequest();
            string persistentZipPath = Path.Combine(Application.persistentDataPath, ModelPath + ".zip");
            File.WriteAllBytes(persistentZipPath, www.downloadHandler.data);
            
            using (var zip = Ionic.Zip.ZipFile.Read(persistentZipPath))
            {
                zip.ExtractAll(Application.persistentDataPath);
            }
        }
        _decompressedModelPath = persistentFolderPath;
#else
        _decompressedModelPath = Path.Combine(Application.streamingAssetsPath, ModelPath);
#endif
        yield return null;
    }

    private IEnumerator WaitForMicrophoneInput()
    {
        while (Microphone.devices.Length <= 0) yield return null;
    }

    void Update()
    {
        // Drain final results — don't let them queue up between frames
        while (_threadedResultQueue.TryDequeue(out string voiceResult))
        {
            OnTranscriptionResult?.Invoke(voiceResult);
        }

        // Drain partials. Vosk emits one partial per audio frame as a word forms
        // ("br" → "bre" → "brea" → "break") so multiple can arrive in a single
        // Unity frame. Fire all of them — HandlePartialResult dedupes by word.
        while (_threadedPartialQueue.TryDequeue(out string partialResult))
        {
            LastPartial     = partialResult;
            LastPartialTime = Time.time;
            OnPartialResult?.Invoke(partialResult);
        }
    }

    private void VoiceProcessorOnOnFrameCaptured(short[] samples)
    {
        _threadedBufferQueue.Enqueue(samples);
    }

    private void VoiceProcessorOnOnRecordingStop()
    {
        // Handle stop logic if needed
    }

    private void CreateRecognizer()
    {
        if (_model == null) return;

        if (!string.IsNullOrEmpty(_grammar))
            _recognizer = new VoskRecognizer(_model, 16000.0f, _grammar);
        else
            _recognizer = new VoskRecognizer(_model, 16000.0f);

        _recognizer.SetMaxAlternatives(0); // Speed hack
        _recognizerReady = true;
    }

    private IEnumerator ThreadedWorkCoroutine()
    {
        // Initial setup
        if (!_recognizerReady)
        {
            UpdateGrammar();
            CreateRecognizer();
        }

        float lastLatticeReset = Time.time;
        const float IDLE_RESET_INTERVAL = 2.0f;   // flush lattice every 2s of idle time
        const float IDLE_PARTIAL_THRESHOLD = 0.5f; // no partials for 0.5s = idle

        while (_running)
        {
            // Grammar was changed (RebuildGrammar called) — recreate recognizer
            if (!_recognizerReady)
            {
                CreateRecognizer();
                lastLatticeReset = Time.time;
            }

            // Drain ALL pending frames per Update instead of one.
            // Without this, a single slow frame causes audio to pile up and
            // recognition falls progressively further behind real-time.
            int processed = 0;
            while (_threadedBufferQueue.TryDequeue(out short[] voiceResult) && processed < 32)
            {
                processed++;
                if (_recognizer.AcceptWaveform(voiceResult, voiceResult.Length))
                {
                    _threadedResultQueue.Enqueue(_recognizer.Result());
                    _lastEnqueuedPartial = "";   // recognizer state reset — clear dedupe
                    lastLatticeReset = Time.time;
                }
                else
                {
                    var partial = _recognizer.PartialResult();
                    if (partial.Length > 14 && partial != _lastEnqueuedPartial)
                    {
                        _threadedPartialQueue.Enqueue(partial);
                        _lastEnqueuedPartial = partial;
                    }
                }
            }

            // Periodic idle reset: Vosk's internal lattice grows with every
            // AcceptWaveform until Result()/FinalResult() is called. Background
            // music can keep Vosk from ever hitting its silence threshold, so
            // we force a reset during idle gaps. Without this, recognition
            // gets progressively slower as the round drags on.
            if (Time.time - lastLatticeReset > IDLE_RESET_INTERVAL &&
                Time.time - LastPartialTime  > IDLE_PARTIAL_THRESHOLD)
            {
                _recognizer.FinalResult();
                _lastEnqueuedPartial = "";
                lastLatticeReset = Time.time;
            }

            yield return null;
        }
    }
}