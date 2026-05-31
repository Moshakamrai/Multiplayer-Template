using UnityEngine;
using System.Collections.Generic;

public class CardDatabase : MonoBehaviour
{
    public static CardDatabase Instance { get; private set; }

    // ── BASIC (1 cost) ─────────────────────────────────────────────────────
    public static readonly CombatCardData[] BasicCards = new CombatCardData[]
    {
        new CombatCardData { cardId="jab", displayName="Jab", rarity=CardRarity.Basic, type=CardType.Attack, family=CardFamily.Strike, cost=1, timingWindow=0.30f, damagePercent="+8%", description="STRIKE — fast, light hit. Beats Throws, stopped by Block & Parry. Shout on the beat for bonus damage.", triggerName="Jab" },
        new CombatCardData { cardId="cross", displayName="Cross", rarity=CardRarity.Basic, type=CardType.Attack, family=CardFamily.Strike, cost=1, timingWindow=0.30f, damagePercent="+12%", description="STRIKE — solid mid hit. Beats Throws, stopped by guards. Shout on the beat to power it up.", triggerName="Cross" },
        new CombatCardData { cardId="hook", displayName="Hook", rarity=CardRarity.Basic, type=CardType.Attack, family=CardFamily.Strike, cost=1, timingWindow=0.30f, damagePercent="+15%", description="STRIKE — heavy swing. Beats Throws, loses to guards. Shout on the beat for a big hit.", triggerName="Hook" },
        new CombatCardData { cardId="block", displayName="Block", rarity=CardRarity.Basic, type=CardType.Defense, family=CardFamily.Block, cost=1, timingWindow=0.40f, damagePercent="+2%", description="BLOCK — safe guard. Stops Strikes, loses to Throws. No shout needed; good timing reduces damage further.", triggerName="Block" },
        new CombatCardData { cardId="dodge_left", displayName="Dodge Left", rarity=CardRarity.Basic, type=CardType.Defense, family=CardFamily.Block, cost=1, timingWindow=0.30f, damagePercent="0%", description="BLOCK — slip left. Evades Strikes, loses to Throws.", triggerName="Left" },
        new CombatCardData { cardId="dodge_right", displayName="Dodge Right", rarity=CardRarity.Basic, type=CardType.Defense, family=CardFamily.Block, cost=1, timingWindow=0.30f, damagePercent="0%", description="BLOCK — slip right. Evades Strikes, loses to Throws.", triggerName="Right" },
        new CombatCardData { cardId="reflect", displayName="Reflect", rarity=CardRarity.Basic, type=CardType.Defense, family=CardFamily.Parry, cost=1, timingWindow=0.30f, damagePercent="+10% to attacker", description="PARRY — shout on the exact beat to nullify a Strike and counter for damage. Loses to Throws.", triggerName="ParryIntent" },
        new CombatCardData { cardId="boom", displayName="Boom", rarity=CardRarity.Basic, type=CardType.Attack, family=CardFamily.Strike, cost=1, timingWindow=0.30f, damagePercent="+25%", description="STRIKE — huge, slow haymaker. Beats Throws, loses to guards. Shout on the beat to confirm the hit.", triggerName="UnbreakablePunch" },
        new CombatCardData { cardId="grapple", displayName="Grapple", rarity=CardRarity.Basic, type=CardType.Attack, family=CardFamily.Throw, cost=1, timingWindow=0.30f, damagePercent="+18%", description="THROW — grab through any guard. Beats Block & Parry, loses to Strikes. Shout on the beat to land it.", triggerName="Grapple" },
        new CombatCardData { cardId="fake", displayName="Fake", rarity=CardRarity.Basic, type=CardType.Attack, family=CardFamily.Throw, cost=1, timingWindow=0.30f, damagePercent="+5%", description="THROW — feint that cracks defense. Beats Block & Parry, loses to Strikes. Shout on the beat.", triggerName="Fake" },
    };

    // ── ADVANCED (2 cost) ──────────────────────────────────────────────────
    public static readonly CombatCardData[] AdvancedCards = new CombatCardData[]
    {
        new CombatCardData { cardId="uppercut", displayName="Uppercut", rarity=CardRarity.Advanced, type=CardType.Attack, family=CardFamily.Strike, cost=2, timingWindow=0.30f, damagePercent="+20%", description="STRIKE — rising hit. Beats Throws, stopped by guards. Shout on the beat for a counter-hit.", triggerName="Uppercut" },
        new CombatCardData { cardId="sweep", displayName="Sweep", rarity=CardRarity.Advanced, type=CardType.Attack, family=CardFamily.Throw, cost=2, timingWindow=0.30f, damagePercent="+16%", description="THROW — low hit that goes under guards. Beats Block & Parry, loses to Strikes. Shout on the beat.", triggerName="Sweep" },
        new CombatCardData { cardId="focus", displayName="Focus", rarity=CardRarity.Advanced, type=CardType.Defense, family=CardFamily.Support, cost=2, timingWindow=0.30f, damagePercent="0%", description="SUPPORT — charge up. Your next Strike deals +50% damage. No counter, just setup. Best before Hook or Boom.", triggerName="Focus" },
        new CombatCardData { cardId="taunt", displayName="Taunt", rarity=CardRarity.Advanced, type=CardType.Defense, family=CardFamily.Support, cost=2, timingWindow=0.35f, damagePercent="+3% self", description="SUPPORT — force the opponent into attack-only on the next beat (no defense). Risk: you take +3% self-damage.", triggerName="Taunt" },
    };

    // ── LEGENDARY (3 cost) ─────────────────────────────────────────────────
    public static readonly CombatCardData[] LegendaryCards = new CombatCardData[]
    {
        new CombatCardData { cardId="overclock", displayName="Overclock", rarity=CardRarity.Legendary, type=CardType.Attack, family=CardFamily.Strike, cost=3, timingWindow=0.30f, damagePercent="+35% opp / +10% self", description="STRIKE — unstoppable heavy hit. Can't be interrupted, beats Throws. Huge damage but +10% self-damage.", triggerName="Overclock" },
        new CombatCardData { cardId="reverse", displayName="Reverse", rarity=CardRarity.Legendary, type=CardType.Attack, family=CardFamily.Parry, cost=3, timingWindow=0.30f, damagePercent="0% up to +30", description="PARRY — nullify a Strike and return up to 30% of its damage. Shout on the beat. Loses to Throws.", triggerName="Reverse" },
        new CombatCardData { cardId="trap", displayName="Trap", rarity=CardRarity.Legendary, type=CardType.Defense, family=CardFamily.Support, cost=3, timingWindow=0.30f, damagePercent="15%", description="SUPPORT — set a hidden hit that punishes the opponent's next move. Rewards reading aggression.", triggerName="Trap" },
        new CombatCardData { cardId="cage", displayName="Cage", rarity=CardRarity.Legendary, type=CardType.Defense, family=CardFamily.Support, cost=2, timingWindow=0.30f, damagePercent="10%", description="SUPPORT — lock the opponent out of defending on the next beat. Forces them to eat a hit.", triggerName="Cage" },
        new CombatCardData { cardId="mirror", displayName="Mirror", rarity=CardRarity.Legendary, type=CardType.Defense, family=CardFamily.Parry, cost=3, timingWindow=0.30f, damagePercent="+15% to attacker", description="PARRY — reflect the next Strike back with +15% bonus. Shout on the beat. Loses to Throws.", triggerName="Mirror" },
        new CombatCardData { cardId="clutch", displayName="Clutch", rarity=CardRarity.Legendary, type=CardType.Defense, family=CardFamily.Parry, cost=3, timingWindow=0.30f, damagePercent="0% or +30%", description="PARRY — high-risk read on heavy Strikes. Perfect beat = nullify + big counter; miss = +20% self-damage. ONCE PER ROUND. Loses to Throws.", triggerName="Clutch" },
    };

    // ── TRAIT CARDS (passive effects for the entire round) ────────────────
    public static readonly TraitCardData[] TraitCards = new TraitCardData[]
    {
        // Offense Traits
        new TraitCardData { traitId="piercing",    displayName="Piercing",    cost=3, effect="Ignore 25% of opponent's Block defense",                     description="Defense becomes less effective. Chip damage through blocks becomes real damage." },
        new TraitCardData { traitId="bloodlust",   displayName="Bloodlust",   cost=3, effect="Each consecutive hit gains +6% damage (stacks, resets on miss)", description="Rewards accuracy. Build up momentum with each successful hit." },
        new TraitCardData { traitId="executioner", displayName="Executioner", cost=3, effect="+35% damage when opponent is above 65% health",               description="Finish them fast before they recover. Press the advantage early." },
        new TraitCardData { traitId="momentum",    displayName="Momentum",    cost=4, effect="Consecutive hits increase damage by 10% each (max +40%)",     description="Combo snowball trait. Each hit makes the next hit deadlier." },
        new TraitCardData { traitId="fury",        displayName="Fury",        cost=3, effect="+15% all attack damage for the entire round",                 description="Pure aggression. Every hit you land this round deals extra damage." },
        new TraitCardData { traitId="glass",       displayName="Glass Cannon",cost=4, effect="+40% attack damage, +20% damage taken for the round",         description="All-in offense. You hit harder but you break easier. High risk, high reward." },
        new TraitCardData { traitId="vampire",     displayName="Vampire",     cost=4, effect="Heal 5% HP on every successful hit this round",               description="Sustain through offense. Land hits to stay healthy." },
        new TraitCardData { traitId="grappler",    displayName="Grappler",    cost=3, effect="Grapple deals +30% extra damage this round",                  description="Specialise in the clinch. Grapple becomes a serious threat." },

        // Defense Traits
        new TraitCardData { traitId="anchored",    displayName="Anchored",    cost=4, effect="Reduce stagger buildup by 40%",                              description="Stay in the fight longer. Takes more hits to knock you down." },
        new TraitCardData { traitId="fortress",    displayName="Fortress",    cost=4, effect="Reduce all damage taken by 18% for the round",               description="Tank trait. You're harder to hurt. Great for sustain builds." },
        new TraitCardData { traitId="stalwart",    displayName="Stalwart",    cost=4, effect="Each successful block increases your next attack by 10% (max +50%)", description="Defensive counter. Turn blocks into bigger hits." },
        new TraitCardData { traitId="regenerate",  displayName="Regenerate",  cost=3, effect="Heal 4% health every beat during the round",                 description="Passive healing. Survive long enough and outlast your opponent." },

        // Utility Traits
        new TraitCardData { traitId="quicktrigger",displayName="Quicktrigger",cost=3, effect="Timing window +0.1s for all cards this round",               description="Easier execution. More time to hit the sweet spot on every card." },
        new TraitCardData { traitId="echo",        displayName="Echo",        cost=5, effect="25% chance any used card refreshes and can be used again",   description="Card duplication. Sometimes your moves come back for free." },
        new TraitCardData { traitId="trickster",   displayName="Trickster",   cost=3, effect="Fake, Taunt, and Trap effects are doubled this round",        description="Mind-game specialist. Your deceptive cards hit twice as hard." },
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

    // ── LOOKUP HELPERS ─────────────────────────────────────────────────────
    public static CombatCardData GetCombatCard(string cardId)
    {
        foreach (var c in BasicCards)     if (c.cardId == cardId) return c;
        foreach (var c in AdvancedCards)  if (c.cardId == cardId) return c;
        foreach (var c in LegendaryCards) if (c.cardId == cardId) return c;
        return null;
    }

    public static CardFamily GetCardFamily(string cardId)
    {
        var c = GetCombatCard(cardId);
        return c != null ? c.family : CardFamily.Support;
    }

    void Awake() { if (Instance == null) Instance = this; }
}
