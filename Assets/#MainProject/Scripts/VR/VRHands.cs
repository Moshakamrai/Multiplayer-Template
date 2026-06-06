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

    [Header("Punch input (replaces shout-on-beat)")]
    [Tooltip("Hand speed (m/s) at or below this = weakest hit (no power bonus).")]
    public float minPunchSpeed = 1.0f;
    [Tooltip("Hand speed (m/s) at or above this = full power bonus.")]
    public float maxPunchSpeed = 5.0f;

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

    // --- Punch input (motion speed + trigger), consumed by PlayerCombat in VR ---
    // A trigger press latches a "punch" with the swinging hand's speed as its power (0.4..1).
    // PlayerCombat.CheckLocalParryTiming consumes it on the beat, in place of the mic volume.
    private static bool _punchPending;
    private static float _punchPower;
    private static float _punchSetTime;
    private const float PUNCH_EXPIRY = 0.12f; // a press only counts for ~120ms

    private Vector3 _prevLPos, _prevRPos;
    private bool _havedPrevPos;
    private bool _prevLTrig, _prevRTrig;

    // Charge state: while a trigger is held we track the peak swing speed of that hand.
    private bool _chargingL, _chargingR;
    private float _peakSpeedL, _peakSpeedR;

    /// Called by PlayerCombat instead of reading mic volume when in VR.
    /// Returns true (once) if a trigger-punch is waiting, with its power (0.4..1 from hand speed).
    public static bool ConsumePunch(out float power)
    {
        if (_punchPending && Time.time - _punchSetTime <= PUNCH_EXPIRY)
        {
            power = _punchPower;
            _punchPending = false;
            return true;
        }
        _punchPending = false; // expire stale presses
        power = 0f;
        return false;
    }

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

        UpdatePunchInput();

        Camera cam = Camera.main;
        if (cam == null) return;

        InputDevice hmd = InputDevices.GetDeviceAtXRNode(XRNode.Head);
        if (!hmd.TryGetFeatureValue(CommonUsages.centerEyePosition, out Vector3 headPos))
            hmd.TryGetFeatureValue(CommonUsages.devicePosition, out headPos);

        PlaceGlove(_leftAnchor, InputDevices.GetDeviceAtXRNode(XRNode.LeftHand), cam, headPos, _rotOffsetL);
        PlaceGlove(_rightAnchor, InputDevices.GetDeviceAtXRNode(XRNode.RightHand), cam, headPos, _rotOffsetR);
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

    /// Tracks each controller's speed and fires a punch on a trigger press (rising edge),
    /// using that hand's current speed as the punch power.
    private void UpdatePunchInput()
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

        // CHARGE & RELEASE:
        //   PRESS trigger  -> start charging (begin tracking how fast you swing)
        //   HOLD + swing   -> we remember the PEAK speed of the swing  (= damage power)
        //   RELEASE on beat-> throws the punch; the release moment is the timing input (= multiplier)
        bool ltrig = ReadBtn(lh, CommonUsages.triggerButton);
        bool rtrig = ReadBtn(rh, CommonUsages.triggerButton);

        // LEFT hand
        if (ltrig && !_prevLTrig) { _chargingL = true; _peakSpeedL = 0f; }
        if (_chargingL) _peakSpeedL = Mathf.Max(_peakSpeedL, lspeed);
        if (!ltrig && _prevLTrig && _chargingL) { RegisterPunch(_peakSpeedL); _chargingL = false; }

        // RIGHT hand
        if (rtrig && !_prevRTrig) { _chargingR = true; _peakSpeedR = 0f; }
        if (_chargingR) _peakSpeedR = Mathf.Max(_peakSpeedR, rspeed);
        if (!rtrig && _prevRTrig && _chargingR) { RegisterPunch(_peakSpeedR); _chargingR = false; }

        _prevLTrig = ltrig; _prevRTrig = rtrig;
    }

    private void RegisterPunch(float speed)
    {
        float t = Mathf.Clamp01((speed - minPunchSpeed) / Mathf.Max(0.01f, maxPunchSpeed - minPunchSpeed));
        // Map onto the existing "volume" power channel: 0.4 = no bonus, 1.0 = full +25%.
        _punchPower = Mathf.Lerp(0.4f, 1.0f, t);
        _punchPending = true;
        _punchSetTime = Time.time;
        Debug.Log($"[VRHands] Punch! speed={speed:0.0}m/s power={_punchPower:0.00}");
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
