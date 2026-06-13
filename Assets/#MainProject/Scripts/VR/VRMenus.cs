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

    private const float PANEL_DISTANCE = 2.6f;  // metres in front of the player
    private const float PANEL_SCALE    = 0.0016f;
    private const float SHOP_X_OFFSET  = 0f;    // centered (was -0.9 left)

    private Font _font;
    private GameObject _pickerPanel;
    private bool _pickerShown;
    private GameObject _shopPanel;
    private bool _shopShown;
    private bool _shopDirty; // a purchase happened → rebuild to refresh SOLD state + credits
    private Vector3 _shopPos; private Quaternion _shopRot; private bool _shopPlaced; // keep pose across rebuilds
    private Text _shopTimerText; // live-updated each frame so the countdown ticks without a full rebuild

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
        else if (!showShop && _shopShown) { Destroy(_shopPanel); _shopShown = false; _shopPlaced = false; _shopTimerText = null; }
        else if (showShop && _shopDirty) { Destroy(_shopPanel); BuildShop(spm); _shopDirty = false; }
        else if (showShop && _shopTimerText != null)
        {
            // Live countdown without rebuilding the whole board.
            bool urgent = spm.shopTimeRemaining <= 15f;
            _shopTimerText.text  = $"{Mathf.Max(0f, spm.shopTimeRemaining):F0}s";
            _shopTimerText.color = urgent ? new Color(1f, 0.25f, 0.25f) : new Color(0.15f, 0.85f, 1f);
        }
    }

    // ── Round picker ────────────────────────────────────────────────────────────────────────
    private void BuildPicker(RhythmRoundManager rmm)
    {
        Camera cam = Camera.main;
        if (cam == null) { _pickerShown = false; return; }

        var options = rmm.GetRoundOptionsForVR();

        _pickerPanel = new GameObject("VRRoundPicker");
        PlacePanel(_pickerPanel, cam);
        var crt = MakeCanvas(_pickerPanel, new Vector2(1200, 900));
        MakeImage(crt, new Color(0.02f, 0.03f, 0.06f, 0.92f), Vector2.zero, crt.sizeDelta);

        var title = MakeText(crt, "PICK THE ROUND", 64, new Vector2(0, 390), new Vector2(1100, 90));
        title.fontStyle = FontStyle.Bold;
        title.color = new Color(1f, 0.85f, 0.2f);

        const int cols = 2;
        Vector2 btnSize = new Vector2(520, 260);
        float gapX = 40f, gapY = 32f, startY = 280f;
        for (int i = 0; i < options.Count; i++)
        {
            int row = i / cols, col = i % cols;
            int colsThisRow = Mathf.Min(cols, options.Count - row * cols);
            float rowW = colsThisRow * btnSize.x + (colsThisRow - 1) * gapX;
            float x = -rowW / 2f + btnSize.x / 2f + col * (btnSize.x + gapX);
            float y = startY - row * (btnSize.y + gapY);
            var o = options[i];
            MakeArtButton(crt, o.label, o.artwork, o.color, o.used, o.select, new Vector2(x, y), btnSize);
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
        // Big canvas (lots of pixels) + wide columns + TALL cards so the description has its own room
        // and the buy button sits on a separate bottom bar that can never overlap the text. Scaled up
        // in world space so it's comfortably readable in a headset.
        var crt = MakeCanvas(_shopPanel, new Vector2(3000, 1820));
        crt.localScale = Vector3.one * 0.00128f; // bigger world size than before so text is legible
        MakeImage(crt, new Color(0.015f, 0.02f, 0.045f, 0.95f), Vector2.zero, crt.sizeDelta);

        var inv    = GameManager.localPlayer != null ? GameManager.localPlayer.GetComponent<PlayerInventory>() : null;
        var oppPc  = GameManager.localPlayer != null ? GameManager.localPlayer.GetOpponent() : null;
        var oppInv = oppPc != null ? oppPc.GetComponent<PlayerInventory>() : null;

        var title = MakeText(crt, "POST-ROUND SHOP", 72, new Vector2(0, 840), new Vector2(2600, 96));
        title.fontStyle = FontStyle.Bold; title.color = new Color(0.2f, 0.95f, 1f);

        // Three info pills across the top (timer | currency | round) — mirrors the flat shop header.
        float pillW = 820f, pillH = 86f, pillY = 730f, pillGap = 36f;
        int credits = inv != null ? inv.credits : 0;
        int tokens  = inv != null ? inv.traitTokens : 0;
        bool urgent = spm.shopTimeRemaining <= 15f;
        _shopTimerText = MakeInfoPill(crt, -(pillW + pillGap), pillY, pillW, pillH,
            $"{Mathf.Max(0f, spm.shopTimeRemaining):F0}s",
            urgent ? "TIME RUNNING OUT!" : "TIME REMAINING",
            urgent ? new Color(1f, 0.25f, 0.25f) : new Color(0.15f, 0.85f, 1f));
        MakeInfoPill(crt, 0f, pillY, pillW, pillH,
            $"{credits} CR   |   {tokens} TK", "COMBAT CREDITS  |  TRAIT TOKENS",
            new Color(1f, 0.9f, 0.25f));
        MakeInfoPill(crt, pillW + pillGap, pillY, pillW, pillH,
            $"ROUND {spm.currentShopRound}", "CURRENT ROUND",
            new Color(1f, 0.30f, 0.80f));

        const float colW = 700f, top = 620f;
        float[] colX = { -1095f, -365f, 365f, 1095f };
        Color teal    = new Color(0.20f, 0.95f, 0.80f), orange  = new Color(1f, 0.55f, 0.15f),
              green   = new Color(0.30f, 0.95f, 0.45f), magenta = new Color(1f, 0.20f, 0.70f);

        // 1) YOUR DECK
        ColumnHeader(crt, colX[0], top, colW, "YOUR DECK", teal);
        if (inv != null) DeckColumn(crt, colX[0], top - 120f, colW, inv);

        // 2) COMBAT CARDS (buy with credits)
        ColumnHeader(crt, colX[1], top, colW, "COMBAT CARDS", orange);
        var combat = spm.CombatShopSlots;
        if (combat != null)
            for (int i = 0; i < combat.Count; i++)
            {
                int idx = i; var card = combat[i];
                if (card == null) continue;
                bool sold  = spm.IsCombatSlotPurchased(i);
                bool owned = inv != null && inv.ownedCombatCards.Contains(card.cardId);
                int  lvl   = owned && inv != null ? inv.GetLevel(card.cardId) : 0;
                bool maxed = owned && inv != null && inv.IsMaxLevel(card.cardId);
                bool afford = inv != null && inv.credits >= card.cost;

                (Color famC, string fam) = FamInfo(card.family);
                (Color rarC, string rar) = RarityInfo(card.rarity);

                // Button: maxed → MAX; sold → SOLD(disabled gold); can't afford → NEED; else BUY/UPGRADE.
                string btnLabel; int btnState;
                if (maxed)        { btnLabel = "★ MAX ★"; btnState = 2; }
                else if (sold)    { btnLabel = "SOLD";     btnState = 3; }
                else if (!afford) { btnLabel = $"NEED {card.cost}"; btnState = 1; }
                else              { btnLabel = owned ? "UPGRADE ◈" : "BUY ◈"; btnState = 0; }

                MakeShopCard(crt, colX[1], top - 120f - i * 232f, colW, 210f,
                    card.displayName, card.description,
                    famC, fam, rarC, rar,
                    $"◈ {card.cost}", owned ? StarStr(lvl) : "",
                    btnLabel, btnState,
                    () =>
                    {
                        var pc = GameManager.localPlayer != null ? GameManager.localPlayer.GetComponent<PlayerCombat>() : null;
                        var iv = GameManager.localPlayer != null ? GameManager.localPlayer.GetComponent<PlayerInventory>() : null;
                        if (pc != null && iv != null && iv.credits >= card.cost && !spm.IsCombatSlotPurchased(idx))
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
                if (tr == null) continue;
                bool sold = spm.IsTraitSlotPurchased(i);
                bool equipped = inv != null && inv.equippedTraitId == tr.traitId;
                bool afford = inv != null && inv.traitTokens >= tr.cost;

                string btnLabel; int btnState;
                if (equipped)     { btnLabel = "EQUIPPED";        btnState = 3; }
                else if (sold)    { btnLabel = "SOLD";            btnState = 3; }
                else if (!afford) { btnLabel = $"NEED {tr.cost}"; btnState = 1; }
                else              { btnLabel = "BUY ◈";           btnState = 0; }

                // Traits are passive — flat shop tags them green "PASSIVE" with no rarity tier label.
                MakeShopCard(crt, colX[2], top - 120f - i * 232f, colW, 210f,
                    tr.displayName, tr.effect,
                    green, "PASSIVE", green, "",
                    $"◈ {tr.cost} TK", "",
                    btnLabel, btnState,
                    () =>
                    {
                        var pc = GameManager.localPlayer != null ? GameManager.localPlayer.GetComponent<PlayerCombat>() : null;
                        var iv = GameManager.localPlayer != null ? GameManager.localPlayer.GetComponent<PlayerInventory>() : null;
                        if (pc != null && iv != null && iv.traitTokens >= tr.cost && !spm.IsTraitSlotPurchased(idx))
                        { pc.CmdBuyTraitCard(idx); spm.MarkTraitSlotPurchased(idx); _shopDirty = true; }
                    });
            }

        // 4) OPPONENT DECK
        ColumnHeader(crt, colX[3], top, colW, "OPPONENT DECK", magenta);
        if (oppInv != null) DeckColumn(crt, colX[3], top - 120f, colW, oppInv);

        // LOCK IN & FIGHT
        var lockBtn = MakeButton(crt, "LOCK IN & FIGHT", false, () =>
        {
            var iv = GameManager.localPlayer != null ? GameManager.localPlayer.GetComponent<PlayerInventory>() : null;
            if (iv != null) spm.LockInShop(iv);
        }, new Vector2(0, -820f), new Vector2(820f, 150f));
        lockBtn.normalColor = new Color(0.10f, 0.50f, 0.20f, 0.97f);
        lockBtn.hoverColor  = new Color(0.20f, 0.90f, 0.40f, 0.98f);
        lockBtn.Refresh();
    }

    // A player's owned-card deck (name + family tag + level stars), display-only — plus the equipped
    // trait shown as a "TRAIT (PASSIVE)" row at the bottom, matching the flat shop's deck columns.
    private void DeckColumn(RectTransform crt, float cx, float startY, float w, PlayerInventory inv)
    {
        Color green = new Color(0.30f, 0.95f, 0.45f);

        // Subtitle: "COMBAT (n) — drawn at random per type"
        int count = inv.ownedCombatCards != null ? inv.ownedCombatCards.Count : 0;
        var sub = MakeText(crt, $"COMBAT ({count}) — drawn at random per type", 24,
            new Vector2(cx, startY + 34f), new Vector2(w, 32f));
        sub.alignment = TextAnchor.MiddleLeft; sub.color = new Color(1f, 0.55f, 0.15f);

        int i = 0;
        if (inv.ownedCombatCards != null)
            foreach (var id in inv.ownedCombatCards)
            {
                var card = CardDatabase.GetCombatCard(id);
                (Color famC, string fam) = card != null ? FamInfo(card.family) : (Color.gray, "");
                (Color rarC, string rar) = card != null ? RarityInfo(card.rarity) : (Color.gray, "");
                int lvl = inv.GetLevel(id);
                MakeShopCard(crt, cx, startY - i * 116f, w, 104f,
                    card != null ? card.displayName : id, "",
                    famC, fam, rarC, "",
                    "", StarStr(lvl),
                    null, 0, null);
                i++;
            }

        // Equipped trait (passive) row.
        if (!string.IsNullOrEmpty(inv.equippedTraitId))
        {
            var tr = CardDatabase.GetTraitCard(inv.equippedTraitId);
            string trName = tr != null ? tr.displayName : inv.equippedTraitId;
            float trY = startY - i * 116f - 22f;
            var lbl = MakeText(crt, "TRAIT (PASSIVE)", 22, new Vector2(cx, trY + 30f), new Vector2(w, 28f));
            lbl.alignment = TextAnchor.MiddleLeft; lbl.color = new Color(0.6f, 0.6f, 0.65f);
            MakeShopCard(crt, cx, trY - 30f, w, 92f,
                trName, "",
                green, "PASSIVE", green, "",
                "", "",
                null, 0, null);
        }
    }

    // A header info pill: tinted box + accent top/bottom edges, big main text, small sub-label under it.
    // Returns the main Text so callers can live-update it (e.g. the ticking countdown timer).
    private Text MakeInfoPill(RectTransform crt, float cx, float cy, float w, float h, string main, string sub, Color accent)
    {
        MakeImage(crt, new Color(accent.r * 0.14f, accent.g * 0.14f, accent.b * 0.14f, 0.9f), new Vector2(cx, cy), new Vector2(w, h));
        MakeImage(crt, accent, new Vector2(cx, cy + h / 2f - 2f), new Vector2(w, 4f));
        MakeImage(crt, accent, new Vector2(cx, cy - h / 2f + 2f), new Vector2(w, 4f));
        var mainT = MakeText(crt, main, 42, new Vector2(cx, cy + 12f), new Vector2(w, 48f));
        mainT.fontStyle = FontStyle.Bold; mainT.color = accent;
        var subT = MakeText(crt, sub, 22, new Vector2(cx, cy - 26f), new Vector2(w, 28f));
        subT.color = new Color(0.7f, 0.72f, 0.78f);
        return mainT;
    }

    private void ColumnHeader(RectTransform crt, float cx, float top, float w, string title, Color accent)
    {
        MakeImage(crt, new Color(accent.r, accent.g, accent.b, 0.16f), new Vector2(cx, top), new Vector2(w, 70));
        MakeImage(crt, accent, new Vector2(cx, top - 37f), new Vector2(w, 4f));
        var t = MakeText(crt, title, 40, new Vector2(cx, top), new Vector2(w, 70));
        t.fontStyle = FontStyle.Bold; t.color = accent;
    }

    // Family color + tag — matched 1:1 with the flat ShopUI (GetFamilyColor/GetFamilyName) so the
    // VR board looks identical to the desktop shop screenshot.
    private (Color, string) FamInfo(CardFamily f)
    {
        switch (f)
        {
            case CardFamily.Strike: return (new Color(1f, 0.40f, 0.20f), "STRIKE");   // hot orange-red
            case CardFamily.Throw:  return (new Color(0.85f, 0.35f, 1f), "THROW");    // magenta-purple
            case CardFamily.Block:  return (new Color(0.30f, 0.60f, 1f), "BLOCK");    // blue
            case CardFamily.Parry:  return (new Color(0.15f, 0.95f, 0.95f), "PARRY"); // cyan
            default:                return (new Color(0.70f, 0.70f, 0.78f), "SUPPORT"); // grey
        }
    }

    // Rarity tier color + label, matched to the flat ShopUI rarity stripe/badge.
    private (Color, string) RarityInfo(CardRarity r)
    {
        switch (r)
        {
            case CardRarity.Advanced:  return (new Color(0.35f, 0.60f, 1f), "ADVANCED");
            case CardRarity.Legendary: return (new Color(1f, 0.75f, 0.15f), "LEGENDARY");
            default:                   return (new Color(0.60f, 0.60f, 0.60f), "BASIC");
        }
    }

    // Filled + empty stars up to the max level, e.g. Lv2 -> "★★☆" (matches ShopUI.StarStr).
    private string StarStr(int level)
    {
        level = Mathf.Clamp(level, 0, PlayerInventory.MaxLevel);
        return new string('★', level) + new string('☆', PlayerInventory.MaxLevel - level);
    }

    // Full shop card matched to the flat ShopUI card: top rarity stripe, family-colored left spine,
    // tier badge (BASIC/ADVANCED/LEGENDARY) + family tag (top-right), bold name, wrapped description,
    // ◈ cost + ★ stars (bottom-left) and a styled BUY/UPGRADE/NEED/MAX button (bottom-right).
    //
    // buttonLabel: text on the buy button ("BUY ◈", "UPGRADE ◈", null = no button row, e.g. deck cards).
    // buttonState: 0 = buyable (green, clickable), 1 = can't afford ("NEED n", red, disabled),
    //              2 = maxed ("★ MAX ★", gold, disabled), 3 = equipped ("EQUIPPED", gold, disabled).
    private void MakeShopCard(
        RectTransform crt, float cx, float y, float w, float h,
        string name, string desc,
        Color famColor, string famTag,
        Color rarityColor, string rarityTag,
        string costText, string starText,
        string buttonLabel, int buttonState, System.Action onClick)
    {
        var go = new GameObject("Card", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(crt, false); rt.anchoredPosition = new Vector2(cx, y); rt.sizeDelta = new Vector2(w, h);

        bool dimmed = buttonState == 2 || buttonState == 3;
        var img = go.AddComponent<Image>();
        Color baseCol = dimmed ? new Color(0.10f, 0.10f, 0.12f, 0.85f) : new Color(0.06f, 0.07f, 0.14f, 0.97f);
        img.color = baseCol;

        // Top rarity stripe (thin line across the very top).
        MakeImage(rt, rarityColor, new Vector2(0f, h / 2f - 4f), new Vector2(w, 8f));
        // Family-colored left spine.
        MakeImage(rt, famColor, new Vector2(-w / 2f + 7f, 0f), new Vector2(14f, h - 16f));

        const float pad = 30f;
        float spine = 14f;
        float textX = -w / 2f + spine + pad;

        // A card with a buy button gets a dedicated bottom BAR (its own strip). Everything above the
        // bar is the text zone; the bar holds cost/stars (left) + button (right). With no button (deck
        // cards) there's no bar — cost/stars sit on the bottom line instead. This hard separation is
        // what stops the button from ever overlapping the description.
        bool hasBar = !string.IsNullOrEmpty(buttonLabel);
        float barH  = hasBar ? 76f : 0f;
        float barTop = -h / 2f + barH; // text must not descend below this line

        if (hasBar)
            MakeImage(rt, new Color(0f, 0f, 0f, 0.30f), new Vector2(0f, -h / 2f + barH / 2f), new Vector2(w - 4f, barH));

        // Tier badge (top-right) + family tag box just left of it — mirrors the flat shop's badges.
        float badgeW = 150f, badgeH = 40f;
        float badgeX = w / 2f - pad - badgeW / 2f;
        float badgeY = h / 2f - pad - badgeH / 2f;
        // Family tag (filled box, family color).
        MakeImage(rt, famColor, new Vector2(badgeX, badgeY), new Vector2(badgeW, badgeH));
        var famT = MakeText(rt, famTag, 26, new Vector2(badgeX, badgeY), new Vector2(badgeW, badgeH));
        famT.fontStyle = FontStyle.Bold; famT.color = new Color(0.05f, 0.05f, 0.08f);
        // Tier label (small, to the LEFT of the family tag).
        if (!string.IsNullOrEmpty(rarityTag))
        {
            var tierT = MakeText(rt, rarityTag, 22, new Vector2(badgeX - badgeW / 2f - 80f, badgeY), new Vector2(150f, badgeH));
            tierT.alignment = TextAnchor.MiddleRight; tierT.fontStyle = FontStyle.Bold; tierT.color = rarityColor;
        }

        // Name (bold, top-left).
        float nameW = w - spine - pad * 2f - badgeW - 100f;
        float nameY = h / 2f - pad - 22f;
        var nameT = MakeText(rt, name.ToUpper(), 36, new Vector2(textX + nameW / 2f, nameY), new Vector2(nameW, 48f));
        nameT.alignment = TextAnchor.MiddleLeft; nameT.fontStyle = FontStyle.Bold;
        nameT.color = dimmed ? new Color(0.6f, 0.6f, 0.6f) : Color.white;

        // Description (wrapped). It fills the zone BETWEEN the name and the bottom bar, with explicit
        // top + bottom bounds so it can never spill onto the bar/button.
        if (!string.IsNullOrEmpty(desc))
        {
            float descTop = nameY - 40f;            // just under the name
            float descBottom = barTop + 10f;        // just above the bar
            float descH = Mathf.Max(40f, descTop - descBottom);
            float descW = w - spine - pad * 2f;
            var descT = MakeText(rt, desc, 24, new Vector2(textX + descW / 2f, descTop - descH / 2f), new Vector2(descW, descH));
            descT.alignment = TextAnchor.UpperLeft; descT.color = new Color(0.82f, 0.86f, 0.93f);
            descT.horizontalOverflow = HorizontalWrapMode.Wrap; descT.verticalOverflow = VerticalWrapMode.Truncate;
        }

        // ── Bottom bar contents: cost (left) + stars (next to it), button pinned to the far right. ──
        float barY = -h / 2f + barH / 2f;
        // For deck cards (no bar) put cost/stars on a baseline near the bottom.
        float lineY = hasBar ? barY : -h / 2f + pad + 4f;

        if (!string.IsNullOrEmpty(costText))
        {
            var costT = MakeText(rt, costText, 30, new Vector2(textX + 80f, lineY), new Vector2(170f, 40f));
            costT.alignment = TextAnchor.MiddleLeft; costT.fontStyle = FontStyle.Bold;
            costT.color = new Color(1f, 1f, 0.45f);
        }
        if (!string.IsNullOrEmpty(starText))
        {
            // Stars sit to the right of the cost on the bar; on deck cards (no button) they go far-right.
            float starX = hasBar ? textX + 250f : w / 2f - pad - 90f;
            var starT = MakeText(rt, starText, 28, new Vector2(starX, lineY), new Vector2(180f, 40f));
            starT.alignment = TextAnchor.MiddleLeft; starT.fontStyle = FontStyle.Bold;
            starT.color = new Color(1f, 0.85f, 0.2f);
        }

        if (hasBar)
        {
            float bh = 58f;
            float bw = 260f;
            Vector2 bpos = new Vector2(w / 2f - pad - bw / 2f, barY);
            var bgo = new GameObject("BuyBtn", typeof(RectTransform));
            var brt = (RectTransform)bgo.transform;
            brt.SetParent(rt, false); brt.anchoredPosition = bpos; brt.sizeDelta = new Vector2(bw, bh);
            var bimg = bgo.AddComponent<Image>();

            Color btnCol = buttonState switch
            {
                0 => new Color(0.15f, 0.65f, 0.30f, 0.92f), // buyable green
                1 => new Color(0.45f, 0.14f, 0.14f, 0.75f), // can't afford red
                _ => new Color(0.9f, 0.7f, 0.2f, 0.22f),    // maxed / equipped gold tint
            };
            bimg.color = btnCol;
            var blabel = MakeText(brt, buttonLabel, 28, Vector2.zero, new Vector2(bw, bh));
            blabel.fontStyle = FontStyle.Bold;
            blabel.color = buttonState == 1 ? new Color(1f, 0.55f, 0.55f)
                         : buttonState >= 2 ? new Color(1f, 0.8f, 0.35f)
                         : Color.white;

            if (buttonState == 0 && onClick != null)
            {
                var vbtn = bgo.AddComponent<VRButton>();
                vbtn.background = bimg; vbtn.normalColor = btnCol;
                vbtn.hoverColor = new Color(0.25f, 1f, 0.5f, 0.98f);
                vbtn.interactable = true; vbtn.OnClick = onClick; vbtn.Refresh();
                var col = bgo.AddComponent<BoxCollider>(); col.size = new Vector3(bw, bh, 30f);
            }
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
        // Crank pixel density so text + art render crisp in VR (default 1 = blurry).
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 4f;
        scaler.referencePixelsPerUnit = 100f;
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

    /// <summary>
    /// A button with artwork texture + label strip — used for the round picker so artwork is visible in VR.
    /// </summary>
    private VRButton MakeArtButton(RectTransform parent, string label, Texture2D artwork, Color color, bool used, System.Action onClick, Vector2 pos, Vector2 size)
    {
        var go = new GameObject("VRArtBtn", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;

        float stripH = size.y * 0.22f;
        float artH = size.y - stripH;

        // Background
        var img = go.AddComponent<Image>();
        img.color = new Color(0.06f, 0.06f, 0.10f, 0.95f);

        // Artwork image (fills the top portion)
        if (artwork != null)
        {
            var artGo = new GameObject("Art", typeof(RectTransform));
            var artRt = (RectTransform)artGo.transform;
            artRt.SetParent(rt, false);
            artRt.anchoredPosition = new Vector2(0, stripH * 0.5f);
            artRt.sizeDelta = new Vector2(size.x, artH);
            var rawImg = artGo.AddComponent<RawImage>();
            rawImg.texture = artwork;
            rawImg.color = used ? new Color(0.4f, 0.4f, 0.4f, 1f) : Color.white;
        }
        else
        {
            // Fallback dark panel when no artwork is assigned
            var fallback = MakeImage(rt, new Color(0.08f, 0.08f, 0.12f, 0.98f), new Vector2(0, stripH * 0.5f), new Vector2(size.x, artH));
            fallback.transform.SetParent(rt, false);
        }

        // Dark gradient at bottom of art so name strip blends in
        var grad = MakeImage(rt, new Color(0.02f, 0.03f, 0.06f, 0.55f), new Vector2(0, -artH * 0.5f + 14f), new Vector2(size.x, 28f));
        grad.transform.SetParent(rt, false);

        // Name strip background
        var strip = MakeImage(rt, new Color(0.03f, 0.04f, 0.08f, 0.97f), new Vector2(0, -size.y * 0.5f + stripH * 0.5f), new Vector2(size.x, stripH));
        strip.transform.SetParent(rt, false);

        // Accent line between art and strip
        var accentLine = MakeImage(rt, color, new Vector2(0, -artH * 0.5f), new Vector2(size.x, 3f));
        accentLine.transform.SetParent(rt, false);

        // Label text
        var txt = MakeText(rt, label, 30, new Vector2(0, -size.y * 0.5f + stripH * 0.5f), new Vector2(size.x, stripH));
        txt.fontStyle = FontStyle.Bold;
        txt.color = used ? new Color(0.5f, 0.5f, 0.5f) : color;

        // Hover glow border (drawn as an image child, VRButton will tint it)
        var glowGo = new GameObject("Glow", typeof(RectTransform));
        var glowRt = (RectTransform)glowGo.transform;
        glowRt.SetParent(rt, false);
        glowRt.anchoredPosition = Vector2.zero;
        glowRt.sizeDelta = size + new Vector2(8f, 8f);
        var glowImg = glowGo.AddComponent<Image>();
        glowImg.color = new Color(color.r, color.g, color.b, 0f);

        // Button logic
        var btn = go.AddComponent<VRButton>();
        btn.background = glowImg;
        btn.normalColor = new Color(color.r, color.g, color.b, 0f);
        btn.hoverColor  = new Color(color.r, color.g, color.b, 0.35f);
        btn.interactable = !used;
        btn.OnClick = onClick;
        btn.Refresh();

        // Collider
        var col = go.AddComponent<BoxCollider>();
        col.size = new Vector3(size.x, size.y, 30f);

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
