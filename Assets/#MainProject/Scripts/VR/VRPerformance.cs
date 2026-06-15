using UnityEngine;
using UnityEngine.XR;
using UnityEngine.Rendering.Universal;

/// Applies aggressive mobile-VR performance settings at runtime, but ONLY when an
/// XR headset is actually active — so flat PC/phone builds keep their full quality.
///
/// Self-bootstraps via RuntimeInitializeOnLoadMethod, so there is nothing to set up
/// in any scene. It survives scene loads and keeps stripping post-processing off
/// whatever camera becomes Camera.main (menu camera, gameplay camera, etc.).
public class VRPerformance : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var go = new GameObject("~VRPerformance");
        DontDestroyOnLoad(go);
        go.AddComponent<VRPerformance>();
    }

    private bool _globalApplied;
    private Camera _lastCam;

    void Update()
    {
        // No headset => this is a flat build; do nothing and leave quality untouched.
        if (!XRSettings.isDeviceActive) return;

        if (!_globalApplied)
        {
            // Render fewer pixels than the panel's native resolution. 0.9 = 81% of the
            // pixels — sharper than 0.8 while still saving GPU. Tune 0.7–1.0.
            XRSettings.renderViewportScale = 0.9f;

            // The VR compositor owns frame timing; vSync here just wastes work.
            QualitySettings.vSyncCount = 0;

            // Pull shadows in close and drop detail with distance.
            QualitySettings.shadowDistance = 35f;
            QualitySettings.lodBias = 0.7f;
            QualitySettings.shadowCascades = 1;

            _globalApplied = true;
            Debug.Log("[VRPerformance] Mobile-VR settings applied.");
        }

        // Keep post-processing ON in VR so Bloom (the neon floor glow, power cone, etc.) renders
        // like the PC build. This costs GPU — if you target a standalone headset (Quest) and need
        // the frames back, flip this to false.
        Camera cam = Camera.main;
        if (cam != null && cam != _lastCam)
        {
            var data = cam.GetComponent<UniversalAdditionalCameraData>();
            if (data != null) data.renderPostProcessing = true;
            _lastCam = cam;
        }
    }
}
