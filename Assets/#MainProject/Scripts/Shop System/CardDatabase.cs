using UnityEngine;
using System.Collections.Generic;

public class CardDatabase : MonoBehaviour
{
    public static CardDatabase Instance { get; private set; }

    // ── BASIC (1 cost) ─────────────────────────────────────────────────────
    public static readonly CombatCardData[] BasicCards = new CombatCardData[]
    {
        new CombatCardData { cardId="jab", displayName="Jab", rarity=CardRarity.Basic, type=CardType.Attack, cost=1, timingWindow=0.30f, damagePercent="+8%", description="Fast, reliable poke. Standard light attack with forgiving timing window. Beats Cross, loses to Hook and Boom.", triggerName="Jab" },
        new CombatCardData { cardId="cross", displayName="Cross", rarity=CardRarity.Basic, type=CardType.Attack, cost=1, timingWindow=0.30f, damagePercent="+12%", description="Standard mid attack. Beats Hook and Block and movement (left, right), loses to Jab and Boom. Reliable damage dealer.", triggerName="Cross" },
        new CombatCardData { cardId="hook", displayName="Hook", rarity=CardRarity.Basic, type=CardType.Attack, cost=1, timingWindow=0.30f, damagePercent="+15%", description="Strong close-range attack. Beats Jab and Block, loses to Cross and Boom. High damage for timing.", triggerName="Hook" },
        new CombatCardData { cardId="block", displayName="Block", rarity=CardRarity.Basic, type=CardType.Defense, cost=1, timingWindow=0.30f, damagePercent="+2%", description="Chip damage only. Blocks Jab, Cross, Hook. Loses to Grapple, Fake, Sweep. Longest window.", triggerName="Block" },
        new CombatCardData { cardId="dodge_left", displayName="Dodge Left", rarity=CardRarity.Basic, type=CardType.Defense, cost=1, timingWindow=0.30f, damagePercent="0%", description="Sidestep left. Evades Jab, Cross, Boom. Loses to Hook, Grapple, Uppercut.", triggerName="Left" },
        new CombatCardData { cardId="dodge_right", displayName="Dodge Right", rarity=CardRarity.Basic, type=CardType.Defense, cost=1, timingWindow=0.30f, damagePercent="0%", description="Sidestep right. Same properties as Dodge Left. Mirror option for mind games.", triggerName="Right" },
        new CombatCardData { cardId="reflect", displayName="Reflect", rarity=CardRarity.Basic, type=CardType.Defense, cost=1, timingWindow=0.30f, damagePercent="+10% to attacker", description="Perfect timing reflect. Nullifies attack and deals 10% back. Beats Jab/Cross/Hook, loses to Boom/Grapple/Fake.", triggerName="ParryIntent" },
        new CombatCardData { cardId="boom", displayName="Boom", rarity=CardRarity.Basic, type=CardType.Attack, cost=1, timingWindow=0.30f, damagePercent="+25%", description="Slow but devastating heavy attack. Beats Jab/Cross/Hook. Loses to Block, Parry, Clutch. Highest base damage.", triggerName="UnbreakablePunch" },
        new CombatCardData { cardId="grapple", displayName="Grapple", rarity=CardRarity.Basic, type=CardType.Attack, cost=1, timingWindow=0.30f, damagePercent="+18%", description="Command grab. Blocks Block and Dodge. Loses to Jab, Cross, Uppercut. Bypasses defenses.", triggerName="Grapple" },
        new CombatCardData { cardId="fake", displayName="Fake", rarity=CardRarity.Basic, type=CardType.Attack, cost=1, timingWindow=0.30f, damagePercent="+5%", description="Cancels opponent defense. Beats Block and Parry. Loses to all attacks. Mind game tool.", triggerName="Fake" },
        new CombatCardData { cardId="clutch", displayName="Clutch", rarity=CardRarity.Basic, type=CardType.Defense, cost=1, timingWindow=0.30f, damagePercent="0% or +30%", description="HIGH RISK. Nullify heavy attack (Boom/Hook) on perfect timing, reflect +15% to attacker. Miss timing = +20% self-damage. ONCE PER ROUND.", triggerName="Clutch" },
    };

    // ── ADVANCED (2 cost) ──────────────────────────────────────────────────
    public static readonly CombatCardData[] AdvancedCards = new CombatCardData[]
    {
        new CombatCardData { cardId="uppercut", displayName="Uppercut", rarity=CardRarity.Advanced, type=CardType.Attack, cost=2, timingWindow=0.30f, damagePercent="+20%", description="Anti-dodge attack. Beats Dodge and Grapple. Loses to Block and Cross. Punishes evasive play.", triggerName="Uppercut" },
        new CombatCardData { cardId="sweep", displayName="Sweep", rarity=CardRarity.Advanced, type=CardType.Attack, cost=2, timingWindow=0.30f, damagePercent="+16%", description="Low attack. Beats Block. Loses to Dodge and Cross. Catches defensive players.", triggerName="Sweep" },
        new CombatCardData { cardId="focus", displayName="Focus", rarity=CardRarity.Advanced, type=CardType.Defense, cost=2, timingWindow=0.30f, damagePercent="0%", description="Charge up. Next attack deals +50% damage. No self-damage. Setup for big punish.", triggerName="Focus" },
        new CombatCardData { cardId="taunt", displayName="Taunt", rarity=CardRarity.Advanced, type=CardType.Defense, cost=2, timingWindow=0.35f, damagePercent="+3% self", description="Force opponent to use only Attack Cards next turn. Loses to all attacks.", triggerName="Taunt" },
    };

    // ── LEGENDARY (3 cost) ─────────────────────────────────────────────────
    public static readonly CombatCardData[] LegendaryCards = new CombatCardData[]
    {
        new CombatCardData { cardId="overclock", displayName="Overclock", rarity=CardRarity.Legendary, type=CardType.Attack, cost=3, timingWindow=0.30f, damagePercent="+35% opp / +10% self", description="High damage rush. Deals +35% to opponent but +10% self-damage. Glass cannon option.", triggerName="Overclock" },
        new CombatCardData { cardId="reverse", displayName="Reverse", rarity=CardRarity.Legendary, type=CardType.Attack, cost=3, timingWindow=0.30f, damagePercent="0% up to +30", description="Completely negates all damage this turn and returns it back to opponent if the timing is at least good.", triggerName="Reverse" },
        new CombatCardData { cardId="trap", displayName="Trap", rarity=CardRarity.Legendary, type=CardType.Defense, cost=3, timingWindow=0.30f, damagePercent="15%", description="Set trap. If the opponent moves left or right or blocks it takes damage.", triggerName="Trap" },
        new CombatCardData { cardId="cage", displayName="Cage", rarity=CardRarity.Legendary, type=CardType.Defense, cost=2, timingWindow=0.30f, damagePercent="10%", description="Trap opponent. If the opponent plays a card next turn it damages them.", triggerName="Cage" },
        new CombatCardData { cardId="mirror", displayName="Mirror", rarity=CardRarity.Legendary, type=CardType.Defense, cost=3, timingWindow=0.30f, damagePercent="+15% to attacker", description="Returns next attack damage +15% bonus. Beats Jab/Cross/Hook. Loses to Boom/Grapple.", triggerName="Mirror" },
    };

    // ── VEX CARDS ──────────────────────────────────────────────────────────
    public static readonly VexCardData[] VexCards = new VexCardData[]
    {
        new VexCardData { cardId="striker", displayName="Striker", type=CardType.Meta, cost=2, effect="+15% all attack damage for 2 consecutive turns", description="Default aggressive playstyle. Pure damage boost. Simple and effective." },
        new VexCardData { cardId="tank", displayName="Tank", type=CardType.Meta, cost=2, effect="-25% damage taken for 2 consecutive turns", description="Defensive fortress. Slower timing windows but survives longer. Good for percentage wars." },
        new VexCardData { cardId="speedster", displayName="Speedster", type=CardType.Meta, cost=2, effect="+20% timing window, -8% damage for 2 consecutive turns", description="Easier execution, less power. For players who want consistent hits over big damage." },
        new VexCardData { cardId="grappler", displayName="Grappler", type=CardType.Meta, cost=2, effect="Grapple +30% damage, next 2 times its used", description="Command grab focus. Makes Grapple a true threat. Opponent can't dodge (move left or right)." },
        new VexCardData { cardId="trickster", displayName="Trickster", type=CardType.Meta, cost=3, effect="Fake/Taunt/Trap effects doubled for next time use only", description="Mind games amplified. Cage, Taunt, Trap effects stays one more turn." },
        new VexCardData { cardId="vampire", displayName="Vampire", type=CardType.Meta, cost=4, effect="Heal 5% on every successful hit throughout the round", description="Sustain focused. Chip away and heal back. Good for long rounds with no limit." },
        new VexCardData { cardId="glass", displayName="Glass", type=CardType.Meta, cost=3, effect="40% all damage, 20% damage taken throughout the round", description="High risk high reward. Glass cannon. Die fast or kill fast." },
        new VexCardData { cardId="momentum", displayName="Momentum", type=CardType.Meta, cost=5, effect="Consecutive hits increase damage by 10% per hit", description="Combo snowball. 1st hit normal, 2nd +10%, 3rd +20%, etc. Rewards aggression." },
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
