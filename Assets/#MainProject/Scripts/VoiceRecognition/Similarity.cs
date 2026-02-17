using UnityEngine;

public static class Similarity {
    
    // Returns a score from 0.0 (completely different) to 1.0 (exact match)
    public static float Compare(string source, string target) {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target)) return 0.0f;
        
        source = source.ToLower();
        target = target.ToLower();
    
        int distance = LevenshteinDistance(source, target);
        int maxLength = Mathf.Max(source.Length, target.Length);
        
        return 1.0f - ((float)distance / maxLength);
    }
    
    // Standard algorithm to count character differences
    private static int LevenshteinDistance(string s, string t) {
        int n = s.Length;
        int m = t.Length;
        int[,] d = new int[n + 1, m + 1];
    
        if (n == 0) return m;
        if (m == 0) return n;
    
        for (int i = 0; i <= n; d[i, 0] = i++) { }
        for (int j = 0; j <= m; d[0, j] = j++) { }
    
        for (int i = 1; i <= n; i++) {
            for (int j = 1; j <= m; j++) {
                int cost = (t[j - 1] == s[i - 1]) ? 0 : 1;
                d[i, j] = Mathf.Min(
                    Mathf.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }
        return d[n, m];
    }
}