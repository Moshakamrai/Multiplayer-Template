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
    private const float LEFT_SIDEBAR_W = 0.20f;
    private const float SHOP_W = 0.55f;
    private const float RIGHT_SIDEBAR_W = 0.20f;
    private const float MARGIN = 12f;
    private const float TOP_H = 100f;

    // Hover tooltip state
    private string _hoveredCardId = "";
    private Vector2 _tooltipPos = Vector2.zero;
    private string _tooltipText = "";
    private string _tooltipTitle = "";
    private bool _hasTooltip = false;

    private void Awake() { if (Instance == null) Instance = this; }

    private void OnGUI()
    {
        if (ShopPhaseManager.Instance == null || !ShopPhaseManager.Instance.isShopPhase) return;

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
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _whiteTex);
        GUI.color = Color.white;

        // Calculate layout
        float leftW = Screen.width * LEFT_SIDEBAR_W;
        float shopW = Screen.width * SHOP_W;
        float rightW = Screen.width * RIGHT_SIDEBAR_W;

        float leftX = MARGIN;
        float shopX = leftX + leftW + MARGIN;
        float rightX = shopX + shopW + MARGIN;

        float contentY = TOP_H + MARGIN;
        float contentH = Screen.height - contentY - 90f;

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
    }

    private void DrawHeader(ShopPhaseManager spm, PlayerInventory inv)
    {
        // Title with neon glow effect
        GUI.color = new Color(0f, 1f, 0.8f, 0.15f);
        GUI.DrawTexture(new Rect(0, 5, Screen.width, 55), _whiteTex);
        GUI.color = Color.white;

        CyberpunkGUIUtils.DrawGlowText(new Rect(0, 8, Screen.width, 45), "⚡ POST-ROUND SHOP ⚡",
            CyberpunkGUIUtils.NEON_CYAN, _titleStyle, CyberpunkGUIUtils.NEON_CYAN);

        // Top bar with info
        float barY = 60f;
        float thirdW = Screen.width / 3f;

        // Timer
        Color timerColor = spm.shopTimeRemaining <= 15f ? new Color(1f, 0.2f, 0.2f) : CyberpunkGUIUtils.NEON_CYAN;
        CyberpunkGUIUtils.DrawGlowText(new Rect(MARGIN, barY, thirdW - MARGIN * 2, 30), $"⏱  {Mathf.Max(0f, spm.shopTimeRemaining):F0}s",
            timerColor, _timerStyle, timerColor);

        // Credits
        string creditsStr = inv != null ? $"💰 {inv.credits}" : "💰 --";
        GUIStyle creditStyle = new GUIStyle(_timerStyle);
        CyberpunkGUIUtils.DrawGlowText(new Rect(thirdW, barY, thirdW, 30), creditsStr,
            CyberpunkGUIUtils.NEON_YELLOW, creditStyle, CyberpunkGUIUtils.NEON_YELLOW);

        // Round
        GUIStyle roundStyle = new GUIStyle(_timerStyle);
        CyberpunkGUIUtils.DrawGlowText(new Rect(thirdW * 2f, barY, thirdW - MARGIN * 2, 30), $"ROUND {spm.currentShopRound}",
            CyberpunkGUIUtils.NEON_MAGENTA, roundStyle, CyberpunkGUIUtils.NEON_MAGENTA);
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
                bool atMax = inv.ownedCombatCards.Count >= PlayerInventory.MaxCombatCards;
                Color headerColor = atMax ? new Color(1f, 0.4f, 0.2f) : CyberpunkGUIUtils.NEON_ORANGE;
                GUIStyle combatHeaderStyle = new GUIStyle(_descStyle);
                string capacityLabel = atMax ? $"COMBAT ({inv.ownedCombatCards.Count}/8) — FULL" : $"COMBAT ({inv.ownedCombatCards.Count}/8)";
                CyberpunkGUIUtils.DrawGlowText(new Rect(x + MARGIN, cardY, cardW, 22), capacityLabel, headerColor, combatHeaderStyle, headerColor);
                cardY += 24f;

                foreach (var cardId in inv.ownedCombatCards.Keys)
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
                cost = card.cost;
                rarityColor = new Color(0.25f, 0.9f, 0.4f);
            }
        }

        Rect cardRect = new Rect(x, y, w - (canRemove ? 50f : 0), h);

        // Card background with rarity
        GUI.color = new Color(rarityColor.r * 0.25f, rarityColor.g * 0.25f, rarityColor.b * 0.25f);
        GUI.DrawTexture(cardRect, _whiteTex);

        // Rarity left border
        GUI.color = rarityColor;
        GUI.DrawTexture(new Rect(x, y, 3f, h), _whiteTex);

        // Text
        GUI.color = Color.white;
        GUI.Label(new Rect(x + 8f, y + 5f, cardRect.width - 16f, 20f), displayName.ToUpper(), _cardNameStyle);
        _costStyle.normal.textColor = new Color(1f, 1f, 0f, 0.8f);
        GUI.Label(new Rect(x + 8f, y + 26f, 50f, 16f), $"{cost} CR", _costStyle);

        // Hover
        if (cardRect.Contains(Event.current.mousePosition))
        {
            _hoveredCardId = cardId;
            _tooltipPos = Event.current.mousePosition;
            _tooltipTitle = displayName;
            _tooltipText = description;
            _hasTooltip = true;
        }

        // Remove button
        if (canRemove)
        {
            Rect removeRect = new Rect(x + w - 45f, y + (h - 24f) / 2f, 40f, 24f);
            GUI.color = new Color(1f, 0.3f, 0.3f, 0.7f);
            if (GUI.Button(removeRect, "REMOVE", new GUIStyle(GUI.skin.button) { fontSize = 10, fontStyle = FontStyle.Bold }))
            {
                var localPc = GameManager.localPlayer?.GetComponent<PlayerCombat>();
                if (localPc != null) localPc.CmdRemoveCard(cardId);
            }
            GUI.color = Color.white;
        }
    }

    private void DrawShopPanels(float startX, float startY, float panelTotalW, float panelH, PlayerInventory inv, ShopPhaseManager spm)
    {
        // 8 combat cards (left) + 4 traits (right)
        float panelW = (panelTotalW - MARGIN) / 2f;
        float x1 = startX;
        float x2 = startX + panelW + MARGIN;

        if (spm.CombatShopSlots != null)
            DrawCombatShopPanel(x1, startY, panelW, panelH, "COMBAT CARDS (8)", new Color(1f, 0.5f, 0.15f), spm.CombatShopSlots, inv, spm);
        if (spm.TraitShopSlots != null)
            DrawTraitShopPanel(x2, startY, panelW, panelH, "TRAITS (4)", new Color(0.25f, 0.9f, 0.4f), spm.TraitShopSlots, inv, spm);
    }

    private void DrawCombatShopPanel(float x, float y, float w, float h, string title, Color accent, IReadOnlyList<CombatCardData> slots, PlayerInventory inv, ShopPhaseManager spm)
    {
        DrawPanelBorder(x, y, w, h, accent);
        DrawSectionHeader(x, y, w, title, accent);

        float pad = MARGIN;
        float cardW = w - pad * 2;
        float cardH = (h - 40f - pad * 7) / 8f;

        for (int i = 0; i < 8; i++)
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
        GUI.color = new Color(0.2f, 0.2f, 0.3f);
        GUI.DrawTexture(new Rect(x, y, w, h), _whiteTex);
        GUI.color = new Color(0.5f, 0.5f, 0.5f);
        _descStyle.alignment = TextAnchor.MiddleCenter;
        _descStyle.fontSize = 14;
        GUI.Label(new Rect(x, y, w, h), "SOLD OUT", _descStyle);
        _descStyle.alignment = TextAnchor.MiddleLeft;
    }

    private void DrawCombatCard(float x, float y, float w, float h, CombatCardData card, PlayerInventory inv, int slotIndex, ShopPhaseManager spm)
    {
        Rect fullCardRect = new Rect(x, y, w, h);
        bool isHovered = fullCardRect.Contains(Event.current.mousePosition);

        // Check upgrade status
        bool isOwned = inv != null && inv.OwnsCard(card.cardId);
        int upgradeLevel = isOwned ? inv.GetUpgradeLevel(card.cardId) : -1;
        bool isMaxed = upgradeLevel >= PlayerInventory.MaxUpgradeLevel;

        // Background
        GUI.color = isHovered ? new Color(0.08f, 0.1f, 0.18f) : new Color(0.05f, 0.06f, 0.12f);
        GUI.DrawTexture(fullCardRect, _whiteTex);

        Color rarityCol = card.rarity == CardRarity.Basic ? new Color(0.6f, 0.6f, 0.6f) :
                         card.rarity == CardRarity.Advanced ? new Color(0.2f, 0.5f, 1f) :
                         new Color(1f, 0.75f, 0.1f);

        // Enhanced glow border on hover
        if (isHovered)
        {
            GUI.color = new Color(1f, 0.5f, 0.15f, 0.8f); // Orange glow
            GUI.DrawTexture(new Rect(x - 2, y - 2, w + 4, 4f), _whiteTex);
            GUI.DrawTexture(new Rect(x - 2, y + h - 2, w + 4, 4f), _whiteTex);
            GUI.DrawTexture(new Rect(x - 2, y, 4f, h), _whiteTex);
            GUI.DrawTexture(new Rect(x + w - 2, y, 4f, h), _whiteTex);
        }
        else
        {
            GUI.color = rarityCol;
            GUI.DrawTexture(new Rect(x, y, w, 2f), _whiteTex);
            GUI.DrawTexture(new Rect(x, y + h - 2f, w, 2f), _whiteTex);
        }

        // Card name with glow + upgrade stars
        GUIStyle nameStyle = new GUIStyle(_cardNameStyle);
        Color nameColor = isHovered ? new Color(1f, 1f, 0.3f) : Color.white;
        string displayText = card.displayName.ToUpper();
        if (isOwned && upgradeLevel >= 0)
        {
            string stars = inv.GetUpgradeStars(card.cardId);
            displayText = $"{displayText} {stars}";
        }
        CyberpunkGUIUtils.DrawGlowText(new Rect(x + 8f, y + 4f, w - 70f, 18f), displayText, nameColor, nameStyle, nameColor);

        // Description with better visibility
        _descStyle.fontSize = 11;
        GUI.color = isHovered ? new Color(1f, 1f, 1f, 0.95f) : new Color(0.9f, 0.9f, 0.9f, 0.85f);

        string descText = card.description;
        if (isOwned && upgradeLevel >= 0)
        {
            float currentMult = inv.GetUpgradeMultiplier(card.cardId);
            int baseDmg = card.baseDamage;
            int currentDmg = Mathf.RoundToInt(baseDmg * currentMult);
            descText += $"\n\nUpgraded to Tier {upgradeLevel + 1}: {currentDmg}% damage";
            if (!isMaxed && upgradeLevel + 1 <= PlayerInventory.MaxUpgradeLevel)
            {
                float nextMult = upgradeLevel switch
                {
                    0 => 1.1f,
                    1 => 1.2f,
                    2 => 1.35f,
                    _ => 1.0f
                };
                int nextDmg = Mathf.RoundToInt(baseDmg * nextMult);
                descText += $" (Next: {nextDmg}%, +{(nextDmg - currentDmg)}%)";
            }
        }

        GUI.Label(new Rect(x + 8f, y + 24f, w - 16f, h - 54f), descText, _descStyle);

        // Cost with glow
        GUIStyle costStyle = new GUIStyle(_costStyle);
        CyberpunkGUIUtils.DrawGlowText(new Rect(x + 8f, y + h - 22f, 50f, 18f), $"{card.cost} CR", CyberpunkGUIUtils.NEON_YELLOW, costStyle, CyberpunkGUIUtils.NEON_YELLOW);

        // Button logic: BUY, UPGRADE, or MAXED
        bool inventoryFull = inv != null && inv.ownedCombatCards.Count >= PlayerInventory.MaxCombatCards;
        Rect btnRect = new Rect(x + w - 75f, y + h - 24f, 70f, 22f);

        if (isOwned && isMaxed)
        {
            // Already maxed out
            GUI.color = new Color(0.6f, 0.3f, 0.3f, 0.6f);
            GUI.Button(btnRect, "MAXED", _buttonStyle);
            GUI.color = Color.white;
        }
        else if (isOwned)
        {
            // Upgrade button
            GUI.color = isHovered ? new Color(0.8f, 0.6f, 0.2f, 0.9f) : new Color(0.7f, 0.5f, 0.1f, 0.8f);
            if (GUI.Button(btnRect, "UPGRADE", _buttonStyle))
            {
                var localPc = GameManager.localPlayer?.GetComponent<PlayerCombat>();
                if (localPc != null) localPc.CmdBuyCombatCard(slotIndex);
                spm.MarkCombatSlotPurchased(slotIndex);
            }
            GUI.color = Color.white;
        }
        else if (inventoryFull)
        {
            // Inventory full
            GUI.color = new Color(0.5f, 0.5f, 0.5f, 0.5f);
            GUI.Button(btnRect, "FULL", _buttonStyle);
            GUI.color = Color.white;
        }
        else
        {
            // Buy button
            GUI.color = isHovered ? new Color(0.2f, 0.9f, 0.4f, 0.9f) : new Color(0.15f, 0.7f, 0.3f, 0.8f);
            if (GUI.Button(btnRect, "BUY", _buttonStyle))
            {
                var localPc = GameManager.localPlayer?.GetComponent<PlayerCombat>();
                if (localPc != null) localPc.CmdBuyCombatCard(slotIndex);
                spm.MarkCombatSlotPurchased(slotIndex);
            }
            GUI.color = Color.white;
        }

        // Hover hints
        if (inventoryFull && !isOwned && isHovered)
        {
            GUIStyle fullHint = new GUIStyle(GUI.skin.label) { fontSize = 10, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            fullHint.normal.textColor = new Color(1f, 0.5f, 0.2f);
            GUI.Label(new Rect(x, y + h - 38f, w, 16f), "REMOVE A CARD FIRST", fullHint);
        }

        if (isHovered)
        {
            _hoveredCardId = card.cardId;
            _tooltipPos = Event.current.mousePosition;
            _tooltipTitle = card.displayName;
            _tooltipText = card.description;
            if (isOwned && upgradeLevel >= 0)
                _tooltipText += $"\n\nTier {upgradeLevel + 1} ({inv.GetUpgradeStars(card.cardId)})";
            _hasTooltip = true;
        }
    }

    private void DrawTraitCard(float x, float y, float w, float h, TraitCardData card, PlayerInventory inv, int slotIndex, ShopPhaseManager spm)
    {
        Rect fullCardRect = new Rect(x, y, w, h);
        bool isHovered = fullCardRect.Contains(Event.current.mousePosition);
        bool alreadyEquipped = inv != null && inv.equippedTraitId == card.traitId;

        // Background
        GUI.color = isHovered ? new Color(0.04f, 0.14f, 0.08f) : new Color(0.03f, 0.08f, 0.05f);
        GUI.DrawTexture(fullCardRect, _whiteTex);

        // Border — green glow for traits
        if (isHovered)
        {
            GUI.color = new Color(0.25f, 1f, 0.5f, 0.8f);
            GUI.DrawTexture(new Rect(x - 2, y - 2, w + 4, 4f), _whiteTex);
            GUI.DrawTexture(new Rect(x - 2, y + h - 2, w + 4, 4f), _whiteTex);
            GUI.DrawTexture(new Rect(x - 2, y, 4f, h), _whiteTex);
            GUI.DrawTexture(new Rect(x + w - 2, y, 4f, h), _whiteTex);
        }
        else
        {
            GUI.color = alreadyEquipped ? new Color(1f, 0.8f, 0.1f) : new Color(0.25f, 0.9f, 0.4f);
            GUI.DrawTexture(new Rect(x, y, w, 2f), _whiteTex);
            GUI.DrawTexture(new Rect(x, y + h - 2f, w, 2f), _whiteTex);
        }

        // PASSIVE badge
        GUI.color = new Color(0.1f, 0.4f, 0.2f, 0.8f);
        GUI.DrawTexture(new Rect(x + w - 68f, y + 4f, 60f, 16f), _whiteTex);
        GUIStyle badgeStyle = new GUIStyle(GUI.skin.label) { fontSize = 9, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        badgeStyle.normal.textColor = new Color(0.3f, 1f, 0.5f);
        GUI.color = Color.white;
        GUI.Label(new Rect(x + w - 68f, y + 4f, 60f, 16f), "PASSIVE", badgeStyle);

        // Card name
        GUIStyle nameStyle = new GUIStyle(_cardNameStyle);
        Color nameColor = isHovered ? new Color(0.4f, 1f, 0.6f) : Color.white;
        CyberpunkGUIUtils.DrawGlowText(new Rect(x + 8f, y + 4f, w - 76f, 18f), card.displayName.ToUpper(), nameColor, nameStyle, nameColor);

        // Effect text
        _descStyle.fontSize = 10;
        GUI.color = isHovered ? new Color(1f, 1f, 1f, 0.95f) : new Color(0.88f, 0.95f, 0.88f, 0.85f);
        GUI.Label(new Rect(x + 8f, y + 24f, w - 16f, h - 54f), card.effect, _descStyle);

        // Cost
        GUIStyle costStyle = new GUIStyle(_costStyle);
        CyberpunkGUIUtils.DrawGlowText(new Rect(x + 8f, y + h - 22f, 60f, 18f), $"{card.cost} CR", CyberpunkGUIUtils.NEON_YELLOW, costStyle, CyberpunkGUIUtils.NEON_YELLOW);

        // Buy / Equipped state
        Rect btnRect = new Rect(x + w - 75f, y + h - 24f, 70f, 22f);
        if (alreadyEquipped)
        {
            GUI.color = new Color(1f, 0.8f, 0.1f, 0.7f);
            GUI.Button(btnRect, "EQUIPPED", _buttonStyle);
        }
        else
        {
            GUI.color = isHovered ? new Color(0.3f, 1f, 0.5f, 0.9f) : new Color(0.2f, 0.75f, 0.35f, 0.8f);
            if (GUI.Button(btnRect, "BUY", _buttonStyle))
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

                foreach (var cardId in botInv.ownedCombatCards.Keys)
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
        float tooltipW  = 420f + artW;
        float headerH   = 38f;
        float descH     = 100f;
        float matchupH  = hasCounters ? (hasTwoParts ? 48f : 28f) : 0f;
        float oppH      = hasOppHint  ? 42f : 0f;
        float sepCount  = (hasCounters ? 1 : 0) + (hasOppHint ? 1 : 0);
        float tooltipH  = headerH + descH + sepCount * 6f + matchupH + oppH + 12f;

        float tooltipX = _tooltipPos.x + 22f;
        float tooltipY = _tooltipPos.y + 22f;
        if (tooltipX + tooltipW > Screen.width)  tooltipX = Screen.width  - tooltipW - 10f;
        if (tooltipY + tooltipH > Screen.height) tooltipY = Screen.height - tooltipH - 10f;

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
        foreach (var opCardId in opponentInv.ownedCombatCards.Keys)
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
        GUI.color = new Color(accent.r * 0.2f, accent.g * 0.2f, accent.b * 0.2f, 0.8f);
        GUI.DrawTexture(new Rect(x, y, w, h), _whiteTex);

        GUI.color = accent;
        GUI.DrawTexture(new Rect(x, y, w, 1.5f), _whiteTex);
        GUI.DrawTexture(new Rect(x, y + h - 1.5f, w, 1.5f), _whiteTex);
        GUI.DrawTexture(new Rect(x, y, 1.5f, h), _whiteTex);
        GUI.DrawTexture(new Rect(x + w - 1.5f, y, 1.5f, h), _whiteTex);

        GUI.color = Color.white;
    }

    private void DrawSectionHeader(float x, float y, float w, string text, Color accent)
    {
        GUI.color = new Color(accent.r, accent.g, accent.b, 0.3f);
        GUI.DrawTexture(new Rect(x, y, w, 32f), _whiteTex);
        GUI.color = Color.white;
        CyberpunkGUIUtils.DrawGlowText(new Rect(x, y, w, 32f), text, accent, _sectionStyle, accent);
    }

    private void DrawLockInButton()
    {
        float btnW = 280f;
        float btnH = 56f;
        float btnX = Screen.width / 2f - btnW / 2f;
        float btnY = Screen.height - 70f;

        GUI.color = new Color(0f, 1f, 0.6f, 0.2f);
        GUI.DrawTexture(new Rect(btnX - 2, btnY - 2, btnW + 4, btnH + 4), _whiteTex);

        GUI.color = new Color(0f, 1f, 0.6f);
        if (GUI.Button(new Rect(btnX, btnY, btnW, btnH), "⚡ LOCK IN & FIGHT ⚡", new GUIStyle(GUI.skin.button) { fontSize = 20, fontStyle = FontStyle.Bold }))
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
            fontSize = 14,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.UpperLeft
        };

        _descStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            alignment = TextAnchor.UpperLeft,
            wordWrap = true
        };

        _costStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft
        };

        _timerStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 18,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };

        _sectionStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 15,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };

        _buttonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 12,
            fontStyle = FontStyle.Bold
        };
    }
}
