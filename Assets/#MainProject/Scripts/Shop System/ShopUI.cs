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

        if (inv != null && (inv.ownedCombatCards.Count > 0 || inv.ownedVexCards.Count > 0 || !string.IsNullOrEmpty(inv.equippedTraitId)))
        {
            // Combat cards
            if (inv.ownedCombatCards.Count > 0)
            {
                GUI.color = new Color(0.3f, 0.3f, 0.4f);
                GUI.Label(new Rect(x + MARGIN, cardY, cardW, 22), $"COMBAT ({inv.ownedCombatCards.Count})", _descStyle);
                cardY += 24f;

                foreach (var cardId in inv.ownedCombatCards)
                {
                    if (cardY + cardH > y + h - 10) break;
                    DrawInventoryCard(x + MARGIN, cardY, cardW, cardH, cardId, "Combat", true);
                    cardY += cardH + pad;
                }
            }

            // Vex cards
            if (inv.ownedVexCards.Count > 0)
            {
                GUI.color = new Color(0.3f, 0.3f, 0.4f);
                GUI.Label(new Rect(x + MARGIN, cardY, cardW, 22), $"VEX ({inv.ownedVexCards.Count})", _descStyle);
                cardY += 24f;

                foreach (var cardId in inv.ownedVexCards)
                {
                    if (cardY + cardH > y + h - 10) break;
                    DrawInventoryCard(x + MARGIN, cardY, cardW, cardH, cardId, "Vex", false);
                    cardY += cardH + pad;
                }
            }

            // Trait
            if (!string.IsNullOrEmpty(inv.equippedTraitId))
            {
                GUI.color = new Color(0.3f, 0.3f, 0.4f);
                GUI.Label(new Rect(x + MARGIN, cardY, cardW, 22), "TRAIT", _descStyle);
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
        else if (category == "Vex")
        {
            var card = CardDatabase.VexCards.FirstOrDefault(c => c.cardId == cardId);
            if (card != null)
            {
                displayName = card.displayName;
                description = card.description;
                cost = card.cost;
                rarityColor = new Color(0.9f, 0.3f, 1f);
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
        float panelW = (panelTotalW - MARGIN * 2) / 3f;
        float x1 = startX;
        float x2 = startX + panelW + MARGIN;
        float x3 = startX + panelW * 2 + MARGIN * 2;

        if (spm.CombatShopSlots != null)
            DrawCombatShopPanel(x1, startY, panelW, panelH, "COMBAT CARDS", new Color(1f, 0.5f, 0.15f), spm.CombatShopSlots, inv);
        if (spm.VexShopSlots != null)
            DrawVexShopPanel(x2, startY, panelW, panelH, "VEX CARDS", new Color(0.9f, 0.3f, 1f), spm.VexShopSlots, inv);
        if (spm.TraitShopSlots != null)
            DrawTraitShopPanel(x3, startY, panelW, panelH, "TRAIT CARDS", new Color(0.25f, 0.9f, 0.4f), spm.TraitShopSlots, inv);
    }

    private void DrawCombatShopPanel(float x, float y, float w, float h, string title, Color accent, IReadOnlyList<CombatCardData> slots, PlayerInventory inv)
    {
        DrawPanelBorder(x, y, w, h, accent);
        DrawSectionHeader(x, y, w, title, accent);

        float pad = MARGIN;
        float cardW = w - pad * 2;
        float cardH = (h - 40f - pad * 5) / 6f;

        for (int i = 0; i < 6; i++)
        {
            float cardY = y + 40f + i * (cardH + pad);
            bool hasCard = i < slots.Count && slots[i] != null;

            if (hasCard)
                DrawCombatCard(x + pad, cardY, cardW, cardH, slots[i], inv, i);
            else
                DrawEmptySlot(x + pad, cardY, cardW, cardH);
        }
    }

    private void DrawVexShopPanel(float x, float y, float w, float h, string title, Color accent, IReadOnlyList<VexCardData> slots, PlayerInventory inv)
    {
        DrawPanelBorder(x, y, w, h, accent);
        DrawSectionHeader(x, y, w, title, accent);

        float pad = MARGIN;
        float cardW = w - pad * 2;
        float cardH = (h - 40f - pad * 2) / 3f;

        for (int i = 0; i < 3; i++)
        {
            float cardY = y + 40f + i * (cardH + pad);
            bool hasCard = i < slots.Count && slots[i] != null;

            if (hasCard)
                DrawVexCard(x + pad, cardY, cardW, cardH, slots[i], inv, i);
            else
                DrawEmptySlot(x + pad, cardY, cardW, cardH);
        }
    }

    private void DrawTraitShopPanel(float x, float y, float w, float h, string title, Color accent, IReadOnlyList<TraitCardData> slots, PlayerInventory inv)
    {
        DrawPanelBorder(x, y, w, h, accent);
        DrawSectionHeader(x, y, w, title, accent);

        float pad = MARGIN;
        float cardW = w - pad * 2;
        float cardH = (h - 40f - pad * 2) / 3f;

        for (int i = 0; i < 3; i++)
        {
            float cardY = y + 40f + i * (cardH + pad);
            bool hasCard = i < slots.Count && slots[i] != null;

            if (hasCard)
                DrawTraitCard(x + pad, cardY, cardW, cardH, slots[i], inv, i);
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

    private void DrawCombatCard(float x, float y, float w, float h, CombatCardData card, PlayerInventory inv, int slotIndex)
    {
        GUI.color = new Color(0.05f, 0.06f, 0.12f);
        GUI.DrawTexture(new Rect(x, y, w, h), _whiteTex);

        Color rarityCol = card.rarity == CardRarity.Basic ? new Color(0.6f, 0.6f, 0.6f) :
                         card.rarity == CardRarity.Advanced ? new Color(0.2f, 0.5f, 1f) :
                         new Color(1f, 0.75f, 0.1f);
        GUI.color = rarityCol;
        GUI.DrawTexture(new Rect(x, y, w, 2f), _whiteTex);
        GUI.DrawTexture(new Rect(x, y + h - 2f, w, 2f), _whiteTex);

        GUI.color = Color.white;
        GUI.Label(new Rect(x + 8f, y + 4f, w - 70f, 18f), card.displayName.ToUpper(), _cardNameStyle);

        _descStyle.fontSize = 11;
        GUI.Label(new Rect(x + 8f, y + 24f, w - 16f, h - 54f), card.description, _descStyle);

        _costStyle.normal.textColor = new Color(1f, 1f, 0f);
        GUI.Label(new Rect(x + 8f, y + h - 22f, 50f, 18f), $"{card.cost} CR", _costStyle);

        Rect btnRect = new Rect(x + w - 55f, y + h - 24f, 50f, 22f);
        GUI.color = new Color(0.15f, 0.7f, 0.3f, 0.8f);
        if (GUI.Button(btnRect, "BUY", _buttonStyle))
        {
            var localPc = GameManager.localPlayer?.GetComponent<PlayerCombat>();
            if (localPc != null) localPc.CmdBuyCombatCard(slotIndex);
        }
        GUI.color = Color.white;

        Rect fullCardRect = new Rect(x, y, w, h);
        if (fullCardRect.Contains(Event.current.mousePosition))
        {
            _hoveredCardId = card.cardId;
            _tooltipPos = Event.current.mousePosition;
            _tooltipTitle = card.displayName;
            _tooltipText = card.description;
            _hasTooltip = true;
        }
    }

    private void DrawVexCard(float x, float y, float w, float h, VexCardData card, PlayerInventory inv, int slotIndex)
    {
        GUI.color = new Color(0.05f, 0.06f, 0.12f);
        GUI.DrawTexture(new Rect(x, y, w, h), _whiteTex);

        GUI.color = new Color(0.9f, 0.3f, 1f);
        GUI.DrawTexture(new Rect(x, y, w, 2f), _whiteTex);
        GUI.DrawTexture(new Rect(x, y + h - 2f, w, 2f), _whiteTex);

        GUI.color = Color.white;
        GUI.Label(new Rect(x + 8f, y + 4f, w - 70f, 18f), card.displayName.ToUpper(), _cardNameStyle);

        _descStyle.fontSize = 10;
        string fullDesc = $"{card.effect}\n{card.description}";
        GUI.Label(new Rect(x + 8f, y + 24f, w - 16f, h - 54f), fullDesc, _descStyle);

        _costStyle.normal.textColor = new Color(1f, 1f, 0f);
        GUI.Label(new Rect(x + 8f, y + h - 22f, 50f, 18f), $"{card.cost} CR", _costStyle);

        Rect btnRect = new Rect(x + w - 55f, y + h - 24f, 50f, 22f);
        GUI.color = new Color(0.15f, 0.7f, 0.3f, 0.8f);
        if (GUI.Button(btnRect, "BUY", _buttonStyle))
        {
            var localPc = GameManager.localPlayer?.GetComponent<PlayerCombat>();
            if (localPc != null) localPc.CmdBuyVexCard(slotIndex);
        }
        GUI.color = Color.white;

        Rect fullCardRect = new Rect(x, y, w, h);
        if (fullCardRect.Contains(Event.current.mousePosition))
        {
            _hoveredCardId = card.cardId;
            _tooltipPos = Event.current.mousePosition;
            _tooltipTitle = card.displayName;
            _tooltipText = $"{card.effect}\n{card.description}";
            _hasTooltip = true;
        }
    }

    private void DrawTraitCard(float x, float y, float w, float h, TraitCardData card, PlayerInventory inv, int slotIndex)
    {
        GUI.color = new Color(0.05f, 0.06f, 0.12f);
        GUI.DrawTexture(new Rect(x, y, w, h), _whiteTex);

        GUI.color = new Color(0.25f, 0.9f, 0.4f);
        GUI.DrawTexture(new Rect(x, y, w, 2f), _whiteTex);
        GUI.DrawTexture(new Rect(x, y + h - 2f, w, 2f), _whiteTex);

        GUI.color = Color.white;
        GUI.Label(new Rect(x + 8f, y + 4f, w - 70f, 18f), card.displayName.ToUpper(), _cardNameStyle);

        _descStyle.fontSize = 10;
        string fullDesc = $"{card.effect}\n{card.description}";
        GUI.Label(new Rect(x + 8f, y + 24f, w - 16f, h - 54f), fullDesc, _descStyle);

        _costStyle.normal.textColor = new Color(1f, 1f, 0f);
        GUI.Label(new Rect(x + 8f, y + h - 22f, 50f, 18f), $"{card.cost} CR", _costStyle);

        Rect btnRect = new Rect(x + w - 55f, y + h - 24f, 50f, 22f);
        GUI.color = new Color(0.15f, 0.7f, 0.3f, 0.8f);
        if (GUI.Button(btnRect, "BUY", _buttonStyle))
        {
            var localPc = GameManager.localPlayer?.GetComponent<PlayerCombat>();
            if (localPc != null) localPc.CmdBuyTraitCard(slotIndex);
        }
        GUI.color = Color.white;

        Rect fullCardRect = new Rect(x, y, w, h);
        if (fullCardRect.Contains(Event.current.mousePosition))
        {
            _hoveredCardId = card.traitId;
            _tooltipPos = Event.current.mousePosition;
            _tooltipTitle = card.displayName;
            _tooltipText = $"{card.effect}\n{card.description}";
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

        if (botInv != null && (botInv.ownedCombatCards.Count > 0 || botInv.ownedVexCards.Count > 0 || !string.IsNullOrEmpty(botInv.equippedTraitId)))
        {
            if (botInv.ownedCombatCards.Count > 0)
            {
                GUI.color = new Color(0.3f, 0.3f, 0.4f);
                GUI.Label(new Rect(x + MARGIN, cardY, cardW, 22), $"COMBAT ({botInv.ownedCombatCards.Count})", _descStyle);
                cardY += 24f;

                foreach (var cardId in botInv.ownedCombatCards)
                {
                    if (cardY + cardH > y + h - 10) break;
                    DrawOpponentCard(x + MARGIN, cardY, cardW, cardH, cardId, "Combat");
                    cardY += cardH + pad;
                }
            }

            if (botInv.ownedVexCards.Count > 0)
            {
                GUI.color = new Color(0.3f, 0.3f, 0.4f);
                GUI.Label(new Rect(x + MARGIN, cardY, cardW, 22), $"VEX ({botInv.ownedVexCards.Count})", _descStyle);
                cardY += 24f;

                foreach (var cardId in botInv.ownedVexCards)
                {
                    if (cardY + cardH > y + h - 10) break;
                    DrawOpponentCard(x + MARGIN, cardY, cardW, cardH, cardId, "Vex");
                    cardY += cardH + pad;
                }
            }

            if (!string.IsNullOrEmpty(botInv.equippedTraitId))
            {
                GUI.color = new Color(0.3f, 0.3f, 0.4f);
                GUI.Label(new Rect(x + MARGIN, cardY, cardW, 22), "TRAIT", _descStyle);
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
        else if (category == "Vex")
        {
            var card = CardDatabase.VexCards.FirstOrDefault(c => c.cardId == cardId);
            if (card != null)
            {
                displayName = card.displayName;
                description = card.description;
                rarityColor = new Color(0.9f, 0.3f, 1f);
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

        float tooltipW = 350f;
        float tooltipH = 220f;
        float tooltipX = _tooltipPos.x + 20f;
        float tooltipY = _tooltipPos.y + 20f;

        if (tooltipX + tooltipW > Screen.width) tooltipX = Screen.width - tooltipW - 10f;
        if (tooltipY + tooltipH > Screen.height) tooltipY = Screen.height - tooltipH - 10f;

        GUI.color = new Color(0f, 0.2f, 0.3f, 0.95f);
        GUI.DrawTexture(new Rect(tooltipX - 2, tooltipY - 2, tooltipW + 4, tooltipH + 4), _whiteTex);

        GUI.color = new Color(0f, 1f, 0.8f);
        GUI.DrawTexture(new Rect(tooltipX, tooltipY, tooltipW, 2f), _whiteTex);
        GUI.DrawTexture(new Rect(tooltipX, tooltipY + tooltipH - 2f, tooltipW, 2f), _whiteTex);

        GUI.color = Color.white;
        _cardNameStyle.fontSize = 16;
        GUI.Label(new Rect(tooltipX + 10f, tooltipY + 8f, tooltipW - 20f, 26f), _tooltipTitle.ToUpper(), _cardNameStyle);
        _cardNameStyle.fontSize = 14;

        _descStyle.fontSize = 12;
        GUI.Label(new Rect(tooltipX + 10f, tooltipY + 36f, tooltipW - 20f, tooltipH - 50f), _tooltipText, _descStyle);
    }

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
        GUI.color = accent;
        GUI.DrawTexture(new Rect(x, y, w, 32f), _whiteTex);
        GUI.color = Color.white;
        _sectionStyle.normal.textColor = Color.white;
        GUI.Label(new Rect(x, y, w, 32f), text, _sectionStyle);
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
