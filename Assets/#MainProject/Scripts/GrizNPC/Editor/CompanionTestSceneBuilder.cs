using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Tools > Griz > Create Companion Test Scene
// Builds and saves Assets/Scenes/CompanionTest.unity with Vosk (free vocabulary) wired
// to CompanionBrain + CompanionConsole — the OPEN-DOMAIN conversational NPC (talk about
// anything, not a shop negotiation). No manual setup needed.
public static class CompanionTestSceneBuilder
{
    const string ScenePath = "Assets/Scenes/CompanionTest.unity";

    [MenuItem("Tools/Griz/Create Companion Test Scene")]
    static void CreateScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        var voskGO = new GameObject("Vosk (free vocabulary)");
        var vp = voskGO.AddComponent<VoiceProcessor>();
        var stt = voskGO.AddComponent<VoskSpeechToText>();
        stt.VoiceProcessor = vp;
        stt.AutoStart = true;
        stt.KeyPhrases = new List<string>();
        stt.FreeDictation = true;
        stt.MaxAlternatives = 0;
        stt.MaxRecordLength = 8;

        var npcGO = new GameObject("Sana");
        var brain = npcGO.AddComponent<CompanionBrain>();
        var console = npcGO.AddComponent<CompanionConsole>();
        console.Vosk = stt;
        console.Brain = brain;

        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log($"[Companion] Test scene saved to {ScenePath}. Press Play and just talk to her about " +
                  "anything — no shop, no negotiation, genuinely open conversation. Same voice/LLM/TTS " +
                  "stack as Griz; different persona (edit CompanionBrain.persona to change who she is). " +
                  "Mic takes a few seconds to init — typed input works immediately.");
    }
}
