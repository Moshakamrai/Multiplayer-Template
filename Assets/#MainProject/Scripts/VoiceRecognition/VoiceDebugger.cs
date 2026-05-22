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
    public List<string> WatchWords = new List<string>
    {
        // Basic cards
        "punch", "jab", "flank", "frank", "blank", "hook", "block", "guard",
        "cage", "page", "engage", "crush", "crash", "crushing", "crashing",
        "left", "right", "grapple", "grab", "wrap", "fake", "faint", "paint",
        "clutch", "catch", "crunch",
        // Advanced cards
        "uppercut", "upper", "cutter", "sweep", "swipe", "sweet",
        "focus", "charge", "power", "taunt", "taught", "tall",
        // Legendary cards
        "overclock", "over", "clock", "overload", "reverse", "revert", "reflect",
        "trap", "trip", "track", "mirror", "mere", "near",
        // Utility
        "cancel", "clear"
    };

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
        if (_bg == null) { _bg = new Texture2D(1, 1); _bg.SetPixel(0, 0, Color.white); _bg.Apply(); }

        float sw = Screen.width, sh = Screen.height;
        float panelW = 420f, panelH = 340f;
        float px = sw - panelW - 12f, py = 12f;

        // Background
        GUI.color = new Color(0.02f, 0.02f, 0.04f, 0.92f);
        GUI.DrawTexture(new Rect(px, py, panelW, panelH), _bg);

        // Border
        GUI.color = new Color(0.4f, 0.8f, 1f, 0.6f);
        GUI.DrawTexture(new Rect(px, py, panelW, 2f), _bg);
        GUI.DrawTexture(new Rect(px, py + panelH - 2f, panelW, 2f), _bg);
        GUI.DrawTexture(new Rect(px, py, 2f, panelH), _bg);
        GUI.DrawTexture(new Rect(px + panelW - 2f, py, 2f, panelH), _bg);

        GUI.color = Color.white;
        float mx = px + 10f, my = py + 8f, mw = panelW - 20f;

        // Title
        GUI.Label(new Rect(mx, my, mw, 26f), "VOICE DEBUGGER (Open Vocab)", Style(16, FontStyle.Bold, new Color(0.3f, 1f, 0.8f)));
        my += 28f;

        // Status
        GUI.Label(new Rect(mx, my, mw, 20f), _statusMsg, Style(11, FontStyle.Normal, new Color(0.7f, 0.7f, 0.7f)));
        my += 24f;

        // Current partial (live)
        GUI.Label(new Rect(mx, my, 70f, 20f), "LIVE:", Style(11, FontStyle.Bold, new Color(0.6f, 0.6f, 0.6f)));
        GUI.Label(new Rect(mx + 50f, my, mw - 50f, 22f),
            string.IsNullOrEmpty(_currentPartial) ? "(silence)" : _currentPartial,
            Style(13, FontStyle.Bold, Color.yellow));
        my += 26f;

        // History header
        GUI.Label(new Rect(mx, my, mw, 18f), "HISTORY (newest first):", Style(11, FontStyle.Bold, new Color(0.5f, 0.5f, 0.5f)));
        my += 20f;

        // History entries
        float entryH = 18f;
        int maxEntries = Mathf.FloorToInt((py + panelH - my - 8f) / entryH);
        for (int i = 0; i < _history.Count && i < maxEntries; i++)
        {
            string entry = _history[i];
            bool isWatch = entry.StartsWith(">>>");
            GUI.Label(new Rect(mx, my, mw, entryH), entry,
                Style(10, FontStyle.Normal, isWatch ? new Color(1f, 0.85f, 0.2f) : new Color(0.75f, 0.75f, 0.8f)));
            my += entryH;
        }
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
