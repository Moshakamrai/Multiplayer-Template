using Mirror;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Handles all shop UI rendering via OnGUI.
/// Pre-round shop: each player picks 3 cards from 6 basics (not shared).
/// Between-round shop: 60 seconds total, players alternate 20s each.
/// After shop: shows track picker (blurred behind it until loser picks).
/// </summary>
public class ShopUI : MonoBehaviour
{
    public static ShopUI Instance;

    // -----------------------------------------------------------------------
    // State
    // -----------------------------------------------------------------------
    public enum UIPhase
    {
        Hidden,
        PreRoundShop,   // before round 1
        BetweenRound,   // 60s alternating shop
        TrackPicker,    // loser picks next track
        MatchResult     // match over screen
    }

    private UIPhase _phase = UIPhase.Hidden;

    // Pre-round shop
    private List<ShopSlot> _preRoundSlots = new List<ShopSlot>();
    private int _p1PicksRemaining = 3;
    private int _p2PicksRemaining = 3;
    private int _coinWinnerIndex = 0; // picks track after first round
    private bool _preRoundP1Done = false;
    private bool _preRoundP2Done = false;

    // Between-round shop
    private float _shopTimer = 0f;
    private const float ShopTotalTime = 60f;
    private const float TurnTime = 20f;
    private int _currentBuyerIndex = 0; // 0 or 1
    private bool _shopRunning = false;
    private int _loserIndex = -1; // picks track after shop

    // Track picker
    private int _trackPickerIndex = 0;
    private List<string> _customMapNames = new List<string>();

    // Match result
    private int _matchWinnerIndex = -1;
    private int _finalW1, _finalW2;

    // Deck management (remove card prompt)
    private bool _showDeckManager = false;
    private int _pendingBuyCardId = -1;
    private int _pendingBuyPlayerIndex = -1;

    // Textures
    private Texture2D _whiteTex;
    private Texture2D _blurOverlay;

    private void Awake()
    {
        if (Instance == null) Instance = this;
    }

    private void Start()
    {
        _whiteTex = new Texture2D(1, 1);
        _whiteTex.SetPixel(0, 0, Color.white);
        _whiteTex.Apply();

        _blurOverlay = new Texture2D(1, 1);
        _blurOverlay.SetPixel(0, 0, new Color(0, 0, 0, 0.7f));
        _blurOverlay.Apply();
    }

    // -----------------------------------------------------------------------
    // Public openers called by MatchManager
    // -----------------------------------------------------------------------
    public void OpenPreRoundShop(int coinWinnerIndex)
    {
        _coinWinnerIndex = coinWinnerIndex;
        _preRoundSlots = ShopManager.Instance.GeneratePreRoundShop();
        _p1PicksRemaining = 3;
        _p2PicksRemaining = 3;
        _preRoundP1Done = false;
        _preRoundP2Done = false;
        _phase = UIPhase.PreRoundShop;
    }

    public void OpenBetweenRoundShop()
    {
        int round = MatchManager.Instance != null ? MatchManager.Instance.currentRound : 1;
        ShopManager.Instance.GenerateBetweenRoundShop(round);

        _loserIndex = MatchManager.Instance != null ? MatchManager.Instance.trackPickerIndex : 0;

        // Loser buys first (as per spec: "loser gets to buy first card")
        _currentBuyerIndex = _loserIndex;
        _shopTimer = ShopTotalTime;
        _shopRunning = true;
        _phase = UIPhase.BetweenRound;
    }

    public void ShowTrackPicker(int pickerIndex)
    {
        _trackPickerIndex = pickerIndex;
        _customMapNames.Clear();

        string registry = PlayerPrefs.GetString("CustomMapRegistry", "");
        if (!string.IsNullOrEmpty(registry))
            foreach (string n in registry.Split('|'))
                if (!string.IsNullOrEmpty(n) && PlayerPrefs.HasKey("CustomMap_" + n) && !_customMapNames.Contains(n))
                    _customMapNames.Add(n);

        _phase = UIPhase.TrackPicker;
    }

    public void ShowMatchResult(int winnerIndex, int w1, int w2)
    {
        _matchWinnerIndex = winnerIndex;
        _finalW1 = w1;
        _finalW2 = w2;
        _phase = UIPhase.MatchResult;
    }

    public void CloseAll()
    {
        _phase = UIPhase.Hidden;
        _shopRunning = false;
    }

    // -----------------------------------------------------------------------
    // Update: shop timer
    // -----------------------------------------------------------------------
    private void Update()
    {
        if (_phase != UIPhase.BetweenRound || !_shopRunning) return;

        _shopTimer -= Time.deltaTime;

        // Auto-advance turn at turn boundaries
        float timeUsed = ShopTotalTime - _shopTimer;
        int expectedTurn = Mathf.FloorToInt(timeUsed / TurnTime); // 0 or 1
        int expectedBuyer = expectedTurn == 0 ? _loserIndex : (1 - _loserIndex);
        if (_currentBuyerIndex != expectedBuyer)
            _currentBuyerIndex = expectedBuyer;

        if (_shopTimer <= 0f)
        {
            _shopTimer = 0f;
            _shopRunning = false;
            StartCoroutine(CloseShopThenTrackPicker());
        }
    }

    private IEnumerator CloseShopThenTrackPicker()
    {
        yield return new WaitForSeconds(0.5f);
        _phase = UIPhase.TrackPicker;

        if (NetworkServer.active && MatchManager.Instance != null)
            MatchManager.Instance.OnBetweenRoundShopClosed();
    }

    // -----------------------------------------------------------------------
    // OnGUI
    // -----------------------------------------------------------------------
    private void OnGUI()
    {
        if (_whiteTex == null) return;

        switch (_phase)
        {
            case UIPhase.PreRoundShop:  DrawPreRoundShop();  break;
            case UIPhase.BetweenRound:  DrawBetweenRoundShop(); break;
            case UIPhase.TrackPicker:   DrawTrackPicker();   break;
            case UIPhase.MatchResult:   DrawMatchResult();   break;
        }

        if (_showDeckManager)
            DrawDeckManager();
    }

    // -----------------------------------------------------------------------
    // PRE-ROUND SHOP
    // -----------------------------------------------------------------------
    private void DrawPreRoundShop()
    {
        // Dark fullscreen overlay
        GUI.color = new Color(0, 0, 0, 0.85f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _whiteTex);
        GUI.color = Color.white;

        GUIStyle titleStyle = MakeStyle(28, FontStyle.Bold, Color.cyan, TextAnchor.MiddleCenter);
        GUIStyle subStyle   = MakeStyle(16, FontStyle.Normal, Color.white, TextAnchor.MiddleCenter);

        // Title
        GUI.Label(new Rect(0, 20, Screen.width, 40), "PRE-ROUND CARD SELECTION", titleStyle);
        GUI.Label(new Rect(0, 65, Screen.width, 30), "Each player picks 3 cards to start their deck. Free starters cost 0.", subStyle);

        // --- PLAYER 1 SECTION (left half) ---
        DrawPlayerPreRoundPanel(0, 0, Screen.width / 2f - 10f);

        // Divider
        GUI.color = new Color(0.5f, 0.5f, 0.5f, 0.8f);
        GUI.DrawTexture(new Rect(Screen.width / 2f - 2f, 100, 4, Screen.height - 120), _whiteTex);
        GUI.color = Color.white;

        // --- PLAYER 2 SECTION (right half) ---
        DrawPlayerPreRoundPanel(1, Screen.width / 2f + 10f, Screen.width / 2f - 10f);
    }

    private void DrawPlayerPreRoundPanel(int playerIndex, float panelX, float panelW)
    {
        bool done = playerIndex == 0 ? _preRoundP1Done : _preRoundP2Done;
        int picks = playerIndex == 0 ? _p1PicksRemaining : _p2PicksRemaining;
        var deck  = ShopManager.Instance.GetDeck(playerIndex);

        GUIStyle headerStyle = MakeStyle(20, FontStyle.Bold, Color.yellow, TextAnchor.MiddleCenter);
        GUIStyle pickStyle   = MakeStyle(14, FontStyle.Normal, Color.white, TextAnchor.MiddleCenter);

        string playerName = MatchManager.Instance != null ? MatchManager.Instance.GetPlayerName(playerIndex) : $"Player {playerIndex + 1}";
        GUI.Label(new Rect(panelX, 100, panelW, 30), playerName.ToUpper(), headerStyle);
        GUI.Label(new Rect(panelX, 130, panelW, 25), done ? "READY!" : $"Picks remaining: {picks}", pickStyle);

        if (done)
        {
            GUIStyle doneStyle = MakeStyle(18, FontStyle.Bold, Color.green, TextAnchor.MiddleCenter);
            GUI.Label(new Rect(panelX, 160, panelW, 40), "✓ CONFIRMED", doneStyle);
            DrawDeckPreview(playerIndex, panelX, 210, panelW);
            return;
        }

        // Draw the 6 card slots in a 3x2 grid
        float cardW = 140f, cardH = 110f, gap = 12f;
        float gridW = 3 * cardW + 2 * gap;
        float startX = panelX + (panelW - gridW) / 2f;
        float startY = 165f;

        for (int i = 0; i < _preRoundSlots.Count && i < 6; i++)
        {
            float cx = startX + (i % 3) * (cardW + gap);
            float cy = startY + (i / 3) * (cardH + gap + 5f);

            ShopSlot slot = _preRoundSlots[i];
            CardData card = CardDatabase.GetCardById(slot.cardId);
            if (card == null) continue;

            bool alreadyPicked = deck.Contains(card.id);
            Color cardColor = alreadyPicked ? Color.green : GetRarityColor(card.rarity);
            string costLabel = card.isStarter ? "FREE" : $"{card.shopCost}c";

            DrawCard(cx, cy, cardW, cardH, card.cardName, card.effect, cardColor, costLabel, alreadyPicked ? null : (System.Action)(() =>
            {
                if (picks > 0 && !alreadyPicked)
                    HandlePreRoundPick(playerIndex, card);
            }));
        }

        // Deck preview below grid
        DrawDeckPreview(playerIndex, panelX, startY + 2 * (cardH + gap) + 20f, panelW);

        // Confirm button (only when picks used up)
        if (picks == 0)
        {
            float btnY = startY + 2 * (cardH + gap) + 20f + (deck.Count * 22f) + 20f;
            GUIStyle btnStyle = MakeStyle(16, FontStyle.Bold, Color.black, TextAnchor.MiddleCenter);
            GUI.color = Color.green;
            if (GUI.Button(new Rect(panelX + panelW / 2f - 80f, btnY, 160f, 40f), "CONFIRM"))
            {
                if (playerIndex == 0) _preRoundP1Done = true;
                else                  _preRoundP2Done = true;

                if (_preRoundP1Done && _preRoundP2Done)
                    OnPreRoundShopComplete();
            }
            GUI.color = Color.white;
        }
    }

    private void HandlePreRoundPick(int playerIndex, CardData card)
    {
        var deck = ShopManager.Instance.GetDeck(playerIndex);

        // LEFT or RIGHT awards both
        if (card.id == 6 || card.id == 7)
        {
            if (!deck.Contains(6)) deck.Add(6);
            if (!deck.Contains(7)) deck.Add(7);
        }
        else
        {
            deck.Add(card.id);
        }

        if (playerIndex == 0) _p1PicksRemaining--;
        else                  _p2PicksRemaining--;
    }

    private void OnPreRoundShopComplete()
    {
        _phase = UIPhase.Hidden;
        if (NetworkServer.active && MatchManager.Instance != null)
            MatchManager.Instance.OnPreRoundShopClosed();
    }

    // -----------------------------------------------------------------------
    // BETWEEN-ROUND SHOP
    // -----------------------------------------------------------------------
    private void DrawBetweenRoundShop()
    {
        // Dark overlay
        GUI.color = new Color(0, 0, 0, 0.88f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _whiteTex);
        GUI.color = Color.white;

        // Timer bar
        float timerFrac = Mathf.Clamp01(_shopTimer / ShopTotalTime);
        GUI.color = new Color(0.1f, 0.1f, 0.15f, 1f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, 50), _whiteTex);
        GUI.color = timerFrac > 0.33f ? Color.cyan : Color.red;
        GUI.DrawTexture(new Rect(0, 0, Screen.width * timerFrac, 50), _whiteTex);
        GUI.color = Color.white;

        // Timer label
        GUIStyle timerStyle = MakeStyle(22, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
        GUI.Label(new Rect(0, 5, Screen.width, 40), $"SHOP  {_shopTimer:F0}s", timerStyle);

        // Turn indicator
        string buyerName = MatchManager.Instance != null ? MatchManager.Instance.GetPlayerName(_currentBuyerIndex) : $"Player {_currentBuyerIndex + 1}";
        float turnTimeLeft = _shopTimer > TurnTime ? (_shopTimer - TurnTime) : _shopTimer;
        GUIStyle turnStyle = MakeStyle(16, FontStyle.Bold, Color.yellow, TextAnchor.MiddleCenter);
        GUI.Label(new Rect(0, 52, Screen.width, 28), $"{buyerName.ToUpper()}'S TURN  ({turnTimeLeft:F0}s)  •  Buys remaining: {MaxBuysLeft(_currentBuyerIndex)}", turnStyle);

        // Shared slots
        DrawSharedShopSlots();

        // Class card columns (sides)
        DrawClassSlots(0, 10f, 160f);               // P1 left side
        DrawClassSlots(1, Screen.width - 170f, 160f); // P2 right side

        // Deck previews at bottom
        DrawDeckPreviewCompact(0, 10f, Screen.height - 180f, 300f);
        DrawDeckPreviewCompact(1, Screen.width - 310f, Screen.height - 180f, 300f);

        // Trait slot (bottom center)
        DrawTraitSlot();
    }

    private int MaxBuysLeft(int playerIndex)
    {
        int bought = playerIndex == 0 ? ShopManager.Instance.p1BoughtThisPhase : ShopManager.Instance.p2BoughtThisPhase;
        return ShopManager.MaxCardsPerShopVisit - bought;
    }

    private void DrawSharedShopSlots()
    {
        var slots = ShopManager.Instance.sharedSlots;
        // Filter out trait slot (drawn separately)
        var cardSlots = new List<ShopSlot>();
        ShopSlot traitSlot = null;
        foreach (var s in slots)
        {
            if (s.slotType == SlotType.Trait) traitSlot = s;
            else cardSlots.Add(s);
        }

        float cardW = 150f, cardH = 120f, gap = 14f;
        int count = cardSlots.Count;
        float totalW = count * cardW + (count - 1) * gap;
        float startX = Screen.width / 2f - totalW / 2f;
        float startY = 95f;

        for (int i = 0; i < cardSlots.Count; i++)
        {
            ShopSlot slot = cardSlots[i];
            CardData card = CardDatabase.GetCardById(slot.cardId);
            if (card == null) continue;

            float cx = startX + i * (cardW + gap);
            Color col = GetRarityColor(card.rarity);
            string costLabel = $"{card.shopCost}c";

            bool canBuy = _currentBuyerIndex >= 0 && MaxBuysLeft(_currentBuyerIndex) > 0;

            DrawCard(cx, startY, cardW, cardH, card.cardName, card.effect, col, costLabel, canBuy ? (System.Action)(() =>
            {
                TryPurchaseCard(_currentBuyerIndex, card.id);
            }) : null);
        }
    }

    private void DrawClassSlots(int playerIndex, float panelX, float panelW)
    {
        var slots = playerIndex == 0 ? ShopManager.Instance.p1ClassSlots : ShopManager.Instance.p2ClassSlots;
        string playerName = MatchManager.Instance != null ? MatchManager.Instance.GetPlayerName(playerIndex) : $"P{playerIndex + 1}";

        GUIStyle hdr = MakeStyle(13, FontStyle.Bold, Color.yellow, TextAnchor.MiddleCenter);
        GUI.Label(new Rect(panelX, 85, panelW, 22), $"{playerName} CLASS CARDS", hdr);

        float cardW = panelW - 10f, cardH = 95f, gap = 8f;
        for (int i = 0; i < slots.Count; i++)
        {
            CardData card = CardDatabase.GetCardById(slots[i].cardId);
            if (card == null) continue;

            float cy = 110f + i * (cardH + gap);
            bool canBuy = _currentBuyerIndex == playerIndex && MaxBuysLeft(playerIndex) > 0;

            DrawCard(panelX + 5f, cy, cardW, cardH, card.cardName, card.effect, new Color(0.4f, 0.8f, 1f), $"{card.shopCost}c", canBuy ? (System.Action)(() =>
            {
                TryPurchaseCard(playerIndex, card.id);
            }) : null);
        }
    }

    private void DrawTraitSlot()
    {
        ShopSlot traitSlot = null;
        foreach (var s in ShopManager.Instance.sharedSlots)
            if (s.slotType == SlotType.Trait) { traitSlot = s; break; }

        if (traitSlot == null) return;

        TraitData trait = CardDatabase.GetTraitById(traitSlot.traitId);
        if (trait == null) return;

        float tw = 220f, th = 80f;
        float tx = Screen.width / 2f - tw / 2f;
        float ty = Screen.height - th - 20f;

        bool canBuy = _currentBuyerIndex >= 0 && MaxBuysLeft(_currentBuyerIndex) > 0;
        DrawCard(tx, ty, tw, th, $"TRAIT: {trait.traitName}", trait.effect, new Color(1f, 0.6f, 0f), $"{trait.shopCost}c", canBuy ? (System.Action)(() =>
        {
            TryPurchaseTrait(_currentBuyerIndex, traitSlot.traitId);
        }) : null);
    }

    private void TryPurchaseCard(int playerIndex, int cardId)
    {
        var deck = ShopManager.Instance.GetDeck(playerIndex);
        if (deck.Count >= ShopManager.DeckLimit)
        {
            // Prompt deck manager
            _pendingBuyCardId = cardId;
            _pendingBuyPlayerIndex = playerIndex;
            _showDeckManager = true;
            return;
        }

        bool success = ShopManager.Instance.TryBuyCard(playerIndex, cardId);
        if (!success)
            Debug.Log($"[ShopUI] P{playerIndex + 1} can't afford card {cardId}");
    }

    private void TryPurchaseTrait(int playerIndex, int traitId)
    {
        bool success = ShopManager.Instance.TryBuyTrait(playerIndex, traitId);
        if (!success)
            Debug.Log($"[ShopUI] P{playerIndex + 1} can't afford trait {traitId}");
    }

    // -----------------------------------------------------------------------
    // DECK MANAGER (remove card popup)
    // -----------------------------------------------------------------------
    private void DrawDeckManager()
    {
        float pw = 500f, ph = 420f;
        float px = Screen.width / 2f - pw / 2f;
        float py = Screen.height / 2f - ph / 2f;

        GUI.color = new Color(0, 0, 0, 0.95f);
        GUI.DrawTexture(new Rect(px, py, pw, ph), _whiteTex);
        GUI.color = new Color(1f, 0.4f, 0f);
        DrawBorder(px, py, pw, ph, 3f);
        GUI.color = Color.white;

        GUIStyle title = MakeStyle(18, FontStyle.Bold, new Color(1f, 0.5f, 0f), TextAnchor.MiddleCenter);
        GUI.Label(new Rect(px, py + 10, pw, 30), "DECK FULL — Remove a card to make room", title);

        var deck = ShopManager.Instance.GetDeck(_pendingBuyPlayerIndex);
        float rowH = 30f;
        for (int i = 0; i < deck.Count; i++)
        {
            CardData c = CardDatabase.GetCardById(deck[i]);
            if (c == null) continue;
            float ry = py + 50f + i * rowH;

            GUI.color = new Color(0.15f, 0.15f, 0.2f, 1f);
            GUI.DrawTexture(new Rect(px + 10, ry, pw - 20, rowH - 2), _whiteTex);
            GUI.color = Color.white;

            GUIStyle row = MakeStyle(13, FontStyle.Normal, Color.white, TextAnchor.MiddleLeft);
            GUI.Label(new Rect(px + 15, ry + 2, pw - 100, rowH - 4), $"{c.cardName}  ({GetTypeLabel(c.type)})  •  cost: {c.shopCost}", row);

            GUI.color = Color.red;
            if (GUI.Button(new Rect(px + pw - 85, ry + 2, 75f, rowH - 4), "REMOVE"))
            {
                ShopManager.Instance.RemoveCardFromDeck(_pendingBuyPlayerIndex, i);
                ShopManager.Instance.TryBuyCard(_pendingBuyPlayerIndex, _pendingBuyCardId);
                _showDeckManager = false;
                _pendingBuyCardId = -1;
            }
            GUI.color = Color.white;
        }

        GUI.color = Color.grey;
        if (GUI.Button(new Rect(px + pw / 2f - 60f, py + ph - 50f, 120f, 35f), "CANCEL"))
        {
            _showDeckManager = false;
            _pendingBuyCardId = -1;
        }
        GUI.color = Color.white;
    }

    // -----------------------------------------------------------------------
    // TRACK PICKER
    // -----------------------------------------------------------------------
    private void DrawTrackPicker()
    {
        // Semi-transparent overlay so the round state shows through (unblurred per spec)
        GUI.color = new Color(0, 0, 0, 0.72f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _whiteTex);
        GUI.color = Color.white;

        string pickerName = MatchManager.Instance != null
            ? MatchManager.Instance.GetPlayerName(_trackPickerIndex)
            : $"Player {_trackPickerIndex + 1}";

        GUIStyle titleStyle = MakeStyle(26, FontStyle.Bold, Color.cyan, TextAnchor.MiddleCenter);
        GUIStyle subStyle   = MakeStyle(16, FontStyle.Normal, Color.white, TextAnchor.MiddleCenter);

        GUI.Label(new Rect(0, 30, Screen.width, 40), "CHOOSE NEXT ROUND TYPE", titleStyle);
        GUI.Label(new Rect(0, 75, Screen.width, 28), $"{pickerName.ToUpper()} PICKS", subStyle);

        float cardW = 155f, cardH = 110f, gap = 18f;
        int totalCards = 2 + _customMapNames.Count;
        float totalW = totalCards * cardW + (totalCards - 1) * gap;
        float startX = Screen.width / 2f - totalW / 2f;
        float startY = 120f;

        GUIStyle btnStyle = MakeStyle(16, FontStyle.Bold, Color.black, TextAnchor.MiddleCenter);

        DrawCard(startX, startY, cardW, cardH, "SLOW RHYTHM", "Single beat\nrhythm combat", Color.cyan, "", () =>
        {
            if (NetworkClient.active)
                MatchManager.Instance?.SelectTrack(RoundType.SlowRhythm, "");
            _phase = UIPhase.Hidden;
        });

        DrawCard(startX + cardW + gap, startY, cardW, cardH, "FAST COMBO", "Cluster attack\nsequences", Color.magenta, "", () =>
        {
            if (NetworkClient.active)
                MatchManager.Instance?.SelectTrack(RoundType.FastCombo, "");
            _phase = UIPhase.Hidden;
        });

        for (int i = 0; i < _customMapNames.Count; i++)
        {
            float cx = startX + (i + 2) * (cardW + gap);
            string mapName = _customMapNames[i];
            DrawCard(cx, startY, cardW, cardH, mapName.ToUpper(), "Custom Map", Color.green, "", () =>
            {
                if (NetworkClient.active)
                    MatchManager.Instance?.SelectTrack(RoundType.CustomTrack, mapName);
                _phase = UIPhase.Hidden;
            });
        }
    }

    // -----------------------------------------------------------------------
    // MATCH RESULT
    // -----------------------------------------------------------------------
    private void DrawMatchResult()
    {
        GUI.color = new Color(0, 0, 0, 0.9f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _whiteTex);
        GUI.color = Color.white;

        GUIStyle winStyle  = MakeStyle(40, FontStyle.Bold, Color.yellow, TextAnchor.MiddleCenter);
        GUIStyle subStyle  = MakeStyle(22, FontStyle.Normal, Color.white, TextAnchor.MiddleCenter);
        GUIStyle scoreStyle= MakeStyle(28, FontStyle.Bold, Color.cyan, TextAnchor.MiddleCenter);

        string winnerName = _matchWinnerIndex >= 0 && MatchManager.Instance != null
            ? MatchManager.Instance.GetPlayerName(_matchWinnerIndex)
            : "DRAW";

        GUI.Label(new Rect(0, Screen.height / 2f - 120, Screen.width, 60), "MATCH OVER", winStyle);
        GUI.Label(new Rect(0, Screen.height / 2f - 50, Screen.width, 40), _matchWinnerIndex >= 0 ? $"{winnerName.ToUpper()} WINS!" : "IT'S A DRAW!", subStyle);
        GUI.Label(new Rect(0, Screen.height / 2f + 10, Screen.width, 40), $"{_finalW1}  —  {_finalW2}", scoreStyle);

        GUI.color = Color.cyan;
        if (GUI.Button(new Rect(Screen.width / 2f - 100f, Screen.height / 2f + 80f, 200f, 50f), "BACK TO MENU"))
        {
            if (NetworkServer.active) NetworkManager.singleton.StopHost();
            else NetworkManager.singleton.StopClient();
        }
        GUI.color = Color.white;
    }

    // -----------------------------------------------------------------------
    // Deck Previews
    // -----------------------------------------------------------------------
    private void DrawDeckPreview(int playerIndex, float panelX, float startY, float panelW)
    {
        var deck = ShopManager.Instance.GetDeck(playerIndex);
        GUIStyle style = MakeStyle(12, FontStyle.Normal, new Color(0.85f, 0.85f, 0.85f), TextAnchor.MiddleLeft);
        GUIStyle hdr   = MakeStyle(13, FontStyle.Bold, Color.yellow, TextAnchor.MiddleCenter);
        GUI.Label(new Rect(panelX, startY, panelW, 20), $"DECK ({deck.Count}/{ShopManager.DeckLimit})", hdr);
        for (int i = 0; i < deck.Count; i++)
        {
            CardData c = CardDatabase.GetCardById(deck[i]);
            if (c != null)
                GUI.Label(new Rect(panelX + 10, startY + 22 + i * 20, panelW - 15, 20), $"• {c.cardName}", style);
        }
    }

    private void DrawDeckPreviewCompact(int playerIndex, float x, float y, float w)
    {
        var deck = ShopManager.Instance.GetDeck(playerIndex);
        string name = MatchManager.Instance != null ? MatchManager.Instance.GetPlayerName(playerIndex) : $"P{playerIndex+1}";
        int traitId = ShopManager.Instance.GetTrait(playerIndex);

        GUI.color = new Color(0.08f, 0.08f, 0.14f, 0.95f);
        GUI.DrawTexture(new Rect(x, y, w, 170), _whiteTex);
        GUI.color = Color.white;

        GUIStyle hdr = MakeStyle(13, FontStyle.Bold, Color.yellow, TextAnchor.MiddleLeft);
        GUI.Label(new Rect(x + 8, y + 5, w, 22), $"{name}  Deck ({deck.Count}/{ShopManager.DeckLimit})", hdr);

        GUIStyle row = MakeStyle(11, FontStyle.Normal, Color.white, TextAnchor.MiddleLeft);
        int cols = 2, rows = Mathf.CeilToInt(deck.Count / (float)cols);
        float colW = (w - 16) / cols;
        for (int i = 0; i < deck.Count && i < 12; i++)
        {
            CardData c = CardDatabase.GetCardById(deck[i]);
            if (c == null) continue;
            int col = i % cols, rowIdx = i / cols;
            GUI.Label(new Rect(x + 8 + col * colW, y + 30 + rowIdx * 18, colW, 18), $"• {c.cardName}", row);
        }

        if (traitId >= 0)
        {
            TraitData t = CardDatabase.GetTraitById(traitId);
            GUIStyle traitStyle = MakeStyle(11, FontStyle.Italic, new Color(1f, 0.6f, 0f), TextAnchor.MiddleLeft);
            if (t != null) GUI.Label(new Rect(x + 8, y + 148, w - 16, 18), $"Trait: {t.traitName}", traitStyle);
        }
    }

    // -----------------------------------------------------------------------
    // Card Drawing Helper
    // -----------------------------------------------------------------------
    private void DrawCard(float x, float y, float w, float h, string title, string desc, Color accent, string costLabel, System.Action onClick)
    {
        // Background
        GUI.color = new Color(0.08f, 0.08f, 0.13f, 0.97f);
        GUI.DrawTexture(new Rect(x, y, w, h), _whiteTex);

        // Border
        DrawBorder(x, y, w, h, 4f, accent);

        GUIStyle titleStyle = MakeStyle(13, FontStyle.Bold, accent, TextAnchor.UpperCenter);
        GUIStyle descStyle  = MakeStyle(10, FontStyle.Normal, new Color(0.85f, 0.85f, 0.9f), TextAnchor.UpperCenter);
        descStyle.wordWrap  = true;
        GUIStyle costStyle  = MakeStyle(12, FontStyle.Bold, Color.yellow, TextAnchor.LowerRight);

        GUI.color = Color.white;
        GUI.Label(new Rect(x + 4, y + 6, w - 8, 22), title, titleStyle);
        GUI.Label(new Rect(x + 4, y + 30, w - 8, h - 50), desc, descStyle);

        if (!string.IsNullOrEmpty(costLabel))
            GUI.Label(new Rect(x + 4, y + h - 22, w - 8, 18), costLabel, costStyle);

        if (onClick != null)
        {
            GUI.color = new Color(1, 1, 1, 0.01f);
            if (GUI.Button(new Rect(x, y, w, h), ""))
                onClick.Invoke();
            GUI.color = Color.white;
        }
    }

    // -----------------------------------------------------------------------
    // Style & Drawing Utilities
    // -----------------------------------------------------------------------
    private GUIStyle MakeStyle(int fontSize, FontStyle fontStyle, Color color, TextAnchor alignment)
    {
        return new GUIStyle(GUI.skin.label)
        {
            fontSize  = fontSize,
            fontStyle = fontStyle,
            alignment = alignment,
            normal    = { textColor = color },
            wordWrap  = false
        };
    }

    private void DrawBorder(float x, float y, float w, float h, float thick, Color? color = null)
    {
        GUI.color = color ?? Color.white;
        GUI.DrawTexture(new Rect(x, y, w, thick), _whiteTex);
        GUI.DrawTexture(new Rect(x, y + h - thick, w, thick), _whiteTex);
        GUI.DrawTexture(new Rect(x, y, thick, h), _whiteTex);
        GUI.DrawTexture(new Rect(x + w - thick, y, thick, h), _whiteTex);
        GUI.color = Color.white;
    }

    private Color GetRarityColor(CardRarity rarity) => rarity switch
    {
        CardRarity.Basic     => Color.white,
        CardRarity.Advanced  => Color.yellow,
        CardRarity.Legendary => new Color(1f, 0.4f, 0f),
        _                    => Color.white
    };

    private string GetTypeLabel(CardType type) => type switch
    {
        CardType.Attack  => "ATK",
        CardType.Defense => "DEF",
        CardType.Meta    => "META",
        _                => "?"
    };
}
