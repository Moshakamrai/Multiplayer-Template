using UnityEngine;

/// <summary>
/// Drop on any GameObject in the game scene.
/// Wire up VoskInstance and VoiceProcessor in the Inspector.
/// Press F9 at runtime to toggle the panel on/off.
/// Shows mic caps, actual vs target sample rate, resampling status,
/// queue depth, and live partial results — everything needed to
/// diagnose voice-input lag on other PCs.
/// </summary>
public class VoiceDebugGUI : MonoBehaviour
{
    [Header("References")]
    public VoskSpeechToText VoskInstance;
    public VoiceProcessor   VoiceProc;

    [Header("Display")]
    public KeyCode ToggleKey = KeyCode.F9;
    public bool    ShowOnStart = true;

    private bool      _visible;
    private Texture2D _bg;
    private string    _lastFinalText = "";

    // Rolling queue-depth history for a tiny sparkline
    private const int HISTORY_LEN = 60;
    private int[]  _queueHistory = new int[HISTORY_LEN];
    private int    _historyIdx;

    void Start()
    {
        _visible = ShowOnStart;
        _bg = new Texture2D(1, 1);
        _bg.SetPixel(0, 0, Color.black);
        _bg.Apply();

        if (VoskInstance != null)
            VoskInstance.OnTranscriptionResult += OnFinalResult;
    }

    void OnDestroy()
    {
        if (VoskInstance != null)
            VoskInstance.OnTranscriptionResult -= OnFinalResult;
    }

    private void OnFinalResult(string json)
    {
        int s = json.IndexOf("\"text\" : \"");
        if (s == -1) return;
        s += 10;
        int e = json.IndexOf("\"", s);
        string text = e > s ? json.Substring(s, e - s) : "";
        if (!string.IsNullOrWhiteSpace(text)) _lastFinalText = text;
    }

    void Update()
    {
        if (Input.GetKeyDown(ToggleKey)) _visible = !_visible;

        if (_visible && VoskInstance != null)
        {
            _queueHistory[_historyIdx % HISTORY_LEN] = VoskInstance.PendingFrameCount;
            _historyIdx++;
        }
    }

    void OnGUI()
    {
        if (!_visible) return;

        const float W  = 400f;
        const float X  = 10f;
        const float Y  = 10f;
        const float PAD = 10f;

        // Background
        GUI.color = new Color(0f, 0f, 0f, 0.82f);
        GUI.DrawTexture(new Rect(X, Y, W, 390f), _bg);
        GUI.color = Color.white;

        GUILayout.BeginArea(new Rect(X + PAD, Y + PAD, W - PAD * 2, 370f));

        // Title row
        GUILayout.Label("VOICE DEBUG  [F9 to hide]", Bold(13, new Color(0.3f, 0.95f, 1f)));
        GUILayout.Space(4);

        // ── Live voice input ─────────────────────────────────────
        string livePartial = VoskInstance != null ? ParsePartial(VoskInstance.LastPartial) : "";
        string displayText = !string.IsNullOrEmpty(livePartial) ? livePartial
                           : !string.IsNullOrEmpty(_lastFinalText) ? _lastFinalText
                           : "...";
        bool isActive = !string.IsNullOrEmpty(livePartial);
        Color liveCol = isActive ? new Color(0.1f, 1f, 0.4f) : new Color(0.5f, 0.5f, 0.5f);
        GUILayout.Label("HEARS:", Bold(11, new Color(0.7f, 0.7f, 0.7f)));
        GUILayout.Label(displayText, Bold(22, liveCol));
        GUILayout.Space(6);

        // ── Mic device ───────────────────────────────────────────
        if (VoiceProc != null)
        {
            string devName = VoiceProc.CurrentDeviceName;
            if (devName.Length > 38) devName = devName.Substring(0, 36) + "…";
            GUILayout.Label($"Device:  {devName}", Normal(11, Color.white));

            int minC = VoiceProc.MinDeviceCaps;
            int maxC = VoiceProc.MaxDeviceCaps;
            string capsStr = (minC == 0 && maxC == 0) ? "any" : $"{minC} – {maxC} Hz";
            GUILayout.Label($"Mic caps:  {capsStr}", Normal(11, Color.white));

            int actual = VoiceProc.ActualSampleRate;
            int target = VoiceProc.SampleRate;
            Color rateCol = VoiceProc.IsResampling ? new Color(1f, 0.55f, 0f) : new Color(0.3f, 1f, 0.3f);
            string rateLabel = VoiceProc.IsResampling
                ? $"{actual} Hz  →  resample to {target} Hz  ⚠ MISMATCH"
                : $"{actual} Hz  ✓ native";
            GUILayout.Label($"Recording: {rateLabel}", Normal(12, rateCol));

            GUILayout.Space(4);

            // Volume bar
            float vol = VoiceProc.CurrentRawVolume;
            Color volCol = vol > 0.15f ? Color.green : vol > 0.05f ? Color.yellow : new Color(0.5f, 0.5f, 0.5f);
            string volBar = BuildBar(vol, 20);
            GUILayout.Label($"Volume:  [{volBar}]  {vol:F3}", Normal(11, volCol));
        }
        else
        {
            GUILayout.Label("VoiceProcessor not assigned!", Normal(11, Color.red));
        }

        GUILayout.Space(6);

        // ── Vosk status ──────────────────────────────────────────
        if (VoskInstance != null)
        {
            GUILayout.Label($"Status:  {VoskInstance.StatusMessage}", Normal(11, Color.white));

            int qd = VoskInstance.PendingFrameCount;
            Color qdCol = qd > 20 ? Color.red : qd > 8 ? new Color(1f, 0.55f, 0f) : new Color(0.3f, 1f, 0.3f);
            string sparkline = BuildSparkline();
            GUILayout.Label($"Queue:  {qd} frames  {sparkline}", Normal(11, qdCol));

            // Lag estimate: each frame = FrameLength / SampleRate seconds
            if (VoiceProc != null && VoiceProc.SampleRate > 0 && VoiceProc.FrameLength > 0)
            {
                float frameDur = (float)VoiceProc.FrameLength / VoiceProc.SampleRate;
                float lagMs    = qd * frameDur * 1000f;
                Color lagCol   = lagMs > 300f ? Color.red : lagMs > 100f ? new Color(1f, 0.55f, 0f) : new Color(0.3f, 1f, 0.3f);
                GUILayout.Label($"Est lag:  {lagMs:F0} ms", Normal(11, lagCol));
            }

            GUILayout.Space(6);

            float ago = VoskInstance.LastPartialTime > 0f ? Time.time - VoskInstance.LastPartialTime : -1f;
            string agoStr = ago >= 0f ? $"last heard {ago:F2}s ago" : "waiting...";
            GUILayout.Label(agoStr, Normal(10, new Color(0.5f, 0.5f, 0.55f)));
        }
        else
        {
            GUILayout.Label("VoskSpeechToText not assigned!", Normal(11, Color.red));
        }

        GUILayout.Space(8);
        GUILayout.Label("If lag > 300ms and orange/red above — mic sample rate mismatch confirmed.", Normal(10, new Color(0.6f, 0.6f, 0.65f)));

        GUILayout.EndArea();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private string BuildBar(float value, int slots)
    {
        int filled = Mathf.RoundToInt(Mathf.Clamp01(value) * slots);
        return new string('|', filled) + new string(' ', slots - filled);
    }

    private string BuildSparkline()
    {
        char[] spark = new char[] { '_', '▁', '▂', '▃', '▄', '▅', '▆', '▇', '█' };
        int maxVal = 1;
        for (int i = 0; i < HISTORY_LEN; i++) if (_queueHistory[i] > maxVal) maxVal = _queueHistory[i];
        System.Text.StringBuilder sb = new System.Text.StringBuilder(HISTORY_LEN);
        for (int i = 0; i < HISTORY_LEN; i++)
        {
            int idx = (_historyIdx + i) % HISTORY_LEN;
            int level = Mathf.RoundToInt((float)_queueHistory[idx] / maxVal * (spark.Length - 1));
            sb.Append(spark[Mathf.Clamp(level, 0, spark.Length - 1)]);
        }
        return sb.ToString();
    }

    private static string ParsePartial(string json)
    {
        if (string.IsNullOrEmpty(json)) return "";
        int s = json.IndexOf("partial\" : \"");
        if (s == -1) return "";
        s += 12;
        int e = json.LastIndexOf("\"");
        return e > s ? json.Substring(s, e - s) : "";
    }

    private static GUIStyle Normal(int size, Color col)
    {
        var s = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = FontStyle.Normal, wordWrap = false };
        s.normal.textColor = col;
        return s;
    }

    private static GUIStyle Bold(int size, Color col)
    {
        var s = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = FontStyle.Bold };
        s.normal.textColor = col;
        return s;
    }
}
