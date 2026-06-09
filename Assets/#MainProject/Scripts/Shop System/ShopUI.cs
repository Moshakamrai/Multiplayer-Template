using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class ShopUI : MonoBehaviour
{
    public static ShopUI Instance { get; private set; }

    private Texture2D _whiteTex;
    private GUIStyle _titleStyle;
    private GUIStyle _cardNameStyle;
    private GUIStyle _descStyle;
    private GUIStyle _costStyle;
    private GUIStyle _timerStyle;
    private GUIStyle _sectionStyle;
    private GUIStyle _buttonStyle;

    // Layout constants
    private const float LEFT_SIDEBAR_W = 0.22f;
    private const float SHOP_W = 0.52f;
    private const float RIGHT_SIDEBAR_W = 0.22f;
    private const float MARGIN = 14f;
    private const float TOP_H = 90f;

    // Hover tooltip state
    private string _hoveredCardId = "";
    private Vector2 _tooltipPos = Vector2.zero;
    private string _tooltipText = "";
    private string _tooltipTitle = "";
    private bool _hasTooltip = false;

    // Virtual screen dims — equal to the real screen on desktop, or a fixed
    // 1920x1080 reference on Android (scaled up via MobileGUI) so buttons stay
    // a tappable size on high-DPI phones. Set at the top of OnGUI.
    private float _vw, _vh;
    private Matrix4x4 _guiPrev;

    private void Awake() { if (Instance == null) Instance = this; }

    private void OnGUI()
    {
        if (VRCameraDriver.VRActive) return; // VR uses the world-space shop panel (VRMenus)
        if (ShopPhaseManager.Instance == null || !ShopPhaseManager.Instance.isShopPhase) return;

        // Scale the whole shop for phones (no-op on desktop).
        _guiPrev = MobileGUI.Begin(out _vw, out _vh);

        // Reset hover state each frame
        _hasTooltip = false;
        _hoveredCardId = "";

        EnsureTextures();
        EnsureStyles();

        var spm = ShopPhaseManager.Instance;
        var localInv = GameManager.localPlayer?.GetComponent<PlayerInventory>();
        var botInv = GetBotInventory();

        // Dark neon background
        GUI.color = new Color(0.01f, 0.02f, 0.06f, 1f);
        GUI.DrawTexture(new Rect(0, 0, _vw, _vh), _whiteTex);
        GUI.color = Color.white;

        // Calculate layout
        float leftW = _vw * LEFT_SIDEBAR_W;
        float shopW = _vw * SHOP_W;
        float rightW = _vw * RIGHT_SIDEBAR_W;

        float leftX = MARGIN;
        float shopX = leftX + leftW + MARGIN;
        float rightX = shopX + shopW + MARGIN;

        float contentY = TOP_H + MARGIN;
        float contentH = _vh - contentY - 90f;

        // ── HEADER ──
        DrawHeader(spm, localInv);

        // ── THREE MAIN SECTIONS ──
        DrawPlayerInventoryPanel(leftX, contentY, leftW - MARGIN, contentH, localInv);
        DrawShopPanels(shopX, contentY, shopW - MARGIN, contentH, localInv, spm);
        DrawBotInventoryPanel(rightX, contentY, rightW - MARGIN, contentH, botInv);

        // ── FOOTER: Lock In button ──
        DrawLockInButton();

        // ── HOVER TOOLTIP (drawn last, on top) ──
        if (_hasTooltip)
            DrawTooltip();

        MobileGUI.End(_guiPrev);
    }

    private void DrawHeader(ShopPhaseManager spm, PlayerInventory inv)
    {
        // Animated gradient header bar
        float pulse = (Mathf.Sin(Time.time * 3f) + 1f) * 0.5f;
        GUI.color = new Color(0f, 0.9f, 0.7f, 0.08f + pulse * 0.06f);
        GUI.DrawTexture(new Rect(0, 0, _vw, 70), _whiteTex);
        GUI.color = Color.white;

        // Title with stronger glow
        CyberpunkGUIUtils.DrawGlowText(new Rect(0, 6, _vw, 42), "⚡  POST-ROUND SHOP  ⚡",
            CyberpunkGUIUtils.NEON_CYAN, _titleStyle, CyberpunkGUIUtils.NEON_CYAN);

        // Decorative underline
        GUI.color = new Color(0f, 1f, 0.8f, 0.4f + pulse * 0.3f);
        GUI.DrawTexture(new Rect(_vw * 0.25f, 48, _vw * 0.5f, 2f), _whiteTex);
        GUI.color = Color.white;

        // Top bar with info — three distinct pill-shaped panels
        float barY = 54f;
        float pillH = 30f;
        float pillGap = 16f;
        float totalPillW = _vw - MARGIN * 2f;
        float pillW = (totalPillW - pillGap * 2f) / 3f;

        // Timer pill
        Color timerColor = spm.shopTimeRemaining <= 15f ? new Color(1f, 0.2f, 0.2f) : new Color(0f, 0.85f, 1f);
        DrawInfoPill(MARGIN, barY, pillW, pillH, $"⏱  {Mathf.Max(0f, spm.shopTimeRemaining):F0}s", timerColor,
            spm.shopTimeRemaining <= 15f ? "TIME RUNNING OUT!" : "TIME REMAINING");

        // Currency pill
        int credits = inv != null ? inv.credits : 0;
        int tokens = inv != null ? inv.traitTokens : 0;
        DrawInfoPill(MARGIN + pillW + pillGap, barY, pillW, pillH,
            $"💰 {credits} CR  |  🟢 {tokens} TK", new Color(1f, 0.9f, 0.2f),
            "COMBAT CREDITS  |  TRAIT TOKENS");

        // Round pill
        DrawInfoPill(MARGIN + (pillW + pillGap) * 2f, barY, pillW, pillH,
            $"ROUND {spm.currentShopRound}", new Color(1f, 0.3f, 0.8f), "CURRENT ROUND");
    }

    private void DrawInfoPill(float x, float y, float w, float h, string mainText, Color accent, string subText)
    {
        // Pill background
        GUI.color = new Color(accent.r * 0.12f, accent.g * 0.12f, accent.b * 0.12f, 0.85f);
        GUI.DrawTexture(new Rect(x, y, w, h), _whiteTex);

        // Pill border glow
        GUI.color = new Color(accent.r, accent.g, accent.b, 0.5f);
        GUI.DrawTexture(new Rect(x, y, w, 1.5f), _whiteTex);
        GUI.DrawTexture(new Rect(x, y + h - 1.5f, w, 1.5f), _whiteTex);

        // Main text
        GUIStyle mainSt = new GUIStyle(GUI.skin.label)
        {
            fontSize = 15,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        CyberpunkGUIUtils.DrawGlowText(new Rect(x, y, w, h), mainText, accent, mainSt, accent);

        // Sub label (tiny, below the pill)
        GUIStyle subSt = new GUIStyle(GUI.skin.label)
        {
            fontSize = 9,
            alignment = TextAnchor.MiddleCenter
        };
        subSt.normal.textColor = new Color(accent.r, accent.g, accent.b, 0.5f);
        GUI.color = Color.white;
        GUI.Label(new Rect(x, y + h + 2f, w, 14f), subText, subSt);
    }

    private void DrawPlayerInventoryPanel(float x, float y, float w, float h, PlayerInventory inv)
    {
        DrawPanelBorder(x, y, w, h, new Color(0f, 0.8f, 0.6f));
        DrawSectionHeader(x, y, w, "YOUR DECK", new Color(0f, 0.8f, 0.6f));

        float cardY = y + 40f;
        float cardW = w - MARGIN * 2;
        float cardH = 50f;
        float pad = 8f;

        if (inv != null && (inv.ownedCombatCards.Count > 0 || !string.IsNullOrEmpty(inv.equippedTraitId)))
        {
            // Combat cards with capacity indicator
            if (inv.ownedCombatCards.Count > 0)
            {
                GUIStyle combatHeaderStyle = new GUIStyle(_descStyle);
                CyberpunkGUIUtils.DrawGlowText(new Rect(x + MARGIN, cardY, cardW, 22), $"COMBAT ({inv.ownedCombatCards.Count}) — drawn at random per type", CyberpunkGUIUtils.NEON_ORANGE, combatHeaderStyle, CyberpunkGUIUtils.NEON_ORANGE);
                cardY += 24f;

                foreach (var cardId in inv.ownedCombatCards)
                {
                    if (cardY + cardH > y + h - 10) break;
                    DrawInventoryCard(x + MARGIN, cardY, cardW, cardH, cardId, "Combat", true);
                    cardY += cardH + pad;
                }
            }

            // Active trait
            if (!string.IsNullOrEmpty(inv.equippedTraitId))
            {
                GUIStyle traitHeaderStyle = new GUIStyle(_descStyle);
                CyberpunkGUIUtils.DrawGlowText(new Rect(x + MARGIN, cardY, cardW, 22), "TRAIT (PASSIVE)", CyberpunkGUIUtils.NEON_GREEN, traitHeaderStyle, CyberpunkGUIUtils.NEON_GREEN);
                cardY += 24f;
                DrawInventoryCard(x + MARGIN, cardY, cardW, cardH, inv.equippedTraitId, "Trait", false);
            }
        }
        else
        {
            GUI.color = new Color(0.5f, 0.5f, 0.5f);
            GUI.Label(new Rect(x + MARGIN, cardY, cardW, 30), "No cards owned yet", _descStyle);
        }
    }

    private void DrawInventoryCard(float x, float y, float w, float h, string cardId, string category, bool canRemove)
    {
        string displayName = cardId;
        string description = "";
        int cost = 0;
        Color rarityColor = new Color(0.6f, 0.6f, 0.6f);
        Color sideColor = rarityColor;
        string famTag = "";

        // Get card data
        if (category == "Combat")
        {
            var card = CardDatabase.BasicCards.Concat(CardDatabase.AdvancedCards).Concat(CardDatabase.LegendaryCards)
                .FirstOrDefault(c => c.cardId == cardId);
            if (card != null)
            {
                displayName = card.displayName;
                description = card.description;
                cost = card.cost;
                rarityColor = card.rarity == CardRarity.Basic ? new Color(0.6f, 0.6f, 0.6f) :
                             card.rarity == CardRarity.Advanced ? new Color(0.35f, 0.6f, 1f) :
                             new Color(1f, 0.75f, 0.15f);
                sideColor = GetFamilyColor(card.family);
                famTag = GetFamilyName(card.family);
            }
        }
        else if (category == "Trait")
        {
            var card = CardDatabase.TraitCards.FirstOrDefault(c => c.traitId == cardId);
            if (card != null)
            {
                displayName = card.displayName;
                description = card.description;
                cost = card.cost;
                rarityColor = new Color(0.25f, 0.9f, 0.4f);
                sideColor = rarityColor;
            }
        }

        Rect cardRect = new Rect(x, y, w - (canRemove ? 52f : 0), h);
        bool isHovered = cardRect.Contains(Event.current.mousePosition);

        // Card background with rarity
        GUI.color = isHovered
            ? new Color(rarityColor.r * 0.35f, rarityColor.g * 0.35f, rarityColor.b * 0.35f)
            : new Color(rarityColor.r * 0.2f, rarityColor.g * 0.2f, rarityColor.b * 0.2f);
        GUI.DrawTexture(cardRect, _whiteTex);

        // Family-colored left border
        GUI.color = sideColor;
        GUI.DrawTexture(new Rect(x, y, isHovered ? 5f : 3f, h), _whiteTex);

        // Text
        GUI.color = Color.white;
        GUI.Label(new Rect(x + 10f, y + 4f, cardRect.width - 70f, 20f), displayName.ToUpper(), _cardNameStyle);

        // Family tag (small, top-right) for combat cards
        if (!string.IsNullOrEmpty(famTag))
        {
            GUIStyle famSt = new GUIStyle(GUI.skin.label) { fontSize = 10, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight };
            famSt.normal.textColor = sideColor;
            GUI.Label(new Rect(x + cardRect.width - 60f, y + 4f, 54f, 16f), famTag, famSt);
        }
        _costStyle.normal.textColor = new Color(1f, 1f, 0f, 0.7f);
        GUI.Label(new Rect(x + 10f, y + 24f, 50f, 16f), $"◈ {cost}", _costStyle);

        // Hover
        if (cardRect.Contains(Event.current.mousePosition))
        {
            _hoveredCardId = cardId;
            _tooltipPos = Event.current.mousePosition;
            _tooltipTitle = displayName;
            _tooltipText = description;
            _hasTooltip = true;
        }

        // Upgrade stars (combat cards only)
        if (category == "Combat")
        {
            var locInv = GameManager.localPlayer != null ? GameManager.localPlayer.GetComponent<PlayerInventory>() : null;
            int lvl = locInv != null ? locInv.GetLevel(cardId) : 1;
            GUIStyle starSt = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            starSt.normal.textColor = new Color(1f, 0.85f, 0.2f);
            GUI.Label(new Rect(x + 56f, y + 24f, 80f, 16f), StarStr(lvl), starSt);
        }

        // Remove button
        if (canRemove)
        {
            Rect removeRect = new Rect(x + w - 48f, y + (h - 26f) / 2f, 44f, 26f);
            GUI.color = new Color(1f, 0.25f, 0.25f, isHovered ? 0.8f : 0.5f);
            GUI.DrawTexture(removeRect, _whiteTex);
            GUI.color = new Color(1f, 0.5f, 0.5f, isHovered ? 1f : 0.7f);
            GUIStyle rmSt = new GUIStyle(GUI.skin.label) { fontSize = 10, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            GUI.Label(removeRect, "✕", rmSt);
            if (GUI.Button(removeRect, "", GUIStyle.none))
            {
                var localPc = GameManager.localPlayer?.GetComponent<PlayerCombat>();
                if (localPc != null) localPc.CmdRemoveCard(cardId);
            }
            GUI.color = Color.white;
        }
    }

    private void DrawShopPanels(float startX, float startY, float panelTotalW, float panelH, PlayerInventory inv, ShopPhaseManager spm)
    {
        // 4 combat cards (left) + 4 traits (right)
        float panelW = (panelTotalW - MARGIN) / 2f;
        float x1 = startX;
        float x2 = startX + panelW + MARGIN;

        if (spm.CombatShopSlots != null)
            DrawCombatShopPanel(x1, startY, panelW, panelH, "COMBAT CARDS", new Color(1f, 0.5f, 0.15f), spm.CombatShopSlots, inv, spm);
        if (spm.TraitShopSlots != null)
            DrawTraitShopPanel(x2, startY, panelW, panelH, "TRAITS", new Color(0.25f, 0.9f, 0.4f), spm.TraitShopSlots, inv, spm);
    }

    private void DrawCombatShopPanel(float x, float y, float w, float h, string title, Color accent, IReadOnlyList<CombatCardData> slots, PlayerInventory inv, ShopPhaseManager spm)
    {
        DrawPanelBorder(x, y, w, h, accent);
        DrawSectionHeader(x, y, w, title, accent);

        float pad = MARGIN;
        float cardW = w - pad * 2;
        float cardH = (h - 40f - pad * 4) / 4f;

        for (int i = 0; i < 4; i++)
        {
            float cardY = y + 40f + i * (cardH + pad);
            bool isPurchased = spm.IsCombatSlotPurchased(i);
            bool hasCard = i < slots.Count && slots[i] != null && !isPurchased;

            if (hasCard)
                DrawCombatCard(x + pad, cardY, cardW, cardH, slots[i], inv, i, spm);
            else
                DrawEmptySlot(x + pad, cardY, cardW, cardH);
        }
    }

    private void DrawTraitShopPanel(float x, float y, float w, float h, string title, Color accent, IReadOnlyList<TraitCardData> slots, PlayerInventory inv, ShopPhaseManager spm)
    {
        DrawPanelBorder(x, y, w, h, accent);
        DrawSectionHeader(x, y, w, title, accent);

        float pad = MARGIN;
        float cardW = w - pad * 2;
        float cardH = (h - 40f - pad * 3) / 4f;

        for (int i = 0; i < 4; i++)
        {
            float cardY = y + 40f + i * (cardH + pad);
            bool isPurchased = spm.IsTraitSlotPurchased(i);
            bool hasCard = i < slots.Count && slots[i] != null && !isPurchased;

            if (hasCard)
                DrawTraitCard(x + pad, cardY, cardW, cardH, slots[i], inv, i, spm);
            else
                DrawEmptySlot(x + pad, cardY, cardW, cardH);
        }
    }

    private void DrawEmptySlot(float x, float y, float w, float h)
    {
        // Dashed-line style background
        GUI.color = new Color(0.15f, 0.15f, 0.22f, 0.5f);
        GUI.DrawTexture(new Rect(x, y, w, h), _whiteTex);

        // Dotted border
        GUI.color = new Color(0.35f, 0.35f, 0.45f, 0.4f);
        float dash = 8f;
        for (float dx = 0; dx < w; dx += dash * 2f)
        {
            GUI.DrawTexture(new Rect(x + dx, y, Mathf.Min(dash, w - dx), 1.5f), _whiteTex);
            GUI.DrawTexture(new Rect(x + dx, y + h - 1.5f, Mathf.Min(dash, w - dx), 1.5f), _whiteTex);
        }

        GUIStyle soldSt = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        soldSt.normal.textColor = new Color(0.4f, 0.4f, 0.5f, 0.6f);
        GUI.color = Color.white;
        GUI.Label(new Rect(x, y, w, h), "◆  SOLD OUT  ◆", soldSt);
    }

    private void DrawCombatCard(float x, float y, float w, float h, CombatCardData card, PlayerInventory inv, int slotIndex, ShopPhaseManager spm)
    {
        Rect fullCardRect = new Rect(x, y, w, h);
        bool isHovered = fullCardRect.Contains(Event.current.mousePosition);

        Color famCol = GetFamilyColor(card.family);
        Color rarityCol = card.rarity == CardRarity.Basic ? new Color(0.6f, 0.6f, 0.6f) :
                         card.rarity == CardRarity.Advanced ? new Color(0.35f, 0.6f, 1f) :
                         new Color(1f, 0.75f, 0.15f);

        // Background with rarity tint
        GUI.color = isHovered ? new Color(0.1f, 0.12f, 0.22f) : new Color(0.06f, 0.07f, 0.14f);
        GUI.DrawTexture(fullCardRect, _whiteTex);

        // Rarity top stripe
        GUI.color = rarityCol;
        GUI.DrawTexture(new Rect(x, y, w, 3f), _whiteTex);

        // Family-colored left spine (thicker on hover)
        float spine = isHovered ? 8f : 5f;
        GUI.color = famCol;
        GUI.DrawTexture(new Rect(x, y, spine, h), _whiteTex);

        // Glow border on hover
        if (isHovered)
        {
            GUI.color = new Color(famCol.r, famCol.g, famCol.b, 0.6f);
            GUI.DrawTexture(new Rect(x, y, w, 2.5f), _whiteTex);
            GUI.DrawTexture(new Rect(x, y + h - 2.5f, w, 2.5f), _whiteTex);
            GUI.DrawTexture(new Rect(x + w - 2.5f, y, 2.5f, h), _whiteTex);
        }

        float pad = 12f;
        float textX = x + spine + pad;
        float textW = w - spine - pad * 2;

        // Family badge (top-right)
        string famName = GetFamilyName(card.family);
        GUIStyle badgeSt = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        float badgeW = 80f, badgeH = 22f;
        float badgeX = x + w - badgeW - pad;
        GUI.color = new Color(famCol.r * 0.2f, famCol.g * 0.2f, famCol.b * 0.2f, 0.9f);
        GUI.DrawTexture(new Rect(badgeX, y + pad, badgeW, badgeH), _whiteTex);
        GUI.color = famCol;
        GUI.DrawTexture(new Rect(badgeX, y + pad, badgeW, 1.5f), _whiteTex);
        GUI.DrawTexture(new Rect(badgeX, y + pad + badgeH - 1.5f, badgeW, 1.5f), _whiteTex);
        badgeSt.normal.textColor = famCol;
        GUI.color = Color.white;
        GUI.Label(new Rect(badgeX, y + pad, badgeW, badgeH), famName, badgeSt);

        // Rarity label (small, above family badge)
        GUIStyle raritySt = new GUIStyle(GUI.skin.label) { fontSize = 10, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight };
        raritySt.normal.textColor = rarityCol;
        GUI.Label(new Rect(badgeX - 60f, y + pad - 2f, 58f, 16f), card.rarity.ToString().ToUpper(), raritySt);

        // Card name with glow
        GUIStyle nameStyle = new GUIStyle(_cardNameStyle) { fontSize = 22 };
        Color nameColor = isHovered ? new Color(1f, 1f, 0.6f) : Color.white;
        CyberpunkGUIUtils.DrawGlowText(new Rect(textX, y + pad + 2f, textW - badgeW - 6f, 28f), card.displayName.ToUpper(), nameColor, nameStyle, famCol);

        // Description
        GUIStyle bodySt = new GUIStyle(_descStyle) { fontSize = 14, wordWrap = true };
        bodySt.normal.textColor = isHovered ? new Color(1f, 1f, 1f, 0.95f) : new Color(0.85f, 0.88f, 0.92f, 0.85f);
        GUI.Label(new Rect(textX, y + pad + 30f, textW, h - pad * 2 - 56f), card.description, bodySt);

        // Bottom row: cost + stars + button
        float bottomY = y + h - 38f;

        // Cost with coin icon
        GUIStyle costStyle = new GUIStyle(_costStyle) { fontSize = 18 };
        CyberpunkGUIUtils.DrawGlowText(new Rect(textX, bottomY, 100f, 24f), $"◈ {card.cost}", CyberpunkGUIUtils.NEON_YELLOW, costStyle, CyberpunkGUIUtils.NEON_YELLOW);

        // Buy / Upgrade / Maxed / Can't afford button
        bool owned = inv != null && inv.ownedCombatCards.Contains(card.cardId);
        int ownedLvl = owned ? inv.GetLevel(card.cardId) : 0;
        bool maxed = owned && inv.IsMaxLevel(card.cardId);
        bool canAfford = inv != null && inv.credits >= card.cost;
        float bw = 110f, bh = 32f;
        Rect btnRect = new Rect(x + w - bw - pad, bottomY - 2f, bw, bh);
        GUIStyle bigBtn = new GUIStyle(GUI.skin.button) { fontSize = 15, fontStyle = FontStyle.Bold };

        // Stars shown inline next to cost
        if (owned)
        {
            GUIStyle starSt = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            starSt.normal.textColor = new Color(1f, 0.85f, 0.2f);
            GUI.Label(new Rect(textX + 80f, bottomY, 80f, 24f), StarStr(ownedLvl), starSt);
        }

        if (maxed)
        {
            GUI.color = new Color(1f, 0.75f, 0.2f, 0.25f);
            GUI.DrawTexture(btnRect, _whiteTex);
            GUI.color = new Color(1f, 0.75f, 0.2f, 0.7f);
            GUI.Label(btnRect, "★ MAX ★", new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter });
        }
        else if (!canAfford)
        {
            GUI.color = new Color(0.5f, 0.15f, 0.15f, 0.6f);
            GUI.DrawTexture(btnRect, _whiteTex);
            GUI.color = new Color(1f, 0.4f, 0.4f, 0.6f);
            GUI.Label(btnRect, $"NEED {card.cost}", new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter });
        }
        else
        {
            Color btnCol = isHovered ? new Color(0.25f, 1f, 0.5f, 0.95f) : new Color(0.15f, 0.75f, 0.35f, 0.85f);
            GUI.color = btnCol;
            if (GUI.Button(btnRect, owned ? "UPGRADE ◈" : "BUY ◈", bigBtn))
            {
                var localPc = GameManager.localPlayer?.GetComponent<PlayerCombat>();
                if (localPc != null) localPc.CmdBuyCombatCard(slotIndex);
                spm.MarkCombatSlotPurchased(slotIndex);
            }
        }
        GUI.color = Color.white;

        if (isHovered)
        {
            _hoveredCardId = card.cardId;
            _tooltipPos    = Event.current.mousePosition;
            _tooltipTitle  = card.displayName;

            if (owned)
            {
                string lv1 = $"Lv1 — {card.baseDamage}dmg  |  {card.timingWindow:F2}s window";
                string lv2 = $"Lv2 — {card.lv2Damage}dmg  |  {(card.timingWindow + card.lv2TimingBonus):F2}s window";
                string lv3 = $"Lv3 — {card.lv3Damage}dmg  |  {(card.timingWindow + card.lv3TimingBonus):F2}s window";
                string perkLine = string.IsNullOrEmpty(card.perkDescription) ? "" : $"\nPERK: {card.perkDescription}";
                string arrow = ownedLvl == 1 ? $"  ← YOU ARE HERE\n{lv2}\n{lv3}" :
                               ownedLvl == 2 ? $"\n{lv2}  ← YOU ARE HERE\n{lv3}" :
                                               $"\n{lv2}\n{lv3}  ← MAXED";
                _tooltipText = $"{lv1}{arrow}{perkLine}";
            }
            else
            {
                string lv1 = $"Lv1 — {card.baseDamage}dmg  |  {card.timingWindow:F2}s window";
                string lv2 = $"Lv2 — {card.lv2Damage}dmg  |  {(card.timingWindow + card.lv2TimingBonus):F2}s window";
                string lv3 = $"Lv3 — {card.lv3Damage}dmg  |  {(card.timingWindow + card.lv3TimingBonus):F2}s window";
                string perkLine = string.IsNullOrEmpty(card.perkDescription) ? "" : $"\nPERK: {card.perkDescription}";
                _tooltipText = $"{card.description}\n\n{lv1}\n{lv2}\n{lv3}{perkLine}";
            }
            _hasTooltip = true;
        }
    }

    private void DrawTraitCard(float x, float y, float w, float h, TraitCardData card, PlayerInventory inv, int slotIndex, ShopPhaseManager spm)
    {
        Rect fullCardRect = new Rect(x, y, w, h);
        bool isHovered = fullCardRect.Contains(Event.current.mousePosition);
        bool alreadyEquipped = inv != null && inv.equippedTraitId == card.traitId;
        Color traitCol = new Color(0.3f, 0.95f, 0.5f);
        Color equipCol = new Color(1f, 0.8f, 0.15f);

        // Background
        GUI.color = isHovered ? new Color(0.05f, 0.14f, 0.08f) : new Color(0.03f, 0.08f, 0.05f);
        GUI.DrawTexture(fullCardRect, _whiteTex);

        // Top stripe — gold if equipped, green if not
        Color stripeCol = alreadyEquipped ? equipCol : traitCol;
        GUI.color = stripeCol;
        GUI.DrawTexture(new Rect(x, y, w, 3f), _whiteTex);

        // Left spine
        GUI.DrawTexture(new Rect(x, y, isHovered ? 6f : 4f, h), _whiteTex);

        // Glow border on hover
        if (isHovered)
        {
            GUI.color = new Color(traitCol.r, traitCol.g, traitCol.b, 0.5f);
            GUI.DrawTexture(new Rect(x, y, w, 2f), _whiteTex);
            GUI.DrawTexture(new Rect(x, y + h - 2f, w, 2f), _whiteTex);
            GUI.DrawTexture(new Rect(x + w - 2f, y, 2f, h), _whiteTex);
        }

        float pad = 12f;
        float textX = x + pad + 4f;
        float textW = w - pad * 2 - 4f;

        // PASSIVE badge (top-right)
        GUI.color = new Color(0.1f, 0.35f, 0.18f, 0.85f);
        GUI.DrawTexture(new Rect(x + w - 72f, y + pad, 58f, 18f), _whiteTex);
        GUIStyle badgeStyle = new GUIStyle(GUI.skin.label) { fontSize = 10, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        badgeStyle.normal.textColor = traitCol;
        GUI.color = Color.white;
        GUI.Label(new Rect(x + w - 72f, y + pad, 58f, 18f), "PASSIVE", badgeStyle);

        // Equipped badge (if active)
        if (alreadyEquipped)
        {
            GUI.color = new Color(equipCol.r * 0.2f, equipCol.g * 0.2f, equipCol.b * 0.1f, 0.9f);
            GUI.DrawTexture(new Rect(x + w - 72f, y + pad + 22f, 58f, 18f), _whiteTex);
            GUIStyle eqSt = new GUIStyle(GUI.skin.label) { fontSize = 10, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            eqSt.normal.textColor = equipCol;
            GUI.color = Color.white;
            GUI.Label(new Rect(x + w - 72f, y + pad + 22f, 58f, 18f), "ACTIVE", eqSt);
        }

        // Card name
        GUIStyle nameStyle = new GUIStyle(_cardNameStyle) { fontSize = 20 };
        Color nameColor = isHovered ? new Color(0.5f, 1f, 0.65f) : Color.white;
        CyberpunkGUIUtils.DrawGlowText(new Rect(textX, y + pad + 2f, textW - 80f, 26f), card.displayName.ToUpper(), nameColor, nameStyle, traitCol);

        // Effect text
        GUIStyle effSt = new GUIStyle(_descStyle) { fontSize = 14, wordWrap = true };
        effSt.normal.textColor = isHovered ? new Color(1f, 1f, 1f, 0.95f) : new Color(0.85f, 0.95f, 0.88f, 0.88f);
        GUI.Label(new Rect(textX, y + pad + 28f, textW, h - pad * 2 - 54f), card.effect, effSt);

        // Bottom row
        float bottomY = y + h - 36f;

        // Cost
        GUIStyle costStyle = new GUIStyle(_costStyle) { fontSize = 18 };
        CyberpunkGUIUtils.DrawGlowText(new Rect(textX, bottomY, 120f, 24f), $"◈ {card.cost} TK", new Color(0.5f, 1f, 0.45f), costStyle, new Color(0.5f, 1f, 0.45f));

        // Button
        bool canAfford = inv != null && inv.traitTokens >= card.cost;
        float bw = 110f, bh = 30f;
        Rect btnRect = new Rect(x + w - bw - pad, bottomY - 2f, bw, bh);
        GUIStyle bigBtn = new GUIStyle(GUI.skin.button) { fontSize = 15, fontStyle = FontStyle.Bold };

        if (alreadyEquipped)
        {
            GUI.color = new Color(equipCol.r, equipCol.g, equipCol.b, 0.2f);
            GUI.DrawTexture(btnRect, _whiteTex);
            GUI.color = new Color(equipCol.r, equipCol.g, equipCol.b, 0.8f);
            GUI.Label(btnRect, "★ EQUIPPED", new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter });
        }
        else if (!canAfford)
        {
            GUI.color = new Color(0.5f, 0.15f, 0.15f, 0.5f);
            GUI.DrawTexture(btnRect, _whiteTex);
            GUI.color = new Color(1f, 0.4f, 0.4f, 0.5f);
            GUI.Label(btnRect, $"NEED {card.cost}", new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter });
        }
        else
        {
            Color btnCol = isHovered ? new Color(0.3f, 1f, 0.5f, 0.95f) : new Color(0.18f, 0.7f, 0.32f, 0.85f);
            GUI.color = btnCol;
            if (GUI.Button(btnRect, "BUY ◈", bigBtn))
            {
                var localPc = GameManager.localPlayer?.GetComponent<PlayerCombat>();
                if (localPc != null) localPc.CmdBuyTraitCard(slotIndex);
                spm.MarkTraitSlotPurchased(slotIndex);
            }
        }
        GUI.color = Color.white;

        if (isHovered)
        {
            _hoveredCardId = card.traitId;
            _tooltipPos = Event.current.mousePosition;
            _tooltipTitle = card.displayName;
            _tooltipText = $"{card.effect}\n\n{card.description}";
            _hasTooltip = true;
        }
    }

    private void DrawBotInventoryPanel(float x, float y, float w, float h, PlayerInventory botInv)
    {
        DrawPanelBorder(x, y, w, h, new Color(1f, 0.2f, 0.8f));
        DrawSectionHeader(x, y, w, "OPPONENT DECK", new Color(1f, 0.2f, 0.8f));

        float cardY = y + 40f;
        float cardW = w - MARGIN * 2;
        float cardH = 50f;
        float pad = 8f;

        if (botInv != null && (botInv.ownedCombatCards.Count > 0 || !string.IsNullOrEmpty(botInv.equippedTraitId)))
        {
            if (botInv.ownedCombatCards.Count > 0)
            {
                GUI.color = new Color(0.3f, 0.3f, 0.4f);
                GUI.Label(new Rect(x + MARGIN, cardY, cardW, 22), $"COMBAT ({botInv.ownedCombatCards.Count}/8)", _descStyle);
                cardY += 24f;

                foreach (var cardId in botInv.ownedCombatCards)
                {
                    if (cardY + cardH > y + h - 10) break;
                    DrawOpponentCard(x + MARGIN, cardY, cardW, cardH, cardId, "Combat");
                    cardY += cardH + pad;
                }
            }

            if (!string.IsNullOrEmpty(botInv.equippedTraitId))
            {
                GUI.color = new Color(0.3f, 0.3f, 0.4f);
                GUI.Label(new Rect(x + MARGIN, cardY, cardW, 22), "TRAIT (PASSIVE)", _descStyle);
                cardY += 24f;
                DrawOpponentCard(x + MARGIN, cardY, cardW, cardH, botInv.equippedTraitId, "Trait");
            }
        }
        else
        {
            GUI.color = new Color(0.5f, 0.5f, 0.5f);
            GUI.Label(new Rect(x + MARGIN, cardY, cardW, 30), "Opponent deck building", _descStyle);
        }
    }

    private void DrawOpponentCard(float x, float y, float w, float h, string cardId, string category)
    {
        string displayName = cardId;
        string description = "";
        Color rarityColor = new Color(0.6f, 0.6f, 0.6f);

        if (category == "Combat")
        {
            var card = CardDatabase.BasicCards.Concat(CardDatabase.AdvancedCards).Concat(CardDatabase.LegendaryCards)
                .FirstOrDefault(c => c.cardId == cardId);
            if (card != null)
            {
                displayName = card.displayName;
                description = card.description;
                rarityColor = card.rarity == CardRarity.Basic ? new Color(0.6f, 0.6f, 0.6f) :
                             card.rarity == CardRarity.Advanced ? new Color(0.2f, 0.5f, 1f) :
                             new Color(1f, 0.75f, 0.1f);
            }
        }
        else if (category == "Trait")
        {
            var card = CardDatabase.TraitCards.FirstOrDefault(c => c.traitId == cardId);
            if (card != null)
            {
                displayName = card.displayName;
                description = card.description;
                rarityColor = new Color(0.25f, 0.9f, 0.4f);
            }
        }

        Rect cardRect = new Rect(x, y, w, h);
        GUI.color = new Color(rarityColor.r * 0.25f, rarityColor.g * 0.25f, rarityColor.b * 0.25f);
        GUI.DrawTexture(cardRect, _whiteTex);

        GUI.color = rarityColor;
        GUI.DrawTexture(new Rect(x, y, 3f, h), _whiteTex);

        GUI.color = Color.white;
        GUI.Label(new Rect(x + 8f, y + 5f, w - 16f, 20f), displayName.ToUpper(), _cardNameStyle);

        if (cardRect.Contains(Event.current.mousePosition))
        {
            _hoveredCardId = cardId;
            _tooltipPos = Event.current.mousePosition;
            _tooltipTitle = displayName;
            _tooltipText = description;
            _hasTooltip = true;
        }
    }

    private void DrawTooltip()
    {
        if (string.IsNullOrEmpty(_tooltipTitle)) return;

        string counters = GetCountersText(_hoveredCardId);
        bool hasCounters = !string.IsNullOrEmpty(counters);
        int pipeIdx = counters.IndexOf("  |  ");
        bool hasTwoParts = pipeIdx >= 0;

        var botInv = GetBotInventory();
        string[] oppHits = GetOpponentCounterHint(_hoveredCardId, botInv);
        bool hasOppHint = oppHits.Length > 0;

        Color typeColor = GetCardTypeColor(_hoveredCardId);
        string typeBadge = GetCardTypeBadge(_hoveredCardId);

        Texture2D cardArt = GetCardTexture(_hoveredCardId);
        float artW = cardArt != null ? 100f : 0f;
        float tooltipW  = 460f + artW;
        float headerH   = 38f;
        // Scale desc height to content — upgrade preview has 4-6 lines
        int newlineCount = _tooltipText.Split('\n').Length;
        float descH     = Mathf.Max(100f, newlineCount * 18f + 10f);
        float matchupH  = hasCounters ? (hasTwoParts ? 48f : 28f) : 0f;
        float oppH      = hasOppHint  ? 42f : 0f;
        float sepCount  = (hasCounters ? 1 : 0) + (hasOppHint ? 1 : 0);
        float tooltipH  = headerH + descH + sepCount * 6f + matchupH + oppH + 12f;

        float tooltipX = _tooltipPos.x + 22f;
        float tooltipY = _tooltipPos.y + 22f;
        if (tooltipX + tooltipW > _vw)  tooltipX = _vw  - tooltipW - 10f;
        if (tooltipY + tooltipH > _vh) tooltipY = _vh - tooltipH - 10f;

        // ── Background ────────────────────────────────────────────────────────
        GUI.color = new Color(0.03f, 0.04f, 0.11f, 0.97f);
        GUI.DrawTexture(new Rect(tooltipX, tooltipY, tooltipW, tooltipH), _whiteTex);

        // Card art panel (left side if available)
        float artX = tooltipX;
        float textX = tooltipX;
        if (cardArt != null && artW > 0)
        {
            GUI.color = new Color(0.08f, 0.08f, 0.16f);
            GUI.DrawTexture(new Rect(artX, tooltipY, artW, tooltipH), _whiteTex);

            // Draw card art with proper aspect ratio
            float artAspect = (float)cardArt.width / cardArt.height;
            float artDisplayW = artW - 8f;
            float artDisplayH = tooltipH - 8f;
            float artScaledW = artDisplayW;
            float artScaledH = artScaledW / artAspect;
            if (artScaledH > artDisplayH)
            {
                artScaledH = artDisplayH;
                artScaledW = artScaledH * artAspect;
            }
            float artCenterX = artX + (artW - artScaledW) / 2f;
            float artCenterY = tooltipY + (tooltipH - artScaledH) / 2f;

            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(artCenterX, artCenterY, artScaledW, artScaledH), cardArt);

            textX = artX + artW + 4f;
            GUI.color = typeColor;
            GUI.DrawTexture(new Rect(textX - 2f, tooltipY, 2f, tooltipH), _whiteTex);
        }

        // Left rarity bar
        GUI.color = typeColor;
        GUI.DrawTexture(new Rect(tooltipX, tooltipY, 4f, tooltipH), _whiteTex);

        // Top + bottom border glow
        GUI.color = new Color(typeColor.r * 0.8f, typeColor.g * 0.8f, typeColor.b * 0.8f, 0.9f);
        GUI.DrawTexture(new Rect(tooltipX, tooltipY, tooltipW, 2f), _whiteTex);
        GUI.DrawTexture(new Rect(tooltipX, tooltipY + tooltipH - 2f, tooltipW, 2f), _whiteTex);

        // Header tint
        GUI.color = new Color(typeColor.r * 0.12f, typeColor.g * 0.12f, typeColor.b * 0.12f, 1f);
        float headerX = cardArt != null ? textX : tooltipX + 4f;
        float headerW = cardArt != null ? tooltipW - artW - 8f : tooltipW - 4f;
        GUI.DrawTexture(new Rect(headerX, tooltipY + 2f, headerW, headerH - 2f), _whiteTex);

        float cx = headerX + 10f;
        float cw = headerW - 20f;
        float cy = tooltipY + 8f;

        // ── Header: name + type badge ─────────────────────────────────────────
        GUI.color = Color.white;
        GUIStyle titleSt = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold };
        titleSt.normal.textColor = typeColor;
        GUI.Label(new Rect(cx, cy, cw - 56f, 26f), _tooltipTitle.ToUpper(), titleSt);

        // Type badge box
        float badgeX = tooltipX + tooltipW - 52f;
        GUI.color = new Color(typeColor.r * 0.25f, typeColor.g * 0.25f, typeColor.b * 0.25f, 0.9f);
        GUI.DrawTexture(new Rect(badgeX, cy - 1f, 44f, 22f), _whiteTex);
        GUI.color = typeColor;
        GUI.DrawTexture(new Rect(badgeX, cy - 1f, 44f, 1.5f), _whiteTex);
        GUI.DrawTexture(new Rect(badgeX, cy + 20.5f, 44f, 1.5f), _whiteTex);
        GUIStyle badgeSt = new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        badgeSt.normal.textColor = typeColor;
        GUI.color = Color.white;
        GUI.Label(new Rect(badgeX, cy - 1f, 44f, 22f), typeBadge, badgeSt);

        // ── Description ───────────────────────────────────────────────────────
        cy = tooltipY + headerH + 6f;
        GUIStyle descSt = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true };
        descSt.normal.textColor = new Color(0.92f, 0.92f, 0.92f);
        GUI.Label(new Rect(cx, cy, cw, descH), _tooltipText, descSt);
        cy += descH + 2f;

        // ── Matchup section ───────────────────────────────────────────────────
        if (hasCounters)
        {
            GUI.color = new Color(0.3f, 0.3f, 0.45f, 0.55f);
            GUI.DrawTexture(new Rect(tooltipX + 4f, cy, tooltipW - 4f, 1f), _whiteTex);
            cy += 5f; GUI.color = Color.white;

            if (hasTwoParts)
            {
                string countersLine = counters.Substring(0, pipeIdx);
                string beatenLine   = counters.Substring(pipeIdx + 5);

                GUIStyle cSt = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, wordWrap = true };
                cSt.normal.textColor = new Color(0.25f, 1f, 0.45f);
                GUI.Label(new Rect(cx, cy, cw, 22f), countersLine, cSt);

                GUIStyle bSt = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, wordWrap = true };
                bSt.normal.textColor = new Color(1f, 0.42f, 0.18f);
                GUI.Label(new Rect(cx, cy + 22f, cw, 22f), beatenLine, bSt);
            }
            else
            {
                GUIStyle infoSt = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
                infoSt.normal.textColor = new Color(1f, 0.85f, 0.2f);
                GUI.Label(new Rect(cx, cy, cw, matchupH), counters, infoSt);
            }
            cy += matchupH;
        }

        // ── Opponent counter banner ───────────────────────────────────────────
        if (hasOppHint)
        {
            GUI.color = new Color(0.3f, 0.3f, 0.45f, 0.55f);
            GUI.DrawTexture(new Rect(tooltipX + 4f, cy, tooltipW - 4f, 1f), _whiteTex);
            cy += 5f;

            GUI.color = new Color(0f, 0.85f, 0.35f, 0.13f);
            GUI.DrawTexture(new Rect(tooltipX + 4f, cy, tooltipW - 4f, 36f), _whiteTex);
            GUI.color = new Color(0f, 1f, 0.4f);
            GUI.DrawTexture(new Rect(tooltipX + 4f, cy, 3f, 36f), _whiteTex);

            GUI.color = Color.white;
            GUIStyle labelSt = new GUIStyle(GUI.skin.label) { fontSize = 10, fontStyle = FontStyle.Bold };
            labelSt.normal.textColor = new Color(0.5f, 1f, 0.65f);
            GUI.Label(new Rect(cx + 4f, cy + 2f, cw, 15f), "⚡ PUNISHES OPPONENT'S DECK:", labelSt);

            GUIStyle cardsSt = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
            cardsSt.normal.textColor = new Color(0.15f, 1f, 0.5f);
            GUI.Label(new Rect(cx + 4f, cy + 17f, cw, 20f), string.Join(",  ", oppHits), cardsSt);
        }

        GUI.color = Color.white;
    }

    private Texture2D GetCardTexture(string cardId)
    {
        if (string.IsNullOrEmpty(cardId)) return null;

        var artLib = FindObjectOfType<CardArtLibrary>();
        if (artLib == null) return null;

        // Try to get the texture using the card ID
        return artLib.GetTextureForTrigger(cardId);
    }

    // Family class colors — the 4-class triangle (+ Support).
    private Color GetFamilyColor(CardFamily f) => f switch
    {
        CardFamily.Strike => new Color(1f, 0.40f, 0.20f),   // hot orange-red
        CardFamily.Throw  => new Color(0.85f, 0.35f, 1f),   // magenta-purple
        CardFamily.Block  => new Color(0.30f, 0.60f, 1f),   // blue
        CardFamily.Parry  => new Color(0.15f, 0.95f, 0.95f),// cyan
        _                 => new Color(0.70f, 0.70f, 0.78f) // support — grey
    };

    private string GetFamilyName(CardFamily f) => f switch
    {
        CardFamily.Strike => "STRIKE",
        CardFamily.Throw  => "THROW",
        CardFamily.Block  => "BLOCK",
        CardFamily.Parry  => "PARRY",
        _                 => "SUPPORT"
    };

    // Filled + empty stars up to the max upgrade level (e.g. Lv2 -> "★★☆").
    private string StarStr(int level)
    {
        level = Mathf.Clamp(level, 0, PlayerInventory.MaxLevel);
        return new string('★', level) + new string('☆', PlayerInventory.MaxLevel - level);
    }

    private Color GetCardTypeColor(string cardId)
    {
        var combat = CardDatabase.BasicCards.Concat(CardDatabase.AdvancedCards).Concat(CardDatabase.LegendaryCards)
            .FirstOrDefault(c => c.cardId == cardId);
        if (combat != null)
        {
            if (combat.rarity == CardRarity.Legendary) return new Color(1f, 0.75f, 0.1f);
            if (combat.rarity == CardRarity.Advanced)  return new Color(0.3f, 0.6f, 1f);
            return combat.type == CardType.Attack ? new Color(1f, 0.4f, 0.15f) : new Color(0f, 0.9f, 0.9f);
        }
        return new Color(0.25f, 0.9f, 0.4f);
    }

    private string GetCardTypeBadge(string cardId)
    {
        var combat = CardDatabase.BasicCards.Concat(CardDatabase.AdvancedCards).Concat(CardDatabase.LegendaryCards)
            .FirstOrDefault(c => c.cardId == cardId);
        if (combat != null) return combat.type == CardType.Attack ? "ATK" : "DEF";
        return "TRAIT";
    }

    private string[] GetOpponentCounterHint(string hoveredCardId, PlayerInventory opponentInv)
    {
        if (opponentInv == null || opponentInv.ownedCombatCards.Count == 0) return new string[0];
        string[] countered = GetCounteredCardIds(hoveredCardId);
        if (countered.Length == 0) return new string[0];

        var hits = new List<string>();
        foreach (var opCardId in opponentInv.ownedCombatCards)
        {
            if (System.Array.IndexOf(countered, opCardId) >= 0)
            {
                var card = CardDatabase.BasicCards.Concat(CardDatabase.AdvancedCards).Concat(CardDatabase.LegendaryCards)
                    .FirstOrDefault(c => c.cardId == opCardId);
                if (card != null && !hits.Contains(card.displayName))
                    hits.Add(card.displayName);
            }
        }
        return hits.ToArray();
    }

    private string[] GetCounteredCardIds(string cardId) => cardId switch
    {
        "jab"         => new[]{"dodge_left", "dodge_right"},
        "cross"       => new[]{"jab", "dodge_left", "dodge_right", "sweep"},
        "hook"        => new[]{"cross", "jab"},
        "boom"        => new[]{"dodge_left", "dodge_right", "jab", "cross", "hook", "focus"},
        "grapple"     => new[]{"block", "dodge_left", "dodge_right"},
        "fake"        => new[]{"block", "reflect", "dodge_left", "dodge_right", "clutch", "focus", "taunt", "trap", "cage", "mirror", "reverse"},
        "reflect"     => new[]{"jab", "cross", "hook", "boom", "grapple", "uppercut", "sweep", "overclock"},
        "block"       => new[]{"jab", "cross", "overclock"},
        "dodge_left"  => new[]{"jab", "cross"},
        "dodge_right" => new[]{"jab", "cross"},
        "clutch"      => new[]{"boom", "hook", "overclock"},
        "uppercut"    => new[]{"dodge_left", "dodge_right", "grapple"},
        "sweep"       => new[]{"block"},
        "reverse"     => new[]{"jab", "cross", "hook", "boom", "overclock"},
        "trap"        => new[]{"dodge_left", "dodge_right", "block"},
        "cage"        => new[]{"block", "dodge_left", "dodge_right"},
        "mirror"      => new[]{"jab", "cross", "hook", "grapple", "uppercut", "sweep"},
        _             => new string[0]
    };

    private string GetCountersText(string cardId) => cardId switch
    {
        "jab"         => "COUNTERS: Dodge  |  BEATEN BY: Cross, Hook, Block",
        "cross"       => "COUNTERS: Jab, Dodge  |  BEATEN BY: Hook, Block, Clutch",
        "hook"        => "COUNTERS: Cross, Jab  |  BEATEN BY: Clutch, Block",
        "boom"        => "COUNTERS: Dodge (wide arc)  |  BEATEN BY: Clutch, Block",
        "grapple"     => "COUNTERS: Block, Dodge  |  BEATEN BY: Fake, Uppercut",
        "fake"        => "COUNTERS: Grapple, all defense  |  BEATEN BY: any attack",
        "reflect"     => "COUNTERS: any attack  |  BEATEN BY: Grapple, Boom",
        "block"       => "COUNTERS: Jab, Cross  |  BEATEN BY: Grapple, Sweep",
        "dodge_left"  => "COUNTERS: Jab, Cross  |  BEATEN BY: Hook, Sweep, Grapple",
        "dodge_right" => "COUNTERS: Jab, Cross  |  BEATEN BY: Hook, Sweep, Grapple",
        "clutch"      => "COUNTERS: Boom, Hook  |  BEATEN BY: Jab, Cross, Grapple",
        "uppercut"    => "COUNTERS: Dodge, Grapple  |  BEATEN BY: Block, Cross",
        "sweep"       => "COUNTERS: Block  |  BEATEN BY: Dodge, Cross",
        "focus"       => "Passive setup — no matchup. Buff consumed on next attack.",
        "taunt"       => "Forces opponent into attack-only next beat. Risk: you eat whatever they throw.",
        "overclock"   => "COUNTERS: anything — amplifies next hit  |  Risk: +10% self-damage",
        "reverse"     => "COUNTERS: heavy attacks  |  BEATEN BY: Fake, bad timing",
        "trap"        => "COUNTERS: Dodge, Block  |  BEATEN BY: attacks (bypasses trap)",
        "cage"        => "COUNTERS: Block, Dodge  |  BEATEN BY: fast counter before it fires",
        "mirror"      => "COUNTERS: attack-heavy players  |  BEATEN BY: Fake",
        _             => ""
    };

    private void DrawPanelBorder(float x, float y, float w, float h, Color accent)
    {
        // Dark glass background
        GUI.color = new Color(0.02f, 0.03f, 0.06f, 0.92f);
        GUI.DrawTexture(new Rect(x, y, w, h), _whiteTex);

        // Subtle inner glow at top
        GUI.color = new Color(accent.r * 0.15f, accent.g * 0.15f, accent.b * 0.15f, 0.4f);
        GUI.DrawTexture(new Rect(x + 2, y + 2, w - 4, 60f), _whiteTex);

        // Bright accent border
        GUI.color = accent;
        GUI.DrawTexture(new Rect(x, y, w, 2f), _whiteTex);
        GUI.DrawTexture(new Rect(x, y + h - 2f, w, 2f), _whiteTex);
        GUI.DrawTexture(new Rect(x, y, 2f, h), _whiteTex);
        GUI.DrawTexture(new Rect(x + w - 2f, y, 2f, h), _whiteTex);

        // Corner accents
        float corner = 12f;
        GUI.color = new Color(accent.r, accent.g, accent.b, 0.7f);
        GUI.DrawTexture(new Rect(x, y, corner, 2f), _whiteTex);
        GUI.DrawTexture(new Rect(x, y, 2f, corner), _whiteTex);
        GUI.DrawTexture(new Rect(x + w - corner, y, corner, 2f), _whiteTex);
        GUI.DrawTexture(new Rect(x + w - 2f, y, 2f, corner), _whiteTex);
        GUI.DrawTexture(new Rect(x, y + h - 2f, corner, 2f), _whiteTex);
        GUI.DrawTexture(new Rect(x, y + h - corner, 2f, corner), _whiteTex);
        GUI.DrawTexture(new Rect(x + w - corner, y + h - 2f, corner, 2f), _whiteTex);
        GUI.DrawTexture(new Rect(x + w - 2f, y + h - corner, 2f, corner), _whiteTex);

        GUI.color = Color.white;
    }

    private void DrawSectionHeader(float x, float y, float w, string text, Color accent)
    {
        // Gradient header strip
        GUI.color = new Color(accent.r * 0.25f, accent.g * 0.25f, accent.b * 0.25f, 0.7f);
        GUI.DrawTexture(new Rect(x, y, w, 36f), _whiteTex);

        // Accent underline
        GUI.color = accent;
        GUI.DrawTexture(new Rect(x, y + 34f, w, 2f), _whiteTex);

        // Decorative left bar
        GUI.DrawTexture(new Rect(x, y, 4f, 36f), _whiteTex);

        GUI.color = Color.white;
        CyberpunkGUIUtils.DrawGlowText(new Rect(x + 12f, y + 2f, w - 24f, 32f), text, accent, _sectionStyle, accent);
    }

    private void DrawLockInButton()
    {
        float btnW = 320f;
        float btnH = 60f;
        float btnX = _vw / 2f - btnW / 2f;
        float btnY = _vh - 78f;

        // Pulsing glow background
        float pulse = (Mathf.Sin(Time.time * 4f) + 1f) * 0.5f;
        GUI.color = new Color(0f, 1f, 0.6f, 0.1f + pulse * 0.1f);
        GUI.DrawTexture(new Rect(btnX - 8, btnY - 8, btnW + 16, btnH + 16), _whiteTex);
        GUI.color = new Color(0f, 1f, 0.6f, 0.25f + pulse * 0.15f);
        GUI.DrawTexture(new Rect(btnX - 4, btnY - 4, btnW + 8, btnH + 8), _whiteTex);

        // Button background
        GUI.color = new Color(0f, 0.85f, 0.5f, 0.85f);
        GUI.DrawTexture(new Rect(btnX, btnY, btnW, btnH), _whiteTex);

        // Border
        GUI.color = new Color(0f, 1f, 0.7f, 0.9f);
        GUI.DrawTexture(new Rect(btnX, btnY, btnW, 2.5f), _whiteTex);
        GUI.DrawTexture(new Rect(btnX, btnY + btnH - 2.5f, btnW, 2.5f), _whiteTex);
        GUI.DrawTexture(new Rect(btnX, btnY, 2.5f, btnH), _whiteTex);
        GUI.DrawTexture(new Rect(btnX + btnW - 2.5f, btnY, 2.5f, btnH), _whiteTex);

        // Text
        GUIStyle btnStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 22,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        GUI.color = Color.white;
        CyberpunkGUIUtils.DrawGlowText(new Rect(btnX, btnY + 2f, btnW, btnH), "⚡  LOCK IN & FIGHT  ⚡", Color.white, btnStyle, new Color(0f, 1f, 0.7f));

        // Invisible click area
        if (GUI.Button(new Rect(btnX, btnY, btnW, btnH), "", GUIStyle.none))
        {
            var localPc = GameManager.localPlayer?.GetComponent<PlayerCombat>();
            if (localPc != null) localPc.CmdLockInPostRoundShop();
        }
        GUI.color = Color.white;
    }

    private PlayerInventory GetBotInventory()
    {
        foreach (var p in GameManager.players)
        {
            if (p != null && p != GameManager.localPlayer)
            {
                var inv = p.GetComponent<PlayerInventory>();
                if (inv != null) return inv;
            }
        }
        return null;
    }

    private void EnsureTextures()
    {
        if (_whiteTex == null)
        {
            _whiteTex = new Texture2D(1, 1);
            _whiteTex.SetPixel(0, 0, Color.white);
            _whiteTex.Apply();
        }
    }

    private void EnsureStyles()
    {
        if (_titleStyle != null) return;

        _titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 36,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };

        _cardNameStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 16,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.UpperLeft
        };

        _descStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            alignment = TextAnchor.UpperLeft,
            wordWrap = true
        };

        _costStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 15,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft
        };

        _timerStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 20,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };

        _sectionStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 18,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };

        _buttonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 14,
            fontStyle = FontStyle.Bold
        };
    }
}
