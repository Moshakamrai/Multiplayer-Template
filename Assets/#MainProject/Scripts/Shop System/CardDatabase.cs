using UnityEngine;
using System.Collections.Generic;

public class CardDatabase : MonoBehaviour
{
    public static CardDatabase Instance { get; private set; }

    // ── BASIC (1 cost) ─────────────────────────────────────────────────────
    public static readonly CombatCardData[] BasicCards = new CombatCardData[]
    {
        new CombatCardData {
            cardId="jab", displayName="Jab", rarity=CardRarity.Basic, type=CardType.Attack,
            family=CardFamily.Strike, cost=1, triggerName="Jab", timingWindow=0.32f,
            baseDamage=8,  lv2Damage=11, lv3Damage=14,
            lv2TimingBonus=0.04f, lv3TimingBonus=0.08f,
            damagePercent="8%",
            description="STRIKE — fastest card in the game. Beats Throws, stopped by guards. Lv3: no lockout after use.",
            perk=CardPerk.FastRedraw, perkActiveAtLv1=false, perkActiveAtLv2=false,
            perkDescription="Lv3: No cooldown — Strike slot redraws instantly after Jab lands."
        },
        new CombatCardData {
            cardId="cross", displayName="Cross", rarity=CardRarity.Basic, type=CardType.Attack,
            family=CardFamily.Strike, cost=1, triggerName="Cross", timingWindow=0.30f,
            baseDamage=12, lv2Damage=16, lv3Damage=21,
            lv2TimingBonus=0.03f, lv3TimingBonus=0.06f,
            damagePercent="12%",
            description="STRIKE — reliable mid-range punch. Beats Throws, stopped by guards. Lv2: +timing. Lv3: +counter bonus.",
            perk=CardPerk.CounterBonus, perkActiveAtLv1=false, perkActiveAtLv2=false,
            perkDescription="Lv3: If opponent attacked last beat, Cross deals +20% bonus damage."
        },
        new CombatCardData {
            cardId="hook", displayName="Hook", rarity=CardRarity.Basic, type=CardType.Attack,
            family=CardFamily.Strike, cost=1, triggerName="Hook", timingWindow=0.28f,
            baseDamage=15, lv2Damage=20, lv3Damage=26,
            lv2TimingBonus=0.03f, lv3TimingBonus=0.06f,
            damagePercent="15%",
            description="STRIKE — heavy swing. Beats Throws, stopped by guards. Lv3: staggers the opponent — they can't act next beat.",
            perk=CardPerk.Stagger, perkActiveAtLv1=false, perkActiveAtLv2=false,
            perkDescription="Lv3: A landing Hook staggers the opponent — they miss their next beat entirely."
        },
        new CombatCardData {
            cardId="block", displayName="Block", rarity=CardRarity.Basic, type=CardType.Defense,
            family=CardFamily.Block, cost=1, triggerName="Block", timingWindow=0.40f,
            baseDamage=0, lv2Damage=0, lv3Damage=0,
            lv2TimingBonus=0.05f, lv3TimingBonus=0.10f,
            damagePercent="0 (absorb)",
            description="BLOCK — safest card. Stops Strikes, breaks on Throws. Lv3: landing a Block charges your next attack +15%.",
            perk=CardPerk.BlockCounter, perkActiveAtLv1=false, perkActiveAtLv2=false,
            perkDescription="Lv3: Successfully blocking charges your next attack with +15% bonus damage."
        },
        new CombatCardData {
            cardId="dodge_left", displayName="Dodge Left", rarity=CardRarity.Basic, type=CardType.Defense,
            family=CardFamily.Block, cost=1, triggerName="Left", timingWindow=0.30f,
            baseDamage=0, lv2Damage=0, lv3Damage=0,
            lv2TimingBonus=0.04f, lv3TimingBonus=0.08f,
            damagePercent="0 (evade)",
            description="BLOCK — slip left. Evades Strikes completely, breaks on Throws. Lv3: full dodge negates ALL Strike damage.",
            perk=CardPerk.DodgeEvade, perkActiveAtLv1=false, perkActiveAtLv2=false,
            perkDescription="Lv3: If opponent threw a Strike, Dodge Left negates 100% of the damage this beat."
        },
        new CombatCardData {
            cardId="dodge_right", displayName="Dodge Right", rarity=CardRarity.Basic, type=CardType.Defense,
            family=CardFamily.Block, cost=1, triggerName="Right", timingWindow=0.30f,
            baseDamage=0, lv2Damage=0, lv3Damage=0,
            lv2TimingBonus=0.04f, lv3TimingBonus=0.08f,
            damagePercent="0 (evade)",
            description="BLOCK — slip right. Evades Strikes completely, breaks on Throws. Lv3: full dodge negates ALL Strike damage.",
            perk=CardPerk.DodgeEvade, perkActiveAtLv1=false, perkActiveAtLv2=false,
            perkDescription="Lv3: If opponent threw a Strike, Dodge Right negates 100% of the damage this beat."
        },
        new CombatCardData {
            cardId="reflect", displayName="Reflect", rarity=CardRarity.Basic, type=CardType.Defense,
            family=CardFamily.Parry, cost=1, triggerName="ParryIntent", timingWindow=0.28f,
            baseDamage=0, lv2Damage=0, lv3Damage=0,
            lv2TimingBonus=0.04f, lv3TimingBonus=0.08f,
            damagePercent="counter (10%)",
            description="PARRY — shout on the beat to reflect a Strike back. Lv3: within 0.08s of beat = full reflect + stun 1 beat.",
            perk=CardPerk.ReflectPrecision, perkActiveAtLv1=false, perkActiveAtLv2=false,
            perkDescription="Lv3: Perfect timing (within 0.08s) = 100% reflect + opponent stunned 1 beat."
        },
        new CombatCardData {
            cardId="boom", displayName="Boom", rarity=CardRarity.Basic, type=CardType.Attack,
            family=CardFamily.Strike, cost=1, triggerName="UnbreakablePunch", timingWindow=0.25f,
            baseDamage=22, lv2Damage=29, lv3Damage=37,
            lv2TimingBonus=0.03f, lv3TimingBonus=0.05f,
            damagePercent="22%",
            description="STRIKE — slow, massive haymaker. Uninterruptible. Beats Throws. Costs 5% HP to throw. Cannot be blocked.",
            perk=CardPerk.SelfCost, perkActiveAtLv1=true, perkActiveAtLv2=true,
            perkDescription="Always: costs 5% self HP to throw, but cannot be interrupted. Lv3: +0.05s timing window."
        },
        new CombatCardData {
            cardId="grapple", displayName="Grapple", rarity=CardRarity.Basic, type=CardType.Attack,
            family=CardFamily.Throw, cost=1, triggerName="Grapple", timingWindow=0.30f,
            baseDamage=18, lv2Damage=23, lv3Damage=29,
            lv2TimingBonus=0.03f, lv3TimingBonus=0.06f,
            damagePercent="18%",
            description="THROW — grabs through any guard. Beats Block & Parry, loses to Strikes. Lv3: leaves a bleed (3% HP × 2 beats).",
            perk=CardPerk.GrappleBleed, perkActiveAtLv1=false, perkActiveAtLv2=false,
            perkDescription="Lv3: Landing Grapple leaves a bleed — opponent loses 3% HP each beat for 2 beats."
        },
        new CombatCardData {
            cardId="fake", displayName="Fake", rarity=CardRarity.Basic, type=CardType.Attack,
            family=CardFamily.Throw, cost=1, triggerName="Fake", timingWindow=0.32f,
            baseDamage=7, lv2Damage=10, lv3Damage=14,
            lv2TimingBonus=0.04f, lv3TimingBonus=0.08f,
            damagePercent="7%",
            description="THROW — low damage feint, beats guards. Lv3: if opponent defended last beat, Fake deals DOUBLE damage.",
            perk=CardPerk.FakeCounter, perkActiveAtLv1=false, perkActiveAtLv2=false,
            perkDescription="Lv3: If opponent played a defensive card last beat, Fake deals ×2 damage."
        },
    };

    // ── ADVANCED (2 cost) ──────────────────────────────────────────────────
    public static readonly CombatCardData[] AdvancedCards = new CombatCardData[]
    {
        new CombatCardData {
            cardId="uppercut", displayName="Uppercut", rarity=CardRarity.Advanced, type=CardType.Attack,
            family=CardFamily.Strike, cost=2, triggerName="Uppercut", timingWindow=0.28f,
            baseDamage=20, lv2Damage=26, lv3Damage=33,
            lv2TimingBonus=0.04f, lv3TimingBonus=0.08f,
            damagePercent="20%",
            description="STRIKE — rising punch. Beats Throws, stopped by guards. Lv3: +20% dmg if opponent attacked last beat.",
            perk=CardPerk.CounterBonus, perkActiveAtLv1=false, perkActiveAtLv2=false,
            perkDescription="Lv3: If opponent attacked last beat, Uppercut deals +20% bonus damage."
        },
        new CombatCardData {
            cardId="sweep", displayName="Sweep", rarity=CardRarity.Advanced, type=CardType.Attack,
            family=CardFamily.Throw, cost=2, triggerName="Sweep", timingWindow=0.30f,
            baseDamage=16, lv2Damage=21, lv3Damage=27,
            lv2TimingBonus=0.03f, lv3TimingBonus=0.06f,
            damagePercent="16%",
            description="THROW — low sweep under guards. Beats Block & Parry, loses to Strikes. Lv3: breaks opponent's combo buffer.",
            perk=CardPerk.SweepKnockback, perkActiveAtLv1=false, perkActiveAtLv2=false,
            perkDescription="Lv3: A landing Sweep clears the opponent's queued combo moves — their next chain is wasted."
        },
        new CombatCardData {
            cardId="focus", displayName="Focus", rarity=CardRarity.Advanced, type=CardType.Defense,
            family=CardFamily.Support, cost=2, triggerName="Focus", timingWindow=0.35f,
            baseDamage=0, lv2Damage=0, lv3Damage=0,
            lv2TimingBonus=0.05f, lv3TimingBonus=0.10f,
            damagePercent="0 (buff)",
            description="SUPPORT — charge up. Next Strike +50% damage. Lv3: the buffed Strike also becomes uninterruptible.",
            perk=CardPerk.FocusBuff, perkActiveAtLv1=true, perkActiveAtLv2=true,
            perkDescription="Always: next Strike +50% dmg. Lv3: that Strike is also uninterruptible."
        },
        new CombatCardData {
            cardId="taunt", displayName="Taunt", rarity=CardRarity.Advanced, type=CardType.Defense,
            family=CardFamily.Support, cost=2, triggerName="Taunt", timingWindow=0.35f,
            baseDamage=0, lv2Damage=0, lv3Damage=0,
            lv2TimingBonus=0.05f, lv3TimingBonus=0.10f,
            damagePercent="3% self",
            description="SUPPORT — force opponent into attack-only next beat (no defense). Costs 3% self HP. Lv3: lasts 2 beats.",
            perk=CardPerk.TauntLock, perkActiveAtLv1=true, perkActiveAtLv2=true,
            perkDescription="Always: forces opponent attack-only for 1 beat. Lv3: effect lasts 2 beats."
        },
    };

    // ── LEGENDARY (3 cost) ─────────────────────────────────────────────────
    public static readonly CombatCardData[] LegendaryCards = new CombatCardData[]
    {
        new CombatCardData {
            cardId="overclock", displayName="Overclock", rarity=CardRarity.Legendary, type=CardType.Attack,
            family=CardFamily.Strike, cost=3, triggerName="Overclock", timingWindow=0.26f,
            baseDamage=30, lv2Damage=38, lv3Damage=48,
            lv2TimingBonus=0.03f, lv3TimingBonus=0.06f,
            damagePercent="30%",
            description="STRIKE — uninterruptible super hit. Beats Throws, costs 10% self HP. Lv3: self-damage reduced to 6%.",
            perk=CardPerk.Overload, perkActiveAtLv1=true, perkActiveAtLv2=true,
            perkDescription="Always: cannot be interrupted. Costs 10% self HP (Lv3: reduced to 6% self HP)."
        },
        new CombatCardData {
            cardId="reverse", displayName="Reverse", rarity=CardRarity.Legendary, type=CardType.Defense,
            family=CardFamily.Parry, cost=3, triggerName="Reverse", timingWindow=0.28f,
            baseDamage=0, lv2Damage=0, lv3Damage=0,
            lv2TimingBonus=0.04f, lv3TimingBonus=0.08f,
            damagePercent="counter (30%)",
            description="PARRY — return a Strike's full damage back at the attacker. Lv3: also heals 5% HP from the damage returned.",
            perk=CardPerk.ReverseLeech, perkActiveAtLv1=false, perkActiveAtLv2=false,
            perkDescription="Lv3: Reverse returns the Strike's damage AND heals you for 5% of that damage."
        },
        new CombatCardData {
            cardId="trap", displayName="Trap", rarity=CardRarity.Legendary, type=CardType.Defense,
            family=CardFamily.Support, cost=3, triggerName="Trap", timingWindow=0.30f,
            baseDamage=15, lv2Damage=20, lv3Damage=26,
            lv2TimingBonus=0.04f, lv3TimingBonus=0.08f,
            damagePercent="15%",
            description="SUPPORT — set a hidden hit punishing the opponent's next move. Lv3: punishes their next 2 moves.",
            perk=CardPerk.TrapPunish, perkActiveAtLv1=true, perkActiveAtLv2=true,
            perkDescription="Always: punishes opponent's next move. Lv3: punishes next 2 moves instead of 1."
        },
        new CombatCardData {
            cardId="cage", displayName="Cage", rarity=CardRarity.Legendary, type=CardType.Defense,
            family=CardFamily.Support, cost=2, triggerName="Cage", timingWindow=0.30f,
            baseDamage=10, lv2Damage=13, lv3Damage=17,
            lv2TimingBonus=0.04f, lv3TimingBonus=0.08f,
            damagePercent="10%",
            description="SUPPORT — lock opponent out of defending next beat. Forces them to eat a hit. Lv3: lasts 2 beats.",
            perk=CardPerk.CageBreak, perkActiveAtLv1=true, perkActiveAtLv2=true,
            perkDescription="Always: opponent cannot defend for 1 beat. Lv3: opponent cannot defend for 2 beats."
        },
        new CombatCardData {
            cardId="mirror", displayName="Mirror", rarity=CardRarity.Legendary, type=CardType.Defense,
            family=CardFamily.Parry, cost=3, triggerName="Mirror", timingWindow=0.28f,
            baseDamage=0, lv2Damage=0, lv3Damage=0,
            lv2TimingBonus=0.04f, lv3TimingBonus=0.08f,
            damagePercent="counter (+15%)",
            description="PARRY — reflect a Strike back with +15% bonus damage. Lv3: amplified to +25% bonus.",
            perk=CardPerk.MirrorAmplify, perkActiveAtLv1=true, perkActiveAtLv2=true,
            perkDescription="Always: reflect + 15% bonus (Lv2: +20%, Lv3: +25% bonus on top of reflected damage)."
        },
        new CombatCardData {
            cardId="clutch", displayName="Clutch", rarity=CardRarity.Legendary, type=CardType.Defense,
            family=CardFamily.Parry, cost=3, triggerName="Clutch", timingWindow=0.30f,
            baseDamage=0, lv2Damage=0, lv3Damage=0,
            lv2TimingBonus=0.04f, lv3TimingBonus=0.08f,
            damagePercent="0 or ×2 counter",
            description="PARRY — high-risk read. Within 0.1s of beat = double counter; miss = 20% self-damage. Lv3: miss penalty reduced to 10%.",
            perk=CardPerk.ClutchCritical, perkActiveAtLv1=true, perkActiveAtLv2=true,
            perkDescription="Always: perfect timing = ×2 counter; miss = 20% self-dmg. Lv3: miss penalty reduced to 10%."
        },
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
