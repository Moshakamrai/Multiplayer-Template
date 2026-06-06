using UnityEngine;

/// 3D world-space music visualizer — spawns two rows of cubes (left + right of arena)
/// that scale and glow in response to the audio spectrum and attack triggers.
///
/// Setup:
///   1. Add this component to an empty GameObject in your Level scene.
///   2. Position that GameObject at the center of your arena floor (y = 0).
///   3. Drag your music AudioSource into "Target Audio".
///   4. Hit Play. Bars spawn and react automatically.
///   5. (Optional) Assign a BeatMap for auto attack spikes.
///
/// Glow requires Bloom in your URP post-processing volume. Without it,
/// emission just brightens the bar color — still looks good, just no halo.
[DefaultExecutionOrder(100)]
public class BeatVisualizerWorld : MonoBehaviour
{
    [Header("Audio")]
    public AudioSource targetAudio;
    public BeatMap beatMap;

    [Header("Bar Layout")]
    [Range(8, 256)] public int barCount = 20;
    [Tooltip("Distance between bar centers along the row (Z axis).")]
    public float barSpacing = 0.1f;
    [Tooltip("Base width and depth of each bar cube. Height is driven by audio.")]
    public Vector2 barFootprint = new Vector2(0.05f, 0.05f);
    [Tooltip("Maximum height a bar can reach.")]
    public float maxBarHeight = 0.8f;
    [Tooltip("Minimum height so bars are always visible.")]
    public float minBarHeight = 0.04f;

    [Header("Waveform Look (Picture-1 style)")]
    [Tooltip("ON: bars grow symmetrically up AND down from a center line — the classic " +
             "soundwave strip. OFF: bars rise from the floor like an equalizer.")]
    public bool waveformMode = true;
    [Tooltip("Height of the waveform's horizontal center line (waveform mode only).")]
    public float waveCenterY = 1.5f;
    [Tooltip("Blends each bar toward its neighbours so the wave flows smoothly " +
             "instead of jumping. 0 = raw spectrum, 1 = very smooth.")]
    [Range(0f, 1f)] public float neighbourSmoothing = 0.55f;

    [Header("Positioning")]
    [Tooltip("X distance from this GameObject's origin to each bar row.")]
    public float sideOffset = 5f;
    [Tooltip("Y position of bar bases (floor level relative to this transform).")]
    public float floorY = 0f;
    public bool mirrorRightSide = true;

    [Header("Spectrum")]
    [Range(5f, 40f)] public float amplitudeScale = 22f;

    [Header("Bar Colors (URP)")]
    public Color barColorBase = new Color(0.85f, 0.04f, 0.02f);
    public Color barColorPeak = new Color(1.00f, 0.72f, 0.00f);
    [Tooltip("Emission multiplier — raise this for bloom glow. Requires Bloom post-process.")]
    [Range(0f, 8f)] public float emissionIntensity = 3f;

    [Header("Beat Detection (ambient)")]
    [Range(0.005f, 0.3f)] public float beatThreshold = 0.05f;
    [Range(0.05f, 0.5f)]  public float beatCooldown   = 0.18f;

    [Header("Attack Spike")]
    public Color spikeColor = new Color(1f, 0.92f, 0.25f);
    [Range(1f, 5f)]    public float spikeBarBoost   = 2.6f;
    [Range(2f, 20f)]   public float spikeDecaySpeed = 7f;
    [Range(0.02f, 0.3f)] public float spikeWindow   = 0.08f;

    [Header("Smoothing")]
    [Range(4f, 30f)] public float riseSpeed = 18f;
    [Range(1f, 10f)] public float fallSpeed  =  5f;

    // ── Runtime ───────────────────────────────────────────────────────────
    private float[]   _spectrum;
    private float[]   _bars;
    private float[]   _barsScratch; // neighbour-smoothing work buffer

    private Transform[]  _leftT,  _rightT;
    private Renderer[]   _leftR,  _rightR;
    private Vector3[]    _leftBase, _rightBase; // stored xz positions

    private MaterialPropertyBlock _mpb;
    private Material _sharedMat;

    private float _cooldownTimer;
    private float _spikeBoost = 1f;
    private float _spikeGlow  = 0f;
    private int   _nextBeatIdx;

    // ─────────────────────────────────────────────────────────────────────
    void Start()
    {
        _spectrum    = new float[512];
        _bars        = new float[barCount];
        _barsScratch = new float[barCount];
        _mpb         = new MaterialPropertyBlock();

        if (targetAudio == null)
            targetAudio = GetComponent<AudioSource>();

        BuildMaterial();
        SpawnBars();
    }

    void BuildMaterial()
    {
        // URP Lit with emission — falls back to Standard if URP not found
        Shader sh = Shader.Find("Universal Render Pipeline/Lit")
                 ?? Shader.Find("Standard");

        _sharedMat = new Material(sh);
        _sharedMat.EnableKeyword("_EMISSION");

        // URP Lit uses _BaseColor; Standard uses _Color — set both for safety
        _sharedMat.SetColor("_BaseColor", Color.white);
        _sharedMat.SetColor("_Color",     Color.white);
    }

    void SpawnBars()
    {
        _leftT     = new Transform[barCount];
        _rightT    = new Transform[barCount];
        _leftR     = new Renderer[barCount];
        _rightR    = new Renderer[barCount];
        _leftBase  = new Vector3[barCount];
        _rightBase = new Vector3[barCount];

        float totalLen = (barCount - 1) * barSpacing;
        float startZ   = -totalLen * 0.5f;

        for (int i = 0; i < barCount; i++)
        {
            float z = startZ + i * barSpacing;

            // Local offsets — so bars follow the parent when moved in Play mode
            _leftBase[i]  = new Vector3(-sideOffset, floorY, z);
            _rightBase[i] = new Vector3( sideOffset, floorY, z);

            _leftT[i]  = SpawnBar($"BarL_{i}", _leftBase[i],  out _leftR[i]);
            if (mirrorRightSide)
                _rightT[i] = SpawnBar($"BarR_{i}", _rightBase[i], out _rightR[i]);
        }
    }

    Transform SpawnBar(string barName, Vector3 basePos, out Renderer rend)
    {
        var go  = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = barName;
        go.transform.SetParent(transform);

        // Remove collider — these are visual only
        Destroy(go.GetComponent<Collider>());

        go.transform.position   = basePos;
        go.transform.localScale = new Vector3(barFootprint.x, minBarHeight, barFootprint.y);

        rend          = go.GetComponent<Renderer>();
        rend.material = _sharedMat;
        return go.transform;
    }

    // ─────────────────────────────────────────────────────────────────────
    void Update()
    {
        if (targetAudio == null || !targetAudio.isPlaying) return;

        targetAudio.GetSpectrumData(_spectrum, 0, FFTWindow.BlackmanHarris);

        UpdateBars();
        SmoothBarsSpatially();
        DetectAmbientBeat();
        TickBeatMapSpikes();
        ApplyToScene();

        _spikeBoost    = Mathf.MoveTowards(_spikeBoost, 1f,  Time.deltaTime * spikeDecaySpeed * 0.6f);
        _spikeGlow     = Mathf.MoveTowards(_spikeGlow,  0f,  Time.deltaTime * spikeDecaySpeed);
        _cooldownTimer -= Time.deltaTime;
    }

    void UpdateBars()
    {
        int usable = _spectrum.Length / 2;

        for (int i = 0; i < barCount; i++)
        {
            // Power-curve bin grouping: more visual weight on bass
            int lo = Mathf.FloorToInt(Mathf.Pow((float)i       / barCount, 1.6f) * usable);
            int hi = Mathf.FloorToInt(Mathf.Pow((float)(i + 1) / barCount, 1.6f) * usable);
            hi = Mathf.Max(hi, lo + 1);

            float peak = 0f;
            for (int j = lo; j < hi && j < _spectrum.Length; j++)
                peak = Mathf.Max(peak, _spectrum[j]);

            float target = Mathf.Clamp01(peak * amplitudeScale);
            float speed  = target > _bars[i] ? riseSpeed : fallSpeed;
            _bars[i]     = Mathf.Lerp(_bars[i], target, Time.deltaTime * speed);
        }
    }

    // Blends each bar toward the average of its neighbours so the heights form a
    // flowing wave envelope (picture-1 look) instead of jagged, independent spikes.
    void SmoothBarsSpatially()
    {
        if (neighbourSmoothing <= 0f) return;

        System.Array.Copy(_bars, _barsScratch, barCount);
        for (int i = 0; i < barCount; i++)
        {
            float l = _barsScratch[Mathf.Max(0, i - 1)];
            float c = _barsScratch[i];
            float r = _barsScratch[Mathf.Min(barCount - 1, i + 1)];
            float blurred = (l + c + r) / 3f;
            _bars[i] = Mathf.Lerp(c, blurred, neighbourSmoothing);
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
            _spikeGlow     = 0.4f;
            _cooldownTimer = beatCooldown;
        }
    }

    void TickBeatMapSpikes()
    {
        if (beatMap?.allBeats == null || _nextBeatIdx >= beatMap.allBeats.Count) return;

        var beat = beatMap.allBeats[_nextBeatIdx];
        if (targetAudio.time >= beat.time - spikeWindow)
        {
            TriggerAttackSpike(Mathf.Clamp01(beat.strength * 6f));
            _nextBeatIdx++;
        }
    }

    void ApplyToScene()
    {
        for (int i = 0; i < barCount; i++)
        {
            float boosted = Mathf.Clamp01(_bars[i] * _spikeBoost);
            float height  = Mathf.Max(minBarHeight, boosted * maxBarHeight);

            Color col      = Color.Lerp(barColorBase, barColorPeak, boosted);
            Color emission = Color.Lerp(col, spikeColor, _spikeGlow) * emissionIntensity;

            ApplyBar(_leftT[i], _leftR[i], _leftBase[i], height, col, emission);

            if (mirrorRightSide && _rightT[i] != null)
                ApplyBar(_rightT[i], _rightR[i], _rightBase[i], height, col, emission);
        }
    }

    void ApplyBar(Transform t, Renderer r, Vector3 basePos, float height, Color col, Color emission)
    {
        if (waveformMode)
            // Center-anchor: cube center sits on the wave line, so the bar grows
            // equally up AND down — the symmetric soundwave strip from picture 1.
            t.localPosition = new Vector3(basePos.x, waveCenterY, basePos.z);
        else
            // Bottom-anchor: cube pivot is center, so shift Y up by half height.
            t.localPosition = basePos + new Vector3(0f, height * 0.5f, 0f);

        t.localScale = new Vector3(barFootprint.x, height, barFootprint.y);

        // MaterialPropertyBlock avoids creating per-instance material copies
        _mpb.SetColor("_BaseColor",     col);      // URP Lit
        _mpb.SetColor("_Color",         col);      // Standard fallback
        _mpb.SetColor("_EmissionColor", emission);
        r.SetPropertyBlock(_mpb);
    }

    // ── Public API ────────────────────────────────────────────────────────

    /// Call from RhythmRoundManager when an attack chain fires.
    /// strength: 0–1 (EXCELLENT = 1.0, GOOD = 0.6)
    public void TriggerAttackSpike(float strength = 1f)
    {
        strength    = Mathf.Clamp01(strength);
        _spikeBoost = Mathf.Lerp(1f, spikeBarBoost, strength);
        _spikeGlow  = strength;
        _cooldownTimer = beatCooldown;
    }

    /// Lighter ambient pulse — call on GOOD hits or rhythm confirmation.
    public void TriggerBeat(float strength = 1f)
    {
        _spikeGlow     = Mathf.Clamp01(strength) * 0.5f;
        _cooldownTimer = beatCooldown;
    }

    void OnDestroy()
    {
        if (_sharedMat != null)
            Destroy(_sharedMat);
    }
}
