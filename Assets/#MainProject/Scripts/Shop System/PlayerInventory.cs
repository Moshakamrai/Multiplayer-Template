using System.Collections.Generic;
using UnityEngine;

public class PlayerInventory : MonoBehaviour
{
    public int credits = 0;

    // Combat cards with upgrade levels: cardId -> upgradeLevel (0-3)
    public Dictionary<string, int> ownedCombatCards = new Dictionary<string, int>();

    // Equipped trait (one passive at a time, persists for the match)
    public string equippedTraitId = "";

    // Cards selected for the next round (up to 8)
    public List<string> equippedCombatCards = new List<string>();

    public const int MaxCombatCards = 8;
    public const int MaxUpgradeLevel = 3;

    public void AddCredits(int amount) => credits += amount;

    public bool SpendCredits(int amount)
    {
        if (credits < amount) return false;
        credits -= amount;
        return true;
    }

    /// <summary>
    /// Buy or upgrade a combat card
    /// If card already owned: UPGRADE (increase tier, no inventory slot consumed)
    /// If card not owned: BUY (add to inventory, consumes 1 slot)
    /// </summary>
    public bool BuyCombatCard(string cardId, int cost)
    {
        if (credits < cost) return false;

        // Card already owned - UPGRADE
        if (ownedCombatCards.ContainsKey(cardId))
        {
            int currentLevel = ownedCombatCards[cardId];

            // Already maxed
            if (currentLevel >= MaxUpgradeLevel)
            {
                Debug.Log($"<color=yellow>INVENTORY:</color> {cardId} already maxed at tier {currentLevel}");
                return false;
            }

            // Upgrade
            ownedCombatCards[cardId]++;
            credits -= cost;
            Debug.Log($"<color=cyan>INVENTORY:</color> Upgraded {cardId} to tier {ownedCombatCards[cardId]}");
            return true;
        }

        // Card not owned - BUY (if inventory has space)
        if (ownedCombatCards.Count >= MaxCombatCards)
        {
            Debug.Log($"<color=red>INVENTORY FULL:</color> Cannot buy {cardId}. Max {MaxCombatCards} cards.");
            return false;
        }

        ownedCombatCards[cardId] = 0; // Base tier
        credits -= cost;
        Debug.Log($"<color=cyan>INVENTORY:</color> Bought {cardId}. Owned: {string.Join(",", ownedCombatCards.Keys)}");
        return true;
    }

    public bool BuyTraitCard(string traitId, int cost)
    {
        if (credits < cost) return false;
        equippedTraitId = traitId;
        credits -= cost;
        return true;
    }

    /// <summary>
    /// Get upgrade level for a card (0-3, or -1 if not owned)
    /// </summary>
    public int GetUpgradeLevel(string cardId)
    {
        return ownedCombatCards.ContainsKey(cardId) ? ownedCombatCards[cardId] : -1;
    }

    /// <summary>
    /// Check if card is owned (at any upgrade level)
    /// </summary>
    public bool OwnsCard(string cardId) => ownedCombatCards.ContainsKey(cardId);

    /// <summary>
    /// Get damage multiplier for a card based on upgrade level
    /// Tier 0: 1.0x (base)
    /// Tier 1: 1.1x (+10%)
    /// Tier 2: 1.2x (+20%)
    /// Tier 3: 1.35x (+35%)
    /// </summary>
    public float GetUpgradeMultiplier(string cardId)
    {
        int level = GetUpgradeLevel(cardId);
        return level switch
        {
            0 => 1.0f,
            1 => 1.1f,
            2 => 1.2f,
            3 => 1.35f,
            _ => 1.0f
        };
    }

    /// <summary>
    /// Get visual star count for UI display (⭐⭐⭐⭐ = tier 3)
    /// </summary>
    public string GetUpgradeStars(string cardId)
    {
        int level = GetUpgradeLevel(cardId);
        if (level < 0) return ""; // Not owned
        return new string('⭐', level + 1); // Tier 0 = 1 star, Tier 3 = 4 stars
    }

    public void EquipCombatCards(List<string> cardIds)
    {
        equippedCombatCards.Clear();
        foreach (var id in cardIds)
        {
            if (OwnsCard(id) && equippedCombatCards.Count < MaxCombatCards)
                equippedCombatCards.Add(id);
        }
    }

    public void ResetForNewMatch()
    {
        credits = 0;
        ownedCombatCards.Clear();
        equippedTraitId = "";
        equippedCombatCards.Clear();
    }
}
