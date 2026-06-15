using UnityEngine;

/// <summary>
/// Helper for making legacy IMGUI (OnGUI) screens phone-friendly.
///
/// IMGUI uses raw pixel coordinates, so on a high-DPI phone fixed-size buttons
/// and fonts render physically tiny and are hard to tap. This scales the whole
/// GUI from a fixed 1920x1080 reference up to the device resolution, so every
/// button/label keeps a consistent, finger-sized footprint regardless of screen.
///
/// Usage inside OnGUI (AFTER any early-out returns, BEFORE the first draw call):
///   var prev = MobileGUI.Begin(out float vw, out float vh);
///   ... draw using vw / vh instead of Screen.width / Screen.height ...
///   MobileGUI.End(prev);
///
/// On desktop this is a no-op: it returns the real screen size and leaves the
/// matrix untouched, so existing Steam/PC layout and mouse hit-testing are
/// completely unchanged.
/// </summary>
public static class MobileGUI
{
    public const float RefW = 1920f;
    public const float RefH = 1080f;

    /// <summary>
    /// Begins GUI scaling. Outputs the virtual width/height to lay out against
    /// and returns the previous GUI.matrix (pass it to End to restore).
    /// </summary>
    public static Matrix4x4 Begin(out float vw, out float vh)
    {
        Matrix4x4 prev = GUI.matrix;
#if UNITY_ANDROID && !UNITY_EDITOR
        vw = RefW;
        vh = RefH;
        GUI.matrix = Matrix4x4.TRS(
            Vector3.zero,
            Quaternion.identity,
            new Vector3(Screen.width / RefW, Screen.height / RefH, 1f));
#else
        vw = Screen.width;
        vh = Screen.height;
#endif
        return prev;
    }

    /// <summary>Restores the GUI.matrix saved by Begin.</summary>
    public static void End(Matrix4x4 prev)
    {
        GUI.matrix = prev;
    }
}
