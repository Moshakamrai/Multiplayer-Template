using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ShopPhaseManager : MonoBehaviour
{
    public static ShopPhaseManager Instance { get; private set; }

    public bool isShopPhase = false;
    public float shopTimeRemaining = 120f;
    public int currentShopRound = 1;

    // Server-only shop state
    private List<CombatCardData> _combatShopSlots = new List<CombatCardData>();
    private List<VexCardData> _vexShopSlots = new List<VexCardData>();
    private List<TraitCardData> _traitShopSlots = new List<TraitCardData>();

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
        _vexShopSlots.Clear();
        _traitShopSlots.Clear();

        CardDatabase.GetRarityChances(roundNumber, out float basicChance, out float advancedChance, out float legendaryChance);

        // Collect all owned card IDs across players so we don't show duplicates
        var ownedCardIds = new HashSet<string>();
        foreach (var p in GameManager.players)
        {
            var inv = p?.GetComponent<PlayerInventory>();
            if (inv != null)
                foreach (var id in inv.ownedCombatCards)
                    ownedCardIds.Add(id);
        }

        // Generate 6 combat card slots (no duplicates, no owned cards)
        var usedIds = new HashSet<string>();
        int attempts = 0;
        while (_combatShopSlots.Count < 6 && attempts < 50)
        {
            attempts++;
            float roll = Random.value;
            CombatCardData[] pool;
            if (roll < basicChance) pool = CardDatabase.BasicCards;
            else if (roll < basicChance + advancedChance) pool = CardDatabase.AdvancedCards;
            else pool = CardDatabase.LegendaryCards;

            if (pool.Length == 0) continue;
            var card = pool[Random.Range(0, pool.Length)];

            // Skip if already owned or already in shop
            if (ownedCardIds.Contains(card.cardId)) continue;
            if (usedIds.Contains(card.cardId)) continue;

            usedIds.Add(card.cardId);
            _combatShopSlots.Add(card);
        }

        // Generate 3 Vex cards (no duplicates)
        var vexPool = new List<VexCardData>(CardDatabase.VexCards);
        var usedVexIds = new HashSet<string>();
        while (_vexShopSlots.Count < 3 && vexPool.Count > 0)
        {
            int idx = Random.Range(0, vexPool.Count);
            var card = vexPool[idx];
            vexPool.RemoveAt(idx);
            if (usedVexIds.Contains(card.cardId)) continue;
            usedVexIds.Add(card.cardId);
            _vexShopSlots.Add(card);
        }

        // Generate 3 Trait cards (no duplicates)
        var traitPool = new List<TraitCardData>(CardDatabase.TraitCards);
        var usedTraitIds = new HashSet<string>();
        while (_traitShopSlots.Count < 3 && traitPool.Count > 0)
        {
            int idx = Random.Range(0, traitPool.Count);
            var trait = traitPool[idx];
            traitPool.RemoveAt(idx);
            if (usedTraitIds.Contains(trait.traitId)) continue;
            usedTraitIds.Add(trait.traitId);
            _traitShopSlots.Add(trait);
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

        int credits = botInv.credits;
        bool inventoryFull = botInv.ownedCombatCards.Count >= 10;

        // 1. Buy best affordable combat cards (respect 10-card inventory limit)
        if (!inventoryFull)
        {
            var affordableCombat = new List<(CombatCardData card, int index)>();
            for (int i = 0; i < _combatShopSlots.Count; i++)
            {
                var card = _combatShopSlots[i];
                if (card != null && card.cost <= credits && !botInv.ownedCombatCards.Contains(card.cardId))
                    affordableCombat.Add((card, i));
            }

            // Sort by cost descending (prefer expensive/stronger cards), then by rarity
            affordableCombat.Sort((a, b) =>
            {
                int costCompare = b.card.cost.CompareTo(a.card.cost);
                if (costCompare != 0) return costCompare;
                return b.card.rarity.CompareTo(a.card.rarity);
            });

            // Buy until inventory full or broke
            foreach (var item in affordableCombat)
            {
                if (botInv.ownedCombatCards.Count >= 10) break;
                if (botInv.credits < item.card.cost) break;
                if (botInv.ownedCombatCards.Contains(item.card.cardId)) continue;

                botInv.BuyCombatCard(item.card.cardId, item.card.cost);
            }
        }

        // 2. Buy a Vex card if affordable
        foreach (var vex in _vexShopSlots)
        {
            if (vex != null && botInv.credits >= vex.cost)
            {
                botInv.BuyVexCard(vex.cardId, vex.cost);
                break;
            }
        }

        // 3. Buy a Trait if affordable
        foreach (var trait in _traitShopSlots)
        {
            if (trait != null && botInv.credits >= trait.cost)
            {
                botInv.BuyTraitCard(trait.traitId, trait.cost);
                break;
            }
        }

        // 4. Auto-equip all owned combat cards (up to 10)
        var equipList = new List<string>(botInv.ownedCombatCards);
        botInv.EquipCombatCards(equipList);
    }

    // ── PURCHASING ───────────────────────────────────────────────────────────

    
    public void TryBuyCombatCard(PlayerInventory inv, int slotIndex)
    {
        if (!isShopPhase || slotIndex < 0 || slotIndex >= _combatShopSlots.Count) return;
        var card = _combatShopSlots[slotIndex];
        if (card == null) return;
        inv.BuyCombatCard(card.cardId, card.cost);
    }

    
    public void TryBuyVexCard(PlayerInventory inv, int slotIndex)
    {
        if (!isShopPhase || slotIndex < 0 || slotIndex >= _vexShopSlots.Count) return;
        var card = _vexShopSlots[slotIndex];
        if (card == null) return;
        inv.BuyVexCard(card.cardId, card.cost);
    }

    
    public void TryBuyTraitCard(PlayerInventory inv, int slotIndex)
    {
        if (!isShopPhase || slotIndex < 0 || slotIndex >= _traitShopSlots.Count) return;
        var trait = _traitShopSlots[slotIndex];
        if (trait == null) return;
        inv.BuyTraitCard(trait.traitId, trait.cost);
    }

    
    public void LockInShop(PlayerInventory inv)
    {
        if (!isShopPhase) return;
        // Auto-equip all owned combat cards up to 10
        var equipList = new List<string>(inv.ownedCombatCards);
        Debug.Log($"<color=yellow>SHOP LOCK-IN:</color> Player has {inv.ownedCombatCards.Count} owned cards, equipping: {string.Join(",", equipList)}");
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
        isShopPhase = false;
        // Show round picker instead of auto-starting
        RhythmRoundManager.Instance?.ShowRoundPicker();
    }

    // Client access for UI drawing
    public IReadOnlyList<CombatCardData> CombatShopSlots => _combatShopSlots;
    public IReadOnlyList<VexCardData> VexShopSlots => _vexShopSlots;
    public IReadOnlyList<TraitCardData> TraitShopSlots => _traitShopSlots;
}
