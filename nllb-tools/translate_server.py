"""
NLLB translation server — the 4th local subprocess in the stack, alongside
whisper-server.exe, llama-server.exe, and piper.exe.

Why this exists: Whisper's --translate mode (Bangla speech -> English text) works fine
for understanding the PLAYER. But asking the LLM (Qwen) to WRITE its reply directly in
Bangla produces broken grammar - Bangla is low-resource in its training data. This
server does the other half properly: the LLM keeps writing in English (where its
personality/wit actually works), and this translates the finished English line into
real Bangla with a dedicated translation model (NLLB-200), which is what NLLB is built
for and is dramatically better at it than a general chat LLM.

Endpoints:
  GET  /health                          -> {"status": "ok"}
  POST /translate  {"text": "...", "target_lang": "ben_Beng", "source_lang": "eng_Latn"}
                                         -> {"translation": "..."}

Language codes are NLLB's FLORES-200 codes. The two this project needs:
  eng_Latn = English   ben_Beng = Bangla (Bengali script)

Run: python translate_server.py --port 8733
Model dir defaults to ./nllb-600m-ct2 (built via convert_model.py / the README).
"""
import argparse
import sys

from flask import Flask, request, jsonify
import ctranslate2
import transformers

app = Flask(__name__)
translator = None
tokenizers = {}  # cache one tokenizer per src_lang (AutoTokenizer's src_lang affects encoding)

MODEL_HF_ID = "facebook/nllb-200-distilled-600M"


def get_tokenizer(src_lang: str):
    if src_lang not in tokenizers:
        tokenizers[src_lang] = transformers.AutoTokenizer.from_pretrained(MODEL_HF_ID, src_lang=src_lang)
    return tokenizers[src_lang]


@app.route("/health", methods=["GET"])
def health():
    return jsonify({"status": "ok"})


@app.route("/translate", methods=["POST"])
def translate():
    data = request.get_json(force=True, silent=True) or {}
    text = (data.get("text") or "").strip()
    target_lang = data.get("target_lang", "ben_Beng")
    source_lang = data.get("source_lang", "eng_Latn")

    if not text:
        return jsonify({"translation": ""})

    try:
        tok = get_tokenizer(source_lang)
        tokens = tok.convert_ids_to_tokens(tok(text).input_ids)
        results = translator.translate_batch([tokens], target_prefix=[[target_lang]])
        out_tokens = results[0].hypotheses[0][1:]  # drop the target-lang prefix token
        out_text = tok.decode(tok.convert_tokens_to_ids(out_tokens))
        return jsonify({"translation": out_text})
    except Exception as e:
        return jsonify({"error": str(e)}), 500


def main():
    global translator
    parser = argparse.ArgumentParser()
    parser.add_argument("--port", type=int, default=8733)
    parser.add_argument("--model_dir", default="nllb-600m-ct2")
    parser.add_argument("--device", default="cpu", choices=["cpu", "cuda"])
    args = parser.parse_args()

    print(f"[nllb] loading model from {args.model_dir} on {args.device} ...", flush=True)
    translator = ctranslate2.Translator(args.model_dir, device=args.device)
    print(f"[nllb] ready — listening on http://127.0.0.1:{args.port}", flush=True)

    app.run(host="127.0.0.1", port=args.port, threaded=False)


if __name__ == "__main__":
    main()
