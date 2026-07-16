# Bangla Piper Voice — Unity Integration Handoff

Status: voice trained, tested against the real shipped `piper.exe`, and already copied into
the game project. This doc is what's needed to wire it up in Unity from here.

## Game project location

`D:\UnityProjects\Multiplayer Card Game\Multiplayer-Template`

## Files already placed in the game project (done — nothing to copy)

These three files were copied/overwritten directly in the game project's
`Assets/StreamingAssets/piper/` folder (which is gitignored — voice models are local setup
artifacts, not committed to the repo):

| File | Path (relative to game project root) | What it is |
|---|---|---|
| `bn_BD-griz-medium.onnx` | `Assets/StreamingAssets/piper/bn_BD-griz-medium.onnx` | The trained Bangla voice model (fine-tuned from a Hindi Piper checkpoint on OpenSLR SLR37 speaker 03042, ~46 min single-speaker Bangla audio) |
| `bn_BD-griz-medium.onnx.json` | `Assets/StreamingAssets/piper/bn_BD-griz-medium.onnx.json` | Voice config — phoneme map, sample rate, espeak voice ID (`bn`). **Already stripped of 5 unused diphthong phoneme entries** (`aɪ`, `aʊ`, `ɔɪ`, `eɪ`, `oʊ`) that the game's C++ `piper.exe` rejects on load — do not re-add these. |
| `espeak-ng-data/bn_dict` | `Assets/StreamingAssets/piper/espeak-ng-data/bn_dict` | **Overwritten** with a patched Bangla phonemizer dictionary. Fixes: (1) vowels after nukta letters (য়/ড়/ঢ়) were being silently deleted — broke words like দয়া, আবহাওয়া, হয়েছে, পড়া, খাওয়া; (2) most consonants were missing the word-final inherent-vowel rule — broke conjunct words like যথেষ্ট, গল্পটা. **This is a source-level patch to espeak-ng itself, not something Unity/C# controls** — if `bn_dict` is ever replaced (espeak-ng update, reinstall, fresh clone), these Bangla words will silently regress and no error will be thrown. |

The stock (unpatched) `bn_dict` is still preserved at `C:\Program Files\eSpeak NG\espeak-ng-data\bn_dict` on this machine, in case a diff or rollback is ever needed.

## Setup in Unity (what's NOT done yet)

1. **Open the project in the Unity Editor once** so it imports the two new `.onnx`/`.onnx.json`
   files (they don't have `.meta` files yet — Unity generates those automatically on import).

2. **Add/configure a `PiperVoice` component** on whichever NPC should speak Bangla:
   - Script: `Assets/#MainProject/Scripts/GrizNPC/PiperVoice.cs`
   - Set the `Preferred Model` field to `bn_BD` (or `griz`, or any substring unique to
     `bn_BD-griz-medium.onnx` — it's a case-insensitive filename substring match against
     everything in `StreamingAssets/piper/`).
   - Leave `Speaker Id` at `-1` (this is a single-speaker model, not multi-speaker).
   - Tune `pitch` / `lengthScale` / `volume` same as any other `PiperVoice` instance.

3. **Not done — LLM output language.** `LlamaIntentService.GenerateReply(...)` currently always
   asks the model to reply in English (via its default system prompt / closing instruction).
   For an NPC to actually speak Bangla, its system prompt needs to request Bangla-script output
   before `PiperVoice.Speak(...)` is called with that text. This is a small prompt text change,
   not a code architecture change — the hook point (`systemPromptOverride` /
   `closingInstruction` parameters) already exists per the original project doc
   (`Assets/#MainProject/DOC/BANGLA_TTS_PROJECT.md`).

4. **Verify end-to-end once wired**: with the EN/BN toggle switched to BN (which already
   restarts `WhisperTranscriber` with `-l bn --translate`), confirm the NPC's spoken Bangla
   output sounds right — especially any dialogue containing দয়া-style words, conjuncts, or
   other patterns not covered by the test sentences already checked by ear.

## Known limitations of this voice (for context, not blockers)

- Trained on ~46 minutes of audio (473 clips) for 100 fine-tune epochs — good enough to sound
  like a consistent, natural voice per manual listening check, but not studio-polished.
- Source dataset (OpenSLR SLR37, bn-BD) is CC BY-SA 4.0. Trained model weights are not
  considered a derivative work under the prevailing ML-community interpretation, but this is
  untested in court — worth one real legal check before commercial ship, not a blocker for
  further dev work.
- `piper.exe` shipped in the game is the original MIT-licensed C++ binary (not the newer
  GPL-licensed Python training toolchain used to train this voice) — no GPL exposure from
  shipping it.
