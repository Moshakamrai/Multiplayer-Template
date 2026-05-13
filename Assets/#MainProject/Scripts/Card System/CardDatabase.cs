using System.Collections.Generic;
using UnityEngine;

public enum CardRarity { Basic, Advanced, Legendary }
public enum CardType { Attack, Defense, Meta }
public enum CharacterClass { Universal, Vex, Fury }

[System.Serializable]
public class CardData
{
    public int id;
    public string cardName;
    public string voiceCommand;
    public CardType type;
    public CardRarity rarity;
    public CharacterClass characterClass;
    public string effect;
    public int shopCost;
    public bool isStarter;
}

[System.Serializable]
public class TraitData
{
    public int id;
    public string traitName;
    public string effect;
    public int shopCost;
    public CharacterClass characterClass;
}

public static class CardDatabase
{
    public static readonly List<CardData> AllCards = new List<CardData>
    {
        // --- BASIC STARTERS (cost 1 after pre-round shop) ---
        new CardData { id=1,  cardName="JAB",          voiceCommand="Punch",          type=CardType.Attack,  rarity=CardRarity.Basic, characterClass=CharacterClass.Universal, effect="10 dmg, fast, beats Dodge",                                     shopCost=1, isStarter=true  },
        new CardData { id=2,  cardName="CROSS",         voiceCommand="Flank",          type=CardType.Attack,  rarity=CardRarity.Basic, characterClass=CharacterClass.Universal, effect="15 dmg, beats Jab/Dodge, +energy on land",                       shopCost=1, isStarter=true  },
        new CardData { id=3,  cardName="HOOK",          voiceCommand="Hook",           type=CardType.Attack,  rarity=CardRarity.Basic, characterClass=CharacterClass.Universal, effect="25 dmg, breaks Block, +1 energy",                                shopCost=1, isStarter=true  },
        new CardData { id=4,  cardName="BLOCK",         voiceCommand="Block",          type=CardType.Defense, rarity=CardRarity.Basic, characterClass=CharacterClass.Universal, effect="100% mitigation, 0.40s window",                                  shopCost=1, isStarter=true  },
        new CardData { id=5,  cardName="CAGE",          voiceCommand="Cage",           type=CardType.Defense, rarity=CardRarity.Basic, characterClass=CharacterClass.Universal, effect="120% reflect, 0.30s window, +1 energy",                          shopCost=1, isStarter=true  },
        new CardData { id=6,  cardName="LEFT",          voiceCommand="Left",           type=CardType.Defense, rarity=CardRarity.Basic, characterClass=CharacterClass.Universal, effect="Dodge left, beats Jab/Hook/Boom",                                shopCost=1, isStarter=true  },
        new CardData { id=7,  cardName="RIGHT",         voiceCommand="Right",          type=CardType.Defense, rarity=CardRarity.Basic, characterClass=CharacterClass.Universal, effect="Dodge right, beats Jab/Hook/Boom",                               shopCost=1, isStarter=true  },

        // --- BASIC NON-STARTER (pool slots 8-14) ---
        new CardData { id=8,  cardName="BOOM",          voiceCommand="Crush",          type=CardType.Attack,  rarity=CardRarity.Basic, characterClass=CharacterClass.Universal, effect="15 dmg, 0.2s window, beats all except Cage",                     shopCost=1  },
        new CardData { id=9,  cardName="FEINT",         voiceCommand="Fake",           type=CardType.Attack,  rarity=CardRarity.Basic, characterClass=CharacterClass.Universal, effect="0 dmg, forces opponent to auto-burn defense slot",                shopCost=1  },
        new CardData { id=10, cardName="GRAPPLE",       voiceCommand="Grab",           type=CardType.Attack,  rarity=CardRarity.Basic, characterClass=CharacterClass.Universal, effect="12 dmg, ignores Block & Dodge, beaten by Jab",                   shopCost=2  },
        new CardData { id=11, cardName="RIPOSTE",       voiceCommand="Strike",         type=CardType.Attack,  rarity=CardRarity.Basic, characterClass=CharacterClass.Universal, effect="20 dmg, only after successful Block, 0.25s window, free slot",   shopCost=2  },
        new CardData { id=12, cardName="BRACE",         voiceCommand="Steel",          type=CardType.Defense, rarity=CardRarity.Basic, characterClass=CharacterClass.Universal, effect="50% mitigation all damage, free slot, rooted 2 beats",           shopCost=2  },
        new CardData { id=13, cardName="COUNTER-STEP",  voiceCommand="Shift",          type=CardType.Defense, rarity=CardRarity.Basic, characterClass=CharacterClass.Universal, effect="Dodge that costs attack slot, success grants +1 defense slot",   shopCost=2  },
        new CardData { id=14, cardName="CANCEL",        voiceCommand="Cancel",         type=CardType.Meta,    rarity=CardRarity.Basic, characterClass=CharacterClass.Universal, effect="Undo last queued input (Combo Mode only)",                        shopCost=1  },

        // --- ADVANCED (10 cards, ids 15-24) ---
        new CardData { id=15, cardName="PIERCING JAB",  voiceCommand="Pierce",         type=CardType.Attack,  rarity=CardRarity.Advanced, characterClass=CharacterClass.Universal, effect="8 dmg, ignores 50% of Block mitigation",                     shopCost=2  },
        new CardData { id=16, cardName="HEAVY CROSS",   voiceCommand="Slam",           type=CardType.Attack,  rarity=CardRarity.Advanced, characterClass=CharacterClass.Universal, effect="22 dmg, 0.35s window, no energy gain",                       shopCost=2  },
        new CardData { id=17, cardName="SWEEP",         voiceCommand="Sweep",          type=CardType.Attack,  rarity=CardRarity.Advanced, characterClass=CharacterClass.Universal, effect="15 dmg, beats Dodge, loses to Block",                        shopCost=2  },
        new CardData { id=18, cardName="CHARGE PUNCH",  voiceCommand="Charge",         type=CardType.Attack,  rarity=CardRarity.Advanced, characterClass=CharacterClass.Universal, effect="0 dmg this beat, next attack +10 dmg",                       shopCost=1  },
        new CardData { id=19, cardName="QUICK BLOCK",   voiceCommand="Shield",         type=CardType.Defense, rarity=CardRarity.Advanced, characterClass=CharacterClass.Universal, effect="Block with 0.50s window, but costs 2 defense slots",          shopCost=2  },
        new CardData { id=20, cardName="REVERSAL",      voiceCommand="Turn",           type=CardType.Defense, rarity=CardRarity.Advanced, characterClass=CharacterClass.Universal, effect="If hit this beat, next beat your attack is free slot",         shopCost=2  },
        new CardData { id=21, cardName="BAIT DODGE",    voiceCommand="Fakeout",        type=CardType.Defense, rarity=CardRarity.Advanced, characterClass=CharacterClass.Universal, effect="Dodge that triggers opponent's Feint (they waste it)",         shopCost=2  },
        new CardData { id=22, cardName="CLUTCH",        voiceCommand="Clutch",         type=CardType.Meta,    rarity=CardRarity.Advanced, characterClass=CharacterClass.Universal, effect="If HP < 100, next card costs 0 slots this beat",              shopCost=2  },
        new CardData { id=23, cardName="MOMENTUM",      voiceCommand="Flow",           type=CardType.Meta,    rarity=CardRarity.Advanced, characterClass=CharacterClass.Universal, effect="Win a trade next beat +15% damage",                           shopCost=2  },
        new CardData { id=24, cardName="SILENCE",       voiceCommand="Quiet",          type=CardType.Meta,    rarity=CardRarity.Advanced, characterClass=CharacterClass.Universal, effect="Opponent's next shout has no volume bonus (Combo only)",       shopCost=2  },

        // --- LEGENDARY (6 cards, ids 25-30) ---
        new CardData { id=25, cardName="OMEGA STRIKE",  voiceCommand="Omega",          type=CardType.Attack,  rarity=CardRarity.Legendary, characterClass=CharacterClass.Universal, effect="35 dmg, costs 3 attack slots, 0.25s window",               shopCost=4  },
        new CardData { id=26, cardName="PERFECT DODGE", voiceCommand="Vanish",         type=CardType.Defense, rarity=CardRarity.Legendary, characterClass=CharacterClass.Universal, effect="Dodge with 0.40s window, beats everything except Cross",    shopCost=3  },
        new CardData { id=27, cardName="TIME WARP",     voiceCommand="Slow",           type=CardType.Meta,    rarity=CardRarity.Legendary, characterClass=CharacterClass.Universal, effect="Opponent's next beat is delayed by 0.3s (once per match)",  shopCost=3  },
        new CardData { id=28, cardName="ADRENALINE",    voiceCommand="Surge",          type=CardType.Meta,    rarity=CardRarity.Legendary, characterClass=CharacterClass.Universal, effect="For 3 beats, ignore Pressure window shrink",                shopCost=3  },
        new CardData { id=29, cardName="MIRROR",        voiceCommand="Copy",           type=CardType.Meta,    rarity=CardRarity.Legendary, characterClass=CharacterClass.Universal, effect="Copy opponent's last played card (same timing, same effect)",shopCost=3  },
        new CardData { id=30, cardName="OVERLOAD",      voiceCommand="Overload",       type=CardType.Meta,    rarity=CardRarity.Legendary, characterClass=CharacterClass.Universal, effect="Sacrifice 50 HP, next attack deals +100% damage",           shopCost=3  },

        // --- CLASS CARDS: VEX ---
        new CardData { id=101, cardName="LAG SPIKE",    voiceCommand="Lag",            type=CardType.Attack,  rarity=CardRarity.Advanced, characterClass=CharacterClass.Vex, effect="8 dmg, opponent's next beat window shrinks by 0.15s",            shopCost=2  },
        new CardData { id=102, cardName="ECHO",         voiceCommand="Echo",           type=CardType.Meta,    rarity=CardRarity.Advanced, characterClass=CharacterClass.Vex, effect="Store 1 Dead Zone shout, auto-fires next valid window",           shopCost=2  },
        new CardData { id=103, cardName="SYSTEM CRASH", voiceCommand="Crash",          type=CardType.Defense, rarity=CardRarity.Legendary, characterClass=CharacterClass.Vex, effect="Once per match, auto-escape Stagger +50 HP heal",              shopCost=3  },
        new CardData { id=104, cardName="PHANTOM HIT",  voiceCommand="Ghost",          type=CardType.Attack,  rarity=CardRarity.Advanced, characterClass=CharacterClass.Vex, effect="15 dmg, only if opponent Blocked last beat; ignores Block/timing",shopCost=2  },

        // --- CLASS CARDS: FURY ---
        new CardData { id=201, cardName="INFERNO",      voiceCommand="Burn",           type=CardType.Attack,  rarity=CardRarity.Advanced, characterClass=CharacterClass.Fury, effect="18 dmg, volume bonus applies in Single Mode (+25% max)",         shopCost=2  },
        new CardData { id=202, cardName="OVERCLOCK",    voiceCommand="Push",           type=CardType.Meta,    rarity=CardRarity.Advanced, characterClass=CharacterClass.Fury, effect="Next 3 beats, your shout window is +0.10s wider",                shopCost=2  },
        new CardData { id=203, cardName="FEEDBACK",     voiceCommand="Scream",         type=CardType.Attack,  rarity=CardRarity.Advanced, characterClass=CharacterClass.Fury, effect="8 dmg base, +5 splash damage if mic spike >0.5",                 shopCost=2  },
        new CardData { id=204, cardName="PHOENIX",      voiceCommand="Rise",           type=CardType.Defense, rarity=CardRarity.Legendary, characterClass=CharacterClass.Fury, effect="On Stagger escape, next attack deals +50% damage",             shopCost=3  },
    };

    public static readonly List<TraitData> AllTraits = new List<TraitData>
    {
        new TraitData { id=1,  traitName="NEURAL LINK",   effect="Start with +1 attack slot",                          shopCost=2, characterClass=CharacterClass.Universal },
        new TraitData { id=2,  traitName="FIREWALL",      effect="Start with +1 defense slot",                         shopCost=2, characterClass=CharacterClass.Universal },
        new TraitData { id=3,  traitName="OVERCLOCKER",   effect="Pressure decays 2x faster",                          shopCost=3, characterClass=CharacterClass.Universal },
        new TraitData { id=4,  traitName="COLD BLOOD",    effect="Block window 0.50s (from 0.40s)",                    shopCost=2, characterClass=CharacterClass.Universal },
        new TraitData { id=5,  traitName="GAMBLER",       effect="20% chance attacks cost 0 slots (proc per beat)",    shopCost=3, characterClass=CharacterClass.Universal },
        new TraitData { id=6,  traitName="MARATHON",      effect="Stagger bar fills 30% faster",                       shopCost=2, characterClass=CharacterClass.Universal },
        new TraitData { id=7,  traitName="ECHO CHAMBER",  effect="Echo stores 2 shouts instead of 1",                  shopCost=3, characterClass=CharacterClass.Vex       },
        new TraitData { id=8,  traitName="PYROMANIAC",    effect="Volume bonus cap raised to +35%",                    shopCost=3, characterClass=CharacterClass.Fury      },
        new TraitData { id=9,  traitName="GLITCH SKIN",   effect="15% chance to ignore interrupt damage",              shopCost=3, characterClass=CharacterClass.Vex       },
        new TraitData { id=10, traitName="INFERNO CORE",  effect="Burn effects last +1 beat",                          shopCost=2, characterClass=CharacterClass.Fury      },
    };

    // Helper pools
    public static List<CardData> GetBasicPool()
    {
        var pool = new List<CardData>();
        foreach (var c in AllCards)
            if (c.rarity == CardRarity.Basic && c.characterClass == CharacterClass.Universal)
                pool.Add(c);
        return pool;
    }

    public static List<CardData> GetAdvancedPool()
    {
        var pool = new List<CardData>();
        foreach (var c in AllCards)
            if (c.rarity == CardRarity.Advanced && c.characterClass == CharacterClass.Universal)
                pool.Add(c);
        return pool;
    }

    public static List<CardData> GetLegendaryPool()
    {
        var pool = new List<CardData>();
        foreach (var c in AllCards)
            if (c.rarity == CardRarity.Legendary && c.characterClass == CharacterClass.Universal)
                pool.Add(c);
        return pool;
    }

    public static List<CardData> GetClassCards(CharacterClass cls)
    {
        var pool = new List<CardData>();
        foreach (var c in AllCards)
            if (c.characterClass == cls)
                pool.Add(c);
        return pool;
    }

    public static List<TraitData> GetAvailableTraits(CharacterClass cls)
    {
        var pool = new List<TraitData>();
        foreach (var t in AllTraits)
            if (t.characterClass == CharacterClass.Universal || t.characterClass == cls)
                pool.Add(t);
        return pool;
    }

    public static CardData GetCardById(int id)
    {
        foreach (var c in AllCards)
            if (c.id == id) return c;
        return null;
    }

    public static TraitData GetTraitById(int id)
    {
        foreach (var t in AllTraits)
            if (t.id == id) return t;
        return null;
    }

    // Starter card IDs (JAB=1, CROSS=2, HOOK=3, BLOCK=4, CAGE=5, LEFT=6, RIGHT=7)
    public static readonly int[] StarterCardIds = { 1, 2, 3, 4, 5, 6, 7 };

    // The 6 pre-round picker options (punch, flank, hook, block, left, right)
    // LEFT and RIGHT award both cards if picked
    public static readonly int[] PreRoundPickerIds = { 1, 2, 3, 4, 6, 7 };
}
