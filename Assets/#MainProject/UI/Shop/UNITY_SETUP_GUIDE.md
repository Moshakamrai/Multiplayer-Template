# Cyberpunk Shop Panel - Unity Implementation Guide

## Assets Overview

| File | Description | Use In Unity |
|------|-------------|--------------|
| `panel_bg_cyan.png` | Trait Cards panel background | Image component - Panel bg |
| `panel_bg_orange.png` | Combat Cards panel background | Image component - Panel bg |
| `panel_bg_magenta.png` | Vex Cards panel background | Image component - Panel bg |
| `panel_bg_green.png` | Your Deck panel background | Image component - Panel bg |
| `panel_bg_pink.png` | Opponent Deck panel background | Image component - Panel bg |
| `card_shop_orange.png` | Shop card template (Combat) | Image component - Card bg |
| `card_shop_magenta.png` | Shop card template (Vex) | Image component - Card bg |
| `card_shop_cyan.png` | Shop card template (Trait) | Image component - Card bg |
| `card_deck.png` | Deck card template (owned) | Image component - Deck card bg |
| `btn_buy.png` | BUY button template | Image component - Button bg |
| `btn_action.png` | LOCK IN & FIGHT button | Image component - Action button bg |

---

## Color Palette (Hex Codes)

Copy these exact hex values into Unity's color picker:

### Panel Accent Colors
| Panel | Primary Color | Glow Color (HDR) |
|-------|--------------|------------------|
| Your Deck | `#00ff88` | HDR: (0, 1, 0.53, 1) |
| Combat Cards | `#ff8800` | HDR: (1, 0.53, 0, 1) |
| Vex Cards | `#ff00a0` | HDR: (1, 0, 0.63, 1) |
| Trait Cards | `#00f0ff` | HDR: (0, 0.94, 1, 1) |
| Opponent Deck | `#ff69b4` | HDR: (1, 0.41, 0.71, 1) |

### Global Colors
| Element | Hex | Notes |
|---------|-----|-------|
| Background | `#050508` | Main screen bg |
| Panel Background | `#06060a` | Dark panel fill |
| Card Background | `#0a0a12` | Card dark fill |
| Credits / Gold | `#ffdd00` | Credit cost text |
| Credits Glow | HDR (1, 0.87, 0, 2) | Intensity boost |
| Text Primary | `#e0e0e0` | Main white text |
| Text Secondary | `#808080` | Descriptions |
| Text Muted | `#404040` | Labels, counts |
| Danger / Remove | `#ff0044` | Remove button |
| Danger Glow | HDR (1, 0, 0.27, 1) | Red glow |

---

## Fonts

Import these free Google Fonts into Unity:

| Purpose | Font | Weight | Unity Import |
|---------|------|--------|--------------|
| Headers, Titles, Button Text | **Orbitron** | Bold (700) | Download from fonts.google.com |
| Body Text, Descriptions | **Rajdhani** | Regular (400), SemiBold (600) | Download from fonts.google.com |

**Orbitron** - Use for: Panel titles, card names, button text, timer, round counter, credit count
**Rajdhani** - Use for: Card descriptions, labels, cost text, small UI text

---

## Unity UI Hierarchy (Canvas Setup)

```
Canvas (Screen Space - Overlay, 1920x1080 reference)
|
|-- Background
|   |-- Image: #050508 solid fill
|   |-- Image: Grid pattern overlay (tiled, 5% opacity cyan lines)
|
|-- Scanlines Overlay (full screen)
|   |-- Image: repeating horizontal black lines, 3% opacity
|
|-- TopBar
|   |-- TimerGroup (left)
|   |   |-- Circle progress image
|   |   |-- Text: "118s" (Orbitron Bold)
|   |   |-- Label: "TIME REMAINING" (Rajdhani, gray)
|   |   |-- Slider: progress bar fill
|   |
|   |-- CreditsGroup (center)
|   |   |-- Image: diamond icon
|   |   |-- Text: "4" (Orbitron Bold, gold)
|   |   |-- Label: "CREDITS"
|   |
|   |-- RoundGroup (right)
|       |-- Text: "ROUND 2" (Orbitron Bold, magenta)
|       |-- Label: "CURRENT ROUND"
|       |-- Image: pulsing dot
|
|-- MainContent (Horizontal Layout Group, 5 columns)
|   |-- YourDeckPanel (Vertical Layout Group)
|   |   |-- Image: panel_bg_green (sliced, 20px border)
|   |   |-- HeaderText: "YOUR DECK" (Orbitron, green)
|   |   |-- ScrollView
|   |       |-- DeckCard prefab instances
|   |
|   |-- CombatCardsPanel
|   |   |-- Image: panel_bg_orange (sliced)
|   |   |-- HeaderText: "COMBAT CARDS" (Orbitron, orange)
|   |   |-- ScrollView
|   |       |-- ShopCard prefab instances
|   |
|   |-- VexCardsPanel
|   |   |-- Image: panel_bg_magenta (sliced)
|   |   |-- HeaderText: "VEX CARDS" (Orbitron, magenta)
|   |   |-- ScrollView
|   |       |-- ShopCard prefab instances
|   |
|   |-- TraitCardsPanel
|   |   |-- Image: panel_bg_cyan (sliced)
|   |   |-- HeaderText: "TRAIT CARDS" (Orbitron, cyan)
|   |   |-- ScrollView
|   |       |-- ShopCard prefab instances
|   |
|   |-- OpponentDeckPanel
|       |-- Image: panel_bg_pink (sliced)
|       |-- HeaderText: "OPPONENT DECK" (Orbitron, pink)
|       |-- ScrollView
|           |-- DeckCard prefab instances
|
|-- BottomBar
|   |-- Decorative line left (green gradient)
|   |-- Button: LOCK IN & FIGHT
|   |   |-- Image: btn_action (sliced)
|   |   |-- Text: "LOCK IN & FIGHT" (Orbitron, green)
|   |-- Decorative line right (green gradient)
```

---

## Prefabs to Create

### 1. ShopCard Prefab
```
ShopCard (RectTransform: width=300, height=auto)
|-- Background
|   |-- Image: card_shop_[color] (sliced)
|-- HeaderRow (Horizontal Layout)
|   |-- CardNameText (TextMeshPro, Orbitron Bold, 14pt, accent color)
|   |-- TypeTag (Image: semi-transparent bg + border)
|       |-- TagText (TextMeshPro, 10pt, accent color)
|-- DescriptionText (TextMeshPro, Rajdhani, 11pt, gray)
|-- BottomRow (Horizontal Layout)
    |-- CostText (TextMeshPro, Orbitron Bold, 12pt, gold)
    |-- BuyButton (Button component)
        |-- Image: btn_buy (sliced, tinted to accent color)
        |-- Text: "BUY" (Orbitron, 10pt)
```

**Script: ShopCard.cs**
```csharp
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ShopCard : MonoBehaviour
{
    [Header("UI References")]
    public Image cardBackground;
    public TextMeshProUGUI cardNameText;
    public TextMeshProUGUI typeTagText;
    public TextMeshProUGUI descriptionText;
    public TextMeshProUGUI costText;
    public Button buyButton;
    
    [Header("Hover Settings")]
    public Color normalBorderColor;
    public Color hoverBorderColor;
    public float hoverLift = 3f;
    
    private Vector3 originalPos;
    
    void Awake()
    {
        originalPos = transform.localPosition;
        
        // Add hover events
        var trigger = gameObject.AddComponent<EventTrigger>();
        
        var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        enter.callback.AddListener((data) => OnHoverEnter());
        trigger.triggers.Add(enter);
        
        var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
        exit.callback.AddListener((data) => OnHoverExit());
        trigger.triggers.Add(exit);
    }
    
    void OnHoverEnter()
    {
        transform.localPosition = originalPos + Vector3.up * hoverLift;
        cardBackground.color = hoverBorderColor;
        // Add glow effect
        var glow = cardBackground.gameObject.AddComponent<Outline>();
        glow.effectColor = hoverBorderColor;
        glow.effectDistance = new Vector2(2, 2);
    }
    
    void OnHoverExit()
    {
        transform.localPosition = originalPos;
        cardBackground.color = normalBorderColor;
        Destroy(cardBackground.GetComponent<Outline>());
    }
    
    public void Setup(string name, string type, string desc, int cost, Color accentColor)
    {
        cardNameText.text = name;
        typeTagText.text = type;
        descriptionText.text = desc;
        costText.text = $"{cost} CR";
        
        cardNameText.color = accentColor;
        typeTagText.color = accentColor;
        normalBorderColor = new Color(accentColor.r, accentColor.g, accentColor.b, 0.25f);
        hoverBorderColor = accentColor;
    }
}
```

### 2. DeckCard Prefab
```
DeckCard (RectTransform: width=280, height=40)
|-- Background
|   |-- Image: card_deck (sliced)
|-- NameText (TextMeshPro, Orbitron Bold, 12pt, accent color)
|-- CostText (TextMeshPro, Rajdhani, 10pt, gray) [optional]
|-- RemoveButton (Button, right side)
    |-- Text: "REMOVE" (10pt, red)
```

---

## Neon Glow Effect (Material)

Create a simple UI glow material using Unity's URP/HDR:

```
1. Create > Shader > Unlit Shader Graph
2. Add "Color" property (HDR enabled)
3. Multiply color by intensity value
4. Connect to Emission
5. Use on Image components with "Use Sprite Mesh" enabled
```

Or use **TextMeshPro's built-in Outline + Underlay** for text glow:
```
TextMeshPro > Extra Settings:
- Outline Color: accent color (25% opacity)
- Outline Width: 0.1
- Underlay Color: accent color (15% opacity)  
- Underlay Offset X: 0.5, Y: -0.5
- Underlay Dilate: 0.2
```

---

## Animation Setup

### 1. Card Hover Animation (Animator)
```
Trigger: "Hover"
- Scale: 1.0 -> 1.02 (ease out, 0.15s)
- Position Y: +3px
- Border Glow: opacity 0.25 -> 0.8

Trigger: "Idle"
- Reverse of above
```

### 2. Button Shine Animation
```csharp
// Add to Buy button
public class ButtonShine : MonoBehaviour
{
    public float shineSpeed = 2f;
    private Material mat;
    
    void Start()
    {
        mat = GetComponent<Image>().material;
        // Animate _ShineLocation property from -1 to 2
        mat.DOFloat(2f, "_ShineLocation", shineSpeed)
           .SetEase(Ease.Linear)
           .SetLoops(-1, LoopType.Restart);
    }
}
```

### 3. Pulsing Dot (Round indicator)
```
Animation Curve:
- Scale: 1.0 -> 1.3 -> 1.0
- Opacity: 1.0 -> 0.6 -> 1.0
- Duration: 2s, loop infinite
```

---

## Layout Dimensions (1920x1080)

| Element | Width | Height | Position |
|---------|-------|--------|----------|
| Top Bar | 1920 | 60 | Top |
| Main Content | 1880 | 940 | Center (padding 20px) |
| Each Panel | ~360 | 920 | Evenly spaced |
| Shop Card | 340 | auto | Stacked vertically |
| Deck Card | 320 | 45 | Stacked vertically |
| Bottom Bar | 1920 | 80 | Bottom |
| Action Button | 400 | 50 | Center of Bottom Bar |

---

## Quick Import Checklist

- [ ] Import all PNGs to `Assets/UI/ShopPanel/` (set Texture Type: Sprite 2D and UI)
- [ ] Import Orbitron and Rajdhani fonts
- [ ] Create TMP Font Assets for both fonts
- [ ] Set all panel backgrounds to **Sliced** with 20px border
- [ ] Set all card backgrounds to **Sliced** with 15px border
- [ ] Set buttons to **Sliced** with 10px border
- [ ] Create the Canvas (Screen Space - Overlay)
- [ ] Build prefabs: ShopCard, DeckCard
- [ ] Create the 5 panels with VerticalLayoutGroup
- [ ] Add ScrollRect + Mask to panels with many cards
- [ ] Set up colors on all text components
- [ ] Add hover scripts to cards
- [ ] Test at 1920x1080 resolution

---

## Pro Tips

1. **9-slice all panel/card images** - The circuit corners and borders will stretch properly
2. **Use Layout Groups** - VerticalLayoutGroup with ContentSizeFitter for auto-sizing cards
3. **Pool your cards** - Instantiate card prefabs from a pool for smooth scrolling
4. **HDR colors for glow** - Bump color intensity above 1.0 for Bloom post-processing
5. **Canvas scaler** - Set to "Scale With Screen Size", reference 1920x1080
6. **Particle system** - Add subtle floating particles as a separate canvas layer for atmosphere
