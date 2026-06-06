using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

/// Owns the VR camera across ALL scenes (menu, lobby, gameplay).
///
/// Self-bootstraps via RuntimeInitializeOnLoadMethod — there is nothing to place in
/// a scene, and any leftover copy on the player object self-destructs (singleton).
///
/// Responsibilities:
///  • Detect the headset and expose the static VRActive flag the game code branches on.
///  • Force every camera to render BOTH eyes and never to a render texture
///    (a camera set to one eye / a target texture is the classic "I only see one socket").
///  • Drive the camera's ROTATION from the HMD (head-look). Position is left to the
///    game's own follow code; XR adds the per-eye stereo offset on top.
[DefaultExecutionOrder(10000)] // run after everything else that moves the camera
public class VRCameraDriver : MonoBehaviour
{
    public static bool VRActive { get; private set; }
    private static VRCameraDriver _instance;

    // Live-tunable VR viewpoint offset (set in-headset with the right thumbstick).
    // GameManager.HandleCamera reads these to push the first-person view forward/up.
    public static float ForwardOffset { get; private set; } = 2.4f;
    public static float HeightOffset { get; private set; } = 0f;
    private const string FwdKey = "VR_FwdOffset";
    private const string HgtKey = "VR_HeightOffset";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (_instance != null) return;
        var go = new GameObject("~VRCameraRig");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<VRCameraDriver>();
    }

    private void Awake()
    {
        // Kill any duplicate (e.g. an old copy left on the player prefab).
        if (_instance != null && _instance != this) { Destroy(this); return; }
        _instance = this;

        ForwardOffset = PlayerPrefs.GetFloat(FwdKey, 0.8f);
        HeightOffset = PlayerPrefs.GetFloat(HgtKey, 0f);
    }

    // Diagnostics shown on the readout so we can see what input (if any) is arriving.
    private static string _diag = "no input read yet";
    private float _logTimer;
    // Camera position is locked (set once, not adjusted with buttons to avoid conflicts with gloves).
    // If you need to retune, manually call this in editor or modify ForwardOffset/HeightOffset directly.

    private InputDevice _hmd;
    private bool _hmdFound;
    private Camera _cam;

    private void TryFindHMD()
    {
        var devices = new List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.HeadMounted, devices);
        if (devices.Count == 0) return;

        _hmd = devices[0];
        _hmdFound = true;
        VRActive = true;
        Debug.Log($"[VR] HMD detected: {_hmd.name}");
    }

    private void LateUpdate()
    {
        if (!_hmdFound || !_hmd.isValid) TryFindHMD();
        if (!_hmdFound) return;

        // Re-acquire the main camera when it changes (scene load). Only then do the
        // (allocating) all-cameras scan, to avoid per-frame GC in VR.
        if (_cam == null || !_cam.isActiveAndEnabled)
        {
            _cam = Camera.main;
            if (_cam == null) return;

            // --- Force correct stereo config on EVERY camera in the scene ---
            // This is the fix for "I only see from one eye": a camera whose
            // stereoTargetEye is Left/Right, or which renders into a targetTexture,
            // won't display in both eyes.
            foreach (Camera c in Camera.allCameras)
            {
                if (c == null) continue;
                if (c.stereoTargetEye != StereoTargetEyeMask.Both)
                    c.stereoTargetEye = StereoTargetEyeMask.Both;
                if (c.targetTexture != null)
                    c.targetTexture = null;
            }
        }
        if (_cam == null) return;

        // --- Position: own the VR viewpoint directly from the player's eye socket ---
        // This runs in gameplay (proven by head-look working), so the tuning offset is
        // GUARANTEED to apply here — no dependency on GameManager's camera code.
        PlayerController lp = GameManager.localPlayer;
        if (lp != null && lp.CameraPosition != null)
        {
            Transform eye = lp.CameraPosition;
            Vector3 flatFwd = eye.forward; flatFwd.y = 0f;
            if (flatFwd.sqrMagnitude > 0.0001f) flatFwd.Normalize();
            _cam.transform.position = eye.position + flatFwd * ForwardOffset + Vector3.up * HeightOffset;
        }

        // --- Head-look: drive the camera ROTATION from the HMD. ---
        if (_hmd.TryGetFeatureValue(CommonUsages.centerEyeRotation, out Quaternion hmdRot))
            _cam.transform.rotation = hmdRot;
        else if (_hmd.TryGetFeatureValue(CommonUsages.deviceRotation, out hmdRot))
            _cam.transform.rotation = hmdRot;
    }

    // Center crosshair + a live readout of the view-distance value so you can read it
    // off and tell me what felt right.
    private void OnGUI()
    {
        if (!VRActive) return;
        const float size = 10f;
        Rect r = new Rect(Screen.width * 0.5f - size * 0.5f, Screen.height * 0.5f - size * 0.5f, size, size);
        Color prev = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, 0.85f);
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = prev;

        // Readout (centered, just below the crosshair). Big font so it's legible in-headset.
        var style = new GUIStyle(GUI.skin.label)
        {
            fontSize = 34,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        style.normal.textColor = new Color(0.3f, 1f, 0.4f);
        GUI.Label(new Rect(0, Screen.height * 0.5f + 30f, Screen.width, 50f),
                  $"View distance: {ForwardOffset:0.00}   (right stick / A=closer B=farther)", style);

        // Diagnostic line — tells us if the controller input is actually arriving.
        var dstyle = new GUIStyle(GUI.skin.label) { fontSize = 26, alignment = TextAnchor.MiddleCenter };
        dstyle.normal.textColor = new Color(1f, 0.9f, 0.3f);
        GUI.Label(new Rect(0, Screen.height * 0.5f + 80f, Screen.width, 40f), _diag, dstyle);
    }

    public static void ForceDisable() => VRActive = false;
}
