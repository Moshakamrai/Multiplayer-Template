using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ShopPhaseManager : MonoBehaviour
{
    public static ShopPhaseManager Instance { get; private set; }

    public bool isShopPhase = false;
    public bool isInitialShop = true; // First time shop (pick 4 from 6)
    public float shopTimeRemaining = 120f;
    public int currentShopRound = 1;

    // Server-only shop state
    // Normal shop: 8 combat cards + 4 trait cards
    // Initial shop: 6 combat cards (3 ATK + 3 DEF) + 0 traits
    private List<CombatCardData> _combatShopSlots = new List<CombatCardData>();
    private List<TraitCardData> _traitShopSlots = new List<TraitCardData>();

    // Track which slots have been purchased (for empty slot display)
    private HashSet<int> _purchasedCombatSlots = new HashSet<int>();
    private HashSet<int> _purchasedTraitSlots = new HashSet<int>();

    // Track initial shop selections per player
    private Dictionary<PlayerInventory, List<string>> _initialSelections = new Dictionary<PlayerInventory, List<string>>();

    private bool _p1Locked = false;
    private bool _p2Locked = false;

    private void Awake() { if (Instance == null) Instance = this; }

    public void StartShopPhase(int roundNumber)
    {
        isShopPhase = true;
        shopTimeRemaining = 120f;
        currentShopRound = roundNumber;
        _p1Locked = false;
        _p2Locked = false;

        GenerateShop(roundNumber);

        // Bot auto-buys immediately
        var players = new List<PlayerController>(GameManager.players);
        foreach (var p in players)
        {
            if (p != null && p.GetComponent<BotController>() != null)
            {
                var botInv = p.GetComponent<PlayerInventory>();
                if (botInv != null)
                {
                    RunBotShopAI(botInv);
                    int idx = GetPlayerIndex(botInv);
                    if (idx == 0) _p1Locked = true;
                    else if (idx == 1) _p2Locked = true;
                }
                break;
            }
        }

        StartCoroutine(ShopTimerRoutine());
    }

    private void GenerateShop(int roundNumber)
    {
        _combatShopSlots.Clear();
        _traitShopSlots.Clear();
        _purchasedCombatSlots.Clear();
        _purchasedTraitSlots.Clear();
        _initialSelections.Clear();

        if (isInitialShop)
            GenerateInitialShop();
        else
            GenerateNormalShop(roundNumber);
    }

    private void GenerateInitialShop()
    {
        // Initial shop: 6 random cards (3 ATK + 3 DEF, no traits)
        var atkCards = new List<CombatCardData>();
        var defCards = new List<CombatCardData>();

        // Collect all attacks and defenses from layer arrays
        atkCards.AddRange(CardDatabase.LowAttacks);
        atkCards.AddRange(CardDatabase.MidAttacks);
        atkCards.AddRange(CardDatabase.HighAttacks);

        defCards.AddRange(CardDatabase.LowDefenses);
        defCards.AddRange(CardDatabase.MidDefenses);
        defCards.AddRange(CardDatabase.HighDefenses);

        // Shuffle and pick 3 attacks, 3 defenses
        for (int i = 0; i < 3 && atkCards.Count > 0; i++)
        {
            int idx = Random.Range(0, atkCards.Count);
            _combatShopSlots.Add(atkCards[idx]);
            atkCards.RemoveAt(idx);
        }

        for (int i = 0; i < 3 && defCards.Count > 0; i++)
        {
            int idx = Random.Range(0, defCards.Count);
            _combatShopSlots.Add(defCards[idx]);
            defCards.RemoveAt(idx);
        }
    }

    private void GenerateNormalShop(int roundNumber)
    {
        // Normal shop: 8 random combat cards + 4 random traits
        CardDatabase.GetRarityChances(roundNumber, out float basicChance, out float advancedChance, out float legendaryChance);

        // Collect all owned card IDs across players
        var ownedCardIds = new HashSet<string>();
        foreach (var p in GameManager.players)
        {
            var inv = p?.GetComponent<PlayerInventory>();
            if (inv != null)
                foreach (var id in inv.ownedCombatCards.Keys)
                    ownedCardIds.Add(id);
        }

        // Generate 8 combat card slots (allow duplicates since upgrade system handles it)
        var usedIds = new HashSet<string>();
        int attempts = 0;
        while (_combatShopSlots.Count < 8 && attempts < 80)
        {
            attempts++;
            float roll = Random.value;
            CombatCardData[] pool;
            if (roll < basicChance) pool = CardDatabase.BasicCards;
            else if (roll < basicChance + advancedChance) pool = CardDatabase.AdvancedCards;
            else pool = CardDatabase.LegendaryCards;

            if (pool.Length == 0) continue;
            var card = pool[Random.Range(0, pool.Length)];
            if (usedIds.Contains(card.cardId)) continue;

            usedIds.Add(card.cardId);
            _combatShopSlots.Add(card);
        }

        // Generate 4 trait card slots
        var traitPool = new List<TraitCardData>(CardDatabase.TraitCards);
        var usedTraitIds = new HashSet<string>();
        while (_traitShopSlots.Count < 4 && traitPool.Count > 0)
        {
            int idx = Random.Range(0, traitPool.Count);
            var card = traitPool[idx];
            traitPool.RemoveAt(idx);
            if (usedTraitIds.Contains(card.traitId)) continue;
            usedTraitIds.Add(card.traitId);
            _traitShopSlots.Add(card);
        }
    }

    private IEnumerator ShopTimerRoutine()
    {
        while (shopTimeRemaining > 0f && isShopPhase)
        {
            yield return null;
            shopTimeRemaining -= Time.deltaTime;

            if (AllPlayersLockedIn())
            {
                FinalizeShop();
                yield break;
            }
        }

        if (isShopPhase) FinalizeShop();
    }

    private bool AllPlayersLockedIn()
    {
        var players = new List<PlayerController>(GameManager.players);
        if (players.Count == 0) return false;
        bool p1 = _p1Locked;
        bool p2 = players.Count > 1 ? _p2Locked : true;
        return p1 && p2;
    }

    // ── BOT SHOP AI ──────────────────────────────────────────────────────────

    private void RunBotShopAI(PlayerInventory botInv)
    {
        if (botInv == null) return;

        if (isInitialShop)
            RunBotInitialShop(botInv);
        else
            RunBotNormalShop(botInv);
    }

    private void RunBotInitialShop(PlayerInventory botInv)
    {
        // Initial shop: pick 4 random cards from the 6 available
        var selections = new List<string>();
        var cardIndices = new List<int>();
        for (int i = 0; i < _combatShopSlots.Count; i++) cardIndices.Add(i);

        // Shuffle and pick 4
        for (int i = 0; i < 4 && cardIndices.Count > 0; i++)
        {
            int idx = Random.Range(0, cardIndices.Count);
            var card = _combatShopSlots[cardIndices[idx]];
            if (card != null)
                selections.Add(card.cardId);
            cardIndices.RemoveAt(idx);
        }

        // Confirm selection
        ConfirmInitialShopSelection(botInv, selections);
    }

    private void RunBotNormalShop(PlayerInventory botInv)
    {
        int credits = botInv.credits;
        bool inventoryFull = botInv.ownedCombatCards.Count >= PlayerInventory.MaxCombatCards;

        // 1. Prefer upgrading existing cards over buying new ones
        var upgradeableCards = new List<(CombatCardData card, int index, int upgrade)>();
        for (int i = 0; i < _combatShopSlots.Count; i++)
        {
            var card = _combatShopSlots[i];
            if (card != null && botInv.OwnsCard(card.cardId))
            {
                int upgradeLevel = botInv.GetUpgradeLevel(card.cardId);
                if (upgradeLevel < PlayerInventory.MaxUpgradeLevel && card.cost <= credits)
                    upgradeableCards.Add((card, i, upgradeLevel));
            }
        }

        // Upgrade high-value cards first
        foreach (var item in upgradeableCards)
        {
            if (botInv.credits < item.card.cost) break;
            botInv.BuyCombatCard(item.card.cardId, item.card.cost);
        }

        // 2. Buy new cards if inventory not full
        if (!inventoryFull)
        {
            var affordableCombat = new List<(CombatCardData card, int index)>();
            for (int i = 0; i < _combatShopSlots.Count; i++)
            {
                var card = _combatShopSlots[i];
                if (card != null && card.cost <= credits && !botInv.OwnsCard(card.cardId))
                    affordableCombat.Add((card, i));
            }

            // Prefer expensive/higher rarity cards
            affordableCombat.Sort((a, b) =>
            {
                int costCompare = b.card.cost.CompareTo(a.card.cost);
                if (costCompare != 0) return costCompare;
                return b.card.rarity.CompareTo(a.card.rarity);
            });

            foreach (var item in affordableCombat)
            {
                if (botInv.ownedCombatCards.Count >= PlayerInventory.MaxCombatCards) break;
                if (botInv.credits < item.card.cost) break;
                botInv.BuyCombatCard(item.card.cardId, item.card.cost);
            }
        }

        // 3. Buy a trait card if affordable
        foreach (var trait in _traitShopSlots)
        {
            if (trait != null && botInv.credits >= trait.cost)
            {
                botInv.BuyTraitCard(trait.traitId, trait.cost);
                break;
            }
        }

        // 4. Auto-equip all owned combat cards (up to 8)
        botInv.EquipCombatCards(new List<string>(botInv.ownedCombatCards.Keys));
    }

    // ── PURCHASING ───────────────────────────────────────────────────────────

    public void TryBuyCombatCard(PlayerInventory inv, int slotIndex)
    {
        if (!isShopPhase || slotIndex < 0 || slotIndex >= _combatShopSlots.Count) return;
        var card = _combatShopSlots[slotIndex];
        if (card == null) return;

        // Allow buying new cards or upgrading existing ones
        bool isOwned = inv.OwnsCard(card.cardId);
        bool isMaxedUpgrade = isOwned && inv.GetUpgradeLevel(card.cardId) >= PlayerInventory.MaxUpgradeLevel;
        bool inventoryFull = inv.ownedCombatCards.Count >= PlayerInventory.MaxCombatCards;

        if (isMaxedUpgrade)
        {
            Debug.Log($"<color=yellow>SHOP:</color> {card.cardId} is already maxed");
            return;
        }

        if (!isOwned && inventoryFull)
        {
            Debug.Log($"<color=yellow>SHOP:</color> Inventory full, cannot buy {card.cardId}");
            return;
        }

        inv.BuyCombatCard(card.cardId, card.cost);
        MarkCombatSlotPurchased(slotIndex);
    }

    public void TryBuyTraitCard(PlayerInventory inv, int slotIndex)
    {
        if (!isShopPhase || slotIndex < 0 || slotIndex >= _traitShopSlots.Count) return;
        var card = _traitShopSlots[slotIndex];
        if (card == null) return;
        inv.BuyTraitCard(card.traitId, card.cost);
    }

    public void ConfirmInitialShopSelection(PlayerInventory inv, List<string> cardIds)
    {
        if (!isInitialShop || inv == null || cardIds == null) return;

        // Buy the 4 selected cards (no cost in initial shop)
        foreach (var cardId in cardIds)
        {
            var card = _combatShopSlots.Find(c => c != null && c.cardId == cardId);
            if (card != null)
                inv.BuyCombatCard(cardId, 0); // Free purchase in initial shop
        }

        inv.EquipCombatCards(cardIds);
        int idx = GetPlayerIndex(inv);
        if (idx == 0) _p1Locked = true;
        else if (idx == 1) _p2Locked = true;

        Debug.Log($"<color=cyan>INITIAL SHOP:</color> Player {idx} confirmed {cardIds.Count} cards");
    }

    public void LockInShop(PlayerInventory inv)
    {
        if (!isShopPhase) return;
        var equipList = new List<string>(inv.ownedCombatCards.Keys);
        Debug.Log($"<color=yellow>SHOP LOCK-IN:</color> Player has {inv.ownedCombatCards.Count} combat cards, equipping: {string.Join(",", equipList)}");
        inv.EquipCombatCards(equipList);
        Debug.Log($"<color=yellow>SHOP LOCK-IN:</color> Equipped {inv.equippedCombatCards.Count} cards: {string.Join(",", inv.equippedCombatCards)}");

        int idx = GetPlayerIndex(inv);
        if (idx == 0) _p1Locked = true;
        else if (idx == 1) _p2Locked = true;
    }

    private int GetPlayerIndex(PlayerInventory inv)
    {
        var players = new List<PlayerController>(GameManager.players);
        for (int i = 0; i < players.Count; i++)
            if (players[i] != null && players[i].GetComponent<PlayerInventory>() == inv) return i;
        return -1;
    }

    private void FinalizeShop()
    {
        if (isInitialShop)
        {
            // Transition from initial shop to first round
            isInitialShop = false;
            isShopPhase = false;
            Debug.Log("<color=cyan>INITIAL SHOP COMPLETE - Starting game!</color>");
            RhythmRoundManager.Instance?.ShowRoundPicker();
        }
        else
        {
            // Normal shop finalization
            isShopPhase = false;
            RhythmRoundManager.Instance?.ShowRoundPicker();
        }
    }

    // Client access for UI drawing
    public IReadOnlyList<CombatCardData> CombatShopSlots => _combatShopSlots;
    public IReadOnlyList<TraitCardData> TraitShopSlots => _traitShopSlots;

    public bool IsCombatSlotPurchased(int slotIndex) => _purchasedCombatSlots.Contains(slotIndex);
    public bool IsTraitSlotPurchased(int slotIndex) => _purchasedTraitSlots.Contains(slotIndex);

    public void MarkCombatSlotPurchased(int slotIndex) => _purchasedCombatSlots.Add(slotIndex);
    public void MarkTraitSlotPurchased(int slotIndex) => _purchasedTraitSlots.Add(slotIndex);
}
