using System.Collections.Generic;
using UnityEngine;

public class PlayerInventory : MonoBehaviour
{
    public int credits = 0;

    // Owned combat cards (persistent collection)
    public List<string> ownedCombatCards = new List<string>();

    // Owned Vex cards (one-time use, persist in inventory until used)
    public List<string> ownedVexCards = new List<string>();

    // Equipped trait (only one at a time, persists for the match)
    public string equippedTraitId = "";

    // Cards selected for the next round (up to 10)
    public List<string> equippedCombatCards = new List<string>();

    public void AddCredits(int amount)
    {
        credits += amount;
    }

    public bool SpendCredits(int amount)
    {
        if (credits < amount) return false;
        credits -= amount;
        return true;
    }

    public bool BuyCombatCard(string cardId, int cost)
    {
        if (credits < cost) return false;
        if (!ownedCombatCards.Contains(cardId))
            ownedCombatCards.Add(cardId);
        credits -= cost;
        Debug.Log($"<color=cyan>INVENTORY:</color> Bought {cardId}. Now owning: {string.Join(",", ownedCombatCards)}");
        return true;
    }

    public bool BuyVexCard(string cardId, int cost)
    {
        if (credits < cost) return false;
        ownedVexCards.Add(cardId);
        credits -= cost;
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
            // Include both owned combat cards and vex cards (player explicitly selected them)
            if ((ownedCombatCards.Contains(id) || ownedVexCards.Contains(id)) && equippedCombatCards.Count < 8)
                equippedCombatCards.Add(id);
        }
    }

    public bool UseVexCard(string cardId)
    {
        if (!ownedVexCards.Contains(cardId)) return false;
        ownedVexCards.Remove(cardId);
        return true;
    }

    public void ResetForNewMatch()
    {
        credits = 0;
        ownedCombatCards.Clear();
        ownedVexCards.Clear();
        equippedTraitId = "";
        equippedCombatCards.Clear();
    }
}
