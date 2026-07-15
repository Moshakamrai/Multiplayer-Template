using System.IO;
using UnityEditor;
using UnityEngine;

// Tools > Griz > Fix Plugin DLL Warnings
//
// llama.cpp and whisper.cpp both ship DLLs with identical names (ggml.dll,
// ggml-cuda.dll, ggml-cpu-*.dll...). These DLLs are NEVER loaded by Unity/the
// Editor — they're only read by our own subprocesses (llama-server.exe /
// whisper-server.exe) from disk. The correct fix is telling Unity's importer
// they are NOT Editor-compatible plugins, which permanently silences the
// "Multiple plugins with the same name" warning.
//
// v2: the first version silently did nothing on this project, because these
// DLLs' .meta files were stuck on the plain DefaultImporter (not
// PluginImporter) — so `AssetImporter.GetAtPath(...) as PluginImporter`
// always returned null and every file was skipped without saying why. Root
// cause: forcing a reimport (delete + refresh) makes Unity redetect them as
// native plugins from scratch, which produces a real PluginImporter this
// time. This version does that automatically instead of assuming it's
// already a PluginImporter.
public static class GrizPluginFix
{
    [MenuItem("Tools/Griz/Fix Plugin DLL Warnings")]
    static void FixPlugins()
    {
        string[] dirs = { "Assets/StreamingAssets/llama", "Assets/StreamingAssets/whisper" };
        int reimported = 0, fixedCount = 0, stillWrongType = 0;

        // Pass 1: force a clean reimport of every DLL so Unity redetects it as a plugin.
        foreach (var dir in dirs)
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var path in Directory.GetFiles(dir, "*.dll", SearchOption.TopDirectoryOnly))
            {
                string assetPath = path.Replace('\\', '/');
                if (AssetImporter.GetAtPath(assetPath) is PluginImporter) continue; // already correct type
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                reimported++;
            }
        }
        if (reimported > 0) AssetDatabase.Refresh();

        // Pass 2: now actually configure them as non-Editor plugins.
        foreach (var dir in dirs)
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var path in Directory.GetFiles(dir, "*.dll", SearchOption.TopDirectoryOnly))
            {
                string assetPath = path.Replace('\\', '/');
                if (!(AssetImporter.GetAtPath(assetPath) is PluginImporter importer))
                {
                    stillWrongType++;
                    Debug.LogWarning($"[Griz] '{assetPath}' is STILL not a PluginImporter after reimport " +
                        $"(got {AssetImporter.GetAtPath(assetPath)?.GetType().Name ?? "null"}). " +
                        "This file may need its .meta deleted manually, then Unity re-focused to reimport.");
                    continue;
                }

                bool changed = false;
                if (importer.GetCompatibleWithEditor())
                {
                    importer.SetCompatibleWithEditor(false);
                    changed = true;
                }
                if (importer.GetCompatibleWithAnyPlatform())
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

        Debug.Log($"[Griz] Plugin fix pass: reimported {reimported} DLL(s) as native plugins, " +
                  $"fixed {fixedCount} to be non-Editor, {stillWrongType} still wrong (see warnings above). " +
                  (stillWrongType == 0
                      ? "The 'Multiple plugins' warnings should be gone now — restart Unity if they still show (the warning is sometimes cached until the next domain reload)."
                      : "Some files need manual attention — see warnings above."));
    }
}
