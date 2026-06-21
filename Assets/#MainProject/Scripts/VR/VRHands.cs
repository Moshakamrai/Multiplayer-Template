using UnityEngine;
using UnityEngine.XR;

/// Makes the two boxing gloves track the physical Touch controllers in VR.
///
/// Put this on the player prefab (same object as PlayerController / PlayerCombat) and drag
/// the two glove Transforms into LeftGlove / RightGlove — the same objects you assigned to
/// GloveBeatGlow's renderer list.
///
/// PIVOT FIX:
///   The imported glove meshes have their pivot (transform origin) far from the actual mesh,
///   so rotating the object swings the mesh in a huge arc — impossible to tune. To fix this we
///   build a fresh "pivot anchor" right at each glove's mesh center (from its renderer bounds),
///   parent the glove under that anchor, and drive the ANCHOR. Now position and rotation both
///   happen around the visible glove, exactly like a properly-pivoted model.
///
/// HOW TRACKING WORKS (no XR Origin needed):
///   VRCameraDriver sets the camera's rotation to the HMD's orientation, so game-world axes
///   line up with the headset's tracking-space axes. That makes the controller's offset from
///   the head identical in both spaces — we drop each anchor at
///       cameraPosition + (controllerPos - headPos).
///   The hidden body keeps animating for combat events; we only override the gloves here in
///   LateUpdate (after the Animator), so nothing in the fight logic breaks.
[DefaultExecutionOrder(10001)] // after VRCameraDriver (10000), so the camera pose is final
public class VRHands : MonoBehaviour
{
    [Header("Drag the two glove Transforms (same objects as GloveBeatGlow renderers)")]
    public Transform leftGlove;
    public Transform rightGlove;

    // Per-glove rotation to align the model with the controller. Hardcoded (not a serialized
    // field) so a stale Inspector value can't override it — change here if alignment drifts.
    private static readonly Vector3 LEFT_GLOVE_ROT = new Vector3(0f, 270f, 0f);
    private static readonly Vector3 RIGHT_GLOVE_ROT = new Vector3(180f, 270f, 0f);

    [Tooltip("Scales how far the gloves sit from your head. 1 = real arm distance.")]
    public float reach = 1f;

    [Header("Trigger-charge power (VR)")]
    [Tooltip("Hold the trigger and pulse the controller to BUILD power. Charge gained per (m/s of motion · second) while the trigger is held. Higher = fills faster.")]
    public float chargeGain = 0.5f;
    [Tooltip("How fast the power charge dissolves (per second) when you stop moving or release the trigger.")]
    public float chargeDecay = 0.55f;
    [Tooltip("Controller speed (m/s) above which motion counts as a 'pulse' that builds charge.")]
    public float motionThreshold = 0.4f;

    [Header("Optional")]
    [Tooltip("Fully stop the character Animator. Leave OFF — the body is hidden anyway, and " +
             "disabling it can break animation-event-driven combat.")]
    public bool disableAnimator = false;

    private PlayerController _pc;
    private Animator _animator;
    private bool _animatorHandled;

    // Pivot anchors we build at each glove's true mesh center. We drive THESE, not the gloves.
    private Transform _leftAnchor;
    private Transform _rightAnchor;
    private bool _pivotsBuilt;

    // Glove alignment rotations — copied from the baked-in defaults on first frame.
    private Vector3 _rotOffsetL;
    private Vector3 _rotOffsetR;
    private bool _rotInit;

    // --- Trigger-charge power (VR), read by PlayerCombat when the shout locks in ---
    // HOLD a trigger and PULSE the controller to BUILD power (0..1). It dissolves when motion
    // stops, so you keep it alive by pulsing. The power meter shows it live. The on-beat SHOUT is
    // what fires the move and LOCKS IN this charge as the punch's power.
    private static float _charge; // 0..1 accumulated power
    public static float Charge => _charge;

    // ── Live hand + head tracking (exposed for the drone-rush punch/dodge checks) ──────────────
    // World positions + per-hand speed, updated every frame in UpdateChargeInput(). Other systems
    // (e.g. DroneRushSegment) read these to test "hand near drone + moving forward fast = a punch"
    // and "head moved aside = a dodge", without re-reading XR input themselves.
    private static Vector3 _leftHandPos, _rightHandPos, _headPos;
    private static float   _leftSpeed, _rightSpeed;
    private static bool    _tracking;

    public static bool  Tracking      => _tracking;
    public static Vector3 LeftHandPos => _leftHandPos;
    public static Vector3 RightHandPos=> _rightHandPos;
    public static float LeftSpeed     => _leftSpeed;
    public static float RightSpeed    => _rightSpeed;
    public static Vector3 HeadPos     => _headPos;

    /// True if either hand is within `radius` of `worldPos` while moving at least `minSpeed` (m/s) —
    /// i.e. a punch landed on something there. Outputs which hand and that hand's speed (punch power).
    public static bool PunchedAt(Vector3 worldPos, float radius, float minSpeed,
                                 out bool rightHand, out float speed)
    {
        rightHand = false; speed = 0f;
        if (!_tracking) return false;
        bool lHit = Vector3.Distance(_leftHandPos,  worldPos) <= radius && _leftSpeed  >= minSpeed;
        bool rHit = Vector3.Distance(_rightHandPos, worldPos) <= radius && _rightSpeed >= minSpeed;
        if (rHit && (_rightSpeed >= _leftSpeed || !lHit)) { rightHand = true;  speed = _rightSpeed; return true; }
        if (lHit)                                          { rightHand = false; speed = _leftSpeed;  return true; }
        return false;
    }

    /// Take the current charge as punch power and reset it (called when the lock-in fires on the beat).
    public static float ConsumeCharge()
    {
        float c = _charge;
        _charge = 0f;
        return c;
    }

    // Trigger RELEASE = a lock-in (alternative to shout). Tracked PER HAND so PlayerCombat can
    // require the matching controller: RIGHT fires Strike/Throw, LEFT fires Block/Parry.
    private const float RELEASE_EXPIRY = 0.15f;
    private static bool  _relPendL, _relPendR;
    private static float _relTimeL, _relTimeR, _relChargeL, _relChargeR;
    private bool _prevLTrig, _prevRTrig;

    /// Non-consuming check: is EITHER hand's trigger release currently pending (and unexpired)?
    /// Used to spot an "fired too early" attempt without eating the release (so a real on-beat fire
    /// a moment later still works).
    public static bool AnyReleasePending()
    {
        return (_relPendR && Time.time - _relTimeR <= RELEASE_EXPIRY)
            || (_relPendL && Time.time - _relTimeL <= RELEASE_EXPIRY);
    }

    /// True (once) if the given hand's trigger released within RELEASE_EXPIRY, with the charge at release.
    public static bool ConsumeRelease(bool rightHand, out float charge)
    {
        if (rightHand)
        {
            if (_relPendR && Time.time - _relTimeR <= RELEASE_EXPIRY) { charge = _relChargeR; _relPendR = false; return true; }
            _relPendR = false;
        }
        else
        {
            if (_relPendL && Time.time - _relTimeL <= RELEASE_EXPIRY) { charge = _relChargeL; _relPendL = false; return true; }
            _relPendL = false;
        }
        charge = 0f;
        return false;
    }

    private Vector3 _prevLPos, _prevRPos;
    private bool _havedPrevPos;
    private PowerMeterReactor _reactor; // local power cone, driven live with the charge

    private void Awake()
    {
        _pc = GetComponent<PlayerController>();
        var combat = GetComponent<PlayerCombat>();
        if (combat != null) _animator = combat.animator;
    }

    private bool Active => VRCameraDriver.VRActive && _pc != null && _pc.isLocalPlayer;

    private void LateUpdate()
    {
        if (!Active) return;

        if (disableAnimator && !_animatorHandled && _animator != null)
        {
            _animator.enabled = false;
            _animatorHandled = true;
        }

        if (!_rotInit)
        {
            _rotOffsetL = LEFT_GLOVE_ROT;
            _rotOffsetR = RIGHT_GLOVE_ROT;
            _rotInit = true;
        }

        if (!_pivotsBuilt)
        {
            _leftAnchor = BuildPivot(leftGlove);
            _rightAnchor = BuildPivot(rightGlove);
            _pivotsBuilt = true;
        }

        UpdateChargeInput();

        Camera cam = CachedCamera.Main;
        if (cam == null) return;

        InputDevice hmd = InputDevices.GetDeviceAtXRNode(XRNode.Head);
        if (!hmd.TryGetFeatureValue(CommonUsages.centerEyePosition, out Vector3 headPos))
            hmd.TryGetFeatureValue(CommonUsages.devicePosition, out headPos);

        PlaceGlove(_leftAnchor, InputDevices.GetDeviceAtXRNode(XRNode.LeftHand), cam, headPos, _rotOffsetL);
        PlaceGlove(_rightAnchor, InputDevices.GetDeviceAtXRNode(XRNode.RightHand), cam, headPos, _rotOffsetR);

        // Publish the gloves' real WORLD positions (where the player sees their fists) + head world
        // position for the drone-rush proximity/dodge checks. (Speed comes from UpdateChargeInput.)
        if (_leftAnchor  != null) _leftHandPos  = _leftAnchor.position;
        if (_rightAnchor != null) _rightHandPos = _rightAnchor.position;
        _headPos = cam.transform.position;
        _tracking = true;
    }

    /// Creates an empty anchor at the glove's true mesh center and reparents the glove under it.
    /// Returns the anchor transform (which we then drive). Returns null if no glove assigned.
    private Transform BuildPivot(Transform glove)
    {
        if (glove == null) return null;

        // Mesh center in WORLD space — robust for both static and skinned renderers.
        Renderer r = glove.GetComponentInChildren<Renderer>();
        Vector3 center = (r != null) ? r.bounds.center : glove.position;

        // Pull the glove out of the body skeleton so bone scale/animation can't distort it.
        var anchorGo = new GameObject(glove.name + "__pivot");
        anchorGo.transform.SetParent(transform, true);
        anchorGo.transform.position = center;        // pivot sits ON the mesh
        anchorGo.transform.rotation = glove.rotation;

        glove.SetParent(anchorGo.transform, true);   // keep the glove's current world transform

        return anchorGo.transform;
    }

    /// While EITHER trigger is held, controller motion BUILDS the power charge (pulse to charge up).
    /// With no motion (or the trigger released) the charge dissolves. The live value is pushed to the
    /// power meter every frame so the bar reacts in real time; the on-beat shout consumes it.
    private void UpdateChargeInput()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        InputDevice lh = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        InputDevice rh = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);

        lh.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 lpos);
        rh.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 rpos);

        float lspeed = 0f, rspeed = 0f;
        if (_havedPrevPos)
        {
            lspeed = (lpos - _prevLPos).magnitude / dt;
            rspeed = (rpos - _prevRPos).magnitude / dt;
        }
        _prevLPos = lpos; _prevRPos = rpos; _havedPrevPos = true;

        // Publish live hand SPEED for the drone-rush punch check (world POSITIONS + head are published
        // in LateUpdate from the glove anchors, which match where the fists visually are).
        _leftSpeed = lspeed; _rightSpeed = rspeed;

        bool ltrig = ReadBtn(lh, CommonUsages.triggerButton);
        bool rtrig = ReadBtn(rh, CommonUsages.triggerButton);
        bool anyTrig = ltrig || rtrig;
        float speed = Mathf.Max(lspeed, rspeed);

        float prevCharge = _charge;
        if (anyTrig && speed > motionThreshold)
            _charge = Mathf.Clamp01(_charge + speed * chargeGain * dt); // pulse to build power
        else
            _charge = Mathf.Max(0f, _charge - chargeDecay * dt);        // dissolve with no motion

        // HAPTICS: holding a trigger rumbles that hand, growing with the charge (engine-rev feel),
        // and a double-blip fires the moment the bar maxes so you know you're full without looking.
        if (_charge > 0.02f)
        {
            float rumble = 0.06f + 0.40f * _charge;
            if (ltrig) VRHaptics.Rumble(VRHaptics.Hand.Left, rumble);
            if (rtrig) VRHaptics.Rumble(VRHaptics.Hand.Right, rumble);
        }
        if (_charge >= 1f && prevCharge < 1f)
            VRHaptics.FullCharge(ltrig && !rtrig ? VRHaptics.Hand.Left
                               : rtrig && !ltrig ? VRHaptics.Hand.Right
                               : VRHaptics.Hand.Both);

        // Trigger RELEASE near the beat = lock-in, latched PER HAND (right = Strike/Throw, left = Block/Parry).
        float now = Time.time;
        if (!ltrig && _prevLTrig) { _relPendL = true; _relTimeL = now; _relChargeL = _charge; }
        if (!rtrig && _prevRTrig) { _relPendR = true; _relTimeR = now; _relChargeR = _charge; }
        _prevLTrig = ltrig; _prevRTrig = rtrig;

        // Drive the power cone live so the player SEES the charge building / dissolving.
        if (_reactor == null) _reactor = GetComponentInChildren<PowerMeterReactor>(true);
        if (_reactor != null) _reactor.SetLiveCharge(_charge);
    }

    private static bool ReadBtn(InputDevice d, InputFeatureUsage<bool> usage)
        => d.isValid && d.TryGetFeatureValue(usage, out bool v) && v;

    private void PlaceGlove(Transform anchor, InputDevice ctrl, Camera cam, Vector3 headPos, Vector3 rotOffset)
    {
        if (anchor == null || !ctrl.isValid) return;
        if (!ctrl.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 cpos)) return;
        ctrl.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion crot);

        // Controller offset from the head (tracking space == world axes here, see header).
        Vector3 offset = (cpos - headPos) * reach;
        anchor.position = cam.transform.position + offset;
        anchor.rotation = crot * Quaternion.Euler(rotOffset);
    }
}
