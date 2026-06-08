#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using System.IO;
using System.Collections.Generic;

/// Bakes the custom beat-maps you created in the editor (stored in PlayerPrefs) into
/// TextAssets under Assets/Resources/BeatMaps/. PlayerPrefs is empty in a fresh build, so
/// without this the custom tracks never appear on device. Resources DO ship with builds, and
/// RhythmRoundManager.GetMapTapData() reads from here when PlayerPrefs has no entry.
///
/// USAGE: map your tracks in the editor as usual, then run
///   Tools ▸ Beat Maps ▸ Export PlayerPrefs Maps to Resources
/// and assign the matching AudioClips to RhythmRoundManager.availableTracks. Rebuild — done.
public static class BeatMapExporter
{
    private const string OUT_DIR = "Assets/Resources/BeatMaps";

    [MenuItem("Tools/Beat Maps/Export PlayerPrefs Maps to Resources")]
    public static void Export()
    {
        Directory.CreateDirectory(OUT_DIR);

        // Gather candidate map names: the saved registry + any names from availableTracks.
        var names = new HashSet<string>();
        string registry = PlayerPrefs.GetString("CustomMapRegistry", "");
        foreach (var n in registry.Split('|'))
            if (!string.IsNullOrEmpty(n)) names.Add(n);

        var rmm = Object.FindObjectOfType<RhythmRoundManager>();
        if (rmm != null && rmm.availableTracks != null)
            foreach (var clip in rmm.availableTracks)
                if (clip != null) names.Add(clip.name);

        int exported = 0;
        var missing = new List<string>();
        foreach (var name in names)
        {
            string key = "CustomMap_" + name;
            if (!PlayerPrefs.HasKey(key)) { missing.Add(name); continue; }

            string data = PlayerPrefs.GetString(key);
            File.WriteAllText(Path.Combine(OUT_DIR, SafeName(name) + ".txt"), data);
            exported++;
            Debug.Log($"[BeatMapExporter] Baked '{name}'  ({data.Split('|').Length} taps).");
        }

        AssetDatabase.Refresh();

        Debug.Log($"[BeatMapExporter] DONE — {exported} map(s) written to {OUT_DIR}. " +
                  "These now ship with builds. Make sure each track's AudioClip is in " +
                  "RhythmRoundManager.availableTracks too.");
        if (missing.Count > 0)
            Debug.LogWarning($"[BeatMapExporter] No beat-data in PlayerPrefs for: {string.Join(", ", missing)} " +
                             "(map them in the editor first, then re-run).");
    }

    // Resources lookup is by name without extension; keep filenames filesystem-safe.
    private static string SafeName(string s)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s;
    }
}

/// Runs the exporter automatically before every build, so the custom maps are always baked
/// fresh and you can never forget to do it manually.
public class BeatMapBuildPreprocessor : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        Debug.Log("[BeatMapExporter] Auto-baking custom maps before build...");
        BeatMapExporter.Export();
    }
}
#endif
