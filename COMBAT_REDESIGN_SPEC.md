# ⚔️ "ON BEAT" — Simplified Combat Redesign (Event / Single-Player Build)

> **Status:** Design locked, implementation in progress.
> **Constraints (from owner):**
> - ❌ Do NOT rename any cards — art is already made. Names stay, **only tooltips change**.
> - ✅ Use all **20 combat cards** — every one maps to a family.
> - ✅ Keep **Traits** — modify their effects to fit this design (names stay).
> - ✅ Remove the **first/pre-round shop**; players start with a fixed learning deck, then buy from the shop **after** rounds.
> - ❌ Do NOT touch song rhythm / beat timing — owner tunes beats manually later.

---

## The one-liner
*Two attacks, two defenses, one beat. Tap to play it safe — shout on the beat to pull off the big stuff.*

## The four actions

| Action | Type | Voice? |
|--------|------|--------|
| **Strike** | Offense | Optional (shout = powered-up / counter-hit) |
| **Throw** | Offense | **Required** (shout-on-beat to land, else whiff) |
| **Block** | Defense | None (safe default) |
| **Parry** | Defense | **Required** (precise shout-on-beat) |

## The matchup matrix (the whole game)

*Row = you, Column = opponent.*

|  | Strike | Throw | Block | Parry |
|--|--------|-------|-------|-------|
| **Strike** | ⏱️ Timing clash | ✅ You win | 🛡️ Chipped | ⚡ They parry *if timed* (else you hit) |
| **Throw** | ❌ You lose | ⏱️ Timing clash | ✅ You win | ✅ You win |
| **Block** | 🛡️ You block (chip) | ❌ You lose | neutral | neutral |
| **Parry** | ⚡ You parry *if timed* | ❌ You lose | neutral | neutral |

**The logic (one sentence each):**
- **Strike beats Throw** — you hit them before the grab connects.
- **Throw beats Block & Parry** — you can't guard a grab.
- **Block & Parry beat Strike** — your guard stops the punch.

Triangle: **Strike → Throw → Defense → Strike.** Defense splits into **Block (safe, small)** vs **Parry (risky, huge)** — the player's read against an incoming Strike.

## What the shout does (voice = spice, never the gate)

- **Strike** — tap = normal hit. **Shout on beat = EX Strike** (counter-hit bonus). Optional.
- **Throw** — **must shout on beat** to land; mistimed = whiff and punishable.
- **Block** — no voice, always available, the comfort option.
- **Parry** — **must shout precisely on the impact beat.** Land = nullify + free counter. Miss = eat the full hit.

## Resolving "both attacked" (timing clash)

- Same offense vs same offense → **compare beat timing.** EXCELLENT > GOOD > BAD. Cleaner beat lands; loser stuffed (no damage).
- **Tie grade = CLASH** — both bounce off, spark + screen-shake, reset to neutral.
- Optional decisive break: **louder shout wins the clash** (single-player = out-scream the bot's threshold).
- *Implementation note:* reuse the existing **TiebreakerManager** infrastructure for clashes.

---

## Your 20 combat cards → 5 families

Names unchanged. Only the **counter** is locked by family; cards differ in damage/cost/gimmick.

| Family | Counter role | Cards |
|--------|--------------|-------|
| **STRIKE** | Beats Throw, loses to defense | Jab, Cross, Hook, Boom, Uppercut |
| **THROW** (cracks turtles) | Beats all defense, loses to Strike | Grapple, Fake, Sweep |
| **BLOCK** (safe defense) | Stops Strike, loses to Throw | Block, Dodge Left, Dodge Right |
| **PARRY** (timed counter) | Nullifies Strike big, loses to Throw | Reflect, Clutch, Reverse, Mirror |
| **SUPPORT / BUFF** (no triangle) | Modifies your moves, not a counter | Focus, Taunt, Trap, Cage, Overclock |

Total: 5 + 3 + 3 + 4 + 5 = **20** ✅ — *(implemented as a `CardFamily` enum + `family` field on each card)*

> **Pass-2 reconciliation needed:** Fake, Sweep, and Uppercut are tagged to their target families now, but the *current* combat code still resolves them via the old per-card RPS. The combat-matrix pass will make behavior match these tags.

**Notes:**
- **Reflect** is the flagship Parry (already nullify + reflect).
- **Grapple / Fake / Sweep** share one job — beat the blocker — so they're the Throw family.
- **SUPPORT** cards don't fight the triangle; they're modifiers (kept, since all 20 must be used).

---

## Proposed new tooltips (names stay, text changes)

| Card | Family | New tooltip |
|------|--------|-------------|
| Jab | Strike | Quick strike. Beats Throws, stopped by guards. Shout on beat for bonus damage. |
| Cross | Strike | Solid strike. Beats Throws. Shout on beat to power it up. |
| Hook | Strike | Heavy strike. Beats Throws, loses to guards. Shout on beat for a big hit. |
| Boom | Strike | Huge strike. Beats Throws. Slow but brutal — shout on beat to confirm. |
| Uppercut | Strike | Rising strike. Beats Throws. Shout on beat for counter-hit damage. |
| Overclock | Strike | Reckless strike — hurts you a little, hits them a lot. Shout to amp it. |
| Grapple | Throw | Grab through any guard. Beats Block & Parry, loses to Strikes. Shout on beat to land it. |
| Fake | Throw | Feint that cracks defense. Beats guards, loses to Strikes. Shout on beat. |
| Sweep | Throw | Low grab that beats blockers. Loses to Strikes. Shout on beat to land. |
| Block | Block | Safe guard. Stops Strikes, loses to Throws. No shout needed. |
| Dodge Left | Block | Slip left. Evades Strikes, loses to Throws. |
| Dodge Right | Block | Slip right. Evades Strikes, loses to Throws. |
| Reflect | Parry | Shout on the exact beat to nullify a Strike and counter. Loses to Throws. |
| Clutch | Parry | High-risk parry vs heavy Strikes. Perfect beat = big counter; miss = you eat it. |
| Reverse | Parry | Parry that returns the Strike's damage. Loses to Throws. |
| Mirror | Parry | Parry that reflects the next Strike with bonus. Loses to Throws. |
| Focus | Support | Charge up — your next Strike hits +50%. |
| Taunt | Support | Force the opponent to attack next beat. |
| Trap | Support | Set a trap that punishes their defense next beat. |
| Cage | Support | Punish the opponent if they act next beat. |

---

## Shop & starting deck

- **Remove the pre-round (first) shop.** Players begin with a fixed **learning deck** (TBD — see open questions) so they learn the triangle before spending.
- **Shops appear after each round** as normal, letting players add Advanced/Legendary/Support cards and Traits.
- Equipped deck cap stays at **8**.

---

## Traits (kept, refitted to the new design)

Names unchanged; effects rewritten to reinforce the 4 families. *(Exact remap pending owner decision — see open questions.)* Direction:
- Offense traits → buff Strike / Throw (e.g., bigger counter-hits, unstuffables).
- Defense traits → buff Block / Parry (e.g., wider parry window, chip reduction).
- Utility traits → timing/economy help (e.g., easier parry timing, refresh).

---

## Booth onboarding ramp (progressive disclosure)

- **Tier 0 (first 60–90s):** Strike + Block + Parry only. *"Hit on the beat to attack. Block to defend. SHOUT on the beat to PARRY and counter."*
- **Tier 1:** add **Throw** — *"They won't stop blocking? Shout to GRAB through their guard."*
- **Tier 2:** sprinkle in Support cards / Traits for repeat players.

---

## Suggested feel (rhythm untouched per owner)

- **Parry window:** generous for the booth (~±0.15s), tighten later for competitive.
- **Rough damage:** Strike ~8–12%, EX-Strike +50%, Throw ~15–18%, Parry counter ~20%+, Block chip ~2%.
- **Bot = difficulty dial:** Easy bot always GOOD timing (player's EXCELLENT always wins); Hard bot frequent EXCELLENT + occasional Parry reads.

---

## Optional depth levers (full game, not booth)
- **Dodge beats Throw:** split Dodge out of Block for a dedicated throw-escape.
- **Throw vs Parry:** let a *perfect* Parry tech a Throw, rewarding godlike reads.
