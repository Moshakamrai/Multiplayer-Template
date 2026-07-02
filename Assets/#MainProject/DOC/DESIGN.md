---
name: project-design-doc
description: "Full design and technical state of the Multiplayer Card Game — game loop, systems, branches, what's done and what's pending"
metadata: 
  node_type: memory
  type: project
  originSessionId: 7a6d0b0e-12e5-4aa0-885e-6e3688f6e517
---

# Multiplayer Card Game — Design & State Doc

**Engine:** Unity 2022.3 URP  
**Networking:** Mirror ([Server], [ClientRpc], [SyncVar])  
**Input:** New Input System (ENABLE_INPUT_SYSTEM)  
**Platforms:** VR (Meta Quest, branch: Elemental-Card-Changes) + PC flat (branch: Full-Game-without-VR)  
**Current working branch:** Full-Game-without-VR  

---

## Lobby ready-up (auto)
- Manual lobby ready handshake is NOT used. PlayerController.Start auto-calls SetReady(true) for
  the local human (bots already auto-ready in BotController). GameManager.ReadyToStart (runs while
  IsLobby) then fires ServerChangeScene(level) → arena loads → RhythmRoundManager.OnStartServer →
  round flow. Fixes "stuck on lobby, Not Ready 0 of 1" (LobbyUI shows that when the human never
  readies). Steam init errors are harmless/unrelated.

## Entry: "Say Start" (FIXED)
- BeginFromVoiceStart is now a PLAIN method (not [Command]/[Server]) — routing through networking
  silently no-op'd because Steam transport doesn't fully start; game runs as host so direct call works.
- RhythmRoundManager.Awake now does "last one wins" for Instance (was `if null`) + warns on dupes —
  a stale/second manager could hold Instance while a different one drew the panel.
- Vosk mishears shouted "start" as guard/god/hard/card/star/art — all accepted as start aliases.
- KEYBOARD FALLBACK: press Enter/Space at the "SAY START" prompt (works in builds, top of Update).
- Prompt shows "(or press Enter)".

## Combo chain panel
- "NEXT COMBO CHAIN" bottom-left panel (PlayerCombat OnGUI section 4) DISABLED via `if (false && ...)`.

## Old entry notes
- Flat/PC scene load → RhythmRoundManager sets WaitingForVoiceStart (SyncVar) instead of
  opening the picker directly. OnGUI draws a left-side "SAY \"START\"" prompt (DrawVoiceStartPrompt).
- VoiceCommandManager.ProcessWords checks for "start" FIRST (before combat/round gating) — saying
  "start" calls RhythmRoundManager.BeginFromVoiceStart() → ShowRoundPicker(). Accepts start/started/
  starts + fuzzy Match. Runs on host (server=client).

## Core Game Loop

1. Players pick a song from the song picker (ShowRoundPicker UI)
2. Song plays; beat map drives all timing
3. Each round: player shouts a card name → picks a move → bot runs in on the beat and executes it
4. Rock-paper-scissors outcome (strike beats nothing, block beats strike, parry beats block, etc.)
5. Score tracked per round; 3 rounds per match
6. Dense beat runs → Drone Segment fires (see below)
7. Match end → result screen

---

## Card System

- Player shouts card name via Vosk speech-to-text
- VoiceCommandManager matches transcript → card family
- Cards have families: Strike, Throw, Block, Parry, Dash
- Bot picks cards via BotController.ThinkNextMove (combo, taunted, single-move paths)
- CardManager gates card display during drone segment (AnySegmentActive)

**Voice recognition:** Vosk English model. Push-to-talk removed. Word count guard (>3 words rejected). Strict Match() with length-aware thresholds.

**Manual beat mapping (SmartBeatMapper.cs):** `quantizeEnabled` bool (default OFF) — when off,
taps save at their RAW time (no snap/quantize), for pure hand-mapping. When on, snaps to detected
beats / BPM grid. SnapTapsToBeats + NearestBeatOffset both early-out when off.

---

## Bot System (BotBeatApproach.cs)

- Self-driving: Update() checks if free + beat within maxLeadTime, starts run immediately
- Run animation speed: 1.4f base, boosts up to 3f on fast beats
- Injured run: Run_Injured plays for exactly ONE run-in immediately after an EXCELLENT-timed hit only
  (GOOD/BAD-timed hits don't limp), then auto-clears — not tied to round end anymore (old behavior
  limped for the rest of the round after any hit; changed per user request)
- Attack animations: Attack1 (Strike family), Attack2 (Throw family)
- arriveEarly = 0.28f — bot arrives slightly before the beat
- postBeatHold = 0.4f — holds at attack point, triggers hurt/BackDash animation, then zaps back
- returnSlideTime = 0.18f
- Slow motion on hurt
- Position 100% pin-driven via LateUpdate; CharacterController.Move disabled to prevent drift
- KnockbackRoutine disabled for bot
- [Server] attribute on Update — only server drives bot

**Hurt:** Single animation "Knockback" (was 4 random hurt states — simplified)  
**Fallback (no damage):** "BackDash" animation  

### Outcome-based rest positions (added)
The bot no longer always returns to its fixed home spot after a beat. Its rest position after each
beat now depends on the trade OUTCOME, and for a got-hit, on the ATTACKING HUMAN's shout-timing
rating:

- **EXCELLENT-timed hit** → `Outcome.GotHitExcellent` → hard **Knockback** animation, bot flies to
  `RhythmRoundManager.botHardHitPosition` (scene Transform reference, like the existing
  `botAttackPosition` pattern).
- **GOOD-timed hit** (not excellent, not bad) → `Outcome.GotHitGood` → **"Hurt 2"** animation
  (`BotBeatApproach.hurt2State`), bot settles at `RhythmRoundManager.botGoodHitPosition`.
- **BAD-timed hit** → still plain `Outcome.GotHit` → classic Knockback recoil, no special fly-off
  (stays at home — this preserves old behavior for a sloppy-timed hit that still lands).
- **Landed hit / Defended / Clash** (bot took no damage) → BackDash animation, bot goes to
  `RhythmRoundManager.botNoHitPosition` — this REPLACES the old plain home-return for all these cases.

All three position fields are optional Transforms on RhythmRoundManager (assign empty GameObjects in
the scene); if left unassigned, that outcome falls back to the bot's normal home spot.

**How the rating reaches the bot:** `PlayerCombat.LastTimingRating` (new field, "EXCELLENT"/"GOOD"/
"BAD") is set server-side in `RhythmRoundManager.EvaluateAndSendFeedback` (both chain and single-move
paths), right before the trade resolves. At the `ReportOutcome` call site, the ATTACKING human's
`LastTimingRating` (not the bot's) picks which `Outcome` variant to send.

**Critical mechanic change — persistent rest override:** `PlayerController.transform.position` is
always pinned to either `_approachPos` (while `SetApproachOverride` is active) or `_homePosition`
(otherwise) — see `PlayerController.cs:152`. The old code called `SetApproachOverride(home)` then
immediately `ClearApproachOverride()` at the end of every beat, which always snapped the bot back to
home regardless of any lerp target. To let the bot actually REST somewhere else between beats, the end
of `BotBeatApproach.RunIn` now does `SetApproachOverride(restTarget)` and deliberately does NOT clear
it — the override stays set until the next `RunIn` starts (which immediately re-overrides it anyway
for the new run-in). The old `LateUpdate` safety net that force-cleared the override every idle frame
was REMOVED (it would have undone this every frame) — `CancelApproach()` still explicitly clears the
override for real interruptions (disable, drone segment start, round reset), so nothing is stuck.
`RunIn`'s `start` position was also changed from a hardcoded `home` to `transform.position` (the bot's
true current rest spot), so the next run-in doesn't teleport from wherever it was resting.

**Fixed a conflicting bug in `BeginApproach`:** it used to unconditionally snap the bot to
`HomePosition` at the start of every single run-in (even when nothing was stale), which would have
silently erased the rest-position feature the instant the next beat started. Now it only snaps home
when a previous routine was genuinely still running (`_routine != null`, i.e. truly stranded
mid-flight) — a normal clean rest (at home, hard-hit, good-hit, or no-hit spot) is left alone, since
`RunIn` already starts from `transform.position`.

**Distance-aware lead time for close rest spots:** the self-driving `Update()` used to always wait
until `secsToBeat <= maxLeadTime` (flat 2.0s cap) before starting the run-in, regardless of how far the
bot actually had to travel. Since `botGoodHitPosition`/`botNoHitPosition` are intentionally close to the
attack point, this made the bot set off far too early from there. `IsRestingCloseToTarget()` (new)
checks whether the bot's current position is within 0.35m of either of those two Transforms; if so, it
computes the REAL lead time needed (`arriveEarly + distance/closeRestSpeedMps`, floored at 0.3s) and
uses that instead of `maxLeadTime` for this approach. `closeRestSpeedMps` (new field, default 3, matches
the ~3 m/s reference the run animation speed is scaled against in RunIn) tunes the estimate. Only
applies to good-hit/no-hit rest — from home or the hard-hit position the flat `maxLeadTime` still
applies (those are meant to be far, so the early head-start is correct there).

---

## Drone Segment

### VR (DroneRushSegment.cs) — Elemental-Card-Changes branch
- Drones fly from portal near bot toward player
- Player punches (VR hands) or dodges (head movement)
- Drone.cs handles flight: arc/bob/sway/banking + EnergyGlove shader glow (red=right, blue=left)
- Drone assets: Assets/#MainProject/Characters/Drone/ (FBX + materials)

### DroneRushSegment.cs updates (latest)
- NO self-bootstrap anymore (removed RuntimeInitializeOnLoadMethod + DontDestroyOnLoad) — it was
  spawning a duplicate ~DroneRushSegment with empty prefab that stole Instance ("prefab emptied on
  start"). Now lives ONLY as a scene object (DoneSegment) with Drone Prefab assigned.
- Skips the segment (red error, no invisible drones) if dronePrefab is null.
- Arrow mapping FIXED: uses playerRight = Cross(up, -toPlayer) so fromRight = screen-right =
  RIGHT arrow (was reversed). LEFT arrow → left drone (punchState), RIGHT → right drone (uppercutState).
- Drones lowered to glove height: gloveHeightOffset (-0.6) added to the arrival point.
- punchAnimSpeed (1.8) boosts human animator.speed during the segment, restored on end.
- Projectile on hit: FireProjectileAtDrone fires punchProjectilePrefab (or a runtime energy ball)
  from gloveMuzzleOffset to the drone, then PunchIntoBot. projectileTime, this-segment-only.

### DroneRushSegment.cs — arrow-key PUNCH (current) — MERGED
- MERGED: PCDroneSegment.cs DELETED; its PC logic now lives IN DroneRushSegment.cs (one script,
  the original name). Class is now a plain MonoBehaviour (was NetworkBehaviour), self-bootstraps
  as ~DroneRushSegment. Old VR hand-punch/head-dodge logic replaced entirely. AnySegmentActive
  is now just DroneRushSegment's own SegmentActive. Removed dead NotifyScore (was in PlayerCombat
  AddScore) + ResetForNewRound (was in RhythmRoundManager) calls. All PCDroneSegment.* refs across
  FloorBeatColorizer/CardManager/BotBeatApproach/GameManager/PlayerCombat/RhythmRoundManager
  updated to DroneRushSegment.
- NOTE: a leftover ~PCDroneSegment GameObject may still be in Lobby.unity as a missing-script —
  user re-adds DroneRushSegment component to a fresh scene object + assigns Drone Prefab.
- Bot stands IDLE (PlayerCombat.HoldIdle/ReleaseIdle — new; pins via IsStaggered flag but
  plays idleState not stagger pose).
- Drones spawn BEHIND the bot (spawnBehind), drift out to bot's LEFT or RIGHT (sideDrift,
  first 30% of travel), then fly STRAIGHT at the player's matching side (playerSideOffset),
  arriving ON a beat (travelTime lead). Fire-and-forget per drone (no shared state).
- HIT with ARROW KEYS (not dodge): LEFT arrow → punch a LEFT drone (punchState="Cross");
  RIGHT arrow → uppercut a RIGHT drone (uppercutState="Uppercut"). Correct key+side within
  hitWindow(0.55) around the beat → PlayLocalAttackAnim on player + camera kick + punch the
  drone into the bot (TakeDroneHit damage + score). Miss/wrong → drone hits you, bot scores.
- Left drone tinted blue, right orange. NO overlays/half-screen/parry/shout/double/dodge.
- Anim state names live in the card Animator (AnimName just returns trigger): Cross/Jab/Hook/
  Uppercut are real states. Configurable via punchState/uppercutState inspector fields.
- Tutorial overlay: "◄ LEFT ARROW = punch" / "RIGHT ARROW ► = uppercut", click to start.
- PlayerCombat additions: HoldIdle(), ReleaseIdle(), RpcPlayIdleHold(), PlayLocalAttackAnim(state).
- FloorBeatColorizer side-warning code now DEAD (SetSideWarning never called) — harmless, left in.

### (superseded) PC drone earlier design — flanking drones dodge/shout
- Same dense-beat trigger (DENSE_GAP=1.35f, DENSE_MIN=2)
- TWO drones hover statically flanking the bot (left + right), calm blue, no wobble.
- Per strike, timed to a beat:
  - SINGLE strike: one drone charges RED + that screen half pulses red (telegraphLead
    ~1s early, beeps via sin pulse). On beat it SWOOSHES straight at player (swooshTime
    ~0.32s, smooth, no wobble). Player CLICKS THE SAFE (opposite) half to dodge.
    Rule: "red side = danger = click the OTHER side." Correct → camera lean + deflect
    drone into bot. Wrong/late → hit, bot scores.
  - DOUBLE strike (doubleChance ~0.22): BOTH drones red, whole screen warns, "SHOUT!"
    prompt shows → player SHOUTS (mic >= shoutVolume 0.25) to blast both back.
- _clickSide latched in Update(); _warnLeft/_warnRight drive OnGUI red half-screen pulses.
- Drone tinted via its material _BaseColor/_RimColor/_FlowColor/_EmissionColor (EnergyGlove
  shader), calmColor↔dangerColor lerp by charge. VR Drone.cs flight component STRIPPED
  (we drive position directly for the static-hover + swoosh).
- Camera dodge: slide+tilt+yaw+duck, fast-out (camOutTime)/smooth-back (camBackTime).
- Mouse RETICLE drawn at cursor in OnGUI (cursor forced visible during segment).
- Cursor: GameManager.HandleCursor keeps cursor visible+unlocked while AnySegmentActive.
- Tunables: telegraphLead=1.0, minArrivalGap=1.1, reactWindow=0.9, swooshTime=0.32,
  droneSideOffset=1.1, droneHeight=1.5, droneForward=0.4, droneScale=1.3.
- FIRST-TIME TUTORIAL (once/session, _tutorialShown): pauses (timeScale=0 +
  PauseTrackClock), OnGUI overlay: "RED SIDE = DANGER, click other side" / "BOTH RED =
  SHOUT". Click to continue → ResumeTrackClock(realElapsed) keeps beats aligned.
- NOTE: old scroll-dodge + separate slash-flyer design REPLACED. slashPrefab removed.
  Drone.cs InitPC()/onDodged/onMissed callbacks now unused (harmless, left in place).
- Drones IDLE-FLOAT: pure vertical Y bob ONLY (NO rotation/spin) + a small VIBRATE that
  ramps up with charge (vibrateIdle→vibrateCharge). FloatLane in Update; floating=false
  while script-driving swoosh/deflect/return.
- Glow: LOW idle (calmGlow=0.7) → VERY HIGH on attack (chargeGlow=9), lerped by charge.
  Colour calmColor(blue)→dangerColor(red) by charge. droneForward=0 (on bot's Z),
  droneScale=1.5, sideOffset=1.2.
- FLOOR side lighting: tiles are horizontal LEFT→RIGHT (element 0 = leftmost). LEFT attack
  lights the FIRST sideWarnTileCount(3) tiles red; RIGHT attack lights the LAST 3.
  FloorBeatColorizer.SetSideWarning(left,right), sideWarnColor/sideWarnIntensity(10).
  Index-based (no world-X classification). Static Instance. PCDroneSegment feeds per-frame,
  clears on resolve + segment end.
- Screen-half danger: DrawDangerHalf() — bold red half-tint + bright edge band fading inward.
- RUN LOOP IS NOW FULLY SEQUENTIAL (DoStrike, single method for both single + double): one
  strike fully resolves (telegraph → act-now window → resolve → return home) before the next.
  Fixes the old overlapping-coroutine bug where two strikes fought over a lane's position/state
  ("vibrates+glows but doesn't move/attack", unresponsive). No more RunStrike/RunDouble/ResolveClick.
- ACT-NOW window: _actNow bool, opens reactWindow BEFORE the beat and closes reactWindow after
  (forgiving). Big CENTRE CUE in OnGUI tells you exactly what to do: charging shows "DANGER RIGHT
  → dodge LEFT"; on the beat it flashes green "◄◄ CLICK LEFT!" (or "SHOUT NOW!"). This is the
  main "when to hit" clarity fix.
- Camera dodge: NO flip — camYaw=0, camDuck=0 by default. Just a clean left/right slide + small
  tilt (camTilt=10). No head-turn.
- Idle float bumped visible: floatBob=0.25, floatSpeed=2.2, pure Y bob, no rotation.
- Floor warning tiles now IGNORE the segment dim (tileDim=1) so red reads bright. Index-based
  first-3 / last-3. swooshTime field removed (swoosh uses reactWindow).

### RhythmRoundManager pause helpers (added for tutorial)
- PauseTrackClock() — pauses audioSource; .time freezes (CustomTrack stays synced)
- ResumeTrackClock(realElapsed) — shifts _startTime forward by paused duration for
  built-in tracks, unpauses audio. Mirrors the tiebreaker's _startTime shift pattern.

---

## Key Scripts & Locations

| Script | Path | Notes |
|--------|------|-------|
| BotBeatApproach | AI script/BotBeatApproach.cs | Bot movement, timing, animations |
| BotController | AI script/BotController.cs | ThinkNextMove, StartApproach (now no-op) |
| PlayerController | Player Scripts/PlayerController.cs | HomePosition, pin via LateUpdate |
| PlayerCombat | Player Scripts/PlayerCombat.cs | Scoring, hurt, AddScore, AnySegmentActive gate |
| RhythmRoundManager | Round Scripts/RhythmRoundManager.cs | Beat timing, dense run detection, CheckForcedDroneRun |
| PCDroneSegment | Scripts/PCDroneSegment.cs | PC drone segment, self-bootstrapping singleton |
| DroneRushSegment | Scripts/VR/DroneRushSegment.cs | VR drone segment |
| Drone | Scripts/VR/Drone.cs | Drone flight/glow, InitPC() for PC use |
| VRCameraDriver | Scripts/VR/VRCameraDriver.cs | EditorVRSim = false (CRITICAL — was true) |
| VoiceCommandManager | VoiceRecognition/VoiceCommandManager.cs | No PTT, strict match, word count guard |
| FloorBeatColorizer | FloorBeatColorizer.cs | Dims during drone segment |
| CardManager | Card System/CardManager.cs | Hides cards during drone segment |
| MatchResultHud | UI/MatchResultHud.cs | Per-ROUND result now SCREEN-SPACE OnGUI overlay (ShowRoundResult → _roundOnly OnGUI card w/ pop-in + fade). MATCH-end Show() still 3D panels+leaderboard. |
| ScoreHud | UI/ScoreHud.cs | Anchors to bot HomePosition, LateUpdate |
| BeatCoach | UI/BeatCoach.cs | CoachingVisible = false (permanently off) |
| FighterCardVFX | VFX/FighterCardVFX.cs | No glove aura; effectSizeScale = 0.3f |
| PowerSurge | PowerSurge.cs | Kept but not wired to gameplay currently |

---

## Branch State

### Full-Game-without-VR (PC branch — active)
- Song picker works (editorAutoStart block fully removed)
- EditorVRSim = false
- Bot approach self-driving and working
- PC drone segment implemented (needs testing)
- MatchResultHud is world-space 3D panels (needs rewrite to screen overlay)
- Missing from VR branch: Drone FBX/materials (now copied over), Attack.wav (now copied over)

### Elemental-Card-Changes (VR branch)
- Full VR drone segment working
- All world-space UI (leaderboard etc.) — NOT wanted on PC branch
- Source of truth for: Drone prefab, Drone FBX, audio clips, VR scripts

---

## Drone Segment Trigger (how it activates)
- At map load, RhythmRoundManager scans loaded beat times for "dense runs":
  DENSE_MIN (2)+ consecutive beats each less than DENSE_GAP (1.35s) apart.
- Logs magenta `[DRONE] N forced drone run(s) detected` at load — check console.
- CheckForcedDroneRun (server, every frame) fires PCDroneSegment.StartSegment(run)
  when the song reaches (run[0] - travelTime).
- StartSegment now logs lime/orange diagnostics if it starts or bails.
- If NO segment appears: song likely has no 2-beats-under-1.35s section, OR
  dronePrefab unassigned (drones spawn invisible — warns in console now).

## Perf / Bug fixes
- CardActivationEffectRoutine (RhythmRoundManager ~2862): was doing
  GetComponentInChildren<Renderer>().material.color — grabbed a KriptoFX BFX_Decal
  renderer with no _Color property → threw "doesn't have color property _Color"
  EVERY FRAME during the flash (major log-spam lag) AND leaked a material instance
  per card. FIXED: picks first renderer whose sharedMaterial HasProperty(_Color),
  tints via MaterialPropertyBlock (no instance alloc).

## Known Issues / Pending

- [ ] MatchResultHud needs rewrite as screen-space Canvas overlay (currently ugly world-space 3D panels)
- [ ] PCDroneSegment needs in-game testing (just written)
- [ ] slashPrefab slot on PCDroneSegment needs an asset assigned in inspector
- [ ] Drone prefab needs assigning in PCDroneSegment inspector (or DroneRushSegment must be in scene for auto-pull)

---

## Design Decisions Made

- **No glove VFX** — weapon aura disabled entirely; blood VFX only
- **Hit VFX size:** effectSizeScale = 0.3f, slashSizeScale = 0.3f
- **Bot hurt:** single "Knockback" state only (not random Hurt 1-4)
- **Bot position:** fully pin-driven, no physics drift
- **Score HUD:** anchored to HomePosition not live transform
- **Beat coach:** permanently off
- **Voice:** no push-to-talk, strict matching, no loose aliases
- **Dense run threshold:** DENSE_GAP = 1.35f, DENSE_MIN = 2
- **Bot arrive early:** 0.28s before beat so animation has time to play

---

## Upcoming / Discussed

- Football World Cup event showcase (VR build on Elemental-Card-Changes branch)
  - Drone → football skin idea
  - Voice card aliases: SHOOT, WALL, COUNTER, NUTMEG (English words Vosk handles)
  - Stadium crowd audio during drone segment
  - "MAN OF THE MATCH" end screen
- MatchResultHud screen-space overlay rewrite
