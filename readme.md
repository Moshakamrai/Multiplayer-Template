# Voice-Driven Rhythm Card Combat

A multiplayer action game where **your voice is the controller**. Players shout combat cards
("STRIKE", "THROW", "BLOCK", "PARRY", "DASH") to declare an action, then execute it on the
beat of the music. Card selection is always voice-driven; beat execution is swappable between
shouting and a keypress so a quiet player can still play.

Alongside the combat game, the project contains a second, deeper line of work: **NPCs you
talk to out loud**, driven by a fully local speech-to-LLM-to-speech stack with no cloud
dependency — a merchant you haggle with, and an interrogation game where you question
suspects by speaking.

Built in Unity 2022.3 LTS (URP) with Mirror networking and entirely offline, on-device
speech recognition and synthesis. Roughly 38k lines of C# across 122 scripts.

---

## Table of Contents

- [What makes this project interesting](#what-makes-this-project-interesting)
- [Core gameplay loop](#core-gameplay-loop)
- [Architecture](#architecture)
- [Voice recognition pipeline](#voice-recognition-pipeline)
- [Conversational NPCs: the local AI stack](#conversational-npcs-the-local-ai-stack)
- [Bangla speech pipeline](#bangla-speech-pipeline)
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

The same intent-matching philosophy carries into the conversational NPC work below, where an
LLM is deliberately confined to *understanding* the player while authored content stays in
control of what characters say.

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

## Conversational NPCs: the local AI stack

The project's most substantial system. You talk to NPCs with your actual voice and they
answer out loud — **entirely offline**, with no API keys, no per-request cost, and nothing
leaving the machine. Three game modes are built on it:

| Mode | Scripts | Premise |
|---|---|---|
| **Griz the merchant** | `Scripts/GrizNPC/` | Haggle out loud; how well you negotiate sets the item's quality tier |
| **Gnomes & Gaslight** | `Scripts/Interrogation/` | Victorian whodunnit — question suspects by speaking, fill a case board |
| **Terminal Leak** | `Scripts/TerminalLeak/` | Talk your way past a sentinel AI |

### The key design decision

The LLM is **only allowed to classify intent**, never to speak for a character:

> *"The LLM only CLASSIFIES what the player means (intent + offered amount) —
> `GrizBrain`'s state machine and authored lines still decide what he says, so no
> hallucinated prices and no broken character."* — `LlamaIntentService.cs`

This is the architectural heart of the system. It means a merchant can never be talked into
an impossible price, can never break character, and can never invent items — while the
player still gets to speak freely instead of picking from a dialogue menu. Suspects in the
interrogation mode are constrained the same way, with commits specifically fixing them
"dodging every question with invented nonsense" and "free-associating false-give content
before it's earned."

### Graceful degradation

Every AI subprocess is optional and independently fails safe. If llama.cpp is not installed,
`LlamaIntentService.IsReady` stays false and callers fall back to a **keyword classifier** —
the game remains playable with the components still in the scene. Editor menus
(`Tools > Griz > Check Llama Setup`) verify each piece.

### Runtime shape

Three MIT-licensed native binaries run as **separate subprocesses** that Unity queries over
localhost, rather than being embedded in-engine:

```
llama.cpp  (llama-server.exe, port 8731)  → intent classification
whisper.cpp                               → speech to text
piper                                     → text to speech
```

Supporting systems: `MouthLipSync` and `FacialExpressions` drive visemes and mood from the
generated audio; `GrizHeadLook` handles gaze; `CompanionBrain` and `NpcSwitcher` manage
multiple characters.

> **Note:** binaries and model weights live in gitignored `StreamingAssets/` subfolders
> (`llama/`, `whisper/`, `piper/`) because they are multi-GB, machine-specific, and freely
> re-downloadable. A fresh clone needs them fetched via the `Tools >` setup menus. The
> conversational modes will fall back or stay inert until then; **rhythm combat is
> unaffected** and runs out of the box.

---

## Bangla speech pipeline

An extension of the above: NPCs that understand and reply in **spoken Bangla**, using a
custom-trained voice.

```
Bangla speech
  → Whisper (transcribe only, Bengali.AI community fine-tune)
    → NLLB-200 bn→en
      → LLM reasons and writes in English
        → NLLB en→bn
          → Piper bn_BD voice → spoken reply
```

Four hard-won findings, each of which cost real debugging time:

1. **Transcribe, never translate, at the ASR step.** Whisper's `--translate` is unusable here
   (turbo cannot translate; fine-tunes lose the translation task). Translation is a separate
   NLLB stage.
2. **The hard bug was script, not accuracy.** Stock Whisper medium transcribed Bangla into
   **Devanagari**, which silently poisoned downstream translation — the root cause of the
   whole recognition saga. A Bengali.AI fine-tune fixed it: correct transcript in ~1.1s
   versus 15.7s and the wrong alphabet.
3. **The LLM always writes English.** Its Bangla output is unreliable, so generation happens
   in English and NLLB handles the final en→bn hop.
4. **The Bangla voice required patching espeak-ng itself.** The stock `bn_dict` phonemizer
   silently deleted vowels after nukta letters (য়/ড়/ঢ়) and missed word-final inherent-vowel
   rules, breaking common words. The patched dictionary is a **source-level fix outside
   Unity's control** — if `bn_dict` is ever replaced, these words regress silently with no
   error. See `DOC/UNITY_INTEGRATION.md`.

The voice itself was fine-tuned from a Hindi Piper checkpoint on ~46 minutes of
single-speaker Bangla audio (OpenSLR SLR37).

> Unity's IMGUI cannot shape Bengali text, so debug overlays render it as broken glyphs.
> Inspect Bangla output in an external editor, not the in-game console.

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
      GrizNPC/           Local LLM/TTS/ASR NPC stack, lip sync, expressions
      Interrogation/     Gnomes & Gaslight: case board, suspects, intro sequence
      TerminalLeak/      Sentinel AI encounter
      Tutorial/, UI/, VFX/, Camera/, Sound Scripts/, VR/
    Characters/, Environment/, Models/, VFX/, Audio/, Prefabs/, Shaders/
    DOC/                 Design documents
  Scripts/               Shared/template scripts (interaction, UI, Steamworks)
  Scenes/                Menu, Lobby, RhythmTest, TrackEditor, Griz/NPC tests
  StreamingAssets/       Vosk model (committed); llama/whisper/piper (gitignored)
  Mirror/                Networking (third party)
  vosk-unity-asr-master/ Speech recognition (third party)

nllb-tools/              Bangla translation server (Python; weights gitignored)
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
| `Scenes/GrizTest.unity` | Griz the merchant — talk-to-haggle |
| `Scenes/MultiNpcTest.unity` | Multiple conversational NPCs |
| `Scenes/CompanionTest.unity` | Companion NPC behaviour |

Beat maps are authored with a **minimum 1.5s gap** between a player's beats, so there is
always time to declare a card and then execute it.

**Rhythm combat runs on a fresh clone.** The conversational NPC scenes additionally need the
local AI binaries and models fetched through the `Tools >` setup menus (see the section
above); without them those scenes fall back to keyword matching or stay inert rather than
erroring.

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
| `DESIGN.md` | Single-player combat systems — the record of what is built |
| `DESIGN_COOP.md` | 2-player co-op roguelite direction, voice-NPC hub design |
| `BANGLA_ASR_PROJECT.md` | Bangla speech recognition: model selection, A/B results |
| `BANGLA_TTS_PROJECT.md` | Training the custom Bangla Piper voice |
| `UNITY_INTEGRATION.md` | Wiring the Bangla voice into Unity; the espeak-ng patch |
| `GRIZ_SHOWCASE_SCRIPT.md` | Demo script for the merchant NPC |
| `TERMINAL_LEAK_FINAL_GDD.txt` | Terminal Leak design |

`DESIGN_COOP.md` is the most current design thinking and documents several **reversed
decisions with their reasoning**, which is the fastest way to understand why the code looks
the way it does.

---

## Branches

`main` carries the current work. The others are kept for history:

| Branch | Contents |
|---|---|
| `main` | Current line of work — combat, conversational NPCs, Bangla pipeline |
| `Full-Game-without-VR` | Earlier PC flat build, before the NPC/AI work |
| `Elemental-Card-Changes` | VR (Meta Quest) build |
| `NLP-Branch` | Early language/NPC experiments |

---

## Third-party dependencies

Unity packages (see `Packages/manifest.json`): Universal RP 14.0.12, Input System 1.14.0,
Post Processing 3.4.0, Newtonsoft JSON 3.2.1, Timeline 1.7.7, AI Navigation 1.1.6.

Vendored under `Assets/`: Mirror (networking, MIT), Vosk (speech recognition, Apache 2.0),
Steamworks.NET, PlayFlowCloud, plus VFX and animation packages (KriptoFX, Kevin Iglesias,
Lyrebird-Studio, _Creepy_Cat, DoubleL, SoundManagerPackage).

Local AI runtimes, fetched at setup rather than committed, each running as a separate
localhost subprocess: **llama.cpp** (intent classification), **whisper.cpp** (speech to
text), **piper** (speech synthesis), and **NLLB-200 distilled 600M** via CTranslate2
(Bangla translation). The runtimes are MIT-licensed; the **model weights carry their own
separate licenses**, which anyone shipping commercially should check per model — Meta's
NLLB-200 in particular is not permissively licensed for commercial use.

> Anyone redistributing or shipping this project commercially should verify the license terms
> of the vendored art, VFX, and audio packages above, as several are Asset Store packs whose
> licenses are tied to the original purchaser's account rather than to the repository.
