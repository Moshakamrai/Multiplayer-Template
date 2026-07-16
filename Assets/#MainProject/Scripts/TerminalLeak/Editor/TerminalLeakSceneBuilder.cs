using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Tools > Terminal Leak > Create Solo Test Scene
//
// Builds the solo hotseat prototype of TERMINAL LEAK (see TerminalLeakConsole for the
// design): speak clues as Intel, type guesses as Operator, the SentinelAI wiretaps and
// races you. This scene exists to tune the Sentinel's fun-factor BEFORE any Mirror
// networking is written — Whisper/llama/Piper are created at runtime by the console.
public static class TerminalLeakSceneBuilder
{
    const string ScenePath = "Assets/Scenes/TerminalLeakSolo.unity";

    [MenuItem("Tools/Terminal Leak/Create Solo Test Scene")]
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

        var game = new GameObject("TerminalLeak");
        var console = game.AddComponent<TerminalLeakConsole>();
        console.Vosk = stt;

        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log($"[TerminalLeak] Solo test scene saved to {ScenePath}. Press Play: SPEAK disguised " +
                  "clues for the classified word (Intel role), TYPE guesses in the field (Operator " +
                  "role). The Sentinel wiretaps the radio and races you — its hearing sharpens every " +
                  "vault layer. Lose a word and you enter the 45s Visual Override while it lies to you.");
    }
}
