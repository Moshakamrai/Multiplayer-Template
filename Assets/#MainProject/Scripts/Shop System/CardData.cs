using UnityEngine;

public enum CardRarity { Basic, Advanced, Legendary }
public enum CardType { Attack, Defense, Meta }
public enum ShopCategory { CombatCard, TraitCard }

// Simplified combat archetype. The counter logic lives in the family, never the card.
// Strike beats Throw -> Throw beats Block/Parry -> Block/Parry beats Strike.
// Support cards sit outside the triangle (they modify your moves instead of countering).
public enum CardFamily { Strike, Throw, Block, Parry, Support }

[System.Serializable]
public class CombatCardData
{
    public string cardId;
    public string displayName;
    public CardRarity rarity;
    public CardType type;
    public CardFamily family;
    public int cost;
    public float timingWindow;
    public string damagePercent;
    public string description;
    public string triggerName;
}

[System.Serializable]
public class TraitCardData
{
    public string traitId;
    public string displayName;
    public int cost;
    public string effect;
    public string description;
}
