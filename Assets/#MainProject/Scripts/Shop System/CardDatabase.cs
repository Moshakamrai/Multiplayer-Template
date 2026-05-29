using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class CardDatabase : MonoBehaviour
{
    public static CardDatabase Instance { get; private set; }

    // ── ATTACKS BY LAYER ───────────────────────────────────────────────────
    public static readonly CombatCardData[] LowAttacks = new CombatCardData[]
    {
        new CombatCardData { cardId="jab", displayName="Jab", rarity=CardRarity.Basic, type=CardType.Attack, cost=1, baseDamage=8, timingWindow=0.30f, damagePercent="+8%", description="Fast low hit.", triggerName="Jab", attackLayer=AttackLayer.Low },
        new CombatCardData { cardId="sweep", displayName="Sweep", rarity=CardRarity.Advanced, type=CardType.Attack, cost=2, baseDamage=16, timingWindow=0.30f, damagePercent="+16%", description="Low kick that catches defensive players.", triggerName="Sweep", attackLayer=AttackLayer.Low },
        new CombatCardData { cardId="grapple", displayName="Grapple", rarity=CardRarity.Basic, type=CardType.Attack, cost=1, baseDamage=18, timingWindow=0.30f, damagePercent="+18%", description="Breaks through defenses.", triggerName="Grapple", attackLayer=AttackLayer.Low },
    };

    public static readonly CombatCardData[] MidAttacks = new CombatCardData[]
    {
        new CombatCardData { cardId="cross", displayName="Cross", rarity=CardRarity.Basic, type=CardType.Attack, cost=1, baseDamage=12, timingWindow=0.30f, damagePercent="+12%", description="Straight power hit.", triggerName="Cross", attackLayer=AttackLayer.Mid },
        new CombatCardData { cardId="hook", displayName="Hook", rarity=CardRarity.Basic, type=CardType.Attack, cost=1, baseDamage=15, timingWindow=0.30f, damagePercent="+15%", description="Wide heavy hit.", triggerName="Hook", attackLayer=AttackLayer.Mid },
        new CombatCardData { cardId="uppercut", displayName="Uppercut", rarity=CardRarity.Advanced, type=CardType.Attack, cost=2, baseDamage=20, timingWindow=0.30f, damagePercent="+20%", description="Rising hit that catches dodgers.", triggerName="Uppercut", attackLayer=AttackLayer.Mid },
    };

    public static readonly CombatCardData[] HighAttacks = new CombatCardData[]
    {
        new CombatCardData { cardId="unbreakablepunch", displayName="UnbreakablePunch", rarity=CardRarity.Advanced, type=CardType.Attack, cost=2, baseDamage=25, timingWindow=0.30f, damagePercent="+25%", description="Explosive high damage.", triggerName="UnbreakablePunch", attackLayer=AttackLayer.High },
        new CombatCardData { cardId="overclock", displayName="Overclock", rarity=CardRarity.Advanced, type=CardType.Attack, cost=2, baseDamage=16, timingWindow=0.30f, damagePercent="+35%", description="High damage rush with self-damage.", triggerName="Overclock", attackLayer=AttackLayer.High },
        new CombatCardData { cardId="fake", displayName="Fake", rarity=CardRarity.Basic, type=CardType.Attack, cost=1, baseDamage=5, timingWindow=0.30f, damagePercent="+5%", description="Bait the opponent.", triggerName="Fake", attackLayer=AttackLayer.High },
    };

    // ── DEFENSES BY LAYER ──────────────────────────────────────────────────
    public static readonly CombatCardData[] LowDefenses = new CombatCardData[]
    {
        new CombatCardData { cardId="block", displayName="Block", rarity=CardRarity.Basic, type=CardType.Defense, cost=1, timingWindow=0.30f, baseBlockMitigation=100, defenseType=DefenseType.Passive, damagePercent="100% block", description="Standard defense.", triggerName="Block", defenseLayer=DefenseLayer.Low },
        new CombatCardData { cardId="left", displayName="Left", rarity=CardRarity.Basic, type=CardType.Defense, cost=1, timingWindow=0.30f, baseBlockMitigation=100, defenseType=DefenseType.Evasive, damagePercent="100% evasion", description="Dodge left.", triggerName="Left", defenseLayer=DefenseLayer.Low },
        new CombatCardData { cardId="clutch", displayName="Clutch", rarity=CardRarity.Basic, type=CardType.Defense, cost=1, timingWindow=0.30f, baseBlockMitigation=100, defenseType=DefenseType.Counter, damagePercent="100% block", description="Perfect timing blocks heavy attacks.", triggerName="Clutch", defenseLayer=DefenseLayer.Low },
    };

    public static readonly CombatCardData[] MidDefenses = new CombatCardData[]
    {
        new CombatCardData { cardId="parryintent", displayName="ParryIntent", rarity=CardRarity.Basic, type=CardType.Defense, cost=1, timingWindow=0.30f, baseBlockMitigation=100, defenseType=DefenseType.Counter, damagePercent="120% reflect", description="Elite parry.", triggerName="ParryIntent", defenseLayer=DefenseLayer.Mid },
        new CombatCardData { cardId="focus", displayName="Focus", rarity=CardRarity.Advanced, type=CardType.Defense, cost=2, timingWindow=0.30f, baseBlockMitigation=50, defenseType=DefenseType.Passive, damagePercent="50% block", description="Charge up for +50% next attack.", triggerName="Focus", defenseLayer=DefenseLayer.Mid },
        new CombatCardData { cardId="reverse", displayName="Reverse", rarity=CardRarity.Advanced, type=CardType.Defense, cost=2, timingWindow=0.30f, baseBlockMitigation=100, defenseType=DefenseType.Counter, damagePercent="100% reflect", description="Redirect damage back.", triggerName="Reverse", defenseLayer=DefenseLayer.Mid },
    };

    public static readonly CombatCardData[] HighDefenses = new CombatCardData[]
    {
        new CombatCardData { cardId="right", displayName="Right", rarity=CardRarity.Basic, type=CardType.Defense, cost=1, timingWindow=0.30f, baseBlockMitigation=100, defenseType=DefenseType.Evasive, damagePercent="100% evasion", description="Dodge right.", triggerName="Right", defenseLayer=DefenseLayer.High },
        new CombatCardData { cardId="cage", displayName="Cage", rarity=CardRarity.Advanced, type=CardType.Defense, cost=2, timingWindow=0.30f, baseBlockMitigation=100, defenseType=DefenseType.Passive, damagePercent="100% block", description="Prevent opponent defense.", triggerName="Cage", defenseLayer=DefenseLayer.High },
        new CombatCardData { cardId="trap", displayName="Trap", rarity=CardRarity.Advanced, type=CardType.Defense, cost=2, timingWindow=0.30f, baseBlockMitigation=100, defenseType=DefenseType.Passive, damagePercent="100% block", description="Hidden delayed hit.", triggerName="Trap", defenseLayer=DefenseLayer.High },
        new CombatCardData { cardId="mirror", displayName="Mirror", rarity=CardRarity.Advanced, type=CardType.Defense, cost=2, timingWindow=0.30f, baseBlockMitigation=100, defenseType=DefenseType.Counter, damagePercent="115% reflect", description="Copy opponent's move.", triggerName="Mirror", defenseLayer=DefenseLayer.High },
    };

    // ── TRAIT CARDS ────────────────────────────────────────────────────────
    public static readonly TraitCardData[] TraitCards = new TraitCardData[]
    {
        // Offense Traits
        new TraitCardData { traitId="piercing", displayName="Piercing", cost=3, effect="Ignore 25% of opponent's Block defense", description="Defense becomes less effective. Chip damage through blocks becomes real damage." },
        new TraitCardData { traitId="bloodlust", displayName="Bloodlust", cost=3, effect="Each consecutive hit gains +6% damage (stacks, resets on miss)", description="Rewards accuracy. Build up momentum with each successful hit." },
        new TraitCardData { traitId="executioner", displayName="Executioner", cost=3, effect="+35% damage when opponent above 65% health", description="Finish them fast before they recover. Press the advantage early." },
        new TraitCardData { traitId="momentum", displayName="Momentum", cost=4, effect="Consecutive hits increase damage by 10% each (max +40%)", description="Combo snowball trait. Each hit makes the next hit deadlier." },

        // Defense Traits
        new TraitCardData { traitId="anchored", displayName="Anchored", cost=4, effect="Reduce stagger buildup by 40%", description="Stay in the fight longer. Takes more hits to knock you down." },
        new TraitCardData { traitId="fortress", displayName="Fortress", cost=4, effect="Reduce damage taken by 18%", description="Tank trait. You're harder to hurt. Sustain builds or grinds." },
        new TraitCardData { traitId="stalwart", displayName="Stalwart", cost=4, effect="Each successful block increases next attack by 10% (max 50%)", description="Defensive counter. Turn defense into offense." },
        new TraitCardData { traitId="regenerate", displayName="Regenerate", cost=3, effect="Heal 4% health every beat during round", description="Sustain trait. Passive healing keeps you in the fight." },

        // Utility Traits
        new TraitCardData { traitId="quicktrigger", displayName="Quicktrigger", cost=3, effect="Timing window +0.1s for all cards", description="Easier execution. More time to hit the sweet spot." },
        new TraitCardData { traitId="echo", displayName="Echo", cost=5, effect="25% chance cards refresh and can be used again next turn", description="Card duplication. Sometimes your moves come back for free." },
    };

    // ── RARITY POOLS ──────────────────────────────────────────────────────
    public static CombatCardData[] BasicCards
    {
        get
        {
            var list = new List<CombatCardData>();
            list.AddRange(LowAttacks.Where(c => c.rarity == CardRarity.Basic));
            list.AddRange(MidAttacks.Where(c => c.rarity == CardRarity.Basic));
            list.AddRange(HighAttacks.Where(c => c.rarity == CardRarity.Basic));
            list.AddRange(LowDefenses.Where(c => c.rarity == CardRarity.Basic));
            list.AddRange(MidDefenses.Where(c => c.rarity == CardRarity.Basic));
            list.AddRange(HighDefenses.Where(c => c.rarity == CardRarity.Basic));
            return list.ToArray();
        }
    }

    public static CombatCardData[] AdvancedCards
    {
        get
        {
            var list = new List<CombatCardData>();
            list.AddRange(LowAttacks.Where(c => c.rarity == CardRarity.Advanced));
            list.AddRange(MidAttacks.Where(c => c.rarity == CardRarity.Advanced));
            list.AddRange(HighAttacks.Where(c => c.rarity == CardRarity.Advanced));
            list.AddRange(LowDefenses.Where(c => c.rarity == CardRarity.Advanced));
            list.AddRange(MidDefenses.Where(c => c.rarity == CardRarity.Advanced));
            list.AddRange(HighDefenses.Where(c => c.rarity == CardRarity.Advanced));
            return list.ToArray();
        }
    }

    public static CombatCardData[] LegendaryCards => new CombatCardData[0];

    // ── TFT RARITY PROGRESSION ─────────────────────────────────────────────
    public static void GetRarityChances(int roundNumber, out float basicChance, out float advancedChance, out float legendaryChance)
    {
        switch (roundNumber)
        {
            case 1: case 2:
                basicChance = 0.90f; advancedChance = 0.10f; legendaryChance = 0.00f; break;
            case 3: case 4:
                basicChance = 0.70f; advancedChance = 0.28f; legendaryChance = 0.02f; break;
            case 5: case 6:
                basicChance = 0.50f; advancedChance = 0.40f; legendaryChance = 0.10f; break;
            case 7: case 8:
                basicChance = 0.30f; advancedChance = 0.45f; legendaryChance = 0.25f; break;
            case 9:
                basicChance = 0.15f; advancedChance = 0.35f; legendaryChance = 0.50f; break;
            default:
                basicChance = 0.15f; advancedChance = 0.35f; legendaryChance = 0.50f; break;
        }
    }

    void Awake() { if (Instance == null) Instance = this; }
}
