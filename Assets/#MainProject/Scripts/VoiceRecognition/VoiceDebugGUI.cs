using UnityEngine;

public class VoiceDebugGUI : MonoBehaviour
{
    public VoskSpeechToText VoskInstance;

    [Header("Display Settings")]
    public bool ShowDebug = true;
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

        // ── BASIC CARDS (8 + 3) ──
        if (Similarity(word, "punch") > 0.70f || word == "jab") return "→ JAB";
        if (word == "flank" || word == "frank" || word == "blank" || Similarity(word, "flank") > 0.75f) return "→ CROSS";
        if (Similarity(word, "hook")  > 0.75f) return "→ HOOK";
        if (Similarity(word, "block") > 0.75f || Similarity(word, "guard") > 0.75f) return "→ BLOCK";
        if (Similarity(word, "left")  > 0.75f) return "→ DODGE LEFT";
        if (Similarity(word, "right") > 0.75f) return "→ DODGE RIGHT";
        if (Similarity(word, "cage")  > 0.70f || word == "page" || word == "engage") return "→ PARRY / CAGE";
        if (word == "crush" || word == "crash" || word == "crushing" || word == "crashing" || Similarity(word, "crush") > 0.75f) return "→ BOOM";
        if (Similarity(word, "grapple") > 0.75f || word == "grab" || word == "wrap") return "→ GRAPPLE";
        if (Similarity(word, "feint") > 0.70f || word == "faint" || word == "paint") return "→ FEINT";
        if (Similarity(word, "clutch") > 0.75f || word == "catch" || word == "crunch") return "→ CLUTCH";

        // ── ADVANCED CARDS (4) ──
        if (Similarity(word, "uppercut") > 0.75f || word == "upper" || word == "cutter") return "→ UPPERCUT";
        if (Similarity(word, "sweep") > 0.75f || word == "swipe" || word == "sweet") return "→ SWEEP";
        if (Similarity(word, "focus") > 0.75f || word == "charge" || word == "power") return "→ FOCUS";
        if (Similarity(word, "taunt") > 0.75f || word == "taught" || word == "tall") return "→ TAUNT";

        // ── LEGENDARY CARDS (5) ──
        if (Similarity(word, "overclock") > 0.70f || word == "over" || word == "clock" || word == "overload") return "→ OVERCLOCK";
        if (Similarity(word, "reverse") > 0.75f || word == "revert" || word == "reflect") return "→ REVERSE";
        if (Similarity(word, "trap") > 0.75f || word == "trip" || word == "track") return "→ TRAP";
        if (Similarity(word, "mirror") > 0.75f || word == "mere" || word == "near") return "→ MIRROR";

        // ── UTILITY ──
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
        if (!ShowDebug) return;

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
