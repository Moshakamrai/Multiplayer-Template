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

    // Debug-visible state (read from VoiceDebugGUI)
    public int    PendingFrameCount => _threadedBufferQueue.Count;
    public string LastPartial       { get; private set; } = "";
    public float  LastPartialTime   { get; private set; }
    public string StatusMessage     { get; private set; } = "Initializing...";

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
        if (!VoiceProcessor.IsRecording && _didInit)
        {
            _running = true;
            
            // --- FASTER POLLING FIX ---
            // We force the frameSize to 256 instead of the default 512. 
            // This makes the microphone feed Vosk twice as often!
            VoiceProcessor.StartRecording(16000, 256); 
            
            StartCoroutine(ThreadedWorkCoroutine());
        }
    }

    public void StopRecordingManual()
    {
        if (VoiceProcessor.IsRecording)
        {
            _running = false;
            VoiceProcessor.StopRecording();
        }
    }

    private void UpdateGrammar()
    {
        // --- THE MASSIVE SPEED HACK ---
        // Instead of relying on the Unity Inspector, we hardcode the EXACT 
        // JSON array of words your VoiceCommandManager uses.
        // Vosk will instantly stop checking its 100,000 word dictionary.
        
        // Combat words + combo-card number words (with real Vosk mishears: "tree"=three, "for"=four)
        _grammar = "[\"punch\", \"jab\", \"blast\", \"last\", \"fast\", \"cast\", \"hook\", \"block\", \"guard\", \"cage\", \"page\", \"engage\", \"boom\", \"room\", \"doom\", \"left\", \"right\", \"one\", \"two\", \"tree\", \"for\", \"[unk]\"]";

        Debug.Log("<color=cyan>VOSK GRAMMAR:</color> Locked to combat + combo-number words (one/two/tree/for).");
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
        // Fire Final Results
        if (_threadedResultQueue.TryDequeue(out string voiceResult))
        {
            OnTranscriptionResult?.Invoke(voiceResult);
        }

        // Fire Partial Results (Fast)
        if (_threadedPartialQueue.TryDequeue(out string partialResult))
        {
            LastPartial = partialResult;
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

    private IEnumerator ThreadedWorkCoroutine()
    {
        if (!_recognizerReady)
        {
            UpdateGrammar();
            
            // Initialize with Grammar if valid
            if (!string.IsNullOrEmpty(_grammar))
                _recognizer = new VoskRecognizer(_model, 16000.0f, _grammar);
            else
                _recognizer = new VoskRecognizer(_model, 16000.0f);

            _recognizer.SetMaxAlternatives(0); // Speed hack
            _recognizerReady = true;
        }

        while (_running)
        {
            // Drain ALL pending frames per Update instead of one.
            // Without this, a single slow frame causes audio to pile up and
            // recognition falls progressively further behind real-time.
            int processed = 0;
            while (_threadedBufferQueue.TryDequeue(out short[] voiceResult) && processed < 12)
            {
                processed++;
                if (_recognizer.AcceptWaveform(voiceResult, voiceResult.Length))
                {
                    _threadedResultQueue.Enqueue(_recognizer.Result());
                }
                else
                {
                    var partial = _recognizer.PartialResult();
                    if (partial.Length > 14)
                        _threadedPartialQueue.Enqueue(partial);
                }
            }
            yield return null;
        }
    }
}