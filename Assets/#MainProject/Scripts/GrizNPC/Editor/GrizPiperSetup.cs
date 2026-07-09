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
}
