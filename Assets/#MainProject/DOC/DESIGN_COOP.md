# DESIGN — 2P Co-op Voice Roguelite (working title)

**Status:** Design v3 (2026-07-08) — full rewrite for design review.
DESIGN.md remains the record of the shipped single-player systems.
**Engine:** Unity 2022.3 URP · **Networking:** Mirror + Epic Online Services relay ·
**Voice:** Vosk (offline STT) · **Branch:** Coop-Mode-With-PC

---

## 1. Vision (one paragraph)

Two friends, two mics, one run. **Your voice is your weapon and your wallet.** In combat,
one player CALLS attacks by shouting card names while the other TIMES them to the beat of
the music. Between fights, both players walk a shared hub and literally TALK to NPCs —
haggling, flattering, threatening — to get gear. Fights earn gold; talking turns gold into
weapons; weapons change how the fight plays. Roguelite runs across musical biomes.

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

## 3. Combat — CALLER & STRIKER (core structure)

One fighter, two brains. Per fight (or per round — see Open Questions), one player is the
**Caller** (mic) and one is the **Striker** (timing).

**The beat cycle:**
1. Enemy telegraphs its move ~1 beat early (announcer VO / visual tell).
2. **Caller** shouts a card from the Striker's equipped weapon — ideally the one that
   COUNTERS the telegraph (rock-paper-scissors table, unchanged from current game).
3. **Striker** executes on the beat: timing (existing EXCELLENT/GOOD/BAD ratings) plus
   DIRECTION (enemy approaches in a left/right lane — aim + time, two-axis skill).
4. Outcome resolves via the existing RPS + timing engine. Nothing in the resolution
   layer changes.

**Both roles are skills:**
- **Calling is a reflex + resource game:** counter the telegraph, manage consumables,
  decide when to spend momentum on a finisher, read your partner ("he keeps missing —
  calling wide-window cards until he settles").
- **Striking is execution + discipline:** timing, lane aiming, and FEINTS — enemies
  occasionally fake off-beat; pressing on a feint is a punished mistake.
- **Momentum:** consecutive EXCELLENTs fill a meter; the CALLER decides when to call the
  weapon's finisher.

**The call shapes the window:** every card has a timing-window width and damage defined by
the WEAPON (see §4) — e.g. SMASH is narrow/huge, JAB is wide/small. Caller choice = risk
management on the striker's behalf. This is where the co-op arguing/coaching lives, and it
is the point of the game.

**Role swap:** default swap each round. (Open question: fixed roles by preference vs.
forced swaps; boss songs may force a swap on the chorus as a panic moment.)

### Alternate mode: ALTERNATING BEATS (same pipeline, kept as a mode)
Caller & Striker and alternating beats are ~90% the same code — "declare card, execute
beat" with the declare coming from your own mic vs. your partner's. Build the pipeline
once, ship both modes, playtest to decide which is the headline. Alternating also serves
as the fallback/solo-adjacent mode. In alternating mode: enemy alternates targets A/B;
missing your beat damages your PARTNER (tunable).

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

## 4. Weapons, Items & Loot (combat is item-based)

**A weapon = the 4 words the Caller can shout** + the timing-window shapes the Striker
must hit. Loot is never stat inflation — it changes what can be SAID and how the beat
FEELS.

### Card slots (per weapon)
1. **Primary attack** — the weapon's identity.
2. **Secondary attack** — different family/rhythm feel.
3. **Defense** — Block OR Parry (weapon chooses → defensive personality).
4. **Co-op card** — always affects the partner (heal, wider window, absorb a miss).

All cards map to the existing families (Strike/Throw/Block/Parry/Dash) → the combat
engine, animations, and RPS table are untouched. A card is `word + family + window shape
+ modifier`.

### Weapon types
| Type | Twist |
|------|-------|
| Melee | Baseline: full power on your beats |
| Ranged | Weaker solo, but co-op card acts on the PARTNER's beat (cover fire) |
| Arcane | Weak attacks, best utility (heals, shields, window-bending) |
| Exotic | Rule-breakers, one per run (Moonblade: ONE overpowered card, no defense, no co-op) |

Launch target ~10 weapons (Katana, Warhammer, Gauntlets / Longbow, Twin Pistols / Storm
Staff, Chime Orb / Moonblade, Megaphone…). Full card lists in Appendix A.

### ITEM QUALITY = NEGOTIATION OUTCOME (signature mechanic)
Haggle too hard and you get the janky one. Same weapon, three tiers:
- **Clean** (full price): cards as designed.
- **Worn** (mid): one small defect — e.g. parry window shifted 0.1s late.
- **Janky** (his floor price): a real defect with personality — SLASH occasionally
  misfires, GUARD has a 1-beat cooldown. Griz WARNS you in character: *"At 55 you get
  the one that sticks. YOUR problem now."*
Defects are authored (not random stats) so each janky weapon is a character. You get
what you negotiated. (Open question: is jank repairable later — pay/sweet-talk Griz —
or forever?)

### Consumables (the Caller's resource game)
One-word shouted items, limited uses per fight, bought/haggled from NPCs:
BOMB (aoe), SALVE (heal), OIL (next hit burns), CHALK (striker's next window widened).

### Loot rules
- Fights drop **gold + crumbs** (consumables, materials). Gold comes ONLY from fights.
- **Rare weapons never drop.** They are acquired by TALKING: shops, quests, favors,
  the Bookie's bets. Talking is the acquisition path — that's the differentiator.
- Enemies visibly WIELD their weapons; beat them and it shows up on Griz's table next
  visit ("where'd you get this?" — "found it").
- Run modifiers attach to weapon SLOTS ("Defense slot: window +20%").

### Traits = CLASSES (passive — zero voice load)
Pick ONE at run start (your class), loot more, max 3 equipped:
Berserker (attack windows +20%, defense −15%) · Sentinel (partner-miss damage to you
−50%) · Maestro (co-op cards +50%) · Duelist (EXCELLENT chains stack) · Loudmouth
(volume effects amplified). Build = trait + weapon (+ jank you settled for).

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
3. **Declare/execute pipeline** with BOTH modes (Caller & Striker + alternating).
   One full fight, two real machines. ← *first fun milestone; playtest picks the core*
4. **Weapon layer:** WeaponDef ScriptableObjects (word + family + window shape),
   grammar built from the equipped weapon. Katana + Warhammer, clean tier only.
5. **Minimal map:** 5 nodes, 1 biome, fight/hub/boss, vote-to-move.
6. **Economy v1:** gold from fights, Griz sells the 2 weapons in 3 quality tiers,
   consumables, run structure (HP carryover, party wipe, result screen).
7. Traits (Berserker + Sentinel), ranged weapons, second biome, Fixer + Bookie,
   drone-segment code removal, polish.

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

- [ ] Role swap: per round, per fight, or player-chosen fixed roles?
- [ ] Enemy telegraph from day one (needs announcer VO or clear visual tell)?
- [ ] Jank repair: fixable via pay/sweet-talk, or forever (save for the clean one)?
- [ ] Weapon drops/shop stock: shared pool both players negotiate over, or per-player?
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

## Appendix A — Weapon card lists (draft)

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
