using UnityEngine;
using System.Collections.Generic;

public class CardDatabase : MonoBehaviour
{
    public static CardDatabase Instance { get; private set; }

    // ── BASIC (1 cost) ─────────────────────────────────────────────────────
    public static readonly CombatCardData[] BasicCards = new CombatCardData[]
    {
        new CombatCardData { cardId="jab", displayName="Jab", rarity=CardRarity.Basic, type=CardType.Attack, cost=1, timingWindow=0.30f, damagePercent="+8%", description="Fast low hit. COUNTERS: Dodge. BEATEN BY: Cross, Hook, Block. Safe opener.", triggerName="Jab" },
        new CombatCardData { cardId="cross", displayName="Cross", rarity=CardRarity.Basic, type=CardType.Attack, cost=1, timingWindow=0.30f, damagePercent="+12%", description="Straight power hit. COUNTERS: Jab, Dodge. BEATEN BY: Hook, Block, Clutch.", triggerName="Cross" },
        new CombatCardData { cardId="hook", displayName="Hook", rarity=CardRarity.Basic, type=CardType.Attack, cost=1, timingWindow=0.30f, damagePercent="+15%", description="Wide heavy hit. COUNTERS: Cross, Jab. BEATEN BY: Clutch, Block, Grapple interrupt. TRIGGERS Clutch.", triggerName="Hook" },
        new CombatCardData { cardId="block", displayName="Block", rarity=CardRarity.Basic, type=CardType.Defense, cost=1, timingWindow=0.40f, damagePercent="+2%", description="Reduces all incoming damage by ~50%. COUNTERS: Jab, Cross. BEATEN BY: Grapple (breaks through). Timing bonus reduces damage further.", triggerName="Block" },
        new CombatCardData { cardId="dodge_left", displayName="Dodge Left", rarity=CardRarity.Basic, type=CardType.Defense, cost=1, timingWindow=0.30f, damagePercent="0%", description="Sidestep left — nullifies most straight hits. COUNTERS: Jab, Cross. BEATEN BY: Hook, Sweep, Grapple.", triggerName="Left" },
        new CombatCardData { cardId="dodge_right", displayName="Dodge Right", rarity=CardRarity.Basic, type=CardType.Defense, cost=1, timingWindow=0.30f, damagePercent="0%", description="Sidestep right — nullifies most straight hits. COUNTERS: Jab, Cross. BEATEN BY: Hook, Sweep, Grapple.", triggerName="Right" },
        new CombatCardData { cardId="reflect", displayName="Reflect", rarity=CardRarity.Basic, type=CardType.Defense, cost=1, timingWindow=0.30f, damagePercent="+10% to attacker", description="Returns 10% damage to attacker on hit. COUNTERS: any attack. BEATEN BY: Grapple, Boom (reflect doesn't fully negate). Good timing = 50% block.", triggerName="ParryIntent" },
        new CombatCardData { cardId="boom", displayName="Boom", rarity=CardRarity.Basic, type=CardType.Attack, cost=1, timingWindow=0.30f, damagePercent="+25%", description="Explosive hit, high damage. COUNTERS: Dodge (wide arc). BEATEN BY: Clutch (perfect), Block. TRIGGERS Clutch.", triggerName="UnbreakablePunch" },
        new CombatCardData { cardId="grapple", displayName="Grapple", rarity=CardRarity.Basic, type=CardType.Attack, cost=1, timingWindow=0.30f, damagePercent="+18%", description="Breaks through Block and Dodge. COUNTERS: Block, Dodge Left, Dodge Right. BEATEN BY: Fake, Uppercut interrupt.", triggerName="Grapple" },
        new CombatCardData { cardId="fake", displayName="Fake", rarity=CardRarity.Basic, type=CardType.Attack, cost=1, timingWindow=0.30f, damagePercent="+5%", description="Bait the opponent. COUNTERS: Grapple (dodge at last second). BEATEN BY: Jab, Cross (not fooled).", triggerName="Fake" },
    };

    // ── ADVANCED (2 cost) ──────────────────────────────────────────────────
    public static readonly CombatCardData[] AdvancedCards = new CombatCardData[]
    {
        new CombatCardData { cardId="uppercut", displayName="Uppercut", rarity=CardRarity.Advanced, type=CardType.Attack, cost=2, timingWindow=0.30f, damagePercent="+20%", description="Rising hit that catches dodgers and grapplers. COUNTERS: Dodge Left, Dodge Right, Grapple. BEATEN BY: Block, Cross.", triggerName="Uppercut" },
        new CombatCardData { cardId="sweep", displayName="Sweep", rarity=CardRarity.Advanced, type=CardType.Attack, cost=2, timingWindow=0.30f, damagePercent="+16%", description="Low kick that goes under Block entirely. COUNTERS: Block. BEATEN BY: Dodge, Cross. Catches turtling players.", triggerName="Sweep" },
        new CombatCardData { cardId="focus", displayName="Focus", rarity=CardRarity.Advanced, type=CardType.Defense, cost=2, timingWindow=0.30f, damagePercent="0%", description="Charge up. Your NEXT attack deals +50% damage. Safe setup — no self-damage. BEATEN BY: Boom (breaks Focus). Best paired with Hook or Boom.", triggerName="Focus" },
        new CombatCardData { cardId="taunt", displayName="Taunt", rarity=CardRarity.Advanced, type=CardType.Defense, cost=2, timingWindow=0.35f, damagePercent="+3% self", description="Forces opponent into attack-only on the next beat — no Block, Dodge, or defense. RISK: you take +3% self-damage and eat whatever attack they throw.", triggerName="Taunt" },
    };

    // ── LEGENDARY (3 cost) ─────────────────────────────────────────────────
    public static readonly CombatCardData[] LegendaryCards = new CombatCardData[]
    {
        new CombatCardData { cardId="overclock", displayName="Overclock", rarity=CardRarity.Legendary, type=CardType.Attack, cost=3, timingWindow=0.30f, damagePercent="+35% opp / +10% self", description="Overclock your next attack: +35% damage, +10% self-damage risk. COUNTERS: anything — amplifies your next hit. Use before a big attack.", triggerName="Overclock" },
        new CombatCardData { cardId="reverse", displayName="Reverse", rarity=CardRarity.Legendary, type=CardType.Attack, cost=3, timingWindow=0.30f, damagePercent="0% up to +30", description="Redirect up to 30% of incoming damage back to attacker. COUNTERS: heavy attacks. BEATEN BY: Fake (no damage to reflect).", triggerName="Reverse" },
        new CombatCardData { cardId="trap", displayName="Trap", rarity=CardRarity.Legendary, type=CardType.Defense, cost=3, timingWindow=0.30f, damagePercent="15%", description="Hidden delayed hit triggers on opponent's next attack. COUNTERS: aggressive players. BEATEN BY: defensive play (they can dodge/block trigger).", triggerName="Trap" },
        new CombatCardData { cardId="cage", displayName="Cage", rarity=CardRarity.Legendary, type=CardType.Defense, cost=2, timingWindow=0.30f, damagePercent="10%", description="Prevents opponent using defense on next beat. COUNTERS: Block, Dodge. BEATEN BY: offensive counter before cage activates.", triggerName="Cage" },
        new CombatCardData { cardId="mirror", displayName="Mirror", rarity=CardRarity.Legendary, type=CardType.Defense, cost=3, timingWindow=0.30f, damagePercent="+15% to attacker", description="Copy opponent's move and return it. COUNTERS: attack-heavy players. BEATEN BY: Fake (nothing to copy).", triggerName="Mirror" },
        new CombatCardData { cardId="clutch", displayName="Clutch", rarity=CardRarity.Legendary, type=CardType.Defense, cost=3, timingWindow=0.30f, damagePercent="0% or +30%", description="HIGH RISK. Perfect timing nullifies Boom/Hook and reflects +15% back. Good timing = full block, no reflect. Miss = +20% self-damage. ONCE PER ROUND. COUNTERS: Boom, Hook. BEATEN BY: Jab, Cross, Grapple.", triggerName="Clutch" },
    };

    // ── VEX CARDS ──────────────────────────────────────────────────────────
    public static readonly VexCardData[] VexCards = new VexCardData[]
    {
        new VexCardData { cardId="striker", displayName="Striker", type=CardType.Meta, cost=2, effect="+15% all attack damage for 2 consecutive turns", description="PLAY AS DEFENSE to activate. Boost all attack damage +15% for 2 beats. Best with Boom or Hook." },
        new VexCardData { cardId="tank", displayName="Tank", type=CardType.Meta, cost=2, effect="-25% damage taken for 2 consecutive turns", description="PLAY AS DEFENSE to activate. Take -25% damage for 2 beats. Hard counter to burst combos." },
        new VexCardData { cardId="speedster", displayName="Speedster", type=CardType.Meta, cost=2, effect="+20% timing window, -8% damage for 2 consecutive turns", description="PLAY AS DEFENSE to activate. Timing windows +20% for 2 beats, damage -8%. Great for risky cards." },
        new VexCardData { cardId="grappler", displayName="Grappler", type=CardType.Meta, cost=2, effect="Grapple +30% damage, next 2 times its used", description="PLAY AS DEFENSE to activate. Your next 2 Grapples deal +30% damage. Combo with Cage to force defends." },
        new VexCardData { cardId="trickster", displayName="Trickster", type=CardType.Meta, cost=3, effect="Fake/Taunt/Trap effects doubled for next time use only", description="PLAY AS DEFENSE to activate. Next use of Fake/Trap has doubled effect. One-time burst." },
        new VexCardData { cardId="vampire", displayName="Vampire", type=CardType.Meta, cost=4, effect="Heal 5% on every successful hit throughout the round", description="PLAY AS DEFENSE to activate. Heal 5% HP on every successful hit this round. Snowballs with combos." },
        new VexCardData { cardId="glass", displayName="Glass", type=CardType.Meta, cost=3, effect="40% all damage, 20% damage taken throughout the round", description="PLAY AS DEFENSE to activate. All your damage +40%, damage taken +20% for the round. High risk." },
        new VexCardData { cardId="momentum", displayName="Momentum", type=CardType.Meta, cost=5, effect="Consecutive hits increase damage by 10% per hit", description="PLAY AS DEFENSE to activate. Each consecutive hit this round adds +10% damage. Never stop attacking." },
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
