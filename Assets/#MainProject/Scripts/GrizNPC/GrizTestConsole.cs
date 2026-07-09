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
    public GrizVoice Voice;
    public PiperVoice Piper;
    public GrizAnimatorLink AnimLink;

    [Tooltip("F1 toggles between the full debug console and cinematic mode (corner dialog box only) — use cinematic for showcase videos.")]
    public bool cinematicMode = false;
    string _lastGrizLine = "";
    float _lastGrizLineTime = -99f;
    string _lastYouLine = "";
    float _lastYouLineTime = -99f;

    [Tooltip("Show the hidden state panel (price/patience/respect/fear).")]
    public bool showDebugState = true;

    [Tooltip("Seconds of silence before Griz starts rambling unprompted.")]
    public float idleBlabberSeconds = 25f;
    float _lastExchangeTime;

    [Tooltip("Your sentence only ENDS after this much real mic silence — keep talking and it all stays one message. Griz replies immediately once it sends.")]
    public float silenceToSendSeconds = 2f;
    [Tooltip("Mic volume above this counts as still-speaking (keeps the sentence open).")]
    public float speakingVolume = 0.06f;
    string _pendingText = "";
    float _pendingVolume;
    float _lastVoiceActivity;

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
        if (Voice == null) Voice = Brain != null ? Brain.gameObject.AddComponent<GrizVoice>() : null;
        if (Piper == null && Brain != null)
        {
            // own child GameObject so its AudioSource doesn't fight GrizVoice's
            var go = new GameObject("PiperVoice");
            go.transform.SetParent(Brain.transform, false);
            Piper = go.AddComponent<PiperVoice>();
        }
        if (AnimLink == null) AnimLink = FindObjectOfType<GrizAnimatorLink>();
        if (Vosk != null)
        {
            Vosk.OnTranscriptionResult += OnFinalResult;
            Vosk.OnPartialResult += p =>
            {
                _partial = ExtractText(p);
                if (!string.IsNullOrEmpty(_partial)) _lastVoiceActivity = Time.time;
            };
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
        {
            _peakVolume = Mathf.Max(_peakVolume, vp.CurrentRawVolume);
            if (vp.CurrentRawVolume >= speakingVolume) _lastVoiceActivity = Time.time;
        }

        // Send only after the player has been genuinely quiet for silenceToSendSeconds
        if (!string.IsNullOrEmpty(_pendingText) &&
            Time.time - _lastVoiceActivity >= silenceToSendSeconds)
            ReplyNow();

        // Go quiet too long and he fills the silence himself
        if (Brain != null && !Brain.DealClosed && !Brain.KickedOut &&
            Time.time - _lastExchangeTime > idleBlabberSeconds)
        {
            string mutter = Brain.IdleMutter();
            Say("GRIZ", mutter);
            _lastGrizLine = mutter;
            _lastGrizLineTime = Time.time;
            SpeakLine(mutter, muttering: true);
            if (AnimLink != null) AnimLink.PlayForLine(mutter, GrizBrain.Intent.Smalltalk, Brain);
            _lastExchangeTime = Time.time;
        }
    }

    void OnFinalResult(string json)
    {
        string text = ExtractText(json);
        _partial = "";
        if (string.IsNullOrWhiteSpace(text)) { _peakVolume = 0f; return; }
        HandleUtterance(text, _peakVolume, immediate: false);
        _peakVolume = 0f;
    }

    // Voice input: Vosk finals are BUFFERED while the player is still talking — the message
    // only sends after silenceToSendSeconds of real mic silence (Update decides), so a short
    // breath never ends your sentence. Griz replies immediately once it sends.
    // Typed input sends immediately.
    void HandleUtterance(string text, float peakVolume, bool immediate)
    {
        _lastExchangeTime = Time.time;
        // player spoke while Griz was mid-line → audibly cut him off and let the brain react
        bool wasSpeaking = (Voice != null && Voice.IsSpeaking) || (Piper != null && Piper.IsSpeaking);
        if (wasSpeaking)
        {
            if (Voice != null) Voice.Stop();
            if (Piper != null) Piper.Stop();
            if (Brain != null) Brain.PendingInterruption = true;
        }

        _pendingText = string.IsNullOrEmpty(_pendingText) ? text : _pendingText + " " + text;
        _pendingVolume = Mathf.Max(_pendingVolume, peakVolume);
        _lastVoiceActivity = Time.time;

        if (immediate) ReplyNow();
    }

    void ReplyNow()
    {
        string text = _pendingText;
        float vol = _pendingVolume;
        _pendingText = "";
        _pendingVolume = 0f;
        if (string.IsNullOrWhiteSpace(text)) return;

        _lastExchangeTime = Time.time;
        Say("YOU", $"{text}   <vol {vol:0.00}>");
        _lastYouLine = text;
        _lastYouLineTime = Time.time;
        var reply = Brain.Process(text, vol);
        Say("GRIZ", $"{reply.line}   <{reply.intent}>");
        _lastGrizLine = reply.line;
        _lastGrizLineTime = Time.time;
        SpeakLine(reply.line);
        if (AnimLink != null) AnimLink.PlayForLine(reply.line, reply.intent, Brain);
        if (reply.dealClosed) Say("*", "── DEAL CLOSED ── (Reset to go again)");
        if (reply.kickedOut) Say("*", "── KICKED OUT ── (Reset to grovel your way back in)");
    }

    // Piper (real TTS) when installed, procedural gibberish otherwise
    void SpeakLine(string line, bool muttering = false)
    {
        if (Piper != null && Piper.Available)
        {
            Piper.Speak(line);
        }
        else if (Voice != null)
        {
            // gibberish agitation rises as patience falls; muttering = lower and slower
            float mood = muttering ? 0.9f : 1f + (60f - (Brain != null ? Brain.Patience : 60f)) / 150f;
            Voice.Speak(line, mood);
        }
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
        // F1 toggles cinematic mode (Event-based — works with either input backend)
        var e = Event.current;
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.F1)
        {
            cinematicMode = !cinematicMode;
            e.Use();
        }
        if (cinematicMode) { DrawCinematic(); return; }

        // Fullscreen, scaled to resolution (test harness only — real game UI comes later)
        int margin = Mathf.RoundToInt(Screen.width * 0.03f);
        int W = Screen.width - margin * 2;
        int H = Screen.height - margin * 2;
        float k = Screen.height / 1080f; // font scale factor
        int fBig = Mathf.RoundToInt(34 * k);
        int fLog = Mathf.RoundToInt(26 * k);
        int fSmall = Mathf.RoundToInt(20 * k);

        GUILayout.BeginArea(new Rect(margin, margin, W, H), GUI.skin.box);

        GUILayout.Label("<b>GRIZ — negotiation prototype</b>   <color=#888888>(talk, or type below and press Enter — F1: cinematic mode)</color>",
            Rich(fBig));

        // status row
        var vp = Vosk != null ? Vosk.VoiceProcessor : null;
        bool mic = vp != null && vp.IsRecording;
        float vol = mic ? vp.CurrentRawVolume : 0f;
        string voiceMode = Piper != null && Piper.Available
            ? "<color=#66ff66>Piper TTS</color>"
            : "gibberish <color=#888888>(run Tools > Griz > Check Piper Setup for real TTS)</color>";
        GUILayout.Label($"Mic: <b>{(mic ? "<color=#66ff66>LIVE</color>" : "starting… (typing works now)")}</b>   " +
                        $"Vol: {Bar(vol)}   Peak: {_peakVolume:0.00}   Voice: {voiceMode}   " +
                        $"<color=#888888>{(Vosk != null ? Vosk.StatusMessage : "no Vosk in scene")}</color>", Rich(fSmall));
        if (!string.IsNullOrEmpty(_partial) || !string.IsNullOrEmpty(_pendingText))
        {
            string composing = (_pendingText + " " + _partial).Trim();
            float quiet = Time.time - _lastVoiceActivity;
            GUILayout.Label($"<color=#ffe066>hearing: “{composing}…”  (sends after {Mathf.Max(0f, silenceToSendSeconds - quiet):0.0}s of silence)</color>", Rich(fSmall));
        }

        // conversation log — takes all remaining vertical space
        float reserved = fBig + fSmall * 4 + 90 * k + 60;
        _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(H - reserved));
        foreach (var line in _log)
        {
            string colored = line.StartsWith("[GRIZ]") ? $"<color=#ffb366>{line}</color>"
                           : line.StartsWith("[YOU]") ? $"<color=#99ccff>{line}</color>"
                           : $"<color=#aaffaa>{line}</color>";
            GUILayout.Label(colored, Rich(fLog));
            GUILayout.Space(6 * k);
        }
        GUILayout.EndScrollView();

        // typed input
        GUI.SetNextControlName("grizInput");
        var inputStyle = new GUIStyle(GUI.skin.textField) { fontSize = fLog };
        _typed = GUILayout.TextField(_typed, inputStyle, GUILayout.Height(46 * k));
        bool submit = Event.current.type == EventType.KeyUp &&
                      (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter) &&
                      GUI.GetNameOfFocusedControl() == "grizInput";
        var btnStyle = new GUIStyle(GUI.skin.button) { fontSize = fSmall };
        GUILayout.BeginHorizontal();
        if ((GUILayout.Button("Send (typed)", btnStyle, GUILayout.Width(220 * k), GUILayout.Height(40 * k)) || submit)
            && !string.IsNullOrWhiteSpace(_typed))
        {
            // typed input: fake a calm volume; append ! to simulate shouting
            float vol2 = _typed.EndsWith("!") ? 0.6f : 0.1f;
            HandleUtterance(_typed.TrimEnd('!'), vol2, immediate: true);
            _typed = "";
            GUI.FocusControl("grizInput");
        }
        if (GUILayout.Button("Reset Griz", btnStyle, GUILayout.Width(180 * k), GUILayout.Height(40 * k)))
        {
            Brain.ResetGriz();
            _log.Clear();
            Say("GRIZ", "(He flattens a fresh napkin.) Clean slate. TALK.");
        }
        var toggleStyle = new GUIStyle(GUI.skin.toggle) { fontSize = fSmall };
        showDebugState = GUILayout.Toggle(showDebugState, " state", toggleStyle);
        GUILayout.FlexibleSpace();
        GUILayout.Label("<color=#888888>tip: typed input ending in ! counts as shouting</color>", Rich(fSmall));
        GUILayout.EndHorizontal();

        if (showDebugState && Brain != null)
            GUILayout.Label($"price <b>{Brain.Price}</b>  |  patience {Brain.Patience:0}  |  respect {Brain.Respect:0}  |  fear {Brain.Fear:0}" +
                            $"{(Brain.DealClosed ? "  |  <color=#66ff66>DEAL</color>" : "")}{(Brain.KickedOut ? "  |  <color=#ff6666>KICKED OUT</color>" : "")}", Rich(fSmall));

        GUILayout.EndArea();
    }

    // Cinematic mode: just a corner dialog box with big text + the live "hearing" hint.
    // Clean enough to film — the debug console stays one F1 away.
    void DrawCinematic()
    {
        float k = Screen.height / 1080f;
        int fName = Mathf.RoundToInt(30 * k);
        int fLine = Mathf.RoundToInt(38 * k);
        int fSmall = Mathf.RoundToInt(20 * k);

        GUI.Label(new Rect(10, 6, 500, 34), "<color=#66666688>F1 — debug console</color>", Rich(fSmall));

        // YOUR box, bottom-right: live transcription (big, yellow, with send countdown)
        // while you talk; the sent line lingers white for a few seconds after.
        bool composingNow = !string.IsNullOrEmpty(_partial) || !string.IsNullOrEmpty(_pendingText);
        string youText = null;
        if (composingNow)
        {
            float quiet = Time.time - _lastVoiceActivity;
            youText = $"<color=#ffe066>{(_pendingText + " " + _partial).Trim()}…   " +
                      $"<size={fSmall}>({Mathf.Max(0f, silenceToSendSeconds - quiet):0.0}s)</size></color>";
        }
        else if (Time.time - _lastYouLineTime < 4f && !string.IsNullOrEmpty(_lastYouLine))
        {
            youText = $"<color=#ffffff>{_lastYouLine}</color>";
        }
        if (youText != null)
        {
            float yw = Mathf.Max(480f, Screen.width * 0.36f);
            int fYou = Mathf.RoundToInt(30 * k);
            var youStyle = Rich(fYou);
            float yh = youStyle.CalcHeight(new GUIContent(youText), yw - 36 * k) + 48 * k;
            var yr = new Rect(Screen.width - yw - 24 * k, Screen.height - yh - 24 * k, yw, yh);
            GUI.Box(yr, GUIContent.none);
            GUI.Box(yr, GUIContent.none);
            GUI.Label(new Rect(yr.x + 18 * k, yr.y + 8 * k, yw - 36 * k, fSmall + 8),
                "<b><color=#99ccff>YOU</color></b>", Rich(fSmall));
            GUI.Label(new Rect(yr.x + 18 * k, yr.y + fSmall + 16 * k, yw - 36 * k, yh - fSmall - 20 * k),
                youText, youStyle);
        }

        bool speaking = (Voice != null && Voice.IsSpeaking) || (Piper != null && Piper.IsSpeaking);
        bool show = !string.IsNullOrEmpty(_lastGrizLine) &&
                    (speaking || Time.time - _lastGrizLineTime < 6f);
        if (!show) return;

        // dialog box, bottom-left corner
        float w = Mathf.Max(520f, Screen.width * 0.42f);
        var lineStyle = Rich(fLine);
        float textH = lineStyle.CalcHeight(new GUIContent(_lastGrizLine), w - 44 * k);
        float h = textH + fName + 44 * k;
        var rect = new Rect(24 * k, Screen.height - h - 24 * k, w, h);
        GUI.Box(rect, GUIContent.none);
        GUI.Box(rect, GUIContent.none); // stacked for readability over bright scenes
        GUI.Label(new Rect(rect.x + 22 * k, rect.y + 10 * k, w - 44 * k, fName + 10),
            "<b><color=#ffb366>GRIZ</color></b>", Rich(fName));
        GUI.Label(new Rect(rect.x + 22 * k, rect.y + fName + 22 * k, w - 44 * k, textH + 8),
            $"<color=#ffffff>{_lastGrizLine}</color>", lineStyle);
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
