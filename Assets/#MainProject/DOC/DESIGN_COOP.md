# DESIGN — 2P Co-op Voice Roguelite (working title)

**Status:** Design v4 (2026-07-11) — combat reworked to Split Calling (§3) and weapons
reworked to single-attack + crafting (§4); see those sections for what changed and why.
DESIGN.md remains the record of the shipped single-player systems.
**Engine:** Unity 2022.3 URP · **Networking:** Mirror + Epic Online Services relay ·
**Voice:** Vosk (offline STT) · **Branch:** Coop-Mode-With-PC

---

## 1. Vision (one paragraph)

Two friends, two mics, one run. **Your voice is your weapon and your wallet.** In combat,
both players CALL attacks by shouting card names AND TIME them to the beat of the music —
split by SLOT (each player runs their own 4-weapon loadout), not by a fixed caller/striker
divide, so nobody is ever just waiting on the other. Between fights, both players walk a
shared hub and literally TALK to NPCs — haggling, flattering, threatening — to get loot.
Fights earn gold and components; talking turns gold into gear (and unlocks the build-
defining Keystones); crafted weapons change how the fight plays. Roguelite runs across
musical biomes.

**The economy in one sentence: fight to earn, talk to spend, gear to fight better.**

---

## 2. Core Loop

```
Lobby (host + join code via EOS)
  → Overworld map (For The King-style nodes, shared party token, vote to move)
    → FIGHT node: song + beat map → Caller & Striker combat → gold + crumb loot
    → HUB / SHOP node: walk around together, mics live → negotiate gear, quests, bets
    → EVENT / REST / BOSS nodes
  → Biome boss → next biome → run ends (win or party wipe) → lobby
```

Runs are self-contained (For The King style). Persistent across runs: cosmetics, unlocked
songs, and **NPC relationships** (grudges, debts, favorites).

---

## 3. Combat — SPLIT CALLING (core structure, v4, 2026-07-11)

Supersedes the strict "one Caller, one Striker" split. **Both players call AND both players
time hits** — the difference is WHICH slot each is responsible for, not who talks and who
doesn't. Nobody sits idle waiting for the other to finish a round; both mics and both
timing-inputs are live throughout a fight.

### Slots, not roles
Each player equips their own **4-slot loadout** (Primary / Secondary / Defense / Co-op —
see §4). Each slot is filled by a separate single-attack WEAPON. "Whose beat is it" is
decided dynamically per beat (enemy telegraph target, or whoever's slot is most relevant),
not by a fixed turn order — so both players are reading telegraphs and shouting words
continuously, just for different slots/moments.

### The beat cycle (per beat)
1. Enemy telegraphs its move ~1 beat early (announcer VO / visual tell) AND telegraphs
   which player it's targeting.
2. The TARGETED player shouts the word for whichever equipped weapon counters the
   telegraph, then executes the timing + lane-aim on the beat (existing EXCELLENT/GOOD/BAD
   ratings, two-axis: timing + left/right).
3. The OTHER player is not idle — see "Combo Calls" and "Danger Calls" below for what they
   are doing on that same beat.
4. Outcome resolves via the existing RPS + timing engine. Resolution layer is unchanged.

### Combo Calls — the signature co-op mechanic
On any beat, the non-targeted player can shout a ONE-WORD MODIFIER ("HARD!", "FAST!",
"LOW!") that alters the targeted player's in-flight attack — power, window width, or lane —
**before it lands**. The attack doesn't reach its full potential until both players
contribute something. This is a shared sentence, not a command chain: neither player is
ever just watching.

### Danger Calls — the defensive job for the "off" player
While one player is busy calling/timing an offensive beat, the enemy may ALSO telegraph a
threat directly at the other player. That player has their own one-word defensive call
(block/parry/dodge) to shout on their own beat, independent of the main exchange. Both mics
are live throughout a fight — one doing offense-calling, one doing defense-calling — which
naturally splits the work without turn-swapping.

### Decision-load & momentum (gear determines HOW HARD you have to think — §4)
With beats every ~4 seconds, the real bottleneck is the decision, not the shout. Gear
quality changes how much thinking a beat requires, not just numbers:
- **Better gear auto-counters MORE of the 4 possible telegraphs** with your equipped
  Primary — so a strong loadout means you're mostly repeating your best word and only
  truly deciding on the telegraphs it doesn't cover. Feels easier because it IS easier.
- **Worse/janky gear gives no shortcut** — every beat is a genuine 4-way read. Same 4
  seconds, real puzzle every time.
- **Shared momentum meter** (not per-player): consecutive correct calls from EITHER
  player widen BOTH players' next timing window. A good call is a gift to your partner —
  this is where the co-op payoff lives.
- **Streak-cap as difficulty:** janky weapons cap how high the momentum streak can climb
  (e.g. resets every 2 hits even on success) instead of just dealing less damage — a bad
  weapon holds back the FEELING of being on a roll, which reads more viscerally than a
  numeric penalty.
- Correct-telegraph glow/pulse intensity scales with gear tier — a free "the game is
  rewarding my loot" visual, no mechanical cost.

### Alternate mode: STRICT CALLER & STRIKER (kept as a mutator)
The original one-Caller/one-Striker split (whole fight or per-round, fixed) is kept as an
optional mutator/difficulty mode, not the default — useful for a biome twist, a boss
gimmick, or players who prefer a calmer division of labor. Same underlying declare/execute
pipeline; just locks who calls and who strikes instead of letting it flow per-slot.

### Alternating beats (fallback / solo-adjacent mode)
Also retained: enemy alternates targets strictly A/B, missing your beat damages your
PARTNER (tunable). Useful as the simplest mode to ship first while the full split-calling
system is being tuned, and as a lighter-weight mode for less voice-confident players.

### Beat authoring rule (replaces the old drone segment — REMOVED)
All beat maps are authored with a **minimum 1.5s gap** between beats, enforced as a
SmartBeatMapper save-time warning. Dense-run runtime detection is retired on this branch
(VR branch keeps its drone segment). Also deleted: AnySegmentActive gating,
DroneRushSegment/Drone PC paths.

### Per-player input & voice architecture
- Striker executes with **key press OR shout-onset** (mic amplitude spike, not word
  recognition — Vosk latency can never be beat-accurate). Per-player setting.
- Each client runs its own Vosk locally; no voice audio is ever networked. Caller's
  recognized card is latched in the **declare window** and sent to the host as text.
- Combat uses **grammar mode** (recognition locked to the weapon's words — fast,
  accurate); the hub uses **FreeDictation mode** (full sentences). Both exist in
  VoskSpeechToText today (`FreeDictation` flag, built 2026-07-07).

### Stretch
- On-beat revive (downed partner revived by N consecutive EXCELLENTs).
- Duet beats (both players must hit — boss moments).

---

## 4. Weapons, Crafting & Loot (combat is item-based, v2, 2026-07-11)

**A weapon = ONE attack, ONE shouted word.** Each player equips up to **4 weapons**, one
per slot (Primary / Secondary / Defense / Co-op), giving each player 4 cards total — same
count as before, but now every drop is independently useful and mixing families across
slots (a Warhammer SMASH + a Katana DEFLECT + a Gauntlet RALLY, on one character) is the
core of build variety, not an exception to it.

### Slots (unchanged in purpose, now filled by separate weapons)
1. **Primary** — main attack, defines the loadout's identity.
2. **Secondary** — different family/rhythm feel from Primary.
3. **Defense** — Block OR Parry (each defensive weapon picks one).
4. **Co-op** — always affects the PARTNER (heal, wider window, absorb a miss).

Slots are **not type-locked** — a Ranged attack, an Arcane co-op card, and a Melee defense
can sit in the same loadout freely. Type affects flavor/component pools, not eligibility.
Every card still maps to an existing family (Strike/Throw/Block/Parry/Dash) → combat
engine, animations, RPS table untouched. A card is `word + family + window shape +
modifier`.

### CRAFTING — components (2–3 per weapon)
Loot drops **components, not finished weapons.** A weapon is assembled at a bench (or via
Griz — fits the negotiation system) from:
1. **Handle** (required) — carries the weapon's BASE STATS: base damage, base window
   width, which family it belongs to. This is what makes a weapon usable at all — 2
   components (Handle + Head) is a complete, fair, unexciting weapon.
2. **Head/Edge** (required) — carries the DAMAGE CURVE and STATUS TYPE: e.g. flat damage
   vs. EXCELLENT-only burst, bleed/stagger/slow on hit, wide-vs-narrow window shaping.
   This is where a weapon's core FEEL comes from.
3. **Keystone** (optional, 3rd slot) — the whacky one. Doesn't just tweak numbers, it can
   change what the attack DOES: SMASH-that-also-freezes, PARRY-that-echoes-to-partner,
   an attack that swaps families entirely on EXCELLENT. 2-component weapons are complete
   and fair; the Keystone is the build-defining, "this is MY weapon" slot.

Components are shared/reusable across many weapons (a "Cursed" Head fits any Handle of
the right family), so a small authored pool produces a large number of viable weapons —
variety comes from combinatorics, not hand-authoring every combo.

### Weapon types (now a component/flavor axis, not a slot lock)
| Type | Twist |
|------|-------|
| Melee | Baseline: full power on your own beats |
| Ranged | Weaker solo, but its Co-op-slot version acts on the PARTNER's beat (cover fire) |
| Arcane | Weak attacks, best utility — Keystones here are RUNES modifying the co-op effect |
| Exotic | Rule-breakers, one per run (Moonblade: ONE massive-damage weapon, no viable defense pairing intended) |

Launch target ~15-20 single-attack weapons built from a smaller shared component pool
(cheaper than ~10 hand-authored 4-card kits, and produces more effective builds). Legacy
4-card kit lists (Katana, Warhammer, etc., Appendix A) become the SEED CONTENT: each old
kit's 4 cards splits into 4 standalone weapons plus their Handle/Head/Keystone parts.

### ITEM QUALITY = NEGOTIATION OUTCOME (signature mechanic, unchanged in spirit)
Haggle too hard and you get the janky one — now expressed per COMPONENT, not per whole
weapon (so a clean Head on a janky Handle is a real, legible tradeoff):
- **Clean** (full price): component as designed.
- **Worn** (mid): one small defect — e.g. window shifted 0.1s late.
- **Janky** (floor price): a real defect with personality — misfires, a cooldown,
  caps your momentum streak (see §3). Griz WARNS you in character: *"At 55 you get the
  one that sticks. YOUR problem now."*
Defects are authored, not random stats, so every janky component is a character. Mixing
qualities across a weapon's 2-3 components is intentional: a clean Head + janky Handle is
a good attack you can't quite trust. (Open question: jank repairable later via Griz, or
forever?)

### Consumables (the shared resource game — anyone can call one)
One-word shouted items, limited uses per fight, bought/haggled from NPCs:
BOMB (aoe), SALVE (heal), OIL (next hit burns), CHALK (next window widened for whoever's
up).

### Loot rules
- Fights drop **gold + component crumbs**. Gold comes ONLY from fights.
- **Keystones (the whacky 3rd slot) never drop.** Acquired by TALKING: shops, quests,
  favors, the Bookie's bets. Talking is the acquisition path for build-defining content —
  that's the differentiator. Handles/Heads can drop in fights.
- Enemies visibly WIELD their weapons; beat them and their components show up on Griz's
  table next visit ("where'd you get this?" — "found it").
- Run modifiers can attach to a SLOT ("Defense slot: window +20%") independent of which
  weapon/components currently fill it.

### Traits = CLASSES (passive — zero voice load)
Pick ONE at run start (your class), loot more, max 3 equipped:
Berserker (attack windows +20%, defense −15%) · Sentinel (partner-miss damage to you
−50%) · Maestro (co-op cards +50%) · Duelist (EXCELLENT chains stack) · Loudmouth
(volume effects amplified). Build = trait + 4-weapon loadout + the component quality you
settled for.

---

## 5. Hub, NPCs & Voice Negotiation

Between fights both players walk a SHARED hub (the arena's backstage), **mics live**, and
talk to NPCs. Story is delivered light-touch: environmental + NPC memory, no cutscenes.

### Negotiation is a game system, not a chatbot
NPC hidden state (`price, patience, respect[player], fear`) moved by detected intents
(haggle, flatter, threaten, beg, offer-with-number, insult, ask, smalltalk, buy).
Personality = authored response pools gated by state. The classifier finds the player's
MOVE; the writing does the humor.

### Style reactivity (react to HOW players talk)
Volume (shouting = threat; whispering gets called out) · rambling vs. curt · interrupting
the NPC mid-line (he notices) · repetition ("you've flattered me twice, it's getting
weird") · talk-share between the two players → the NPC picks a FAVORITE and teases the
other.

### Design laws
- **Failure is content:** mishearings never dead-end. Griz is canonically hard of
  hearing — every recognition error is HIS character flaw and a comedy beat.
- **Co-op negotiation:** both mics at one NPC — good cop / bad cop as real mechanics
  (one farms patience/respect, the other hammers price).
- **Voice is the delight, never the wall:** every interaction also works with 2–3
  buttons; no progress gated on recognition.
- **NPC memory persists across runs** (the napkin ledger).

### NPC roster
1. **Griz — merchant/arms dealer.** ✅ PROTOTYPE WORKING (2026-07-07): GrizBrain.cs
   (intents + state machine + ~60 authored lines), GrizTest scene, free-speech Vosk,
   real offers via number parsing ("seventy gold" works), volume-aware threats.
   Sells weapons by quality tier (§4). First playtest verdict: fun.
2. **The Fixer — quest-giver:** run objectives ("win using only Parry — double pay").
   Literal-minded.
3. **The Bookie:** pre-fight bets on your own performance, negotiated odds.

### Tech tiers
1. ✅ Keyword-rule intents (shipped in prototype).
2. Sentence-similarity upgrade: MiniLM-class embeddings via Unity Sentis (~20MB, offline).
3. Optional LLM paraphrase layer for line variety — game must be great without it.

---

## 6. Overworld & Biomes

- Node map (For The King / Slay the Spire): 10–15 nodes, 2–4 branches; shared party
  token; both vote to move, disagreement after 10s = random of the two.
- Node types: Fight (majority), Hub/Shop, Event (rhythm micro-games: row on the beat,
  shrine chants, shout-haggling), Rest (heal, trade weapons/mods between players), Boss.
- **Biome = music genre + rule twist:**
  | Biome | Feel | Twist |
  |-------|------|-------|
  | Meadow (start) | Slow, wide windows | Teaching biome, no partner-damage |
  | Volcano | Fastest maps (1.5s floor) | Tighter windows |
  | Crypt | Syncopated / offbeat | Fake-out beats, parry-heavy enemies |
  | Finale | Medley | Boss mixes all rules |
- Boss = full song, phases keyed to sections; may force a Caller/Striker swap on chorus.

---

## 7. Networking

- **Transport:** Mirror + EOSTransport (Epic Online Services relay — free, join codes,
  no dedicated server; host = server + player A). Steam transport removed; Mirror
  transports are swappable so Steam can return later as packaging.
- **The latency law: never judge a remote player's beat timing on the server.**
  1. Host sends song start; clients clock-sync once (NTP-style).
  2. Each client judges its OWN hits locally against its local audio clock.
  3. Client sends compact results (`beatIndex, card, rating`); host resolves and syncs
     outcomes; animations are cosmetic per-client.
- Hub: transcripts/intents go to host as tiny text messages; NPC state is
  host-authoritative; response line IDs sync so both hear the same line.
- Known host-only hacks to fix (documented from DESIGN.md): BeginFromVoiceStart plain
  method, server-side timing evaluation in RhythmRoundManager, lobby auto-ready flow.

---

## 8. Build Order (risk-first)

1. **Transport swap:** Steam out, EOS in; existing fight runs with a second client
   connected and spectating.
2. **Clock sync + local beat judging refactor** (§7). Verify EXCELLENTs at 100ms+
   simulated ping.
3. **Declare/execute pipeline** with BOTH modes (Split Calling + alternating).
   One full fight, two real machines. ← *first fun milestone; playtest picks the core*
4. **Split Calling core:** dynamic per-beat targeting, Combo Calls, Danger Calls, shared
   momentum meter. Prove "nobody is idle" before adding crafting on top.
5. **Weapon + component layer:** WeaponDef (word + family + window shape) built from
   Handle/Head/Keystone ScriptableObjects; grammar built from the 4 equipped weapons.
   Start with ~4-6 single-attack weapons (one legacy kit split apart), clean tier only.
6. **Minimal map:** 5 nodes, 1 biome, fight/hub/boss, vote-to-move.
7. **Economy v1:** gold from fights, Griz sells components in 3 quality tiers, a basic
   crafting bench, consumables, run structure (HP carryover, party wipe, result screen).
8. Traits (Berserker + Sentinel), decision-load/auto-counter tiers by gear, remaining
   legacy kits split into weapons, second biome, Fixer + Bookie, drone-segment code
   removal, polish.

**Parallel track (running ahead of schedule):** Griz. ✅ Prototype passed the fun gate.
Next: fix mishears as they're found → embeddings → co-op mics → hub scene with walkable
characters.

---

## 9. Uniqueness Hooks (backlog — pick a few)

1. **Hype Meter:** louder EXCELLENT shouts fill a shared meter → Finisher card.
2. **Echo combos:** shout your partner's card within a beat of their EXCELLENT.
3. **Silencer enemy:** mutes one player's mic for a round — partner calls for both.
4. **Karaoke boss:** boss SHOUTS its attack; caller counter-calls what beats it.
5. **Biome vocabulary reskins** (football-event precedent from DESIGN.md).
6. **Custom song runs:** own MP3 + SmartBeatMapper (1.5s rule at save).
7. **Announcer learns your shouted fighter name** (local recording, never networked).

---

## 10. Open Questions

- [ ] Split Calling: how is "who's targeted this beat" actually decided — pure enemy
      telegraph, round-robin with jitter, or weighted by who's been idle longest?
- [ ] Combo Call words: fixed universal set (HARD/FAST/LOW) or per-weapon-family modifiers?
- [ ] Danger Calls: same declare-window timing as main attacks, or a shorter panic-window?
- [ ] Crafting bench: a physical station in the hub, or done through Griz directly
      (fits negotiation — "fit this Head for me" as a haggle-able service)?
- [ ] Enemy telegraph from day one (needs announcer VO or clear visual tell)?
- [ ] Jank repair: fixable via pay/sweet-talk, or forever (save for the clean one)?
- [ ] Component drops/shop stock: shared pool both players negotiate over, or per-player?
- [ ] Moonblade balance in co-op (partner carries the defense load — feature?).
- [ ] Do enemies get weapon movesets per biome (telegraphed by VO)?
- [ ] Griz voice output: text + gibberish mumble (cheap, charming) vs. VO vs. TTS?
- [ ] Solo mode: bot partner, or co-op only?
- [ ] Beat map A/B and lane tagging format in SmartBeatMapper.
- [ ] EOS org/account ownership.

---

## 11. Already Built (state as of 2026-07-08)

- Single-player rhythm combat: full loop (DESIGN.md) — RPS resolution, timing ratings,
  bot approach/animations, song picker, SmartBeatMapper.
- **Griz negotiation prototype** ✅ — GrizBrain.cs, GrizTestConsole.cs, GrizTest scene;
  Vosk FreeDictation mode added; swallowed-finals bug fixed.
- **CyberPixel art direction** — RimLit shader (posterized lighting, HDR rim, ink
  outline) on all converted Lobby materials; **beat-reactive rim glow** driven by
  FloorBeatColorizer (`_CyberBeatPulse` global). Pixelate render feature written but
  currently disabled (rejected after testing). Converter tool: Tools > CyberPixel.
- Mirror networking (host-only paths; needs §7 refactor). Steam transport present but
  slated for removal.

## Appendix A — Legacy 4-card kits (seed content for the component system, §4)

Each kit below splits into 4 standalone single-attack weapons; the kit's cards become
that weapon's Handle+Head defaults (Keystones are new, separate loot — see §4).

- **Katana** (melee/precision): SLASH · THRUST (+dmg on EXCELLENT) · DEFLECT (parry) ·
  FOCUS (partner window +30%)
- **Warhammer** (melee/tank): SMASH (narrow window, 2x) · QUAKE (enemy skips a beat) ·
  GUARD (block) · BRACE (absorb partner's next miss)
- **Gauntlets** (melee/combo): JAB (wide window, chains) · HOOK · WEAVE (dash) ·
  RALLY (heal partner on your EXCELLENT)
- **Longbow** (ranged/setup): VOLLEY · PIERCE (ignores block) · ROLL (dash) ·
  MARK (partner's beat: +50% their damage)
- **Twin Pistols** (ranged/bodyguard): FAN · SNIPE (EXCELLENT-only 2x) · DUCK (dash) ·
  COVER (partner's beat: intercept their miss)
- **Storm Staff** (arcane/healer): BOLT · SURGE · WARD (persistent block) · MEND (heal)
- **Chime Orb** (arcane/tempo): TOLL · BIND (parry) · SLOW (wider windows both players) ·
  ECHO (enables echo-combo)
- **Moonblade** (exotic): MOONFALL. That's it. That's the weapon.
- **Megaphone** (exotic): BLAST (dmg scales with mic volume) · TAUNT (retarget) ·
  AMP (boost partner's next card) · HUSH (block)

*Voice rule per weapon: 4 words, 1–2 syllables, Vosk-distinct (no rhymes/similar vowels —
DESIGN.md documents "start"→guard/card/star mishears).*
