using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Tools > Griz > Create Multi-NPC Test Scene
// Builds Assets/Scenes/MultiNpcTest.unity with BOTH Griz and Sana in one scene, an
// NpcSwitcher tab bar (top-right) to pick who you're talking to, and ONE shared
// Vosk/Whisper/Llama backend — switching preserves each NPC's full conversation state.
public static class MultiNpcTestSceneBuilder
{
    const string ScenePath = "Assets/Scenes/MultiNpcTest.unity";

    [MenuItem("Tools/Griz/Create Multi-NPC Test Scene")]
    static void CreateScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        // ONE shared Vosk instance — each console's mic-event handlers gate on their own
        // Active flag, so only the currently-active NPC actually reacts to it.
        var voskGO = new GameObject("Vosk (free vocabulary, shared)");
        var vp = voskGO.AddComponent<VoiceProcessor>();
        var stt = voskGO.AddComponent<VoskSpeechToText>();
        stt.VoiceProcessor = vp;
        stt.AutoStart = true;
        stt.KeyPhrases = new List<string>();
        stt.FreeDictation = true;
        stt.MaxAlternatives = 0;
        stt.MaxRecordLength = 8;

        var grizGO = new GameObject("Griz");
        var grizBrain = grizGO.AddComponent<GrizBrain>();
        var grizConsole = grizGO.AddComponent<GrizTestConsole>();
        grizConsole.Vosk = stt;
        grizConsole.Brain = grizBrain;

        var sanaGO = new GameObject("Sana");
        var sanaBrain = sanaGO.AddComponent<CompanionBrain>();
        var sanaConsole = sanaGO.AddComponent<CompanionConsole>();
        sanaConsole.Vosk = stt;
        sanaConsole.Brain = sanaBrain;

        var switcherGO = new GameObject("NpcSwitcher");
        var switcher = switcherGO.AddComponent<NpcSwitcher>();
        switcher.npcs = new List<NpcSwitcher.NpcEntry>
        {
            new NpcSwitcher.NpcEntry { label = "Griz (shop)", grizConsole = grizConsole },
            new NpcSwitcher.NpcEntry { label = "Sana (open chat)", companionConsole = sanaConsole },
        };
        switcher.startIndex = 0;

        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log($"[Multi-NPC] Test scene saved to {ScenePath}. Press Play, use the tab bar " +
                  "(top-right) or TAB key to switch between Griz and Sana. Both NPCs share one " +
                  "Whisper/Llama backend — only ~1 model process running either way. Switching " +
                  "preserves each NPC's full conversation/state; pick up right where you left off.");
    }
}
