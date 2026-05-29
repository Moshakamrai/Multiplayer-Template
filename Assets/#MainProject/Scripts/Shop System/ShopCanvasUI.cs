using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Programmatic Canvas shop — attach to any GameObject in the scene.
// When this component exists, the old ShopUI (IMGUI) automatically yields to it.
//
// SETUP:
//  1. Add this component to a GameObject (e.g. ShopCanvas).
//  2. Add CardArtLibrary to the same or any other persistent GameObject and wire up your PNGs.
//  3. The old ShopUI component can stay — it will do nothing while this is active.
public class ShopCanvasUI : MonoBehaviour
{
    public static ShopCanvasUI Instance { get; private set; }

    // ── colours (matching the cyberpunk theme) ─────────────────────────────
    static readonly Color BG_DARK       = new Color(0.01f, 0.02f, 0.06f, 1f);
    static readonly Color PANEL_BG      = new Color(0.04f, 0.05f, 0.10f, 0.95f);
    static readonly Color NEON_CYAN     = new Color(0f,    1f,    0.8f,  1f);
    static readonly Color NEON_ORANGE   = new Color(1f,    0.5f,  0.15f, 1f);
    static readonly Color NEON_GREEN    = new Color(0.25f, 0.9f,  0.4f,  1f);
    static readonly Color NEON_YELLOW   = new Color(1f,    0.95f, 0.1f,  1f);
    static readonly Color NEON_MAGENTA  = new Color(1f,    0.2f,  0.8f,  1f);
    static readonly Color CARD_OVERLAY  = new Color(0f,    0f,    0f,    0.52f);
    static readonly Color BTN_BUY       = new Color(0.1f,  0.6f,  0.25f, 1f);
    static readonly Color BTN_FULL      = new Color(0.35f, 0.35f, 0.35f, 1f);
    static readonly Color BTN_REMOVE    = new Color(0.7f,  0.15f, 0.15f, 1f);

    // ── runtime refs ───────────────────────────────────────────────────────
    Canvas      _canvas;
    GameObject  _root;
    Text        _timerText, _creditsText, _roundText;
    Transform   _deckContent, _oppContent;
    Text        _deckCapacityText;

    readonly List<CombatSlot> _combatSlots = new List<CombatSlot>();
    readonly List<TraitSlot>  _traitSlots  = new List<TraitSlot>();

    bool _wasShopActive;

    // ── card slot data ─────────────────────────────────────────────────────
    struct CombatSlot
    {
        public GameObject root;
        public Image      art;
        public Text       nameText, descText, costText;
        public Button     buyBtn;
        public Text       buyBtnText;
        public GameObject soldOut;
    }

    struct TraitSlot
    {
        public GameObject root;
        public Image      art;
        public Text       nameText, effectText, costText;
        public Button     buyBtn;
        public Text       buyBtnText;
        public GameObject soldOut;
    }

    // ──────────────────────────────────────────────────────────────────────
    void Awake()
    {
        Instance = this;
        BuildCanvas();
        _root.SetActive(false);
    }

    void Update()
    {
        var spm = ShopPhaseManager.Instance;
        // Force-hide if a round is actively running, even if isShopPhase is stuck
        bool roundRunning = RhythmRoundManager.Instance != null && RhythmRoundManager.Instance.isRoundActive;
        bool active = !roundRunning && spm != null && spm.isShopPhase;

        if (active && !_wasShopActive) OpenShop();
        if (!active && _wasShopActive) _root.SetActive(false);
        _wasShopActive = active;

        if (active) TickHeader(spm);
    }

    void OpenShop()
    {
        _root.SetActive(true);
        PopulateCombatSlots();
        PopulateTraitSlots();
        RefreshInventoryPanels();
        // Force layout rebuild so ContentSizeFitter sizes the grid content correctly
        Canvas.ForceUpdateCanvases();
    }

    // ── HEADER TICK ────────────────────────────────────────────────────────
    void TickHeader(ShopPhaseManager spm)
    {
        float t = spm.shopTimeRemaining;
        _timerText.text = $"⏱  {Mathf.Max(0f, t):F0}s";
        _timerText.color = t <= 15f ? new Color(1f, 0.25f, 0.25f) : NEON_CYAN;

        var inv = GameManager.localPlayer?.GetComponent<PlayerInventory>();
        _creditsText.text = inv != null ? $"💰  {inv.credits} CR" : "💰  --";
        _roundText.text   = $"ROUND  {spm.currentShopRound}";

        // Refresh capacity label and buy buttons reactively
        bool full = inv != null && inv.ownedCombatCards.Count >= PlayerInventory.MaxCombatCards;
        if (_deckCapacityText != null)
        {
            int cnt = inv?.ownedCombatCards.Count ?? 0;
            _deckCapacityText.text  = full ? $"DECK  {cnt}/8  ─  FULL" : $"DECK  {cnt}/8";
            _deckCapacityText.color = full ? NEON_ORANGE : NEON_CYAN;
        }
        foreach (var s in _combatSlots)
            UpdateCombatBuyState(s, inv);

        RefreshInventoryPanels();
    }

    // ── SLOT POPULATION ────────────────────────────────────────────────────
    void PopulateCombatSlots()
    {
        var spm = ShopPhaseManager.Instance;
        var inv = GameManager.localPlayer?.GetComponent<PlayerInventory>();
        for (int i = 0; i < _combatSlots.Count; i++)
        {
            var slot = _combatSlots[i];
            bool hasSpm   = spm != null;
            bool hasCard  = hasSpm && i < spm.CombatShopSlots.Count && spm.CombatShopSlots[i] != null;
            bool purchased = hasSpm && spm.IsCombatSlotPurchased(i);

            if (!hasCard || purchased)
            {
                slot.soldOut.SetActive(true);
                slot.art.gameObject.SetActive(false);
                continue;
            }

            var card = spm.CombatShopSlots[i];
            slot.soldOut.SetActive(false);
            slot.art.gameObject.SetActive(true);

            // Art
            var sprite = CardArtLibrary.Instance?.GetSprite(card.cardId);
            slot.art.sprite        = sprite;
            slot.art.color         = sprite != null ? Color.white : new Color(0.3f, 0.3f, 0.4f);
            slot.art.preserveAspect = false;

            // Text
            slot.nameText.text = card.displayName.ToUpper();
            slot.descText.text = card.description;
            slot.costText.text = $"{card.cost} CR";

            // Rarity border colour
            Color border = card.rarity == CardRarity.Legendary ? new Color(1f, 0.75f, 0.1f) :
                           card.rarity == CardRarity.Advanced   ? new Color(0.3f, 0.6f, 1f)  :
                           new Color(0.6f, 0.6f, 0.65f);
            SetBorderColor(slot.root, border);

            // Buy button (capacity guard applied in TickHeader too)
            int idx = i;
            slot.buyBtn.onClick.RemoveAllListeners();
            slot.buyBtn.onClick.AddListener(() =>
            {
                var pc = GameManager.localPlayer?.GetComponent<PlayerCombat>();
                if (pc != null) pc.CmdBuyCombatCard(idx);
                ShopPhaseManager.Instance?.MarkCombatSlotPurchased(idx);
                var s2 = _combatSlots[idx];
                s2.soldOut.SetActive(true);
                s2.art.gameObject.SetActive(false);
                _combatSlots[idx] = s2;
                RefreshInventoryPanels();
            });

            UpdateCombatBuyState(slot, inv);
            _combatSlots[i] = slot;
        }
    }

    void UpdateCombatBuyState(CombatSlot slot, PlayerInventory inv)
    {
        if (slot.buyBtn == null) return;
        bool full    = inv != null && inv.ownedCombatCards.Count >= PlayerInventory.MaxCombatCards;
        bool soldOut = slot.soldOut != null && slot.soldOut.activeSelf;
        if (soldOut) return;

        var img = slot.buyBtn.GetComponent<Image>();
        if (full)
        {
            slot.buyBtnText.text = "FULL";
            if (img != null) img.color = BTN_FULL;
            slot.buyBtn.interactable = false;
        }
        else
        {
            slot.buyBtnText.text = "BUY";
            if (img != null) img.color = BTN_BUY;
            slot.buyBtn.interactable = true;
        }
    }

    void PopulateTraitSlots()
    {
        var spm = ShopPhaseManager.Instance;
        var inv = GameManager.localPlayer?.GetComponent<PlayerInventory>();
        for (int i = 0; i < _traitSlots.Count; i++)
        {
            var slot = _traitSlots[i];
            bool hasCard  = spm != null && i < spm.TraitShopSlots.Count && spm.TraitShopSlots[i] != null;
            bool purchased = spm != null && spm.IsTraitSlotPurchased(i);

            if (!hasCard || purchased)
            {
                slot.soldOut.SetActive(true);
                slot.art.gameObject.SetActive(false);
                continue;
            }

            var card = spm.TraitShopSlots[i];
            bool equipped = inv != null && inv.equippedTraitId == card.traitId;

            slot.soldOut.SetActive(false);
            slot.art.gameObject.SetActive(true);

            var sprite = CardArtLibrary.Instance?.GetSprite(card.traitId);
            slot.art.sprite         = sprite;
            slot.art.color          = sprite != null ? Color.white : new Color(0.1f, 0.3f, 0.15f);
            slot.art.preserveAspect = false;

            slot.nameText.text   = card.displayName.ToUpper();
            slot.effectText.text = card.effect;
            slot.costText.text   = $"{card.cost} CR";

            SetBorderColor(slot.root, NEON_GREEN);

            int idx = i;
            slot.buyBtn.onClick.RemoveAllListeners();
            slot.buyBtn.onClick.AddListener(() =>
            {
                var pc = GameManager.localPlayer?.GetComponent<PlayerCombat>();
                if (pc != null) pc.CmdBuyTraitCard(idx);
                ShopPhaseManager.Instance?.MarkTraitSlotPurchased(idx);
                PopulateTraitSlots();
                RefreshInventoryPanels();
            });

            var btnImg = slot.buyBtn.GetComponent<Image>();
            if (equipped)
            {
                slot.buyBtnText.text = "EQUIPPED";
                if (btnImg != null) btnImg.color = NEON_YELLOW;
                slot.buyBtn.interactable = false;
            }
            else
            {
                slot.buyBtnText.text = "BUY";
                if (btnImg != null) btnImg.color = BTN_BUY;
                slot.buyBtn.interactable = true;
            }

            _traitSlots[i] = slot;
        }
    }

    // ── INVENTORY PANELS ───────────────────────────────────────────────────
    void RefreshInventoryPanels()
    {
        if (_deckContent == null || _oppContent == null) return;

        var inv    = GameManager.localPlayer?.GetComponent<PlayerInventory>();
        var botInv = GetBotInventory();

        RebuildDeckPanel(_deckContent, inv, isLocal: true);
        RebuildDeckPanel(_oppContent,  botInv, isLocal: false);
    }

    void RebuildDeckPanel(Transform content, PlayerInventory inv, bool isLocal)
    {
        foreach (Transform child in content) Destroy(child.gameObject);

        if (inv == null) return;

        // Combat cards
        foreach (var id in inv.ownedCombatCards)
            AddInventoryRow(content, id, "Combat", isLocal);

        // Trait
        if (!string.IsNullOrEmpty(inv.equippedTraitId))
        {
            AddInlineLabel(content, "TRAIT  (PASSIVE)", NEON_GREEN);
            AddInventoryRow(content, inv.equippedTraitId, "Trait", false);
        }
    }

    void AddInlineLabel(Transform parent, string label, Color color)
    {
        var go = new GameObject("InlineLabel", typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(0, 20);
        var t = go.GetComponent<Text>();
        t.text      = label;
        t.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize  = 11;
        t.fontStyle = FontStyle.Bold;
        t.color     = color;
        t.alignment = TextAnchor.MiddleLeft;
    }

    void AddInventoryRow(Transform parent, string cardId, string category, bool canRemove)
    {
        string name = cardId, desc = "";
        Sprite art  = null;
        Color  col  = new Color(0.5f, 0.5f, 0.5f);

        if (category == "Combat")
        {
            var found = System.Array.Find(
                System.Linq.Enumerable.ToArray(
                    System.Linq.Enumerable.Concat(
                        System.Linq.Enumerable.Concat(CardDatabase.BasicCards, CardDatabase.AdvancedCards),
                        CardDatabase.LegendaryCards)),
                c => c.cardId == cardId);
            if (found != null)
            {
                name = found.displayName;
                desc = found.description;
                col  = found.rarity == CardRarity.Legendary ? new Color(1f, 0.75f, 0.1f) :
                       found.rarity == CardRarity.Advanced   ? new Color(0.3f, 0.6f, 1f)  :
                       new Color(0.6f, 0.6f, 0.65f);
            }
            art = CardArtLibrary.Instance?.GetSprite(cardId);
        }
        else if (category == "Trait")
        {
            var found = System.Array.Find(CardDatabase.TraitCards, t => t.traitId == cardId);
            if (found != null) { name = found.displayName; desc = found.effect; }
            art = CardArtLibrary.Instance?.GetSprite(cardId);
            col = NEON_GREEN;
        }

        // Row root
        var row = new GameObject($"Row_{cardId}", typeof(RectTransform), typeof(Image));
        row.transform.SetParent(parent, false);
        var rowRt = row.GetComponent<RectTransform>();
        rowRt.sizeDelta = new Vector2(0, 52);
        row.GetComponent<Image>().color = new Color(col.r * 0.15f, col.g * 0.15f, col.b * 0.15f, 0.9f);

        // Rarity stripe on left
        var stripe = new GameObject("Stripe", typeof(RectTransform), typeof(Image));
        stripe.transform.SetParent(row.transform, false);
        var stripeRt = stripe.GetComponent<RectTransform>();
        stripeRt.anchorMin = Vector2.zero; stripeRt.anchorMax = new Vector2(0, 1);
        stripeRt.sizeDelta = new Vector2(3, 0); stripeRt.anchoredPosition = Vector2.zero;
        stripe.GetComponent<Image>().color = col;

        // Thumbnail
        float thumbW = 44f;
        var thumb = new GameObject("Thumb", typeof(RectTransform), typeof(Image));
        thumb.transform.SetParent(row.transform, false);
        var thumbRt = thumb.GetComponent<RectTransform>();
        thumbRt.anchorMin = new Vector2(0, 0); thumbRt.anchorMax = new Vector2(0, 1);
        thumbRt.offsetMin = new Vector2(6, 4); thumbRt.offsetMax = new Vector2(6 + thumbW, -4);
        var thumbImg = thumb.GetComponent<Image>();
        thumbImg.sprite         = art;
        thumbImg.color          = art != null ? Color.white : new Color(col.r * 0.4f, col.g * 0.4f, col.b * 0.4f);
        thumbImg.preserveAspect = true;

        // Name text
        float textX = 6 + thumbW + 6;
        float removeW = canRemove ? 52f : 0f;
        var nameGo = new GameObject("Name", typeof(RectTransform), typeof(Text));
        nameGo.transform.SetParent(row.transform, false);
        var nameRt = nameGo.GetComponent<RectTransform>();
        nameRt.anchorMin = new Vector2(0, 0.5f); nameRt.anchorMax = new Vector2(1, 1);
        nameRt.offsetMin = new Vector2(textX, 0); nameRt.offsetMax = new Vector2(-removeW - 4, 0);
        var nameT = nameGo.GetComponent<Text>();
        nameT.text      = name.ToUpper();
        nameT.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        nameT.fontSize  = 12;
        nameT.fontStyle = FontStyle.Bold;
        nameT.color     = Color.white;
        nameT.alignment = TextAnchor.LowerLeft;

        // Description text
        var descGo = new GameObject("Desc", typeof(RectTransform), typeof(Text));
        descGo.transform.SetParent(row.transform, false);
        var descRt = descGo.GetComponent<RectTransform>();
        descRt.anchorMin = new Vector2(0, 0); descRt.anchorMax = new Vector2(1, 0.5f);
        descRt.offsetMin = new Vector2(textX, 2); descRt.offsetMax = new Vector2(-removeW - 4, 0);
        var descT = descGo.GetComponent<Text>();
        descT.text      = desc;
        descT.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        descT.fontSize  = 9;
        descT.color     = new Color(0.75f, 0.75f, 0.75f);
        descT.alignment = TextAnchor.UpperLeft;

        // Remove button
        if (canRemove)
        {
            var btnGo = new GameObject("RemoveBtn", typeof(RectTransform), typeof(Image), typeof(Button));
            btnGo.transform.SetParent(row.transform, false);
            var btnRt = btnGo.GetComponent<RectTransform>();
            btnRt.anchorMin = new Vector2(1, 0); btnRt.anchorMax = new Vector2(1, 1);
            btnRt.sizeDelta = new Vector2(48, 0);
            btnRt.anchoredPosition = new Vector2(-26, 0);
            btnGo.GetComponent<Image>().color = BTN_REMOVE;
            var btnLbl = CreateChildText(btnGo.transform, "×", 14, Color.white, TextAnchor.MiddleCenter);
            SetStretch(btnLbl.gameObject);
            string capturedId = cardId;
            btnGo.GetComponent<Button>().onClick.AddListener(() =>
            {
                var pc = GameManager.localPlayer?.GetComponent<PlayerCombat>();
                if (pc != null) pc.CmdRemoveCard(capturedId);
                RefreshInventoryPanels();
            });
        }
    }

    // ── CANVAS BUILDER ─────────────────────────────────────────────────────
    void BuildCanvas()
    {
        // Canvas
        var canvasGo = new GameObject("ShopCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        _canvas = canvasGo.GetComponent<Canvas>();
        _canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 20;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight  = 0.5f;

        // Root (full-screen background)
        _root = CreatePanel(canvasGo.transform, "Root", BG_DARK, stretch: true);

        BuildHeader(_root.transform);
        var contentArea = BuildContentArea(_root.transform);
        BuildLeftPanel(contentArea);
        BuildCenterPanel(contentArea);
        BuildRightPanel(contentArea);
        BuildFooter(_root.transform);
    }

    // ── HEADER ─────────────────────────────────────────────────────────────
    void BuildHeader(Transform parent)
    {
        var header = CreatePanel(parent, "Header", new Color(0, 0.08f, 0.12f, 1f));
        var rt = header.GetComponent<RectTransform>();
        rt.anchorMin  = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1);
        rt.sizeDelta  = new Vector2(0, 90);
        rt.anchoredPosition = new Vector2(0, -45);

        // Bottom glow line
        AddHLine(header.transform, 0, 2, NEON_CYAN, 0.5f);

        // Title
        var title = CreateChildText(header.transform, "⚡  POST-ROUND SHOP  ⚡", 28, NEON_CYAN, TextAnchor.MiddleCenter);
        var tRt = title.GetComponent<RectTransform>();
        tRt.anchorMin = new Vector2(0.2f, 0.5f); tRt.anchorMax = new Vector2(0.8f, 1f);
        tRt.sizeDelta = Vector2.zero;
        title.fontStyle = FontStyle.Bold;

        // Timer (left third)
        _timerText = CreateChildText(header.transform, "⏱  120s", 20, NEON_CYAN, TextAnchor.MiddleLeft);
        SetAnchoredRect(_timerText.gameObject, 0f, 0f, 0.33f, 1f, 16, 0, -8, 0);
        _timerText.fontStyle = FontStyle.Bold;

        // Credits (center)
        _creditsText = CreateChildText(header.transform, "💰  --", 20, NEON_YELLOW, TextAnchor.MiddleCenter);
        SetAnchoredRect(_creditsText.gameObject, 0.33f, 0f, 0.66f, 1f, 0, 0, 0, 0);
        _creditsText.fontStyle = FontStyle.Bold;

        // Round (right third)
        _roundText = CreateChildText(header.transform, "ROUND  1", 18, NEON_MAGENTA, TextAnchor.MiddleRight);
        SetAnchoredRect(_roundText.gameObject, 0.66f, 0f, 1f, 1f, 0, 0, -16, 0);
        _roundText.fontStyle = FontStyle.Bold;
    }

    // ── CONTENT AREA ───────────────────────────────────────────────────────
    GameObject BuildContentArea(Transform parent)
    {
        var go = new GameObject("ContentArea", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(1, 1);
        rt.offsetMin = new Vector2(8,  80);
        rt.offsetMax = new Vector2(-8, -92);
        var hlg = go.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing             = 8;
        hlg.childControlWidth   = true;
        hlg.childControlHeight  = true;
        hlg.childForceExpandWidth  = true;
        hlg.childForceExpandHeight = true;
        hlg.padding = new RectOffset(0, 0, 0, 0);
        return go;
    }

    // ── LEFT PANEL (your deck) ─────────────────────────────────────────────
    void BuildLeftPanel(GameObject parent)
    {
        var panel = CreateBorderedPanel(parent.transform, "YourDeck", NEON_CYAN, 0.20f);

        AddSectionLabel(panel.transform, "YOUR DECK", NEON_CYAN);
        _deckCapacityText = CreateChildText(panel.transform, "DECK  0/8", 11, NEON_CYAN, TextAnchor.UpperLeft);
        var capRt = _deckCapacityText.GetComponent<RectTransform>();
        capRt.anchorMin = Vector2.zero; capRt.anchorMax = new Vector2(1, 0);
        capRt.sizeDelta  = new Vector2(0, 18);
        capRt.anchoredPosition = new Vector2(0, 22);

        var scroll = BuildScrollView(panel.transform);
        _deckContent = scroll.content;
    }

    // ── CENTER PANEL (shop cards) ──────────────────────────────────────────
    void BuildCenterPanel(GameObject parent)
    {
        var outer = new GameObject("Center", typeof(RectTransform), typeof(Image));
        outer.transform.SetParent(parent.transform, false);
        outer.GetComponent<Image>().color = Color.clear;
        var le = outer.AddComponent<LayoutElement>();
        le.flexibleWidth = 0.55f;

        var hlg = outer.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 8;
        hlg.childControlWidth   = true;
        hlg.childControlHeight  = true;
        hlg.childForceExpandWidth  = true;
        hlg.childForceExpandHeight = true;

        BuildCombatShopPanel(outer.transform);
        BuildTraitShopPanel(outer.transform);
    }

    void BuildCombatShopPanel(Transform parent)
    {
        var panel = CreateBorderedPanel(parent, "CombatShop", NEON_ORANGE, 0.5f);
        AddSectionLabel(panel.transform, "COMBAT  CARDS  (8)", NEON_ORANGE);

        var scroll = BuildScrollView(panel.transform);
        var grid   = scroll.content.gameObject;

        // BuildScrollView adds VLG + CSF; replace with GridLayoutGroup for card grid
        DestroyImmediate(grid.GetComponent<VerticalLayoutGroup>());
        DestroyImmediate(grid.GetComponent<ContentSizeFitter>());

        var glg = grid.AddComponent<GridLayoutGroup>();
        glg.cellSize        = new Vector2(200, 260);
        glg.spacing         = new Vector2(8, 8);
        glg.constraint      = GridLayoutGroup.Constraint.FixedColumnCount;
        glg.constraintCount = 2;
        glg.startCorner     = GridLayoutGroup.Corner.UpperLeft;
        glg.startAxis       = GridLayoutGroup.Axis.Horizontal;
        glg.padding         = new RectOffset(6, 6, 6, 6);
        var csf = grid.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        _combatSlots.Clear();
        for (int i = 0; i < 8; i++)
            _combatSlots.Add(BuildCombatCardSlot(scroll.content));
    }

    void BuildTraitShopPanel(Transform parent)
    {
        var panel = CreateBorderedPanel(parent, "TraitShop", NEON_GREEN, 0.5f);
        AddSectionLabel(panel.transform, "TRAITS  —  PASSIVE  (4)", NEON_GREEN);

        var scroll = BuildScrollView(panel.transform);
        var grid   = scroll.content.gameObject;

        // BuildScrollView adds VLG + CSF; replace with GridLayoutGroup for card grid
        DestroyImmediate(grid.GetComponent<VerticalLayoutGroup>());
        DestroyImmediate(grid.GetComponent<ContentSizeFitter>());

        var glg = grid.AddComponent<GridLayoutGroup>();
        glg.cellSize        = new Vector2(200, 260);
        glg.spacing         = new Vector2(8, 8);
        glg.constraint      = GridLayoutGroup.Constraint.FixedColumnCount;
        glg.constraintCount = 2;
        glg.startCorner     = GridLayoutGroup.Corner.UpperLeft;
        glg.startAxis       = GridLayoutGroup.Axis.Horizontal;
        glg.padding         = new RectOffset(6, 6, 6, 6);
        var csf = grid.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        _traitSlots.Clear();
        for (int i = 0; i < 4; i++)
            _traitSlots.Add(BuildTraitCardSlot(scroll.content));
    }

    // ── RIGHT PANEL (opponent deck) ────────────────────────────────────────
    void BuildRightPanel(GameObject parent)
    {
        var panel = CreateBorderedPanel(parent.transform, "OppDeck", NEON_MAGENTA, 0.20f);
        AddSectionLabel(panel.transform, "OPPONENT  DECK", NEON_MAGENTA);
        var scroll = BuildScrollView(panel.transform);
        _oppContent = scroll.content;
    }

    // ── FOOTER ─────────────────────────────────────────────────────────────
    void BuildFooter(Transform parent)
    {
        var footer = CreatePanel(parent, "Footer", new Color(0, 0.05f, 0.08f, 1f));
        var rt = footer.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(1, 0);
        rt.sizeDelta = new Vector2(0, 78);
        rt.anchoredPosition = new Vector2(0, 39);

        AddHLine(footer.transform, 1, 2, NEON_CYAN, 0.5f);

        // Lock In button
        var btnGo = new GameObject("LockInBtn", typeof(RectTransform), typeof(Image), typeof(Button));
        btnGo.transform.SetParent(footer.transform, false);
        var btnRt = btnGo.GetComponent<RectTransform>();
        btnRt.anchorMin = new Vector2(0.35f, 0.15f);
        btnRt.anchorMax = new Vector2(0.65f, 0.85f);
        btnRt.sizeDelta = Vector2.zero;
        btnGo.GetComponent<Image>().color = new Color(0f, 0.7f, 0.45f, 1f);

        var lbl = CreateChildText(btnGo.transform, "⚡  LOCK IN  &  FIGHT  ⚡", 20, Color.white, TextAnchor.MiddleCenter);
        SetStretch(lbl.gameObject);
        lbl.fontStyle = FontStyle.Bold;

        btnGo.GetComponent<Button>().onClick.AddListener(() =>
        {
            var pc = GameManager.localPlayer?.GetComponent<PlayerCombat>();
            if (pc != null) pc.CmdLockInPostRoundShop();
        });
    }

    // ── CARD SLOT BUILDERS ─────────────────────────────────────────────────
    CombatSlot BuildCombatCardSlot(Transform parent)
    {
        var slot = new CombatSlot();

        // Root card panel
        slot.root = new GameObject("CombatSlot", typeof(RectTransform), typeof(Image));
        slot.root.transform.SetParent(parent, false);
        slot.root.GetComponent<Image>().color = PANEL_BG;

        // Full-bleed art image
        var artGo = new GameObject("Art", typeof(RectTransform), typeof(Image));
        artGo.transform.SetParent(slot.root.transform, false);
        SetStretch(artGo);
        slot.art = artGo.GetComponent<Image>();
        slot.art.preserveAspect = false;

        // Dark gradient overlay (bottom 45%) for text readability
        var overlay = CreatePanel(slot.root.transform, "Overlay", CARD_OVERLAY);
        var ovRt = overlay.GetComponent<RectTransform>();
        ovRt.anchorMin = Vector2.zero; ovRt.anchorMax = new Vector2(1, 0.45f);
        ovRt.sizeDelta = Vector2.zero;

        // Card name (bottom area)
        slot.nameText = CreateChildText(slot.root.transform, "CARD", 13, Color.white, TextAnchor.LowerLeft);
        SetAnchoredRect(slot.nameText.gameObject, 0, 0.18f, 1, 0.38f, 6, 0, -6, 0);
        slot.nameText.fontStyle = FontStyle.Bold;

        // Description (small, wraps)
        slot.descText = CreateChildText(slot.root.transform, "", 9, new Color(0.85f, 0.85f, 0.85f), TextAnchor.UpperLeft);
        SetAnchoredRect(slot.descText.gameObject, 0, 0.38f, 1, 0.55f, 6, 0, -6, 0);

        // Cost badge (bottom-left)
        slot.costText = CreateChildText(slot.root.transform, "1 CR", 12, NEON_YELLOW, TextAnchor.LowerLeft);
        SetAnchoredRect(slot.costText.gameObject, 0, 0, 0.5f, 0.18f, 6, 2, 0, 0);
        slot.costText.fontStyle = FontStyle.Bold;

        // Buy button (bottom-right)
        var btnGo = new GameObject("BuyBtn", typeof(RectTransform), typeof(Image), typeof(Button));
        btnGo.transform.SetParent(slot.root.transform, false);
        var btnRt = btnGo.GetComponent<RectTransform>();
        btnRt.anchorMin = new Vector2(0.48f, 0f); btnRt.anchorMax = new Vector2(1f, 0.18f);
        btnRt.offsetMin = new Vector2(0, 3); btnRt.offsetMax = new Vector2(-4, -3);
        btnGo.GetComponent<Image>().color = BTN_BUY;
        slot.buyBtn     = btnGo.GetComponent<Button>();
        slot.buyBtnText = CreateChildText(btnGo.transform, "BUY", 12, Color.white, TextAnchor.MiddleCenter);
        SetStretch(slot.buyBtnText.gameObject);
        slot.buyBtnText.fontStyle = FontStyle.Bold;

        // Sold-out overlay
        slot.soldOut = CreatePanel(slot.root.transform, "SoldOut", new Color(0.06f, 0.06f, 0.10f, 0.96f), stretch: true);
        var soText = CreateChildText(slot.soldOut.transform, "SOLD  OUT", 14, new Color(0.55f, 0.55f, 0.65f), TextAnchor.MiddleCenter);
        SetStretch(soText.gameObject);
        soText.fontStyle = FontStyle.Bold;
        // Top accent line so empty slots are visible against dark background
        var soLine = new GameObject("Line", typeof(RectTransform), typeof(Image));
        soLine.transform.SetParent(slot.soldOut.transform, false);
        var soLRt = soLine.GetComponent<RectTransform>();
        soLRt.anchorMin = new Vector2(0,1); soLRt.anchorMax = new Vector2(1,1);
        soLRt.sizeDelta = new Vector2(0, 2);
        soLine.GetComponent<Image>().color = new Color(0.35f, 0.35f, 0.45f, 1f);
        slot.soldOut.SetActive(true);

        return slot;
    }

    TraitSlot BuildTraitCardSlot(Transform parent)
    {
        var slot = new TraitSlot();

        slot.root = new GameObject("TraitSlot", typeof(RectTransform), typeof(Image));
        slot.root.transform.SetParent(parent, false);
        slot.root.GetComponent<Image>().color = new Color(0.03f, 0.08f, 0.05f, 0.95f);

        // Art
        var artGo = new GameObject("Art", typeof(RectTransform), typeof(Image));
        artGo.transform.SetParent(slot.root.transform, false);
        SetStretch(artGo);
        slot.art = artGo.GetComponent<Image>();
        slot.art.color = new Color(0.1f, 0.3f, 0.15f);

        // Overlay
        var overlay = CreatePanel(slot.root.transform, "Overlay", CARD_OVERLAY);
        var ovRt = overlay.GetComponent<RectTransform>();
        ovRt.anchorMin = Vector2.zero; ovRt.anchorMax = new Vector2(1, 0.52f);
        ovRt.sizeDelta = Vector2.zero;

        // PASSIVE badge (top-right)
        var badge = CreatePanel(slot.root.transform, "Badge", new Color(0.05f, 0.25f, 0.1f, 0.9f));
        var badgeRt = badge.GetComponent<RectTransform>();
        badgeRt.anchorMin = new Vector2(1, 1); badgeRt.anchorMax = new Vector2(1, 1);
        badgeRt.sizeDelta = new Vector2(72, 20);
        badgeRt.anchoredPosition = new Vector2(-38, -12);
        var badgeTxt = CreateChildText(badge.transform, "PASSIVE", 9, NEON_GREEN, TextAnchor.MiddleCenter);
        SetStretch(badgeTxt.gameObject);
        badgeTxt.fontStyle = FontStyle.Bold;

        // Name
        slot.nameText = CreateChildText(slot.root.transform, "TRAIT", 13, NEON_GREEN, TextAnchor.LowerLeft);
        SetAnchoredRect(slot.nameText.gameObject, 0, 0.28f, 1, 0.44f, 6, 0, -6, 0);
        slot.nameText.fontStyle = FontStyle.Bold;

        // Effect
        slot.effectText = CreateChildText(slot.root.transform, "", 9, new Color(0.8f, 0.95f, 0.8f), TextAnchor.UpperLeft);
        SetAnchoredRect(slot.effectText.gameObject, 0, 0.44f, 1, 0.62f, 6, 0, -6, 0);

        // Cost
        slot.costText = CreateChildText(slot.root.transform, "3 CR", 12, NEON_YELLOW, TextAnchor.LowerLeft);
        SetAnchoredRect(slot.costText.gameObject, 0, 0, 0.5f, 0.18f, 6, 2, 0, 0);
        slot.costText.fontStyle = FontStyle.Bold;

        // Buy button
        var btnGo = new GameObject("BuyBtn", typeof(RectTransform), typeof(Image), typeof(Button));
        btnGo.transform.SetParent(slot.root.transform, false);
        var btnRt = btnGo.GetComponent<RectTransform>();
        btnRt.anchorMin = new Vector2(0.48f, 0f); btnRt.anchorMax = new Vector2(1f, 0.18f);
        btnRt.offsetMin = new Vector2(0, 3); btnRt.offsetMax = new Vector2(-4, -3);
        btnGo.GetComponent<Image>().color = BTN_BUY;
        slot.buyBtn     = btnGo.GetComponent<Button>();
        slot.buyBtnText = CreateChildText(btnGo.transform, "BUY", 12, Color.white, TextAnchor.MiddleCenter);
        SetStretch(slot.buyBtnText.gameObject);
        slot.buyBtnText.fontStyle = FontStyle.Bold;

        // Sold-out
        slot.soldOut = CreatePanel(slot.root.transform, "SoldOut", new Color(0.04f, 0.08f, 0.06f, 0.96f), stretch: true);
        var soText = CreateChildText(slot.soldOut.transform, "SOLD  OUT", 14, new Color(0.35f, 0.60f, 0.40f), TextAnchor.MiddleCenter);
        SetStretch(soText.gameObject);
        soText.fontStyle = FontStyle.Bold;
        var soLine2 = new GameObject("Line", typeof(RectTransform), typeof(Image));
        soLine2.transform.SetParent(slot.soldOut.transform, false);
        var soL2Rt = soLine2.GetComponent<RectTransform>();
        soL2Rt.anchorMin = new Vector2(0,1); soL2Rt.anchorMax = new Vector2(1,1);
        soL2Rt.sizeDelta = new Vector2(0, 2);
        soLine2.GetComponent<Image>().color = new Color(0.2f, 0.5f, 0.3f, 1f);
        slot.soldOut.SetActive(true);

        return slot;
    }

    // ── HELPERS ────────────────────────────────────────────────────────────
    static (ScrollRect scroll, Transform content) BuildScrollView(Transform parent)
    {
        var svGo = new GameObject("ScrollView", typeof(RectTransform), typeof(ScrollRect), typeof(Image));
        svGo.transform.SetParent(parent, false);
        SetStretch(svGo);
        svGo.GetComponent<Image>().color = Color.clear;
        var sr = svGo.GetComponent<ScrollRect>();
        sr.horizontal = false;

        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
        viewport.transform.SetParent(svGo.transform, false);
        SetStretch(viewport);
        viewport.GetComponent<Image>().color = Color.clear;
        viewport.GetComponent<Mask>().showMaskGraphic = false;
        sr.viewport = viewport.GetComponent<RectTransform>();

        var content = new GameObject("Content", typeof(RectTransform));
        content.transform.SetParent(viewport.transform, false);
        var cRt = content.GetComponent<RectTransform>();
        cRt.anchorMin = new Vector2(0, 1); cRt.anchorMax = new Vector2(1, 1);
        cRt.pivot     = new Vector2(0.5f, 1);
        cRt.sizeDelta = new Vector2(0, 0);
        sr.content = cRt;

        var vlg = content.AddComponent<VerticalLayoutGroup>();
        vlg.spacing    = 4;
        vlg.childControlWidth  = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;
        vlg.padding = new RectOffset(4, 4, 4, 4);

        var csf = content.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        return (sr, cRt);
    }

    static GameObject CreateBorderedPanel(Transform parent, string name, Color accent, float flex)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = PANEL_BG;
        var le = go.AddComponent<LayoutElement>();
        le.flexibleWidth = flex;

        AddBorder(go.transform, accent);

        var vlg = go.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(6, 6, 32, 6);
        vlg.spacing = 6;
        vlg.childControlWidth  = true;
        vlg.childControlHeight = false;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = true;

        return go;
    }

    static void AddBorder(Transform parent, Color c)
    {
        foreach (var (anchor0, anchor1, size, pos) in new (Vector2, Vector2, Vector2, Vector2)[]
        {
            (new Vector2(0,1), new Vector2(1,1), new Vector2(0,2), new Vector2(0,-1)),
            (new Vector2(0,0), new Vector2(1,0), new Vector2(0,2), new Vector2(0, 1)),
            (new Vector2(0,0), new Vector2(0,1), new Vector2(2,0), new Vector2(1, 0)),
            (new Vector2(1,0), new Vector2(1,1), new Vector2(2,0), new Vector2(-1,0)),
        })
        {
            var line = new GameObject("Border", typeof(RectTransform), typeof(Image));
            line.transform.SetParent(parent, false);
            var rt = line.GetComponent<RectTransform>();
            rt.anchorMin = anchor0; rt.anchorMax = anchor1;
            rt.sizeDelta = size; rt.anchoredPosition = pos;
            line.GetComponent<Image>().color = c;
        }
    }

    static void AddSectionLabel(Transform parent, string text, Color color)
    {
        // Full-width header bar with accent background
        var bg = new GameObject("SectionHeader", typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(parent, false);
        var bgImg = bg.GetComponent<Image>();
        bgImg.color = new Color(color.r * 0.18f, color.g * 0.18f, color.b * 0.18f, 1f);
        var bgLe = bg.AddComponent<LayoutElement>();
        bgLe.preferredHeight = 28;
        bgLe.flexibleWidth   = 1;

        var lbl = CreateChildText(bg.transform, text, 13, color, TextAnchor.MiddleCenter);
        SetStretch(lbl.gameObject);
        lbl.fontStyle = FontStyle.Bold;
    }

    static void SetBorderColor(GameObject cardRoot, Color c)
    {
        foreach (Transform child in cardRoot.transform)
            if (child.name == "Border")
                child.GetComponent<Image>().color = c;
    }

    static GameObject CreatePanel(Transform parent, string name, Color color, bool stretch = false)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = color;
        if (stretch) SetStretch(go);
        return go;
    }

    static Text CreateChildText(Transform parent, string text, int size, Color color, TextAnchor anchor)
    {
        var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<Text>();
        t.text      = text;
        t.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize  = size;
        t.color     = color;
        t.alignment = anchor;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow   = VerticalWrapMode.Truncate;
        return t;
    }

    static void SetStretch(GameObject go)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.sizeDelta = Vector2.zero; rt.anchoredPosition = Vector2.zero;
    }

    static void SetAnchoredRect(GameObject go, float xMin, float yMin, float xMax, float yMax,
                                float padL, float padB, float padR, float padT)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(xMin, yMin);
        rt.anchorMax = new Vector2(xMax, yMax);
        rt.offsetMin = new Vector2(padL, padB);
        rt.offsetMax = new Vector2(padR, padT);
    }

    static void AddHLine(Transform parent, float anchorY, float h, Color color, float alphaMultiplier = 1f)
    {
        var go = new GameObject("HLine", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, anchorY); rt.anchorMax = new Vector2(1, anchorY);
        rt.sizeDelta = new Vector2(0, h);
        go.GetComponent<Image>().color = new Color(color.r, color.g, color.b, color.a * alphaMultiplier);
    }

    PlayerInventory GetBotInventory()
    {
        foreach (var p in GameManager.players)
            if (p != null && p != GameManager.localPlayer)
            {
                var inv = p.GetComponent<PlayerInventory>();
                if (inv != null) return inv;
            }
        return null;
    }
}
