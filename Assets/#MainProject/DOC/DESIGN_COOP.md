# CO-OP DESIGN — Rhythm Card Combat, 2-Player Roguelite

**Status:** Design v1 (2026-07-05) — supersedes nothing; DESIGN.md remains the record of the
current single-player systems. This doc describes where the game is GOING.

**Engine:** Unity 2022.3 URP · **Networking:** Mirror + Epic Online Services relay ·
**Input:** New Input System + Vosk voice · **Platform:** PC flat (branch: Full-Game-without-VR)

---

## 1. Vision

A 2-player co-op rhythm combat roguelite. Two friends travel a For The King-style node map
across musical biomes. At fight nodes, enemies run in on the beat of the song; players shout
their card (Strike / Throw / Block / Parry / Dash) and execute on the beat — by shouting or by
pressing a key, per-player choice. One run = one map. Die together, restart together.

**The identity of the game is voice.** Card selection stays voice-driven always. Only the
*beat execution* is swappable (shout vs. key), so a quiet player can still play.

---

## 2. Core Loop

```
Lobby (host + join via EOS code)
  → Overworld map (shared party token, pick route together)
    → Node: Fight / Event / Rest / Shop / Boss
      → Fight = song + beat map (existing combat loop, now alternating between players)
    → Loot: weapons (your 4 voice cards), slot modifiers, HP
  → Boss node ends the biome → next biome or run end
  → Run ends (win or party wipe) → result screen → back to lobby
```

Existing single-player combat loop (DESIGN.md "Core Game Loop") is preserved inside a fight
node. The song picker becomes the node's song (biome decides it); rounds-per-match becomes
enemies-per-node.

---

## 3. Co-op Combat — ALTERNATING BEATS (core structure)

The bot/enemy alternates its run-ins between Player A and Player B.

- Beat map is split into an A/B pattern at load (simple alternation by default; beat maps can
  later tag beats `A`, `B`, or `BOTH`).
- On YOUR beat: you shout your card during the declare window, then execute on the beat.
  Normal rock-paper-scissors resolution (existing outcome table).
- **Miss your beat → your PARTNER takes the damage.** This is the co-op glue: you protect
  each other by playing well. (Tunable: `missHurtsPartner` on/off for a gentler mode.)
- The non-active player is never idle-locked: they can shout a one-word **assist**
  ("HYPE!") once per N beats to widen the active player's timing window slightly.

### Dense beats: solved by AUTHORING RULE (drone segment + burst mode both cut)
**The drone segment is REMOVED entirely** (decision 2026-07-05). It existed because fast
beats left no time to shout a card AND hit the beat. Alternation halves each player's
density, and on top of that all beat maps are authored with a **minimum beat gap of 1.5s**
— so each player always has **≥3s** to declare and execute. That guarantee makes both the
drone segment AND any burst/card-lock fallback unnecessary; there is no dense-beat special
case in gameplay at all.
- Dense-run detection (DENSE_GAP/DENSE_MIN) is RETIRED from runtime on the PC branch.
- Instead, **SmartBeatMapper warns at save time** if any two taps are <1.5s apart, so the
  rule is enforced where it belongs: in the mapping tool, not in gameplay code.
- Also delete: `AnySegmentActive` gating in CardManager/PlayerCombat/FloorBeatColorizer,
  DroneRushSegment.cs, Drone.cs PC paths, HoldIdle/ReleaseIdle (PC branch only — VR branch
  keeps its drone segment untouched).

### Stretch structures (post-core, in priority order)
1. **On-beat revive** — downed partner revived by hitting N consecutive EXCELLENTs.
2. **Caller & Striker mutator** — optional biome/event modifier where one player shouts
   cards and the other executes beats, swap per round. (Was a candidate core; kept as spice.)

### Per-player beat input
`BeatInput = Shout | Key` — a per-player setting (lobby + pause menu).
- **Key:** Space (or bound key) pressed on the beat.
- **Shout:** mic volume ONSET on the beat (amplitude spike — same technique as the old
  double-drone SHOUT mechanic). NOT Vosk recognition timing — recognition latency
  (~200–400ms) can never be beat-accurate.

### Voice architecture (2 players)
- Each client runs its OWN Vosk instance on its own mic. No voice audio is networked.
- Card recognition happens in a **declare window** (from previous beat resolve until
  ~0.25s before your beat). Recognized card is latched locally and sent to host as part of
  the beat result. Late recognition = no card = auto-whiff (same as staying silent today).
- Crosstalk between friends on a call is mostly mitigated by alternating turns: only the
  active player's mic result matters for that beat.

---

## 4. Weapons & Cards (For The King-style movesets)

**A weapon = the 4 words you can shout.** (Decision 2026-07-05, replaces the global shared
card pool.) Players loot weapons on the map and carry ONE; your weapon defines YOUR 4 voice
cards. Two players with different weapons shout different vocabularies at the same fight.

**Engine stays intact:** every weapon card maps to an existing family (Strike / Throw /
Block / Parry / Dash) under the hood — a card is `voice word + family + modifier`. The
rock-paper-scissors table, timing ratings, animations, and RhythmRoundManager resolution
are untouched (same architecture as the football-alias reskin idea from DESIGN.md).

### Card slot structure (4 slots per weapon)
1. **Primary attack** — the weapon's identity (Strike or Throw family).
2. **Secondary attack** — different family / different rhythm feel (bigger damage,
   tighter window, chain bonus…).
3. **Defense** — Block OR Parry; the weapon chooses which → defensive personality.
4. **Co-op card** — ALWAYS affects the partner (heal, widen their next window, absorb
   their next miss-damage, enable an echo). Co-op is baked into every deck.

### Weapon TYPES (type = when/how you act, not just flavor)
| Type | Rule twist | Slot bias |
|------|-----------|-----------|
| **Melee** | Acts on YOUR beats, full power — the baseline | Strong attacks |
| **Ranged** | Weaker on own beats, but the co-op card fires on the PARTNER's beat (cover fire / intercepts) | Attack + intervention |
| **Arcane** | Weakest attacks, strongest utility/co-op: heals, persistent shields, window-bending for BOTH players | Support |
| **Exotic** | Rule-breakers, one per run max | Anything goes |

### Weapon roster (launch target ~10; Katana + Warhammer built first)
**Melee**
- **Katana** — SLASH (strike) · THRUST (throw, +dmg on EXCELLENT) · DEFLECT (parry) ·
  FOCUS (partner window +30%). *Precision.*
- **Warhammer** — SMASH (strike, 2x dmg, EXCELLENT-only) · QUAKE (throw, enemy skips next
  beat) · GUARD (block) · BRACE (absorb partner's next miss). *Tank.*
- **Gauntlets** — JAB (strike, 2-chain bonus) · HOOK (throw) · WEAVE (dash) ·
  RALLY (heal partner on your EXCELLENT). *Combo.*

**Ranged**
- **Longbow** — VOLLEY (throw) · PIERCE (strike, ignores block) · ROLL (dash) ·
  MARK (co-op, on partner's beat: their hit gains +50% dmg). *Setup.*
- **Twin Pistols** — FAN (strike) · SNIPE (throw, EXCELLENT-only 2x) · DUCK (dash) ·
  COVER (co-op, on partner's beat: intercept their miss, halve the damage). *Bodyguard at range.*

**Arcane**
- **Storm Staff** — BOLT (strike) · SURGE (throw) · WARD (block, shield persists one extra
  beat) · MEND (heal partner). *Healer.*
- **Chime Orb** — TOLL (strike) · BIND (parry) · SLOW (enemy run-in slows: wider window for
  BOTH players' next beats) · ECHO (enables partner echo-combo on your next EXCELLENT). *Tempo mage.*

**Exotic**
- **Moonblade** — MOONFALL (strike, massive dmg) — and NOTHING else. The FTK homage: one
  overpowered card, no defense, no co-op. Someone will always pick it.
- **Megaphone** — BLAST (strike, damage scales with mic VOLUME) · TAUNT (enemy retargets
  to you) · AMP (partner's next card boosted) · HUSH (block). Ties into the Hype Meter (§10).

### Traits = CLASSES (passive cards — the FTK attribute layer)
Traits are the class system: **passive, never shouted** — zero extra Vosk load. Pick ONE
at run start (that's your class), find more as run loot, **max 3 equipped**.
- **Berserker** — attack timing windows +20%, defense windows −15%.
- **Sentinel** — damage you take from partner misses −50%.
- **Maestro** — your assists and co-op cards 50% stronger.
- **Duelist** — consecutive EXCELLENTs stack a damage chain.
- **Loudmouth** — all mic-volume-based effects amplified (Hype Meter, Megaphone).
Build identity = trait + weapon: Sentinel+Warhammer (bodyguard), Maestro+Storm Staff
(healer), Loudmouth+Megaphone (the screamer). Traits and weapons drop separately, so
every run remixes the combo.

### Voice constraints (hard rules per weapon)
- The 4 words must be Vosk-distinct: different syllable counts/vowels, no rhymes
  (DESIGN.md documents "start" mishearing as guard/card/star — this is real).
- One or two syllables, common English words only.
- Cross-player collisions are tolerable (players alternate beats; only the active
  player's mic result counts), but avoid them in weapon pools where cheap.

### Loot loop
- Weapons drop at fight/shop nodes from biome-flavored pools; rarity tiers.
- Run modifiers (§6) attach to a weapon SLOT ("Parry slot: window +20%") — a build =
  weapon + stacked slot modifiers.
- Weapons can be swapped at rest nodes and TRADED between players — handing your friend
  the Warhammer because they keep missing beats is the co-op conversation.

### Implementation shape (mostly data)
- `WeaponDef` ScriptableObject: name, rarity, biome pool, 4 card entries
  (word, Vosk aliases, family, modifier id).
- `VoiceCommandManager` matches against the player's CURRENT WEAPON's words (not a global
  list); `CardManager` displays the weapon's 4 cards.
- Combat resolution unchanged.

---

## 5. Overworld & Biomes

### Map
- Node graph, For The King / Slay the Spire style: 10–15 nodes, 2–4 branches wide.
- One shared **party token** — players move together. Route choice: both players vote by
  clicking a node; agreement moves; disagreement after 10s = random of the two (arguing is
  the fun). Host-authoritative movement.
- Node types: **Fight** (majority), **Event** (rhythm micro-game or choice), **Rest**
  (heal, swap upgrades), **Shop**, **Boss** (biome finale).

### Biomes = music genres
| Biome | Tempo/feel | Gameplay twist |
|-------|-----------|----------------|
| Meadow (start) | Slow, wide timing windows | Teaching biome, no partner-damage on miss |
| Volcano | Fastest maps (gaps at the 1.5s floor) | Relentless alternation, tighter timing windows |
| Crypt | Syncopated, offbeat | Fake-out beats, Parry-heavy enemies |
| Finale | Mixes all | Boss medley song |

Each biome defines: song pool + beat maps, enemy/drone skins, ambient palette
(FloorBeatColorizer theme), and 1 rule twist. Boss = full song with phases keyed to sections.

### Non-combat rhythm events (cheap — reuse beat judging)
- Row a raft on the beat together to cross water (both inputs, forgiving).
- Shrine chant: shout the displayed word on 4 beats for a blessing (upgrade).
- Shout-haggle at the shop: hit 3 beats to knock the price down.

---

## 6. Roguelite Progression

- **Run-based.** Nothing persists between runs except cosmetics/unlocked songs (later).
- Party shares HP pool? NO — individual HP, but miss-damage goes to partner (see §3).
  Party wipes when BOTH are down (revive window while one stands).
- **Loot = weapons (§4) + slot modifiers.** Modifiers attach to a weapon card SLOT for
  the rest of the run:
  - "Defense slot: window +20%" · "Primary: EXCELLENT deals double" · "Dash refunds a miss (1/fight)"
  - "Echo: partner's assist also gives YOU the wider window"
- Rest nodes allow trading weapons and modifiers between players.
- 3 rounds/match → reframed as N enemies per fight node; boss nodes = 1 long song.

---

## 7. Hub, Story & Voice NPCs (decision 2026-07-07)

**The second half of the hook: your voice is your weapon in combat — and your tongue in
the hub.** Between fights, both players walk around a SHARED hub (the arena's backstage /
underbelly), mics live, and TALK to NPCs: negotiate prices, charm quest-givers, talk
their way into trouble. Story is delivered light-touch: environmental + NPC memory across
runs, no cutscenes.

### Tech tiers (build in this order)
1. **Keyword-rule intents** (weekend prototype): Vosk transcript → rule scoring → intent.
2. **Sentence similarity**: MiniLM-class embedding model via Unity Sentis (~20MB, offline,
   free) matching transcripts to intent examples. Ship tier.
3. **LLM paraphrase layer** (optional, later): rewrites authored beats for line variety.
   The game must be great WITHOUT this tier.

### Negotiation is a GAME SYSTEM, not a chatbot
NPC hidden state (`price`, `patience`, `respect[player]`, `fear`) moved by detected
intents (`haggle`, `flatter`, `threaten`, `beg`, `ask_info`, `insult`, `smalltalk`,
`buy`). Personality = authored response pools gated by state — the classifier finds the
player's MOVE; the writing does the humor.

### Style reactivity (react to HOW players talk — all cheap signals)
- **Volume** (already captured): shouting = threat modifier; whispering gets called out.
- **Word count**: ramblers get cut off; curt players get "a man of few words. I respect it."
- **Interruption**: mic spike while NPC is mid-line → he notices and objects.
- **Repetition**: third flattery in a row gets flagged in-character.
- **Talk-share between the two players**: the NPC develops a FAVORITE and teases the other.

### Design laws
- **Failure is content**: mishearings never dead-end — NPC #1 is canonically hard of
  hearing, so every Vosk mangle is HIS character flaw and a comedy beat. Clips of this
  are the marketing.
- **Co-op negotiation**: both mics live at one NPC — good cop / bad cop as a real
  mechanic (one player farms `patience`/`respect`, the other hammers `price`).
- **Voice is the delight, never the wall**: every interaction also works with 2–3 button
  choices; no progress gated on recognition.
- **NPC memory persists across runs** (debts, grudges, favorites — a save file, cheap).

### NPC roster (build ONE first, learn, then template)
1. **Griz — merchant/arms dealer** (FIRST): slightly deaf (load-bearing trait), keeps a
   grudge "ledger" (a napkin), plays favorites, respects loud confidence, secretly
   cowardly (`threaten` + volume works — once). The negotiation showcase.
2. **The Fixer — quest-giver**: run objectives ("win a fight using only Parry — double
   pay"). Literal-minded; takes everything you say at face value.
3. **The Bookie**: bets on your own performance; negotiate the odds with them.

### Networking note
Voice audio is NEVER networked. Each client's Vosk transcribes locally; transcripts/
intents go to the host as tiny text messages; NPC state is host-authoritative; response
line IDs sync so both players hear the same authored line.

## 8. Networking Architecture

### Transport: Epic Online Services (decision made)
- Mirror + **EOSTransport** (community transport for Mirror) from day one — all testing is
  real internet play. Free, no Steam dependency, NAT punchthrough via EOS relay.
- Requires an Epic dev account + product/sandbox/deployment IDs (free). Players connect by
  **join code / lobby id** — host creates, friend enters code. No dedicated server:
  **host = server + player A**, client = player B.
- Steam remains a future packaging option — Mirror transports are swappable; write nothing
  Steam-specific. Remove/disable the current Steam transport + its init path.

### The latency rule (MOST IMPORTANT TECHNICAL DECISION)
**Never judge a remote player's beat timing on the server.**
1. On song start, host sends `(songId, songStartDspTime)` and clients run a small
   clock-sync handshake (ping/2 offset, NTP-style, a few round trips, done once).
2. Each client plays audio locally and judges its OWN hits against its LOCAL clock
   (`AudioSettings.dspTime`-based, like current RhythmRoundManager judging).
3. Client sends a compact result: `CmdBeatResult(beatIndex, card, rating)`.
4. Host resolves the trade (rock-paper-scissors + outcome table), updates SyncVar state
   (HP/score), and RPCs the outcome so both clients play animations/VFX.
5. Bot run-in animation is cosmetic per-client, driven by each client's local beat clock —
   NOT by server position sync (kills the current `[Server]` Update + position-pin-over-
   network approach for remote clients; host still simulates authoritatively).

### Known host-only hacks that MUST be fixed (from DESIGN.md)
- `BeginFromVoiceStart` was made a plain method because networked routing no-op'd under the
  broken Steam transport → must become a real `[Command]`-capable path again.
- `RhythmRoundManager` server-side timing evaluation (`EvaluateAndSendFeedback`) assumes the
  shouting player is local to the server → split into local-judge + server-resolve.
- Auto-ready lobby flow (PlayerController.Start → SetReady(true)) is fine for co-op but the
  scene-change flow needs testing with a real second client.
- `DroneRushSegment` — no longer a fix needed: the segment is DELETED on the PC branch
  (see §3 dense-beats authoring rule). Removal itself is a task: strip the script, scene object, and all
  `AnySegmentActive` call sites.

---

## 9. Build Order (risk-first)

1. **Transport swap:** remove Steam, install EOSTransport, host+join via code. Existing
   single-player fight must run with a second client merely CONNECTED and spectating.
2. **Clock sync + local beat judging refactor** (§8). Verify: remote client's EXCELLENT
   ratings match what they see/hear locally at 100ms+ simulated ping (Mirror latency sim).
3. **Alternating beats:** A/B beat assignment, partner-damage, per-player BeatInput setting,
   second PlayerCombat/anim path. One full fight, 2 real machines. ← *first fun milestone*
4. **Minimal map:** 5 nodes, 1 biome (Meadow), fight/rest/boss only, vote-to-move.
5. **Weapon layer:** WeaponDef ScriptableObjects, VoiceCommandManager matches current
   weapon's words, CardManager shows weapon cards. Katana + Warhammer first (co-op cards
   included — they're the point).
6. **Run structure:** HP carryover, party wipe, weapon drops + 2–3 slot modifiers as loot,
   result screen (reuse MatchResultHud rewrite — still pending from DESIGN.md).
7. **Drone segment removal** + SmartBeatMapper 1.5s-gap save warning, then events, shop,
   traits (start with Berserker + Sentinel), ranged/arcane weapons, second biome, polish.

**Parallel track (any time, single-scene prototype): Griz the merchant (§7).**
Keyword-rule intents + price/patience state + ~40 authored lines. Judge ONE thing: is
haggling with him fun for 5 minutes? If yes → embeddings, style reactivity, co-op mics,
hub scene. If no → fix the writing, not the tech.

Do not build biome content before milestone 3 is fun on two real machines.

---

## 10. Uniqueness Hooks (idea backlog — pick a few, not all)

Ideas to make the game unmistakably ITS OWN thing. Rough priority order by
(impact ÷ effort); none block the §7 milestones.

1. **Hype Meter (shared, volume-driven).** Shout LOUDER on an EXCELLENT and the shared
   Hype Meter fills faster. Full meter = one **Finisher card** either player can call by
   name. Makes volume itself a mechanic — no other game rewards literally screaming with
   your friend. Cheap: mic amplitude is already read for shout-onset.
2. **Echo combos.** Within one beat after your partner's EXCELLENT, shout the SAME card
   name to "echo" it for bonus damage. Creates the signature co-op sound of the game: two
   people yelling "PARRY! PARRY!" in sequence. Only needs the non-active mic listening for
   one specific word.
3. **Silencer enemy.** A biome enemy that curses ONE player — their mic goes dead for a
   round and their PARTNER must call cards for them (mini Caller & Striker, see §3
   stretch). Turns the stretch mutator into an enemy gimmick instead of a mode.
4. **Karaoke boss (counter-calling).** The boss SHOUTS its own attack ("STRIKE!") via
   announcer VO a beat early — you must shout the card that BEATS it. Tests the
   rock-paper-scissors table as a voice reflex; pure audio telegraphing, very cheap.
5. **Biome vocabulary reskins.** Each biome renames the five card words (the football
   SHOOT/WALL/COUNTER/NUTMEG idea from DESIGN.md, generalized): Crypt = incantations,
   Volcano = battle roars. Same families, new Vosk aliases — content, not code.
6. **Custom song runs.** Players drop in their own MP3, map it with SmartBeatMapper
   (1.5s rule enforced at save), and the run's final biome uses their song. Shareable
   maps later. Big hook for streamers/friends, tooling mostly exists.
7. **Announcer that learns your names.** Lobby asks each player to shout their fighter
   name once; the crowd/announcer uses the recorded clip for round intros and MAN OF THE
   MATCH. Recorded locally, played locally — nothing networked or stored.

## 11. Open Questions

- [ ] Beat map A/B tagging format — extend SmartBeatMapper output or tag at load?
- [ ] Does the non-host player need the bot to be a NetworkIdentity at all, or fully
      client-side cosmetic with host-only resolution? (Leaning: cosmetic.)
- [ ] Assist mechanic ("HYPE!") — v1 or cut? (Cheap, but adds a second live mic path.)
- [ ] Solo mode: keep playable alone (bot partner / all beats yours) or co-op only?
- [ ] EOS account/product setup — who owns the Epic org?
- [ ] Weapon drops: one shared drop both players negotiate over, or per-player drops?
- [ ] Moonblade balancing: how does a no-defense player survive alternating beats?
      (Leaning: their partner's co-op cards are the counterweight — or partner damage
      doubles, making it a duo decision, not a solo pick.)
- [ ] Do bot/enemies also get weapon movesets per biome (telegraphed by announcer VO)?

## 12. Explicitly Preserved from Current Game

- Voice card selection (Vosk, strict match, no PTT, >3-word guard)
- Rock-paper-scissors outcome table + timing ratings (EXCELLENT/GOOD/BAD)
- Bot run-in feel: arriveEarly 0.28s, outcome-based rest positions, injured-run rule
- Dense-run detection — RETIRED at runtime on PC branch; replaced by the 1.5s minimum beat
  gap authoring rule (enforced as a SmartBeatMapper save-time warning). Drone segment removed
  on PC branch; VR branch keeps its version.
- SmartBeatMapper manual mapping workflow
- "Say START" entry (plus Enter fallback) — becomes the host's fight-node start
