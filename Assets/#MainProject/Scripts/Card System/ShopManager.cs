using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Generates shop slot contents per round using TFT-style rarity ramp.
/// Also manages player decks (add/remove cards, 12-card limit) and traits.
/// </summary>
public class ShopManager : MonoBehaviour
{
    public static ShopManager Instance;

    public const int DeckLimit = 12;
    public const int MaxCardsPerShopVisit = 2;

    // Hardcoded to Vex for now; swap per character when animations are ready
    public const CharacterClass PlayerClass = CharacterClass.Vex;

    // Per-player decks (card IDs)
    public List<int> p1Deck = new List<int>();
    public List<int> p2Deck = new List<int>();

    // Per-player active trait ID (-1 = none)
    public int p1TraitId = -1;
    public int p2TraitId = -1;

    // Current shared shop slots (regenerated each round)
    // Slots 0-1: basics, 2-3: advanced, 4: legendary (round 4+), 5: trait
    public List<ShopSlot> sharedSlots = new List<ShopSlot>();

    // Per-player class card slots (always 3, constant across shops for that player)
    public List<ShopSlot> p1ClassSlots = new List<ShopSlot>();
    public List<ShopSlot> p2ClassSlots = new List<ShopSlot>();

    // Tracks how many cards bought this shop phase per player
    public int p1BoughtThisPhase = 0;
    public int p2BoughtThisPhase = 0;

    private void Awake()
    {
        if (Instance == null) Instance = this;
    }

    // -----------------------------------------------------------------------
    // Pre-Round Shop Generation (only 6 basic cards shown, no class/trait)
    // -----------------------------------------------------------------------
    public List<ShopSlot> GeneratePreRoundShop()
    {
        var slots = new List<ShopSlot>();
        var basicPool = CardDatabase.GetBasicPool();
        Shuffle(basicPool);

        // Show first 6 basics (all starters are in the basic pool)
        int count = Mathf.Min(6, basicPool.Count);
        for (int i = 0; i < count; i++)
        {
            slots.Add(new ShopSlot
            {
                slotType = SlotType.BasicCard,
                cardId = basicPool[i].id,
                traitId = -1,
                isFreeForPicker = basicPool[i].isStarter
            });
        }
        return slots;
    }

    // -----------------------------------------------------------------------
    // Between-Round Shop Generation (TFT-style rarity ramp)
    // -----------------------------------------------------------------------
    public void GenerateBetweenRoundShop(int roundNumber)
    {
        sharedSlots.Clear();
        p1BoughtThisPhase = 0;
        p2BoughtThisPhase = 0;

        var basicPool    = CardDatabase.GetBasicPool();
        var advancedPool = CardDatabase.GetAdvancedPool();
        var legendPool   = CardDatabase.GetLegendaryPool();
        var traitPool    = CardDatabase.GetAvailableTraits(PlayerClass);

        Shuffle(basicPool);
        Shuffle(advancedPool);
        Shuffle(legendPool);
        Shuffle(traitPool);

        // Rarity ramp based on round number (TFT style):
        // Round 1: 6 basic, 0 adv, 0 leg
        // Round 2: 5 basic, 1 adv, 0 leg
        // Round 3: 2 basic, 2 adv, 0 leg  (but wait — spec says round 3 = 2adv 4basic? typo? using spec)
        // Round 4+: legendary slot unlocks

        int basicCount    = GetBasicCount(roundNumber);
        int advancedCount = GetAdvancedCount(roundNumber);
        bool showLegendary = roundNumber >= 4 && legendPool.Count > 0;
        bool showTrait     = traitPool.Count > 0;

        // Basic slots
        for (int i = 0; i < basicCount && i < basicPool.Count; i++)
            sharedSlots.Add(new ShopSlot { slotType = SlotType.BasicCard, cardId = basicPool[i].id, traitId = -1 });

        // Advanced slots
        for (int i = 0; i < advancedCount && i < advancedPool.Count; i++)
            sharedSlots.Add(new ShopSlot { slotType = SlotType.AdvancedCard, cardId = advancedPool[i].id, traitId = -1 });

        // Legendary slot (round 4+)
        if (showLegendary)
            sharedSlots.Add(new ShopSlot { slotType = SlotType.LegendaryCard, cardId = legendPool[0].id, traitId = -1 });

        // Trait slot
        if (showTrait)
            sharedSlots.Add(new ShopSlot { slotType = SlotType.Trait, cardId = -1, traitId = traitPool[0].id });

        // Generate class card slots (3 per player, constant per match phase)
        RegenerateClassSlots();
    }

    private int GetBasicCount(int round)
    {
        if (round <= 1) return 6;
        if (round == 2) return 5;
        return 4; // round 3+: 4 basic, 2 advanced per spec
    }

    private int GetAdvancedCount(int round)
    {
        if (round <= 1) return 0;
        if (round == 2) return 1;
        return 2; // round 3+
    }

    private void RegenerateClassSlots()
    {
        var classCards = CardDatabase.GetClassCards(PlayerClass);
        Shuffle(classCards);

        p1ClassSlots.Clear();
        p2ClassSlots.Clear();

        int count = Mathf.Min(3, classCards.Count);
        for (int i = 0; i < count; i++)
        {
            p1ClassSlots.Add(new ShopSlot { slotType = SlotType.ClassCard, cardId = classCards[i].id, traitId = -1 });
            p2ClassSlots.Add(new ShopSlot { slotType = SlotType.ClassCard, cardId = classCards[i].id, traitId = -1 });
        }
    }

    // -----------------------------------------------------------------------
    // Purchase logic
    // -----------------------------------------------------------------------
    public bool TryBuyCard(int playerIndex, int cardId)
    {
        CardData card = CardDatabase.GetCardById(cardId);
        if (card == null) return false;

        // Check purchase limit this phase
        int bought = playerIndex == 0 ? p1BoughtThisPhase : p2BoughtThisPhase;
        if (bought >= MaxCardsPerShopVisit) return false;

        // Check credits
        int cost = card.shopCost;
        if (!EconomyManager.Instance.SpendCredits(playerIndex, cost)) return false;

        // Add to deck
        var deck = playerIndex == 0 ? p1Deck : p2Deck;
        if (deck.Count >= DeckLimit)
        {
            // Can't add — UI should prompt remove first
            EconomyManager.Instance.AddCredits(playerIndex, cost); // refund
            return false;
        }

        deck.Add(cardId);

        if (playerIndex == 0) p1BoughtThisPhase++;
        else                  p2BoughtThisPhase++;

        Debug.Log($"[ShopManager] Player {playerIndex + 1} bought {card.cardName} for {cost} credits.");
        return true;
    }

    public bool TryBuyTrait(int playerIndex, int traitId)
    {
        TraitData trait = CardDatabase.GetTraitById(traitId);
        if (trait == null) return false;

        int bought = playerIndex == 0 ? p1BoughtThisPhase : p2BoughtThisPhase;
        if (bought >= MaxCardsPerShopVisit) return false;

        if (!EconomyManager.Instance.SpendCredits(playerIndex, trait.shopCost)) return false;

        // Replace current trait
        if (playerIndex == 0) p1TraitId = traitId;
        else                  p2TraitId = traitId;

        if (playerIndex == 0) p1BoughtThisPhase++;
        else                  p2BoughtThisPhase++;

        Debug.Log($"[ShopManager] Player {playerIndex + 1} equipped trait {trait.traitName}.");
        return true;
    }

    // Remove a card from deck (called from deck management UI)
    public bool RemoveCardFromDeck(int playerIndex, int deckSlotIndex)
    {
        var deck = playerIndex == 0 ? p1Deck : p2Deck;
        if (deckSlotIndex < 0 || deckSlotIndex >= deck.Count) return false;
        deck.RemoveAt(deckSlotIndex);
        return true;
    }

    // Swap card at deck slot (used when buying when at limit: buy triggers remove prompt)
    public bool SwapCardInDeck(int playerIndex, int removeSlotIndex, int newCardId)
    {
        var deck = playerIndex == 0 ? p1Deck : p2Deck;
        if (removeSlotIndex < 0 || removeSlotIndex >= deck.Count) return false;
        deck[removeSlotIndex] = newCardId;
        return true;
    }

    // Called pre-match to give starter cards to a player
    public void GiveStarterCards(int playerIndex, List<int> cardIds)
    {
        var deck = playerIndex == 0 ? p1Deck : p2Deck;
        deck.Clear();
        foreach (int id in cardIds)
            deck.Add(id);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------
    private void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    public List<int> GetDeck(int playerIndex) => playerIndex == 0 ? p1Deck : p2Deck;
    public int GetTrait(int playerIndex) => playerIndex == 0 ? p1TraitId : p2TraitId;
}

// -----------------------------------------------------------------------
// Data Types
// -----------------------------------------------------------------------
public enum SlotType { BasicCard, AdvancedCard, LegendaryCard, ClassCard, Trait }

[System.Serializable]
public class ShopSlot
{
    public SlotType slotType;
    public int cardId;    // -1 if trait slot
    public int traitId;   // -1 if card slot
    public bool isFreeForPicker; // only used in pre-round shop
}
