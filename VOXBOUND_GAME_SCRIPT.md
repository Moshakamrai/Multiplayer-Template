# VOXBOUND — Complete Game Overview & Player Guide

## Core Concept: "Physical Magic"
**VoxBound** is a revolutionary competitive multiplayer rhythm-fighting game where **your voice and timing ARE your weapon**. No button-mashing. No controllers. You shout combat commands precisely when the beat hits—the closer you are to the beat, the stronger your attack. The game combines three unique systems: **voice recognition**, **rhythm-based combat**, and **card-based resource management**.

---

## 🎮 THE THREE GAME MODES

### 1. **SLOW RHYTHM** (Classic 1v1)
- **Duration**: 90 seconds
- **Beats**: Every 4 seconds (22 total impacts)
- **Playstyle**: Single-move, one attack per beat
- **Perfect for**: Learning fundamentals, precise timing practice
- **Your voice matters**: Timing offset of ±0.10s = **EXCELLENT** (+25% damage)
- **Audio**: Calm, deliberate pacing

### 2. **FAST COMBO** (Cluster Chain Attacks)
- **Duration**: ~52 seconds structured chaos
- **Beat Structure**:
  - **Intro (8-10s)**: 4-hit rapid cluster at 0.6s gaps — learn the feeling
  - **Half-Beat Burst (13-14s)**: 4 hits in 0.3s gaps — *lightning fast*
  - **Engagement Filler (15-38s)**: Single strikes at 4s intervals (think time)
  - **BIG DROP (38.4s)**: 4-hit cluster, then fast burst before loop
- **Playstyle**: Multi-move chains (4 attacks queued in advance)
- **Unique Hook**: A single shout covers 2-4 hits. One good timing = carries through the whole cluster
- **Your voice matters**: Loudness scales damage (up to +25% for loud shouts)
- **Audio**: Electronic, intense buildup

### 3. **CUSTOM TRACKS** (Player-Mapped Beatmaps)
- **How it works**: You tap out any MP3/WAV at your own tempo
- **Smart Quantization**: Taps snap to a BPM grid automatically
- **Dynamic Chaining**: Tightly-spaced taps (< 1.4s gap) auto-group into chains
- **Seamless Looping**: At track end, server waits for exact silence, snaps clients to 0:00, replays
- **Playstyle**: Single or Combo, depending on tap density

---

## 🎤 VOICE INPUT SYSTEM (The Secret Sauce)

### Voice Recognition Engine
- **Technology**: Vosk (offline, low-latency)
- **Grammar**: Constraint-stripped to only combat words (no 100k-word dictionary overhead)
- **Combat Words**:
  - **Attacks**: "Punch" → Jab, "Flank/Blank/Frank" → Cross, "Hook", "Crush/Crash" → Boom (Unbreakable)
  - **Defense**: "Block", "Cage" → Parry, "Left", "Right" → Dodges
  - **Meta**: "Cancel" → undo last input

### Volume Spike Grading
The game doesn't just recognize *what* you said—it measures **when** (millisecond precision) and **how loud** your mic spiked.

**Timing Grades**:
- **EXCELLENT**: ≤ 0.10s offset from beat → +25% damage, bonus energy
- **GOOD**: ≤ 0.30s offset (varies by move) → base damage, standard energy
- **BAD**: Late or no spike → -50% damage penalty

**Volume Bonus** (Combo Mode only):
- Louder shouts (mic spike > 0.4 absolute volume) = up to +25% damage scaling
- Quadratic scaling: quiet shouts barely help, but *screaming* is rewarded

### Shout Window (The Timing Zone)
- **Standard Window**: 0.50 seconds before beat impact
- **Pressure System**: Recent hits make you anxious → window shrinks up to 40% at max pressure
- **Dead Zone**: 0.20s post-beat silence required (prevents you holding a shout across beats)

---

## ⚔️ COMBAT MECHANICS: Rock-Paper-Scissors with Timing

### The Four Core Moves (Single Mode)

#### 🥊 **JAB** (Punch)
- **Rock**: Fast, reliable lead strike
- **Beats**: Dodges (Left/Right) if they nail timing
- **Beaten by**: Hook (side-swing overpowers quick jabs)
- **Blocked by**: Block defense (100% negation)
- **Base Damage**: 10 HP
- **Best for**: Interrupting, chain-starters

#### 💥 **CROSS** (Blast)
- **Paper**: Straight power hit, counters dodge escapes
- **Beats**: Jabs, Dodges (dodge can't sidestep a straight line)
- **Beaten by**: Hook (heavier weight) if both land
- **Blocked by**: Block (100% negation)
- **Base Damage**: 15 HP
- **Energy**: Grants bonus energy on successful land
- **Best for**: Mid-combo damage, repositioning

#### 🪝 **HOOK** (Heavy Side-Swing)
- **Scissors**: Heavyweight attack, breaks through Block defense
- **Beats**: Jab, Cross, other defenses (except Cage/Unbreakable)
- **Beaten by**: Cage parry (reflects back double damage)
- **Dodgeable**: Yes (Left/Right at perfect timing)
- **Base Damage**: 25 HP
- **Special**: Breaks 100% of Block mitigation, grants +1 Energy on land
- **Best for**: Forcing parries, powering through guards

#### ⚡ **UNBREAKABLE (Boom)**
- **Trump Card**: Tightest window (0.2s timing window), highest reward
- **Beats**: Everything except Cage parry and other Unbreakables
- **Dodgeable**: Yes, but strict (0.3s window)
- **Base Damage**: 15 HP (timing-dependent, can scale to 18+ with EXCELLENT)
- **Trade-off**: Tiny input window, easy to miss entirely
- **Best for**: Finishing, punishing staggered opponents, power plays
- **Risk**: Timing window so tight, many players whiff it

---

### The Four Defense Moves

#### 🛡️ **BLOCK** (Standard Guard)
- **Absorbs**: 100% of incoming damage (except Hook, Boom breaks it)
- **Timing Window**: 0.40s (generous, forgiving)
- **Cost**: Defense slot consumption
- **Hook Counter**: Hook shatters Block → 0% mitigation, full damage
- **Boom Counter**: Unbreakable ignores Block completely
- **Use Case**: Safe default, reactive defense

#### 🪞 **CAGE (Parry/Elite Block)**
- **Reflection**: Returns 120% damage back to attacker
- **Tiny Window**: 0.30s before beat (hardest timing move in the game)
- **Risk**: If you miss the window, you take full damage
- **Reward**: Grants +1 Energy on successful parry
- **Beats Unbreakable**: Yes, bounces Boom back as damage
- **Use Case**: High-risk, high-reward for confident players

#### 🚴 **LEFT/RIGHT (Dodge)**
- **Evasion**: Complete sidestep of Jab, Hook, Unbreakable
- **NOT Dodgeable**: Cross/Blast hits straight → unhittable
- **Timing**: 0.30s window
- **Cost**: Movement slot + action slot
- **Mobility**: Moves you toward opponent or away
- **Use Case**: Evade + reposition in one action

---

### The Combo Mechanic (Fast Combo Mode)

**How It Works**:
1. You queue 4 moves at game start
2. Server auto-fires them in sequence (0.3s - 0.6s apart)
3. **One shout = covers all 4 hits** (as long as you're loud enough)
4. Each hit grades on how far from the beat it lands

**Example Combo Chain**:
- Beat fires at 13.0s, you shout at 13.05s
- Hits 1, 2, 3, 4 fire at 13.0, 13.3, 13.6, 13.9 — all grade as "EXCELLENT" because you shouted at 13.05s

**Timing Grade in Chains**:
- **EXCELLENT**: ≤ 0.10s from first beat → 120% damage
- **GOOD**: ≤ 0.30s from beat → 100% damage
- **BAD**: No spike or too late → 50% damage

---

## 💳 CARD SYSTEM: Resource Management (The Secret Depth)

### Slot Economy
Every input window, you have:
- **3 Attack Slots**: Spend one per attack (Jab, Cross, Hook, Unbreakable)
- **2 Defense Slots**: Spend one per defense (Block, Cage, Left, Right)

**What Refills Slots?**
- **Idle Recharge**: Skip an input window → +1 to both pools
- **Winning Trades**: Land a hit → gain defense slot; Block successfully → gain attack slot
- **Parry Success**: Cage a hit → gain attack slot (rewarding risky play)
- **Stagger Recovery**: Clear stagger by shouting → slots reset to max

### Same-Card Cooldown
You can't use the same move **two windows in a row**.
- **Why?** Prevents spam of one powerful move (e.g., Hook every beat)
- **Cooldown Rolling**: If you use Hook on beat N, it's blocked on beat N+1, available again on N+2

### The Stagger Mechanic: VoxBound's Comeback Hook

**How You Get Staggered**:
- Both attack/defense slots are 0 AND neither you nor opponent landed a hit → **automatic 3-beat stagger**
- Stagger = dazed, slow, weakened for 3 incoming beats

**How To Escape Stagger** (3 methods):
1. **Charge + Shout**: Hold mic at ≥0.28 volume, fill a bar to 100% → instant escape
2. **Timing Escape**: Shout with good volume (≥0.25) in the 0.50s shout window before a beat → auto-queue a Block, escape
3. **Time Decay**: If you survive all 3 beats of stagger (do nothing, eat damage), stagger clears automatically

**Stagger Window**:
- Bar fills based on remaining beats × estimated beat interval
- Goal: Refill bar in ~65% of available time window
- Example: 3 beats remaining, 2s per beat interval → need to fill bar in ~4 seconds of sustained ≥0.28 volume shouting

**Why It's Brilliant**:
- **Skill-based comeback**: Screaming your lungs out literally saves you
- **Momentum swing**: Win a trade → opponent staggers → they're vulnerable
- **High-tension moments**: Down to 50 HP, staggered, 3 beats to recover — do you commit to shouting loud or save your voice?

---

## 🎵 RHYTHM ENGINE: Beating the Beat

### The Beatmap System

**Slow Rhythm**:
- Beats every 4.0 seconds for 90 seconds
- 22 impacts total
- Simple, learnable rhythm

**Fast Combo**:
- Clusters at 0.3-0.6s gaps create rapid-fire sequences
- 52-second total arc with buildup, peak, final burst
- Requires faster decision-making

**Custom Tracks** (The Tool):
- **Recording**: Tap out any song's beats on the SmartBeatMapper
- **Quantization**: Taps auto-snap to the nearest grid subdivision (BPM-driven)
- **Chain Detection**: If taps are <1.4s apart, group them into a chain cluster
- **Persistence**: Saved to PlayerPrefs as pipe-delimited times
- **Looping**: At track end, silence detection waits for quiet → snaps all clients to 0:00 → replays seamlessly

### WindUp Animation (0.53s before beat)
- 0.53s before beat fires, both players get a **visual/audio cue**
- Bot thinks its next move
- Chain attackers get their moves assigned visually
- **Why?** Gives players time to react, syncs server <> client animations

### Hit Stop (Time Dilation)
- **Standard Hit**: 0.06s at 0.05x time-scale (brief slow-mo crunch)
- **Heavy Hit** (Unbreakable): 0.12s at 0.02x time-scale (longer, crunchier)
- **Why?** Makes impacts *feel* meaty, gives visual feedback that something happened

---

## 🏆 HOW DAMAGE WORKS: Timing + Volume + Cards

### Damage Formula
```
BaseDamage = Move-specific (Jab=10, Cross=15, Hook=25, Boom=15)
TimingMultiplier = 1.25× (EXCELLENT ≤0.10s) / 1.0× (GOOD ≤0.30s) / 0.5× (BAD)
VolumeBonus = Up to 1.25× (only in Combo mode, for loud shouts)
BlockMitigation = 1.0× (full block) vs 0.0× (Hook breaks it)
FinalDamage = BaseDamage × TimingMultiplier × BlockMitigation × VolumeBonus
```

### Combo Mode Damage (Timing-Clash Only)
- Both players shout once, 4 hits execute in sequence
- Player with timing closest to first beat wins the clash
- **Loser takes damage**, winner takes none
- **Tie**: No damage

### Interruption Penalty
- If opponent's move lands first (Jab before your Cross), you eat **5 bonus interrupt damage** on top of the hit damage
- **Why?** Aggressive players are rewarded for crisp timing

### Staggered Opponents
- All defenses broken → hits always connect
- Full damage, no mitigation

---

## 🎯 THE PRESSURE SYSTEM: Stress Under Fire

### Pressure Mechanic
- **Gain**: +0.30 per hit you take
- **Decay**: -0.12 per second of peace
- **Max**: 1.0 (100% pressure = most stressed)

### Effect on Gameplay
- **Shout Window Shrinking**: At full pressure, your input window shrinks by up to 40%
  - Normal: 0.50s window → At max pressure: 0.30s window
  - Why? Psychological: you're panicking, timing gets tighter

### Recovery
- Stay alive without taking hits for ~8-10 seconds → pressure fully decays
- First hit of a combo resets pressure

---

## 🎬 UI HOOKS & VISUAL FEEDBACK

### Real-Time Combat Display

#### Opponent Health Bar (Top Right)
- Large, red bar showing enemy HP
- Updates live as they take damage
- Name overlay with exact HP remaining

#### Your Shield (Top Center)
- Text display: "25" (current shield value)
- Resets on stagger/trades

#### Mic Level Indicator (Right Side)
- Live volume meter
- Green when ≥ threshold
- Orange when below threshold
- **Hook**: Shows you're hitting the volume sweet spot in real-time

#### Slot Pips (Below Cards)
- 4 visual pips per slot type (Attack: red, Defense: blue)
- Filled = available, dim = used up
- Clear visual of your resource pool

#### Combo Chain Queue (Bottom Left, Combo Mode)
- Visual list: "1. PUNCH ▼ 2. HOOK ▼ 3. CAGE ▼ 4. ?"
- Shows queued moves, waiting slots in dim
- Disappears in Single Mode

#### Stagger Recovery Panel (Center Screen, When Staggered)
- Large pulsing "⚠ STAGGERED ⚠" title
- Fill bar: "SHOUT: 47%"
- Timing hint: "🔊 SHOUT NOW — ESCAPE!" (pulsing when in window)
- Volume readout: "MIC: 0.45 (Min: 0.28)"
- Dark red border, glowing effect for drama

#### Timing Feedback Float (Center, Fades Fast)
- **EXCELLENT** (cyan) — rises & fades, commentary trigger
- **GOOD** (green) — neutral feedback
- **BAD** (red) — punishing feedback, triggers commentary

#### Combat Log (Right Side, ~5 entries max)
- Scrolling history: "Player1: HOOK vs Player2: BLOCK → BLOCKED"
- Color-coded: Green (win), Red (loss), White (tie)
- Real-time, updated every beat

---

## 🎙️ VOICE COMMAND DETAILS: Exact Recognition

### Recognition Mapping (Levenshtein Similarity)

| Word Said | Recognized As | Similarity Threshold | Notes |
|-----------|---|---|---|
| "punch" | Jab | 0.70 | Can handle "pnch", "puch", slight mispronunciation |
| "flank" / "blank" / "frank" | Cross | 0.75 | Homophones (flank/blank), common mishears |
| "hook" | Hook | 0.75 | Tight, specific word |
| "block" / "guard" | Block | 0.75 | Guard is recognized synonym |
| "cage" / "page" / "engage" | ParryIntent (Cage) | 0.70 | "Page" / "engage" are phonetically similar |
| "crush" / "crash" / "crushing" / "crashing" | UnbreakablePunch (Boom) | 0.75 | Rhyming words, intent-matching |
| "left" | Left (Dodge) | 0.75 | Directional, clear |
| "right" | Right (Dodge) | 0.75 | Directional, clear |
| "cancel" / "clear" | Cancel Input | 0.72 | Meta command to undo |

### Echo Guard (Prevents Bleed)
- After executing a command, a **0.6s echo guard** activates
- Any shout tail that overlaps is silently consumed
- **Why?** Your "crrrrrash" from one beat shouldn't queue the next beat's input

### Dead Zone (Rhythm Locked)
- **Single Mode**: 0.20s post-beat (prevents rapid re-queuing)
- **Combo Mode**: 1.2s post-beat (prevents mid-chain input while attacks execute)
- Pressed-during-deadzone words are **retried next frame** until window opens

---

## 🤖 BOT CONTROLLER (Single Player)

### Bot Behavior
- **AI Difficulty**: Intermediate (can win but not unbeatable)
- **Health**: 250 HP (vs player 400 HP)
- **Move Pool**: Jab, Cross, Hook, ParryIntent (Cage)
- **Timing**: Randomized 0-0.15s offset (imperfect but decent)

### Windup Decision
- At 0.53s before beat, bot thinks its next move
- Random selection from pool, weighted by state (low health = more parries)

### Stagger Recovery
- Bot ignores stagger UI, just takes damage
- No voice recovery mechanic (can't shout)
- Stagger clears by time only

---

## 🏥 HEALTH & DEATH

### Starting HP
- **Real Players**: 400 HP
- **Bot**: 250 HP

### Damage Sources
1. **Successful Attacks**: 10-25 HP per hit
2. **Interrupt Penalty**: +5 HP if you were interrupted
3. **Block Break** (Hook): Full damage hits
4. **Stagger Damage**: Everything hits staggered opponents

### Knockout
- HP ≤ 0 triggers knockout animation
- Camera shake (0.45s duration, 0.9x magnitude)
- "KNOCK OUT" animation plays
- **Commentary trigger**: Loser commentary event fires
- Match auto-restarts after 4 seconds (same scene reload)

---

## 🎵 AUDIO HOOKS

### Success Feedback
- **Card Accepted**: Bright chime (inventory-ish sound)
- **Block**: Thud sound (protection)
- **Dash**: Whoosh (evasion)
- **Parry**: Metallic clang (reflection)
- **Attack**: Heavy impact sound
- **Hurt**: Pain/grunt sound

### Commentary System (Dynamic Voice Lines)
- **Excellent**: "What a perfect strike!" (hype)
- **Good**: "Got 'em!" (neutral approval)
- **Bad Timing**: "Missed the window..." (disappointment)
- **Knockout**: Victory/defeat commentary depending on winner
- **Stagger**: Warning if opponent staggers

---

## 🎮 GAME FLOW: Session Structure

### Lobby Phase
1. Server waits for 2 players
2. If only 1 player for 30s, spawn bot
3. Players click "READY"

### Round Selection
- Server shows 3 mode cards: SLOW RHYTHM, FAST COMBO, CUSTOM MAPS
- Player clicks one
- Both clients load the round

### Windup (1 second before first beat)
- All animations queue
- Ready? screen shows countdown

### Active Round
- Beats fire at scheduled times
- Voice commands processed in real-time
- Damage resolved every beat
- Stagger counters down

### Victory Condition
- **First to KO opponent** wins
- Scene reloads to Lobby
- Optional: Rematch or new opponent

---

## 🔧 ADVANCED MECHANICS: The Deep Game

### Card Cooldown Rolling
- **Beat N**: Use Hook
- **Beat N+1**: Hook blocked (unavailable)
- **Beat N+2**: Hook available again
- **Why?** Prevents spam; forces move variety

### Slot Bonus Cascades
- Win a trade → gain 1 slot
- Land a parry → gain attack slot
- Three wins in a row → +3 slots potential
- Inverse: Go 2 beats with no output → stagger risk

### Timing Grading in Combo Mode
- **Beat 1 shout at 0.05s offset**: EXCELLENT (covers whole chain)
- **Beat 2 shout at 0.15s offset**: GOOD (same spike, further offset, still viable)
- **Beats 3-4 inherit the same spike timing** (one shout = one timing grade)

### Stagger Math
- **Remaining Beats**: 3
- **Est. Interval**: 2.0s per beat
- **Total Window**: 3 × 2.0 × 0.65 (buffer) = ~3.9s to fill bar
- **Required Rate**: 1.0 / 3.9s ≈ 0.25/s charge rate (achievable at max volume in ~4s)

---

## 🎯 TIPS FOR MASTERY

### Rhythm Reading
- Listen for the beat drop, feel the pulse
- Slow Rhythm = count "1... 2... 3... 4... SHOUT!" on beat 4
- Fast Combo = rapid-fire beats, one shout covers all

### Voice Technique
- **Loud ≠ Accurate**: Volume is secondary to timing
- **Shout Placement**: Time your shout peak (the spike) to hit the beat center
- **Sustained Pressure**: For Stagger, hold the shout at volume threshold

### Card Economy
- **Early Game**: Spend aggressively, win trades to refund
- **Late Game** (low HP): Parry more, attack less (defensive slots become precious)
- **Stagger Risk**: Never let both slots hit zero—you'll stagger next beat

### Positioning
- Movement (Left/Right dodges) matters in 3D space
- Hook from the side, Jab straight-on
- Knockback from big hits pushes you—react quickly

### Reading Opponent
- If they always Cage → throw Cross (breaks guard)
- If they always Block → Hook (breaks it)
- If they aggressive Jab → Dodge + counter-attack

---

## 🎪 THE VOXBOUND EXPERIENCE: Why It's Unique

1. **Voice = Your Power**: No abstract button input. You literally shout. Your mic volume, your speech clarity, your confidence—all matter.

2. **Rhythm Grounding**: Music isn't just flavor—it's the mechanical clock. Every decision syncs to a beat.

3. **Card Depth**: Slot economy adds strategy. Rookie players spam attacks; pros manage resources like a chess match.

4. **Comeback Mechanic**: Stagger isn't just "you lose." It's "shout loud enough and you escape." Down 100 HP? Scream and reset.

5. **Multiplayer Theater**: Watching two people face off, shouting combat commands, timing shouts to a beat—it's inherently dramatic and fun to watch.

---

## 🚀 STEAM NEXT FEST DEMO READINESS CHECKLIST

- ✅ Voice recognition (Vosk offline, constraint-optimized)
- ✅ Rhythm synchronization (Network time, beat tracking)
- ✅ Combat resolution (Server-authoritative, no client cheat)
- ✅ Visual feedback (Card UI, stagger HUD, timing floaters, opponent health)
- ✅ AI Bot (Intermediate difficulty, convincing opponent)
- ✅ Custom beatmaps (SmartBeatMapper, save/load, looping)
- ✅ Audio commentary (Dynamic voice lines, hit feedback)
- ✅ Performance (RTX 4050 + 32GB optimized, Vosk pipeline tuned)

---

## FINAL WORD

**VoxBound** is a game about **presence**. You can't hide behind inputs or optimization. Your voice, your timing, your reaction—they're all exposed. You feel the beat, you shout the move, the game responds instantly. In a sea of passive, abstract combat games, **VoxBound is visceral, real, and *loud*.**

Good luck, fighter. The arena awaits.

---

*Generated for Steam Next Fest — Build date: 2026-05-11*
