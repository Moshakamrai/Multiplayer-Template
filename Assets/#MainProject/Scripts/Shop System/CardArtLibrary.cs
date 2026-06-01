using System.Collections.Generic;
using UnityEngine;

// Attach to any persistent GameObject (e.g. GameManager or a dedicated "ShopArt" object).
// Drag each card PNG (from UI/UI Cards) into the matching slot in the Inspector.
// Card IDs must match CardDatabase entries exactly (e.g. "jab", "cross", "dodge_left").
//
// Note on filename/ID mismatches from your art folder:
//   punch.png   → cardId "jab"
//   flank.png   → cardId "cross"
//   reflect.png → cardId "reflect"  (also used for ParryIntent)
//   (no block.png exists — leave block entry blank, a fallback colour will be used)
public class CardArtLibrary : MonoBehaviour
{
    public static CardArtLibrary Instance { get; private set; }

    [System.Serializable]
    public struct CardArtEntry
    {
        public string cardId;
        public Texture2D art;
    }

    [Header("Combat Card Art")]
    [Tooltip("Fill in each cardId + its PNG. cardId must match CardDatabase (lowercase).")]
    public List<CardArtEntry> combatCardArt = new List<CardArtEntry>
    {
        new CardArtEntry { cardId = "jab" },
        new CardArtEntry { cardId = "cross" },
        new CardArtEntry { cardId = "hook" },
        new CardArtEntry { cardId = "block" },
        new CardArtEntry { cardId = "dodge_left" },
        new CardArtEntry { cardId = "dodge_right" },
        new CardArtEntry { cardId = "reflect" },
        new CardArtEntry { cardId = "boom" },
        new CardArtEntry { cardId = "grapple" },
        new CardArtEntry { cardId = "fake" },
        new CardArtEntry { cardId = "uppercut" },
        new CardArtEntry { cardId = "sweep" },
        new CardArtEntry { cardId = "focus" },
        new CardArtEntry { cardId = "taunt" },
        new CardArtEntry { cardId = "overclock" },
        new CardArtEntry { cardId = "reverse" },
        new CardArtEntry { cardId = "trap" },
        new CardArtEntry { cardId = "cage" },
        new CardArtEntry { cardId = "mirror" },
        new CardArtEntry { cardId = "clutch" },
    };

    [Header("Trait Card Art (optional — traits use a default background if blank)")]
    public List<CardArtEntry> traitCardArt = new List<CardArtEntry>();

    [Header("Fallback — shown when no art is found for a card")]
    public Texture2D fallbackTexture;

    private Dictionary<string, Texture2D> _lookup = new Dictionary<string, Texture2D>();
    private Dictionary<string, Sprite>    _spriteCache = new Dictionary<string, Sprite>();

    void Awake()
    {
        Instance = this;
        foreach (var e in combatCardArt)
            if (!string.IsNullOrEmpty(e.cardId) && e.art != null)
                _lookup[e.cardId.ToLower()] = e.art;
        foreach (var e in traitCardArt)
            if (!string.IsNullOrEmpty(e.cardId) && e.art != null)
                _lookup[e.cardId.ToLower()] = e.art;
    }

    public Texture2D GetTexture(string cardId)
    {
        if (!string.IsNullOrEmpty(cardId) && _lookup.TryGetValue(cardId.ToLower(), out var tex))
            return tex;
        return fallbackTexture;
    }

    // Converts a Texture2D to a Sprite (cached so we don't re-create every frame).
    public Sprite GetSprite(string cardId)
    {
        string key = string.IsNullOrEmpty(cardId) ? "__fallback__" : cardId.ToLower();
        if (_spriteCache.TryGetValue(key, out var cached)) return cached;

        var tex = GetTexture(cardId);
        if (tex == null) return null;

        var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
        _spriteCache[key] = sprite;
        return sprite;
    }

    // Looks up art by CardManager trigger name, which may differ from the card ID.
    public Texture2D GetTextureForTrigger(string triggerName)
    {
        if (string.IsNullOrEmpty(triggerName)) return fallbackTexture;
        string id = TriggerToCardId(triggerName);
        return GetTexture(id);
    }

    // Sprite version, keyed by trigger name — used by the hand card buttons.
    public Sprite GetSpriteForTrigger(string triggerName)
    {
        if (string.IsNullOrEmpty(triggerName)) return null;
        return GetSprite(TriggerToCardId(triggerName));
    }

    static string TriggerToCardId(string trigger)
    {
        switch (trigger)
        {
            case "UnbreakablePunch": return "boom";
            case "ParryIntent":     return "reflect";
            case "Left":            return "dodge_left";
            case "Right":           return "dodge_right";
            default:                return trigger.ToLower();
        }
    }
}
