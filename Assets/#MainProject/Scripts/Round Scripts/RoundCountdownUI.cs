using UnityEngine;

public class RoundCountdownUI : MonoBehaviour
{
    public static RoundCountdownUI Instance;

    // ── colors per digit 5→1, then FIGHT ──────────────────────────────────
    private static readonly Color[] DigitColors =
    {
        new Color(0.00f, 1.00f, 1.00f), // 5 — cyan
        new Color(0.20f, 0.60f, 1.00f), // 4 — blue
        new Color(1.00f, 1.00f, 0.00f), // 3 — yellow
        new Color(1.00f, 0.45f, 0.00f), // 2 — orange
        new Color(1.00f, 0.08f, 0.08f), // 1 — red
    };
    private static readonly Color FightColor = new Color(0.00f, 1.00f, 0.30f); // acid green

    private enum State { Hidden, Counting, Fight }
    private State _state = State.Hidden;

    private int   _current;         // current digit being shown
    private float _digitStartTime;  // Time.time when this digit appeared
    private float _fightStartTime;

    private Texture2D _tex;

    void Awake()
    {
        if (Instance == null) Instance = this;
        _tex = new Texture2D(1, 1);
        _tex.SetPixel(0, 0, Color.white);
        _tex.Apply();
    }

    public void StartCountdown(int from = 5)
    {
        _current        = from;
        _digitStartTime = Time.time;
        _state          = State.Counting;
    }

    void Update()
    {
        if (_state == State.Counting && Time.time - _digitStartTime >= 1f)
        {
            _current--;
            _digitStartTime = Time.time;
            if (_current <= 0)
            {
                _state         = State.Fight;
                _fightStartTime = Time.time;
            }
        }
        else if (_state == State.Fight && Time.time - _fightStartTime >= 0.9f)
        {
            _state = State.Hidden;
        }
    }

    void OnGUI()
    {
        if (_state == State.Hidden) return;

        float cx = Screen.width  * 0.5f;
        float cy = Screen.height * 0.5f;
        float elapsed = Time.time - (_state == State.Fight ? _fightStartTime : _digitStartTime);

        // ── resolve text, color, scale ─────────────────────────────────────
        string text;
        Color  col;
        float  startScale;

        if (_state == State.Counting)
        {
            text       = _current.ToString();
            int idx    = Mathf.Clamp(5 - _current, 0, DigitColors.Length - 1);
            col        = DigitColors[idx];
            startScale = 2.2f;
        }
        else
        {
            text       = "FIGHT!";
            col        = FightColor;
            startScale = 2.8f;
        }

        // punch-in: cubic ease-out over 0.25 s, tiny undershoot bounce
        float t     = Mathf.Clamp01(elapsed / 0.25f);
        float eased = 1f - Mathf.Pow(1f - t, 3f);
        float scale = Mathf.Lerp(startScale, 1f, eased);

        // very slight bounce back at settle (only during first 0.35 s)
        if (elapsed > 0.25f && elapsed < 0.35f)
        {
            float bounceT = (elapsed - 0.25f) / 0.10f;
            scale = Mathf.Lerp(1f, 0.92f, Mathf.Sin(bounceT * Mathf.PI));
        }

        // fade-out in the last 0.25 s of the digit's lifetime
        float alpha = 1f;
        float lifetime = (_state == State.Fight) ? 0.9f : 1f;
        if (elapsed > lifetime - 0.25f)
            alpha = 1f - Mathf.Clamp01((elapsed - (lifetime - 0.25f)) / 0.25f);

        // ── dark full-screen overlay, tinted toward digit color ────────────
        GUI.color = new Color(col.r * 0.08f, col.g * 0.08f, col.b * 0.08f, 0.55f * alpha);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _tex);
        GUI.color = Color.white;

        // ── expanding ring ─────────────────────────────────────────────────
        float ring    = Mathf.Clamp01(elapsed / lifetime) * 380f;
        float ringAlpha = (1f - Mathf.Clamp01(elapsed / lifetime)) * 0.8f * alpha;
        DrawRect(cx - ring, cy - ring * 0.55f, ring * 2f, ring * 1.1f, 3f,
                 new Color(col.r, col.g, col.b, ringAlpha));

        // inner tight ring (slightly delayed)
        float ring2 = Mathf.Clamp01(Mathf.Max(0, elapsed - 0.1f) / lifetime) * 220f;
        float ring2Alpha = (1f - Mathf.Clamp01(Mathf.Max(0, elapsed - 0.1f) / lifetime)) * 0.5f * alpha;
        DrawRect(cx - ring2, cy - ring2 * 0.55f, ring2 * 2f, ring2 * 1.1f, 2f,
                 new Color(col.r, col.g, col.b, ring2Alpha));

        // ── scanline overlay ───────────────────────────────────────────────
        CyberpunkGUIUtils.DrawScanlines(1.5f, 0.06f * alpha);

        // ── glow shadow (8-direction offset) ──────────────────────────────
        int fontSize     = Mathf.RoundToInt(160f * scale);
        float labelW     = 500f;
        float labelH     = 240f;
        float lx         = cx - labelW * 0.5f;
        float ly         = cy - labelH * 0.5f;

        GUIStyle style   = new GUIStyle(GUI.skin.label)
        {
            fontSize  = fontSize,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };

        // glow pass — wide soft offset
        float gOff = fontSize * 0.025f;
        style.normal.textColor = new Color(col.r, col.g, col.b, 0.35f * alpha);
        for (int dx = -2; dx <= 2; dx++)
        for (int dy = -2; dy <= 2; dy++)
        {
            if (dx == 0 && dy == 0) continue;
            GUI.Label(new Rect(lx + dx * gOff, ly + dy * gOff, labelW, labelH), text, style);
        }

        // main text
        style.normal.textColor = new Color(col.r, col.g, col.b, alpha);
        GUI.Label(new Rect(lx, ly, labelW, labelH), text, style);

        // bright white core highlight (makes it feel lit)
        style.normal.textColor = new Color(1f, 1f, 1f, 0.25f * alpha * Mathf.Clamp01(1f - elapsed / 0.3f));
        GUI.Label(new Rect(lx, ly, labelW, labelH), text, style);
    }

    // Draws a hollow rectangle outline using 4 filled rects
    private void DrawRect(float x, float y, float w, float h, float thickness, Color col)
    {
        GUI.color = col;
        GUI.DrawTexture(new Rect(x,             y,             w,         thickness), _tex); // top
        GUI.DrawTexture(new Rect(x,             y + h,         w,         thickness), _tex); // bottom
        GUI.DrawTexture(new Rect(x,             y,             thickness, h        ), _tex); // left
        GUI.DrawTexture(new Rect(x + w,         y,             thickness, h        ), _tex); // right
        GUI.color = Color.white;
    }
}
