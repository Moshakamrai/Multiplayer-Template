# Voice-Driven Rhythm Card Combat

A multiplayer action game where **your voice is the controller**. Players shout combat cards
("STRIKE", "THROW", "BLOCK", "PARRY", "DASH") to declare an action, then execute it on the
beat of the music. Card selection is always voice-driven; beat execution is swappable between
shouting and a keypress so a quiet player can still play.

Built in Unity 2022.3 LTS (URP) with Mirror networking and offline on-device speech
recognition. Roughly 29k lines of C# across 88 scripts.

---

## Table of Contents

- [What makes this project interesting](#what-makes-this-project-interesting)
- [Core gameplay loop](#core-gameplay-loop)
- [Architecture](#architecture)
- [Voice recognition pipeline](#voice-recognition-pipeline)
- [Bangla NPC negotiation pipeline](#bangla-npc-negotiation-pipeline)
- [Project layout](#project-layout)
- [Running the project](#running-the-project)
- [Multiplayer setup](#multiplayer-setup)
- [Design documents](#design-documents)
- [Branches](#branches)
- [Third-party dependencies](#third-party-dependencies)

---

## What makes this project interesting

Most "voice controlled game" projects wire a speech API to a menu. This one puts recognition
inside a **latency-critical combat loop**, which forces solutions to problems that only show
up at that timescale:

- **Dynamic grammar rebuilding.** The recognizer's vocabulary is regenerated at runtime from
  the player's currently equipped cards, so the search space stays tiny and accuracy stays
  high as the loadout changes mid-run. See `VoiceCommandManager.Update`.
- **Echo guarding.** After a shouted command executes, the mic hears the game's own audio and
  the player's trailing syllables. A 0.6s guard window suppresses re-triggers
  (`EXECUTION_ECHO_GUARD`).
- **Dead-zone retry.** A word recognized during a window where it cannot legally be played is
  not discarded — it is held in `_pendingRetryWord` and retried each frame until the window
  opens, so the player's intent survives bad timing.
- **Partial-result handling.** Commands fire off partial hypotheses rather than waiting for a
  final transcript, because waiting costs more than the beat window allows.
- **Amplitude as a separate input channel.** Parry reads raw mic volume against a threshold
  (`parryVolumeThreshold`), treating loudness as its own gameplay signal distinct from words.
- **Fully offline recognition.** Vosk runs on-device with no network round-trip and no
  per-request cost, which is what makes sub-beat response times possible at all.

The same intent-matching approach is extended to NPC conversation (see the Bangla pipeline
below), where the goal is negotiating with a merchant by speaking naturally rather than
picking dialogue options.

---

## Core gameplay loop

```
Menu → Lobby (auto ready-up) → Arena
  → Round: song plays, enemy runs in on the beat
    → Declare window: shout your card
    → Beat: execute by shout or keypress
    → Resolution: rock-paper-scissors style outcome table
  → Shop phase: spend winnings on cards
  → Next round / tiebreaker → result
```

Combat resolves on a **beat clock**, not on frame time. `RhythmRoundManager` (the largest
system at ~4k lines) owns the authoritative beat schedule, wind-up timing, bot approach
lead time, and round state, all synchronized over Mirror with `[SyncVar]`.

---

## Architecture

**Networking.** Mirror, server-authoritative. Combat resolution, round state, and beat timing
live on the server; clients send intent via `[Command]` and receive results via `[ClientRpc]`.
Voice recognition runs **locally on each client** (the recognizer is destroyed on non-local
players) and only the resolved command crosses the network — never audio.

**Beat synchronization.** The server stamps a `_startTime` and derives all beat positions from
it, so clients agree on when a beat lands without streaming per-beat messages.

**Key systems:**

| System | Location | Role |
|---|---|---|
| Round / beat clock | `Scripts/Round Scripts/RhythmRoundManager.cs` | Authoritative beat schedule, round flow, bot timing |
| Combat resolution | `Scripts/Player Scripts/PlayerCombat.cs` | Outcome table, damage, hitboxes |
| Voice → command | `Scripts/VoiceRecognition/VoiceCommandManager.cs` | Grammar, echo guard, retry, parry volume |
| Speech engine | `Scripts/VoiceRecognition/VoskSpeechToText.cs` | Vosk lifecycle, partial/final results |
| Fuzzy matching | `Scripts/VoiceRecognition/Similarity.cs` | Tolerates near-miss transcriptions |
| Game / lobby flow | `Scripts/Player Scripts/GameManager.cs` | Scene changes, ready-up, match lifecycle |
| Cards | `Scripts/Card System/`, `Scripts/Shop System/` | Card data, elements, inventory, shop |
| Bot opponent | `Scripts/AI script/` | Single-player opponent that plays on the beat |

---

## Voice recognition pipeline

```
Mic (VoiceProcessor)
  → Vosk recognizer, grammar = currently equipped cards
    → partial result → fuzzy match (Similarity.cs)
      → echo guard + dead-zone check
        → [Command] to server → combat resolution
```

Models live in `Assets/StreamingAssets/`. `AndroidPermissionHandler` handles runtime mic
permission on Android. `VoiceDebugGUI` / `VoiceDebugger` provide an in-game overlay showing
live recognition state, which is the fastest way to see the system working.

To try voice alone, open `Assets/Scenes/VoiceInputTestScene.unity`.

---

## Bangla NPC negotiation pipeline

A second, more experimental system: negotiating with NPCs by **speaking Bangla**, with the
NPC answering in spoken Bangla. Rather than a chatbot, this is intent-matching against what
the merchant is willing to accept, and the negotiation outcome determines item quality.

> **This system lives on the `Coop-Mode-With-PC` branch**, not on `Full-Game-without-VR`.
> Check that branch out to see the implementation and its documentation.

```
Bangla speech
  → Whisper (transcribe only, Bengali.AI community fine-tune)
    → NLLB-200 bn→en
      → LLM reasons and writes in English
        → NLLB en→bn
          → Piper bn_BD voice → spoken reply
```

Two findings from building it are worth knowing before touching this code:

1. **Transcribe, never translate, at the ASR step.** Whisper's `--translate` is not usable
   here (turbo cannot translate, and fine-tunes lose the translation task). Translation is a
   separate NLLB stage.
2. **The hard bug was script, not accuracy.** Stock Whisper medium transcribed Bangla speech
   into **Devanagari** script, which silently poisoned the downstream translation. A
   Bengali.AI fine-tune fixed it: correct transcript in ~1.1s versus 15.7s and wrong script.

Full write-up: `Assets/#MainProject/DOC/BANGLA_ASR_PROJECT.md` and
`BANGLA_TTS_PROJECT.md`, both on the `Coop-Mode-With-PC` branch.

> Note: Unity's IMGUI cannot shape Bengali text, so debug overlays render it as broken
> glyphs. Inspect Bangla output in an external editor, not the in-game console.

---

## Project layout

```
Assets/
  #MainProject/          Game code and content (the project proper)
    Scripts/
      Round Scripts/     Beat clock, round flow, commentary, tiebreaker
      Player Scripts/    Controller, combat, energy, game manager
      VoiceRecognition/  Vosk integration, command mapping, debug tools
      Card System/       Card manager, element system
      Shop System/       Card data, database, inventory, shop UI
      AI script/         Bot opponent
      Tutorial/, UI/, VFX/, Camera/, Sound Scripts/, VR/
    Characters/, Environment/, Models/, VFX/, Audio/, Prefabs/, Shaders/
    DOC/                 Design documents
  Scripts/               Shared/template scripts (interaction, UI, Steamworks)
  Scenes/                Menu, Lobby, RhythmTest, TrackEditor, Tutorial, VoiceInputTest
  StreamingAssets/       Speech models
  Mirror/                Networking (third party)
  vosk-unity-asr-master/ Speech recognition (third party)
```

Vendored third-party packages sit at `Assets/` root; first-party work is under
`Assets/#MainProject/`.

---

## Running the project

**Requirements:** Unity **2022.3.62** (LTS), URP. A microphone is required for voice input.

1. Open the project in Unity Hub with the version above.
2. Open `Assets/Scenes/Menu.unity`.
3. Press Play.

Allow first-import time — the project includes speech models and VFX packages.

Useful scenes:

| Scene | Purpose |
|---|---|
| `Scenes/Menu.unity` | Normal entry point |
| `Scenes/Lobby.unity` | Multiplayer lobby |
| `Scenes/RhythmTest.unity` | Combat/beat loop in isolation |
| `Scenes/VoiceInputTestScene.unity` | Voice recognition in isolation |
| `Scenes/TrackEditor.unity` | Author beat maps for songs |

Beat maps are authored with a **minimum 1.5s gap** between a player's beats, so there is
always time to declare a card and then execute it.

---

## Multiplayer setup

Mirror over TCP on port **7777**.

- **Host:** no address needed. Share your address with clients.
- **Client, same machine:** `localhost`
- **Client, same LAN:** host's IPv4 (`ipconfig` on Windows, `ifconfig` on macOS/Linux)
- **Client, remote:** host's external IP, with port 7777 TCP forwarded

Allow the application through your firewall when prompted; if connection issues persist, add
explicit inbound and outbound rules for port 7777.

---

## Design documents

In `Assets/#MainProject/DOC/`:

| Document | Contents |
|---|---|
| `DESIGN.md` | Current single-player systems — the record of what is built |
| `DESIGN_COOP.md` | Where the game is going: 2-player co-op roguelite |

On the `Coop-Mode-With-PC` branch, the same folder additionally holds
`BANGLA_ASR_PROJECT.md` and `BANGLA_TTS_PROJECT.md` (speech pipeline, model selection,
findings).

`DESIGN_COOP.md` is the most current design thinking and documents several reversed
decisions with reasoning, which is useful for understanding why the code looks as it does.

---

## Branches

| Branch | Contents |
|---|---|
| `Full-Game-without-VR` | PC flat build — the main line of work |
| `Elemental-Card-Changes` | VR (Meta Quest) build |
| `Coop-Mode-With-PC`, `Coop-Branch` | Co-op direction from `DESIGN_COOP.md` |
| `NLP-Branch` | Language/NPC experiments |
| `New-Card-System`, `Redo-The-cardsystem` | Card system iterations |

---

## Third-party dependencies

Unity packages (see `Packages/manifest.json`): Universal RP 14.0.12, TextMeshPro 3.0.7,
Input System, Post Processing 3.4.0, Newtonsoft JSON 3.2.1.

Vendored under `Assets/`: Mirror (networking, MIT), Vosk (speech recognition, Apache 2.0),
Steamworks.NET, PlayFlowCloud, plus VFX and animation packages (KriptoFX, Kevin Iglesias,
Lyrebird-Studio, _Creepy_Cat, DoubleL, SoundManagerPackage).

> Anyone redistributing or shipping this project commercially should verify the license terms
> of the vendored art, VFX, and audio packages above, as several are Asset Store packs whose
> licenses are tied to the original purchaser's account rather than to the repository.
