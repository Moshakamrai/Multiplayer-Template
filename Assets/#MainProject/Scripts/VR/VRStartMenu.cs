using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

/// VR-only entry screen shown BEFORE the first round picker — breathing room. Two world-space panels
/// facing the player:
///   • CENTER: a big 3D "PLAY" button → opens the existing level/song round picker.
///   • RIGHT:  the persistent LEADERBOARD (top scores from LeaderboardStore), angled toward the player.
///
/// Self-bootstraps (like VRMenus). Appears while RhythmRoundManager.WaitingForVRStart is true; pressing
/// Play sends CmdBeginFromStartMenu to the server, which dismisses it and shows the round picker.
[DefaultExecutionOrder(10004)]
public class VRStartMenu : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        var go = new GameObject("~VRStartMenu");
        DontDestroyOnLoad(go);
        go.AddComponent<VRStartMenu>();
    }

    private const float DISTANCE   = 2.6f;   // metres in front of the player
    private const float PANEL_SCALE = 0.0016f;
    private const float BOARD_SIDE = 1.7f;   // how far right the leaderboard sits
    private const float BOARD_SLANT = 26f;   // angle the board inward toward the player

    private Font _font;
    private GameObject _root;
    private bool _shown;
    private bool _placed;
    private Vector3 _pos; private Quaternion _rot;

    // Arcade 3-letter initials.
    private readonly char[] _initials = { 'A', 'A', 'A' };
    private bool _initialsInit;
    private readonly Text[] _initialLabels = new Text[3];

    private void Awake()
    {
        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
             ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
    }

    private void Update()
    {
        if (!VRCameraDriver.VRActive) return; // real VR or the editor VR-sim
        var rmm = RhythmRoundManager.Instance;
        bool show = rmm != null && rmm.WaitingForVRStart;

        if (show && !_shown) { Build(rmm); _shown = true; }
        else if (!show && _shown) { if (_root != null) Destroy(_root); _shown = false; _placed = false; }
    }

    private void Build(RhythmRoundManager rmm)
    {
        Camera cam = Camera.main;
        if (cam == null) { _shown = false; return; }

        _root = new GameObject("VRStartMenu");

        if (!_placed)
        {
            Vector3 fwd = cam.transform.forward; fwd.y *= 0.25f; fwd.Normalize();
            _pos = cam.transform.position + fwd * DISTANCE;
            _rot = Quaternion.LookRotation(_pos - cam.transform.position);
            _placed = true;
        }
        _root.transform.SetPositionAndRotation(_pos, _rot);

        Vector3 right = cam.transform.right; right.y = 0f;
        right = right.sqrMagnitude > 0.001f ? right.normalized : Vector3.right;

        EnsureInitials();
        BuildPlayButton(rmm, cam);
        BuildLeaderboard(cam, right);
        BuildNamePanel(cam, right);
    }

    private void BuildPlayButton(RhythmRoundManager rmm, Camera cam)
    {
        var panel = new GameObject("PlayPanel");
        panel.transform.SetParent(_root.transform, false);
        panel.transform.localPosition = Vector3.zero;

        var crt = MakeCanvas(panel, new Vector2(960, 1000));

        // Decorative backing + accent bars so the centerpiece looks built, not bare.
        MakeImage(crt, new Color(0.03f, 0.05f, 0.09f, 0.85f), new Vector2(0, -20), new Vector2(760, 620));
        MakeImage(crt, new Color(0.2f, 0.95f, 1f, 0.9f), new Vector2(0, 290), new Vector2(760, 6));   // top accent
        MakeImage(crt, new Color(0.2f, 0.95f, 1f, 0.9f), new Vector2(0, -330), new Vector2(760, 6));  // bottom accent

        var title = MakeText(crt, "RHYTHM BOXER", 78, new Vector2(0, 210), new Vector2(900, 120));
        title.fontStyle = FontStyle.Bold; title.color = new Color(0.2f, 0.95f, 1f);

        // Big PLAY button with a glow frame behind it.
        MakeImage(crt, new Color(0.15f, 1f, 0.5f, 0.25f), new Vector2(0, -30), new Vector2(580, 300)); // glow frame
        var bgo = new GameObject("PlayBtn", typeof(RectTransform));
        var brt = (RectTransform)bgo.transform;
        brt.SetParent(crt, false); brt.anchoredPosition = new Vector2(0, -30); brt.sizeDelta = new Vector2(520, 240);
        var bimg = bgo.AddComponent<Image>();
        bimg.color = new Color(0.10f, 0.55f, 0.25f, 0.97f);

        var label = MakeText(brt, "▶  PLAY", 96, Vector2.zero, new Vector2(520, 240));
        label.fontStyle = FontStyle.Bold; label.color = Color.white;

        var vbtn = bgo.AddComponent<VRButton>();
        vbtn.background = bimg;
        vbtn.normalColor = new Color(0.10f, 0.55f, 0.25f, 0.97f);
        vbtn.hoverColor  = new Color(0.20f, 0.95f, 0.45f, 0.98f);
        vbtn.interactable = true;
        vbtn.OnClick = () =>
        {
            ResolveNameCollision();   // if the chosen name is taken, auto-randomize it
            CommitName();
            if (rmm != null) rmm.CmdBeginFromStartMenu();
        };
        vbtn.Refresh();
        var col = bgo.AddComponent<BoxCollider>(); col.size = new Vector3(520, 240, 600f);

        MakeText(crt, "Punch to the beat. Highest score tops the board.", 30,
            new Vector2(0, -300), new Vector2(900, 50)).color = new Color(0.7f, 0.75f, 0.85f);
    }

    // ── 3-letter arcade initials (name) ─────────────────────────────────────────────────────────
    // Seed from a previously chosen name, else random initials so a skip still gives a non-default name.
    private void EnsureInitials()
    {
        if (_initialsInit) return;
        _initialsInit = true;
        string prev = GameManager.PlayerName;
        if (!string.IsNullOrEmpty(prev) && prev != GameManager.DefaultPlayerName && prev.Length >= 1)
        {
            for (int i = 0; i < 3; i++)
            {
                char c = i < prev.Length ? char.ToUpper(prev[i]) : 'A';
                _initials[i] = (c >= 'A' && c <= 'Z') ? c : 'A';
            }
        }
        else
        {
            for (int i = 0; i < 3; i++) _initials[i] = (char)('A' + Random.Range(0, 26)); // random default
        }
    }

    private string CurrentName() => new string(_initials);

    private void CommitName()
    {
        string n = CurrentName();
        GameManager.SetPlayerName(n);            // persists to PlayerPrefs
        var lp = GameManager.localPlayer;
        if (lp != null) lp.SetNameNetworked(n);  // re-sync the networked PlayerName
    }

    // If the chosen initials are already on the leaderboard, auto-randomize to a free set (so two
    // players can't share a name / overwrite each other). Updates the on-screen letters too.
    private void ResolveNameCollision()
    {
        if (!LeaderboardStore.NameExists(CurrentName())) return;
        for (int attempt = 0; attempt < 50 && LeaderboardStore.NameExists(CurrentName()); attempt++)
            for (int i = 0; i < 3; i++) _initials[i] = (char)('A' + Random.Range(0, 26));
        for (int i = 0; i < 3; i++)
            if (_initialLabels[i] != null) _initialLabels[i].text = _initials[i].ToString();
    }

    private void CycleInitial(int slot, int dir)
    {
        int v = _initials[slot] - 'A';
        v = ((v + dir) % 26 + 26) % 26;
        _initials[slot] = (char)('A' + v);
        if (_initialLabels[slot] != null) _initialLabels[slot].text = _initials[slot].ToString();
        CommitName(); // keep the name live as they change it
    }

    private void BuildNamePanel(Camera cam, Vector3 right)
    {
        var panel = new GameObject("NamePanel");
        panel.transform.SetParent(_root.transform, false);
        // Offset to the player's LEFT, slanted inward to face them.
        panel.transform.position = _root.transform.position - right * BOARD_SIDE;
        panel.transform.rotation = Quaternion.LookRotation(panel.transform.position - cam.transform.position)
                                 * Quaternion.Euler(0f, -BOARD_SLANT, 0f);

        var crt = MakeCanvas(panel, new Vector2(900, 1100));
        MakeImage(crt, new Color(0.02f, 0.03f, 0.06f, 0.92f), Vector2.zero, crt.sizeDelta);

        var hdr = MakeText(crt, "YOUR NAME", 54, new Vector2(0, 430), new Vector2(860, 80));
        hdr.fontStyle = FontStyle.Bold; hdr.color = new Color(1f, 0.85f, 0.25f);

        // Three initial slots, each with ▲ above and ▼ below.
        float[] slotX = { -240f, 0f, 240f };
        for (int i = 0; i < 3; i++)
        {
            int slot = i;
            MakeCycleButton(crt, "▲", new Vector2(slotX[i], 200f), () => CycleInitial(slot, +1));
            var letter = MakeText(crt, _initials[i].ToString(), 130, new Vector2(slotX[i], 40f), new Vector2(180, 200));
            letter.fontStyle = FontStyle.Bold; letter.color = Color.white;
            _initialLabels[i] = letter;
            MakeCycleButton(crt, "▼", new Vector2(slotX[i], -120f), () => CycleInitial(slot, -1));
        }

        // RANDOM button.
        MakeActionButton(crt, "RANDOM", new Vector2(0, -300f), new Vector2(360, 110),
            new Color(0.3f, 0.35f, 0.6f, 0.95f), () =>
            {
                for (int i = 0; i < 3; i++)
                {
                    _initials[i] = (char)('A' + Random.Range(0, 26));
                    if (_initialLabels[i] != null) _initialLabels[i].text = _initials[i].ToString();
                }
                CommitName();
            });

        MakeText(crt, "(or just hit PLAY for a random name)", 26, new Vector2(0, -420f), new Vector2(860, 40))
            .color = new Color(0.6f, 0.65f, 0.75f);
    }

    private void MakeCycleButton(RectTransform crt, string glyph, Vector2 pos, System.Action onClick)
    {
        MakeActionButton(crt, glyph, pos, new Vector2(150, 130), new Color(0.12f, 0.4f, 0.8f, 0.95f), onClick, 80);
    }

    private void MakeActionButton(RectTransform crt, string label, Vector2 pos, Vector2 size,
                                  Color col, System.Action onClick, int fontSize = 40)
    {
        var bgo = new GameObject("Btn", typeof(RectTransform));
        var brt = (RectTransform)bgo.transform;
        brt.SetParent(crt, false); brt.anchoredPosition = pos; brt.sizeDelta = size;
        var bimg = bgo.AddComponent<Image>(); bimg.color = col;
        var t = MakeText(brt, label, fontSize, Vector2.zero, size); t.fontStyle = FontStyle.Bold;
        var vbtn = bgo.AddComponent<VRButton>();
        vbtn.background = bimg; vbtn.normalColor = col;
        vbtn.hoverColor = new Color(Mathf.Min(1f, col.r + 0.3f), Mathf.Min(1f, col.g + 0.3f), Mathf.Min(1f, col.b + 0.3f), 0.98f);
        vbtn.interactable = true; vbtn.OnClick = onClick; vbtn.Refresh();
        var bc = bgo.AddComponent<BoxCollider>(); bc.size = new Vector3(size.x, size.y, 600f);
    }

    private void BuildLeaderboard(Camera cam, Vector3 right)
    {
        var panel = new GameObject("LeaderboardPanel");
        panel.transform.SetParent(_root.transform, false);
        // Offset to the player's right, slanted inward to face them.
        panel.transform.position = _root.transform.position + right * BOARD_SIDE;
        panel.transform.rotation = Quaternion.LookRotation(panel.transform.position - cam.transform.position)
                                 * Quaternion.Euler(0f, BOARD_SLANT, 0f);

        var crt = MakeCanvas(panel, new Vector2(1000, 1100));
        MakeImage(crt, new Color(0.02f, 0.03f, 0.06f, 0.92f), Vector2.zero, crt.sizeDelta);

        var hdr = MakeText(crt, "LEADERBOARD", 56, new Vector2(0, 470), new Vector2(960, 80));
        hdr.fontStyle = FontStyle.Bold; hdr.color = new Color(0.3f, 0.9f, 1f);

        var entries = LeaderboardStore.All();
        int shown = Mathf.Min(10, entries.Count);
        float y = 360f;
        if (shown == 0)
        {
            MakeText(crt, "No scores yet —\nbe the first!", 40, new Vector2(0, 100), new Vector2(900, 200))
                .color = new Color(0.7f, 0.75f, 0.85f);
        }
        for (int i = 0; i < shown; i++)
        {
            var e = entries[i];
            string nm = e.name.Length > 12 ? e.name.Substring(0, 12) : e.name;
            Color rowCol = i == 0 ? new Color(1f, 0.85f, 0.25f)
                         : i == 1 ? new Color(0.85f, 0.85f, 0.9f)
                         : i == 2 ? new Color(0.85f, 0.55f, 0.3f)
                         : Color.white;
            var rank = MakeText(crt, $"{i + 1}.", 40, new Vector2(-420, y), new Vector2(90, 60));
            rank.alignment = TextAnchor.MiddleRight; rank.color = rowCol; rank.fontStyle = FontStyle.Bold;
            var name = MakeText(crt, nm, 40, new Vector2(-150, y), new Vector2(420, 60));
            name.alignment = TextAnchor.MiddleLeft; name.color = rowCol;
            var sc = MakeText(crt, e.score.ToString("N0"), 40, new Vector2(330, y), new Vector2(280, 60));
            sc.alignment = TextAnchor.MiddleRight; sc.color = rowCol; sc.fontStyle = FontStyle.Bold;
            y -= 76f;
        }
    }

    // ── building blocks (mirrors VRMenus) ──────────────────────────────────────────────────────
    private RectTransform MakeCanvas(GameObject root, Vector2 size)
    {
        var canvasGo = new GameObject("Canvas");
        canvasGo.transform.SetParent(root.transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 4f;
        var crt = (RectTransform)canvasGo.transform;
        crt.sizeDelta = size;
        crt.localScale = Vector3.one * PANEL_SCALE;
        return crt;
    }

    private Image MakeImage(RectTransform parent, Color color, Vector2 pos, Vector2 size)
    {
        var go = new GameObject("Img", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false); rt.anchoredPosition = pos; rt.sizeDelta = size;
        var img = go.AddComponent<Image>(); img.color = color;
        return img;
    }

    private Text MakeText(RectTransform parent, string content, int size, Vector2 pos, Vector2 dim)
    {
        var go = new GameObject("Txt", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false); rt.anchoredPosition = pos; rt.sizeDelta = dim;
        var t = go.AddComponent<Text>();
        t.font = _font; t.text = content; t.fontSize = size;
        t.alignment = TextAnchor.MiddleCenter; t.color = Color.white;
        t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }
}
