using UnityEngine;

/// Draws reactive side-bar spectrum strips, beat-pulse edge glow, and attack-trigger spikes.
///
/// Setup:
///   1. Add to any active GameObject in your Level scene.
///   2. Drag your music AudioSource into "Target Audio".
///   3. (Optional) Drag a BeatMap asset into "Beat Map" for auto attack spikes.
///   4. Call TriggerAttackSpike() from RhythmRoundManager when a chain fires.
[DefaultExecutionOrder(100)]
public class BeatVisualizer : MonoBehaviour
{
    [Header("Audio")]
    public AudioSource targetAudio;

    [Header("Beat Map (optional — auto-spikes on attack triggers)")]
    public BeatMap beatMap;

    [Header("Side Bars")]
    [Range(16, 64)] public int barCount = 32;
    [Range(30f, 400f)] public float maxBarLength = 140f;
    [Range(5f, 30f)]  public float barAmplitudeScale = 18f;
    public Color barColorBase = new Color(0.85f, 0.05f, 0.03f, 0.90f);
    public Color barColorPeak = new Color(1.00f, 0.72f, 0.00f, 1.00f);

    [Header("Beat Detection (ambient pulse)")]
    [Range(0.005f, 0.3f)] public float beatThreshold = 0.05f;
    [Range(0.05f,  0.5f)] public float beatCooldown   = 0.18f;

    [Header("Ambient Beat Flash")]
    public Color flashColor = new Color(1f, 0.30f, 0.00f, 0.15f);
    [Range(1f, 10f)] public float flashDecaySpeed = 5f;

    [Header("Edge Glow (ambient)")]
    public Color edgeColor = new Color(0.75f, 0.03f, 0.00f, 0.55f);
    [Range(10f, 150f)] public float edgeThickness  = 55f;
    [Range(1f,  8f)]   public float edgeDecaySpeed = 2.5f;

    [Header("Attack Spike")]
    public Color spikeFlashColor = new Color(1f, 0.85f, 0.10f, 0.35f);
    public Color spikeEdgeColor  = new Color(1f, 0.60f, 0.00f, 0.90f);
    [Range(1f, 5f)]   public float spikeBarBoost   = 2.8f;   // multiplier on bar scale during spike
    [Range(2f, 20f)]  public float spikeDecaySpeed = 8f;
    [Range(0.05f, 0.4f)] public float spikeWindow  = 0.08f;  // seconds around a beat.time to auto-fire

    [Header("Bar Smoothing")]
    [Range(4f, 25f)] public float riseSpeed = 14f;
    [Range(1f, 10f)] public float fallSpeed  =  5f;

    // ── Runtime ───────────────────────────────────────────────────────────
    private float[]   _spectrum;
    private float[]   _bars;
    private float     _cooldownTimer;

    private float _flashAlpha;
    private float _edgeAlpha;

    private float _spikeAlpha;       // drives spike flash + edge
    private float _spikeBarBoostCur; // current bar scale multiplier (decays to 1)

    private int   _nextBeatIdx;      // index into beatMap.allBeats for auto-spike
    private Texture2D _px;

    // ─────────────────────────────────────────────────────────────────────
    void Awake()
    {
        _spectrum       = new float[512];
        _bars           = new float[barCount];
        _spikeBarBoostCur = 1f;

        if (targetAudio == null)
            targetAudio = GetComponent<AudioSource>();
    }

    void Update()
    {
        if (targetAudio == null || !targetAudio.isPlaying) return;

        targetAudio.GetSpectrumData(_spectrum, 0, FFTWindow.BlackmanHarris);

        UpdateBars();
        DetectAmbientBeat();
        TickBeatMapSpikes();

        _flashAlpha       = Mathf.MoveTowards(_flashAlpha,       0f, Time.deltaTime * flashDecaySpeed);
        _edgeAlpha        = Mathf.MoveTowards(_edgeAlpha,        0f, Time.deltaTime * edgeDecaySpeed);
        _spikeAlpha       = Mathf.MoveTowards(_spikeAlpha,       0f, Time.deltaTime * spikeDecaySpeed);
        _spikeBarBoostCur = Mathf.MoveTowards(_spikeBarBoostCur, 1f, Time.deltaTime * spikeDecaySpeed * 1.5f);
        _cooldownTimer   -= Time.deltaTime;
    }

    // ── Public API ────────────────────────────────────────────────────────

    /// Call when an attack chain fires (from RhythmRoundManager).
    /// strength: 0–1. GOOD = 0.6, EXCELLENT = 1.0
    public void TriggerAttackSpike(float strength = 1f)
    {
        strength          = Mathf.Clamp01(strength);
        _spikeAlpha       = strength;
        _spikeBarBoostCur = Mathf.Lerp(1f, spikeBarBoost, strength);
        _cooldownTimer    = beatCooldown;
    }

    /// Simpler ambient pulse — used internally and can be called on grade feedback.
    public void TriggerBeat(float strength = 1f)
    {
        strength       = Mathf.Clamp01(strength);
        _flashAlpha    = strength;
        _edgeAlpha     = strength;
        _cooldownTimer = beatCooldown;
    }

    // ─────────────────────────────────────────────────────────────────────
    void UpdateBars()
    {
        int usableBins = _spectrum.Length / 2;

        for (int i = 0; i < barCount; i++)
        {
            int lo = Mathf.FloorToInt(Mathf.Pow((float)i       / barCount, 1.6f) * usableBins);
            int hi = Mathf.FloorToInt(Mathf.Pow((float)(i + 1) / barCount, 1.6f) * usableBins);
            hi = Mathf.Max(hi, lo + 1);

            float peak = 0f;
            for (int j = lo; j < hi && j < _spectrum.Length; j++)
                peak = Mathf.Max(peak, _spectrum[j]);

            float target = Mathf.Clamp01(peak * barAmplitudeScale);
            float speed  = target > _bars[i] ? riseSpeed : fallSpeed;
            _bars[i]     = Mathf.Lerp(_bars[i], target, Time.deltaTime * speed);
        }
    }

    void DetectAmbientBeat()
    {
        if (_cooldownTimer > 0f) return;

        float bass = 0f;
        for (int i = 0; i < 5; i++) bass += _spectrum[i];
        bass /= 5f;

        if (bass > beatThreshold)
        {
            _flashAlpha    = 1f;
            _edgeAlpha     = 1f;
            _cooldownTimer = beatCooldown;
        }
    }

    // Auto-fire a spike when audio playhead crosses a beat trigger in the BeatMap
    void TickBeatMapSpikes()
    {
        if (beatMap == null || beatMap.allBeats == null) return;
        if (_nextBeatIdx >= beatMap.allBeats.Count) return;

        float now = targetAudio.time;
        var   beat = beatMap.allBeats[_nextBeatIdx];

        if (now >= beat.time - spikeWindow)
        {
            TriggerAttackSpike(Mathf.Clamp01(beat.strength * 6f));
            _nextBeatIdx++;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    void OnGUI()
    {
        if (RhythmRoundManager.Instance != null && RhythmRoundManager.Instance.isShopPhase) return;
        if (_px == null)
        {
            _px = new Texture2D(1, 1);
            _px.SetPixel(0, 0, Color.white);
            _px.Apply();
        }

        if (targetAudio == null || !targetAudio.isPlaying) return;

        float sw   = Screen.width;
        float sh   = Screen.height;
        float slot = sh / barCount;

        bool spiking = _spikeAlpha > 0.01f;

        // ── Side bars ─────────────────────────────────────────────────
        for (int i = 0; i < barCount; i++)
        {
            float boosted = Mathf.Clamp01(_bars[i] * _spikeBarBoostCur);
            if (boosted < 0.005f) continue;

            float len = boosted * maxBarLength;
            float y   = i * slot;
            float h   = Mathf.Max(1f, slot - 1f);

            // During spike: bars shift toward spike color (gold/white)
            Color col = spiking
                ? Color.Lerp(Color.Lerp(barColorBase, barColorPeak, boosted),
                             spikeFlashColor, _spikeAlpha * 0.5f)
                : Color.Lerp(barColorBase, barColorPeak, boosted);

            GUI.color = col;
            GUI.DrawTexture(new Rect(0,        y, len, h), _px);
            GUI.DrawTexture(new Rect(sw - len, y, len, h), _px);
        }

        // ── Edge glow ─────────────────────────────────────────────────
        float edgeA = Mathf.Max(_edgeAlpha, _spikeAlpha);
        if (edgeA > 0.01f)
        {
            Color ec = spiking
                ? Color.Lerp(edgeColor, spikeEdgeColor, _spikeAlpha)
                : edgeColor;
            ec.a = (spiking ? spikeEdgeColor.a : edgeColor.a) * edgeA;

            float t = spiking
                ? edgeThickness * Mathf.Lerp(1f, 1.6f, _spikeAlpha)
                : edgeThickness;

            GUI.color = ec;
            GUI.DrawTexture(new Rect(0,      0,      sw, t), _px);
            GUI.DrawTexture(new Rect(0,      sh - t, sw, t), _px);
            GUI.DrawTexture(new Rect(0,      0,      t,  sh), _px);
            GUI.DrawTexture(new Rect(sw - t, 0,      t,  sh), _px);
        }

        // ── Full-screen flash ─────────────────────────────────────────
        float flashA = Mathf.Max(_flashAlpha, _spikeAlpha);
        if (flashA > 0.01f)
        {
            Color fc = spiking
                ? Color.Lerp(flashColor, spikeFlashColor, _spikeAlpha)
                : flashColor;
            fc.a = fc.a * flashA;
            GUI.color = fc;
            GUI.DrawTexture(new Rect(0, 0, sw, sh), _px);
        }

        GUI.color = Color.white;
    }
}
