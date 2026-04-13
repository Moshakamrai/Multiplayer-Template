using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class SmartBeatMapper : MonoBehaviour
{
    public AudioSource audioSource;

    [Header("Quantize Settings (FL Studio Style)")]
    public float targetBPM = 100f;

    [Tooltip("1 = Full Beats (0.6s), 2 = Half Beats (0.3s), 4 = 16th Notes")]
    public int quantizeDivisor = 2;

    private List<float> _rawTaps       = new List<float>();
    private List<float> _quantizedBeats = new List<float>();
    private bool        _isRecording   = false;

    // ── Visualizer ────────────────────────────────────────────────────────
    private float      _tapFlashTimer = 0f;
    private const float FLASH_DURATION = 0.15f;
    private float[]    _spectrumData  = new float[64];
    private Texture2D  _vizTex;

    // ── Last-tap feedback ─────────────────────────────────────────────────
    private float _lastTapTime   = -1f;
    private float _lastTapOffset = 0f;

    // ─────────────────────────────────────────────────────────────────────
    void Update()
    {
        if (_tapFlashTimer > 0f)
            _tapFlashTimer -= Time.deltaTime;

        if (!_isRecording || audioSource == null || !audioSource.isPlaying) return;

        if (Input.GetKeyDown(KeyCode.Space))
        {
            float t = audioSource.time;
            _rawTaps.Add(t);
            _lastTapTime = t;

            float snapGrid    = (60f / targetBPM) / quantizeDivisor;
            _lastTapOffset    = t - Mathf.Round(t / snapGrid) * snapGrid;
            _tapFlashTimer    = FLASH_DURATION;

            Debug.Log($"Raw Tap: {t:F3}s | Offset: {_lastTapOffset * 1000f:+0.0;-0.0}ms");
        }

        audioSource.GetSpectrumData(_spectrumData, 0, FFTWindow.BlackmanHarris);
    }

    // ─────────────────────────────────────────────────────────────────────
    private void QuantizeTaps()
    {
        _quantizedBeats.Clear();
        if (_rawTaps.Count == 0) return;

        float snapGrid = (60f / targetBPM) / quantizeDivisor;
        foreach (float tap in _rawTaps)
        {
            float snapped = Mathf.Round(tap / snapGrid) * snapGrid;
            if (!_quantizedBeats.Contains(snapped))
                _quantizedBeats.Add(snapped);
        }

        _quantizedBeats.Sort();
        Debug.Log($"<color=cyan>QUANTIZED:</color> {_rawTaps.Count} raw taps → {_quantizedBeats.Count} beats.");
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
    }

    // ── Control panel (center-top) ────────────────────────────────────────
    private void DrawControlPanel()
    {
        float pw = 420f, py = 20f;
        float px = Screen.width / 2f - pw / 2f;

        GUI.Label(new Rect(px, py,      pw, 30f), "SMART QUANTIZE MAPPER",
            Style(22, FontStyle.Bold, Color.yellow, TextAnchor.MiddleCenter));
        GUI.Label(new Rect(px, py + 30, pw, 22f),
            $"BPM: {targetBPM}  |  Grid: 1/{quantizeDivisor}  ({(60f / targetBPM / quantizeDivisor) * 1000f:F0} ms/snap)",
            Style(12, FontStyle.Normal, new Color(0.85f, 0.85f, 0.85f), TextAnchor.MiddleCenter));

        GUI.color = _isRecording ? new Color(1f, 0.3f, 0.3f) : Color.white;
        if (GUI.Button(new Rect(px, py + 60, pw, 50f), _isRecording ? "■  STOP RECORDING" : "●  START TAP RECORD"))
        {
            _isRecording = !_isRecording;
            if (_isRecording)
            {
                _rawTaps.Clear();
                _quantizedBeats.Clear();
                _lastTapTime = -1f;
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
        float x = 20f, y = 20f, w = 270f, lh = 21f;

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
        GUI.Label(new Rect(x, y, w, lh), $"Remaining : {(clipLen - cur):F3} s", dim);        y += lh + 8f;

        // ── Taps ──
        GUI.Label(new Rect(x, y, w, lh), "[ TAPS ]", sec);        y += lh;
        GUI.Label(new Rect(x, y, w, lh), $"Raw taps :          {_rawTaps.Count}", val);       y += lh;
        GUI.Label(new Rect(x, y, w, lh), $"Quantized beats :   {_quantizedBeats.Count}", val); y += lh;
        if (_rawTaps.Count > 0)
        {
            GUI.Label(new Rect(x, y, w, lh), $"Duplicates dropped : {_rawTaps.Count - _quantizedBeats.Count}", dim);
            y += lh;
        }
        y += 8f;

        // ── Grid ──
        GUI.Label(new Rect(x, y, w, lh), "[ GRID ]", sec);        y += lh;
        float beatInt  = 60f / targetBPM;
        float snapGrid = beatInt / quantizeDivisor;
        GUI.Label(new Rect(x, y, w, lh), $"Beat interval : {beatInt * 1000f:F1} ms", val);    y += lh;
        GUI.Label(new Rect(x, y, w, lh), $"Snap grid :     {snapGrid * 1000f:F1} ms  (1/{quantizeDivisor})", val); y += lh + 8f;

        // ── Last tap ──
        GUI.Label(new Rect(x, y, w, lh), "[ LAST TAP ]", sec);    y += lh;
        if (_lastTapTime >= 0f)
        {
            GUI.Label(new Rect(x, y, w, lh), $"At :     {_lastTapTime:F3} s", val); y += lh;
            float   ms    = _lastTapOffset * 1000f;
            string  grade = Mathf.Abs(ms) < 20f ? "EXCELLENT" : Mathf.Abs(ms) < 50f ? "GOOD" : "BAD";
            Color   gc    = Mathf.Abs(ms) < 20f ? Color.green  : Mathf.Abs(ms) < 50f ? Color.yellow : Color.red;
            GUI.color = gc;
            GUI.Label(new Rect(x, y, w, lh), $"Offset : {ms:+0.0;-0.0} ms   [ {grade} ]", val);
            GUI.color = Color.white;
        }
        else
        {
            GUI.Label(new Rect(x, y, w, lh), "No taps yet.", dim);
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
        Color active = flash ? Color.red                         : new Color(0.15f, 0.95f, 0.35f);
        Color bg     = flash ? new Color(0.28f, 0.04f, 0.04f)   : new Color(0f, 0.18f, 0.08f);

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
