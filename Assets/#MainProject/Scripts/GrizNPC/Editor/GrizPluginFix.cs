using System.IO;
using UnityEditor;
using UnityEngine;

// Tools > Griz > Fix Plugin DLL Warnings
//
// llama.cpp and whisper.cpp both ship DLLs with identical names (ggml.dll,
// ggml-cuda.dll, ggml-cpu-*.dll...). Unity's importer treats anything under
// StreamingAssets with a recognized native-plugin extension as an EDITOR
// plugin candidate, and complains when two files share a name.
//
// These DLLs are NEVER loaded by Unity/the Editor — they're only read by our
// own subprocesses (llama-server.exe / whisper-server.exe) from disk. So the
// correct fix is telling the importer they are NOT Editor-compatible plugins
// at all, which permanently silences the warning (this is not a workaround,
// it's the accurate setting: Unity should never have tried to load them).
public static class GrizPluginFix
{
    [MenuItem("Tools/Griz/Fix Plugin DLL Warnings")]
    static void FixPlugins()
    {
        string[] dirs = { "Assets/StreamingAssets/llama", "Assets/StreamingAssets/whisper" };
        int fixedCount = 0;

        foreach (var dir in dirs)
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var path in Directory.GetFiles(dir, "*.dll", SearchOption.TopDirectoryOnly))
            {
                string assetPath = path.Replace('\\', '/');
                var importer = AssetImporter.GetAtPath(assetPath) as PluginImporter;
                if (importer == null) continue;

                bool changed = false;
                if (importer.GetCompatibleWithEditor())
                {
                    importer.SetCompatibleWithEditor(false);
                    changed = true;
                }
                if (importer.GetCompatibleWithAnyPlatform() || AnyPlatformEnabled(importer))
                {
                    importer.SetCompatibleWithAnyPlatform(false);
                    changed = true;
                }
                if (changed)
                {
                    importer.SaveAndReimport();
                    fixedCount++;
                }
            }
        }

        Debug.Log(fixedCount > 0
            ? $"[Griz] Fixed {fixedCount} DLL(s) — marked as NOT Editor plugins (they're only read by our subprocesses, never loaded by Unity). The 'Multiple plugins' warnings should be gone now."
            : "[Griz] No DLLs needed fixing (already correct, or folders not found).");
    }

    static bool AnyPlatformEnabled(PluginImporter importer)
    {
        foreach (BuildTarget t in System.Enum.GetValues(typeof(BuildTarget)))
        {
            try { if (importer.GetCompatibleWithPlatform(t)) return true; }
            catch { /* some enum values throw for invalid targets */ }
        }
        return false;
    }
}
