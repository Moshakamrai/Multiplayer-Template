# Bangla Speech Recognition — Fine-tuned Whisper Project

**Status:** Planning/handoff doc, 2026-07-16. Same structure and workflow as
`BANGLA_TTS_PROJECT.md` (which produced the working bn_BD Piper voice) — do this in a
SEPARATE folder/repo with its own Python environment, feed this doc to a fresh AI
session, and only the finished model file comes back into the game.

---

## 1. Why this exists — the one remaining broken stage

The game's Bangla NPC pipeline is now:

```
Bangla speech → Whisper (TRANSCRIBE, -l bn, no --translate)
             → NLLB-200 (bn→en) → Qwen 7B reasons + writes reply in ENGLISH
             → NLLB-200 (en→bn) → trained bn_BD Piper voice speaks it
```

Every stage of this is verified working EXCEPT the first one. Stock Whisper's Bangla
transcription is genuinely poor — Bangla was low-resource in Whisper's training data,
and no model-size change fixes that (tested: small/medium/large-v3-turbo; real sessions
produced hallucinated English words, `*Spanish*`-style subtitle artifacts, and mangled
transcripts even at strong mic volume). **The fix is a Bangla-fine-tuned Whisper model.**

Hard-won lessons already baked into the game side (do not re-learn these):
- **Fine-tuned Whisper models are transcribe-only** — fine-tuning loses the translate
  task (same reason large-v3-turbo silently failed at translation). The Unity pipeline
  was ALREADY switched to transcribe + NLLB-for-translation (2026-07-16), so a
  transcribe-only fine-tune drops straight in with zero further code changes.
- Near-silent/short clips make Whisper hallucinate — the game already gates these
  (minSendPeak, 0.5s minimum) so don't chase those ghosts during evaluation.
- The game runs whisper.cpp (`whisper-server.exe`), so the deliverable must be
  **ggml/gguf format**, not raw HuggingFace weights.

## 2. STEP ZERO — try existing community checkpoints before training anything

Bangla has an unusually strong open ML community (Bengali.AI). Fine-tuned Bangla Whisper
checkpoints ALREADY EXIST on HuggingFace. Evaluating one is a ~day; training your own is
weeks. Do this first:

1. Search HuggingFace for Bangla/Bengali fine-tuned Whisper models — known starting
   points to evaluate (verify current state/licenses yourself):
   - `bangla-speech-processing/BanglaASR` (Whisper fine-tuned on Bengali data)
   - Models from the 2022 HF Whisper fine-tuning event tagged `bn`
   - Anything trained on the Bengali.AI Speech dataset (Kaggle competition, ~1,200 hours)
2. Test the checkpoint in Python first (HF `pipeline("automatic-speech-recognition")`)
   against YOUR OWN recorded Bangla — not just their reported WER. Reported WER is on
   clean read speech; the game's reality is conversational speech through a gaming mic.
3. If one is good: convert to ggml (whisper.cpp repo,
   `models/convert-h5-to-ggml.py`), drop the result into
   `Assets/StreamingAssets/whisper/`, set `banglaModelContains` on the
   `WhisperTranscriber` component to match its filename. Done — no training needed.

## 3. If no existing checkpoint is good enough — fine-tune your own

Same shape as the Piper voice project, bigger scale:

- **Data (abundant, this is the easy part):**
  - Bengali.AI Speech (Kaggle, ~1,200h, includes out-of-distribution test sets)
  - Mozilla Common Voice Bangla (hundreds of hours, crowd-sourced)
  - OpenSLR SLR53 (~200h+ Bengali ASR corpus; SLR37 is the TTS one already used)
  - Shrutilipi / AI4Bharat Bengali subsets
  - Check licenses per-dataset before committing (same CC BY-SA caution as the TTS doc).
- **Base model:** whisper-small or whisper-medium. Small fine-tunes faster and runs
  faster in-game; medium has the higher ceiling. Community results suggest fine-tuned
  SMALL beats stock LARGE at Bangla by a wide margin — don't assume bigger base is needed.
- **Training:** HuggingFace's standard Whisper fine-tuning recipe (Seq2SeqTrainer).
  Needs the CUDA GPU (same card used for everything else). Expect hours-to-days
  depending on data volume; you don't need all 1,200 hours — 100-300h of clean,
  conversational-style data beats more hours of mismatched read speech.
- **Evaluate on your own voice + mic + real sentences** before calling it done. WER on
  a held-out set is the metric; the bar is "reads back what I actually said, nearly
  every sentence" at conversational volume.
- **Export:** convert to ggml via whisper.cpp's `models/convert-h5-to-ggml.py`, then
  verify STANDALONE against the game's actual `whisper-server.exe` before Unity:
  `whisper-server.exe -m your-model.bin -l bn --port 8798` + a test WAV.

## 4. Definition of done

`whisper-server.exe` (the exact binary shipped in the game) loads the fine-tuned ggml
model and transcribes YOUR conversational Bangla — spoken at normal volume through your
actual mic — accurately enough that you'd call it "it understands me," verified across
at least 20 varied sentences. Then: drop the .bin in `StreamingAssets/whisper/`, set
`banglaModelContains` to its filename substring, and the whole pipeline lights up with
zero further code changes.

## 5. What NOT to spend time on

- Don't build translation into the ASR (transcribe-only is correct — NLLB owns bn→en).
- Don't chase Whisper's hallucinations-on-silence during evals (already gated in-game).
- Don't ship raw HF weights or a Python ASR server — the game's whisper.cpp subprocess
  is already built, tested, and per-language-model-aware; ggml drop-in is the contract.
