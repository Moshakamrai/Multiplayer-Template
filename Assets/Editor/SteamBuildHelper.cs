using UnityEngine;
using UnityEditor;
using UnityEditor.Callbacks;
using System.IO;

public class SteamBuildHelper
{
    [PostProcessBuild]
    public static void OnPostprocessBuild(BuildTarget target, string pathToBuiltProject)
    {
        if (target != BuildTarget.StandaloneWindows && target != BuildTarget.StandaloneWindows64) return;

        // Where is the file now? (Project Root)
        string sourceFile = Path.Combine(Directory.GetCurrentDirectory(), "steam_appid.txt");
        
        // Where should it go? (Next to the .exe)
        string destFile = Path.Combine(Path.GetDirectoryName(pathToBuiltProject), "steam_appid.txt");

        if (File.Exists(sourceFile))
        {
            File.Copy(sourceFile, destFile, true);
            Debug.Log("✅ [SteamBuildHelper] Copied steam_appid.txt to build folder successfully.");
        }
        else
        {
            Debug.LogWarning("⚠️ [SteamBuildHelper] Could not find steam_appid.txt in project root! Steam might not work in build.");
        }
    }
}