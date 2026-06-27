using UnityEngine;

/// A quick electric "spark" flash over the local player's view — fired when a drone gets past you in
/// the drone-rush segment (a miss). Cosmetic only, no damage. Self-bootstraps; call ScreenSpark.Flash().
///
/// Draws a few jagged cyan/white bolts + a brief edge vignette via OnGUI, fading over ~0.35s. Works on
/// flat; in VR the screen-space overlay shows on the mirror/spectator view (and reads as a hit-flash).
public class ScreenSpark : MonoBehaviour
{
    private static ScreenSpark _inst;

    public static void Flash()
    {
        if (_inst == null)
        {
            var go = new GameObject("~ScreenSpark");
            DontDestroyOnLoad(go);
            _inst = go.AddComponent<ScreenSpark>();
        }
        _inst._t = 1f;
        _inst._seed = Random.Range(0, 9999);
    }

    private const float DURATION = 0.35f;
    private float _t;
    private int   _seed;
    private Texture2D _tex;

    private void EnsureTex()
    {
        if (_tex != null) return;
        _tex = new Texture2D(1, 1);
        _tex.SetPixel(0, 0, Color.white);
        _tex.Apply();
    }

    private void Update()
    {
        if (_t > 0f) _t = Mathf.Max(0f, _t - Time.unscaledDeltaTime / DURATION);
    }

    private void OnGUI()
    {
        if (_t <= 0f) return;
        EnsureTex();

        float a = _t;
        Color spark = new Color(0.6f, 0.95f, 1f, a);          // electric cyan-white
        float w = Screen.width, h = Screen.height;

        // Edge vignette flash.
        GUI.color = new Color(0.5f, 0.85f, 1f, 0.18f * a);
        float edge = 80f;
        GUI.DrawTexture(new Rect(0, 0, w, edge), _tex);
        GUI.DrawTexture(new Rect(0, h - edge, w, edge), _tex);
        GUI.DrawTexture(new Rect(0, 0, edge, h), _tex);
        GUI.DrawTexture(new Rect(w - edge, 0, edge, h), _tex);

        // A handful of jagged bolts streaking inward from random edges.
        var rng = new System.Random(_seed);
        GUI.color = spark;
        int bolts = 5;
        for (int b = 0; b < bolts; b++)
        {
            Vector2 p = RandomEdgePoint(rng, w, h);
            Vector2 dir = (new Vector2(w * 0.5f, h * 0.5f) - p).normalized;
            int segs = 6;
            for (int s = 0; s < segs; s++)
            {
                float len = 30f + (float)rng.NextDouble() * 60f;
                Vector2 jitter = new Vector2((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f) * 40f;
                Vector2 next = p + dir * len + jitter;
                DrawLine(p, next, 3f);
                p = next;
            }
        }
        GUI.color = Color.white;
    }

    private static Vector2 RandomEdgePoint(System.Random rng, float w, float h)
    {
        int side = rng.Next(0, 4);
        float r = (float)rng.NextDouble();
        switch (side)
        {
            case 0: return new Vector2(r * w, 0);
            case 1: return new Vector2(r * w, h);
            case 2: return new Vector2(0, r * h);
            default:return new Vector2(w, r * h);
        }
    }

    // Draw a thick line as a rotated 1px texture quad.
    private void DrawLine(Vector2 a, Vector2 b, float thickness)
    {
        Vector2 d = b - a;
        float len = d.magnitude;
        if (len < 0.01f) return;
        float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
        Matrix4x4 prev = GUI.matrix;
        GUIUtility.RotateAroundPivot(ang, a);
        GUI.DrawTexture(new Rect(a.x, a.y - thickness * 0.5f, len, thickness), _tex);
        GUI.matrix = prev;
    }
}
