using UnityEngine;

public class VoiceDebugGUI : MonoBehaviour
{
    public VoskSpeechToText VoskInstance;

    [Header("Display Settings")]
    public bool ShowDebug = false;
    public KeyCode ToggleKey = KeyCode.F2;

    private string _lastFinal   = "";
    private string _lastPartial = "";
    private string _lastMapped  = "";
    private float  _finalFadeUntil = -1f;

    private VoiceProcessor _vp;

    private static string MapWord(string word)
    {
        if (string.IsNullOrEmpty(word)) return "";
        word = word.ToLower().Trim();

        // ── 3-LAYER SYSTEM: 9 CARDS TOTAL ──
        // LOW ATTACKS (Cyan)
        if (Similarity(word, "punch") > 0.70f || word == "jab") return "→ FAST JAB (LOW)";
        if (Similarity(word, "sweep") > 0.75f || word == "swipe") return "→ HEAVY SWEEP (LOW)";
        if (Similarity(word, "drive") > 0.75f || word == "stab") return "→ DRIVE LOW";

        // MID ATTACKS (White)
        if (word == "cross" || word == "flank" || word == "blank" || Similarity(word, "cross") > 0.75f) return "→ CROSS (MID)";
        if (Similarity(word, "hook") > 0.75f && Similarity(word, "uppercut") < 0.70f) return "→ FAST HOOK (MID)";
        if (Similarity(word, "overhead") > 0.75f || word == "chop") return "→ OVERHEAD (MID)";

        // HIGH ATTACKS (Yellow)
        if (Similarity(word, "slap") > 0.75f || word == "tap") return "→ QUICK SLAP (HIGH)";
        if (Similarity(word, "spin") > 0.75f || word == "spinning") return "→ SPINNING SLASH (HIGH)";
        if (Similarity(word, "smash") > 0.75f || word == "pound") return "→ OVERHEAD SMASH (HIGH)";

        // LOW BLOCKS (Cyan)
        if (Similarity(word, "block") > 0.75f && Similarity(word, "guard") < 0.75f) return "→ CROUCH BLOCK (LOW)";
        if (Similarity(word, "counter") > 0.75f) return "→ COUNTER SWEEP (LOW)";
        if (Similarity(word, "dodge") > 0.75f || Similarity(word, "left") > 0.75f) return "→ QUICK DODGE (LOW)";

        // MID BLOCKS (White)
        if (Similarity(word, "guard") > 0.75f || word == "middle") return "→ MIDDLE GUARD (MID)";
        if (Similarity(word, "parry") > 0.75f || word == "reflect") return "→ PARRY MID";
        if (Similarity(word, "sway") > 0.75f) return "→ SWAY MID";

        // HIGH BLOCKS (Yellow)
        if (Similarity(word, "guard_high") > 0.75f || word == "high") return "→ HIGH GUARD";
        if (Similarity(word, "intercept") > 0.75f) return "→ INTERCEPT HIGH";
        if (Similarity(word, "redirect") > 0.75f || word == "bounce") return "→ REDIRECT (HIGH)";

        // UTILITY
        if (word == "cancel" || word == "clear" || Similarity(word, "cancel") > 0.72f) return "→ CANCEL";

        return "";
    }

    private static float Similarity(string s, string t)
    {
        if (s == t) return 1f;
        int n = s.Length, m = t.Length;
        if (n == 0 || m == 0) return 0f;
        int[,] d = new int[n + 1, m + 1];
        for (int i = 0; i <= n; d[i, 0] = i++) { }
        for (int j = 0; j <= m; d[0, j] = j++) { }
        for (int i = 1; i <= n; i++)
            for (int j = 1; j <= m; j++)
                d[i, j] = Mathf.Min(Mathf.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                                    d[i - 1, j - 1] + (t[j - 1] == s[i - 1] ? 0 : 1));
        return 1f - (float)d[n, m] / Mathf.Max(n, m);
    }

    void Start()
    {
        if (VoskInstance != null)
        {
            _vp = VoskInstance.VoiceProcessor;
            VoskInstance.OnPartialResult       += OnPartial;
            VoskInstance.OnTranscriptionResult += OnFinal;
        }
    }

    void OnDestroy()
    {
        if (VoskInstance != null)
        {
            VoskInstance.OnPartialResult       -= OnPartial;
            VoskInstance.OnTranscriptionResult -= OnFinal;
        }
    }

    void OnPartial(string json)
    {
        _lastPartial = ParsePartial(json).ToLower().Trim();
        string[] words = _lastPartial.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 0) _lastMapped = MapWord(words[words.Length - 1]);
    }

    void OnFinal(string json)
    {
        _lastFinal      = ParseFinal(json).ToLower().Trim();
        _finalFadeUntil = Time.time + 2f;
        _lastPartial    = "";
        _lastMapped     = "";
    }

    void Update()
    {
        if (Input.GetKeyDown(ToggleKey)) ShowDebug = !ShowDebug;
    }

    void OnGUI()
    {
        // Debug panel disabled — press F2 in Inspector (ShowDebug) to re-enable if needed
        return;
        #pragma warning disable CS0162
        // Recalculate height based on device count
        int devCount = (_vp != null && _vp.Devices != null) ? _vp.Devices.Count : 0;
        float panelW  = 300f;
        float panelH  = 230f + devCount * 22f;
        float x       = 10f;
        float y       = Screen.height / 2f - panelH / 2f;

        GUI.color = new Color(0f, 0f, 0f, 0.70f);
        GUI.DrawTexture(new Rect(x, y, panelW, panelH), Texture2D.whiteTexture);
        GUI.color = Color.white;

        GUILayout.BeginArea(new Rect(x + 8, y + 8, panelW - 16, panelH - 16));

        // Title
        GUIStyle title = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold };
        title.normal.textColor = new Color(0.4f, 1f, 1f);
        GUILayout.Label("VOSK DEBUG  [F2]", title);

        GUILayout.Space(3);

        GUIStyle small = new GUIStyle(GUI.skin.label) { fontSize = 11 };

        // Status
        small.normal.textColor = new Color(0.6f, 0.6f, 0.6f);
        string status = VoskInstance != null ? VoskInstance.StatusMessage : "No instance";
        GUILayout.Label("Status: " + status, small);

        // Pending frames
        if (VoskInstance != null)
        {
            int pending = VoskInstance.PendingFrameCount;
            small.normal.textColor = pending > 10 ? Color.red : new Color(0.6f, 0.6f, 0.6f);
            GUILayout.Label($"Pending: {pending} frames", small);
        }

        GUILayout.Space(5);

        // ── Active device info ─────────────────────────────────────────────
        if (_vp != null)
        {
            GUIStyle activeStyle = new GUIStyle(GUI.skin.label) { fontSize = 10 };
            activeStyle.normal.textColor = new Color(0.3f, 1f, 0.8f);
            string activeDevName = _vp.CurrentDeviceIndex >= 0 && _vp.CurrentDeviceIndex < _vp.Devices.Count
                ? _vp.Devices[_vp.CurrentDeviceIndex]
                : "NONE";
            GUILayout.Label($"Active: [{_vp.CurrentDeviceIndex}] {activeDevName}", activeStyle);

            GUIStyle infStyle = new GUIStyle(GUI.skin.label) { fontSize = 9 };
            infStyle.normal.textColor = new Color(0.8f, 0.8f, 0.8f);
            string recStatus = _vp.IsRecording ? "RECORDING" : "STOPPED";
            GUILayout.Label($"Status: {recStatus} | Sample: {_vp.SampleRate}Hz | Vol: {_vp.CurrentRawVolume:F3}", infStyle);

            // Visual volume bar
            float vol = _vp.CurrentRawVolume;
            Rect barBg = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(10), GUILayout.ExpandWidth(true));
            GUI.color = new Color(0.15f, 0.15f, 0.15f);
            GUI.DrawTexture(barBg, Texture2D.whiteTexture);
            Color fillColor = vol < 0.5f ? new Color(0.2f, 0.9f, 0.3f)
                            : vol < 0.8f ? new Color(1f, 0.85f, 0.1f)
                                         : new Color(1f, 0.25f, 0.1f);
            GUI.color = fillColor;
            GUI.DrawTexture(new Rect(barBg.x, barBg.y, barBg.width * Mathf.Clamp01(vol), barBg.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            if (_vp.IsClipping)
            {
                GUIStyle clipWarn = new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Bold };
                clipWarn.normal.textColor = new Color(1f, 0.2f, 0.1f);
                GUILayout.Label("!! TOO LOUD — MOVE BACK FROM MIC !!", clipWarn);
            }
        }

        GUILayout.Space(3);

        // ── Microphone device list ─────────────────────────────────────────
        GUIStyle devHeader = new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Bold };
        devHeader.normal.textColor = new Color(1f, 0.8f, 0.3f);
        GUILayout.Label("MIC DEVICES:", devHeader);

        if (_vp != null && _vp.Devices != null && _vp.Devices.Count > 0)
        {
            GUIStyle devStyle  = new GUIStyle(GUI.skin.label) { fontSize = 10, wordWrap = true };
            GUIStyle btnStyle  = new GUIStyle(GUI.skin.button) { fontSize = 10 };

            for (int i = 0; i < _vp.Devices.Count; i++)
            {
                bool isCurrent = (i == _vp.CurrentDeviceIndex);
                GUILayout.BeginHorizontal();

                // Highlight active device
                devStyle.normal.textColor = isCurrent
                    ? new Color(0.3f, 1f, 0.4f)
                    : new Color(0.7f, 0.7f, 0.7f);

                string prefix = isCurrent ? "► " : $"  [{i}] ";
                GUILayout.Label(prefix + _vp.Devices[i], devStyle);

                if (!isCurrent)
                {
                    if (GUILayout.Button("USE", btnStyle, GUILayout.Width(38)))
                        _vp.ChangeDevice(i);
                }

                GUILayout.EndHorizontal();
            }
        }
        else
        {
            small.normal.textColor = Color.red;
            GUILayout.Label("No devices found", small);
        }

        GUILayout.Space(5);

        // ── Live recognition ───────────────────────────────────────────────
        GUIStyle partialStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
        partialStyle.normal.textColor = Color.yellow;
        string displayPartial = string.IsNullOrEmpty(_lastPartial) ? "(listening...)" : _lastPartial;
        GUILayout.Label("Hearing: " + displayPartial, partialStyle);

        if (!string.IsNullOrEmpty(_lastMapped))
        {
            GUIStyle mapStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold };
            mapStyle.normal.textColor = new Color(0.3f, 1f, 0.3f);
            GUILayout.Label(_lastMapped, mapStyle);
        }

        GUILayout.Space(3);

        if (!string.IsNullOrEmpty(_lastFinal) && Time.time < _finalFadeUntil)
        {
            float alpha = Mathf.Clamp01(_finalFadeUntil - Time.time);
            GUIStyle finalStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true };
            finalStyle.normal.textColor = new Color(1f, 0.6f, 0.2f, alpha);
            GUILayout.Label("Final: " + _lastFinal, finalStyle);
        }

        GUILayout.EndArea();
    }

    private static string ParsePartial(string j)
    {
        int s = j.IndexOf("partial\" : \"");
        if (s == -1) return "";
        s += 12;
        int e = j.LastIndexOf("\"");
        return (e > s) ? j.Substring(s, e - s) : "";
    }

    private static string ParseFinal(string j)
    {
        int s = j.IndexOf("\"text\" : \"");
        if (s == -1) return "";
        s += 10;
        int e = j.LastIndexOf("\"");
        return (e > s) ? j.Substring(s, e - s) : "";
    }
}
