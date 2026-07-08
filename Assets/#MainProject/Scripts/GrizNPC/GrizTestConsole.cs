using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

// Test harness for GrizBrain (DESIGN_COOP.md §7 prototype).
// Talk with the mic (free-vocabulary Vosk) OR type in the input box — both paths hit
// the same brain, so the negotiation can be tuned without shouting at your PC.
public class GrizTestConsole : MonoBehaviour
{
    public VoskSpeechToText Vosk;
    public GrizBrain Brain;

    [Tooltip("Show the hidden state panel (price/patience/respect/fear).")]
    public bool showDebugState = true;

    [Tooltip("Seconds of silence before Griz starts rambling unprompted.")]
    public float idleBlabberSeconds = 25f;
    float _lastExchangeTime;

    readonly List<string> _log = new List<string>();
    string _typed = "";
    string _partial = "";
    float _peakVolume;
    bool _micStarted;
    Vector2 _scroll;

    void Awake()
    {
        // Must be set before VoskSpeechToText.Start() builds the recognizer:
        // conversation needs full-vocabulary dictation, not the card-word grammar.
        if (Vosk == null) Vosk = FindObjectOfType<VoskSpeechToText>();
        if (Vosk != null) Vosk.FreeDictation = true;
    }

    void Start()
    {
        if (Brain == null) Brain = FindObjectOfType<GrizBrain>();
        if (Vosk == null) Vosk = FindObjectOfType<VoskSpeechToText>();
        if (Vosk != null)
        {
            Vosk.OnTranscriptionResult += OnFinalResult;
            Vosk.OnPartialResult += p => _partial = ExtractText(p);
            StartCoroutine(KickMicrophone());
        }
        Say("GRIZ", $"(A hulking figure looks up from a napkin covered in names.) Hm? Customer. SPEAK.");
    }

    void OnDestroy()
    {
        if (Vosk != null) Vosk.OnTranscriptionResult -= OnFinalResult;
    }

    IEnumerator KickMicrophone()
    {
        // Vosk inits async; poke the mic until it's actually recording
        for (int i = 0; i < 30 && !_micStarted; i++)
        {
            yield return new WaitForSeconds(1f);
            var vp = Vosk.VoiceProcessor;
            if (vp != null && vp.IsRecording) { _micStarted = true; break; }
            try { Vosk.StartRecordingManual(); } catch { }
        }
    }

    void Update()
    {
        var vp = Vosk != null ? Vosk.VoiceProcessor : null;
        if (vp != null && vp.IsRecording)
            _peakVolume = Mathf.Max(_peakVolume, vp.CurrentRawVolume);

        // Go quiet too long and he fills the silence himself
        if (Brain != null && !Brain.DealClosed && !Brain.KickedOut &&
            Time.time - _lastExchangeTime > idleBlabberSeconds)
        {
            Say("GRIZ", Brain.IdleMutter());
            _lastExchangeTime = Time.time;
        }
    }

    void OnFinalResult(string json)
    {
        string text = ExtractText(json);
        _partial = "";
        if (string.IsNullOrWhiteSpace(text)) { _peakVolume = 0f; return; }
        HandleUtterance(text, _peakVolume);
        _peakVolume = 0f;
    }

    void HandleUtterance(string text, float peakVolume)
    {
        _lastExchangeTime = Time.time;
        Say("YOU", $"{text}   <vol {peakVolume:0.00}>");
        var reply = Brain.Process(text, peakVolume);
        Say("GRIZ", $"{reply.line}   <{reply.intent}>");
        if (reply.dealClosed) Say("*", "── DEAL CLOSED ── (Reset to go again)");
        if (reply.kickedOut) Say("*", "── KICKED OUT ── (Reset to grovel your way back in)");
    }

    void Say(string who, string line)
    {
        _log.Add($"[{who}] {line}");
        if (_log.Count > 60) _log.RemoveAt(0);
        _scroll.y = float.MaxValue;
    }

    // Vosk final results are JSON: { "text" : "..." } — partials use "partial"
    static string ExtractText(string json)
    {
        if (string.IsNullOrEmpty(json)) return "";
        var m = Regex.Match(json, "\"(?:text|partial)\"\\s*:\\s*\"([^\"]*)\"");
        return m.Success ? m.Groups[1].Value : json;
    }

    void OnGUI()
    {
        const int W = 640;
        int H = Screen.height - 40;
        GUILayout.BeginArea(new Rect(20, 20, W, H), GUI.skin.box);

        GUILayout.Label("<b>GRIZ — negotiation prototype</b>  (talk, or type below and press Enter)",
            Rich(14));

        // status row
        var vp = Vosk != null ? Vosk.VoiceProcessor : null;
        bool mic = vp != null && vp.IsRecording;
        float vol = mic ? vp.CurrentRawVolume : 0f;
        GUILayout.Label($"Mic: {(mic ? "LIVE" : "starting… (typing works now)")}   " +
                        $"Vol: {Bar(vol)}   Peak: {_peakVolume:0.00}   " +
                        $"{(Vosk != null ? Vosk.StatusMessage : "no Vosk in scene")}", Rich(11));
        if (!string.IsNullOrEmpty(_partial))
            GUILayout.Label($"hearing: “{_partial}…”", Rich(11));

        // conversation log
        _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(H - 190));
        foreach (var line in _log)
            GUILayout.Label(line, Rich(13));
        GUILayout.EndScrollView();

        // typed input
        GUI.SetNextControlName("grizInput");
        _typed = GUILayout.TextField(_typed, GUILayout.Height(24));
        bool submit = Event.current.type == EventType.KeyUp &&
                      (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter) &&
                      GUI.GetNameOfFocusedControl() == "grizInput";
        GUILayout.BeginHorizontal();
        if ((GUILayout.Button("Send (typed)", GUILayout.Width(120)) || submit) && !string.IsNullOrWhiteSpace(_typed))
        {
            // typed input: fake a calm volume; append ! to simulate shouting
            float vol2 = _typed.EndsWith("!") ? 0.6f : 0.1f;
            HandleUtterance(_typed.TrimEnd('!'), vol2);
            _typed = "";
            GUI.FocusControl("grizInput");
        }
        if (GUILayout.Button("Reset Griz", GUILayout.Width(100)))
        {
            Brain.ResetGriz();
            _log.Clear();
            Say("GRIZ", "(He flattens a fresh napkin.) Clean slate. TALK.");
        }
        showDebugState = GUILayout.Toggle(showDebugState, "state");
        GUILayout.EndHorizontal();

        if (showDebugState && Brain != null)
            GUILayout.Label($"price {Brain.Price}  |  patience {Brain.Patience:0}  |  respect {Brain.Respect:0}  |  fear {Brain.Fear:0}" +
                            $"{(Brain.DealClosed ? "  |  DEAL" : "")}{(Brain.KickedOut ? "  |  KICKED OUT" : "")}", Rich(11));

        GUILayout.Label("tip: typed input ending in ! counts as shouting", Rich(10));
        GUILayout.EndArea();
    }

    static GUIStyle Rich(int size)
    {
        var s = new GUIStyle(GUI.skin.label) { richText = true, fontSize = size, wordWrap = true };
        return s;
    }

    static string Bar(float v)
    {
        int n = Mathf.Clamp(Mathf.RoundToInt(v * 10), 0, 10);
        return new string('█', n) + new string('░', 10 - n);
    }
}
