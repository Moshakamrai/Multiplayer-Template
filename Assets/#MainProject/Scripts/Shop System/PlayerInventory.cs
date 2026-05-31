using System.Collections.Generic;
using UnityEngine;

public class PlayerInventory : MonoBehaviour
{
    public int credits = 0;

    // Owned combat cards (one entry per unique card; duplicates upgrade instead of stacking).
    public List<string> ownedCombatCards = new List<string>();

    // Upgrade level per owned card (1..MaxLevel). Buying a duplicate bumps the level.
    public Dictionary<string, int> cardLevels = new Dictionary<string, int>();
    public const int MaxLevel = 3;

    // The card chosen to fight with for each family (the loadout: one card per type).
    public Dictionary<CardFamily, string> selectedByFamily = new Dictionary<CardFamily, string>();

    // Equipped trait (one passive at a time, persists for the match)
    public string equippedTraitId = "";

    // Round loadout — built from selectedByFamily (one card per family).
    public List<string> equippedCombatCards = new List<string>();

    // Legacy reference kept so older call sites compile. Owning is no longer capped
    // (you collect across the 5 families and upgrade duplicates), so this is effectively unlimited.
    public const int MaxCombatCards = 99;

    public void AddCredits(int amount) => credits += amount;

    public bool SpendCredits(int amount)
    {
        if (credits < amount) return false;
        credits -= amount;
        return true;
    }

    public int GetLevel(string cardId) => cardLevels.TryGetValue(cardId, out int l) ? l : 1;
    public bool IsMaxLevel(string cardId) => GetLevel(cardId) >= MaxLevel;

    // Buy a combat card. If already owned, the purchase UPGRADES it (capped at MaxLevel).
    public bool BuyCombatCard(string cardId, int cost)
    {
        if (credits < cost) return false;

        if (ownedCombatCards.Contains(cardId))
        {
            int lvl = GetLevel(cardId);
            if (lvl >= MaxLevel) return false;      // already maxed — don't charge
            cardLevels[cardId] = lvl + 1;
            credits -= cost;
            Debug.Log($"<color=cyan>INVENTORY:</color> Upgraded {cardId} to Lv{lvl + 1}");
            return true;
        }

        // New card
        ownedCombatCards.Add(cardId);
        cardLevels[cardId] = 1;
        var fam = CardDatabase.GetCardFamily(cardId);
        if (!selectedByFamily.ContainsKey(fam)) selectedByFamily[fam] = cardId; // auto-select if first of its family
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

    // Choose which owned card fights in its family slot.
    public void SelectCard(string cardId)
    {
        if (!ownedCombatCards.Contains(cardId)) return;
        selectedByFamily[CardDatabase.GetCardFamily(cardId)] = cardId;
    }

    public string GetSelected(CardFamily fam) => selectedByFamily.TryGetValue(fam, out string id) ? id : "";

    // Equip ALL owned cards. In battle the hand draws ONE card per family at random
    // from this pool (see CardManager), so owning more cards of a type = more variety.
    public void EquipCombatCards(List<string> cardIds)
    {
        equippedCombatCards.Clear();
        foreach (var id in ownedCombatCards)
            if (!equippedCombatCards.Contains(id))
                equippedCombatCards.Add(id);
    }

    public void ResetForNewMatch()
    {
        credits = 0;
        ownedCombatCards.Clear();
        cardLevels.Clear();
        selectedByFamily.Clear();
        equippedTraitId = "";
        equippedCombatCards.Clear();
    }
}
