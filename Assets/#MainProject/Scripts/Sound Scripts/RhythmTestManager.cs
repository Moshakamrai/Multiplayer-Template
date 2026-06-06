using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

/// <summary>
/// RhythmTest scene controller.
///
/// LAYOUT (left to right):
///   [LOAD PANEL]  [TIMELINE — beats row + chain windows row]  [STATS + SAVE]
///
/// TIMELINE KEY:
///   Yellow tick  = attack trigger (fires AFTER the voice window)
///   Blue box     = voice input window (speak here, then attack fires at box right-edge)
///   Green box    = currently active voice window (during preview)
///   Red line     = playhead (current song position)
///
/// Scene setup: empty GameObject with AudioSource + BeatAnalysisEngine + this.
/// </summary>
[RequireComponent(typeof(AudioSource))]
[RequireComponent(typeof(BeatAnalysisEngine))]
public class RhythmTestManager : MonoBehaviour
{
    private enum Stage { Idle, Loading, Analysing, Ready, Playing }
    private Stage _stage = Stage.Idle;

    private AudioSource        _audio;
    private BeatAnalysisEngine _engine;
    private Texture2D          _px;

    // Virtual screen dims — real screen on desktop, fixed 1920x1080 on Android
    // (scaled via MobileGUI) so buttons stay tappable on phones. Set in OnGUI.
    private float              _vw, _vh;
    private Matrix4x4          _guiPrev;

    // File
    private string _filePath    = "";
    private string _fileStatus  = "";
    private bool   _fileError   = false;
    private string _pendingFile = null;

    // Analysis
    private float   _progress = 0f;
    private string  _progMsg  = "";
    private BeatMap _map      = null;

    // Save
    private string _saveMsg   = "";
    private bool   _saveErr   = false;

    // Timeline
    private float _tlScroll = 0f;
    private float _tlWin    = 12f;  // seconds visible

    // Preview
    private float[] _spec = new float[64];

    // Grade flash
    private string _grade  = "";
    private Color  _gradeC = Color.white;
    private float  _gradeT = 0f;

    // ─────────────────────────────────────────────────────────────────────
    void Awake()
    {
        _audio  = GetComponent<AudioSource>();
        _engine = GetComponent<BeatAnalysisEngine>();

        _engine.OnAnalysisProgress += p => { _progress = p; _progMsg = $"Analysing… {Mathf.RoundToInt(p*100f)}%"; };
        _engine.OnAnalysisComplete += map =>
        {
            _map   = map;
            _stage = Stage.Ready;
            _progMsg = "";
            Debug.Log($"[RhythmTest] {map.TotalBeatCount} beats, {map.TotalChainCount} chains, {map.bpm:F1} BPM");
        };
        _engine.OnAnalysisError += msg =>
        {
            _fileStatus = msg; _fileError = true; _stage = Stage.Idle; _progMsg = "";
        };
    }

    void Update()
    {
        if (_gradeT > 0f) _gradeT -= Time.deltaTime;

        if (_pendingFile != null) { _filePath = _pendingFile; _pendingFile = null; StartCoroutine(LoadAudio(_filePath)); }

        if (_stage != Stage.Playing) return;

        if (!_audio.isPlaying) { _stage = Stage.Ready; return; }

        _audio.GetSpectrumData(_spec, 0, FFTWindow.BlackmanHarris);
        _tlScroll = Mathf.Max(0f, _audio.time - _tlWin * 0.25f);

#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.Space)) TestVoiceInput();
#endif
    }

    // ─────────────────────────────────────────────────────────────────────
    void OnGUI()
    {
        if (RhythmRoundManager.Instance != null && RhythmRoundManager.Instance.isShopPhase) return;
        if (_px == null) { _px = new Texture2D(1,1); _px.SetPixel(0,0,Color.white); _px.Apply(); }

        // Scale the whole UI for phones (no-op on desktop).
        _guiPrev = MobileGUI.Begin(out _vw, out _vh);

        // Background
        Fill(0, 0, _vw, _vh, new Color(0.07f, 0.07f, 0.1f));

        DrawHeader();

        float panelY = 52f;
        float panelH = _vh - panelY - 90f;

        DrawLoadPanel(16f, panelY, 280f, panelH);
        DrawTimeline(308f, panelY, _vw - 616f, panelH);
        DrawStatsPanel(_vw - 300f, panelY, 284f, panelH);

        DrawFooter();
        DrawGrade();

        MobileGUI.End(_guiPrev);
    }

    // ── Header ────────────────────────────────────────────────────────────
    void DrawHeader()
    {
        Fill(0, 0, _vw, 50, new Color(0.04f, 0.04f, 0.09f));
        string songInfo = _map != null
            ? $"   {_map.songName}   |   {_map.bpm:F1} BPM   |   {_map.TotalBeatCount} triggers   |   {_map.TotalChainCount} voice windows"
            : "   No song loaded";
        Lbl(10, 6,  300, 22, "RHYTHM MAPPER", 16, FontStyle.Bold, new Color(0f,1f,0.55f));
        Lbl(10, 28, _vw - 20, 20, songInfo, 11, FontStyle.Normal, new Color(0.65f,0.65f,0.65f));
    }

    // ── Left: load panel ──────────────────────────────────────────────────
    void DrawLoadPanel(float x, float y, float w, float h)
    {
        Fill(x, y, w, h, new Color(0.04f, 0.04f, 0.09f));
        float cy = y + 8f;

        SectionTitle(x+6, ref cy, w-12, "LOAD SONG");

        Lbl(x+6, cy, w-12, 18, "Audio file (.mp3 .wav .ogg .aiff):", 10, FontStyle.Normal, new Color(0.55f,0.55f,0.55f));
        cy += 18f;
        _filePath = GUI.TextField(new Rect(x+6, cy, w-12, 24), _filePath, Sty(10, FontStyle.Normal, Color.white));
        cy += 28f;

        float bw = (w - 18f) / 2f;
        if (Btn(x+6, cy, bw, 30, "BROWSE", Color.white)) OpenFileDlg();
        bool canLoad = !string.IsNullOrWhiteSpace(_filePath) && _stage != Stage.Loading && _stage != Stage.Analysing;
        GUI.enabled = canLoad;
        if (Btn(x + bw + 12f, cy, bw, 30, "LOAD", new Color(0.3f,0.8f,1f)))
            StartCoroutine(LoadAudio(_filePath.Trim()));
        GUI.enabled = true;
        cy += 36f;

        if (!string.IsNullOrEmpty(_fileStatus))
        {
            Lbl(x+6, cy, w-12, 40, _fileStatus, 10, FontStyle.Normal, _fileError ? Color.red : Color.green);
            cy += 44f;
        }

        if (!string.IsNullOrEmpty(_progMsg))
        {
            Lbl(x+6, cy, w-12, 18, _progMsg, 10, FontStyle.Normal,
                _stage == Stage.Analysing ? Color.yellow : new Color(0f,1f,0.5f));
            cy += 20f;
            if (_stage == Stage.Analysing)
            {
                Fill(x+6, cy, w-12, 8, new Color(0.08f,0.08f,0.15f));
                Fill(x+6, cy, (w-12)*_progress, 8, new Color(0.2f,0.6f,1f));
                cy += 14f;
            }
        }

        if (_stage == Stage.Ready || _stage == Stage.Playing)
        {
            cy += 8f;
            SectionTitle(x+6, ref cy, w-12, "PREVIEW");

            bool playing = _stage == Stage.Playing;
            if (Btn(x+6, cy, w-12, 36, playing ? "■  STOP" : "▶  PLAY", playing ? new Color(1f,0.3f,0.3f) : new Color(0.25f,1f,0.45f)))
            {
                if (playing) { _audio.Stop(); _stage = Stage.Ready; }
                else         { _audio.time = 0f; _audio.Play(); _stage = Stage.Playing; _tlScroll = 0f; }
            }
            cy += 42f;

            if (playing && _audio.clip != null)
            {
                float frac = _audio.time / _audio.clip.length;
                Fill(x+6, cy, w-12, 6, new Color(0.08f,0.08f,0.15f));
                Fill(x+6, cy, (w-12)*frac, 6, new Color(0.25f,1f,0.45f));
                cy += 12f;
                Lbl(x+6, cy, w-12, 18, $"{_audio.time:F1}s / {_audio.clip.length:F1}s",
                    10, FontStyle.Normal, new Color(0.5f,0.5f,0.5f), TextAnchor.MiddleCenter);
                cy += 22f;
            }

            cy += 8f;
            SectionTitle(x+6, ref cy, w-12, "TUNING (Inspector)");
            Lbl(x+6, cy, w-12, 18*5, $"thresholdMultiplier\n  raise → fewer beats\n\nmaxInChainGapSec\n  lower → fewer chains\n\nminInputWindowSec\n  raise → more spacing between triggers",
                9, FontStyle.Normal, new Color(0.45f,0.45f,0.5f));
        }
    }

    // ── Centre: timeline ──────────────────────────────────────────────────
    void DrawTimeline(float tx, float ty, float tw, float th)
    {
        Fill(tx, ty, tw, th, new Color(0.03f, 0.03f, 0.07f));

        if (_map == null)
        {
            Lbl(tx, ty + th*0.4f, tw, 40,
                "Timeline appears here after analysis.",
                12, FontStyle.Italic, new Color(0.3f,0.3f,0.4f), TextAnchor.MiddleCenter);
            return;
        }

        // Zoom / scroll controls
        float ctrlY = ty + th + 4f;
        if (Btn(tx,      ctrlY, 28, 22, "◄", Color.white)) _tlScroll = Mathf.Max(0, _tlScroll - _tlWin * 0.5f);
        if (Btn(tx + 30, ctrlY, 28, 22, "►", Color.white)) _tlScroll = Mathf.Min(_map.songLength - _tlWin, _tlScroll + _tlWin * 0.5f);
        if (Btn(tx + 64, ctrlY, 26, 22, "−", Color.white)) _tlWin = Mathf.Min(_map.songLength, _tlWin * 1.5f);
        if (Btn(tx + 92, ctrlY, 26, 22, "+", Color.white)) _tlWin = Mathf.Max(3f, _tlWin / 1.5f);
        Lbl(tx+122, ctrlY+3, 300, 18, $"{_tlScroll:F1}s – {(_tlScroll+_tlWin):F1}s   (zoom: {_tlWin:F1}s visible)",
            9, FontStyle.Normal, new Color(0.45f,0.45f,0.5f));

        float visEnd = _tlScroll + _tlWin;

        // ─ Time ruler ──────────────────────────────────────────────────
        float rulerH = 22f;
        Fill(tx, ty, tw, rulerH, new Color(0.05f,0.05f,0.1f));

        float step = NiceStep(_tlWin / 8f);
        float first = Mathf.Ceil(_tlScroll / step) * step;
        for (float t = first; t <= visEnd; t += step)
        {
            float rx = TtoX(t, tx, tw);
            Fill(rx, ty, 1f, rulerH, new Color(0.25f,0.25f,0.35f));
            Lbl(rx+2, ty+4, 60, 14, $"{t:F1}s", 8, FontStyle.Normal, new Color(0.5f,0.5f,0.6f));
        }

        // ─ BEATS ROW (top half) ────────────────────────────────────────
        float beatRowY = ty + rulerH;
        float beatRowH = (th - rulerH) * 0.38f;
        Fill(tx, beatRowY, tw, beatRowH, new Color(0.04f, 0.04f, 0.08f));
        Lbl(tx+4, beatRowY+2, 120, 14, "BEAT TRIGGERS", 8, FontStyle.Bold, new Color(0.5f,0.55f,0.6f));

        foreach (var beat in _map.allBeats)
        {
            if (beat.time < _tlScroll - 0.1f || beat.time > visEnd + 0.1f) continue;
            float bx = TtoX(beat.time, tx, tw);
            float bh = beatRowH * Mathf.Clamp(0.3f + beat.strength * 0.6f, 0.2f, 0.95f);
            // Yellow = trigger that the game uses
            Fill(bx - 1f, beatRowY + beatRowH - bh, 3f, bh, new Color(1f, 0.85f, 0.15f));
        }

        // ─ VOICE WINDOWS ROW (bottom half) ────────────────────────────
        float winRowY = beatRowY + beatRowH;
        float winRowH = th - rulerH - beatRowH;
        Fill(tx, winRowY, tw, winRowH, new Color(0.03f, 0.04f, 0.07f));
        Lbl(tx+4, winRowY+2, 180, 14, "VOICE INPUT WINDOWS  (say command inside box)", 8, FontStyle.Bold, new Color(0.4f,0.55f,0.7f));

        for (int ci = 0; ci < _map.chains.Count; ci++)
        {
            var chain = _map.chains[ci];
            if (chain.inputWindowEnd < _tlScroll || chain.inputWindowStart > visEnd) continue;

            float wx = TtoX(chain.inputWindowStart, tx, tw);
            float we = TtoX(chain.inputWindowEnd,   tx, tw);
            float boxW = Mathf.Max(3f, we - wx);

            bool active = _stage == Stage.Playing && chain.WindowContains(_audio.time);
            Color boxFill    = active ? new Color(0.05f,0.35f,0.05f,0.7f) : new Color(0.05f,0.15f,0.35f,0.6f);
            Color boxBorder  = active ? Color.green : new Color(0.2f,0.45f,0.75f);

            Fill(wx,        winRowY + 18f, boxW, winRowH - 22f, boxFill);
            Fill(wx,        winRowY + 18f, 2f,   winRowH - 22f, boxBorder);  // left edge
            Fill(wx+boxW-2, winRowY + 18f, 2f,   winRowH - 22f, boxBorder);  // right edge

            // Arrow pointing right — window ends, then attacks fire
            if (boxW > 20f)
                Fill(wx+boxW, winRowY + 18f + (winRowH-22f)*0.4f, 8f, 2f, new Color(1f,0.85f,0.15f,0.6f));

            // Label
            if (boxW > 30f)
            {
                string label = $"#{ci+1}  {chain.chainLength}×  {chain.inputWindowDuration:F1}s  →atk";
                Lbl(wx+4, winRowY+20f, boxW-6, 16, label,
                    9, FontStyle.Bold, active ? Color.green : new Color(0.55f,0.75f,1f));
            }
        }

        // ─ Playhead ────────────────────────────────────────────────────
        if (_stage == Stage.Playing && _audio.clip != null)
        {
            float px = TtoX(_audio.time, tx, tw);
            Fill(px-1f, ty, 3f, th, new Color(1f, 0.2f, 0.2f, 0.85f));
        }

        // ─ Legend ──────────────────────────────────────────────────────
        float legY = ty + th - 18f;
        Fill(tx, legY, tw, 18f, new Color(0.03f,0.03f,0.06f));
        Lbl(tx+6, legY+2, tw-12, 14,
            "  |  Yellow bar = attack trigger   Blue box = voice window (speak here, THEN attack fires)   Red line = playhead  |",
            9, FontStyle.Normal, new Color(0.4f,0.4f,0.45f), TextAnchor.MiddleCenter);
    }

    // ── Right: stats + save ───────────────────────────────────────────────
    void DrawStatsPanel(float x, float y, float w, float h)
    {
        Fill(x, y, w, h, new Color(0.04f, 0.04f, 0.09f));
        float cy = y + 8f;

        SectionTitle(x+6, ref cy, w-12, "ANALYSIS");

        if (_map == null) { Lbl(x+6, cy, w-12, 24, "No data yet.", 11, FontStyle.Italic, new Color(0.35f,0.35f,0.4f)); return; }

        Stat(x+6, ref cy, w-12, "Song",           _map.songName);
        Stat(x+6, ref cy, w-12, "Detected BPM",   $"{_map.bpm:F1}");
        Stat(x+6, ref cy, w-12, "Beat interval",  $"{_map.beatInterval*1000f:F0} ms");
        Stat(x+6, ref cy, w-12, "Beat triggers",  $"{_map.TotalBeatCount}");
        Stat(x+6, ref cy, w-12, "Voice windows",  $"{_map.TotalChainCount}");
        Stat(x+6, ref cy, w-12, "Song length",    $"{_map.songLength:F1} s");
        cy += 6f;

        // Chain breakdown
        int single = 0, duo = 0, tri = 0, quad = 0;
        foreach (var c in _map.chains)
        {
            if (c.chainLength == 1) single++;
            else if (c.chainLength == 2) duo++;
            else if (c.chainLength == 3) tri++;
            else quad++;
        }
        SectionTitle(x+6, ref cy, w-12, "CHAIN BREAKDOWN");
        Stat(x+6, ref cy, w-12, "Single (1×)", $"{single}");
        Stat(x+6, ref cy, w-12, "Duo    (2×)", $"{duo}");
        Stat(x+6, ref cy, w-12, "Triple (3×)", $"{tri}");
        Stat(x+6, ref cy, w-12, "Quad   (4×)", $"{quad}");
        cy += 6f;

        SectionTitle(x+6, ref cy, w-12, "VOICE WINDOW RULES");
        Lbl(x+6, cy, w-12, 18, "  Window comes BEFORE the attack fires", 10, FontStyle.Normal, new Color(0.7f,0.85f,1f)); cy += 18f;
        Lbl(x+6, cy, w-12, 18, $"  Flat {_engine.minInputWindowSec:F1}s window for ALL chains (1–4 beats)", 10, FontStyle.Normal, new Color(0.7f,0.85f,1f)); cy += 18f;
        Lbl(x+6, cy, w-12, 18, $"  No triggers before {_engine.noTriggerBeforeSec:F0}s (grace period)", 10, FontStyle.Normal, new Color(0.7f,0.85f,1f)); cy += 22f;

        // ── SAVE ─────────────────────────────────────────────────────
        SectionTitle(x+6, ref cy, w-12, "SAVE TO GAME");

        bool saved = PlayerPrefs.HasKey("CustomMap_" + _map.songName);
        string saveLabel = saved ? "♻  OVERWRITE & SAVE" : "💾  SAVE TO GAME";
        Color  saveCol   = saved ? new Color(1f,0.6f,0.2f) : new Color(0.1f,0.9f,0.3f);

        if (Btn(x+6, cy, w-12, 46, saveLabel, saveCol)) DoSave();
        cy += 52f;

        if (!string.IsNullOrEmpty(_saveMsg))
        {
            Lbl(x+6, cy, w-12, 36, _saveMsg, 10, FontStyle.Normal, _saveErr ? Color.red : Color.green);
            cy += 40f;
        }

        if (saved)
        {
            Lbl(x+6, cy, w-12, 18, "✓ Map exists in PlayerPrefs", 10, FontStyle.Normal, Color.green); cy += 18f;
            Lbl(x+6, cy, w-12, 18, "  Lobby PLAY button ready",   10, FontStyle.Normal, new Color(0.5f,0.8f,0.5f));
        }
    }

    // ── Footer: spectrum + back ───────────────────────────────────────────
    void DrawFooter()
    {
        float fy = _vh - 88f;
        Fill(0, fy, _vw, 88, new Color(0.03f,0.03f,0.07f));

        // Spectrum bars
        const int BARS = 52;
        float sw = _vw * 0.48f, sh = 52f;
        float sx = _vw/2f - sw/2f, sy = fy + 8f;
        float bw = sw / BARS - 1f;

        bool winOpen = _stage == Stage.Playing && _map != null && _map.ActiveChainAt(_audio.time) != null;
        Color fg = winOpen ? Color.green : new Color(0.15f, 0.75f, 0.3f);

        for (int i = 0; i < BARS; i++)
        {
            float s  = (_stage == Stage.Playing && i < _spec.Length) ? _spec[i] : 0f;
            float bh = Mathf.Clamp(s*420f, 2f, sh);
            Fill(sx+i*(bw+1f), sy, bw, sh, new Color(0.04f,0.07f,0.04f));
            Fill(sx+i*(bw+1f), sy+sh-bh, bw, bh, fg);
        }

        string specLabel = winOpen ? "●  VOICE WINDOW OPEN — speak your command now" : "spectrum";
        Lbl(sx, sy+sh+2, sw, 14, specLabel,
            10, FontStyle.Bold, winOpen ? Color.green : new Color(0.3f,0.5f,0.3f), TextAnchor.MiddleCenter);

        if (GUI.Button(new Rect(14, _vh - 44f, 150, 36), "← BACK TO MENU"))
        { _audio.Stop(); SceneManager.LoadScene("Menu"); }

        Lbl(_vw/2f-180, _vh-42f, 360, 18,
            "SPACE = simulate voice input (preview only)", 10, FontStyle.Italic,
            new Color(0.4f,0.4f,0.45f), TextAnchor.MiddleCenter);
    }

    // ── Grade flash ───────────────────────────────────────────────────────
    void DrawGrade()
    {
        if (_gradeT <= 0f) return;
        float a = _gradeT / 0.65f;
        GUI.color = new Color(_gradeC.r, _gradeC.g, _gradeC.b, a);
        GUI.Label(new Rect(_vw/2f-160, _vh/2f-55, 320, 110),
            _grade, Sty(56, FontStyle.Bold, _gradeC, TextAnchor.MiddleCenter));
        GUI.color = Color.white;
    }

    // ── Save (exact SmartBeatMapper format) ───────────────────────────────
    void DoSave()
    {
        if (_map == null) return;

        var times = new List<float>();
        float last = -1f;
        // Only output beats that are part of a valid chain (restrictions already applied)
        foreach (var chain in _map.chains)
            foreach (var beat in chain.beats)
            {
                if (beat.time - last >= 0.25f) { times.Add(beat.time); last = beat.time; }
            }

        times.Sort();
        string clipName = _map.songName;
        string data     = string.Join("|", times.ConvertAll(t => t.ToString(CultureInfo.InvariantCulture)));

        PlayerPrefs.SetString("CustomMap_" + clipName, data);
        if (!string.IsNullOrEmpty(_filePath))
            PlayerPrefs.SetString("CustomMapPath_" + clipName, _filePath);

        string reg  = PlayerPrefs.GetString("CustomMapRegistry", "");
        var    keys = new HashSet<string>(reg.Split(new[]{'|'}, System.StringSplitOptions.RemoveEmptyEntries));
        keys.Add(clipName);
        PlayerPrefs.SetString("CustomMapRegistry", string.Join("|", keys));
        PlayerPrefs.Save();

        _saveMsg = $"Saved {times.Count} triggers for \"{clipName}\"";
        _saveErr = false;
        Debug.Log($"<color=green>[RhythmTestManager] Saved CustomMap_{clipName} — {times.Count} beats</color>");
    }

    // ── Test voice input (Space during preview) ───────────────────────────
    void TestVoiceInput()
    {
        if (_map == null) return;
        var chain = _map.ActiveChainAt(_audio.time);
        if (chain == null) { Grade("EARLY / LATE", Color.red); return; }

        float nearest = float.MaxValue;
        foreach (var b in chain.beats)
        { float d = Mathf.Abs(_audio.time - b.time); if (d < nearest) nearest = d; }

        float ms = nearest * 1000f;
        if      (ms <= 80f)  Grade("PERFECT!", new Color(0f,1f,0.4f));
        else if (ms <= 200f) Grade("GOOD",     Color.yellow);
        else                 Grade("OK",        new Color(1f,0.6f,0.2f));
    }

    void Grade(string t, Color c) { _grade = t; _gradeC = c; _gradeT = 0.65f; }

    // ── Audio loader ──────────────────────────────────────────────────────
    IEnumerator LoadAudio(string path)
    {
        _stage = Stage.Loading; _fileStatus = "Loading…"; _fileError = false;
        _saveMsg = ""; _map = null; _progMsg = "";

        AudioType at = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".mp3"            => AudioType.MPEG,
            ".wav"            => AudioType.WAV,
            ".ogg"            => AudioType.OGGVORBIS,
            ".aiff" or ".aif" => AudioType.AIFF,
            _                 => AudioType.UNKNOWN
        };
        if (at == AudioType.UNKNOWN)
        {
            _fileStatus = "Unsupported format — use .wav .mp3 .ogg .aiff";
            _fileError = true; _stage = Stage.Idle; yield break;
        }

        using var req = UnityWebRequestMultimedia.GetAudioClip(new System.Uri(path).AbsoluteUri, at);
        ((DownloadHandlerAudioClip)req.downloadHandler).streamAudio = false;
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            _fileStatus = $"Failed: {req.error}"; _fileError = true; _stage = Stage.Idle; yield break;
        }

        AudioClip clip = DownloadHandlerAudioClip.GetContent(req);
        clip.name = Path.GetFileNameWithoutExtension(path);
        _audio.clip = clip;
        _audio.Stop();

        _fileStatus = $"Loaded \"{clip.name}\" ({clip.length:F1}s) — analysing…";
        _stage = Stage.Analysing;
        _engine.Analyze(clip);
    }

    void OpenFileDlg()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        new System.Threading.Thread(() =>
        {
            const string ps = @"Add-Type -AssemblyName System.Windows.Forms
$d=New-Object System.Windows.Forms.OpenFileDialog
$d.Filter='Audio|*.mp3;*.wav;*.ogg;*.aiff;*.aif|All|*.*'
if($d.ShowDialog()-eq'OK'){Write-Output $d.FileName}";
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell", Arguments = $"-NoProfile -NonInteractive -Command \"{ps.Replace("\"","\\\"")}\"",
                UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true
            };
            using var p = System.Diagnostics.Process.Start(psi);
            string r = p.StandardOutput.ReadToEnd().Trim(); p.WaitForExit();
            if (!string.IsNullOrEmpty(r)) _pendingFile = r;
        }).Start();
#else
        _fileStatus = "Browse not supported — paste path manually"; _fileError = true;
#endif
    }

    // ── GUI helpers ───────────────────────────────────────────────────────
    float TtoX(float t, float tx, float tw) => tx + (t - _tlScroll) / _tlWin * tw;

    float NiceStep(float raw)
    {
        float[] nice = { 0.5f, 1f, 2f, 5f, 10f, 15f, 30f, 60f };
        foreach (float n in nice) if (n >= raw) return n;
        return 60f;
    }

    void SectionTitle(float x, ref float y, float w, string t)
    {
        Fill(x, y+10, w, 1, new Color(0.18f,0.18f,0.25f));
        Lbl(x, y, w, 22, t, 10, FontStyle.Bold, new Color(0f,0.8f,0.5f)); y += 24f;
    }

    void Stat(float x, ref float y, float w, string label, string val)
    {
        Lbl(x, y, w*0.5f, 20, label, 10, FontStyle.Normal, new Color(0.55f,0.55f,0.6f));
        Lbl(x+w*0.5f, y, w*0.5f, 20, val, 10, FontStyle.Bold, Color.white);
        y += 20f;
    }

    void Fill(float x, float y, float w, float h, Color c)
    {
        GUI.color = c;
        GUI.DrawTexture(new Rect(x, y, w, h), _px);
        GUI.color = Color.white;
    }

    bool Btn(float x, float y, float w, float h, string txt, Color col)
    {
        GUI.color = col;
        bool r = GUI.Button(new Rect(x, y, w, h), txt);
        GUI.color = Color.white;
        return r;
    }

    void Lbl(float x, float y, float w, float h, string txt, int sz, FontStyle fs, Color col,
             TextAnchor a = TextAnchor.MiddleLeft)
        => GUI.Label(new Rect(x,y,w,h), txt, Sty(sz,fs,col,a));

    static GUIStyle Sty(int sz, FontStyle fs, Color col, TextAnchor a = TextAnchor.MiddleLeft)
    {
        var s = new GUIStyle(GUI.skin.label) { fontSize=sz, fontStyle=fs, alignment=a };
        s.normal.textColor = col;
        return s;
    }
}
