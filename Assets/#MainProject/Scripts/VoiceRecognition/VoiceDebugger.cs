using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Vosk;

/// <summary>
/// Drop this on a GameObject that also has a VoiceProcessor.
/// It runs Vosk in OPEN VOCABULARY mode (no grammar lock) so you can
/// see exactly what the model transcribes for any spoken word.
///
/// Scene setup:
///   1. New empty scene
///   2. New GameObject → Add VoiceProcessor + VoiceDebugger
///   3. Set ModelPath to match VoskSpeechToText.ModelPath (default already set)
///   4. Hit Play, speak — watch the screen
/// </summary>
[RequireComponent(typeof(VoiceProcessor))]
public class VoiceDebugger : MonoBehaviour
{
    [Tooltip("Relative to StreamingAssets — same value as VoskSpeechToText.ModelPath")]
    public string ModelPath = "vosk-model-small-en-us-0.15";

    [Tooltip("Words to highlight in the history log (helps spot near-misses)")]
    public List<string> WatchWords = new List<string> { "one", "two", "three", "four", "won", "to", "too", "for", "fore", "free" };

    private Model          _model;
    private VoskRecognizer _recognizer;
    private VoiceProcessor _vp;
    private bool           _running;
    private bool           _ready;
    private string         _statusMsg      = "Waiting for microphone...";

    private readonly ConcurrentQueue<short[]> _audioQueue   = new ConcurrentQueue<short[]>();
    private readonly ConcurrentQueue<string>  _partialQueue = new ConcurrentQueue<string>();
    private readonly ConcurrentQueue<string>  _finalQueue   = new ConcurrentQueue<string>();

    private string       _currentPartial = "";
    private List<string> _history        = new List<string>();
    private Texture2D    _bg;

    // ── Lifecycle ──────────────────────────────────────────────────────────
    private void Start()
    {
        _vp = GetComponent<VoiceProcessor>();
        StartCoroutine(Init());
    }

    private IEnumerator Init()
    {
        while (Microphone.devices.Length == 0) yield return null;

        _statusMsg = "Loading Vosk model (no grammar — open vocabulary)...";
        yield return null;

        string modelPath = Path.Combine(Application.streamingAssetsPath, ModelPath);
        Vosk.Vosk.SetLogLevel(-1);
        _model      = new Model(modelPath);
        _recognizer = new VoskRecognizer(_model, 16000f); // NO grammar = hears every English word
        _recognizer.SetMaxAlternatives(0);

        _vp.OnFrameCaptured += frames => _audioQueue.Enqueue(frames);
        _vp.StartRecording(16000, 256);
        _running   = true;
        _ready     = true;
        _statusMsg = "READY  —  speak any word and watch what Vosk hears";

        StartCoroutine(RecognitionLoop());
    }

    private IEnumerator RecognitionLoop()
    {
        while (_running)
        {
            int processed = 0;
            while (_audioQueue.TryDequeue(out short[] frame) && processed < 12)
            {
                processed++;
                if (_recognizer.AcceptWaveform(frame, frame.Length))
                    _finalQueue.Enqueue(_recognizer.Result());
                else
                {
                    string p = _recognizer.PartialResult();
                    if (p.Length > 14) _partialQueue.Enqueue(p);
                }
            }
            yield return null;
        }
    }

    private void Update()
    {
        if (_partialQueue.TryDequeue(out string partial))
            _currentPartial = ParsePartial(partial);

        if (_finalQueue.TryDequeue(out string final))
        {
            string text = ParseFinal(final);
            if (!string.IsNullOrEmpty(text))
            {
                bool watch = false;
                foreach (string w in WatchWords)
                    if (text.Contains(w)) { watch = true; break; }

                string prefix  = watch ? ">>> " : "    ";
                string entry   = $"{prefix}[{Time.time:F1}s]  \"{text}\"";
                _history.Insert(0, entry);
                Debug.Log($"<color={(watch ? "yellow" : "cyan")}>[VoiceDebug] Final: \"{text}\"</color>");

                if (_history.Count > 30) _history.RemoveAt(30);
            }
            _currentPartial = "";
        }
    }

    // ── GUI ───────────────────────────────────────────────────────────────
    private void OnGUI()
    {
        return; // DEBUG UI COMPLETELY DISABLED
    }

    // ── Helpers ───────────────────────────────────────────────────────────
    private static GUIStyle Style(int size, FontStyle fs, Color col)
    {
        var s = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = fs, wordWrap = true };
        s.normal.textColor = col;
        return s;
    }

    private static string ParsePartial(string json)
    {
        int s = json.IndexOf("partial\" : \"");
        if (s == -1) return "";
        s += 12;
        int e = json.LastIndexOf("\"");
        return e > s ? json.Substring(s, e - s) : "";
    }

    private static string ParseFinal(string json)
    {
        int s = json.IndexOf("\"text\" : \"");
        if (s == -1) return "";
        s += 10;
        int e = json.IndexOf("\"", s);
        return e > s ? json.Substring(s, e - s) : "";
    }

    private void OnDestroy()
    {
        _running = false;
        if (_vp != null && _vp.IsRecording) _vp.StopRecording();
    }
}
