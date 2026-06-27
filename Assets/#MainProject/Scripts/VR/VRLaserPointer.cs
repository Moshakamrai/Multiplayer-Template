using UnityEngine;
using UnityEngine.XR;

/// A lightweight VR laser pointer built on raw InputDevices (matches VRHands — no XR Origin / XRIT).
/// Draws a beam from the RIGHT controller, raycasts physics against VRButton colliders, highlights
/// what it's over, and clicks on a trigger press. Only active while a VR menu is open (round picker
/// or shop), so it never interferes with the punch-based combat.
[DefaultExecutionOrder(10003)] // after VRCameraDriver(10000)/VRHands(10001)/VRWorldHud(10002)
public class VRLaserPointer : MonoBehaviour
{
    public float maxDistance = 8f;
    public LayerMask hitMask = ~0; // everything; the only colliders in menus are VRButtons anyway

    private LineRenderer _line;
    private VRButton _hovered;
    private bool _prevTrigger;

    private void Awake()
    {
        var go = new GameObject("~VRLaserLine");
        go.transform.SetParent(transform, false);
        _line = go.AddComponent<LineRenderer>();
        _line.widthMultiplier = 0.006f;
        _line.numCapVertices = 4;
        _line.material = new Material(Shader.Find("Sprites/Default"));
        _line.startColor = new Color(0.2f, 0.85f, 1f, 0.95f);
        _line.endColor   = new Color(0.2f, 0.85f, 1f, 0.25f);
        _line.positionCount = 2;
        _line.enabled = false;
    }

    // The menus that the pointer should be live for.
    private static bool MenusOpen()
    {
        var rmm = RhythmRoundManager.Instance;
        bool picker = rmm != null && rmm.isRoundPickerActive;
        bool start  = rmm != null && rmm.WaitingForVRStart; // the VR Play + leaderboard entry screen
        bool shop   = ShopPhaseManager.Instance != null && ShopPhaseManager.Instance.isShopPhase;
        return picker || start || shop;
    }

    private void Update()
    {
        if (!MenusOpen())
        {
            if (_hovered != null) { _hovered.SetHover(false); _hovered = null; }
            if (_line != null) _line.enabled = false;
            return;
        }

        // EDITOR / flat Play mode (no headset): drive the same world-space VRButtons with the MOUSE so
        // you can test menus without a VR rig. Left-click = press.
        if (!VRCameraDriver.VRActive)
        {
            if (_line != null) _line.enabled = false;
            MouseUpdate();
            return;
        }

        Camera cam = CachedCamera.Main;
        if (cam == null) { _line.enabled = false; return; }

        // Right-controller pose in world space — same offset math VRHands uses.
        InputDevice hmd = InputDevices.GetDeviceAtXRNode(XRNode.Head);
        if (!hmd.TryGetFeatureValue(CommonUsages.centerEyePosition, out Vector3 headPos))
            hmd.TryGetFeatureValue(CommonUsages.devicePosition, out headPos);

        // Prefer the RIGHT controller for the pointer, but fall back to the LEFT if the right isn't
        // tracked/valid (so the laser works whichever hand is awake — was bailing entirely before).
        InputDevice rh = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        if (!rh.isValid || !rh.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 cpos))
        {
            rh = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            if (!rh.isValid || !rh.TryGetFeatureValue(CommonUsages.devicePosition, out cpos))
            {
                _line.enabled = false;
                return;
            }
        }
        rh.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion crot);

        Vector3 origin = cam.transform.position + (cpos - headPos);
        Vector3 dir    = crot * Vector3.forward;

        // Raycast for a button. Use RaycastAll + pick the NEAREST collider that actually belongs to a
        // VRButton — a plain Raycast would stop on whatever's closest (the player's capsule, a hand
        // collider, the floor, a drone…) and miss the button behind it. That blocked-ray case was the
        // main "buttons don't click" bug. We sort hits by distance and take the first real VRButton.
        Vector3 end = origin + dir * maxDistance;
        VRButton hit = FindButton(origin, dir, maxDistance, out Vector3 hitPoint);
        if (hit != null) end = hitPoint;

        _line.enabled = true;
        _line.SetPosition(0, origin);
        _line.SetPosition(1, end);

        // Hover swap.
        if (hit != _hovered)
        {
            if (_hovered != null) _hovered.SetHover(false);
            _hovered = hit;
            if (_hovered != null) _hovered.SetHover(true);
        }

        // Click detection — read the ANALOG trigger (and grip as a fallback) with our OWN low threshold
        // + hysteresis, instead of CommonUsages.triggerButton which only fires past a high hardware
        // press point (that's what made clicks feel unresponsive / miss light pulls). Either hand counts.
        float rTrig = ReadTrigger(rh);
        InputDevice lh = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        float lTrig = ReadTrigger(lh);
        float t = Mathf.Max(rTrig, lTrig);

        // Rising edge with hysteresis: press at >0.5, won't re-arm until it drops below 0.3.
        bool pressed = _prevTrigger ? (t > 0.3f) : (t > 0.5f);
        bool risingEdge = pressed && !_prevTrigger;
        _prevTrigger = pressed;

        // Click grace: a press is remembered for a short window so a SLIGHTLY off/jittery laser still
        // registers — if you press just before the beam lands on the button (or it flickers off for a
        // frame), the click still fires the moment a button is hovered. This is the main "feels
        // unresponsive / dropped clicks" fix.
        if (risingEdge) _pressBufferedUntil = Time.unscaledTime + CLICK_GRACE;

        if (_hovered != null && Time.unscaledTime <= _pressBufferedUntil)
        {
            _hovered.Click();
            _pressBufferedUntil = 0f; // consume so one press = one click
        }
    }

    private const float CLICK_GRACE = 0.18f;
    private float _pressBufferedUntil;

    // ── Mouse fallback for editor / flat Play mode (no headset) ─────────────────────────────────
    private bool _prevMouseDown;
    private void MouseUpdate()
    {
        Camera cam = CachedCamera.Main;
        if (cam == null) return;

        Vector2 mp = MousePos();
        Ray ray = cam.ScreenPointToRay(mp);

        VRButton hit = FindButton(ray.origin, ray.direction, 1000f, out _);

        if (hit != _hovered)
        {
            if (_hovered != null) _hovered.SetHover(false);
            _hovered = hit;
            if (_hovered != null) _hovered.SetHover(true);
        }

        bool down = MouseDown();
        if (down && !_prevMouseDown && _hovered != null) _hovered.Click();
        _prevMouseDown = down;
    }

    private static Vector2 MousePos()
    {
#if ENABLE_INPUT_SYSTEM
        var m = UnityEngine.InputSystem.Mouse.current;
        return m != null ? m.position.ReadValue() : Vector2.zero;
#else
        return Input.mousePosition;
#endif
    }

    private static bool MouseDown()
    {
#if ENABLE_INPUT_SYSTEM
        var m = UnityEngine.InputSystem.Mouse.current;
        return m != null && m.leftButton.isPressed;
#else
        return Input.GetMouseButton(0);
#endif
    }

    // RaycastAll along the ray, return the NEAREST collider that resolves to a VRButton (skipping any
    // non-button colliders in front of it). This is what makes the laser reliably hit menu buttons even
    // when the player's body / hands / floor sit between the controller and the panel.
    private readonly RaycastHit[] _hits = new RaycastHit[16];
    private VRButton FindButton(Vector3 origin, Vector3 dir, float dist, out Vector3 point)
    {
        point = origin + dir * dist;
        int n = Physics.RaycastNonAlloc(origin, dir.normalized, _hits, dist, hitMask, QueryTriggerInteraction.Collide);
        VRButton best = null; float bestDist = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            var btn = _hits[i].collider.GetComponentInParent<VRButton>();
            if (btn == null) continue;                 // not a button — ignore, don't let it block
            if (_hits[i].distance < bestDist) { bestDist = _hits[i].distance; best = btn; point = _hits[i].point; }
        }
        return best;
    }

    // A forgiving "is the user trying to click" value in 0..1. Takes the highest of: analog trigger,
    // analog grip, and the digital trigger/grip/A/X buttons (some runtimes report the analog axes as 0
    // but still fire the digital button). Any of these reaching the threshold counts as a press.
    private static float ReadTrigger(InputDevice d)
    {
        float v = 0f;
        if (!d.isValid) return v;
        if (d.TryGetFeatureValue(CommonUsages.trigger, out float tr)) v = Mathf.Max(v, tr);
        if (d.TryGetFeatureValue(CommonUsages.grip, out float gr))    v = Mathf.Max(v, gr);
        if (d.TryGetFeatureValue(CommonUsages.triggerButton, out bool tb) && tb) v = 1f;
        if (d.TryGetFeatureValue(CommonUsages.gripButton, out bool gb) && gb)    v = 1f;
        if (d.TryGetFeatureValue(CommonUsages.primaryButton, out bool pb) && pb) v = 1f; // A / X
        return v;
    }
}
