using UnityEngine;

/// <summary>
/// Animates the 6 colored floor materials in sync with the rhythm round beat.
///
/// Drag the 6 shared materials (yellow/red/pink/teal/purple/blue slots) directly
/// into the Inspector fields. The script modifies _EmissionColor each frame — no
/// instances are created, so every floor tile using those materials updates together.
///
/// Three states:
///   IDLE      — each material breathes slowly at its own phase offset
///   WIND-UP   — materials charge up and flicker one-by-one (mat1→mat6) as the
///               beat approaches — a visible "incoming beat" countdown
///   HIT       — simultaneous burst + short stagger ripple, then decay back to idle
/// </summary>
public class FloorBeatColorizer : MonoBehaviour
{
    [Header("Floor Materials  (drag from Project window)")]
    public Material mat1; // Element 1 — yellow
    public Material mat2; // Element 2 — red
    public Material mat3; // Element 3 — pink
    public Material mat4; // Element 4 — teal
    public Material mat5; // Element 5 — purple
    public Material mat6; // Element 6 — blue

    [Header("Ghost in the Shell / Akira Palette")]
    [ColorUsage(false, true)] public Color color1 = new Color(0.00f, 1.00f, 0.27f); // acid green
    [ColorUsage(false, true)] public Color color2 = new Color(1.00f, 0.00f, 0.47f); // hot pink
    [ColorUsage(false, true)] public Color color3 = new Color(0.80f, 1.00f, 0.00f); // radioactive yellow-green
    [ColorUsage(false, true)] public Color color4 = new Color(0.20f, 0.00f, 0.80f); // deep indigo
    [ColorUsage(false, true)] public Color color5 = new Color(1.00f, 0.27f, 0.13f); // neon coral
    [ColorUsage(false, true)] public Color color6 = new Color(0.67f, 0.00f, 1.00f); // glitch purple

    [Header("Idle Breathing")]
    [Tooltip("Minimum emission multiplier during idle.")]
    public float idleMin = 0.25f;
    [Tooltip("Maximum emission multiplier during idle breathing peak.")]
    public float idleMax = 0.80f;
    [Tooltip("Breathing cycles per second. Each material is offset in phase.")]
    public float breatheSpeed = 1.0f;

    [Header("Beat Warning (wind-up charge)")]
    [Tooltip("Peak emission when a material is fully charged just before the beat.")]
    public float chargeMaxIntensity = 3.5f;
    [Tooltip("Flicker rate (Hz) while a material is charging up.")]
    public float flickerSpeed = 20f;

    [Header("Beat Hit")]
    [Tooltip("Emission burst multiplier when the beat fires.")]
    public float hitPeakIntensity = 9f;
    [Tooltip("How fast the burst decays back to idle. Higher = snappier flash.")]
    public float hitDecayRate = 6f;

    // ── private state ──────────────────────────────────────────────────────
    private Material[] _mats;
    private Color[]    _colors;

    private enum BeatState { Idle, WindUp, Hit }
    private BeatState _state = BeatState.Idle;

    private float _windUpStartTime;
    private float _windUpDuration  = 0.53f;
    private float _hitFlashTimer;              // 1 → 0 after beat fires
    private float _lastBeatTime    = -1f;
    private bool  _wasWindUpActive;

    // ──────────────────────────────────────────────────────────────────────
    void Start()
    {
        _mats   = new[] { mat1, mat2, mat3, mat4, mat5, mat6 };
        _colors = new[] { color1, color2, color3, color4, color5, color6 };

        foreach (var m in _mats)
            if (m != null) m.EnableKeyword("_EMISSION");

        var mgr = RhythmRoundManager.Instance;
        if (mgr != null) _windUpDuration = mgr.windUpTime;
    }

    void Update()
    {
        var  mgr         = RhythmRoundManager.Instance;
        bool roundActive = mgr != null && mgr.isRoundActive;

        // ── State machine ──────────────────────────────────────────────────
        if (roundActive)
        {
            bool windingUp = mgr.IsWindUpActive;

            if (windingUp && !_wasWindUpActive)
            {
                _state          = BeatState.WindUp;
                _windUpStartTime = Time.time;
                _windUpDuration  = mgr.windUpTime;
            }

            float beat = mgr.lastBeatFireTime;
            if (beat > 0f && !Mathf.Approximately(beat, _lastBeatTime))
            {
                _lastBeatTime  = beat;
                _state         = BeatState.Hit;
                _hitFlashTimer = 1f;
            }

            _wasWindUpActive = windingUp;
        }
        else
        {
            _state           = BeatState.Idle;
            _wasWindUpActive = false;
        }

        if (_state == BeatState.Hit)
        {
            _hitFlashTimer = Mathf.Max(0f, _hitFlashTimer - Time.deltaTime * hitDecayRate);
            if (_hitFlashTimer <= 0f) _state = BeatState.Idle;
        }

        // ── Per-material emission ──────────────────────────────────────────
        float t = Time.time;

        for (int i = 0; i < 6; i++)
        {
            if (_mats[i] == null) continue;

            Color  col;
            float  intensity;

            switch (_state)
            {
                // ── IDLE: gentle per-material breathing ────────────────────
                case BeatState.Idle:
                {
                    float phase   = (float)i / 6f * Mathf.PI * 2f;
                    float breath  = 0.5f + 0.5f * Mathf.Sin(t * breatheSpeed * Mathf.PI * 2f + phase);
                    intensity     = Mathf.Lerp(idleMin, idleMax, breath);
                    col           = _colors[i];
                    break;
                }

                // ── WIND-UP: cascade charge-up mat1 → mat6 ─────────────────
                // Each material has a staggered activation window.
                // mat1 starts charging immediately; mat6 doesn't start until
                // the wind-up is ~83 % done — creating a left-to-right countdown.
                case BeatState.WindUp:
                {
                    float elapsed  = t - _windUpStartTime;
                    float progress = Mathf.Clamp01(elapsed / _windUpDuration);

                    // Threshold at which THIS material begins charging (0 .. 5/6)
                    float activationT = (float)i / 6f;

                    if (progress < activationT)
                    {
                        // Not yet — idle breath
                        float phase  = (float)i / 6f * Mathf.PI * 2f;
                        float breath = 0.5f + 0.5f * Mathf.Sin(t * breatheSpeed * Mathf.PI * 2f + phase);
                        intensity    = Mathf.Lerp(idleMin, idleMax, breath);
                    }
                    else
                    {
                        // Charging: ramp toward max + high-frequency flicker
                        float chargeT  = Mathf.Clamp01((progress - activationT) / Mathf.Max(0.001f, 1f - activationT));
                        float flicker  = 0.65f + 0.35f * Mathf.Sin(t * flickerSpeed * (1f + i * 0.5f));
                        intensity      = Mathf.Lerp(idleMax, chargeMaxIntensity, chargeT) * flicker;
                    }

                    col = _colors[i];
                    break;
                }

                // ── HIT: burst flash with slight per-material stagger ───────
                default: // BeatState.Hit
                {
                    // Each subsequent material lags behind by a tiny amount
                    // so the burst feels like a fast ripple rather than one solid block.
                    float lag       = (float)i / 6f * 0.07f;
                    float localFlash = Mathf.Clamp01(_hitFlashTimer - lag * hitDecayRate);

                    intensity = Mathf.Lerp(idleMin, hitPeakIntensity, localFlash);

                    // At peak brightness, tint toward white for that blown-out look
                    col = Color.Lerp(_colors[i], Color.white, localFlash * 0.45f);
                    break;
                }
            }

            _mats[i].SetColor("_EmissionColor", col * intensity);
        }
    }
}
