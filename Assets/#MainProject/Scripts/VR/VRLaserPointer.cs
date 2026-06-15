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
        bool shop   = ShopPhaseManager.Instance != null && ShopPhaseManager.Instance.isShopPhase;
        return picker || shop;
    }

    private void Update()
    {
        if (!VRCameraDriver.VRActive || !MenusOpen())
        {
            if (_hovered != null) { _hovered.SetHover(false); _hovered = null; }
            if (_line != null) _line.enabled = false;
            return;
        }

        Camera cam = Camera.main;
        if (cam == null) { _line.enabled = false; return; }

        // Right-controller pose in world space — same offset math VRHands uses.
        InputDevice hmd = InputDevices.GetDeviceAtXRNode(XRNode.Head);
        if (!hmd.TryGetFeatureValue(CommonUsages.centerEyePosition, out Vector3 headPos))
            hmd.TryGetFeatureValue(CommonUsages.devicePosition, out headPos);

        InputDevice rh = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        if (!rh.isValid || !rh.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 cpos))
        {
            _line.enabled = false;
            return;
        }
        rh.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion crot);

        Vector3 origin = cam.transform.position + (cpos - headPos);
        Vector3 dir    = crot * Vector3.forward;

        // Raycast for a button.
        Vector3 end = origin + dir * maxDistance;
        VRButton hit = null;
        if (Physics.Raycast(origin, dir, out RaycastHit h, maxDistance, hitMask, QueryTriggerInteraction.Collide))
        {
            hit = h.collider.GetComponentInParent<VRButton>();
            if (hit != null) end = h.point;
        }

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

        // Trigger (rising edge) = click.
        bool trig = rh.TryGetFeatureValue(CommonUsages.triggerButton, out bool tv) && tv;
        if (trig && !_prevTrigger && _hovered != null) _hovered.Click();
        _prevTrigger = trig;
    }
}
