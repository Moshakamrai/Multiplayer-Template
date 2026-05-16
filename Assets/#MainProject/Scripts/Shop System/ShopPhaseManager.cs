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

        // Generate 6 combat card slots
        for (int i = 0; i < 6; i++)
        {
            float roll = Random.value;
            CombatCardData[] pool;
            if (roll < basicChance) pool = CardDatabase.BasicCards;
            else if (roll < basicChance + advancedChance) pool = CardDatabase.AdvancedCards;
            else pool = CardDatabase.LegendaryCards;

            if (pool.Length > 0)
                _combatShopSlots.Add(pool[Random.Range(0, pool.Length)]);
        }

        // Generate 3 Vex cards (random, regardless of round)
        var vexPool = new List<VexCardData>(CardDatabase.VexCards);
        for (int i = 0; i < 3 && vexPool.Count > 0; i++)
        {
            int idx = Random.Range(0, vexPool.Count);
            _vexShopSlots.Add(vexPool[idx]);
            vexPool.RemoveAt(idx);
        }

        // Generate 3 Trait cards (random, regardless of round)
        var traitPool = new List<TraitCardData>(CardDatabase.TraitCards);
        for (int i = 0; i < 3 && traitPool.Count > 0; i++)
        {
            int idx = Random.Range(0, traitPool.Count);
            _traitShopSlots.Add(traitPool[idx]);
            traitPool.RemoveAt(idx);
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

        // 1. Buy best affordable combat cards (prioritize higher rarity/damage)
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

        // Buy up to 3 combat cards or until broke
        int combatBought = 0;
        foreach (var item in affordableCombat)
        {
            if (combatBought >= 3) break;
            if (botInv.credits < item.card.cost) break;
            if (botInv.ownedCombatCards.Contains(item.card.cardId)) continue;

            botInv.BuyCombatCard(item.card.cardId, item.card.cost);
            combatBought++;
        }

        // 2. Buy a Vex card if affordable and interesting
        foreach (var vex in _vexShopSlots)
        {
            if (vex != null && botInv.credits >= vex.cost)
            {
                botInv.BuyVexCard(vex.cardId, vex.cost);
                break; // Buy one vex max
            }
        }

        // 3. Buy a Trait if affordable (prioritize offensive traits)
        foreach (var trait in _traitShopSlots)
        {
            if (trait != null && botInv.credits >= trait.cost)
            {
                botInv.BuyTraitCard(trait.traitId, trait.cost);
                break; // One trait max
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
        inv.EquipCombatCards(equipList);

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
        // Notify RhythmRoundManager to start next round
        RhythmRoundManager.Instance?.StartSlowRound();
    }

    // Client access for UI drawing
    public IReadOnlyList<CombatCardData> CombatShopSlots => _combatShopSlots;
    public IReadOnlyList<VexCardData> VexShopSlots => _vexShopSlots;
    public IReadOnlyList<TraitCardData> TraitShopSlots => _traitShopSlots;
}
