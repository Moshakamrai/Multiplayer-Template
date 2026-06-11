using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drives the emission on a fighter's GLOVE materials for combat juice:
///   • CHARGE  — gloves glow + pulse, ramping up to the beat (and pulsing faster as it nears).
///   • STRIKE  — a bright white-hot burst when this fighter LANDS a hit.
///   • HURT    — a red burst when this fighter GETS hit.
///
/// Put this on the root player object (same GameObject as PlayerCombat) and drag the glove
/// Renderers into the list. It instances the glove materials so each fighter glows independently
/// and never edits the shared asset. Emission-only — no custom shader needed (URP Lit/Standard
/// both have _EmissionColor).
/// </summary>
public class GloveBeatGlow : MonoBehaviour
{
    [Header("Glove Renderers (drag the glove objects)")]
    public Renderer[] gloveRenderers;

    [Header("Active-Hand Card Cue (VR)")]
    [Tooltip("RIGHT glove renderers — glow when a Strike/Throw card is pending (or Support).")]
    public Renderer[] rightGloveRenderers;
    [Tooltip("LEFT glove renderers — glow when a Block/Parry card is pending (or Support).")]
    public Renderer[] leftGloveRenderers;
    [Tooltip("Family colors for the active-hand cue.")]
    [ColorUsage(false, true)] public Color offenseColor = new Color(1.0f, 0.35f, 0.05f); // Strike/Throw = red-orange
    [ColorUsage(false, true)] public Color defenseColor = new Color(0.1f, 0.55f, 1.0f);  // Block/Parry = blue
    [ColorUsage(false, true)] public Color supportColor = new Color(1.0f, 1.0f, 1.0f);   // Support = white
    [Tooltip("Brightness of the active glove's pulse.")]
    public float activeGlowIntensity = 2.2f;
    [Tooltip("Brightness the INACTIVE glove idles at (dim, so it doesn't look broken/off).")]
    public float idleGlowIntensity = 0.25f;
    [Tooltip("Pulse speed of the active-hand cue.")]
    public float cuePulseSpeed = 3.0f;

    [Header("Beat Charge")]
    [ColorUsage(false, true)] public Color chargeColor = new Color(0.0f, 0.85f, 1.0f); // cyan
    public float chargeIntensity = 1.0f;
    [Tooltip("Seconds before the beat the gloves start charging.")]
    public float chargeLead = 0.6f;
    public float chargePulseMax = 12f;

    [Header("Strike / Hurt Bursts")]
    [ColorUsage(false, true)] public Color strikeFlashColor = new Color(1.0f, 0.85f, 0.5f); // warm white-hot
    [ColorUsage(false, true)] public Color hurtFlashColor   = new Color(1.0f, 0.15f, 0.10f); // red
    public float flashIntensity = 4f;
    public float flashDecay     = 6f;

    private Material[] _mats;
    private Material[] _rightMats;
    private Material[] _leftMats;
    private float _flashTimer;
    private Color _flashColor;
    private PlayerCombat _localCombat;   // resolved lazily; only the LOCAL player drives the cue
    private CardManager  _cardManager;
    static readonly int ID_Emission = Shader.PropertyToID("_EmissionColor");

    void Awake()
    {
        _localCombat = GetComponent<PlayerCombat>();
        _cardManager = GetComponent<CardManager>();

        // If the per-hand renderer lists weren't assigned in the Inspector, auto-resolve them from
        // VRHands' leftGlove / rightGlove so the card cue works with zero extra setup.
        if ((rightGloveRenderers == null || rightGloveRenderers.Length == 0) &&
            (leftGloveRenderers  == null || leftGloveRenderers.Length  == 0))
        {
            var hands = GetComponent<VRHands>();
            if (hands != null)
            {
                if (hands.rightGlove != null)
                    rightGloveRenderers = hands.rightGlove.GetComponentsInChildren<Renderer>(true);
                if (hands.leftGlove != null)
                    leftGloveRenderers = hands.leftGlove.GetComponentsInChildren<Renderer>(true);
                Debug.Log($"[GloveCue] auto-resolved hand renderers from VRHands — " +
                          $"right={(rightGloveRenderers?.Length ?? 0)} left={(leftGloveRenderers?.Length ?? 0)}", this);
            }
        }

        _mats      = InstanceMats(gloveRenderers);
        _rightMats = InstanceMats(rightGloveRenderers);
        _leftMats  = InstanceMats(leftGloveRenderers);
    }

    private static Material[] InstanceMats(Renderer[] renderers)
    {
        var list = new List<Material>();
        if (renderers != null)
            foreach (var r in renderers)
                if (r != null)
                {
                    r.material.EnableKeyword("_EMISSION"); // r.material auto-instances (no shared-asset edit)
                    list.Add(r.material);
                }
        return list.ToArray();
    }

    public void FlashStrike() => Flash(strikeFlashColor);
    public void FlashHurt()   => Flash(hurtFlashColor);

    private void Flash(Color c)
    {
        _flashColor = c;
        _flashTimer = 1f;
    }

    void Update()
    {
        if (_mats == null || _mats.Length == 0) return;

        var mgr = RhythmRoundManager.Instance;
        bool round = mgr != null && mgr.isRoundActive;

        // Charge ramp toward the next beat.
        float charge = 0f;
        if (round)
        {
            float toBeat = mgr.GetNextBeatTime() - mgr.GetCurrentTrackTime();
            if (toBeat >= 0f && toBeat <= chargeLead && chargeLead > 0.001f)
                charge = 1f - (toBeat / chargeLead);
        }

        if (_flashTimer > 0f)
            _flashTimer = Mathf.Max(0f, _flashTimer - Time.deltaTime * flashDecay);

        Color emis;
        if (_flashTimer > 0f)
        {
            emis = _flashColor * Mathf.Lerp(chargeIntensity * 0.3f, flashIntensity, _flashTimer);
        }
        else if (charge > 0f)
        {
            float pulse = 0.7f + 0.3f * Mathf.Sin(Time.time * Mathf.Lerp(3f, chargePulseMax, charge) * Mathf.PI);
            emis = chargeColor * (chargeIntensity * charge * pulse);
        }
        else
        {
            emis = Color.black; // idle: gloves dark
        }

        foreach (var m in _mats)
            if (m != null) m.SetColor(ID_Emission, emis);

        UpdateActiveHandCue();
    }

    /// VR: glow the controller the player should use for the pending card — RIGHT for Strike/Throw,
    /// LEFT for Block/Parry, EITHER for Support. The other glove keeps a dim idle glow. Only the
    /// LOCAL player drives this (remote clones / flat-screen leave the per-hand mats untouched).
    private bool _cueDiag2;
    private string _lastCueMove = "\0";
    private void UpdateActiveHandCue()
    {
        if (!_cueDiag2)
        {
            _cueDiag2 = true;
            int rawR = rightGloveRenderers != null ? rightGloveRenderers.Length : -1;
            int rawL = leftGloveRenderers  != null ? leftGloveRenderers.Length  : -1;
            Debug.Log($"[GloveCue] obj='{gameObject.name}' rightMats={_rightMats.Length} leftMats={_leftMats.Length} " +
                      $"(rawRightList={rawR} rawLeftList={rawL}) isLocal={(_localCombat != null && _localCombat.isLocalPlayer)} " +
                      $"hasVRHands={(GetComponent<VRHands>() != null)}", this);
        }

        if (_rightMats.Length == 0 && _leftMats.Length == 0) return;
        // Runs on VR + flat. Only the LOCAL player's gloves cue (remote clones / bot are left dark).
        if (_localCombat == null || !_localCombat.isLocalPlayer) return;

        string move = _localCombat.PendingMoveTrigger;
        if (move != _lastCueMove) { _lastCueMove = move; Debug.Log($"[GloveCue] pending move -> '{move}'", this); }

        Color rightEmis = Color.black;
        Color leftEmis  = Color.black;

        if (!string.IsNullOrEmpty(move) && _cardManager != null)
        {
            CardFamily fam = _cardManager.FamilyOfTrigger(move);
            bool offense = fam == CardFamily.Strike || fam == CardFamily.Throw;
            bool defense = fam == CardFamily.Block  || fam == CardFamily.Parry;

            float pulse = 0.7f + 0.3f * Mathf.Sin(Time.time * cuePulseSpeed * Mathf.PI);

            if (offense)
            {
                rightEmis = offenseColor * (activeGlowIntensity * pulse);
                leftEmis  = defenseColor * idleGlowIntensity;
            }
            else if (defense)
            {
                leftEmis  = defenseColor * (activeGlowIntensity * pulse);
                rightEmis = offenseColor * idleGlowIntensity;
            }
            else // Support — either hand works, glow both
            {
                rightEmis = supportColor * (activeGlowIntensity * pulse);
                leftEmis  = supportColor * (activeGlowIntensity * pulse);
            }
        }

        foreach (var m in _rightMats) if (m != null) m.SetColor(ID_Emission, rightEmis);
        foreach (var m in _leftMats)  if (m != null) m.SetColor(ID_Emission, leftEmis);
    }

    void OnDestroy()
    {
        DestroyMats(_mats);
        DestroyMats(_rightMats);
        DestroyMats(_leftMats);
    }

    private static void DestroyMats(Material[] mats)
    {
        if (mats != null)
            foreach (var m in mats)
                if (m != null) Destroy(m);
    }
}
