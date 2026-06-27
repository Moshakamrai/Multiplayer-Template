using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;

public class ShopPhaseManager : MonoBehaviour
{
    public static ShopPhaseManager Instance { get; private set; }

    public bool isShopPhase = false;
    public float shopTimeRemaining = 120f;
    public int currentShopRound = 1;

    // Server-only shop state (8 combat cards + 4 trait cards)
    private List<CombatCardData> _combatShopSlots = new List<CombatCardData>();
    private List<TraitCardData> _traitShopSlots = new List<TraitCardData>();

    // Track which slots have been purchased (for empty slot display)
    private HashSet<int> _purchasedCombatSlots = new HashSet<int>();
    private HashSet<int> _purchasedTraitSlots = new HashSet<int>();

    private readonly HashSet<uint> _lockedPlayerNetIds = new HashSet<uint>();

    private void Awake() { if (Instance == null) Instance = this; }

    public void StartShopPhase(int roundNumber)
    {
        isShopPhase = true;
        shopTimeRemaining = 120f;
        currentShopRound = roundNumber;
        _lockedPlayerNetIds.Clear();

        GenerateShop(roundNumber);

        // Bot auto-buys immediately and auto-locks in.
        var players = new List<PlayerController>(GameManager.players);
        foreach (var p in players)
        {
            if (p != null && p.GetComponent<BotController>() != null)
            {
                var botInv = p.GetComponent<PlayerInventory>();
                if (botInv != null)
                {
                    RunBotShopAI(botInv);
                    LockInShop(botInv); // auto-lock the bot so the human can finish
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

        CardDatabase.GetRarityChances(roundNumber, out float basicChance, out float advancedChance, out float legendaryChance);

        // Local player's inventory — used only to skip cards they've already maxed (nothing to gain).
        var localInv = GameManager.localPlayer?.GetComponent<PlayerInventory>();

        // Generate 4 combat card slots. Owned cards CAN appear (buying a duplicate upgrades it);
        // we just skip ones already at max level and avoid duplicate slots in the same shop.
        var usedIds = new HashSet<string>();
        int attempts = 0;
        while (_combatShopSlots.Count < 4 && attempts < 80)
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
            if (localInv != null && localInv.ownedCombatCards.Contains(card.cardId) && localInv.IsMaxLevel(card.cardId)) continue;

            usedIds.Add(card.cardId);
            _combatShopSlots.Add(card);
        }

        // Generate 4 trait card slots (shuffle and pick 4 unique traits)
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

        // New netId-based check (works for any number of players).
        foreach (var p in players)
        {
            if (p == null) continue;
            var netId = p.GetComponent<NetworkIdentity>()?.netId ?? 0;
            if (netId == 0 || !_lockedPlayerNetIds.Contains(netId))
                return false;
        }
        return true;
    }

    // ── BOT SHOP AI ──────────────────────────────────────────────────────────

    private void RunBotShopAI(PlayerInventory botInv)
    {
        if (botInv == null) return;

        // 1. Buy/upgrade affordable combat cards (a duplicate purchase upgrades an owned card)
        var affordableCombat = new List<(CombatCardData card, int index)>();
        for (int i = 0; i < _combatShopSlots.Count; i++)
        {
            var card = _combatShopSlots[i];
            if (card == null) continue;
            bool ownedMaxed = botInv.ownedCombatCards.Contains(card.cardId) && botInv.IsMaxLevel(card.cardId);
            if (card.cost <= botInv.credits && !ownedMaxed)
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
            if (botInv.credits < item.card.cost) continue;
            botInv.BuyCombatCard(item.card.cardId, item.card.cost);
        }

        // 2. Buy a trait card if affordable (traits use Trait Tokens, not credits)
        foreach (var trait in _traitShopSlots)
        {
            if (trait != null && botInv.traitTokens >= trait.cost)
            {
                botInv.BuyTraitCard(trait.traitId, trait.cost);
                break;
            }
        }

        // 3. Auto-equip all owned combat cards (up to 8)
        botInv.EquipCombatCards(new List<string>(botInv.ownedCombatCards));
    }

    // ── PURCHASING ───────────────────────────────────────────────────────────

    public void TryBuyCombatCard(PlayerInventory inv, int slotIndex)
    {
        if (!isShopPhase || slotIndex < 0 || slotIndex >= _combatShopSlots.Count)
        {
            Debug.LogWarning($"[ShopPhaseManager] TryBuyCombatCard invalid: isShopPhase={isShopPhase}, slot={slotIndex}, slots={_combatShopSlots.Count}");
            return;
        }
        var card = _combatShopSlots[slotIndex];
        if (card == null) return;
        bool ok = inv.BuyCombatCard(card.cardId, card.cost); // adds the card, or upgrades it if already owned
        Debug.Log($"[ShopPhaseManager] BuyCombatCard slot={slotIndex} id={card.cardId} cost={card.cost} credits={inv.credits} ok={ok}");
    }

    public void TryBuyTraitCard(PlayerInventory inv, int slotIndex)
    {
        if (!isShopPhase || slotIndex < 0 || slotIndex >= _traitShopSlots.Count)
        {
            Debug.LogWarning($"[ShopPhaseManager] TryBuyTraitCard invalid: isShopPhase={isShopPhase}, slot={slotIndex}, slots={_traitShopSlots.Count}");
            return;
        }
        var card = _traitShopSlots[slotIndex];
        if (card == null) return;
        bool ok = inv.BuyTraitCard(card.traitId, card.cost);
        Debug.Log($"[ShopPhaseManager] BuyTraitCard slot={slotIndex} id={card.traitId} cost={card.cost} tokens={inv.traitTokens} ok={ok}");
    }

    public void LockInShop(PlayerInventory inv)
    {
        if (!isShopPhase)
        {
            Debug.LogWarning($"[ShopPhaseManager] LockInShop called while isShopPhase=false for {inv?.name ?? "?"}");
            return;
        }
        var equipList = new List<string>(inv.ownedCombatCards);
        Debug.Log($"<color=yellow>SHOP LOCK-IN:</color> Player has {inv.ownedCombatCards.Count} combat cards, equipping: {string.Join(",", equipList)}");
        inv.EquipCombatCards(equipList);
        Debug.Log($"<color=yellow>SHOP LOCK-IN:</color> Equipped {inv.equippedCombatCards.Count} cards: {string.Join(",", inv.equippedCombatCards)}");

        // Track lock-in by netId so HashSet ordering can never mix up p1/p2.
        uint netId = inv != null && inv.GetComponent<NetworkIdentity>() != null
            ? inv.GetComponent<NetworkIdentity>().netId
            : 0;
        if (netId != 0) _lockedPlayerNetIds.Add(netId);
    }

    private void EquipAllPlayers()
    {
        foreach (var player in GameManager.players)
        {
            var inv = player?.GetComponent<PlayerInventory>();
            if (inv == null) continue;
            var equipList = new List<string>(inv.ownedCombatCards);
            inv.EquipCombatCards(equipList);
            Debug.Log($"<color=yellow>SHOP FINALIZE:</color> Equipped {inv.equippedCombatCards.Count} cards for {player.PlayerName}: {string.Join(",", inv.equippedCombatCards)}");
        }
    }

    private void FinalizeShop()
    {
        // Make sure every purchased card is equipped before we leave, even if the
        // Lock In button wasn't pressed or the command path failed.
        EquipAllPlayers();

        isShopPhase = false;
        RhythmRoundManager.Instance?.ShowRoundPicker();
    }

    // Client access for UI drawing
    public IReadOnlyList<CombatCardData> CombatShopSlots => _combatShopSlots;
    public IReadOnlyList<TraitCardData> TraitShopSlots => _traitShopSlots;

    public bool IsCombatSlotPurchased(int slotIndex) => _purchasedCombatSlots.Contains(slotIndex);
    public bool IsTraitSlotPurchased(int slotIndex) => _purchasedTraitSlots.Contains(slotIndex);

    public void MarkCombatSlotPurchased(int slotIndex) => _purchasedCombatSlots.Add(slotIndex);
    public void MarkTraitSlotPurchased(int slotIndex) => _purchasedTraitSlots.Add(slotIndex);
}
