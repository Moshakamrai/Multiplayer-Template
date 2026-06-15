using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.XR;

/// TEMPORARY (Step 2 / 3 bridge): screen-space UI — UI Toolkit panels and
/// ScreenSpace canvases — render into only one eye under multi-pass VR, which is
/// the "left eye = world, right eye = UI garbage" you saw. Until the UI is rebuilt
/// as world-space (Step 3), this just hides all screen-space UI while a headset is
/// active so both eyes show a clean 3D world.
///
/// Self-bootstraps; nothing to set up. Has ZERO effect on flat (non-XR) builds.
/// Remove this script once world-space UI is in place.
public class VRUISuppressor : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        var go = new GameObject("~VRUISuppressor");
        DontDestroyOnLoad(go);
        go.AddComponent<VRUISuppressor>();
    }

    private float _timer;

    private void Update()
    {
        if (!XRSettings.isDeviceActive) return;

        // Re-scan periodically so UI that spawns mid-game (HUD, panels) also gets hidden.
        _timer -= Time.unscaledDeltaTime;
        if (_timer > 0f) return;
        _timer = 0.5f;

        // UI Toolkit panels (MenuUI, LobbyUI, GameManager options, etc.)
        foreach (UIDocument doc in FindObjectsOfType<UIDocument>())
            if (doc.enabled) doc.enabled = false;

        // uGUI canvases that draw to the screen (menu background, HUD).
        // World-space canvases are left alone — those DO work in VR.
        foreach (Canvas c in FindObjectsOfType<Canvas>())
            if (c.enabled && c.renderMode != RenderMode.WorldSpace) c.enabled = false;
    }
}
