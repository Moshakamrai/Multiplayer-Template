using UnityEngine;

/// Radial ring visualizer — bars arranged in a circle shooting outward from a center point.
/// Designed to sit behind/around a player as an energy aura effect.
///
/// Setup:
///   1. Add to an empty GameObject, position it behind/above your player.
///   2. Drag your music AudioSource into "Target Audio".
///   3. Choose Ring Plane: Horizontal (floor disc) or Vertical (halo facing camera).
///   4. Bars spawn on Play and react to the spectrum in real time.
///
/// Tip: Bloom post-processing on your URP volume makes this look dramatically better.
[DefaultExecutionOrder(100)]
public class BeatVisualizerRadial : MonoBehaviour
{
    public enum RingPlane { Horizontal, Vertical }

    [Header("Audio")]
    public AudioSource targetAudio;
    public BeatMap     beatMap;

    [Header("Ring Layout")]
    public RingPlane plane = RingPlane.Vertical;
    [Range(12, 128)] public int   barCount    = 64;
    [Range(0.2f, 8f)] public float ringRadius  = 1.8f;
    [Range(0.05f, 4f)] public float maxBarLength = 1.2f;
    [Range(0.01f, 0.5f)] public float minBarLength = 0.02f;
    public Vector2 barFootprint = new Vector2(0.05f, 0.05f);

    [Header("Rotation")]
    [Tooltip("Degrees per second. Negative = reverse. 0 = static.")]
    public float rotationSpeed = 10f;

    [Header("Spectrum")]
    [Range(5f, 80f)] public float amplitudeScale = 30f;

    [Header("Frequency Colors")]
    [Tooltip("Bass frequencies (low end of ring).")]
    public Color colorBass = new Color(0.90f, 0.03f, 0.02f);
    [Tooltip("Mid frequencies.")]
    public Color colorMid  = new Color(1.00f, 0.40f, 0.00f);
    [Tooltip("High frequencies (upper end of ring).")]
    public Color colorHigh = new Color(1.00f, 0.85f, 0.10f);
    [Range(0f, 10f)] public float emissionIntensity = 4f;

    [Header("Beat Detection")]
    [Range(0.005f, 0.3f)] public float beatThreshold = 0.05f;
    [Range(0.05f,  0.5f)] public float beatCooldown   = 0.18f;

    [Header("Attack Spike")]
    public Color spikeColor = new Color(1f, 0.95f, 0.85f);
    [Range(1f, 6f)]    public float spikeBoost     = 2.8f;
    [Range(2f, 20f)]   public float spikeDecaySpeed = 8f;
    [Range(0.02f, 0.3f)] public float spikeWindow  = 0.08f;

    [Header("Smoothing")]
    [Range(4f, 40f)] public float riseSpeed = 22f;
    [Range(1f, 12f)] public float fallSpeed  =  6f;

    // ── Runtime ───────────────────────────────────────────────────────────
    private float[]    _spectrum;
    private float[]    _bars;
    private Transform[] _pivots;   // one per bar, at center, rotated outward
    private Transform[] _meshes;   // cube child of pivot, offset by radius
    private Renderer[]  _rends;
    private MaterialPropertyBlock _mpb;
    private Material _mat;

    private float _cooldownTimer;
    private float _spikeBoostCur = 1f;
    private float _spikeGlow;
    private int   _nextBeatIdx;

    // ─────────────────────────────────────────────────────────────────────
    void Start()
    {
        _spectrum = new float[512];
        _bars     = new float[barCount];
        _mpb      = new MaterialPropertyBlock();

        if (targetAudio == null)
        {
            var analyzer = GameObject.FindWithTag("MusicAnalyzer");
            if (analyzer != null)
                targetAudio = analyzer.GetComponent<AudioSource>();
            else
                Debug.LogWarning("[BeatVisualizerRadial] No GameObject with tag 'MusicAnalyzer' found.");
        }

        BuildMaterial();
        SpawnBars();
    }

    void BuildMaterial()
    {
        Shader sh = Shader.Find("Universal Render Pipeline/Lit")
                 ?? Shader.Find("Standard");
        _mat = new Material(sh);
        _mat.EnableKeyword("_EMISSION");
        _mat.SetColor("_BaseColor", Color.white);
        _mat.SetColor("_Color",     Color.white);
    }

    void SpawnBars()
    {
        _pivots = new Transform[barCount];
        _meshes = new Transform[barCount];
        _rends  = new Renderer[barCount];

        for (int i = 0; i < barCount; i++)
        {
            float angle    = 360f / barCount * i;
            float angleRad = Mathf.Deg2Rad * angle;

            // Direction this bar points outward — depends on ring plane
            Vector3 outDir = plane == RingPlane.Horizontal
                ? new Vector3(Mathf.Sin(angleRad), 0f,  Mathf.Cos(angleRad))   // XZ disc
                : new Vector3(Mathf.Sin(angleRad), Mathf.Cos(angleRad), 0f);   // XY halo

            Vector3 upRef = plane == RingPlane.Horizontal ? Vector3.up : Vector3.forward;

            var pivot = new GameObject($"Pivot_{i}");
            pivot.transform.SetParent(transform);
            pivot.transform.localPosition = Vector3.zero;
            pivot.transform.localRotation = Quaternion.LookRotation(outDir, upRef);
            _pivots[i] = pivot.transform;

            var bar = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bar.name = $"Bar_{i}";
            bar.transform.SetParent(pivot.transform);
            Destroy(bar.GetComponent<Collider>());

            // Place bar so its inner edge sits on the ring radius
            bar.transform.localPosition = new Vector3(0f, 0f, ringRadius + minBarLength * 0.5f);
            bar.transform.localScale    = new Vector3(barFootprint.x, barFootprint.y, minBarLength);

            _meshes[i] = bar.transform;
            _rends[i]  = bar.GetComponent<Renderer>();
            _rends[i].material = _mat;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    void Update()
    {
        if (targetAudio == null || !targetAudio.isPlaying) return;

        targetAudio.GetSpectrumData(_spectrum, 0, FFTWindow.BlackmanHarris);

        // Slow drift rotation
        Vector3 axis = plane == RingPlane.Horizontal ? Vector3.up : Vector3.forward;
        transform.Rotate(axis, rotationSpeed * Time.deltaTime, Space.Self);

        UpdateBars();
        DetectAmbientBeat();
        TickBeatMapSpikes();
        ApplyToScene();

        _spikeBoostCur = Mathf.MoveTowards(_spikeBoostCur, 1f,  Time.deltaTime * spikeDecaySpeed * 0.5f);
        _spikeGlow     = Mathf.MoveTowards(_spikeGlow,     0f,  Time.deltaTime * spikeDecaySpeed);
        _cooldownTimer -= Time.deltaTime;
    }

    void UpdateBars()
    {
        int usable = _spectrum.Length / 2;

        for (int i = 0; i < barCount; i++)
        {
            int lo = Mathf.FloorToInt(Mathf.Pow((float)i       / barCount, 1.5f) * usable);
            int hi = Mathf.FloorToInt(Mathf.Pow((float)(i + 1) / barCount, 1.5f) * usable);
            hi = Mathf.Max(hi, lo + 1);

            float peak = 0f;
            for (int j = lo; j < hi && j < _spectrum.Length; j++)
                peak = Mathf.Max(peak, _spectrum[j]);

            float target = Mathf.Clamp01(peak * amplitudeScale);
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
            _spikeGlow     = 0.35f;
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
            float boosted = Mathf.Clamp01(_bars[i] * _spikeBoostCur);
            float length  = Mathf.Max(minBarLength, boosted * maxBarLength);

            // Frequency color gradient across the ring (bass → mid → high)
            float t = (float)i / barCount;
            Color freqCol = t < 0.5f
                ? Color.Lerp(colorBass, colorMid,  t * 2f)
                : Color.Lerp(colorMid,  colorHigh, (t - 0.5f) * 2f);

            // Dim unexcited bars, brighten active ones
            Color col      = Color.Lerp(freqCol * 0.15f, freqCol, boosted);
            Color emission = Color.Lerp(col, spikeColor, _spikeGlow) * emissionIntensity * Mathf.Max(0.1f, boosted);

            // Inner edge stays on ring, bar grows outward
            _meshes[i].localPosition = new Vector3(0f, 0f, ringRadius + length * 0.5f);
            _meshes[i].localScale    = new Vector3(barFootprint.x, barFootprint.y, length);

            _mpb.SetColor("_BaseColor",     col);
            _mpb.SetColor("_Color",         col);
            _mpb.SetColor("_EmissionColor", emission);
            _rends[i].SetPropertyBlock(_mpb);
        }
    }

    // ── Public API ────────────────────────────────────────────────────────

    /// Call from RhythmRoundManager when a chain fires.
    public void TriggerAttackSpike(float strength = 1f)
    {
        strength       = Mathf.Clamp01(strength);
        _spikeBoostCur = Mathf.Lerp(1f, spikeBoost, strength);
        _spikeGlow     = strength;
        _cooldownTimer = beatCooldown;
    }

    public void TriggerBeat(float strength = 1f)
    {
        _spikeGlow     = Mathf.Clamp01(strength) * 0.45f;
        _cooldownTimer = beatCooldown;
    }

    void OnDestroy()
    {
        if (_mat != null) Destroy(_mat);
    }
}
