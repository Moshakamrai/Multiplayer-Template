using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

/// Builds VR world-space menus in code (like VRWorldHud) so they're visible + clickable in a
/// headset, where the flat IMGUI round picker / shop can't render. Spawns the laser pointer too.
///
/// Handles the ROUND PICKER and the post-round SHOP. Self-bootstraps; nothing to set up in a
/// scene, and it does nothing on flat builds.
[DefaultExecutionOrder(10004)]
public class VRMenus : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        var go = new GameObject("~VRMenus");
        DontDestroyOnLoad(go);
        go.AddComponent<VRMenus>();
        go.AddComponent<VRLaserPointer>(); // the pointer lives with the menus
    }

    private const float PANEL_DISTANCE = 2.4f;  // metres in front of the player
    private const float PANEL_SCALE    = 0.0016f;
    private const float SHOP_X_OFFSET  = 0f;    // centered (was -0.9 left)

    private Font _font;
    private GameObject _pickerPanel;
    private bool _pickerShown;
    private GameObject _shopPanel;
    private bool _shopShown;
    private bool _shopDirty; // a purchase happened → rebuild to refresh SOLD state + credits
    private Vector3 _shopPos; private Quaternion _shopRot; private bool _shopPlaced; // keep pose across rebuilds

    private void Awake()
    {
        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
             ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
    }

    private void Update()
    {
        if (!XRSettings.isDeviceActive) return;

        var rmm = RhythmRoundManager.Instance;
        var spm = ShopPhaseManager.Instance;

        // ── Round picker ──
        bool showPicker = rmm != null && rmm.isRoundPickerActive;
        if (showPicker && !_pickerShown) { BuildPicker(rmm); _pickerShown = true; }
        else if (!showPicker && _pickerShown) { Destroy(_pickerPanel); _pickerShown = false; }

        // ── Shop ──
        bool showShop = spm != null && spm.isShopPhase;
        if (showShop && !_shopShown) { BuildShop(spm); _shopShown = true; _shopDirty = false; }
        else if (!showShop && _shopShown) { Destroy(_shopPanel); _shopShown = false; _shopPlaced = false; }
        else if (showShop && _shopDirty) { Destroy(_shopPanel); BuildShop(spm); _shopDirty = false; }
    }

    // ── Round picker ────────────────────────────────────────────────────────────────────────
    private void BuildPicker(RhythmRoundManager rmm)
    {
        Camera cam = Camera.main;
        if (cam == null) { _pickerShown = false; return; }

        var options = rmm.GetRoundOptionsForVR();

        _pickerPanel = new GameObject("VRRoundPicker");
        PlacePanel(_pickerPanel, cam);
        var crt = MakeCanvas(_pickerPanel, new Vector2(1200, 820));
        MakeImage(crt, new Color(0.02f, 0.03f, 0.06f, 0.92f), Vector2.zero, crt.sizeDelta);

        var title = MakeText(crt, "PICK THE ROUND", 64, new Vector2(0, 340), new Vector2(1100, 90));
        title.fontStyle = FontStyle.Bold;
        title.color = new Color(1f, 0.85f, 0.2f);

        const int cols = 2;
        Vector2 btnSize = new Vector2(520, 120);
        float gapX = 40f, gapY = 28f, startY = 200f;
        for (int i = 0; i < options.Count; i++)
        {
            int row = i / cols, col = i % cols;
            int colsThisRow = Mathf.Min(cols, options.Count - row * cols);
            float rowW = colsThisRow * btnSize.x + (colsThisRow - 1) * gapX;
            float x = -rowW / 2f + btnSize.x / 2f + col * (btnSize.x + gapX);
            float y = startY - row * (btnSize.y + gapY);
            var o = options[i];
            MakeButton(crt, o.label, o.used, o.select, new Vector2(x, y), btnSize);
        }
    }

    // ── Shop ────────────────────────────────────────────────────────────────────────────────
    private void BuildShop(ShopPhaseManager spm)
    {
        Camera cam = Camera.main;
        if (cam == null) { _shopShown = false; return; }

        _shopPanel = new GameObject("VRShop");
        if (_shopPlaced) _shopPanel.transform.SetPositionAndRotation(_shopPos, _shopRot);
        else
        {
            PlacePanel(_shopPanel, cam);
            _shopPanel.transform.position += cam.transform.right * SHOP_X_OFFSET; // nudge left
            _shopPanel.transform.rotation = Quaternion.LookRotation(_shopPanel.transform.position - cam.transform.position);
            _shopPos = _shopPanel.transform.position; _shopRot = _shopPanel.transform.rotation; _shopPlaced = true;
        }
        var crt = MakeCanvas(_shopPanel, new Vector2(2080, 1280));
        crt.localScale = Vector3.one * 0.0012f; // a touch smaller than other menus — it's a wide 4-column board
        MakeImage(crt, new Color(0.015f, 0.02f, 0.045f, 0.95f), Vector2.zero, crt.sizeDelta);

        var inv    = GameManager.localPlayer != null ? GameManager.localPlayer.GetComponent<PlayerInventory>() : null;
        var oppPc  = GameManager.localPlayer != null ? GameManager.localPlayer.GetOpponent() : null;
        var oppInv = oppPc != null ? oppPc.GetComponent<PlayerInventory>() : null;

        var title = MakeText(crt, "POST-ROUND SHOP", 56, new Vector2(0, 580), new Vector2(2000, 80));
        title.fontStyle = FontStyle.Bold; title.color = new Color(0.2f, 0.95f, 1f);
        var creditsTxt = MakeText(crt, inv != null ? $"{inv.credits} CR      {inv.traitTokens} TK" : "",
            38, new Vector2(0, 512), new Vector2(2000, 56));
        creditsTxt.color = new Color(1f, 1f, 0.45f);

        const float colW = 480f, top = 430f;
        float[] colX = { -780f, -260f, 260f, 780f };
        Color teal    = new Color(0.20f, 0.95f, 0.80f), orange  = new Color(1f, 0.55f, 0.15f),
              green   = new Color(0.30f, 0.95f, 0.45f), magenta = new Color(1f, 0.20f, 0.70f);

        // 1) YOUR DECK
        ColumnHeader(crt, colX[0], top, colW, "YOUR DECK", teal);
        if (inv != null) DeckColumn(crt, colX[0], top - 82f, colW, inv);

        // 2) COMBAT CARDS (buy with credits)
        ColumnHeader(crt, colX[1], top, colW, "COMBAT CARDS", orange);
        var combat = spm.CombatShopSlots;
        if (combat != null)
            for (int i = 0; i < combat.Count; i++)
            {
                int idx = i; var card = combat[i];
                bool sold = spm.IsCombatSlotPurchased(i) || card == null;
                (Color tagC, string fam) = card != null ? FamInfo(card.family) : (Color.gray, "");
                MakeShopCard(crt, colX[1], top - 82f - i * 102f, colW, 92f,
                    card != null ? card.displayName : "", sold ? "SOLD" : $"{card.cost} CR", "",
                    tagC, sold, sold ? (System.Action)null : () =>
                    {
                        var pc = GameManager.localPlayer != null ? GameManager.localPlayer.GetComponent<PlayerCombat>() : null;
                        var iv = GameManager.localPlayer != null ? GameManager.localPlayer.GetComponent<PlayerInventory>() : null;
                        if (pc != null && iv != null && card != null && iv.credits >= card.cost && !spm.IsCombatSlotPurchased(idx))
                        { pc.CmdBuyCombatCard(idx); spm.MarkCombatSlotPurchased(idx); _shopDirty = true; }
                    });
            }

        // 3) TRAITS (buy with trait tokens)
        ColumnHeader(crt, colX[2], top, colW, "TRAITS", green);
        var traits = spm.TraitShopSlots;
        if (traits != null)
            for (int i = 0; i < traits.Count; i++)
            {
                int idx = i; var tr = traits[i];
                bool sold = spm.IsTraitSlotPurchased(i) || tr == null;
                MakeShopCard(crt, colX[2], top - 90f - i * 152f, colW, 134f,
                    tr != null ? tr.displayName : "", sold ? "SOLD" : $"{tr.cost} TK", tr != null ? tr.effect : "",
                    green, sold, sold ? (System.Action)null : () =>
                    {
                        var pc = GameManager.localPlayer != null ? GameManager.localPlayer.GetComponent<PlayerCombat>() : null;
                        var iv = GameManager.localPlayer != null ? GameManager.localPlayer.GetComponent<PlayerInventory>() : null;
                        if (pc != null && iv != null && tr != null && iv.traitTokens >= tr.cost && !spm.IsTraitSlotPurchased(idx))
                        { pc.CmdBuyTraitCard(idx); spm.MarkTraitSlotPurchased(idx); _shopDirty = true; }
                    });
            }

        // 4) OPPONENT DECK
        ColumnHeader(crt, colX[3], top, colW, "OPPONENT DECK", magenta);
        if (oppInv != null) DeckColumn(crt, colX[3], top - 82f, colW, oppInv);

        // LOCK IN & FIGHT
        var lockBtn = MakeButton(crt, "LOCK IN & FIGHT", false, () =>
        {
            var iv = GameManager.localPlayer != null ? GameManager.localPlayer.GetComponent<PlayerInventory>() : null;
            if (iv != null) spm.LockInShop(iv);
        }, new Vector2(0, -600f), new Vector2(640f, 120f));
        lockBtn.normalColor = new Color(0.10f, 0.50f, 0.20f, 0.97f);
        lockBtn.hoverColor  = new Color(0.20f, 0.90f, 0.40f, 0.98f);
        lockBtn.Refresh();
    }

    // A player's owned-card deck (name + level + family), display-only.
    private void DeckColumn(RectTransform crt, float cx, float startY, float w, PlayerInventory inv)
    {
        if (inv.ownedCombatCards == null) return;
        int i = 0;
        foreach (var id in inv.ownedCombatCards)
        {
            var card = CardDatabase.GetCombatCard(id);
            (Color tagC, string fam) = card != null ? FamInfo(card.family) : (Color.gray, "");
            int lvl = inv.GetLevel(id);
            MakeShopCard(crt, cx, startY - i * 96f, w, 86f,
                card != null ? card.displayName : id, $"Lv{lvl}", fam, tagC, false, null);
            i++;
        }
    }

    private void ColumnHeader(RectTransform crt, float cx, float top, float w, string title, Color accent)
    {
        MakeImage(crt, new Color(accent.r, accent.g, accent.b, 0.16f), new Vector2(cx, top), new Vector2(w, 58));
        MakeImage(crt, accent, new Vector2(cx, top - 31f), new Vector2(w, 3f));
        var t = MakeText(crt, title, 32, new Vector2(cx, top), new Vector2(w, 58));
        t.fontStyle = FontStyle.Bold; t.color = accent;
    }

    private (Color, string) FamInfo(CardFamily f)
    {
        switch (f)
        {
            case CardFamily.Strike: return (new Color(1f, 0.45f, 0.20f), "STRIKE");
            case CardFamily.Block:  return (new Color(0.30f, 0.60f, 1f), "BLOCK");
            case CardFamily.Parry:  return (new Color(0.80f, 0.40f, 1f), "PARRY");
            case CardFamily.Throw:  return (new Color(0.30f, 1f, 0.55f), "THROW");
            default:                return (new Color(0.70f, 0.70f, 0.70f), f.ToString().ToUpper());
        }
    }

    // A styled card row: left accent stripe, bold name, cost/level on the right, optional wrapped
    // description. If onClick != null it becomes a clickable VRButton (a buy slot).
    private void MakeShopCard(RectTransform crt, float cx, float y, float w, float h,
        string name, string info, string desc, Color tag, bool dimmed, System.Action onClick)
    {
        var go = new GameObject("Card", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(crt, false); rt.anchoredPosition = new Vector2(cx, y); rt.sizeDelta = new Vector2(w, h);

        var img = go.AddComponent<Image>();
        Color baseCol = dimmed ? new Color(0.10f, 0.10f, 0.12f, 0.70f) : new Color(0.09f, 0.11f, 0.19f, 0.96f);
        img.color = baseCol;

        MakeImage(rt, tag, new Vector2(-w / 2f + 7f, 0f), new Vector2(10f, h - 14f)); // accent stripe

        const float pad = 30f;
        bool hasDesc = !string.IsNullOrEmpty(desc);
        float topY = hasDesc ? h * 0.28f : 0f;

        var nameT = MakeText(rt, name, 30, new Vector2(pad, topY), new Vector2(w * 0.60f, hasDesc ? h * 0.42f : h));
        nameT.alignment = TextAnchor.MiddleLeft; nameT.fontStyle = FontStyle.Bold;
        if (dimmed) nameT.color = new Color(0.6f, 0.6f, 0.6f);

        var infoT = MakeText(rt, info, 26, new Vector2(-pad, topY), new Vector2(w * 0.34f, hasDesc ? h * 0.42f : h));
        infoT.alignment = TextAnchor.MiddleRight; infoT.color = dimmed ? new Color(0.5f, 0.5f, 0.5f) : new Color(1f, 1f, 0.5f);

        if (hasDesc)
        {
            var descT = MakeText(rt, desc, 20, new Vector2(pad, -h * 0.24f), new Vector2(w - pad * 2f, h * 0.5f));
            descT.alignment = TextAnchor.UpperLeft; descT.color = new Color(0.78f, 0.83f, 0.92f);
            descT.horizontalOverflow = HorizontalWrapMode.Wrap;
        }

        if (onClick != null)
        {
            var btn = go.AddComponent<VRButton>();
            btn.background = img; btn.normalColor = baseCol;
            btn.hoverColor = new Color(0.20f, 0.55f, 1f, 0.98f);
            btn.interactable = true; btn.OnClick = onClick; btn.Refresh();
            var col = go.AddComponent<BoxCollider>(); col.size = new Vector3(w, h, 30f);
        }
    }

    // ── Shared building blocks ───────────────────────────────────────────────────────────────
    private void PlacePanel(GameObject root, Camera cam)
    {
        // Placed ONCE in front of the player (static, so the laser can target it reliably).
        Vector3 fwd = cam.transform.forward; fwd.y *= 0.25f; fwd.Normalize();
        root.transform.position = cam.transform.position + fwd * PANEL_DISTANCE;
        root.transform.rotation = Quaternion.LookRotation(root.transform.position - cam.transform.position);
    }

    private RectTransform MakeCanvas(GameObject root, Vector2 size)
    {
        var canvasGo = new GameObject("Canvas");
        canvasGo.transform.SetParent(root.transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var crt = (RectTransform)canvasGo.transform;
        crt.sizeDelta = size;
        crt.localScale = Vector3.one * PANEL_SCALE;
        return crt;
    }

    private VRButton MakeButton(RectTransform parent, string label, bool used, System.Action onClick, Vector2 pos, Vector2 size)
    {
        var go = new GameObject("VRBtn", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;

        var img = go.AddComponent<Image>();
        var btn = go.AddComponent<VRButton>();
        btn.background = img;
        btn.interactable = !used;
        btn.OnClick = onClick;
        btn.Refresh();

        // Physics collider sized to the rect (local px; canvas scale converts it to world metres).
        var col = go.AddComponent<BoxCollider>();
        col.size = new Vector3(size.x, size.y, 30f);

        var txt = MakeText(rt, label, 34, Vector2.zero, size);
        txt.fontStyle = FontStyle.Bold;
        return btn;
    }

    private Image MakeImage(RectTransform parent, Color color, Vector2 pos, Vector2 size)
    {
        var go = new GameObject("Img", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        var img = go.AddComponent<Image>();
        img.color = color;
        return img;
    }

    private Text MakeText(RectTransform parent, string content, int size, Vector2 pos, Vector2 dim)
    {
        var go = new GameObject("Txt", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchoredPosition = pos;
        rt.sizeDelta = dim;
        var t = go.AddComponent<Text>();
        t.font = _font;
        t.text = content;
        t.fontSize = size;
        t.alignment = TextAnchor.MiddleCenter;
        t.color = Color.white;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }
}
