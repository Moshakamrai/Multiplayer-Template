# Bangla Voice Project — Training a Custom Piper TTS Model

**Status:** Planning doc, 2026-07-15. This is a SEPARATE project from the Unity game —
do the training work in its own folder/repo, not inside `Assets/`. Only the finished
`.onnx` + `.onnx.json` voice files come back into Unity when done.

---

## 1. Why this exists — context from the game project

We built a fully local, offline conversational NPC system in Unity (no cloud APIs, no
subscriptions, runs entirely on the player's PC):

- **Vosk** — live speech recognition, used for real-time UI feedback and for the combat
  game's fixed vocabulary (card names).
- **whisper.cpp** — accurate FINAL transcription of the player's full sentence, run as a
  local subprocess. Whisper is multilingual and has a built-in `--translate` flag: fed
  Bangla speech with `-l bn --translate`, it outputs **English text** directly. This
  already works today — a Bangla-speaking player's words arrive at the NPC brain as
  clean English, no other code needed downstream.
- **llama.cpp running Qwen2.5 (7B, Q4_K_M)** — a local LLM that generates the NPC's
  actual reply. Two NPCs exist: `GrizBrain` (a shop-negotiation merchant with real game
  state — price, patience, respect) and `CompanionBrain` (a fully open-domain
  conversational character named Sana — talk to her about anything, she has persona,
  memory of things you've told her, and mood).
- **Piper** — local neural TTS, runs as a subprocess, speaks the NPC's generated reply
  out loud. This is the ENGLISH-only piece. **Piper has no official Bangla voice model.**

So today: a player can SPEAK Bangla, and the NPC UNDERSTANDS and can be made to REPLY in
Bangla text (see §2) — but the NPC cannot SPEAK Bangla back. That's the one remaining gap,
and it's a real one: Piper's phonemizer (espeak-ng) and its trained voices are built for
languages it has training data for, and Bangla isn't in the stock voice set.

## 2. What's ALREADY DONE on the Unity side (do not redo this)

- `WhisperTranscriber.Restart(string language, bool translate)` — kills and relaunches
  the whisper-server subprocess with a new `-l` / `--translate` flag. Bangla mode:
  `Restart("bn", translate: true)`.
- An EN/BN toggle button exists in the test consoles (`GrizTestConsole`,
  `CompanionConsole`) that calls this.
- `LlamaIntentService.GenerateReply(...)` already accepts a `systemPromptOverride` and
  `closingInstruction` per call — this is the hook point for telling the model "reply in
  Bangla script" once a Bangla voice exists. (Not yet wired to actually request Bangla
  OUTPUT — currently the persona always asks for English. That's a small prompt-text
  change, not part of this doc's scope, but trivial once a voice exists to speak it.)
- `PiperVoice.cs` already supports **multi-speaker models** (`speakerId` field) and a
  **`preferredModel`** filter to pick a specific `.onnx` file out of several dropped in
  the same folder — so adding a Bangla voice alongside the English ones later is just
  "drop the file in, set `preferredModel` per-NPC-or-per-language."

## 3. The goal of THIS project

Train (or fine-tune) a **custom Piper-compatible Bangla voice model**, end to end,
ourselves — not use someone else's pretrained checkpoint if avoidable, per your call to
own this rather than shortcut it. Output target: a working `<name>.onnx` +
`<name>.onnx.json` pair that `piper.exe --model <name>.onnx` can speak Bangla text with,
exactly like the English voices already in `Assets/StreamingAssets/piper/`.

### Why a separate project/folder (not inside the Unity repo)
- Needs a Python environment (PyTorch, `piper-train` or VITS training scripts,
  espeak-ng-ng with Bangla language data, phonemizer tooling) — a completely different
  toolchain from Unity/C#.
- Needs large raw audio + transcript datasets (multiple GB), which must never be
  committed into the game's git history or sit inside `Assets/` where Unity's asset
  pipeline will try to import every file in it.
- Training runs take real GPU time (hours), producing many intermediate checkpoints —
  none of that belongs anywhere near the game project.
- The ONLY artifact that ever crosses back into Unity is the two final files.

## 4. What Piper training actually requires (the real shape of the work)

Piper's voices are VITS-based single/multi-speaker TTS models. To train one:

1. **A Bangla speech dataset**: audio clips + matching text transcripts, ideally one
   speaker (simpler, more consistent voice) recorded cleanly, minimal background noise.
   Realistic sources to evaluate:
   - **OpenSLR** — has several Bengali/Bangla speech corpora (e.g. SLR37, SLR53 —
     check current license terms per corpus before use).
   - **Mozilla Common Voice** — has a Bangla-language dataset, crowd-sourced, variable
     quality/consistency (multi-speaker, which is harder to get one clean, consistent
     synthetic voice out of — may need filtering to a subset or a speaker-adaptation
     step).
   - **Self-recorded**: if quality/control matters more than speed, recording a few
     hours of one consistent voice reading varied Bangla sentences is a legitimate
     (if slow) path, and gives full ownership/rights to the result.
2. **Text normalization + phonemization for Bangla**: Piper relies on espeak-ng for
   phonemization. Bangla (`bn`) support in espeak-ng needs to be checked/verified — if
   its Bangla phoneme rules are weak or incomplete, this is the step most likely to need
   real problem-solving (possibly supplementing/correcting espeak-ng's Bangla ruleset,
   or evaluating alternate phonemizers that support Bangla better).
3. **Training**: Piper's training pipeline (`piper_train`, part of the
   github.com/rhasspy/piper repo, or the newer community forks) fine-tunes/trains a
   VITS model against the prepared dataset. Needs a CUDA-capable GPU (yours qualifies —
   same card used for llama.cpp/whisper.cpp inference). Expect real training time
   (hours, possibly longer depending on dataset size and target quality).
4. **Export + validate**: Piper exports to ONNX (`.onnx` + a `.onnx.json` config with
   phoneme/speaker metadata) — the exact format `piper.exe` already loads. Validate by
   running `piper.exe --model bangla-voice.onnx` directly from the command line with
   sample Bangla text before ever touching Unity.
5. **Bring it home**: drop the two files into `Assets/StreamingAssets/piper/` alongside
   the English voice(s); set `preferredModel` on a `PiperVoice` component (or add
   simple per-language voice selection logic) to pick it when the NPC is in Bangla mode.

## 5. Open questions to resolve before/while starting

- [ ] Which dataset(s) are we actually using — OpenSLR, Common Voice, self-recorded, or
      a mix? (Affects everything downstream: single-speaker cleanliness, licensing,
      total training time.)
- [ ] Do we train a NEW voice from scratch, or fine-tune from an existing Piper
      checkpoint in a related/nearby language (e.g. Hindi, if any shares enough
      phonetic/script-adjacent structure to bootstrap from) to save training time?
- [ ] What's the target voice character — should match one of the NPCs (e.g. a Bangla
      Griz, or a Bangla-speaking Sana) rather than being generic?
- [ ] Licensing check on whichever dataset is used — needs to be legally fine to ship
      a model trained on it inside a commercial game.
- [ ] espeak-ng's actual Bangla phoneme quality — untested assumption above, first
      thing to verify hands-on before committing to the full pipeline.

## 6. Definition of done

`piper.exe --model bangla-voice.onnx --output_file test.wav` (run standalone, no Unity
involved) produces intelligible, natural-sounding spoken Bangla from Bangla text input —
verified by ear before it's considered ready to wire into `PiperVoice`/`CompanionConsole`.
