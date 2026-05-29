using UnityEngine;

public enum CardRarity { Basic, Advanced, Legendary }
public enum CardType { Attack, Defense, Meta }
public enum ShopCategory { CombatCard, TraitCard }

[System.Serializable]
public class CombatCardData
{
    public string cardId;
    public string displayName;
    public CardRarity rarity;
    public CardType type;
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
