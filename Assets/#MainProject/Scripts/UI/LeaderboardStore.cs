using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// Durable high-score leaderboard. Saves to a real file in Application.persistentDataPath so it
/// survives quitting the game AND works in builds (PlayerPrefs would too, but a file is easy to
/// inspect/reset and matches how we persist beat maps). One entry = a player name + a match score.
///
/// Stored newest-sorted-by-score; we keep the top MAX_ENTRIES. The player's name comes straight from
/// what they already set (GameManager.PlayerName / PlayerController.PlayerName) — no extra prompt.
public static class LeaderboardStore
{
    [System.Serializable]
    public struct Entry
    {
        public string name;
        public int    score;
        public string date; // yyyy-MM-dd, for display/tiebreak feel
    }

    [System.Serializable]
    private class Wrapper { public List<Entry> entries = new List<Entry>(); }

    private const int MAX_ENTRIES = 20;
    private static string FilePath => Path.Combine(Application.persistentDataPath, "leaderboard.json");

    private static List<Entry> _cache;

    /// All entries, highest score first. Cached after first load.
    public static List<Entry> All()
    {
        if (_cache != null) return _cache;
        _cache = Load();
        return _cache;
    }

    /// Record a player's score and persist. ONE entry per name — if the name already exists, its score
    /// is OVERWRITTEN with this one (so a player's row updates each round instead of duplicating).
    /// Returns the 1-based rank it landed at (0 = didn't make the top MAX_ENTRIES).
    public static int Record(string name, int score)
    {
        if (string.IsNullOrWhiteSpace(name)) name = "PLAYER";
        name = name.Trim();
        var list = Load();

        // Upsert: replace the existing entry for this name (case-insensitive), else add a new one.
        int existing = list.FindIndex(e => string.Equals(e.name, name, System.StringComparison.OrdinalIgnoreCase));
        var entry = new Entry { name = name, score = score, date = System.DateTime.Now.ToString("yyyy-MM-dd") };
        if (existing >= 0) list[existing] = entry;
        else               list.Add(entry);

        // Highest score first; keep only the top N.
        list.Sort((a, b) => b.score.CompareTo(a.score));
        if (list.Count > MAX_ENTRIES) list.RemoveRange(MAX_ENTRIES, list.Count - MAX_ENTRIES);

        _cache = list;
        Save(list);

        for (int i = 0; i < list.Count; i++)
            if (string.Equals(list[i].name, name, System.StringComparison.OrdinalIgnoreCase)) return i + 1;
        return 0;
    }

    /// True if a player name is already on the board (case-insensitive). Used to reject duplicate names.
    public static bool NameExists(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        name = name.Trim();
        foreach (var e in All())
            if (string.Equals(e.name, name, System.StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    // Preset scores shown on a fresh board — challenging but beatable benchmarks for players to chase.
    // Real runs overwrite/overtake these (one entry per name). Tune the numbers here.
    private static List<Entry> SeedDefaults()
    {
        return new List<Entry>
        {
            new Entry { name = "ACE", score = 92000, date = "2026-06-01" },
            new Entry { name = "NEO", score = 78500, date = "2026-06-01" },
            new Entry { name = "VYX", score = 64000, date = "2026-06-01" },
            new Entry { name = "KAI", score = 51000, date = "2026-06-01" },
            new Entry { name = "ZED", score = 38000, date = "2026-06-01" },
        };
    }

    private static List<Entry> Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var w = JsonUtility.FromJson<Wrapper>(File.ReadAllText(FilePath));
                if (w?.entries != null)
                {
                    w.entries.Sort((a, b) => b.score.CompareTo(a.score));
                    return w.entries;
                }
            }
        }
        catch (System.Exception e) { Debug.LogWarning($"[LeaderboardStore] load failed: {e.Message}"); }

        // No saved board yet → seed the default benchmark scores AND write them so they persist.
        var seeded = SeedDefaults();
        Save(seeded);
        return seeded;
    }

    private static void Save(List<Entry> list)
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            var w = new Wrapper { entries = list };
            File.WriteAllText(FilePath, JsonUtility.ToJson(w, true));
            Debug.Log($"<color=green>[LeaderboardStore]</color> saved {list.Count} entries → {FilePath}");
        }
        catch (System.Exception e) { Debug.LogWarning($"[LeaderboardStore] save failed: {e.Message}"); }
    }
}
