using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using System.Linq;

public class SmartBeatMapper : MonoBehaviour
{
    public AudioSource audioSource;

    [Header("Quantize Settings (FL Studio Style)")]
    public float targetBPM = 100f;

    [Tooltip("1 = Full Beats (0.6s), 2 = Half Beats (0.3s), 4 = 16th Notes")]
    public int quantizeDivisor = 2;

    [Header("Transient Snap")]
    [Tooltip("Scan the audio near each tap and lock onto the nearest energy spike. " +
             "Runs before BPM quantization for maximum accuracy.")]
    public bool useTransientSnap = true;

    [Tooltip("How far (ms) either side of a tap to search for an audio transient. " +
             "Keep this well under half a beat interval to avoid jumping to the wrong beat.")]
    [Range(20f, 300f)]
    public float transientSearchWindowMs = 150f;

    [Header("Chain Density Restriction")]
    [Tooltip("After quantization, enforce a rolling-window density cap: within any " +
             "chainInputWindowSec of time, no more than floor(window / 2) beats are kept. " +
             "Example: 6s window → max 3 beats in any 6s span.")]
    public bool enforceChainDensity = true;

    [Tooltip("The rolling time window (seconds) used to measure beat density. " +
             "Max beats in any window = floor(this / 2).")]
    public float chainInputWindowSec = 6f;

    private List<float> _rawTaps        = new List<float>();
    private List<float> _quantizedBeats = new List<float>();
    private bool        _isRecording    = false;

    // ── Visualizer ────────────────────────────────────────────────────────
    private float       _tapFlashTimer  = 0f;
    private const float FLASH_DURATION  = 0.15f;
    private float[]     _spectrumData   = new float[64];
    private Texture2D   _vizTex;

    // ── File loading ──────────────────────────────────────────────────────
    private string _filePath          = "";
    private string _loadStatus        = "";
    private bool   _loadStatusIsError = false;
    private string _pendingFilePath   = null;   // thread-safe handoff from file dialog

    // ── BPM input ─────────────────────────────────────────────────────────
    private string _bpmInput      = "";
    private string _bpmStatus     = "";
    private bool   _detectingBPM  = false;

    // ── Live tap feedback ─────────────────────────────────────────────────
    private float _lastTapTime      = -1f;
    private float _lastTapOffset    = 0f;   // vs BPM grid (live, shown while recording)

    // ── Post-process feedback (shown after Stop) ──────────────────────────
    private float _lastTransientShift  = 0f;
    private float _lastRawTime         = -1f;
    private float _lastTransientTime   = -1f;
    private float _lastFinalTime       = -1f;
    private int   _chainDensityPruned  = 0;   // beats removed by chain density cap

    // ─────────────────────────────────────────────────────────────────────
    void Update()
    {
        if (_tapFlashTimer > 0f)
            _tapFlashTimer -= Time.deltaTime;

        // Pick up file path set by the file-dialog thread
        if (_pendingFilePath != null)
        {
            _filePath = _pendingFilePath;
            _pendingFilePath = null;
            StartCoroutine(LoadAudioCoroutine(_filePath));
        }

        if (!_isRecording || audioSource == null || !audioSource.isPlaying) return;

        if (Input.GetKeyDown(KeyCode.Space))
        {
            float t = audioSource.time;
            _rawTaps.Add(t);
            _lastTapTime = t;

            float snapGrid = (60f / targetBPM) / quantizeDivisor;
            _lastTapOffset = t - Mathf.Round(t / snapGrid) * snapGrid;
            _tapFlashTimer = FLASH_DURATION;

            Debug.Log($"Raw Tap: {t:F3}s | Grid offset: {_lastTapOffset * 1000f:+0.0;-0.0}ms");
        }

        audioSource.GetSpectrumData(_spectrumData, 0, FFTWindow.BlackmanHarris);
    }

    // ─── Transient snap ───────────────────────────────────────────────────
    // Reads PCM data from the clip in a small window around the tap,
    // divides it into ~10ms chunks, and returns the timestamp of the chunk
    // with the sharpest energy rise (onset = the actual beat hit).
    private float FindNearestTransient(float tapTime)
    {
        AudioClip clip = audioSource.clip;
        int sampleRate = clip.frequency;
        int channels   = clip.channels;

        float windowSec    = transientSearchWindowMs / 1000f;
        int   windowFrames = Mathf.RoundToInt(windowSec * sampleRate);
        int   centerFrame  = Mathf.RoundToInt(tapTime * sampleRate);

        int startFrame = Mathf.Max(0, centerFrame - windowFrames);
        int endFrame   = Mathf.Min(clip.samples - 1, centerFrame + windowFrames);
        int frameCount = endFrame - startFrame;

        if (frameCount <= 0) return tapTime;

        float[] samples = new float[frameCount * channels];
        clip.GetData(samples, startFrame);

        // ~10ms per analysis chunk
        int chunkFrames  = Mathf.Max(1, Mathf.RoundToInt(0.01f * sampleRate));
        int chunkSamples = chunkFrames * channels;

        float prevEnergy = 0f;
        float maxFlux    = float.NegativeInfinity;
        int   bestFrame  = 0;   // relative to startFrame

        for (int i = 0; i + chunkSamples <= samples.Length; i += chunkSamples)
        {
            float energy = 0f;
            int   limit  = Mathf.Min(i + chunkSamples, samples.Length);
            for (int j = i; j < limit; j++)
                energy += samples[j] * samples[j];
            energy /= (limit - i);

            float flux = energy - prevEnergy;   // positive = energy rising = onset
            if (flux > maxFlux)
            {
                maxFlux   = flux;
                bestFrame = i / channels;
            }
            prevEnergy = energy;
        }

        return (startFrame + bestFrame) / (float)sampleRate;
    }

    // ─── Two-stage pipeline ───────────────────────────────────────────────
    // Stage 1 (optional): snap each raw tap to the nearest audio transient.
    // Stage 2: quantize to the BPM grid.
    private void QuantizeTaps()
    {
        _quantizedBeats.Clear();
        if (_rawTaps.Count == 0) return;

        float snapGrid = (60f / targetBPM) / quantizeDivisor;

        for (int i = 0; i < _rawTaps.Count; i++)
        {
            float raw        = _rawTaps[i];
            float transient  = useTransientSnap ? FindNearestTransient(raw) : raw;
            float snapped    = Mathf.Round(transient / snapGrid) * snapGrid;

            // Track stats for the last tap so the UI can display the pipeline
            if (i == _rawTaps.Count - 1)
            {
                _lastRawTime       = raw;
                _lastTransientTime = transient;
                _lastFinalTime     = snapped;
                _lastTransientShift = (transient - raw) * 1000f;
            }

            if (!_quantizedBeats.Contains(snapped))
                _quantizedBeats.Add(snapped);
        }

        _quantizedBeats.Sort();

        // Stage 3: chain density cap
        _chainDensityPruned = 0;
        if (enforceChainDensity)
            _quantizedBeats = EnforceChainDensity(_quantizedBeats);

        Debug.Log($"<color=cyan>QUANTIZED:</color> {_rawTaps.Count} taps → {_quantizedBeats.Count} beats. " +
                  $"Transient snap {(useTransientSnap ? "ON" : "OFF")}. " +
                  $"Chain pruned: {_chainDensityPruned}.");
    }

    // ─── Chain density enforcement ────────────────────────────────────────
    // Sliding-window pass: for each beat, count how many already-accepted beats
    // fall within the preceding chainInputWindowSec. If that count has already
    // reached the cap (floor(window/2)), drop this beat.
    private List<float> EnforceChainDensity(List<float> beats)
    {
        int maxPerWindow = Mathf.Max(1, Mathf.FloorToInt(chainInputWindowSec / 2f));
        var result = new List<float>();

        foreach (float beat in beats)   // beats is already sorted
        {
            int inWindow = 0;
            for (int i = result.Count - 1; i >= 0; i--)
            {
                if (beat - result[i] <= chainInputWindowSec) inWindow++;
                else break;   // sorted → no earlier beat can be in the window
            }

            if (inWindow < maxPerWindow)
                result.Add(beat);
            else
                _chainDensityPruned++;
        }

        return result;
    }

    // ─────────────────────────────────────────────────────────────────────
    private void OnGUI()
    {
        if (audioSource == null || audioSource.clip == null) return;
        if (_vizTex == null)
        {
            _vizTex = new Texture2D(1, 1);
            _vizTex.SetPixel(0, 0, Color.white);
            _vizTex.Apply();
        }

        DrawVisualizer();
        DrawInfoPanel();
        DrawControlPanel();
        DrawLoadPanel();
        DrawBackButton();
    }

    // ── Control panel (center-top) ────────────────────────────────────────
    private void DrawControlPanel()
    {
        float pw = 420f, py = 20f;
        float px = Screen.width / 2f - pw / 2f;

        GUI.Label(new Rect(px, py,      pw, 30f), "SMART QUANTIZE MAPPER",
            Style(22, FontStyle.Bold, Color.yellow, TextAnchor.MiddleCenter));
        GUI.Label(new Rect(px, py + 30, pw, 22f),
            $"BPM: {targetBPM}  |  Grid: 1/{quantizeDivisor}  ({(60f / targetBPM / quantizeDivisor) * 1000f:F0} ms/snap)  |  Transient: {(useTransientSnap ? $"±{transientSearchWindowMs:F0}ms" : "OFF")}",
            Style(11, FontStyle.Normal, new Color(0.85f, 0.85f, 0.85f), TextAnchor.MiddleCenter));

        GUI.color = _isRecording ? new Color(1f, 0.3f, 0.3f) : Color.white;
        if (GUI.Button(new Rect(px, py + 60, pw, 50f), _isRecording ? "■  STOP RECORDING" : "●  START TAP RECORD"))
        {
            _isRecording = !_isRecording;
            if (_isRecording)
            {
                _rawTaps.Clear();
                _quantizedBeats.Clear();
                _lastTapTime = -1f;
                _lastRawTime = _lastTransientTime = _lastFinalTime = -1f;
                audioSource.time = 0f;
                audioSource.Play();
            }
            else
            {
                audioSource.Stop();
                QuantizeTaps();
            }
        }
        GUI.color = Color.white;

        if (!_isRecording && _quantizedBeats.Count > 0)
        {
            GUI.color = Color.green;
            if (GUI.Button(new Rect(px, py + 120, pw, 40f), $"SAVE  {_quantizedBeats.Count}  QUANTIZED BEATS"))
                SaveCustomTrack();
            GUI.color = Color.white;
        }

        GUI.Label(new Rect(px, py + 170, pw, 20f),
            "SPACEBAR = tap to the beat  •  timing is auto-corrected",
            Style(11, FontStyle.Normal, new Color(0.5f, 0.5f, 0.5f), TextAnchor.MiddleCenter));
    }

    // ── Info panel (left side) ────────────────────────────────────────────
    private void DrawInfoPanel()
    {
        float x = 20f, y = 20f, w = 280f, lh = 21f;

        GUIStyle sec = Style(12, FontStyle.Bold,   new Color(0.35f, 1f, 0.75f));
        GUIStyle val = Style(12, FontStyle.Normal,  Color.white);
        GUIStyle dim = Style(11, FontStyle.Normal,  new Color(0.55f, 0.55f, 0.55f));

        // ── Audio ──
        GUI.Label(new Rect(x, y, w, lh), "[ AUDIO ]", sec);       y += lh;
        float clipLen = audioSource.clip.length;
        float cur     = audioSource.time;
        GUI.Label(new Rect(x, y, w, lh), $"Time :      {cur:F3} s  /  {clipLen:F3} s", val); y += lh;
        DrawBar(new Rect(x, y, w - 10f, 7f), cur / clipLen, new Color(0.2f, 0.85f, 0.4f), new Color(0.1f, 0.18f, 0.12f));
        y += 13f;
        GUI.Label(new Rect(x, y, w, lh), $"Remaining : {(clipLen - cur):F3} s", dim); y += lh + 8f;

        // ── Taps ──
        GUI.Label(new Rect(x, y, w, lh), "[ TAPS ]", sec);        y += lh;
        GUI.Label(new Rect(x, y, w, lh), $"Raw taps :          {_rawTaps.Count}", val); y += lh;
        GUI.Label(new Rect(x, y, w, lh), $"Quantized beats :   {_quantizedBeats.Count}", val); y += lh;
        if (_rawTaps.Count > 0)
        {
            GUI.Label(new Rect(x, y, w, lh), $"Duplicates dropped : {_rawTaps.Count - _quantizedBeats.Count}", dim);
            y += lh;
        }
        y += 8f;

        // ── Chain limit ──
        GUI.Label(new Rect(x, y, w, lh), "[ CHAIN LIMIT ]", sec); y += lh;
        int maxPerWindow = Mathf.Max(1, Mathf.FloorToInt(chainInputWindowSec / 2f));
        if (enforceChainDensity)
        {
            GUI.Label(new Rect(x, y, w, lh), $"Window :   {chainInputWindowSec:F1} s", val);          y += lh;
            GUI.Label(new Rect(x, y, w, lh), $"Max beats : {maxPerWindow}  (window ÷ 2)", val);       y += lh;
            if (!_isRecording && _chainDensityPruned > 0)
            {
                GUI.color = new Color(1f, 0.45f, 0.2f);
                GUI.Label(new Rect(x, y, w, lh), $"Pruned :   {_chainDensityPruned} beats removed", val);
                GUI.color = Color.white;
            }
            else if (!_isRecording)
            {
                GUI.color = Color.green;
                GUI.Label(new Rect(x, y, w, lh), "Pruned :   none — density OK", val);
                GUI.color = Color.white;
            }
            y += lh;
        }
        else
        {
            GUI.color = new Color(0.5f, 0.5f, 0.5f);
            GUI.Label(new Rect(x, y, w, lh), "OFF — no density cap applied", val);
            GUI.color = Color.white;
            y += lh;
        }
        y += 8f;

        // ── Grid ──
        GUI.Label(new Rect(x, y, w, lh), "[ GRID ]", sec);        y += lh;
        float beatInt  = 60f / targetBPM;
        float snapGrid = beatInt / quantizeDivisor;
        GUI.Label(new Rect(x, y, w, lh), $"Beat interval : {beatInt * 1000f:F1} ms", val);  y += lh;
        GUI.Label(new Rect(x, y, w, lh), $"Snap grid :     {snapGrid * 1000f:F1} ms  (1/{quantizeDivisor})", val); y += lh + 8f;

        // ── Last tap (live, while recording) ──
        if (_isRecording)
        {
            GUI.Label(new Rect(x, y, w, lh), "[ LAST TAP ]", sec); y += lh;
            if (_lastTapTime >= 0f)
            {
                GUI.Label(new Rect(x, y, w, lh), $"At :     {_lastTapTime:F3} s", val); y += lh;
                float   ms    = _lastTapOffset * 1000f;
                string  grade = Mathf.Abs(ms) < 20f ? "EXCELLENT" : Mathf.Abs(ms) < 50f ? "GOOD" : "BAD";
                Color   gc    = Mathf.Abs(ms) < 20f ? Color.green  : Mathf.Abs(ms) < 50f ? Color.yellow : Color.red;
                GUI.color = gc;
                GUI.Label(new Rect(x, y, w, lh), $"Grid offset : {ms:+0.0;-0.0} ms   [ {grade} ]", val);
                GUI.color = Color.white;
            }
            else
                GUI.Label(new Rect(x, y, w, lh), "No taps yet.", dim);
        }
        else if (_lastRawTime >= 0f)
        {
            // ── Pipeline breakdown (shown after Stop) ──
            GUI.Label(new Rect(x, y, w, lh), "[ LAST TAP PIPELINE ]", sec); y += lh;

            GUI.Label(new Rect(x, y, w, lh), $"① Raw tap :       {_lastRawTime:F3} s", val); y += lh;

            if (useTransientSnap)
            {
                float shiftMs = _lastTransientShift;
                Color sc = Mathf.Abs(shiftMs) < 5f ? new Color(0.5f, 0.5f, 0.5f)
                         : shiftMs < 0f             ? new Color(0.4f, 0.8f, 1f)
                                                    : new Color(1f, 0.75f, 0.2f);
                GUI.color = sc;
                GUI.Label(new Rect(x, y, w, lh),
                    $"② Transient snap : {_lastTransientTime:F3} s  ({shiftMs:+0.0;-0.0} ms)", val);
                GUI.color = Color.white;
                y += lh;
            }
            else
            {
                GUI.color = new Color(0.4f, 0.4f, 0.4f);
                GUI.Label(new Rect(x, y, w, lh), "② Transient snap : OFF", val);
                GUI.color = Color.white;
                y += lh;
            }

            GUI.color = Color.green;
            GUI.Label(new Rect(x, y, w, lh), $"③ Final (quantized) : {_lastFinalTime:F3} s", val);
            GUI.color = Color.white;
        }
    }

    // ── Spectrum visualizer (bottom center) ───────────────────────────────
    private void DrawVisualizer()
    {
        const int BARS = 32;
        float vizW   = Screen.width * 0.45f;
        float vizH   = 80f;
        float startX = Screen.width / 2f - vizW / 2f;
        float startY = Screen.height - vizH - 30f;
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

        GUI.Label(new Rect(startX, startY - 20f, vizW, 18f),
            flash ? "■  TAP" : "AUDIO SPECTRUM",
            Style(10, FontStyle.Bold, flash ? Color.red : new Color(0.3f, 1f, 0.4f), TextAnchor.MiddleCenter));
    }

    // ── Back button (bottom-left) ─────────────────────────────────────────
    private void DrawBackButton()
    {
        if (GUI.Button(new Rect(20f, Screen.height - 55f, 160f, 40f), "← BACK TO MENU"))
        {
            if (_isRecording)
            {
                audioSource.Stop();
                _isRecording = false;
            }
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

    // ── Load panel (right side) ───────────────────────────────────────────
    private void DrawLoadPanel()
    {
        float pw = 340f;
        float px = Screen.width - pw - 20f;
        float py = 20f;
        float lh = 22f;
        float y  = py;

        GUI.Label(new Rect(px, y, pw, lh), "[ LOAD AUDIO ]",
            Style(12, FontStyle.Bold, new Color(0.35f, 1f, 0.75f)));
        y += lh;

        // Current clip name
        string clipName = (audioSource != null && audioSource.clip != null)
            ? audioSource.clip.name
            : "none";
        GUI.Label(new Rect(px, y, pw, lh), $"Loaded: {clipName}",
            Style(11, FontStyle.Normal, Color.white));
        y += lh + 4f;

        // File path field
        GUI.Label(new Rect(px, y, pw, lh), "File path:",
            Style(11, FontStyle.Normal, new Color(0.7f, 0.7f, 0.7f)));
        y += lh;
        _filePath = GUI.TextField(new Rect(px, y, pw, 24f), _filePath,
            Style(10, FontStyle.Normal, Color.white));
        y += 28f;

        // Browse + Load buttons
        if (GUI.Button(new Rect(px, y, pw / 2f - 4f, 30f), "BROWSE..."))
            OpenFileDialog();

        GUI.color = new Color(0.3f, 0.8f, 1f);
        if (GUI.Button(new Rect(px + pw / 2f + 4f, y, pw / 2f - 4f, 30f), "LOAD"))
        {
            if (!string.IsNullOrWhiteSpace(_filePath))
                StartCoroutine(LoadAudioCoroutine(_filePath.Trim()));
        }
        GUI.color = Color.white;
        y += 36f;

        // Status line
        if (!string.IsNullOrEmpty(_loadStatus))
        {
            GUI.color = _loadStatusIsError ? Color.red : Color.green;
            GUI.Label(new Rect(px, y, pw, lh), _loadStatus,
                Style(11, FontStyle.Normal, _loadStatusIsError ? Color.red : Color.green));
            GUI.color = Color.white;
        }
        y += lh + 8f;

        // ── BPM section ───────────────────────────────────────────────────
        GUI.Label(new Rect(px, y, pw, lh), "[ BPM ]",
            Style(12, FontStyle.Bold, new Color(0.35f, 1f, 0.75f)));
        y += lh;

        // Manual input + nudge buttons
        GUI.Label(new Rect(px, y, 60f, lh), "BPM:",
            Style(11, FontStyle.Normal, new Color(0.7f, 0.7f, 0.7f)));
        _bpmInput = GUI.TextField(new Rect(px + 44f, y, 70f, 22f), _bpmInput,
            Style(11, FontStyle.Normal, Color.white));

        if (GUI.Button(new Rect(px + 120f, y, 28f, 22f), "-1"))
            ApplyBPMDelta(-1f);
        if (GUI.Button(new Rect(px + 152f, y, 28f, 22f), "+1"))
            ApplyBPMDelta(+1f);
        if (GUI.Button(new Rect(px + 184f, y, 36f, 22f), "-0.5"))
            ApplyBPMDelta(-0.5f);
        if (GUI.Button(new Rect(px + 224f, y, 36f, 22f), "+0.5"))
            ApplyBPMDelta(+0.5f);

        if (GUI.Button(new Rect(px + 264f, y, 56f, 22f), "SET"))
        {
            if (float.TryParse(_bpmInput, out float parsed) && parsed > 0f)
            {
                targetBPM  = Mathf.Round(parsed * 2f) / 2f;   // round to nearest 0.5
                _bpmInput  = targetBPM.ToString("F1");
                _bpmStatus = $"BPM set to {targetBPM:F1}";
            }
            else
            {
                _bpmStatus = "Invalid BPM — enter a positive number.";
            }
        }
        y += 28f;

        // Auto-detect button
        bool hasClip = audioSource != null && audioSource.clip != null;
        GUI.enabled = hasClip && !_detectingBPM && !_isRecording;
        GUI.color   = _detectingBPM ? Color.grey : new Color(1f, 0.75f, 0.2f);
        if (GUI.Button(new Rect(px, y, pw, 28f),
            _detectingBPM ? "DETECTING BPM..." : "AUTO-DETECT BPM FROM AUDIO"))
        {
            StartCoroutine(DetectBPMCoroutine());
        }
        GUI.color   = Color.white;
        GUI.enabled = true;
        y += 34f;

        // BPM status
        if (!string.IsNullOrEmpty(_bpmStatus))
        {
            GUI.Label(new Rect(px, y, pw, lh), _bpmStatus,
                Style(11, FontStyle.Normal, new Color(1f, 0.85f, 0.4f)));
        }
    }

    // ── BPM helpers ───────────────────────────────────────────────────────
    private void ApplyBPMDelta(float delta)
    {
        targetBPM  = Mathf.Max(20f, targetBPM + delta);
        _bpmInput  = targetBPM.ToString("F1");
        _bpmStatus = $"BPM set to {targetBPM:F1}";
    }

    // Onset-flux BPM detector.
    // 1. Splits the clip into ~10 ms RMS-energy frames.
    // 2. Computes spectral flux (positive energy rise) for each frame.
    // 3. Peaks in the flux signal are onset candidates.
    // 4. Builds an inter-onset interval (IOI) histogram over tempos 60–200 BPM.
    // 5. Picks the histogram bin with the highest accumulated weight.
    private IEnumerator DetectBPMCoroutine()
    {
        _detectingBPM = true;
        _bpmStatus    = "Analysing audio...";
        yield return null;   // let the UI repaint once

        AudioClip clip       = audioSource.clip;
        int       sampleRate = clip.frequency;
        int       channels   = clip.channels;

        // Load the full clip's PCM (may be large — yield after to stay responsive)
        float[] pcm = new float[clip.samples * channels];
        clip.GetData(pcm, 0);
        yield return null;

        // ~10 ms frames
        int frameLen = Mathf.Max(1, Mathf.RoundToInt(0.01f * sampleRate)) * channels;
        int frameCount = pcm.Length / frameLen;

        float prevRMS = 0f;
        var   fluxes  = new List<float>(frameCount);
        var   times   = new List<float>(frameCount);

        for (int f = 0; f < frameCount; f++)
        {
            float sum = 0f;
            int   start = f * frameLen;
            int   end   = Mathf.Min(start + frameLen, pcm.Length);
            for (int i = start; i < end; i++) sum += pcm[i] * pcm[i];
            float rms = Mathf.Sqrt(sum / (end - start));

            float flux = Mathf.Max(0f, rms - prevRMS);   // half-wave rectified
            fluxes.Add(flux);
            times.Add(f * frameLen / channels / (float)sampleRate);
            prevRMS = rms;

            // Yield every 2000 frames to avoid freezing
            if (f % 2000 == 0) yield return null;
        }

        // Simple peak-pick: flux[i] > flux[i-1] && flux[i] > flux[i+1] && > mean
        float mean = fluxes.Count > 0 ? fluxes.Average() : 0f;
        var   onsets = new List<float>();
        for (int i = 1; i < fluxes.Count - 1; i++)
        {
            if (fluxes[i] > fluxes[i - 1] && fluxes[i] > fluxes[i + 1] && fluxes[i] > mean)
                onsets.Add(times[i]);
        }

        if (onsets.Count < 4)
        {
            _bpmStatus    = "Not enough onsets detected — set BPM manually.";
            _detectingBPM = false;
            yield break;
        }

        // IOI histogram over 60–200 BPM (0.5 BPM bins)
        const float BPM_MIN   = 60f;
        const float BPM_MAX   = 200f;
        const float BPM_STEP  = 0.5f;
        int         bins      = Mathf.RoundToInt((BPM_MAX - BPM_MIN) / BPM_STEP) + 1;
        float[]     histogram = new float[bins];

        for (int i = 0; i < onsets.Count - 1; i++)
        {
            float ioi = onsets[i + 1] - onsets[i];
            if (ioi <= 0f) continue;

            float bpm = 60f / ioi;

            // Also vote for multiples/sub-multiples to help ambiguous tempos
            foreach (float mult in new[] { 0.5f, 1f, 2f })
            {
                float b = bpm * mult;
                if (b < BPM_MIN || b > BPM_MAX) continue;
                int bin = Mathf.RoundToInt((b - BPM_MIN) / BPM_STEP);
                if (bin >= 0 && bin < bins)
                    histogram[bin] += 1f / mult;   // weight fundamental highest
            }
        }

        // Gaussian-smooth the histogram (kernel width 3 bins)
        float[] smooth = new float[bins];
        float[] kernel = { 0.25f, 0.5f, 0.25f };
        for (int b = 1; b < bins - 1; b++)
            smooth[b] = histogram[b - 1] * kernel[0]
                      + histogram[b]     * kernel[1]
                      + histogram[b + 1] * kernel[2];

        int   bestBin = 0;
        float bestVal = -1f;
        for (int b = 0; b < bins; b++)
            if (smooth[b] > bestVal) { bestVal = smooth[b]; bestBin = b; }

        float detectedBPM = BPM_MIN + bestBin * BPM_STEP;
        targetBPM  = detectedBPM;
        _bpmInput  = targetBPM.ToString("F1");
        _bpmStatus = $"Detected: {targetBPM:F1} BPM  ({onsets.Count} onsets)";
        _detectingBPM = false;
    }

    // ── File dialog (Windows only, via PowerShell) ────────────────────────
    private void OpenFileDialog()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        var t = new System.Threading.Thread(() =>
        {
            const string ps = @"
Add-Type -AssemblyName System.Windows.Forms
$d = New-Object System.Windows.Forms.OpenFileDialog
$d.Title  = 'Select Audio File'
$d.Filter = 'Audio Files|*.mp3;*.wav;*.ogg;*.aiff;*.aif|All Files|*.*'
if ($d.ShowDialog() -eq 'OK') { Write-Output $d.FileName }
";
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName               = "powershell",
                Arguments              = $"-NoProfile -NonInteractive -Command \"{ps.Replace("\"", "\\\"")}\"",
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                CreateNoWindow         = true
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            string result = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit();
            if (!string.IsNullOrEmpty(result))
                _pendingFilePath = result;
        });
        t.Start();
#else
        _loadStatus = "Browse not supported on this platform — paste path manually.";
        _loadStatusIsError = true;
#endif
    }

    // ── Audio loader coroutine ────────────────────────────────────────────
    private IEnumerator LoadAudioCoroutine(string path)
    {
        _loadStatus = "Loading...";
        _loadStatusIsError = false;

        AudioType audioType = GetAudioTypeFromPath(path);
        if (audioType == AudioType.UNKNOWN)
        {
            _loadStatus = "Unsupported format. Use .mp3, .wav, .ogg, or .aiff";
            _loadStatusIsError = true;
            yield break;
        }

        string url = "file:///" + path.Replace("\\", "/");
        using UnityWebRequest req = UnityWebRequestMultimedia.GetAudioClip(url, audioType);
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            _loadStatus = $"Error: {req.error}";
            _loadStatusIsError = true;
            yield break;
        }

        AudioClip clip = DownloadHandlerAudioClip.GetContent(req);
        clip.name = Path.GetFileNameWithoutExtension(path);

        // Stop any in-progress recording before swapping clip
        if (_isRecording)
        {
            audioSource.Stop();
            _isRecording = false;
        }
        _rawTaps.Clear();
        _quantizedBeats.Clear();
        _lastTapTime = -1f;
        _lastRawTime = _lastTransientTime = _lastFinalTime = -1f;

        audioSource.clip = clip;
        _loadStatus = $"Loaded: {clip.name}  ({clip.length:F1}s)";
        _loadStatusIsError = false;
    }

    private static AudioType GetAudioTypeFromPath(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".mp3"          => AudioType.MPEG,
            ".wav"          => AudioType.WAV,
            ".ogg"          => AudioType.OGGVORBIS,
            ".aiff" or ".aif" => AudioType.AIFF,
            _               => AudioType.UNKNOWN
        };
    }

    // ─────────────────────────────────────────────────────────────────────
    private void SaveCustomTrack()
    {
        string data = string.Join("|", _quantizedBeats);
        string key  = "CustomMap_" + audioSource.clip.name;
        PlayerPrefs.SetString(key, data);
        PlayerPrefs.Save();
        Debug.Log($"<color=green>SAVED PERFECT MAP:</color> {key}");
    }
}
