using UnityEngine;
using System.Collections.Generic;

public class CardDatabase : MonoBehaviour
{
    public static CardDatabase Instance { get; private set; }

    // ══════════════════════════════════════════════════════════════════════════
    // 3-LAYER ATTACK CARDS (9 total: 3 per layer)
    // ══════════════════════════════════════════════════════════════════════════

    // ── LOW ATTACKS (Cyan/Teal color) ───────────────────────────────────────
    public static readonly CombatCardData[] LowAttacks = new CombatCardData[]
    {
        new CombatCardData
        {
            cardId = "jab", displayName = "Fast Jab", rarity = CardRarity.Basic, type = CardType.Attack,
            cost = 1, baseDamage = 8, timingWindow = 0.5f, timingWindowBonus = 0.2f,
            damagePercent = "+8%", description = "Fastest low attack. Each consecutive hit +5% damage.",
            triggerName = "Jab", attackLayer = AttackLayer.Low
        },
        new CombatCardData
        {
            cardId = "sweep", displayName = "Heavy Sweep", rarity = CardRarity.Basic, type = CardType.Attack,
            cost = 1, baseDamage = 15, timingWindow = 0.4f, timingWindowBonus = -0.1f,
            damagePercent = "+15%", description = "Risky low kick. Perfect timing staggers opponent.",
            triggerName = "Sweep", attackLayer = AttackLayer.Low
        },
        new CombatCardData
        {
            cardId = "drive_low", displayName = "Drive Low", rarity = CardRarity.Basic, type = CardType.Attack,
            cost = 1, baseDamage = 12, timingWindow = 0.45f, timingWindowBonus = 0.0f,
            damagePercent = "+12%", description = "Balanced low attack. Reliable mid-range option.",
            triggerName = "DriveLow", attackLayer = AttackLayer.Low
        },
    };

    // ── MID ATTACKS (White/Gray color) ──────────────────────────────────────
    public static readonly CombatCardData[] MidAttacks = new CombatCardData[]
    {
        new CombatCardData
        {
            cardId = "cross", displayName = "Cross Punch", rarity = CardRarity.Basic, type = CardType.Attack,
            cost = 1, baseDamage = 12, timingWindow = 0.6f, timingWindowBonus = 0.3f,
            damagePercent = "+12%", description = "Most forgiving timing of mid attacks. Baseline damage.",
            triggerName = "Cross", attackLayer = AttackLayer.Mid
        },
        new CombatCardData
        {
            cardId = "hook", displayName = "Fast Hook", rarity = CardRarity.Basic, type = CardType.Attack,
            cost = 1, baseDamage = 10, timingWindow = 0.5f, timingWindowBonus = 0.2f,
            damagePercent = "+10%", description = "Quick mid attack, great for combos.",
            triggerName = "Hook", attackLayer = AttackLayer.Mid
        },
        new CombatCardData
        {
            cardId = "overhead", displayName = "Overhead Strike", rarity = CardRarity.Advanced, type = CardType.Attack,
            cost = 2, baseDamage = 16, timingWindow = 0.4f, timingWindowBonus = -0.1f,
            damagePercent = "+16%", description = "Risky mid attack. Tight timing, highest mid-layer damage.",
            triggerName = "Overhead", attackLayer = AttackLayer.Mid
        },
    };

    // ── HIGH ATTACKS (Yellow/Gold color) ────────────────────────────────────
    public static readonly CombatCardData[] HighAttacks = new CombatCardData[]
    {
        new CombatCardData
        {
            cardId = "slap", displayName = "Quick Slap", rarity = CardRarity.Basic, type = CardType.Attack,
            cost = 1, baseDamage = 8, timingWindow = 0.5f, timingWindowBonus = 0.2f,
            damagePercent = "+8%", description = "Fastest high attack. Quick and punishing.",
            triggerName = "Slap", attackLayer = AttackLayer.High
        },
        new CombatCardData
        {
            cardId = "spin", displayName = "Spinning Slash", rarity = CardRarity.Basic, type = CardType.Attack,
            cost = 1, baseDamage = 14, timingWindow = 0.6f, timingWindowBonus = 0.3f,
            damagePercent = "+14%", description = "Balanced high attack. Mid-range speed and damage.",
            triggerName = "Spin", attackLayer = AttackLayer.High
        },
        new CombatCardData
        {
            cardId = "unbreakablepunch", displayName = "Overhead Smash", rarity = CardRarity.Advanced, type = CardType.Attack,
            cost = 2, baseDamage = 18, timingWindow = 0.4f, timingWindowBonus = -0.1f,
            damagePercent = "+18%", description = "Highest damage in game. Very tight timing window.",
            triggerName = "UnbreakablePunch", attackLayer = AttackLayer.High
        },
    };

    // ══════════════════════════════════════════════════════════════════════════
    // 3-LAYER DEFENSE CARDS (9 total: 3 per layer)
    // ══════════════════════════════════════════════════════════════════════════

    // ── LOW BLOCKS (Cyan/Teal color) ────────────────────────────────────────
    public static readonly CombatCardData[] LowDefenses = new CombatCardData[]
    {
        new CombatCardData
        {
            cardId = "block", displayName = "Crouch Block", rarity = CardRarity.Basic, type = CardType.Defense,
            cost = 1, timingWindow = 0.6f, baseBlockMitigation = 100, defenseType = DefenseType.Passive,
            damagePercent = "100% block", description = "Pure low defense. Safest, most passive.",
            triggerName = "Block", defenseLayer = DefenseLayer.Low
        },
        new CombatCardData
        {
            cardId = "clutch", displayName = "Counter Sweep", rarity = CardRarity.Basic, type = CardType.Defense,
            cost = 1, timingWindow = 0.6f, baseBlockMitigation = 100, defenseType = DefenseType.Counter,
            damagePercent = "100% block + 5% reflect", description = "Block and punish. Reflect 5% damage on successful block.",
            triggerName = "Clutch", defenseLayer = DefenseLayer.Low
        },
        new CombatCardData
        {
            cardId = "left", displayName = "Quick Dodge Low", rarity = CardRarity.Basic, type = CardType.Defense,
            cost = 1, timingWindow = 0.45f, baseBlockMitigation = 100, defenseType = DefenseType.Evasive,
            damagePercent = "100% evasion", description = "Evasive low defense. Perfect timing grants invulnerability frame.",
            triggerName = "Left", defenseLayer = DefenseLayer.Low
        },
    };

    // ── MID BLOCKS (White/Gray color) ───────────────────────────────────────
    public static readonly CombatCardData[] MidDefenses = new CombatCardData[]
    {
        new CombatCardData
        {
            cardId = "guard", displayName = "Middle Guard", rarity = CardRarity.Basic, type = CardType.Defense,
            cost = 1, timingWindow = 0.6f, baseBlockMitigation = 100, defenseType = DefenseType.Passive,
            damagePercent = "100% block", description = "Balanced mid defense. Most straightforward block.",
            triggerName = "Guard", defenseLayer = DefenseLayer.Mid
        },
        new CombatCardData
        {
            cardId = "parryintent", displayName = "Parry Mid", rarity = CardRarity.Basic, type = CardType.Defense,
            cost = 1, timingWindow = 0.6f, baseBlockMitigation = 100, defenseType = DefenseType.Counter,
            damagePercent = "100% block + 60% reflect", description = "Risky parry. Good timing reflects 60% damage back.",
            triggerName = "ParryIntent", defenseLayer = DefenseLayer.Mid
        },
        new CombatCardData
        {
            cardId = "fake", displayName = "Sway Mid", rarity = CardRarity.Basic, type = CardType.Defense,
            cost = 1, timingWindow = 0.45f, baseBlockMitigation = 100, defenseType = DefenseType.Evasive,
            damagePercent = "100% evasion", description = "Evasive mid defense. Perfect timing grants invulnerability.",
            triggerName = "Fake", defenseLayer = DefenseLayer.Mid
        },
    };

    // ── HIGH BLOCKS (Yellow/Gold color) ─────────────────────────────────────
    public static readonly CombatCardData[] HighDefenses = new CombatCardData[]
    {
        new CombatCardData
        {
            cardId = "right", displayName = "High Guard", rarity = CardRarity.Basic, type = CardType.Defense,
            cost = 1, timingWindow = 0.6f, baseBlockMitigation = 100, defenseType = DefenseType.Passive,
            damagePercent = "100% block", description = "Pure high defense. Protects head and body.",
            triggerName = "Right", defenseLayer = DefenseLayer.High
        },
        new CombatCardData
        {
            cardId = "uppercut", displayName = "Intercept High", rarity = CardRarity.Advanced, type = CardType.Defense,
            cost = 2, timingWindow = 0.6f, baseBlockMitigation = 100, defenseType = DefenseType.Counter,
            damagePercent = "100% block + disarm", description = "Punish prediction. Forces opponent off high layer next turn.",
            triggerName = "Uppercut", defenseLayer = DefenseLayer.High
        },
        new CombatCardData
        {
            cardId = "reverse", displayName = "Redirect High", rarity = CardRarity.Advanced, type = CardType.Defense,
            cost = 2, timingWindow = 0.45f, baseBlockMitigation = 100, defenseType = DefenseType.Evasive,
            damagePercent = "100% evasion + push", description = "Evasive counter. Perfect timing pushes opponent back.",
            triggerName = "Reverse", defenseLayer = DefenseLayer.High
        },
    };

    // ══════════════════════════════════════════════════════════════════════════
    // TRAIT CARDS (8 SIMPLIFIED TRAITS)
    // ══════════════════════════════════════════════════════════════════════════

    public static readonly TraitCardData[] TraitCards = new TraitCardData[]
    {
        // Offensive Traits
        new TraitCardData
        {
            traitId = "fury", displayName = "Fury", cost = 3,
            effect = "+15% all attack damage for the entire round",
            description = "Pure aggression. Every hit you land this round deals extra damage."
        },
        new TraitCardData
        {
            traitId = "chain", displayName = "Chain", cost = 3,
            effect = "Each consecutive hit gains +5% damage (stacks, resets on miss, max +15%)",
            description = "Rewards accuracy. Build momentum with each successful hit."
        },
        new TraitCardData
        {
            traitId = "bloodlust", displayName = "Bloodlust", cost = 3,
            effect = "Gain 2% health per successful hit this round",
            description = "Sustain through offense. Land hits to stay healthy."
        },

        // Defensive Traits
        new TraitCardData
        {
            traitId = "fortress", displayName = "Fortress", cost = 4,
            effect = "Reduce all damage taken by 15% for the round",
            description = "Tank trait. You're harder to hurt. Great for sustain builds."
        },
        new TraitCardData
        {
            traitId = "evasion", displayName = "Evasion", cost = 3,
            effect = "Increase parry/dodge/evasive timing windows by 10%",
            description = "Slippery defense. Easier to land evasive moves."
        },

        // Utility Traits
        new TraitCardData
        {
            traitId = "reactive", displayName = "Reactive", cost = 4,
            effect = "On successful block, your next attack deals +20% damage (bonus once per turn)",
            description = "Defensive counter. Turn blocks into bigger hits."
        },
        new TraitCardData
        {
            traitId = "momentum", displayName = "Momentum", cost = 3,
            effect = "Consecutive hits increase damage by 10% each (max +30%, resets on miss)",
            description = "Combo snowball. Each hit makes the next hit deadlier."
        },
        new TraitCardData
        {
            traitId = "grappler", displayName = "Grappler", cost = 3,
            effect = "Unlock special Throw card (unblockable 20% damage if opponent has no defense queued)",
            description = "Clinch specialist. Grapple becomes a serious threat."
        },
    };

    // ── LEGACY COMPATIBILITY: Combined array for backward-compat searches ────
    public static List<CombatCardData> BasicCards
    {
        get
        {
            var list = new List<CombatCardData>();
            list.AddRange(LowAttacks);
            list.AddRange(MidAttacks);
            list.AddRange(HighAttacks);
            list.AddRange(LowDefenses);
            list.AddRange(MidDefenses);
            list.AddRange(HighDefenses);
            return list;
        }
    }

    public static List<CombatCardData> AdvancedCards => new List<CombatCardData>();
    public static List<CombatCardData> LegendaryCards => new List<CombatCardData>();

    // ── RARITY PROGRESSION ─────────────────────────────────────────────────────
    public static void GetRarityChances(int roundNumber, out float basicChance, out float advancedChance, out float legendaryChance)
    {
        switch (roundNumber)
        {
            case 1: case 2:
                basicChance = 0.80f; advancedChance = 0.20f; legendaryChance = 0.00f; break;
            case 3: case 4:
                basicChance = 0.60f; advancedChance = 0.35f; legendaryChance = 0.05f; break;
            case 5: case 6:
                basicChance = 0.40f; advancedChance = 0.45f; legendaryChance = 0.15f; break;
            case 7: case 8:
                basicChance = 0.20f; advancedChance = 0.50f; legendaryChance = 0.30f; break;
            case 9:
                basicChance = 0.10f; advancedChance = 0.40f; legendaryChance = 0.50f; break;
            default:
                basicChance = 0.10f; advancedChance = 0.40f; legendaryChance = 0.50f; break;
        }
    }

    void Awake() { if (Instance == null) Instance = this; }
}
