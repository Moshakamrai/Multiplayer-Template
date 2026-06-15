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

    /// Add a finished match's result and persist. Returns the 1-based rank it landed at (0 = didn't
    /// make the top MAX_ENTRIES).
    public static int Record(string name, int score)
    {
        if (string.IsNullOrWhiteSpace(name)) name = "PLAYER";
        var list = Load();
        list.Add(new Entry
        {
            name  = name.Trim(),
            score = score,
            date  = System.DateTime.Now.ToString("yyyy-MM-dd")
        });

        // Highest score first; keep only the top N.
        list.Sort((a, b) => b.score.CompareTo(a.score));
        if (list.Count > MAX_ENTRIES) list.RemoveRange(MAX_ENTRIES, list.Count - MAX_ENTRIES);

        _cache = list;
        Save(list);

        // Find where THIS exact entry ranked (first matching name+score).
        for (int i = 0; i < list.Count; i++)
            if (list[i].name == name.Trim() && list[i].score == score) return i + 1;
        return 0;
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
        return new List<Entry>();
    }

    private static void Save(List<Entry> list)
    {
        try
        {
            var w = new Wrapper { entries = list };
            File.WriteAllText(FilePath, JsonUtility.ToJson(w, true));
        }
        catch (System.Exception e) { Debug.LogWarning($"[LeaderboardStore] save failed: {e.Message}"); }
    }
}
