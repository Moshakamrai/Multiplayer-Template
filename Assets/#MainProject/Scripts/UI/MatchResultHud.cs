using System.Text;
using UnityEngine;

/// World-space match-end display: two 3D panels in front of the player, slanted inward toward them
/// (like an open book / cockpit), built entirely from code (TextMesh + quad backings — no TMP, no
/// scene setup). LEFT panel = the big WIN / LOSE / DRAW result; RIGHT panel = the persistent
/// leaderboard (top scores, name + points). Records the player's run into LeaderboardStore so it
/// survives quitting and ships in builds.
///
/// Called once from RhythmRoundManager.RpcShowMatchResult via the static Show(). It places itself in
/// front of the camera and lives until the scene reloads (which restarts a fresh match).
[DefaultExecutionOrder(10006)]
public class MatchResultHud : MonoBehaviour
{
    public static void Show(bool won, bool draw, string playerName, int matchScore)
    {
        // Record the run first so it appears in the board we're about to draw.
        int myRank = LeaderboardStore.Record(playerName, matchScore);

        var go = new GameObject("~MatchResultHud");
        var hud = go.AddComponent<MatchResultHud>();
        hud._won = won; hud._draw = draw; hud._name = playerName; hud._score = matchScore; hud._rank = myRank;
        hud.Build();
    }

    /// Lightweight per-ROUND result: a SCREEN-SPACE overlay banner (OnGUI) that auto-dismisses after a
    /// few seconds. Not the 3D panels — a clean flat card centred on screen.
    public static void ShowRoundResult(bool won, bool draw, float seconds)
    {
        var go = new GameObject("~RoundResultHud");
        var hud = go.AddComponent<MatchResultHud>();
        hud._won = won; hud._draw = draw; hud._roundOnly = true;
        hud._overlaySeconds = seconds;
        hud._overlayBornAt  = Time.unscaledTime;
        Destroy(go, seconds);
    }

    private bool  _roundOnly;
    private float _overlaySeconds;
    private float _overlayBornAt;

    private const float DISTANCE   = 3.2f;   // metres in front of the player
    private const float SIDE       = 1.25f;  // how far each panel sits left/right of center
    private const float SLANT_DEG  = 28f;    // how much each panel angles INWARD toward the player
    private const float PANEL_W    = 2.1f;
    private const float PANEL_H    = 2.6f;

    private static readonly Color WinCol  = new Color(0.30f, 1f, 0.55f);
    private static readonly Color LoseCol = new Color(1f, 0.35f, 0.35f);
    private static readonly Color DrawCol = new Color(1f, 0.85f, 0.30f);
    private static readonly Color BoardCol = new Color(0.30f, 0.9f, 1f);
    private static readonly Color PanelBg  = new Color(0.02f, 0.03f, 0.06f, 0.92f);

    private bool _won, _draw;
    private string _name;
    private int _score, _rank;
    private Font _font;
    private Material _quadMat;

    private void Build()
    {
        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
             ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
        _quadMat = new Material(Shader.Find("Sprites/Default")); // unlit, vertex-colored, always visible

        Camera cam = Camera.main;
        Vector3 camPos = cam != null ? cam.transform.position : Vector3.zero;
        Vector3 fwd = cam != null ? cam.transform.forward : Vector3.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.forward; fwd.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, fwd);

        Vector3 center = camPos + fwd * DISTANCE;
        center.y = camPos.y; // keep panels at the player's eye height

        // LEFT: result panel, slanted to face the player from the left.
        Vector3 leftPos  = center - right * SIDE;
        BuildResultPanel(leftPos, Quaternion.LookRotation(leftPos - camPos) * Quaternion.Euler(0f, -SLANT_DEG, 0f));

        // RIGHT: leaderboard panel, slanted to face the player from the right.
        Vector3 rightPos = center + right * SIDE;
        BuildBoardPanel(rightPos, Quaternion.LookRotation(rightPos - camPos) * Quaternion.Euler(0f, SLANT_DEG, 0f));
    }

    // ── Screen-space (OnGUI) round-result overlay ────────────────────────────────────────────────────
    private static Texture2D _px;
    private static Texture2D Px
    {
        get { if (_px == null) { _px = new Texture2D(1, 1); _px.SetPixel(0, 0, Color.white); _px.Apply(); } return _px; }
    }
    private static void GuiRect(Rect r, Color c)
    {
        var prev = GUI.color; GUI.color = c; GUI.DrawTexture(r, Px); GUI.color = prev;
    }

    private void OnGUI()
    {
        if (!_roundOnly) return;
        if (VRCameraDriver.VRActive) return; // VR uses its own world HUD

        int w = Screen.width, h = Screen.height;
        Color c = _draw ? DrawCol : (_won ? WinCol : LoseCol);
        string headline = _draw ? "DRAW" : (_won ? "ROUND WON" : "ROUND LOST");

        // Pop-in scale (ease-out-back) for the first ~0.32s.
        float age = Time.unscaledTime - _overlayBornAt;
        float p = Mathf.Clamp01(age / 0.32f);
        float s = 1.70158f; float u = p - 1f;
        float pop = Mathf.Lerp(0.7f, 1f, 1f + (u * u * ((s + 1f) * u + s)));
        // Fade out over the last 0.4s.
        float fade = _overlaySeconds > 0f ? Mathf.Clamp01((_overlaySeconds - age) / 0.4f) : 1f;

        // Card geometry, centred.
        float cw = w * 0.34f * pop, ch = h * 0.18f * pop;
        float cx = (w - cw) * 0.5f, cy = h * 0.20f;

        // Backing + glowing frame + accent bar.
        GuiRect(new Rect(cx - 4, cy - 4, cw + 8, ch + 8), new Color(c.r, c.g, c.b, 0.35f * fade));
        GuiRect(new Rect(cx, cy, cw, ch), new Color(0.02f, 0.03f, 0.06f, 0.92f * fade));
        GuiRect(new Rect(cx + cw * 0.10f, cy + ch * 0.62f, cw * 0.80f, Mathf.Max(2f, h * 0.004f)),
                new Color(c.r, c.g, c.b, fade));

        var head = new GUIStyle(GUI.skin.label)
        { fontSize = Mathf.RoundToInt(h * 0.06f * pop), fontStyle = FontStyle.Bold,
          alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(c.r, c.g, c.b, fade) } };
        var sub = new GUIStyle(GUI.skin.label)
        { fontSize = Mathf.RoundToInt(h * 0.022f), fontStyle = FontStyle.Bold,
          alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.78f, 0.83f, 0.95f, fade) } };

        // Depth shadow + coloured headline.
        var shadow = new GUIStyle(head) { normal = { textColor = new Color(0f, 0f, 0f, 0.8f * fade) } };
        GUI.Label(new Rect(cx + 3, cy + ch * 0.14f + 3, cw, ch * 0.45f), headline, shadow);
        GUI.Label(new Rect(cx, cy + ch * 0.14f, cw, ch * 0.45f), headline, head);
        GUI.Label(new Rect(cx, cy + ch * 0.66f, cw, ch * 0.28f), "DECIDED BY POINTS", sub);
    }

    // Small helper: a coloured, unlit, shadow-free quad parented to `parent`.
    private void MakeQuad(Transform parent, Vector3 localPos, Vector3 localScale, Color col)
    {
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "Quad";
        Destroy(quad.GetComponent<Collider>());
        quad.transform.SetParent(parent, false);
        quad.transform.localPosition = localPos;
        quad.transform.localScale = localScale;
        var mr = quad.GetComponent<MeshRenderer>();
        mr.sharedMaterial = _quadMat;
        mr.material.color = col;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    private void BuildResultPanel(Vector3 pos, Quaternion rot)
    {
        var panel = MakePanel(pos, rot, "ResultPanel");

        Color c = _draw ? DrawCol : (_won ? WinCol : LoseCol);
        string headline = _draw ? "DRAW" : (_won ? "YOU WIN" : "YOU LOSE");

        // Big headline with a dark shadow copy behind for 3D pop.
        MakeText(panel, headline, 0.45f, new Vector3(0f, 0.7f, 0f), Color.black, 0.03f);
        MakeText(panel, headline, 0.45f, new Vector3(0f, 0.7f, 0f), c, 0f);

        MakeText(panel, _name.ToUpper(), 0.16f, new Vector3(0f, 0.05f, 0f), Color.white, 0f);
        MakeText(panel, $"{_score:N0} PTS", 0.22f, new Vector3(0f, -0.35f, 0f), new Color(1f, 0.95f, 0.5f), 0f);

        string rankLine = _rank > 0 ? $"LEADERBOARD RANK  #{_rank}" : "NICE RUN";
        MakeText(panel, rankLine, 0.12f, new Vector3(0f, -0.85f, 0f), c, 0f);
    }

    private void BuildBoardPanel(Vector3 pos, Quaternion rot)
    {
        var panel = MakePanel(pos, rot, "BoardPanel");

        MakeText(panel, "LEADERBOARD", 0.2f, new Vector3(0f, 1.05f, 0f), BoardCol, 0f);

        var all = LeaderboardStore.All();
        var sb = new StringBuilder();
        int shown = Mathf.Min(10, all.Count);
        int myRowIndex = -1;
        for (int i = 0; i < shown; i++)
        {
            var e = all[i];
            bool mine = (i + 1 == _rank);
            if (mine) myRowIndex = i;
            string mark = mine ? "> " : "  ";
            string nm = e.name.Length > 10 ? e.name.Substring(0, 10) : e.name.PadRight(10);
            sb.AppendLine($"{mark}{i + 1,2}. {nm}  {e.score,8:N0}");
        }
        if (shown == 0) sb.AppendLine("No scores yet — this is the first!");

        var body = MakeText(panel, sb.ToString(), 0.115f, new Vector3(0f, 0.75f, 0f), Color.white, 0f);
        body.anchor = TextAnchor.UpperCenter;
        body.alignment = TextAlignment.Left;

        // Highlight bar behind the player's row.
        if (myRowIndex >= 0)
        {
            float rowHeight = 0.145f;
            float topY = 0.75f - myRowIndex * rowHeight - rowHeight * 0.35f;
            var highlight = GameObject.CreatePrimitive(PrimitiveType.Quad);
            highlight.name = "Highlight";
            Destroy(highlight.GetComponent<Collider>());
            highlight.transform.SetParent(panel, false);
            highlight.transform.localPosition = new Vector3(0f, topY, 0.015f);
            highlight.transform.localScale = new Vector3(PANEL_W * 0.92f, rowHeight * 1.05f, 1f);
            var hmr = highlight.GetComponent<MeshRenderer>();
            hmr.sharedMaterial = _quadMat;
            hmr.material.color = new Color(1f, 0.85f, 0.25f, 0.22f);
            hmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            hmr.receiveShadows = false;
        }

        // If the player didn't make the top 10, append their rank separately so it's still visible.
        if (_rank > 10)
        {
            string extra = $"> {_rank,2}. {_name.ToUpper().PadRight(10)}  {_score,8:N0}";
            var extraText = MakeText(panel, extra, 0.115f, new Vector3(0f, -0.85f, 0f), new Color(1f, 0.85f, 0.25f), 0f);
            extraText.anchor = TextAnchor.MiddleCenter;
            extraText.alignment = TextAlignment.Left;
        }
    }

    private Transform MakePanel(Vector3 pos, Quaternion rot, string name)
    {
        var root = new GameObject(name).transform;
        root.SetParent(transform, false);
        root.SetPositionAndRotation(pos, rot);

        // Backing quad.
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "BG";
        Destroy(quad.GetComponent<Collider>());
        quad.transform.SetParent(root, false);
        quad.transform.localPosition = new Vector3(0f, 0f, 0.02f); // just behind the text
        quad.transform.localScale = new Vector3(PANEL_W, PANEL_H, 1f);
        var mr = quad.GetComponent<MeshRenderer>();
        mr.sharedMaterial = _quadMat;
        mr.material.color = PanelBg;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        return root;
    }

    private TextMesh MakeText(Transform parent, string text, float charSize, Vector3 localPos, Color col, float shadowDepth)
    {
        var go = new GameObject("Txt");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos + new Vector3(0f, 0f, shadowDepth > 0f ? 0.012f : 0f);
        var tm = go.AddComponent<TextMesh>();
        tm.font = _font;
        tm.text = text;
        tm.fontSize = 90;
        tm.characterSize = charSize;
        tm.fontStyle = FontStyle.Bold;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = col;
        var mr = go.GetComponent<MeshRenderer>();
        if (mr != null && _font != null) mr.sharedMaterial = _font.material;
        return tm;
    }

    private void OnDestroy()
    {
        if (_quadMat != null) Destroy(_quadMat);
    }
}
