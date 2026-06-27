using UnityEngine;
using UnityEngine.XR;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// Runtime performance tuning. Applies once when an XR headset is active (flat builds keep full
/// quality). Self-bootstraps; nothing to set up in a scene. Goal: maximum smooth FPS for the event.
public class VRPerformance : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var go = new GameObject("~VRPerformance");
        DontDestroyOnLoad(go);
        go.AddComponent<VRPerformance>();
    }

    private bool _applied;
    private Camera _lastCam;

    void Update()
    {
        // No headset => flat build; leave quality untouched.
        if (!XRSettings.isDeviceActive) return;

        if (!_applied)
        {
            _applied = true;
            ApplyOnce();
        }

        // Make sure post-processing stays ON for the active camera (neon bloom etc.).
        Camera cam = CachedCamera.Main;
        if (cam != null && cam != _lastCam)
        {
            var data = cam.GetComponent<UniversalAdditionalCameraData>();
            if (data != null) data.renderPostProcessing = true;
            _lastCam = cam;
        }
    }

    private void ApplyOnce()
    {
        // ── Frame timing ──────────────────────────────────────────────────────────────────────
        QualitySettings.vSyncCount = 0;          // the VR compositor owns timing; vSync just wastes work
        Application.targetFrameRate = 90;        // aim high; the headset clamps to its real refresh

        // ── Render resolution ─────────────────────────────────────────────────────────────────
        XRSettings.renderViewportScale = 0.85f;  // ~72% of the pixels — a solid GPU saving, still sharp

        // ── Shadows (a big GPU cost) ──────────────────────────────────────────────────────────
        QualitySettings.shadowDistance   = 25f;  // pull shadows in close
        QualitySettings.shadowCascades   = 1;
        QualitySettings.shadowResolution = UnityEngine.ShadowResolution.Low;
        QualitySettings.shadows          = UnityEngine.ShadowQuality.HardOnly; // no soft-shadow blur pass

        // ── Detail / LOD ──────────────────────────────────────────────────────────────────────
        QualitySettings.lodBias            = 0.6f;  // swap to cheaper LODs sooner
        QualitySettings.maximumLODLevel    = 0;
        QualitySettings.skinWeights        = SkinWeights.TwoBones; // cheaper character skinning
        QualitySettings.particleRaycastBudget = 16;
        QualitySettings.softParticles     = false;

        // ── Textures / anisotropic ────────────────────────────────────────────────────────────
        QualitySettings.anisotropicFiltering = AnisotropicFiltering.Disable;
        QualitySettings.globalTextureMipmapLimit = 0; // full-res; raise to 1 if VRAM/bandwidth bound

        // ── MSAA off (huge VR cost; bloom/neon doesn't need it) ───────────────────────────────
        QualitySettings.antiAliasing = 0;
        var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (urp != null) urp.msaaSampleCount = 1;

        // ── Physics: fewer solver iterations + a slightly longer fixed step = less CPU ──────────
        Time.fixedDeltaTime = 1f / 60f;          // 60 Hz physics (was likely 50; keeps it predictable)
        Physics.defaultSolverIterations = 4;
        Physics.defaultSolverVelocityIterations = 1;

        Debug.Log("[VRPerformance] Performance settings applied.");
    }
}
