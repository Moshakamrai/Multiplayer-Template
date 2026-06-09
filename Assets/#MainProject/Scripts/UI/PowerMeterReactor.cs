using UnityEngine;
using UnityEngine.UI;
using Mirror;

/// <summary>
/// Drives the Custom/UIPowerMeter material on the FILL image of the cyberpunk power
/// cone. Mirrors the GloveBeatGlow / FloorBeatColorizer pattern: reads
/// RhythmRoundManager.Instance each frame and pushes shader properties.
///
///   _FillAmount    — shout TIMING QUALITY. RegisterShout() stamps a new target
///                    (close-to-beat + loud = high); it eases up fast, decays slow.
///   _GlowIntensity — pulses on the beat and ramps as the next beat approaches.
///   _ColorShift    — a brief hue nudge on each beat.
///
/// Put this on the FILL Image GameObject (the one using PowerMeter_Fill.mat). It
/// instances the material so it never edits the shared asset. VoiceCommandManager
/// finds its OWN child meter and calls RegisterShout() when a shout queues a move.
///
/// Multiplayer-safe: this lives inside the player prefab, which spawns once per
/// player. On a REMOTE clone it disables itself in Start (so the opponent's
/// Overlay canvas never draws over the local view). No singleton — VoiceCommandManager
/// references its own child instance directly.
/// </summary>
[RequireComponent(typeof(Image))]
public class PowerMeterReactor : MonoBehaviour
{
    [Header("Local-only gating")]
    [Tooltip("The group holding the Fill + Border (and ideally the meter's Canvas). Disabled on REMOTE clones so their Overlay canvas never covers your screen. Leave empty to disable just this GameObject.")]
    [SerializeField] private GameObject meterGroup;

    // The group VR docking should move (the whole cone). Falls back to this object.
    public Transform MeterRoot => meterGroup != null ? meterGroup.transform : transform;

    [Header("Fill per grade (the on-beat shout rating)")]
    [Tooltip("BAD / off-beat shout.")]
    public float badFill = 0.25f;
    [Tooltip("GOOD-timed shout.")]
    public float goodFill = 0.6f;
    [Tooltip("EXCELLENT (tightest) shout — the max.")]
    public float excellentFill = 1.0f;

    [Header("Fill motion")]
    [Tooltip("Seconds to ease toward a new target. Bigger = smoother/slower rise.")]
    public float riseSmoothTime = 0.25f;
    [Tooltip("Seconds the meter HOLDS at its level after an attack before it starts draining.")]
    public float holdTime = 1.0f;
    [Tooltip("How fast the meter drains toward the idle floor once the hold ends (units/sec). Lower = slower fade.")]
    public float fillDecay = 0.12f;
    [Tooltip("Fill the meter sags to at rest (0 = empties fully).")]
    public float idleFloor = 0.0f;

    [Header("Glow — beat pulse")]
    public float baseGlow = 0.9f;
    [Tooltip("Seconds before the beat the glow starts ramping up.")]
    public float chargeLead = 0.6f;
    [Tooltip("Extra glow added by the approach ramp + pulse.")]
    public float approachGlow = 0.8f;
    [Tooltip("Extra glow added by the on-beat flash.")]
    public float flashGlow = 1.6f;
    public float pulseSpeedMax = 10f;
    public float flashDecay = 5f;

    [Header("Color shift on beat")]
    [Tooltip("Max hue rotation (0-1) right on the beat, eases out with the flash.")]
    public float colorShiftAmount = 0.12f;

    [Header("Debug")]
    [Tooltip("TEMP: ignore shouts/beat and just sweep the fill 0->1 so you can confirm the shader pipeline works. Turn OFF for real play.")]
    public bool debugOscillate = false;

    private Image _img;
    private Material _mat;
    private float _fillTarget;
    private float _fillCurrent;
    private float _fillVel;
    private float _holdTimer;
    private float _beatFlash;
    private float _lastBeat = -1f;

    static readonly int ID_Fill  = Shader.PropertyToID("_FillAmount");
    static readonly int ID_Glow  = Shader.PropertyToID("_GlowIntensity");
    static readonly int ID_Shift = Shader.PropertyToID("_ColorShift");

    void Start()
    {
        // If we're inside a networked player prefab, only the LOCAL player's meter
        // should exist. Hide remote clones so their Overlay canvas can't cover the
        // local view. (Outside a NetworkBehaviour — e.g. plain scene UI — nb is null
        // and the meter just runs.)
        var nb = GetComponentInParent<NetworkBehaviour>();
        if (nb != null && !nb.isLocalPlayer)
        {
            Debug.Log($"[PowerMeter] Remote clone ({name}) — disabling meter.");
            (meterGroup != null ? meterGroup : gameObject).SetActive(false);
            return;
        }

        _img = GetComponent<Image>();
        // Instance the material so we never write to the shared PowerMeter_Fill asset.
        _mat = Instantiate(_img.material);
        _img.material = _mat;
        _fillTarget = _fillCurrent = idleFloor;

        if (!_mat.HasProperty(ID_Fill))
        {
            Debug.LogError($"[PowerMeter] '{name}' is using shader '{_mat.shader.name}', which has NO _FillAmount. " +
                           $"This component must be on the FILL image whose Material = PowerMeter_Fill (Custom/UIPowerMeter). " +
                           $"Move it there — nothing will animate on this object.", this);
            return;
        }

        // Position the meter on the LEFT side of the screen (flat builds only).
        // VR positioning is handled by VRWorldHud docking.
        var rt = GetComponent<RectTransform>();
        if (rt != null && !UnityEngine.XR.XRSettings.isDeviceActive)
        {
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(24f, 0f);
        }

        var mask = GetComponentInParent<Mask>();
        var rectMask = GetComponentInParent<RectMask2D>();
        Debug.Log($"[PowerMeter] LIVE on '{name}'. Shader='{_mat.shader.name}', maskInParent={(mask != null || rectMask != null)}");
    }

    /// <summary>
    /// Stamp the meter from an on-beat shout's grade ("EXCELLENT" / "GOOD" / "BAD"),
    /// as judged server-side and delivered via PlayerCombat.TargetShowTimingFeedback.
    /// Each shout raises the meter to AT LEAST its grade; better play climbs higher.
    /// </summary>
    public void RegisterGrade(string rating)
    {
        float grade;
        switch (rating)
        {
            case "EXCELLENT": grade = excellentFill; break;
            case "GOOD":      grade = goodFill;      break;
            default:          grade = badFill;       break; // "BAD" / anything else
        }

        _fillTarget = Mathf.Clamp01(Mathf.Max(_fillTarget, grade));
        _holdTimer  = holdTime; // linger before draining
        _beatFlash  = 1f;       // pop the glow on the shout

        Debug.Log($"[PowerMeter] Grade '{rating}' -> fill {grade:F2} (target now {_fillTarget:F2})");
    }

    /// <summary>
    /// Stamp the meter from the raw combined power value (0.0–1.25).
    /// This reflects VR swing speed + mic shout volume combined.
    /// Called alongside RegisterGrade so the meter shows BOTH timing quality AND input power.
    /// </summary>
    public void RegisterPower(float power)
    {
        // Map 0.0–1.25 power onto the meter's 0.0–1.0 fill range
        float powerFill = Mathf.Clamp01(power / 1.25f);

        // Power fill can push the meter higher than the grade alone
        // (e.g. GOOD timing + max power = higher meter than EXCELLENT timing + weak power)
        _fillTarget = Mathf.Clamp01(Mathf.Max(_fillTarget, powerFill));
        _holdTimer  = holdTime;
        _beatFlash  = Mathf.Max(_beatFlash, 0.5f + powerFill * 0.5f); // stronger flash for more power

        Debug.Log($"[PowerMeter] Power {power:F2} -> fill {powerFill:F2} (target now {_fillTarget:F2})");
    }

    void Update()
    {
        if (_mat == null) return;

        if (debugOscillate)
        {
            _fillCurrent = Mathf.PingPong(Time.time * 0.4f, 1f);
            _mat.SetFloat(ID_Fill, _fillCurrent);
            _mat.SetFloat(ID_Glow, 1.5f);
            if (_img != null) _img.SetMaterialDirty();
            return;
        }

        var  mgr   = RhythmRoundManager.Instance;
        bool round = mgr != null && mgr.isRoundActive;

        // Fill: hold after each attack, then the target drains; current eases smoothly toward it.
        if (_holdTimer > 0f)
            _holdTimer -= Time.deltaTime;
        else
            _fillTarget = Mathf.MoveTowards(_fillTarget, idleFloor, fillDecay * Time.deltaTime);
        _fillCurrent = Mathf.SmoothDamp(_fillCurrent, _fillTarget, ref _fillVel, riseSmoothTime);

        // Glow: base + approach ramp/pulse + on-beat flash.
        float glow = baseGlow;
        if (round)
        {
            float beat = mgr.lastBeatFireTime;
            if (beat > 0f && !Mathf.Approximately(beat, _lastBeat))
            {
                _lastBeat  = beat;
                _beatFlash = 1f;
            }

            float toBeat = mgr.GetNextBeatTime() - mgr.GetCurrentTrackTime();
            float approach = (toBeat >= 0f && toBeat <= chargeLead && chargeLead > 0.001f)
                ? 1f - (toBeat / chargeLead) : 0f;
            if (approach > 0f)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * Mathf.Lerp(3f, pulseSpeedMax, approach) * Mathf.PI);
                glow += approachGlow * approach * pulse;
            }
        }

        if (_beatFlash > 0f)
            _beatFlash = Mathf.Max(0f, _beatFlash - Time.deltaTime * flashDecay);
        glow += flashGlow * _beatFlash;

        _mat.SetFloat(ID_Fill, _fillCurrent);
        _mat.SetFloat(ID_Glow, glow);
        _mat.SetFloat(ID_Shift, colorShiftAmount * _beatFlash);

        // If a Mask/RectMask2D is in the hierarchy, UGUI renders through a separate
        // materialForRendering copy; SetMaterialDirty forces it to pick up our values.
        if (_img != null) _img.SetMaterialDirty();
    }

    void OnDestroy()
    {
        if (_mat != null) Destroy(_mat);
    }
}
