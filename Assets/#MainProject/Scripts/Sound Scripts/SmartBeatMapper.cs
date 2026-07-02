using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using System.Linq;

[RequireComponent(typeof(BeatAnalysisEngine))]
public class SmartBeatMapper : MonoBehaviour
{
    public AudioSource audioSource;

    [Header("Beat-Snap Settings")]
    [Tooltip("OFF = pure manual mapping: every tap is saved at its RAW time, no snapping/quantization. " +
             "ON = snap taps to detected beats / BPM grid. Leave OFF for hand-mapping.")]
    public bool quantizeEnabled = false;
    [Tooltip("Max distance (seconds) a tap can be from a detected beat and still snap to it. Taps farther than this keep their raw time.")]
    [Range(0.03f, 0.333f)] public float maxSnapDistance = 0.12f;

    // Hard cap: a quantized beat must never be moved more than 1/3 of a second from the raw tap.
    private const float MAX_QUANTIZE_SHIFT = 1f / 3f;

    [Header("BPM Reference (fallback if analysis not ready)")]
    public float targetBPM = 100f;

    [Tooltip("1 = Full Beats, 2 = Half Beats, 4 = 16th Notes")]
    public int quantizeDivisor = 2;

    private List<float> _rawTaps        = new List<float>();
    private List<float> _quantizedBeats = new List<float>();
    private bool        _isRecording    = false;

    // Virtual screen dims — real screen on desktop, fixed 1920x1080 on Android
    // (scaled via MobileGUI) so buttons stay tappable on phones. Set in OnGUI.
    private float       _vw, _vh;
    private Matrix4x4   _guiPrev;

    // ── Visualizer ────────────────────────────────────────────────────────
    private float       _tapFlashTimer  = 0f;
    private const float FLASH_DURATION  = 0.15f;
    private float[]     _spectrumData   = new float[64];
    private Texture2D   _vizTex;

    // ── File loading ──────────────────────────────────────────────────────
    private string _filePath          = "";
    private string _loadStatus        = "";
    private bool   _loadStatusIsError = false;
    private string _pendingFilePath   = null;
    private NativeFileDialog.Pick _filePick = null; // in-flight Browse dialog (standalone)

    // ── BPM ───────────────────────────────────────────────────────────────
    private string _bpmInput     = "";
    private string _bpmStatus    = "";
    private bool   _detectingBPM = false;

    // ── Live tap feedback ─────────────────────────────────────────────────
    private float _lastTapTime   = -1f;
    private float _lastTapOffset = 0f;

    // ── Post-stop feedback ────────────────────────────────────────────────
    private float _lastRawTime     = -1f;
    private float _lastSnappedTime = -1f;

    // ── Beat analysis ─────────────────────────────────────────────────────
    private BeatAnalysisEngine _engine;
    private List<float>        _analyzedBeats  = new List<float>();
    private bool               _analysisReady  = false;
    private string             _analysisStatus = "";

    // ─────────────────────────────────────────────────────────────────────
    void Awake()
    {
        _engine = GetComponent<BeatAnalysisEngine>();
        _engine.OnAnalysisProgress += p => _analysisStatus = $"Analysing beats… {Mathf.RoundToInt(p * 100f)}%";
        _engine.OnAnalysisComplete += map =>
        {
            _analyzedBeats.Clear();
            foreach (var b in map.allBeats) _analyzedBeats.Add(b.time);
            _analyzedBeats.Sort();
            _analysisReady  = true;
            _analysisStatus = $"Ready — {_analyzedBeats.Count} beats detected  ({map.bpm:F1} BPM)";
            targetBPM       = map.bpm; // keep BPM display in sync
            Debug.Log($"[SmartBeatMapper] Beat analysis done: {_analyzedBeats.Count} snap targets, {map.bpm:F1} BPM");
        };
        _engine.OnAnalysisError += msg =>
        {
            _analysisStatus = $"Analysis failed: {msg} — using BPM grid fallback";
            _analysisReady  = false;
        };
    }

    void Update()
    {
        if (_tapFlashTimer > 0f)
            _tapFlashTimer -= Time.deltaTime;

        // A Browse dialog finished (standalone build, worker thread) — pull its result on the main thread.
        if (_filePick != null && _filePick.Done)
        {
            if (!string.IsNullOrEmpty(_filePick.Error))
            {
                _loadStatus = _filePick.Error; _loadStatusIsError = true;
            }
            else if (!string.IsNullOrEmpty(_filePick.Path))
            {
                _pendingFilePath = _filePick.Path;
            }
            _filePick = null;
        }

        if (_pendingFilePath != null)
        {
            _filePath = _pendingFilePath;
            _pendingFilePath = null;
            StartCoroutine(LoadAudioCoroutine(_filePath));
        }

        if (!_isRecording || audioSource == null || !audioSource.isPlaying) return;

        // Tap = Spacebar (works under either input backend) OR left mouse click as a fallback so it
        // taps even if the keyboard read is unavailable. Previously this was only compiled under the
        // LEGACY input manager — with the new Input System active it was stripped, so Space did nothing.
        if (TapPressed())
        {
            float t = audioSource.time;
            _rawTaps.Add(t);
            _lastTapTime   = t;
            _lastTapOffset = NearestBeatOffset(t);
            _tapFlashTimer = FLASH_DURATION;

            Debug.Log($"Raw Tap: {t:F3}s | Beat offset: {_lastTapOffset * 1000f:+0.0;-0.0}ms");
        }

        audioSource.GetSpectrumData(_spectrumData, 0, FFTWindow.BlackmanHarris);
    }

    // ── Beat-snap: snaps each tap to the nearest analyzed beat ───────────
    // Falls back to BPM grid if analysis hasn't completed yet.
    // Taps farther than maxSnapDistance from any beat keep their raw time.
    // Also hard-clamped so a beat is never moved more than MAX_QUANTIZE_SHIFT (1/3 s).
    private void SnapTapsToBeats()
    {
        _quantizedBeats.Clear();
        if (_rawTaps.Count == 0) return;

        // MANUAL MODE: quantization off → save raw taps exactly as tapped (just de-dupe + sort).
        if (!quantizeEnabled)
        {
            foreach (float raw in _rawTaps)
            {
                _lastRawTime = _lastSnappedTime = raw;
                if (!_quantizedBeats.Contains(raw)) _quantizedBeats.Add(raw);
            }
            _quantizedBeats.Sort();
            Debug.Log($"<color=cyan>RAW (no quantize):</color> {_rawTaps.Count} taps → {_quantizedBeats.Count} beats.");
            return;
        }

        int snapped = 0, fallback = 0, unanchored = 0;

        foreach (float raw in _rawTaps)
        {
            float result;

            if (_analysisReady && _analyzedBeats.Count > 0)
            {
                float nearestBeat = FindNearestBeat(raw);
                float dist        = Mathf.Abs(raw - nearestBeat);

                if (dist <= maxSnapDistance)
                {
                    result = nearestBeat;
                    snapped++;
                }
                else
                {
                    // No analyzed beat nearby — fall back to BPM grid, never raw
                    float grid = (60f / targetBPM) / quantizeDivisor;
                    result = Mathf.Round(raw / grid) * grid;
                    fallback++;
                }
            }
            else
            {
                // Analysis not ready — BPM grid only
                float grid = (60f / targetBPM) / quantizeDivisor;
                result = Mathf.Round(raw / grid) * grid;
                fallback++;
            }

            // Hard clamp: never move a tap more than 1/3 s from its raw time. If either snap mode
            // would push it farther, keep the raw tap instead.
            if (Mathf.Abs(raw - result) > MAX_QUANTIZE_SHIFT)
            {
                result = raw;
                unanchored++;
            }

            _lastRawTime     = raw;
            _lastSnappedTime = result;

            if (!_quantizedBeats.Contains(result))
                _quantizedBeats.Add(result);
        }

        _quantizedBeats.Sort();
        Debug.Log($"<color=cyan>SNAP:</color> {_rawTaps.Count} taps → {_quantizedBeats.Count} beats " +
                  $"(beat-snapped: {snapped}, unanchored: {unanchored}, grid-fallback: {fallback})");
    }

    private float FindNearestBeat(float tapTime)
    {
        float nearest = _analyzedBeats[0];
        float bestDist = Mathf.Abs(tapTime - nearest);
        foreach (float b in _analyzedBeats)
        {
            float d = Mathf.Abs(tapTime - b);
            if (d < bestDist) { bestDist = d; nearest = b; }
            if (b > tapTime + maxSnapDistance) break; // list is sorted, can early-exit
        }
        return nearest;
    }

    // Returns signed offset from wherever this tap will actually snap to (respects the 1/3 s cap).
    private float NearestBeatOffset(float tapTime)
    {
        if (!quantizeEnabled) return 0f; // manual mode — no snapping, so no offset

        if (_analysisReady && _analyzedBeats.Count > 0)
        {
            float nearest = FindNearestBeat(tapTime);
            float off = tapTime - nearest;
            if (Mathf.Abs(off) <= maxSnapDistance && Mathf.Abs(off) <= MAX_QUANTIZE_SHIFT)
                return off;
        }

        float snapGrid = (60f / targetBPM) / quantizeDivisor;
        float gridOff = tapTime - Mathf.Round(tapTime / snapGrid) * snapGrid;
        return Mathf.Abs(gridOff) <= MAX_QUANTIZE_SHIFT ? gridOff : 0f;
    }

    // ─────────────────────────────────────────────────────────────────────
    private void OnGUI()
    {
        if (RhythmRoundManager.Instance != null && RhythmRoundManager.Instance.isShopPhase) return;
        if (_vizTex == null)
        {
            _vizTex = new Texture2D(1, 1);
            _vizTex.SetPixel(0, 0, Color.white);
            _vizTex.Apply();
        }

        // Scale the whole mapper UI for phones (no-op on desktop).
        _guiPrev = MobileGUI.Begin(out _vw, out _vh);

        // The LOAD panel (Browse/Load + BPM) and Back button ALWAYS draw — otherwise the whole screen
        // is blank before a song is loaded and you can't even reach the Browse button.
        DrawLoadPanel();
        DrawBackButton();

        // Audio-dependent panels only draw once a clip is loaded.
        bool hasClip = audioSource != null && audioSource.clip != null;
        if (hasClip)
        {
            DrawVisualizer();
            DrawInfoPanel();
            DrawControlPanel();
            DrawHowToPanel();
        }
        else
        {
            GUI.Label(new Rect(_vw / 2f - 220f, _vh / 2f - 20f, 440f, 40f),
                "Load a song (Browse → Load) to start mapping.",
                Style(18, FontStyle.Bold, Color.yellow, TextAnchor.MiddleCenter));
        }

        MobileGUI.End(_guiPrev);
    }

    // ── Control panel (center-top) ────────────────────────────────────────
    private void DrawControlPanel()
    {
        float pw = 440f, py = 20f;
        float px = _vw / 2f - pw / 2f;

        GUI.Label(new Rect(px, py, pw, 36f), "BEAT MAPPER",
            Style(26, FontStyle.Bold, Color.yellow, TextAnchor.MiddleCenter));
        GUI.Label(new Rect(px, py + 36f, pw, 24f),
            $"BPM: {targetBPM:F1}  |  Grid: 1/{quantizeDivisor}  ({(60f / targetBPM / quantizeDivisor) * 1000f:F0} ms/snap)",
            Style(13, FontStyle.Normal, new Color(0.85f, 0.85f, 0.85f), TextAnchor.MiddleCenter));

        GUI.color = _isRecording ? new Color(1f, 0.3f, 0.3f) : Color.white;
        if (GUI.Button(new Rect(px, py + 68f, pw, 54f), _isRecording ? "■  STOP RECORDING" : "●  START TAP RECORD"))
        {
            _isRecording = !_isRecording;
            if (_isRecording)
            {
                _rawTaps.Clear();
                _quantizedBeats.Clear();
                _lastTapTime = -1f;
                _lastRawTime = _lastSnappedTime = -1f;
                audioSource.time = 0f;
                audioSource.Play();
            }
            else
            {
                audioSource.Stop();
                SnapTapsToBeats();
            }
        }
        GUI.color = Color.white;

        if (!_isRecording && _quantizedBeats.Count > 0)
        {
            GUI.color = Color.green;
            if (GUI.Button(new Rect(px, py + 130f, pw, 44f), $"SAVE  {_quantizedBeats.Count}  BEATS"))
                SaveCustomTrack();
            GUI.color = Color.white;
        }

        GUI.Label(new Rect(px, py + 182f, pw, 22f),
            _analysisReady
                ? "SPACEBAR = tap  •  snaps to nearest detected beat  •  save when done"
                : "SPACEBAR = tap  •  load audio first to enable beat snapping",
            Style(12, FontStyle.Normal, new Color(0.5f, 0.5f, 0.5f), TextAnchor.MiddleCenter));
    }

    // ── How-to guide (center, below control buttons) ─────────────────────
    private void DrawHowToPanel()
    {
        float pw = 440f;
        float px = _vw / 2f - pw / 2f;
        float py = 215f;
        float lh = 24f;
        float y  = py;

        GUIStyle header = Style(15, FontStyle.Bold,   new Color(0.35f, 1f, 0.75f), TextAnchor.MiddleLeft);
        GUIStyle step   = Style(14, FontStyle.Bold,   Color.yellow,                TextAnchor.MiddleLeft);
        GUIStyle desc   = Style(13, FontStyle.Normal, Color.white,                 TextAnchor.MiddleLeft);
        GUIStyle tip    = Style(12, FontStyle.Italic, new Color(0.6f, 0.6f, 0.6f), TextAnchor.MiddleLeft);

        GUI.Label(new Rect(px, y, pw, lh), "[ HOW TO MAP A SONG ]", header); y += lh + 4f;

        GUI.Label(new Rect(px, y, pw, lh), "1.  LOAD YOUR SONG", step); y += lh;
        GUI.Label(new Rect(px + 18f, y, pw, lh), "Click BROWSE or paste a file path, then hit LOAD.", desc); y += lh + 5f;

        GUI.Label(new Rect(px, y, pw, lh), "2.  SET THE BPM  (right panel)", step); y += lh;
        GUI.Label(new Rect(px + 18f, y, pw, lh), "Hit AUTO-DETECT for an estimate, then fine-tune with", desc); y += lh;
        GUI.Label(new Rect(px + 18f, y, pw, lh), "the  − / +  buttons until the grid feels right.", desc); y += lh + 5f;

        GUI.Label(new Rect(px, y, pw, lh), "3.  RECORD YOUR TAPS", step); y += lh;
        GUI.Label(new Rect(px + 18f, y, pw, lh), "Press START TAP RECORD — music plays from 0:00.", desc); y += lh;
        GUI.Label(new Rect(px + 18f, y, pw, lh), "Hit SPACEBAR on every beat or impact you want in the fight.", desc); y += lh + 5f;

        GUI.Label(new Rect(px, y, pw, lh), "4.  STOP & REVIEW", step); y += lh;
        GUI.Label(new Rect(px + 18f, y, pw, lh), "Press STOP — taps snap to your BPM grid automatically.", desc); y += lh;
        GUI.Label(new Rect(px + 18f, y, pw, lh), "Check beat count on the left. Wrong? Tweak BPM and redo.", desc); y += lh + 5f;

        GUI.Label(new Rect(px, y, pw, lh), "5.  SAVE", step); y += lh;
        GUI.Label(new Rect(px + 18f, y, pw, lh), "Hit SAVE BEATS — your map appears as a green PLAY button", desc); y += lh;
        GUI.Label(new Rect(px + 18f, y, pw, lh), "in the game lobby immediately.", desc); y += lh + 8f;

        GUI.Label(new Rect(px, y, pw, lh), "Tip: recording again any time overwrites the old map.", tip);
    }

    // ── Info panel (left side) ────────────────────────────────────────────
    private void DrawInfoPanel()
    {
        float x = 20f, y = 20f, w = 300f, lh = 24f;

        GUIStyle sec = Style(15, FontStyle.Bold,   new Color(0.35f, 1f, 0.75f));
        GUIStyle val = Style(14, FontStyle.Normal,  Color.white);
        GUIStyle dim = Style(13, FontStyle.Normal,  new Color(0.55f, 0.55f, 0.55f));

        // ── Audio ──
        GUI.Label(new Rect(x, y, w, lh), "[ AUDIO ]", sec); y += lh;
        float clipLen = audioSource.clip.length;
        float cur     = audioSource.time;
        GUI.Label(new Rect(x, y, w, lh), $"Time :      {cur:F2} s  /  {clipLen:F2} s", val); y += lh;
        DrawBar(new Rect(x, y, w - 10f, 8f), cur / clipLen, new Color(0.2f, 0.85f, 0.4f), new Color(0.1f, 0.18f, 0.12f));
        y += 14f;
        GUI.Label(new Rect(x, y, w, lh), $"Remaining : {(clipLen - cur):F2} s", dim); y += lh + 10f;

        // ── Taps ──
        GUI.Label(new Rect(x, y, w, lh), "[ TAPS ]", sec); y += lh;
        GUI.Label(new Rect(x, y, w, lh), $"Raw taps :        {_rawTaps.Count}", val); y += lh;
        GUI.Label(new Rect(x, y, w, lh), $"Quantized beats : {_quantizedBeats.Count}", val); y += lh;
        if (_rawTaps.Count > 0)
        {
            GUI.Label(new Rect(x, y, w, lh), $"Dupes dropped :   {_rawTaps.Count - _quantizedBeats.Count}", dim);
            y += lh;
        }
        y += 10f;

        // ── Beat analysis status ──
        GUI.Label(new Rect(x, y, w, lh), "[ BEAT ANALYSIS ]", sec); y += lh;
        Color statusCol = _analysisReady ? Color.green
                        : _analysisStatus.StartsWith("Analysis failed") ? Color.red
                        : Color.yellow;
        GUI.Label(new Rect(x, y, w, lh * 2), _analysisStatus,
            Style(12, FontStyle.Normal, statusCol)); y += lh * 2 + 4f;
        if (_analysisReady)
        {
            GUI.Label(new Rect(x, y, w, lh), $"Snap window : ±{maxSnapDistance * 1000f:F0} ms", val); y += lh;
        }
        else
        {
            float beatInt  = 60f / targetBPM;
            float snapGrid = beatInt / quantizeDivisor;
            GUI.Label(new Rect(x, y, w, lh), $"Fallback grid : {snapGrid * 1000f:F0} ms (1/{quantizeDivisor})", dim); y += lh;
        }
        y += 6f;

        // ── Last tap ──
        if (_isRecording)
        {
            GUI.Label(new Rect(x, y, w, lh), "[ LAST TAP ]", sec); y += lh;
            if (_lastTapTime >= 0f)
            {
                GUI.Label(new Rect(x, y, w, lh), $"At : {_lastTapTime:F3} s", val); y += lh;
                float  ms    = _lastTapOffset * 1000f;
                string grade = Mathf.Abs(ms) < 20f ? "EXCELLENT" : Mathf.Abs(ms) < 50f ? "GOOD" : "BAD";
                Color  gc    = Mathf.Abs(ms) < 20f ? Color.green : Mathf.Abs(ms) < 50f ? Color.yellow : Color.red;
                GUI.color = gc;
                GUI.Label(new Rect(x, y, w, lh), $"Offset : {ms:+0.0;-0.0} ms  [ {grade} ]", val);
                GUI.color = Color.white;
            }
            else
                GUI.Label(new Rect(x, y, w, lh), "No taps yet.", dim);
        }
        else if (_lastRawTime >= 0f)
        {
            GUI.Label(new Rect(x, y, w, lh), "[ LAST TAP ]", sec); y += lh;
            GUI.Label(new Rect(x, y, w, lh), $"Raw :     {_lastRawTime:F3} s", val); y += lh;
            GUI.color = Color.green;
            GUI.Label(new Rect(x, y, w, lh), $"Snapped : {_lastSnappedTime:F3} s", val);
            GUI.color = Color.white;
        }
    }

    // ── Spectrum visualizer (bottom center) ───────────────────────────────
    private void DrawVisualizer()
    {
        const int BARS = 32;
        float vizW   = _vw * 0.45f;
        float vizH   = 80f;
        float startX = _vw / 2f - vizW / 2f;
        float startY = _vh - vizH - 30f;
        float barW   = vizW / BARS - 2f;

        bool  flash  = _tapFlashTimer > 0f;
        Color active = flash ? Color.red                       : new Color(0.15f, 0.95f, 0.35f);
        Color bg     = flash ? new Color(0.28f, 0.04f, 0.04f) : new Color(0f, 0.18f, 0.08f);

        for (int i = 0; i < BARS; i++)
        {
            float sample = (audioSource.isPlaying && i < _spectrumData.Length) ? _spectrumData[i] : 0f;
            float barH   = Mathf.Clamp(sample * 600f, 2f, vizH);
            float bx     = startX + i * (barW + 2f);

            GUI.color = bg;
            GUI.DrawTexture(new Rect(bx, startY, barW, vizH), _vizTex);
            GUI.color = active;
            GUI.DrawTexture(new Rect(bx, startY + vizH - barH, barW, barH), _vizTex);
        }
        GUI.color = Color.white;

        GUI.Label(new Rect(startX, startY - 22f, vizW, 20f),
            flash ? "■  TAP" : "AUDIO SPECTRUM",
            Style(12, FontStyle.Bold, flash ? Color.red : new Color(0.3f, 1f, 0.4f), TextAnchor.MiddleCenter));
    }

    // ── Back button (bottom-left) ─────────────────────────────────────────
    private void DrawBackButton()
    {
        if (GUI.Button(new Rect(20f, _vh - 55f, 170f, 42f), "← BACK TO MENU"))
        {
            if (_isRecording) { audioSource.Stop(); _isRecording = false; }
            SceneManager.LoadScene("Menu");
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    private void DrawBar(Rect r, float fill, Color fg, Color bg)
    {
        GUI.color = bg;
        GUI.DrawTexture(r, _vizTex);
        GUI.color = fg;
        GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(fill), r.height), _vizTex);
        GUI.color = Color.white;
    }

    private static GUIStyle Style(int size, FontStyle fs, Color col, TextAnchor anchor = TextAnchor.MiddleLeft)
    {
        var s = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = fs, alignment = anchor };
        s.normal.textColor = col;
        return s;
    }

    private static GUIStyle BtnStyle(int size)
    {
        var s = new GUIStyle(GUI.skin.button) { fontSize = size, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        return s;
    }

    // ── Load panel (right side) ───────────────────────────────────────────
    private void DrawLoadPanel()
    {
        float pw = 360f;
        float px = _vw - pw - 20f;
        float py = 20f;
        float lh = 24f;
        float y  = py;

        GUIStyle secStyle  = Style(15, FontStyle.Bold,   new Color(0.35f, 1f, 0.75f));
        GUIStyle valStyle  = Style(13, FontStyle.Normal,  Color.white);
        GUIStyle dimStyle  = Style(12, FontStyle.Italic,  new Color(0.55f, 0.55f, 0.55f));

        // ── Load Audio ────────────────────────────────────────────────────
        GUI.Label(new Rect(px, y, pw, lh), "[ LOAD AUDIO ]", secStyle); y += lh;

        string clipName = audioSource?.clip != null ? audioSource.clip.name : "none";
        GUI.Label(new Rect(px, y, pw, lh), $"Loaded: {clipName}", valStyle); y += lh + 4f;

        GUI.Label(new Rect(px, y, pw, lh - 4f), "File path:", Style(12, FontStyle.Normal, new Color(0.7f, 0.7f, 0.7f)));
        y += lh - 2f;
        _filePath = GUI.TextField(new Rect(px, y, pw, 26f), _filePath, Style(11, FontStyle.Normal, Color.white));
        y += 30f;

        if (GUI.Button(new Rect(px, y, pw / 2f - 4f, 32f), "BROWSE...", BtnStyle(13)))
            OpenFileDialog();

        GUI.color = new Color(0.3f, 0.8f, 1f);
        if (GUI.Button(new Rect(px + pw / 2f + 4f, y, pw / 2f - 4f, 32f), "LOAD", BtnStyle(13)))
            if (!string.IsNullOrWhiteSpace(_filePath))
                StartCoroutine(LoadAudioCoroutine(_filePath.Trim()));
        GUI.color = Color.white;
        y += 38f;

        if (!string.IsNullOrEmpty(_loadStatus))
        {
            GUI.color = _loadStatusIsError ? Color.red : Color.green;
            GUI.Label(new Rect(px, y, pw, lh), _loadStatus,
                Style(13, FontStyle.Normal, _loadStatusIsError ? Color.red : Color.green));
            GUI.color = Color.white;
            y += lh + 4f;
        }
        else { y += 4f; }

        // ── BPM Section ───────────────────────────────────────────────────
        GUI.Label(new Rect(px, y, pw, lh), "[ BPM ]", secStyle); y += lh + 4f;

        // Large BPM display
        GUIStyle bpmDisplay = new GUIStyle(GUI.skin.box)
        {
            fontSize = 32,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        bpmDisplay.normal.textColor = Color.yellow;
        GUI.Box(new Rect(px, y, pw, 52f), $"{targetBPM:F1}  BPM", bpmDisplay);
        y += 58f;

        // Coarse adjustment row: -5, -1, +1, +5
        float bw = (pw - 6f) / 4f;
        GUI.color = new Color(1f, 0.5f, 0.5f);
        if (GUI.Button(new Rect(px,            y, bw, 38f), "− 5",  BtnStyle(14))) ApplyBPMDelta(-5f);
        if (GUI.Button(new Rect(px + bw + 2f,  y, bw, 38f), "− 1",  BtnStyle(14))) ApplyBPMDelta(-1f);
        GUI.color = new Color(0.5f, 1f, 0.5f);
        if (GUI.Button(new Rect(px + bw*2 + 4f, y, bw, 38f), "+ 1", BtnStyle(14))) ApplyBPMDelta(+1f);
        if (GUI.Button(new Rect(px + bw*3 + 6f, y, bw, 38f), "+ 5", BtnStyle(14))) ApplyBPMDelta(+5f);
        GUI.color = Color.white;
        y += 42f;

        // Fine adjustment row: -0.5, +0.5
        float hw = pw / 2f - 4f;
        GUI.color = new Color(1f, 0.75f, 0.5f);
        if (GUI.Button(new Rect(px,        y, hw, 32f), "− 0.5", BtnStyle(13))) ApplyBPMDelta(-0.5f);
        if (GUI.Button(new Rect(px + hw + 8f, y, hw, 32f), "+ 0.5", BtnStyle(13))) ApplyBPMDelta(+0.5f);
        GUI.color = Color.white;
        y += 38f;

        // Common BPM presets
        GUI.Label(new Rect(px, y, pw, lh - 4f), "Quick presets:", Style(12, FontStyle.Normal, new Color(0.7f, 0.7f, 0.7f)));
        y += lh;
        float[] presets = { 80f, 90f, 100f, 110f, 120f, 128f, 140f, 160f };
        float   pbw     = (pw - (presets.Length - 1) * 2f) / presets.Length;
        for (int i = 0; i < presets.Length; i++)
        {
            bool active = Mathf.Abs(targetBPM - presets[i]) < 0.5f;
            GUI.color = active ? Color.yellow : Color.white;
            if (GUI.Button(new Rect(px + i * (pbw + 2f), y, pbw, 30f), presets[i].ToString("F0"), BtnStyle(11)))
            {
                targetBPM = presets[i];
                _bpmInput = targetBPM.ToString("F1");
                _bpmStatus = $"BPM set to {targetBPM:F1}";
            }
        }
        GUI.color = Color.white;
        y += 36f;

        // Manual text input + SET
        GUI.Label(new Rect(px, y, 80f, lh), "Manual:", Style(12, FontStyle.Normal, new Color(0.7f, 0.7f, 0.7f)));
        _bpmInput = GUI.TextField(new Rect(px + 64f, y, 100f, 26f), _bpmInput, Style(13, FontStyle.Normal, Color.white));
        GUI.color = new Color(0.4f, 0.8f, 1f);
        if (GUI.Button(new Rect(px + 170f, y, 60f, 26f), "SET", BtnStyle(13)))
        {
            if (float.TryParse(_bpmInput, out float parsed) && parsed > 0f)
            {
                targetBPM  = Mathf.Round(parsed * 2f) / 2f;
                _bpmInput  = targetBPM.ToString("F1");
                _bpmStatus = $"BPM set to {targetBPM:F1}";
            }
            else { _bpmStatus = "Invalid — enter a positive number."; }
        }
        GUI.color = Color.white;
        y += 34f;

        // Auto-detect
        bool hasClip = audioSource?.clip != null;
        GUI.enabled = hasClip && !_detectingBPM && !_isRecording;
        GUI.color   = _detectingBPM ? Color.grey : new Color(1f, 0.8f, 0.2f);
        if (GUI.Button(new Rect(px, y, pw, 34f),
            _detectingBPM ? "DETECTING BPM..." : "AUTO-DETECT BPM FROM AUDIO", BtnStyle(13)))
            StartCoroutine(DetectBPMCoroutine());
        GUI.color   = Color.white;
        GUI.enabled = true;
        y += 40f;

        if (!string.IsNullOrEmpty(_bpmStatus))
        {
            GUI.Label(new Rect(px, y, pw, lh), _bpmStatus,
                Style(13, FontStyle.Normal, new Color(1f, 0.85f, 0.4f)));
            y += lh + 2f;
        }

        GUI.Label(new Rect(px, y, pw, lh * 2f),
            "After auto-detect, fine-tune with the buttons\nabove until the grid feels right.",
            dimStyle);
    }

    // ── BPM helpers ───────────────────────────────────────────────────────
    private void ApplyBPMDelta(float delta)
    {
        targetBPM  = Mathf.Max(20f, targetBPM + delta);
        _bpmInput  = targetBPM.ToString("F1");
        _bpmStatus = $"BPM set to {targetBPM:F1}";
    }

    // ── Improved BPM detector ─────────────────────────────────────────────
    // Uses adaptive-threshold onset detection + correct IOI histogram voting.
    // The old version used `1f/mult` weighting which biased toward 2× tempo
    // and produced ~187.5 BPM for almost every song.
    private IEnumerator DetectBPMCoroutine()
    {
        _detectingBPM = true;
        _bpmStatus    = "Analysing audio...";
        yield return null;

        AudioClip clip       = audioSource.clip;
        int       sampleRate = clip.frequency;
        int       channels   = clip.channels;

        // Analyse first 30 s only — enough to get a solid BPM estimate
        int analyzeSamples = Mathf.Min(clip.samples, sampleRate * 30);
        float[] pcm = new float[analyzeSamples * channels];
        clip.GetData(pcm, 0);
        yield return null;

        // ~23 ms hop (matches a typical STFT hop size)
        float   hopSec   = 0.023f;
        int     hopLen   = Mathf.Max(1, Mathf.RoundToInt(hopSec * sampleRate));
        int     frameCount = analyzeSamples / hopLen;

        // RMS energy per frame
        float[] rms = new float[frameCount];
        for (int f = 0; f < frameCount; f++)
        {
            float sum  = 0f;
            int   s0   = f * hopLen * channels;
            int   s1   = Mathf.Min(s0 + hopLen * channels, pcm.Length);
            for (int i = s0; i < s1; i++) sum += pcm[i] * pcm[i];
            rms[f] = Mathf.Sqrt(sum / Mathf.Max(1, s1 - s0));
            if (f % 500 == 0) yield return null;
        }

        // Half-wave rectified onset strength
        float[] onset = new float[frameCount];
        for (int f = 1; f < frameCount; f++)
            onset[f] = Mathf.Max(0f, rms[f] - rms[f - 1]);

        // Adaptive local threshold: local mean over ±0.5 s × 1.5
        int     winF   = Mathf.Max(1, Mathf.RoundToInt(0.5f / hopSec));
        float[] thresh = new float[frameCount];
        for (int f = 0; f < frameCount; f++)
        {
            int lo = Mathf.Max(0, f - winF), hi = Mathf.Min(frameCount - 1, f + winF);
            float s = 0f;
            for (int i = lo; i <= hi; i++) s += onset[i];
            thresh[f] = (s / (hi - lo + 1)) * 1.5f;
        }

        // Peak-pick: must exceed threshold, min 150 ms between picks
        int     minGap = Mathf.Max(1, Mathf.RoundToInt(0.15f / hopSec));
        var     onsets = new List<float>();
        int     last   = -minGap;
        for (int f = 1; f < frameCount - 1; f++)
        {
            if (onset[f] > onset[f - 1] && onset[f] >= onset[f + 1]
                && onset[f] > thresh[f] && f - last >= minGap)
            {
                onsets.Add(f * hopSec);
                last = f;
            }
        }

        if (onsets.Count < 8)
        {
            _bpmStatus    = $"Only {onsets.Count} onsets found — set BPM manually.";
            _detectingBPM = false;
            yield break;
        }

        // IOI histogram — compare each onset to its next 4 neighbours only.
        // Weight by 1/(distance) so consecutive pairs count more.
        // No octave voting — that's what caused the 187.5 bias.
        const float BPM_MIN  = 60f;
        const float BPM_MAX  = 200f;
        const float BPM_STEP = 1f;
        int         bins     = Mathf.RoundToInt((BPM_MAX - BPM_MIN) / BPM_STEP) + 1;
        float[]     histogram = new float[bins];

        for (int i = 0; i < onsets.Count - 1; i++)
        {
            for (int j = i + 1; j <= Mathf.Min(i + 4, onsets.Count - 1); j++)
            {
                float ioi = onsets[j] - onsets[i];
                if (ioi < 0.25f || ioi > 2.0f) continue;   // 30–240 BPM guard

                float bpm = 60f / ioi;
                if (bpm < BPM_MIN || bpm > BPM_MAX) continue;

                float weight = 1f / (j - i);   // closer pairs get more weight
                int   bin    = Mathf.RoundToInt((bpm - BPM_MIN) / BPM_STEP);
                if (bin >= 0 && bin < bins) histogram[bin] += weight;
            }
        }

        // 5-tap Gaussian smooth to merge near-BPM bins
        float[] smooth = new float[bins];
        for (int b = 2; b < bins - 2; b++)
            smooth[b] = histogram[b-2]*0.1f + histogram[b-1]*0.2f + histogram[b]*0.4f
                      + histogram[b+1]*0.2f + histogram[b+2]*0.1f;

        int bestBin = 0; float bestVal = -1f;
        for (int b = 0; b < bins; b++)
            if (smooth[b] > bestVal) { bestVal = smooth[b]; bestBin = b; }

        targetBPM     = BPM_MIN + bestBin * BPM_STEP;
        _bpmInput     = targetBPM.ToString("F1");
        _bpmStatus    = $"Detected: {targetBPM:F1} BPM  ({onsets.Count} onsets)";
        _detectingBPM = false;
    }

    // ── File dialog ───────────────────────────────────────────────────────
    // Uses the shared NativeFileDialog: native panel in the Editor, a properly-shown PowerShell
    // dialog in a standalone Windows build. The old inline version ran PowerShell with
    // -NonInteractive + CreateNoWindow, so the dialog never appeared and Browse "did nothing".
    private void OpenFileDialog()
    {
        var pick = NativeFileDialog.OpenAudio(out string editorImmediate);

        if (pick == null)
        {
            // Editor: result resolved synchronously.
            if (!string.IsNullOrEmpty(editorImmediate))
                _pendingFilePath = editorImmediate;
            return;
        }

        // Standalone: poll it in Update().
        _filePick = pick;
        _loadStatus = "Opening file browser…"; _loadStatusIsError = false;
    }

    // ── Audio loader ──────────────────────────────────────────────────────
    private IEnumerator LoadAudioCoroutine(string path)
    {
        _loadStatus = "Loading..."; _loadStatusIsError = false;

        AudioType audioType = GetAudioTypeFromPath(path);
        if (audioType == AudioType.UNKNOWN)
        {
            _loadStatus = "Unsupported format. Use .mp3, .wav, .ogg, or .aiff";
            _loadStatusIsError = true; yield break;
        }

        string url = new System.Uri(path).AbsoluteUri;
        using UnityWebRequest req = UnityWebRequestMultimedia.GetAudioClip(url, audioType);
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            _loadStatus = $"Error: {req.error}"; _loadStatusIsError = true; yield break;
        }

        AudioClip clip = DownloadHandlerAudioClip.GetContent(req);
        clip.name = Path.GetFileNameWithoutExtension(path);

        if (_isRecording) { audioSource.Stop(); _isRecording = false; }
        _rawTaps.Clear(); _quantizedBeats.Clear();
        _lastTapTime = -1f; _lastRawTime = _lastSnappedTime = -1f;

        audioSource.clip   = clip;
        _loadStatus        = $"Loaded: {clip.name}  ({clip.length:F1}s)";
        _loadStatusIsError = false;

        // Auto-analyse for beat snapping
        _analysisReady  = false;
        _analyzedBeats.Clear();
        _analysisStatus = "Analysing beats…";
        _engine.Analyze(clip);
    }

    private static AudioType GetAudioTypeFromPath(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".mp3"            => AudioType.MPEG,
            ".wav"            => AudioType.WAV,
            ".ogg"            => AudioType.OGGVORBIS,
            ".aiff" or ".aif" => AudioType.AIFF,
            _                 => AudioType.UNKNOWN
        };
    }

    // ── Save ──────────────────────────────────────────────────────────────
    private void SaveCustomTrack()
    {
        string clipName = audioSource.clip.name;
        // Durable save: writes a real .txt (Resources in editor, persistentDataPath in builds) so the
        // map survives quitting Unity AND ships in builds — not just a fragile PlayerPrefs entry.
        BeatMapStore.Save(clipName, string.Join("|", _quantizedBeats),
            string.IsNullOrEmpty(_filePath) ? null : _filePath);

        _loadStatus = $"Saved \"{clipName}\" — {_quantizedBeats.Count} beats (permanent).";
        _loadStatusIsError = false;
        Debug.Log($"<color=green>SAVED MAP:</color> {clipName}  ({_quantizedBeats.Count} beats)");
    }

    // True on the frame a tap is pressed — Spacebar (or left mouse) — under EITHER input backend.
    private bool TapPressed()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null && kb.spaceKey.wasPressedThisFrame) return true;
        var ms = UnityEngine.InputSystem.Mouse.current;
        if (ms != null && ms.leftButton.wasPressedThisFrame) return true;
        return false;
#else
        return Input.GetKeyDown(KeyCode.Space) || Input.GetMouseButtonDown(0);
#endif
    }
}
