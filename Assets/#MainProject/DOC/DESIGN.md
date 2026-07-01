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

---

## Bot System (BotBeatApproach.cs)

- Self-driving: Update() checks if free + beat within maxLeadTime, starts run immediately
- Run animation speed: 1.4f base, boosts up to 3f on fast beats
- Injured run: Run_Injured after taking a hit; clears after one clean round (NotifyRoundEnded)
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

---

## Drone Segment

### VR (DroneRushSegment.cs) — Elemental-Card-Changes branch
- Drones fly from portal near bot toward player
- Player punches (VR hands) or dodges (head movement)
- Drone.cs handles flight: arc/bob/sway/banking + EnergyGlove shader glow (red=right, blue=left)
- Drone assets: Assets/#MainProject/Characters/Drone/ (FBX + materials)

### PC (PCDroneSegment.cs) — Full-Game-without-VR branch
- Same dense-beat trigger (DENSE_GAP=1.35f, DENSE_MIN=2)
- Reuses Drone.cs for flight/glow via InitPC()
- Player dodges with mouse scroll (UP=dodge LEFT, DOWN=dodge RIGHT)
- Accumulated scroll input (scrollThreshold=0.5f) for responsiveness
- Occasional slash beats: shout (mic volume >= 0.3f) to parry
- Camera swoop on successful dodge (lean + slide + tilt)
- Bot drops to held stagger for duration
- AnySegmentActive static property gates all other systems

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
| MatchResultHud | UI/MatchResultHud.cs | End-of-match result (needs overlay rewrite) |
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
