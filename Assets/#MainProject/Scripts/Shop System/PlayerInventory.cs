using System.Collections.Generic;
using UnityEngine;

public class PlayerInventory : MonoBehaviour
{
    public int credits = 0;

    // Owned combat cards (persistent collection, max 8 equipped per round)
    public List<string> ownedCombatCards = new List<string>();

    // Equipped trait (one passive at a time, persists for the match)
    public string equippedTraitId = "";

    // Cards selected for the next round (up to 8)
    public List<string> equippedCombatCards = new List<string>();

    public const int MaxCombatCards = 8;

    public void AddCredits(int amount) => credits += amount;

    public bool SpendCredits(int amount)
    {
        if (credits < amount) return false;
        credits -= amount;
        return true;
    }

    public bool BuyCombatCard(string cardId, int cost)
    {
        if (credits < cost) return false;
        if (ownedCombatCards.Count >= MaxCombatCards) return false;
        if (!ownedCombatCards.Contains(cardId))
            ownedCombatCards.Add(cardId);
        credits -= cost;
        Debug.Log($"<color=cyan>INVENTORY:</color> Bought {cardId}. Owned: {string.Join(",", ownedCombatCards)}");
        return true;
    }

    public bool BuyTraitCard(string traitId, int cost)
    {
        if (credits < cost) return false;
        equippedTraitId = traitId;
        credits -= cost;
        return true;
    }

    public void EquipCombatCards(List<string> cardIds)
    {
        equippedCombatCards.Clear();
        foreach (var id in cardIds)
        {
            if (ownedCombatCards.Contains(id) && equippedCombatCards.Count < MaxCombatCards)
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
