using UnityEngine;

public enum CardRarity { Basic, Advanced, Legendary }
public enum CardType { Attack, Defense, Meta }
public enum ShopCategory { CombatCard, TraitCard }
public enum AttackLayer { Low, Mid, High }
public enum DefenseLayer { Low, Mid, High }
public enum DefenseType { Passive, Counter, Evasive }

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

    // 3-Layer System
    public AttackLayer? attackLayer;           // For attack cards
    public DefenseLayer? defenseLayer;         // For defense cards
    public DefenseType defenseType;            // Passive, Counter, Evasive
    public int baseDamage;                     // Actual damage value (8-18)
    public float timingWindowBonus;            // ±0.2s, ±0.1s, ±0.0s
    public int baseBlockMitigation = 30;       // Chip damage % when blocked
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
