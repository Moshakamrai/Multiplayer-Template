using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// Durable storage for custom beat-maps so they NEVER vanish on quit and DO ship in builds.
///
/// The old system kept maps only in PlayerPrefs, which: (a) can desync/lose entries between the
/// registry key and the data key, and (b) is empty in a fresh build — so editor-made maps never
/// appeared on device. This writes a real .txt file the instant you save, in two places:
///
///   • IN THE EDITOR → Assets/Resources/BeatMaps/<name>.txt (+ AssetDatabase.Refresh). This is the
///     SAME folder builds read from, so a map you author is permanent, commits to git, and ships in
///     every build automatically — no "Export" step to forget.
///   • IN A BUILD     → Application.persistentDataPath/BeatMaps/<name>.txt, so maps authored inside a
///     standalone build also survive quitting and relaunching.
///
/// PlayerPrefs is still written too (legacy readers + the live registry), but it is no longer the
/// source of truth. Loading prefers, in order: persistentDataPath file → Resources baked → PlayerPrefs.
public static class BeatMapStore
{
    private const string RES_DIR = "Assets/Resources/BeatMaps"; // editor-only, ships via Resources
    private const string SUBDIR  = "BeatMaps";

    private static string PersistentDir => Path.Combine(Application.persistentDataPath, SUBDIR);

    private const string RES_MUSIC_DIR = "Assets/Resources/Music"; // editor-only, ships via Resources
    private static string SongsDir => Path.Combine(Application.persistentDataPath, "Songs");

    /// Save a map's beat times (the "|"-joined string) + COPY the song's audio into durable storage so
    /// the song the player just browsed to becomes a permanent in-game track — no extra steps. Mirrors
    /// to PlayerPrefs for the legacy registry path.
    public static void Save(string mapName, string beatData, string sourceAudioPath = null)
    {
        if (string.IsNullOrEmpty(mapName)) return;
        string safe = SafeName(mapName);

#if UNITY_EDITOR
        // Beat data → permanent, version-controlled, ships in builds via Resources.
        Directory.CreateDirectory(RES_DIR);
        File.WriteAllText(Path.Combine(RES_DIR, safe + ".txt"), beatData);
#else
        // Standalone build: keep authored maps across relaunches.
        Directory.CreateDirectory(PersistentDir);
        File.WriteAllText(Path.Combine(PersistentDir, safe + ".txt"), beatData);
#endif

        // COPY THE AUDIO into the game so it's permanent and ships — the browse moment IS the import.
        string durableAudioPath = CopySongIntoGame(safe, sourceAudioPath);

#if UNITY_EDITOR
        UnityEditor.AssetDatabase.Refresh(); // import both the .txt and the copied audio as assets
#endif

        // Mirror to PlayerPrefs (legacy + the live registry the picker also scans). Store the DURABLE
        // audio path (the in-game copy), not the original browse location which may move/vanish.
        PlayerPrefs.SetString("CustomMap_" + mapName, beatData);
        string pathToRemember = !string.IsNullOrEmpty(durableAudioPath) ? durableAudioPath : sourceAudioPath;
        if (!string.IsNullOrEmpty(pathToRemember))
            PlayerPrefs.SetString("CustomMapPath_" + mapName, pathToRemember);

        var reg = new HashSet<string>(
            PlayerPrefs.GetString("CustomMapRegistry", "")
                .Split(new[] { '|' }, System.StringSplitOptions.RemoveEmptyEntries));
        reg.Add(mapName);
        PlayerPrefs.SetString("CustomMapRegistry", string.Join("|", reg));
        PlayerPrefs.Save();

        Debug.Log($"<color=green>[BeatMapStore] Saved '{mapName}' durably ({beatData.Split('|').Length} beats) " +
                  $"+ audio → {pathToRemember}");
    }

    /// Copy the browsed audio file into the game's durable storage and return the new path.
    ///   • Editor → Assets/Resources/Music/<name>.<ext>  (ships in builds, loadable via Resources)
    ///   • Build  → persistentDataPath/Songs/<name>.<ext> (survives quit, loaded by file path)
    /// Returns "" if there's no source file to copy (e.g. an inspector clip already in the project).
    private static string CopySongIntoGame(string safeName, string sourceAudioPath)
    {
        if (string.IsNullOrEmpty(sourceAudioPath) || !File.Exists(sourceAudioPath)) return "";

        string ext = Path.GetExtension(sourceAudioPath);
        if (string.IsNullOrEmpty(ext)) ext = ".mp3";

        try
        {
#if UNITY_EDITOR
            Directory.CreateDirectory(RES_MUSIC_DIR);
            string dest = Path.Combine(RES_MUSIC_DIR, safeName + ext);
#else
            Directory.CreateDirectory(SongsDir);
            string dest = Path.Combine(SongsDir, safeName + ext);
#endif
            // Don't recopy if it's already the same file (re-saving an existing map).
            string full = Path.GetFullPath(sourceAudioPath);
            if (!string.Equals(full, Path.GetFullPath(dest), System.StringComparison.OrdinalIgnoreCase))
                File.Copy(sourceAudioPath, dest, overwrite: true);
            return dest;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[BeatMapStore] Could not copy song into game: {e.Message}");
            return "";
        }
    }

    /// Load a map's beat-data string, or "" if none. File on disk wins over PlayerPrefs so a freshly
    /// authored (or git-pulled) map is always found, even if PlayerPrefs is stale/empty.
    public static string Load(string mapName)
    {
        if (string.IsNullOrEmpty(mapName)) return "";
        string safe = SafeName(mapName);

        // 1) persistentDataPath (maps authored inside a build).
        string p = Path.Combine(PersistentDir, safe + ".txt");
        if (File.Exists(p)) return File.ReadAllText(p);

        // 2) Resources baked (.txt shipped with the build / authored in editor).
        var baked = Resources.Load<TextAsset>("BeatMaps/" + safe);
        if (baked != null) return baked.text;

        // 3) PlayerPrefs legacy.
        string key = "CustomMap_" + mapName;
        return PlayerPrefs.HasKey(key) ? PlayerPrefs.GetString(key) : "";
    }

    /// STRICT existence: does this map have a real beatmap FILE on disk (persistentDataPath .txt or a
    /// baked Resources TextAsset)? Ignores PlayerPrefs entirely. Use this for the Level UI so a map you
    /// deleted from disk never lingers as a ghost button just because stale beat data is still in prefs.
    public static bool ExistsOnDisk(string mapName)
    {
        if (string.IsNullOrEmpty(mapName)) return false;
        string safe = SafeName(mapName);
        if (File.Exists(Path.Combine(PersistentDir, safe + ".txt"))) return true;
        return Resources.Load<TextAsset>("BeatMaps/" + safe) != null;
    }

    /// Every custom map name we can find on disk (persistent + baked Resources), de-duped.
    public static IEnumerable<string> AllNames()
    {
        var seen = new HashSet<string>();

        if (Directory.Exists(PersistentDir))
            foreach (var f in Directory.GetFiles(PersistentDir, "*.txt"))
            {
                string n = Path.GetFileNameWithoutExtension(f);
                if (seen.Add(n)) yield return n;
            }

        foreach (var ta in Resources.LoadAll<TextAsset>("BeatMaps"))
            if (ta != null && !string.IsNullOrEmpty(ta.name) && seen.Add(ta.name))
                yield return ta.name;
    }

    /// Find the durable audio file for a map by name (the copy made at save time), or "" if none.
    /// Checks the build Songs folder and the editor Resources/Music folder for any supported extension.
    /// Lets the game resolve a map's song even if PlayerPrefs was cleared — the file alone is enough.
    public static string FindSongFile(string mapName)
    {
        if (string.IsNullOrEmpty(mapName)) return "";
        string safe = SafeName(mapName);
        string[] exts = { ".mp3", ".wav", ".ogg", ".aiff", ".aif" };

        foreach (var dir in new[] { SongsDir,
#if UNITY_EDITOR
            RES_MUSIC_DIR
#else
            ""
#endif
        })
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
            foreach (var ext in exts)
            {
                string p = Path.Combine(dir, safe + ext);
                if (File.Exists(p)) return p;
            }
        }
        return "";
    }

    public static string SafeName(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s;
    }
}
