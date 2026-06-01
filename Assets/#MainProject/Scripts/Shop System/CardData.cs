using UnityEngine;

public enum CardRarity { Basic, Advanced, Legendary }
public enum CardType { Attack, Defense, Meta }
public enum ShopCategory { CombatCard, TraitCard }

// Combat family triangle: Strike beats Throw → Throw beats Block/Parry → Block/Parry beats Strike.
// Support sits outside the triangle (modifies moves, no direct counter).
public enum CardFamily { Strike, Throw, Block, Parry, Support }

// Every card's unique perk ID. None = no perk. Evaluated in RhythmRoundManager after combat resolves.
public enum CardPerk
{
    None,
    FastRedraw,      // Jab:      family unlocks after 0 beats instead of 1 (no lockout)
    Stagger,         // Hook:     opponent cannot act on the next beat if this hits
    SelfCost,        // Boom:     deal +bonus dmg but take 5% HP self-damage on use
    CounterBonus,    // Uppercut: +20% extra dmg if opponent attacked last beat
    Overload,        // Overclock:deal massive dmg but take 10% self-dmg; can't be interrupted
    GrappleBleed,    // Grapple:  leaves a bleed — 3% HP per beat for 2 beats if it lands
    FakeCounter,     // Fake:     if opponent played defense last beat, deal double dmg
    SweepKnockback,  // Sweep:    breaks opponent's incoming combo buffer if it lands
    BlockCounter,    // Block:    if opponent attacked last beat, next attack +15% dmg
    DodgeEvade,      // Dodge:    if opponent played a Strike, negate ALL damage this beat
    ReflectPrecision,// Reflect:  within 0.08s of beat = full reflect + stun; outside = normal
    ClutchCritical,  // Clutch:   within 0.1s = 2× counter; miss = 20% self-dmg
    MirrorAmplify,   // Mirror:   reflect + 15% bonus; stacks with Lv bonus
    ReverseLeech,    // Reverse:  return dmg AND heal 5% of returned dmg as HP
    FocusBuff,       // Focus:    next Strike +50% dmg; Lv3 also makes it uninterruptible
    TauntLock,       // Taunt:    forces opponent attack-only next beat; Lv3 = 2 beats
    TrapPunish,      // Trap:     punishes next move; Lv3 = punishes next 2 moves
    CageBreak,       // Cage:     opponent cannot defend next beat; Lv3 = next 2 beats
}

[System.Serializable]
public class CombatCardData
{
    public string     cardId;
    public string     displayName;
    public CardRarity rarity;
    public CardType   type;
    public CardFamily family;
    public int        cost;
    public float      timingWindow;   // base timing window in seconds (Lv1)
    public string     damagePercent;  // display string (e.g. "+15%")
    public string     description;
    public string     triggerName;

    // ── Per-level stats (raw values used by combat; display shown in shop) ──
    public int   baseDamage;          // Lv1 flat damage applied to opponent HP %
    public int   lv2Damage;           // Lv2 damage
    public int   lv3Damage;           // Lv3 damage
    public float lv2TimingBonus;      // extra seconds of timing window at Lv2
    public float lv3TimingBonus;      // extra seconds of timing window at Lv3

    // ── Unique card perk (unlocks at Lv3 unless noted) ───────────────────
    public CardPerk perk;
    public string   perkDescription;  // shown in shop upgrade preview
    public bool     perkActiveAtLv1;  // true = perk is always on (Lv upgrades strengthen it)
    public bool     perkActiveAtLv2;  // true = perk unlocks at Lv2

    // Convenience: current damage for a given level.
    public int DamageForLevel(int lvl) => lvl >= 3 ? lv3Damage : lvl == 2 ? lv2Damage : baseDamage;
    public float TimingForLevel(int lvl) => timingWindow + (lvl >= 3 ? lv3TimingBonus : lvl == 2 ? lv2TimingBonus : 0f);
    public bool  PerkActiveAt(int lvl) => perkActiveAtLv1 || (perkActiveAtLv2 && lvl >= 2) || lvl >= 3;
}

[System.Serializable]
public class TraitCardData
{
    public string traitId;
    public string displayName;
    public int    cost;
    public string effect;
    public string description;
}
