# NLLB Translation Server — Bangla NPC replies

Separate from the Unity project on purpose (same reasoning as the Bangla Piper voice
training project) — Python/torch/model-cache files don't belong inside `Assets/` or in
the game's git history. This folder is **not** committed to the game repo (see the root
`.gitignore` — everything in here except this README is ignored).

## What this is

The 4th local subprocess in the game's AI stack (alongside whisper-server.exe,
llama-server.exe, piper.exe): a dedicated English<->Bangla translation model
(**NLLB-200, distilled 600M**, run via `ctranslate2`), used ONLY to translate the NPC's
finished English reply into Bangla before Piper speaks it.

**Why not just ask the chat LLM (Qwen) to write Bangla directly?** Tried it — the
grammar is genuinely broken (garbled conjuncts, wrong verb forms). Bangla is a
low-resource language in Qwen's training data. NLLB is a model built *specifically* for
translation and is dramatically better. So: Qwen reasons and writes personality in
English (its actual strength) -> NLLB translates the finished line to real Bangla ->
Piper speaks it.

## One-time setup

```
python -m pip install ctranslate2==4.4.0 transformers==4.40.2 sentencepiece flask huggingface_hub
```

**The version pins matter.** Newer `ctranslate2` (4.8.x) + this `transformers` version
hit a `dtype` keyword-argument mismatch in the HuggingFace model-loading step during
conversion (confirmed 2026-07-16) — pin both together as above, don't upgrade one
without the other.

```
python -m ctranslate2.converters.transformers --model facebook/nllb-200-distilled-600M --output_dir nllb-600m-ct2 --quantization int8
```

This downloads the model from HuggingFace (~2.4GB) and converts it to a ~620MB int8
ctranslate2 model in `nllb-600m-ct2/`. One-time, a few minutes.

## Running

```
python translate_server.py --port 8733
```

Unity's `NllbTranslator.cs` launches this automatically (same subprocess pattern as the
other three model servers) — you shouldn't need to run it by hand except to test.

Quick standalone test once it's running:
```
curl -s -X POST http://127.0.0.1:8733/translate -H "Content-Type: application/json" -d "{\"text\":\"Why are you asking me about chickens?\",\"target_lang\":\"ben_Beng\",\"source_lang\":\"eng_Latn\"}"
```

## Files

| File | Purpose |
|---|---|
| `translate_server.py` | The Flask server Unity's `NllbTranslator.cs` launches and talks to. |
| `nllb-600m-ct2/` | The converted model (git-ignored, ~620MB, regenerate with the command above). |

## Notes / gotchas

- Windows console (cp1252) can't print Bengali script directly — if testing by hand with
  `python -c "print(...)"`, write to a UTF-8 file instead of printing, or you'll get a
  `UnicodeEncodeError` that has nothing to do with the translation itself.
- `device=cpu` is the default and is fine — single-line NPC replies translate in well
  under a second. Only switch to `cuda` if you've confirmed VRAM headroom; the 7B llama +
  Whisper-medium/large already use a meaningful chunk of a 6GB card.
- This is a genuinely different quality tier than asking a general chat LLM to translate
  — confirmed side-by-side: NLLB produced grammatically correct, natural Bangla on the
  first try where Qwen's direct Bangla generation was consistently broken.
