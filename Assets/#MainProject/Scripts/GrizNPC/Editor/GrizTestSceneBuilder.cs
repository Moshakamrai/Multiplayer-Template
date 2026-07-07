using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Tools > Griz > Create Griz Test Scene
// Builds and saves Assets/Scenes/GrizTest.unity with Vosk (free vocabulary, no grammar)
// wired to the GrizBrain + GrizTestConsole. No manual setup needed.
public static class GrizTestSceneBuilder
{
    const string ScenePath = "Assets/Scenes/GrizTest.unity";

    [MenuItem("Tools/Griz/Create Griz Test Scene")]
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
        stt.KeyPhrases = new List<string>();   // EMPTY = full free-speech recognition
        stt.MaxAlternatives = 0;
        stt.MaxRecordLength = 8;               // allow full sentences before restart

        var grizGO = new GameObject("Griz");
        var brain = grizGO.AddComponent<GrizBrain>();
        var console = grizGO.AddComponent<GrizTestConsole>();
        console.Vosk = stt;
        console.Brain = brain;

        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log($"[Griz] Test scene saved to {ScenePath}. Press Play and negotiate. " +
                  "Mic takes a few seconds to init — typed input works immediately.");
    }
}
