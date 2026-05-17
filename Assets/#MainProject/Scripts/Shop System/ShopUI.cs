using UnityEngine;
using System.Collections.Generic;

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
    private GUIStyle _ownedStyle;

    private void Awake() { if (Instance == null) Instance = this; }

    private void OnGUI()
    {
        if (ShopPhaseManager.Instance == null || !ShopPhaseManager.Instance.isShopPhase) return;

        EnsureTextures();
        EnsureStyles();

        var spm = ShopPhaseManager.Instance;
        var localInv = GameManager.localPlayer?.GetComponent<PlayerInventory>();

        // Dark overlay
        GUI.color = new Color(0.02f, 0.02f, 0.04f, 0.98f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _whiteTex);
        GUI.color = Color.white;

        float margin = 15f;
        float topH = 100f;
        float panelW = (Screen.width - margin * 4f) / 3f;
        float panelX1 = margin;
        float panelX2 = margin * 2f + panelW;
        float panelX3 = margin * 3f + panelW * 2f;
        float contentY = topH + 10f;
        float contentH = Screen.height - contentY - 80f;

        // ── HEADER ──
        GUI.Label(new Rect(0, 10, Screen.width, 40), "POST-ROUND SHOP", _titleStyle);

        // Timer + Credits bar
        float barY = 55f;
        _timerStyle.normal.textColor = spm.shopTimeRemaining <= 15f ? Color.red : Color.cyan;
        GUI.Label(new Rect(panelX1, barY, panelW, 30), $"TIME: {Mathf.Max(0f, spm.shopTimeRemaining):F0}s", _timerStyle);

        string creditsStr = localInv != null ? $"CREDITS: {localInv.credits}" : "CREDITS: --";
        GUIStyle creditStyle = new GUIStyle(_timerStyle) { normal = { textColor = Color.yellow } };
        GUI.Label(new Rect(panelX2, barY, panelW, 30), creditsStr, creditStyle);

        string roundStr = $"ROUND {spm.currentShopRound} SHOP";
        GUI.Label(new Rect(panelX3, barY, panelW, 30), roundStr, _timerStyle);

        // ── THREE PANELS ──
        DrawPanelBackground(panelX1, contentY, panelW, contentH, new Color(0.06f, 0.04f, 0.02f, 0.95f));
        DrawPanelBackground(panelX2, contentY, panelW, contentH, new Color(0.06f, 0.02f, 0.06f, 0.95f));
        DrawPanelBackground(panelX3, contentY, panelW, contentH, new Color(0.02f, 0.06f, 0.03f, 0.95f));

        DrawSectionHeader(panelX1, contentY, panelW, "COMBAT CARDS", new Color(1f, 0.5f, 0.15f));
        DrawCombatCards(panelX1, contentY + 35f, panelW, contentH - 35f, localInv);

        DrawSectionHeader(panelX2, contentY, panelW, "VEX CARDS", new Color(0.9f, 0.3f, 1f));
        DrawVexCards(panelX2, contentY + 35f, panelW, contentH - 35f, localInv);

        DrawSectionHeader(panelX3, contentY, panelW, "TRAIT CARDS", new Color(0.25f, 0.9f, 0.4f));
        DrawTraitCards(panelX3, contentY + 35f, panelW, contentH - 35f, localInv);

        // Lock In button
        DrawLockInButton();
    }

    private void DrawPanelBackground(float x, float y, float w, float h, Color col)
    {
        GUI.color = col;
        GUI.DrawTexture(new Rect(x, y, w, h), _whiteTex);
        GUI.color = Color.white;
    }

    private void DrawCombatCards(float startX, float startY, float panelW, float panelH, PlayerInventory inv)
    {
        var slots = ShopPhaseManager.Instance.CombatShopSlots;
        float pad = 10f;
        float cardW = panelW - pad * 2f;
        float cardH = (panelH - pad * 2f - 5f * 5f) / 6f; // 6 cards with 5px gaps
        float x = startX + pad;

        for (int i = 0; i < 6; i++)
        {
            float y = startY + pad + i * (cardH + 5f);
            bool hasCard = i < slots.Count && slots[i] != null;

            // Background
            GUI.color = new Color(0.08f, 0.08f, 0.12f, 0.95f);
            GUI.DrawTexture(new Rect(x, y, cardW, cardH), _whiteTex);

            if (hasCard)
            {
                var card = slots[i];
                bool owned = inv != null && inv.ownedCombatCards.Contains(card.cardId);
                bool canAfford = inv != null && inv.credits >= card.cost;
                bool inventoryFull = inv != null && inv.ownedCombatCards.Count >= 10;

                // Border by rarity
                Color borderCol = card.rarity switch
                {
                    CardRarity.Basic => new Color(0.5f, 0.5f, 0.5f),
                    CardRarity.Advanced => new Color(0.2f, 0.5f, 1f),
                    CardRarity.Legendary => new Color(1f, 0.75f, 0.1f),
                    _ => Color.gray
                };
                if (owned) borderCol = new Color(0.15f, 0.9f, 0.3f);

                GUI.color = borderCol;
                GUI.DrawTexture(new Rect(x, y, cardW, 4f), _whiteTex);
                GUI.DrawTexture(new Rect(x, y + cardH - 4f, cardW, 4f), _whiteTex);

                // Name
                GUI.color = Color.white;
                GUI.Label(new Rect(x + 10f, y + 6f, cardW - 20f, 22f), card.displayName.ToUpper(), _cardNameStyle);

                // Description
                _descStyle.normal.textColor = new Color(0.8f, 0.8f, 0.85f);
                GUI.Label(new Rect(x + 10f, y + 28f, cardW - 20f, cardH - 55f), card.description, _descStyle);

                // Bottom row: cost + buy button
                string costText;
                if (owned) costText = "OWNED";
                else if (inventoryFull) costText = "FULL";
                else costText = $"{card.cost} CR";

                GUIStyle costSt = owned ? _ownedStyle : _costStyle;
                if (owned) costSt.normal.textColor = new Color(0.15f, 0.9f, 0.3f);
                else if (inventoryFull) costSt.normal.textColor = Color.red;
                else costSt.normal.textColor = canAfford ? Color.yellow : Color.red;
                GUI.Label(new Rect(x + 10f, y + cardH - 26f, 80f, 22f), costText, costSt);

                if (!owned && canAfford && !inventoryFull)
                {
                    float btnW = 55f;
                    float btnH = 26f;
                    Rect btnRect = new Rect(x + cardW - btnW - 8f, y + cardH - btnH - 6f, btnW, btnH);
                    GUI.color = new Color(0.12f, 0.65f, 0.22f);
                    if (GUI.Button(btnRect, "BUY", new GUIStyle(GUI.skin.button) { fontSize = 13, fontStyle = FontStyle.Bold }))
                    {
                        var localPc = GameManager.localPlayer?.GetComponent<PlayerCombat>();
                        if (localPc != null) localPc.CmdBuyCombatCard(i);
                    }
                    GUI.color = Color.white;
                }
            }
            else
            {
                GUI.color = new Color(0.3f, 0.3f, 0.3f, 0.4f);
                GUI.Label(new Rect(x, y + cardH / 2 - 12f, cardW, 24f), "SOLD OUT", _cardNameStyle);
                GUI.color = Color.white;
            }
        }
    }

    private void DrawVexCards(float startX, float startY, float panelW, float panelH, PlayerInventory inv)
    {
        var slots = ShopPhaseManager.Instance.VexShopSlots;
        float pad = 10f;
        float cardW = panelW - pad * 2f;
        float cardH = (panelH - pad * 2f - 2f * 5f) / 3f;
        float x = startX + pad;

        for (int i = 0; i < 3; i++)
        {
            float y = startY + pad + i * (cardH + 5f);
            bool hasCard = i < slots.Count && slots[i] != null;

            GUI.color = new Color(0.08f, 0.04f, 0.12f, 0.95f);
            GUI.DrawTexture(new Rect(x, y, cardW, cardH), _whiteTex);

            if (hasCard)
            {
                var card = slots[i];
                bool canAfford = inv != null && inv.credits >= card.cost;

                GUI.color = new Color(0.85f, 0.3f, 1f);
                GUI.DrawTexture(new Rect(x, y, cardW, 4f), _whiteTex);
                GUI.DrawTexture(new Rect(x, y + cardH - 4f, cardW, 4f), _whiteTex);

                GUI.color = Color.white;
                GUI.Label(new Rect(x + 10f, y + 6f, cardW - 20f, 22f), card.displayName.ToUpper(), _cardNameStyle);

                _descStyle.normal.textColor = new Color(0.85f, 0.75f, 0.95f);
                GUI.Label(new Rect(x + 10f, y + 28f, cardW - 20f, 30f), card.effect, _descStyle);
                GUI.Label(new Rect(x + 10f, y + 58f, cardW - 20f, cardH - 90f), card.description, _descStyle);

                _costStyle.normal.textColor = canAfford ? Color.yellow : Color.red;
                GUI.Label(new Rect(x + 10f, y + cardH - 26f, 80f, 22f), $"{card.cost} CR", _costStyle);

                if (canAfford)
                {
                    float btnW = 55f;
                    float btnH = 26f;
                    Rect btnRect = new Rect(x + cardW - btnW - 8f, y + cardH - btnH - 6f, btnW, btnH);
                    GUI.color = new Color(0.7f, 0.15f, 0.9f);
                    if (GUI.Button(btnRect, "BUY", new GUIStyle(GUI.skin.button) { fontSize = 13, fontStyle = FontStyle.Bold }))
                    {
                        var localPc = GameManager.localPlayer?.GetComponent<PlayerCombat>();
                        if (localPc != null) localPc.CmdBuyVexCard(i);
                    }
                    GUI.color = Color.white;
                }
            }
            else
            {
                GUI.color = new Color(0.3f, 0.3f, 0.3f, 0.4f);
                GUI.Label(new Rect(x, y + cardH / 2 - 12f, cardW, 24f), "SOLD OUT", _cardNameStyle);
                GUI.color = Color.white;
            }
        }
    }

    private void DrawTraitCards(float startX, float startY, float panelW, float panelH, PlayerInventory inv)
    {
        var slots = ShopPhaseManager.Instance.TraitShopSlots;
        float pad = 10f;
        float cardW = panelW - pad * 2f;
        float cardH = (panelH - pad * 2f - 2f * 5f) / 3f;
        float x = startX + pad;

        for (int i = 0; i < 3; i++)
        {
            float y = startY + pad + i * (cardH + 5f);
            bool hasCard = i < slots.Count && slots[i] != null;

            GUI.color = new Color(0.04f, 0.08f, 0.05f, 0.95f);
            GUI.DrawTexture(new Rect(x, y, cardW, cardH), _whiteTex);

            if (hasCard)
            {
                var trait = slots[i];
                bool equipped = inv != null && inv.equippedTraitId == trait.traitId;
                bool canAfford = inv != null && inv.credits >= trait.cost;

                GUI.color = equipped ? new Color(0.15f, 0.9f, 0.3f) : new Color(0.2f, 0.85f, 0.35f);
                GUI.DrawTexture(new Rect(x, y, cardW, 4f), _whiteTex);
                GUI.DrawTexture(new Rect(x, y + cardH - 4f, cardW, 4f), _whiteTex);

                GUI.color = Color.white;
                GUI.Label(new Rect(x + 10f, y + 6f, cardW - 20f, 22f), trait.displayName.ToUpper(), _cardNameStyle);

                _descStyle.normal.textColor = new Color(0.75f, 0.9f, 0.8f);
                GUI.Label(new Rect(x + 10f, y + 28f, cardW - 20f, 30f), trait.effect, _descStyle);
                GUI.Label(new Rect(x + 10f, y + 58f, cardW - 20f, cardH - 90f), trait.description, _descStyle);

                string costText = equipped ? "EQUIPPED" : $"{trait.cost} CR";
                GUIStyle costSt = equipped ? _ownedStyle : _costStyle;
                costSt.normal.textColor = equipped ? new Color(0.15f, 0.9f, 0.3f) : (canAfford ? Color.yellow : Color.red);
                GUI.Label(new Rect(x + 10f, y + cardH - 26f, 100f, 22f), costText, costSt);

                if (!equipped && canAfford)
                {
                    float btnW = 55f;
                    float btnH = 26f;
                    Rect btnRect = new Rect(x + cardW - btnW - 8f, y + cardH - btnH - 6f, btnW, btnH);
                    GUI.color = new Color(0.12f, 0.65f, 0.25f);
                    if (GUI.Button(btnRect, "BUY", new GUIStyle(GUI.skin.button) { fontSize = 13, fontStyle = FontStyle.Bold }))
                    {
                        var localPc = GameManager.localPlayer?.GetComponent<PlayerCombat>();
                        if (localPc != null) localPc.CmdBuyTraitCard(i);
                    }
                    GUI.color = Color.white;
                }
            }
            else
            {
                GUI.color = new Color(0.3f, 0.3f, 0.3f, 0.4f);
                GUI.Label(new Rect(x, y + cardH / 2 - 12f, cardW, 24f), "SOLD OUT", _cardNameStyle);
                GUI.color = Color.white;
            }
        }
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
        float btnW = 220f;
        float btnH = 50f;
        float btnX = Screen.width / 2f - btnW / 2f;
        float btnY = Screen.height - 65f;

        GUI.color = new Color(0.12f, 0.85f, 0.3f);
        if (GUI.Button(new Rect(btnX, btnY, btnW, btnH), "LOCK IN & FIGHT", new GUIStyle(GUI.skin.button) { fontSize = 20, fontStyle = FontStyle.Bold }))
        {
            var localPc = GameManager.localPlayer?.GetComponent<PlayerCombat>();
            if (localPc != null) localPc.CmdLockInPostRoundShop();
        }
        GUI.color = Color.white;
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
        _titleStyle.normal.textColor = new Color(1f, 0.85f, 0.2f);

        _cardNameStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 16,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };

        _descStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            alignment = TextAnchor.MiddleLeft,
            wordWrap = true
        };

        _costStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft
        };

        _ownedStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
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
            fontSize = 16,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
    }
}
