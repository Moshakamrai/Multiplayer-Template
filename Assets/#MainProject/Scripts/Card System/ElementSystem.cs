using UnityEngine;

/// The five combat elements. Each card family carries one, permanently:
///   Strike = Fire, Throw = Lightning, Parry = Water, Block = Earth, Support = Wind.
///
/// THE WHEEL (one rule to learn):  Fire → Lightning → Water → Earth → Wind → Fire
/// Each element's status is DETONATED by the next element in the wheel.
///
/// Statuses (applied to the opponent when your move succeeds at its job):
///   Fire      → BURN    damage over time
///   Lightning → SHOCK   timing windows shrink (harder to hit clean)
///   Water     → CHILL   outgoing damage reduced (numbed arms)
///   Earth     → ROOT    dodges fail
///   Wind      → EXPOSE  incoming damage increased
///
/// Reactions (land the detonator element on a statused fighter):
///   BURN   + Lightning → FIRESTORM    burst damage
///   SHOCK  + Water     → ELECTROCUTE  burst damage on the reflect
///   CHILL  + Earth     → SHATTER      stagger (skip a beat)
///   ROOT   + Wind      → EROSION      damage + converts to EXPOSE
///   EXPOSE + Fire      → COMBUST      the triggering hit deals bonus damage
public enum Element
{
    None      = 0,
    Fire      = 1, // Strike
    Lightning = 2, // Throw
    Water     = 3, // Parry
    Earth     = 4, // Block (and dodges)
    Wind      = 5, // Support
}

public static class Elements
{
    /// Permanent family → element mapping.
    public static Element OfFamily(CardFamily fam) => fam switch
    {
        CardFamily.Strike  => Element.Fire,
        CardFamily.Throw   => Element.Lightning,
        CardFamily.Parry   => Element.Water,
        CardFamily.Block   => Element.Earth,
        CardFamily.Support => Element.Wind,
        _                  => Element.None,
    };

    /// The wheel: which element DETONATES this element's status into a reaction.
    /// Fire → Lightning → Water → Earth → Wind → Fire (each detonated by the next).
    public static Element DetonatorOf(Element statusElement) => statusElement switch
    {
        Element.Fire      => Element.Lightning, // Burn   + Lightning = FIRESTORM
        Element.Lightning => Element.Water,     // Shock  + Water     = ELECTROCUTE
        Element.Water     => Element.Earth,     // Chill  + Earth     = SHATTER
        Element.Earth     => Element.Wind,      // Root   + Wind      = EROSION
        Element.Wind      => Element.Fire,      // Expose + Fire      = COMBUST
        _                 => Element.None,
    };

    /// Player-facing status name for each element.
    public static string StatusName(Element e) => e switch
    {
        Element.Fire      => "BURN",
        Element.Lightning => "SHOCK",
        Element.Water     => "CHILL",
        Element.Earth     => "ROOT",
        Element.Wind      => "EXPOSE",
        _                 => "",
    };

    /// Player-facing reaction name (keyed by the STATUS element that got detonated).
    public static string ReactionName(Element statusElement) => statusElement switch
    {
        Element.Fire      => "FIRESTORM",
        Element.Lightning => "ELECTROCUTE",
        Element.Water     => "SHATTER",
        Element.Earth     => "EROSION",
        Element.Wind      => "COMBUST",
        _                 => "",
    };

    /// Signature color per element (UI text, damage numbers, glove tints).
    public static Color ColorOf(Element e) => e switch
    {
        Element.Fire      => new Color(1f, 0.45f, 0.10f),  // orange
        Element.Lightning => new Color(1f, 0.92f, 0.25f),  // electric yellow
        Element.Water     => new Color(0.30f, 0.80f, 1f),  // ice blue
        Element.Earth     => new Color(0.80f, 0.62f, 0.30f),// amber/stone
        Element.Wind      => new Color(0.75f, 1f, 0.85f),  // pale green-white
        _                 => Color.white,
    };
}
