using UnityEngine;

/// <summary>
/// Utility class for drawing cyberpunk-styled GUI elements with neon glow effects and scanlines.
/// </summary>
public static class CyberpunkGUIUtils
{
    private static Texture2D _whiteTex;

    // Neon color palette
    public static readonly Color NEON_CYAN = new Color(0f, 1f, 1f);
    public static readonly Color NEON_MAGENTA = new Color(1f, 0.2f, 0.8f);
    public static readonly Color NEON_GREEN = new Color(0.3f, 1f, 0.5f);
    public static readonly Color NEON_BLUE = new Color(0.2f, 0.8f, 1f);
    public static readonly Color NEON_YELLOW = new Color(1f, 1f, 0.3f);
    public static readonly Color NEON_ORANGE = new Color(1f, 0.5f, 0f);

    /// <summary>
    /// Initialize the white texture used for drawing primitives.
    /// </summary>
    private static void EnsureWhiteTexture()
    {
        if (_whiteTex == null)
        {
            _whiteTex = new Texture2D(1, 1);
            _whiteTex.SetPixel(0, 0, Color.white);
            _whiteTex.Apply();
        }
    }

    /// <summary>
    /// Draw text with a subtle neon glow outline effect.
    /// </summary>
    public static void DrawGlowText(Rect rect, string text, Color textColor, GUIStyle style, Color? glowColor = null)
    {
        if (string.IsNullOrEmpty(text)) return;

        Color glow = glowColor ?? textColor;
        glow.a = Mathf.Clamp01(glow.a * 0.25f); // Subtle glow alpha

        // Draw outline in 8 directions
        Vector2 offset;
        Vector2[] offsets = new Vector2[]
        {
            new Vector2(-1, -1), new Vector2(-1, 0), new Vector2(-1, 1),
            new Vector2(0, -1), new Vector2(0, 1),
            new Vector2(1, -1), new Vector2(1, 0), new Vector2(1, 1)
        };

        GUIStyle outlineStyle = new GUIStyle(style);
        outlineStyle.normal.textColor = glow;

        foreach (Vector2 outlineOffset in offsets)
        {
            Rect outlineRect = new Rect(rect.x + outlineOffset.x, rect.y + outlineOffset.y, rect.width, rect.height);
            GUI.Label(outlineRect, text, outlineStyle);
        }

        // Draw main text
        GUIStyle mainStyle = new GUIStyle(style);
        mainStyle.normal.textColor = textColor;
        GUI.Label(rect, text, mainStyle);
    }

    /// <summary>
    /// Draw animated scanlines across the screen for cyberpunk CRT effect.
    /// </summary>
    public static void DrawScanlines(float speed = 2f, float alpha = 0.1f)
    {
        EnsureWhiteTexture();

        GUI.color = new Color(1f, 1f, 1f, alpha);

        float scanlineSpacing = 3f;
        float offset = (Time.time * speed * 100f) % scanlineSpacing;

        for (float y = -offset; y < Screen.height; y += scanlineSpacing)
        {
            GUI.DrawTexture(new Rect(0, y, Screen.width, 1f), _whiteTex);
        }

        GUI.color = Color.white;
    }

    /// <summary>
    /// Draw pulsing animated text with color lerp between base and bright glow.
    /// </summary>
    public static void DrawPulsingText(Rect rect, string text, Color baseColor, Color glowColor, GUIStyle style, float pulseSpeed = 4f)
    {
        float pulse = (Mathf.Sin(Time.time * pulseSpeed) + 1f) * 0.5f;
        Color animatedColor = Color.Lerp(baseColor, glowColor, pulse);
        DrawGlowText(rect, text, animatedColor, style, glowColor);
    }

    /// <summary>
    /// Create a standard cyberpunk label style with bold font.
    /// </summary>
    public static GUIStyle CreateCyberpunkStyle(TextAnchor alignment = TextAnchor.MiddleCenter, int fontSize = 14)
    {
        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            alignment = alignment,
            fontStyle = FontStyle.Bold,
            fontSize = fontSize
        };
        return style;
    }
}
