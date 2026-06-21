using UnityEngine;

/// Camera.main is deceptively expensive — each access does an internal FindGameObjectWithTag, so
/// calling it every frame across many scripts adds up. This caches the main camera and only re-finds
/// it if the cached one is destroyed (e.g. scene reload). Use CachedCamera.Main instead of Camera.main
/// in per-frame code.
public static class CachedCamera
{
    private static Camera _cam;

    public static Camera Main
    {
        get
        {
            if (_cam == null) _cam = Camera.main;   // only re-finds when the reference is gone
            return _cam;
        }
    }

    /// Force a refresh (call after a scene change if needed; usually unnecessary since the getter
    /// auto-refreshes when the cached camera is destroyed).
    public static void Invalidate() => _cam = null;
}
