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
    [Tooltip("Brightness the active glove idles at far from the beat, and the INACTIVE glove stays at.")]
    public float idleGlowIntensity = 1.5f;
    [Tooltip("Pulse speed of the active-hand cue.")]
    public float cuePulseSpeed = 3.0f;

    [Header("Beat Charge")]
    [ColorUsage(false, true)] public Color chargeColor = new Color(0.0f, 0.85f, 1.0f); // cyan
    public float chargeIntensity = 2.2f;
    [Tooltip("Curve of the rise toward the beat. >1 ramps SLOW then SHOOTS UP near the beat (snappier). " +
             "1 = linear. Try 2–3 for a strong gradual-then-sharp climb.")]
    public float chargeRampPower = 2.5f;
    [Tooltip("Seconds before the beat the gloves start charging.")]
    public float chargeLead = 0.6f;
    public float chargePulseMax = 12f;

    [Header("Strike / Hurt Bursts")]
    [ColorUsage(false, true)] public Color strikeFlashColor = new Color(1.0f, 0.85f, 0.5f); // warm white-hot
    [ColorUsage(false, true)] public Color hurtFlashColor   = new Color(1.0f, 0.15f, 0.10f); // red
    public float flashIntensity = 4f;
    public float flashDecay     = 6f;

    [Header("Persistent Card Glow")]
    [Tooltip("Max glow intensity when the beat is close. Crank this to make the ramp dramatic.")]
    public float selectionBloomIntensity = 16f;

    [Header("Persistent Card Glow Colors")]
    [ColorUsage(false, true)] public Color strikeBloomColor = new Color(1.0f, 0.35f, 0.05f);
    [ColorUsage(false, true)] public Color throwBloomColor  = new Color(1.0f, 0.60f, 0.10f);
    [ColorUsage(false, true)] public Color blockBloomColor  = new Color(0.10f, 0.55f, 1.0f);
    [ColorUsage(false, true)] public Color parryBloomColor  = new Color(0.50f, 0.80f, 1.0f);
    [ColorUsage(false, true)] public Color supportBloomColor = new Color(1.0f, 1.0f, 1.0f);

    private Material[] _mats;
    private Material[] _rightMats;
    private Material[] _leftMats;
    private float _flashTimer;
    private Color _flashColor;
    private PlayerCombat _localCombat;   // resolved lazily; only the LOCAL player drives the cue
    private PlayerController _localController;
    private CardManager  _cardManager;
    static readonly int ID_Emission = Shader.PropertyToID("_EmissionColor");

    void Awake()
    {
        _localCombat = GetComponent<PlayerCombat>();
        _localController = GetComponent<PlayerController>();
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

            // Still nothing? Split the generic gloveRenderers into left/right by object name so the
            // active-hand cue works with zero extra setup (matches "left"/"right", "_l"/"_r", "L"/"R").
            if ((rightGloveRenderers == null || rightGloveRenderers.Length == 0) &&
                (leftGloveRenderers  == null || leftGloveRenderers.Length  == 0) &&
                gloveRenderers != null && gloveRenderers.Length > 0)
            {
                var rs = new List<Renderer>(); var ls = new List<Renderer>();
                foreach (var r in gloveRenderers)
                {
                    if (r == null) continue;
                    string n = r.name.ToLower();
                    bool isLeft  = n.Contains("left")  || n.Contains("_l") || n.EndsWith("l");
                    bool isRight = n.Contains("right") || n.Contains("_r") || n.EndsWith("r");
                    if (isRight && !isLeft) rs.Add(r);
                    else if (isLeft && !isRight) ls.Add(r);
                }
                if (rs.Count > 0) rightGloveRenderers = rs.ToArray();
                if (ls.Count > 0) leftGloveRenderers  = ls.ToArray();
                Debug.Log($"[GloveCue] split gloveRenderers by name — right={rs.Count} left={ls.Count}. " +
                          (rs.Count == 0 && ls.Count == 0 ? "<<< couldn't tell hands apart — assign Left/Right Glove Renderers in the Inspector." : ""), this);
            }
        }

        _mats      = InstanceMats(gloveRenderers);
        _rightMats = InstanceMats(rightGloveRenderers);
        _leftMats  = InstanceMats(leftGloveRenderers);

        // If the combined gloveRenderers list wasn't assigned, fall back to the per-hand lists.
        if (_mats.Length == 0 && (_rightMats.Length > 0 || _leftMats.Length > 0))
        {
            var combined = new List<Material>();
            combined.AddRange(_rightMats);
            combined.AddRange(_leftMats);
            _mats = combined.ToArray();
        }
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

    private Color GlowColorFor(CardFamily fam)
    {
        return fam switch
        {
            CardFamily.Strike => strikeBloomColor,
            CardFamily.Throw  => throwBloomColor,
            CardFamily.Block  => blockBloomColor,
            CardFamily.Parry  => parryBloomColor,
            _                 => supportBloomColor,
        };
    }

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

        // Charge ramp toward the next beat — eased so it rises gradually then climbs hard near the beat.
        float charge = 0f;
        if (round)
        {
            float toBeat = mgr.GetNextBeatTime() - mgr.GetCurrentTrackTime();
            if (toBeat >= 0f && toBeat <= chargeLead && chargeLead > 0.001f)
                charge = Mathf.Pow(1f - (toBeat / chargeLead), chargeRampPower);
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

        // HAPTICS (local player only — never buzz the bot's phantom controllers).
        if (round && _localCombat != null && _localCombat.isLocalPlayer)
        {
            VRHaptics.Hand cueHand = CueHandFor(_localCombat.PendingMoveTrigger);
            bool hasPending = !string.IsNullOrEmpty(_localCombat.PendingMoveTrigger);

            // 1. On-beat metronome tick.
            if (mgr.lastBeatFireTime > 0f && mgr.lastBeatFireTime != _lastBeatTickTime)
            {
                _lastBeatTickTime = mgr.lastBeatFireTime;
                VRHaptics.BeatTick(cueHand, 0.5f);
                VRHaptics.ResetBeatApproach(); // clear approach accumulator after beat fires
            }

            // 2. Beat-approach escalating taps: charge goes 0→1 in the chargeLead window before the beat.
            if (charge > 0f)
                VRHaptics.BeatApproach(cueHand, charge);

            // 3. Idle reminder rumble: gentle hum on the cue hand when a card is pending but the beat
            //    is not close yet. Fades out as the approach taps take over.
            if (hasPending)
                VRHaptics.CardIdleRumble(cueHand, charge);
        }

        UpdateActiveHandCue();
    }

    private float _lastBeatTickTime = -1f;

    /// Which controller should act for this move — RIGHT for Strike/Throw, LEFT for Block/Parry,
    /// BOTH for Support / no pending move. Mirrors the glove-glow cue.
    private VRHaptics.Hand CueHandFor(string move)
    {
        if (string.IsNullOrEmpty(move) || _cardManager == null) return VRHaptics.Hand.Both;
        CardFamily fam = _cardManager.FamilyOfTrigger(move);
        if (fam == CardFamily.Strike || fam == CardFamily.Throw) return VRHaptics.Hand.Right;
        if (fam == CardFamily.Block || fam == CardFamily.Parry) return VRHaptics.Hand.Left;
        return VRHaptics.Hand.Both;
    }

    /// VR: glow the controller the player should use for the pending card — RIGHT for Strike/Throw,
    /// LEFT for Block/Parry, BOTH for Support. Glow persists across beats and ramps up as the next
    /// beat approaches, then goes dark while the attack/dash animation plays. Only the LOCAL player
    /// drives this (remote clones / bot are left dark).
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

        // No card chosen, or the attack/dash animation is playing — keep the glove dark.
        bool duringAction = _localCombat.isAttacking || (_localController != null && _localController.IsDashing);
        if (string.IsNullOrEmpty(move) || duringAction)
        {
            foreach (var m in _rightMats) if (m != null) m.SetColor(ID_Emission, Color.black);
            foreach (var m in _leftMats)  if (m != null) m.SetColor(ID_Emission, Color.black);
            return;
        }

        if (move != _lastCueMove)
        {
            _lastCueMove = move;
            Debug.Log($"[GloveCue] pending move -> '{move}'", this);
            // HAPTIC: a new card was picked — tick the controller that must fire it.
            if (!string.IsNullOrEmpty(move)) VRHaptics.CardCue(CueHandFor(move));
        }

        // Ramping intensity: low far from the beat, climbing hard as it nears (eased, within chargeLead).
        float charge = 0f;
        var mgr = RhythmRoundManager.Instance;
        if (mgr != null && mgr.isRoundActive)
        {
            float toBeat = mgr.GetNextBeatTime() - mgr.GetCurrentTrackTime();
            if (toBeat >= 0f && toBeat <= chargeLead && chargeLead > 0.001f)
                charge = Mathf.Pow(1f - (toBeat / chargeLead), chargeRampPower);
        }

        CardFamily fam = _cardManager != null ? _cardManager.FamilyOfTrigger(move) : CardFamily.Support;
        Color color = GlowColorFor(fam);
        float glow = Mathf.Lerp(idleGlowIntensity, selectionBloomIntensity, charge);
        float pulse = 0.7f + 0.3f * Mathf.Sin(Time.time * cuePulseSpeed * Mathf.PI);
        Color emis = color * (glow * pulse);

        bool rightHand = fam == CardFamily.Strike || fam == CardFamily.Throw || fam == CardFamily.Support;
        bool leftHand  = fam == CardFamily.Block  || fam == CardFamily.Parry  || fam == CardFamily.Support;

        foreach (var m in _rightMats) if (m != null) m.SetColor(ID_Emission, rightHand ? emis : Color.black);
        foreach (var m in _leftMats)  if (m != null) m.SetColor(ID_Emission, leftHand  ? emis : Color.black);
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
