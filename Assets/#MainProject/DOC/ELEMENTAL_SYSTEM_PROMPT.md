# Voice Rhythm Fighter — Elemental Card System Rework

## Context

You are helping me rework the combat card system for **Voice Rhythm Fighter**, a rhythm-synchronized real-time combat card game built in **Unity 2022.3 LTS (URP)** with **Mirror Networking**. The game uses voice input (Vosk) and controller triggers to select and execute combat moves on-beat.

The game already has a working card system with 5 card families, a beat-timing grading system (EXCELLENT / GOOD / BAD), a power meter, and server-authoritative combat resolution via `PlayerCombat`. **Nothing about card names, family names, or the timing system changes.** We are adding an elemental layer on top of the existing system.

---

## Existing Card Families & Cards (DO NOT RENAME)

| Family   | Cards                                      | Role              |
|----------|--------------------------------------------|-------------------|
| Strike   | Jab, Hook, Cross, Boom, Uppercut           | Raw damage         |
| Throw    | Grapple, Fake, Sweep                       | Defense-breaking   |
| Block    | Block, Reflect, Parry                      | Damage negation    |
| Parry    | Reflect, Clutch, Mirror                    | Counter-attack     |
| Support  | Focus, Taunt, Reverse                      | Buffs & utility    |

## Existing Timing Grades

| Grade     | Window   | Effect                                   |
|-----------|----------|------------------------------------------|
| EXCELLENT | 0–20ms   | Max damage + full perk activation         |
| GOOD      | 20–80ms  | Normal damage + reduced perk effects      |
| BAD       | 80ms+    | Minimal damage + some perks fail entirely |

## Existing Perk Examples (for reference, not exhaustive)

- **CounterBonus** (Uppercut): +20% damage if opponent attacked last beat.
- **DodgeEvade** (Dodge): Negates all damage vs Strike.
- **FakeCounter** (Fake): 2× damage if opponent defended last beat.
- **Reflect**: Sends damage back + stuns on EXCELLENT timing.
- **Focus**: Next Strike deals +50% damage.

---

# TASK 1: Elemental Card Logic Rework

## Goal

Assign each card family a permanent element. Add an elemental advantage/disadvantage modifier to the existing damage resolution pipeline. **Timing remains the primary skill axis.** Element is a secondary modifier — it should never override a significant timing advantage.

## Elemental Assignments

| Family   | Element          | Thematic Reasoning                                                |
|----------|------------------|-------------------------------------------------------------------|
| Strike   | **Fire** 🔥       | Aggressive, fast, raw offensive energy                            |
| Throw    | **Lightning** ⚡   | Disruptive, breaks through guards, electric force                 |
| Block    | **Earth** 🪨       | Grounded, absorbing, immovable defensive wall                    |
| Parry    | **Water/Ice** 🧊   | Reactive, redirecting force, precision counter                   |
| Support  | **Wind** 🌀        | Utility, speed enhancement, changing the flow of battle          |

## Elemental Counter Cycle

```
Fire 🔥 → Wind 🌀 → Earth 🪨 → Water 🧊 → Fire 🔥
         (burns)    (erodes)   (absorbs)  (quenches)

Lightning ⚡ → Earth 🪨  (Lightning breaks Earth/defense)
Water 🧊 → Lightning ⚡  (Water grounds/shorts Lightning)
```

### In plain terms:
- **Fire beats Wind** — Aggression overwhelms utility/buffs.
- **Wind beats Earth** — Speed/utility disrupts passive defense.
- **Earth beats Water** — Grounded defense absorbs redirection.
- **Water beats Fire** — Precision counters raw aggression.
- **Lightning beats Earth** — Disruption breaks through defense (Throw's existing role).
- **Water beats Lightning** — Precision grounds brute disruption.
- **Lightning vs Fire / Lightning vs Wind** — Neutral (no advantage either way).

## Damage Modifier Rules

### Core Principle: TIMING > ELEMENT. Always.

The elemental modifier applies AFTER the timing grade resolves damage. It is a percentage multiplier, not a replacement.

### Modifier Values (tunable, these are starting points):

| Matchup              | Modifier           | Example                                                        |
|----------------------|--------------------|----------------------------------------------------------------|
| **Elemental Advantage** | **+30% damage**     | Fire Strike vs Wind Support → Strike deals 1.3× its graded damage |
| **Elemental Disadvantage** | **-25% damage**  | Fire Strike vs Water Parry → Strike deals 0.75× its graded damage |
| **Neutral**          | **1.0× (no change)** | Fire Strike vs Earth Block → normal resolution, no element mod |

### Critical Override Rules:

1. **EXCELLENT always hurts.** Even at elemental disadvantage, an EXCELLENT-timed attack must deal meaningful damage (never reduced below 50% of its base EXCELLENT value). A perfectly timed move should never feel wasted.

2. **BAD + Disadvantage = punished hard.** A BAD-timed attack at elemental disadvantage can be reduced to near-zero. This is the correct punishment — bad timing AND bad matchup knowledge.

3. **Equal timing, element breaks the tie.** When both players hit the same grade (both GOOD, both EXCELLENT), the one with elemental advantage wins the exchange or deals significantly more damage. This is where element matters most.

4. **No dead hands.** The player must always have at least one viable option regardless of the opponent's element. If all drawn cards are at elemental disadvantage, timing skill alone should still allow them to win exchanges at EXCELLENT. Element makes it harder, not impossible.

5. **Existing perks still work.** CounterBonus, DodgeEvade, FakeCounter, etc. resolve independently of element. A perk trigger is a perk trigger. Element modifies the final damage number after perk effects apply.

### Resolution Order (updated):

```
1. Both players input on beat → timing grade assigned (EXCELLENT / GOOD / BAD)
2. Card family and specific card determined
3. Defense priority check (did opponent play defense last beat?)
4. Perk trigger check (does this card's perk activate based on opponent's last action?)
5. Base damage calculated from timing grade + card stats + perk bonuses
6. **NEW: Elemental modifier applied**
   → Check attacker's element vs defender's element
   → Apply advantage (+30%), disadvantage (-25%), or neutral (1.0×)
   → Clamp: EXCELLENT attacks never go below 50% of base EXCELLENT value
7. Final damage applied to opponent health
8. Timing feedback sent to client (grade + element interaction result)
```

## Data Structure Changes

### Card Data (ScriptableObject or equivalent)

Each card already has: `cardName`, `family`, `baseDamage`, `perkType`, `perkValue`, `cost`, `maxLevel`.

**Add:**
- `element` (enum: Fire, Lightning, Earth, Water, Wind)
- This is derived from family automatically, but stored per-card for quick lookup.

### New Enum

```
public enum Element { Fire, Lightning, Earth, Water, Wind, Neutral }
```

### New Static Lookup (or similar)

```
ElementAdvantage.GetModifier(Element attacker, Element defender)
→ returns float (1.3, 0.75, or 1.0)
```

### PlayerCombat Changes

- After perk resolution and base damage calc, call `ElementAdvantage.GetModifier()` with attacker's card element vs defender's card element (or defender's last-played card element if defending).
- Multiply final damage by the modifier.
- Clamp per the override rules above.
- **Sync the element interaction result to the client** (new field on the timing feedback TargetRpc) so the UI/VFX layer knows what happened.

## What NOT to Change

- Card names stay exactly as they are (Jab, Hook, Cross, Boom, Uppercut, etc.).
- Family names stay exactly as they are (Strike, Throw, Block, Parry, Support).
- Timing windows stay exactly as they are (0–20ms, 20–80ms, 80ms+).
- Perk logic stays exactly as it is. Element does not interfere with perk activation.
- Shop economy stays the same. Cards don't cost more/less based on element.
- Beat detection, power meter, floor wave — untouched.

## Deliverables for Task 1

1. Updated card data structure with element field.
2. `ElementAdvantage` static class (or ScriptableObject) with the counter cycle lookup and modifier values.
3. Updated `PlayerCombat` damage resolution pipeline inserting the elemental modifier at the correct step.
4. Updated TargetRpc feedback to include elemental interaction result (advantage / disadvantage / neutral) so the client knows what happened for VFX purposes.
5. Brief summary of where each change lives and what it touches.

**Do NOT implement VFX, UI changes, or visual feedback yet. That is Task 2.**

---

# TASK 2: Elemental VFX Triggers & Setup

> **Only start this after Task 1 logic is reviewed and confirmed working.**

## Goal

Wire up visual feedback so players and viewers instantly understand elemental interactions through VFX alone. Every attack, defense, and counter should visually communicate its element and whether the interaction was advantageous, neutral, or disadvantaged — without any text explanation needed.

## Element Visual Language

### Fire 🔥 (Strike Family)
- **Color palette:** Orange, red-orange, hot white core.
- **On activation:** Fist/glove ignites — ember particles trail from knuckles, flame wrap around hand.
- **On impact (hit lands):** Burst of fire sparks at contact point. Small ember scatter. Brief orange screen-edge flash.
- **On-beat EXCELLENT:** Flame intensifies to white-hot core. Larger burst. More particles.
- **Idle (card in hand):** Subtle ember float around the Strike card slot.

### Lightning ⚡ (Throw Family)
- **Color palette:** Electric blue, white, purple crackle.
- **On activation:** Electric arcs crawl up forearm. Brief bright flash on release.
- **On impact (hit lands):** Lightning bolt burst at contact. Crackling discharge particles. Brief screen shake.
- **On-beat EXCELLENT:** Chain lightning arcs between attacker and defender. Brighter, longer discharge.
- **Idle (card in hand):** Faint static sparks around the Throw card slot.

### Earth 🪨 (Block Family)
- **Color palette:** Brown, gold, amber, stone grey.
- **On activation:** Stone/crystal shield materializes in front of player. Ground crack decal beneath feet.
- **On block success:** Shield absorbs hit with rock-fracture particles. Dust cloud. Shield cracks then reforms.
- **On-beat EXCELLENT:** Shield is larger, golden-edged. Impact sends a shockwave ripple outward.
- **Idle (card in hand):** Faint dust motes / floating pebbles around Block card slot.

### Water/Ice 🧊 (Parry Family)
- **Color palette:** Cyan, ice blue, frost white.
- **On activation:** Ice crystals form along forearm/hand. Frost vapor trails.
- **On parry success:** Ice shatter burst at contact point. Frozen shards scatter. Brief frost screen-edge effect.
- **On-beat EXCELLENT (Reflect):** Full ice mirror materializes briefly, shatters spectacularly as damage returns. Opponent gets a frost stun VFX (ice crystals on their model briefly).
- **Idle (card in hand):** Faint frost shimmer around Parry card slot.

### Wind 🌀 (Support Family)
- **Color palette:** White, pale green, silver swirl.
- **On activation:** Wind vortex swirls around player. Clothes/particles get pushed by directional wind.
- **On buff applied:** Burst of wind rings expanding outward. Speed lines briefly.
- **On-beat EXCELLENT:** Larger vortex. Wind ring is more visible. Buff aura lingers longer and glows brighter.
- **Idle (card in hand):** Faint swirl particles around Support card slot.

## Elemental Interaction VFX

These play ON TOP of the individual element VFX when two elements collide.

### Advantage Hit (attacker has elemental advantage)
- **Amplified impact.** Attacker's element VFX is ~1.5× scale. Bigger particles, brighter glow, longer linger time.
- **Defender's element visually "breaks."** E.g., Fire vs Wind: wind particles scatter and dissipate rapidly, fire pushes through. Water vs Fire: fire extinguishes with a steam burst, water crystals remain intact.
- **Screen flash** in the attacker's element color (brief, ~100ms).
- **Damage number** gets a colored glow matching the attacker's element.

### Disadvantage Hit (attacker has elemental disadvantage)
- **Reduced impact.** Attacker's element VFX is ~0.5× scale. Fewer particles, dimmer, shorter linger.
- **Defender's element visually "overwhelms."** E.g., Water vs Fire attacker: fire fizzles into steam on contact, water/ice VFX expands and dominates the frame.
- **Damage number** is dimmer, smaller, with a brief grey tint.
- **No screen flash.** Muted feedback.

### Neutral (no advantage)
- **Normal VFX scale.** Both elements play at standard intensity.
- **Standard damage number.** No extra glow or dimming.

## Implementation Approach

### Data Flow

```
PlayerCombat resolves damage (Task 1)
  → TargetRpc sends to client:
     - timing grade (EXCELLENT / GOOD / BAD)
     - element interaction result (ADVANTAGE / DISADVANTAGE / NEUTRAL)
     - attacker element (Fire / Lightning / Earth / Water / Wind)
     - defender element (Fire / Lightning / Earth / Water / Wind)
     - final damage value

Client receives TargetRpc
  → VFXManager reads the payload
  → Triggers appropriate element VFX on attacker (activation + impact)
  → Triggers appropriate element VFX on defender (reaction)
  → Triggers interaction VFX (advantage/disadvantage/neutral scaling)
  → Triggers UI feedback (damage number color, screen flash if advantage)
```

### VFX Manager (new component or extension of existing)

- `TriggerAttackVFX(Element element, TimingGrade grade, Vector3 impactPoint)`
- `TriggerDefenseVFX(Element element, TimingGrade grade, Vector3 impactPoint)`
- `TriggerInteractionVFX(ElementInteraction interaction, Element attackerElement, Element defenderElement, Vector3 impactPoint)`
- `TriggerDamageNumber(float damage, Element element, ElementInteraction interaction, Vector3 position)`

### Particle Systems

Each element needs a **prefab set**:
- `Fire_Activation` / `Fire_Impact` / `Fire_Excellent`
- `Lightning_Activation` / `Lightning_Impact` / `Lightning_Excellent`
- `Earth_Activation` / `Earth_BlockSuccess` / `Earth_Excellent`
- `Water_Activation` / `Water_ParrySuccess` / `Water_Excellent`
- `Wind_Activation` / `Wind_BuffApplied` / `Wind_Excellent`

Plus **interaction overrides**:
- `Advantage_Amplify` (scales up attacker VFX, plays element-break on defender)
- `Disadvantage_Suppress` (scales down attacker VFX, plays element-overwhelm on defender)

### Platform Considerations

- **Flat (PC):** Full VFX, bloom post-processing enabled. All particles at max.
- **PCVR:** Full VFX, bloom enabled. Particles at max.
- **Mobile VR (Quest):** Reduce particle counts by 50%. Disable bloom-dependent effects. Use simpler shaders for element materials. `VRPerformance` already handles render scale — VFX should respect its LOD tier.

### Card Hand UI (element idle indicators)

- Each card slot in the hand UI gets a subtle elemental idle effect (embers for Strike, sparks for Throw, etc.).
- This is cosmetic only — helps the player associate cards with elements at a glance.
- In VR (world-space hand cards), these are small particle emitters parented to each card slot.
- In flat (IMGUI), these are animated overlays or tinted card borders.

## Deliverables for Task 2

1. `VFXManager` component with methods for triggering element VFX based on combat resolution data.
2. Prefab structure for all 5 element VFX sets (activation, impact, excellent variants).
3. Interaction VFX logic (advantage amplify, disadvantage suppress, neutral standard).
4. Hookup to `PlayerCombat` TargetRpc so VFX triggers automatically on combat resolution.
5. Card hand UI idle element indicators (flat + VR).
6. Platform-aware LOD (particle count reduction for mobile VR).
7. Brief summary of prefab names, where they live, and how to swap/tune them.

---

# Notes for the Developer

- **Architecture before code.** Walk me through your planned class structure and data flow before writing implementation. I want to review the approach first.
- **Tuning is expected.** The modifier values (30% advantage, 25% disadvantage, 50% EXCELLENT floor) are starting points. Expose them as serialized fields or ScriptableObject values so I can tweak in the editor without recompiling.
- **Mirror authority.** All elemental damage resolution happens server-side in `PlayerCombat`. The client only receives the result and plays VFX. No client-side damage calculation.
- **Keep it modular.** Element assignments, counter cycle, and modifier values should be editable without touching combat code. A ScriptableObject or static config class is ideal.
