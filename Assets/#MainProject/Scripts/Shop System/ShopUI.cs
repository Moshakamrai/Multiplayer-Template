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

    private void Awake() { if (Instance == null) Instance = this; }

    private void OnGUI()
    {
        if (ShopPhaseManager.Instance == null || !ShopPhaseManager.Instance.isShopPhase) return;

        EnsureTextures();
        EnsureStyles();

        var spm = ShopPhaseManager.Instance;
        var localInv = GameManager.localPlayer?.GetComponent<PlayerInventory>();

        // Dark overlay
        GUI.color = new Color(0.03f, 0.03f, 0.06f, 0.97f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _whiteTex);
        GUI.color = Color.white;

        // Title
        GUI.Label(new Rect(Screen.width / 2 - 300, 15, 600, 45), "ROUND SHOP", _titleStyle);

        // Timer
        _timerStyle.normal.textColor = spm.shopTimeRemaining <= 15f ? Color.red : Color.cyan;
        GUI.Label(new Rect(Screen.width / 2 - 150, 60, 300, 35), $"TIME: {Mathf.Max(0f, spm.shopTimeRemaining):F0}s", _timerStyle);

        // Credits
        string creditsStr = localInv != null ? $"CREDITS: {localInv.credits}" : "CREDITS: --";
        GUIStyle creditStyle = new GUIStyle(_timerStyle) { fontSize = 20, normal = { textColor = Color.yellow } };
        GUI.Label(new Rect(Screen.width / 2 - 150, 95, 300, 30), creditsStr, creditStyle);

        // ── COMBAT CARDS (6 slots) ──
        DrawSectionHeader(20, 140, 280, "COMBAT CARDS", new Color(1f, 0.35f, 0.1f));
        DrawCombatCards(20, 175, localInv);

        // ── VEX CARDS (3 slots) ──
        float vexX = Screen.width / 2f - 140f;
        DrawSectionHeader(vexX, 140, 280, "VEX CARDS", new Color(0.85f, 0.35f, 1f));
        DrawVexCards(vexX, 175, localInv);

        // ── TRAIT CARDS (3 slots) ──
        float traitX = Screen.width - 300f;
        DrawSectionHeader(traitX, 140, 280, "TRAIT CARDS", new Color(0.2f, 0.85f, 0.35f));
        DrawTraitCards(traitX, 175, localInv);

        // Lock In button
        DrawLockInButton();
    }

    private void DrawCombatCards(float startX, float startY, PlayerInventory inv)
    {
        var slots = ShopPhaseManager.Instance.CombatShopSlots;
        float cardW = 260f;
        float cardH = 110f;
        float gapY = 8f;

        for (int i = 0; i < 6; i++)
        {
            float y = startY + i * (cardH + gapY);
            bool hasCard = i < slots.Count && slots[i] != null;

            // Background
            GUI.color = new Color(0.06f, 0.06f, 0.1f, 0.92f);
            GUI.DrawTexture(new Rect(startX, y, cardW, cardH), _whiteTex);

            if (hasCard)
            {
                var card = slots[i];
                bool owned = inv != null && inv.ownedCombatCards.Contains(card.cardId);
                bool canAfford = inv != null && inv.credits >= card.cost;

                // Border color by rarity
                Color borderCol = card.rarity switch
                {
                    CardRarity.Basic => new Color(0.6f, 0.6f, 0.6f),
                    CardRarity.Advanced => new Color(0.2f, 0.6f, 1f),
                    CardRarity.Legendary => new Color(1f, 0.8f, 0.1f),
                    _ => Color.gray
                };
                if (owned) borderCol = new Color(0.2f, 1f, 0.4f);

                GUI.color = borderCol;
                GUI.DrawTexture(new Rect(startX, y, cardW, 3f), _whiteTex);
                GUI.DrawTexture(new Rect(startX, y + cardH - 3f, cardW, 3f), _whiteTex);

                GUI.color = Color.white;
                GUI.Label(new Rect(startX + 8f, y + 6f, cardW - 16f, 24f), card.displayName.ToUpper(), _cardNameStyle);

                _descStyle.normal.textColor = new Color(0.75f, 0.75f, 0.8f);
                GUI.Label(new Rect(startX + 8f, y + 30f, cardW - 16f, 50f), card.description, _descStyle);

                string costText = owned ? "OWNED" : $"{card.cost} CR";
                _costStyle.normal.textColor = owned ? new Color(0.2f, 1f, 0.4f) : (canAfford ? Color.yellow : Color.red);
                GUI.Label(new Rect(startX + 8f, y + cardH - 26f, 80f, 22f), costText, _costStyle);

                // Buy button
                if (!owned && canAfford)
                {
                    Rect btnRect = new Rect(startX + cardW - 70f, y + cardH - 30f, 60f, 24f);
                    GUI.color = new Color(0.15f, 0.7f, 0.25f);
                    if (GUI.Button(btnRect, "BUY", new GUIStyle(GUI.skin.button) { fontSize = 12, fontStyle = FontStyle.Bold }))
                    {
                        var localPc = GameManager.localPlayer?.GetComponent<PlayerCombat>();
                        if (localPc != null) localPc.CmdBuyCombatCard(i);
                    }
                    GUI.color = Color.white;
                }
            }
            else
            {
                GUI.color = new Color(0.3f, 0.3f, 0.3f, 0.5f);
                GUI.Label(new Rect(startX, y + cardH / 2 - 10f, cardW, 20f), "SOLD OUT", _cardNameStyle);
                GUI.color = Color.white;
            }
        }
    }

    private void DrawVexCards(float startX, float startY, PlayerInventory inv)
    {
        var slots = ShopPhaseManager.Instance.VexShopSlots;
        float cardW = 260f;
        float cardH = 130f;
        float gapY = 8f;

        for (int i = 0; i < 3; i++)
        {
            float y = startY + i * (cardH + gapY);
            bool hasCard = i < slots.Count && slots[i] != null;

            GUI.color = new Color(0.08f, 0.04f, 0.12f, 0.92f);
            GUI.DrawTexture(new Rect(startX, y, cardW, cardH), _whiteTex);

            if (hasCard)
            {
                var card = slots[i];
                bool canAfford = inv != null && inv.credits >= card.cost;

                GUI.color = new Color(0.85f, 0.35f, 1f);
                GUI.DrawTexture(new Rect(startX, y, cardW, 3f), _whiteTex);
                GUI.DrawTexture(new Rect(startX, y + cardH - 3f, cardW, 3f), _whiteTex);

                GUI.color = Color.white;
                GUI.Label(new Rect(startX + 8f, y + 6f, cardW - 16f, 24f), card.displayName.ToUpper(), _cardNameStyle);

                _descStyle.normal.textColor = new Color(0.8f, 0.7f, 0.9f);
                GUI.Label(new Rect(startX + 8f, y + 30f, cardW - 16f, 40f), card.effect, _descStyle);
                GUI.Label(new Rect(startX + 8f, y + 72f, cardW - 16f, 30f), card.description, _descStyle);

                _costStyle.normal.textColor = canAfford ? Color.yellow : Color.red;
                GUI.Label(new Rect(startX + 8f, y + cardH - 26f, 80f, 22f), $"{card.cost} CR", _costStyle);

                if (canAfford)
                {
                    Rect btnRect = new Rect(startX + cardW - 70f, y + cardH - 30f, 60f, 24f);
                    GUI.color = new Color(0.7f, 0.2f, 0.9f);
                    if (GUI.Button(btnRect, "BUY", new GUIStyle(GUI.skin.button) { fontSize = 12, fontStyle = FontStyle.Bold }))
                    {
                        var localPc = GameManager.localPlayer?.GetComponent<PlayerCombat>();
                        if (localPc != null) localPc.CmdBuyVexCard(i);
                    }
                    GUI.color = Color.white;
                }
            }
            else
            {
                GUI.color = new Color(0.3f, 0.3f, 0.3f, 0.5f);
                GUI.Label(new Rect(startX, y + cardH / 2 - 10f, cardW, 20f), "SOLD OUT", _cardNameStyle);
                GUI.color = Color.white;
            }
        }
    }

    private void DrawTraitCards(float startX, float startY, PlayerInventory inv)
    {
        var slots = ShopPhaseManager.Instance.TraitShopSlots;
        float cardW = 260f;
        float cardH = 130f;
        float gapY = 8f;

        for (int i = 0; i < 3; i++)
        {
            float y = startY + i * (cardH + gapY);
            bool hasCard = i < slots.Count && slots[i] != null;

            GUI.color = new Color(0.04f, 0.08f, 0.05f, 0.92f);
            GUI.DrawTexture(new Rect(startX, y, cardW, cardH), _whiteTex);

            if (hasCard)
            {
                var trait = slots[i];
                bool equipped = inv != null && inv.equippedTraitId == trait.traitId;
                bool canAfford = inv != null && inv.credits >= trait.cost;

                GUI.color = equipped ? new Color(0.2f, 1f, 0.4f) : new Color(0.2f, 0.85f, 0.35f);
                GUI.DrawTexture(new Rect(startX, y, cardW, 3f), _whiteTex);
                GUI.DrawTexture(new Rect(startX, y + cardH - 3f, cardW, 3f), _whiteTex);

                GUI.color = Color.white;
                GUI.Label(new Rect(startX + 8f, y + 6f, cardW - 16f, 24f), trait.displayName.ToUpper(), _cardNameStyle);

                _descStyle.normal.textColor = new Color(0.7f, 0.85f, 0.75f);
                GUI.Label(new Rect(startX + 8f, y + 30f, cardW - 16f, 40f), trait.effect, _descStyle);
                GUI.Label(new Rect(startX + 8f, y + 72f, cardW - 16f, 30f), trait.description, _descStyle);

                string costText = equipped ? "EQUIPPED" : $"{trait.cost} CR";
                _costStyle.normal.textColor = equipped ? new Color(0.2f, 1f, 0.4f) : (canAfford ? Color.yellow : Color.red);
                GUI.Label(new Rect(startX + 8f, y + cardH - 26f, 100f, 22f), costText, _costStyle);

                if (!equipped && canAfford)
                {
                    Rect btnRect = new Rect(startX + cardW - 70f, y + cardH - 30f, 60f, 24f);
                    GUI.color = new Color(0.15f, 0.7f, 0.3f);
                    if (GUI.Button(btnRect, "BUY", new GUIStyle(GUI.skin.button) { fontSize = 12, fontStyle = FontStyle.Bold }))
                    {
                        var localPc = GameManager.localPlayer?.GetComponent<PlayerCombat>();
                        if (localPc != null) localPc.CmdBuyTraitCard(i);
                    }
                    GUI.color = Color.white;
                }
            }
            else
            {
                GUI.color = new Color(0.3f, 0.3f, 0.3f, 0.5f);
                GUI.Label(new Rect(startX, y + cardH / 2 - 10f, cardW, 20f), "SOLD OUT", _cardNameStyle);
                GUI.color = Color.white;
            }
        }
    }

    private void DrawSectionHeader(float x, float y, float w, string text, Color accent)
    {
        GUI.color = accent;
        GUI.DrawTexture(new Rect(x, y, w, 28f), _whiteTex);
        GUI.color = Color.white;
        _sectionStyle.normal.textColor = Color.white;
        GUI.Label(new Rect(x, y, w, 28f), text, _sectionStyle);
    }

    private void DrawLockInButton()
    {
        float btnW = 200f;
        float btnH = 45f;
        float btnX = Screen.width / 2f - btnW / 2f;
        float btnY = Screen.height - 70f;

        GUI.color = new Color(0.15f, 0.8f, 0.3f);
        if (GUI.Button(new Rect(btnX, btnY, btnW, btnH), "LOCK IN & FIGHT", new GUIStyle(GUI.skin.button) { fontSize = 18, fontStyle = FontStyle.Bold }))
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
            fontSize = 32,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        _titleStyle.normal.textColor = new Color(1f, 0.85f, 0.2f);

        _cardNameStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft
        };

        _descStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 10,
            alignment = TextAnchor.MiddleLeft,
            wordWrap = true
        };

        _costStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft
        };

        _timerStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 22,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };

        _sectionStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
    }
}
