using System.IO;
using UnityEditor;
using UnityEngine;

// Tools > Griz > Check Piper Setup — validates the shipped-TTS install and prints
// exactly what to download if anything is missing.
public static class GrizPiperSetup
{
    static string Dir => Path.Combine(Application.streamingAssetsPath, "piper");

    [MenuItem("Tools/Griz/Check Piper Setup")]
    static void Check()
    {
        Directory.CreateDirectory(Dir);

        bool exe = File.Exists(Path.Combine(Dir, "piper.exe"));
        bool espeak = Directory.Exists(Path.Combine(Dir, "espeak-ng-data"));
        string model = null;
        foreach (var f in Directory.GetFiles(Dir, "*.onnx")) { model = Path.GetFileName(f); break; }
        bool json = model != null && File.Exists(Path.Combine(Dir, model + ".json"));

        if (exe && espeak && model != null && json)
        {
            Debug.Log($"[Piper] ✅ READY — piper.exe + espeak-ng-data + voice '{model}' found. " +
                      "Griz will use Piper; gibberish stays as fallback.");
            return;
        }

        Debug.LogWarning(
            "[Piper] Setup incomplete. Put these into Assets/StreamingAssets/piper/ :\n" +
            $"  {(exe ? "✅" : "❌")} piper.exe  (+ its DLLs)\n" +
            $"  {(espeak ? "✅" : "❌")} espeak-ng-data folder\n" +
            $"  {(model != null ? "✅ " + model : "❌ a voice model (.onnx)")}\n" +
            $"  {(json ? "✅" : "❌")} the matching .onnx.json\n\n" +
            "DOWNLOADS (both free):\n" +
            "1) Piper release: https://github.com/rhasspy/piper/releases → piper_windows_amd64.zip\n" +
            "   Extract the WHOLE zip contents (piper.exe, *.dll, espeak-ng-data/) into the piper folder.\n" +
            "2) A voice: https://huggingface.co/rhasspy/piper-voices → en/en_US/ryan/medium/\n" +
            "   Grab en_US-ryan-medium.onnx AND en_US-ryan-medium.onnx.json (deep male — good Griz).\n" +
            "   (Also fun to audition: en_US-hfc_male-medium, en_GB-alan-medium.)\n\n" +
            "LICENSING NOTE: keep piper's LICENSE files in the folder and ship them with the game.\n" +
            "Piper runs as a SEPARATE exe (subprocess) — do not link it into game code.");

        EditorUtility.RevealInFinder(Dir);
    }

    static string LlamaDir => Path.Combine(Application.streamingAssetsPath, "llama");

    [MenuItem("Tools/Griz/Check Llama Setup")]
    static void CheckLlama()
    {
        Directory.CreateDirectory(LlamaDir);

        bool exe = File.Exists(Path.Combine(LlamaDir, "llama-server.exe"));
        string model = null;
        foreach (var f in Directory.GetFiles(LlamaDir, "*.gguf")) { model = Path.GetFileName(f); break; }

        if (exe && model != null)
        {
            Debug.Log($"[Llama] ✅ READY — llama-server.exe + model '{model}' found. " +
                      "Griz will use the LLM to understand sentences; keyword classifier stays as fallback.");
            return;
        }

        Debug.LogWarning(
            "[Llama] Setup incomplete. Put these into Assets/StreamingAssets/llama/ :\n" +
            $"  {(exe ? "✅" : "❌")} llama-server.exe (+ its DLLs)\n" +
            $"  {(model != null ? "✅ " + model : "❌ a model (.gguf)")}\n\n" +
            "DOWNLOADS (both free, both offline):\n" +
            "1) llama.cpp: https://github.com/ggml-org/llama.cpp/releases → llama-bXXXX-bin-win-cpu-x64.zip\n" +
            "   Extract EVERYTHING (llama-server.exe + all .dll files) into the llama folder.\n" +
            "2) Model: https://huggingface.co/bartowski/Llama-3.2-1B-Instruct-GGUF\n" +
            "   → download Llama-3.2-1B-Instruct-Q4_K_M.gguf (~0.8GB) into the same folder.\n\n" +
            "LICENSING: llama.cpp is MIT. Llama 3.2 uses the Llama Community License — fine for a\n" +
            "commercial indie game; include the license file and 'Built with Llama' attribution.\n" +
            "The server runs as a SEPARATE exe (subprocess) — keep it that way.");

        EditorUtility.RevealInFinder(LlamaDir);
    }

    static string WhisperDir => Path.Combine(Application.streamingAssetsPath, "whisper");

    [MenuItem("Tools/Griz/Check Whisper Setup")]
    static void CheckWhisper()
    {
        Directory.CreateDirectory(WhisperDir);

        bool exe = File.Exists(Path.Combine(WhisperDir, "whisper-server.exe")) ||
                   File.Exists(Path.Combine(WhisperDir, "server.exe"));
        string model = null;
        foreach (var f in Directory.GetFiles(WhisperDir, "*.bin")) { model = Path.GetFileName(f); break; }

        if (exe && model != null)
        {
            Debug.Log($"[Whisper] ✅ READY — server + model '{model}' found. Hub finals use Whisper; " +
                      "Vosk keeps live feedback + combat. Set language/translate on the WhisperTranscriber component (bn = Bangla).");
            return;
        }

        Debug.LogWarning(
            "[Whisper] Setup incomplete. Put these into Assets/StreamingAssets/whisper/ :\n" +
            $"  {(exe ? "✅" : "❌")} whisper-server.exe (+ its DLLs)\n" +
            $"  {(model != null ? "✅ " + model : "❌ a model (ggml-*.bin)")}\n\n" +
            "DOWNLOADS (both free, both offline):\n" +
            "1) whisper.cpp: https://github.com/ggml-org/whisper.cpp/releases\n" +
            "   → the Windows x64 binary zip (cuda variant if you have an NVIDIA card).\n" +
            "   Extract EVERYTHING (whisper-server.exe + all .dll files) into the whisper folder.\n" +
            "2) Model: https://huggingface.co/ggerganov/whisper.cpp/tree/main\n" +
            "   → ggml-small.bin (~466MB, recommended) into the same folder.\n" +
            "   (ggml-base.bin ~142MB is a lighter fallback; ggml-medium.bin if Bangla becomes primary.)\n\n" +
            "BANGLA: set language=bn and translateToEnglish=true on the WhisperTranscriber component —\n" +
            "players speak Bangla, the NPC pipeline receives English. whisper.cpp is MIT licensed.");

        EditorUtility.RevealInFinder(WhisperDir);
    }
}
