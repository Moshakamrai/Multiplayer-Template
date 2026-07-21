using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Tools > Gnomes & Gaslight > Create Character Lab Scene
//
// Spawns every blendshape head in the project side-by-side (only one visible at a time),
// each wired with FacialExpressions + MouthLipSync, plus a shared PiperVoice. Use the
// on-screen panel to audition models, expressions, shaders, and every installed TTS voice.
public static class CharacterLabSceneBuilder
{
    const string ScenePath = "Assets/Scenes/CharacterLab.unity";

    // Every blendshape model we know about. Prefab path preferred (it may already carry
    // GrizAnimatorLink etc.); FBX path used when there's no prefab.
    static readonly (string label, string path)[] Models =
    {
        ("Gunan",                "Assets/#MainProject/Prefabs/Gunan_animated.prefab"),
        ("Albert 2",             "Assets/#MainProject/Models/BlendshapeModels/Albert Einstein/source/Albert 2/Albert 2 model.fbx"),
        ("Sabine 2",             "Assets/#MainProject/Models/BlendshapeModels/Albert Einstein/source/Sabine 2/Sabine 2.fbx"),
        ("Inquisitor Head",      "Assets/#MainProject/Models/BlendshapeModels/Albert Einstein/source/Inqistor Head/Inqistor Head.fbx"),
        ("Purple Alien (female)","Assets/#MainProject/Models/BlendshapeModels/Albert Einstein/source/Female Purple Alien Face/Female Purple Alien Face model.fbx"),
    };

    [MenuItem("Tools/Gnomes & Gaslight/Create Character Lab Scene")]
    static void CreateScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        // Portrait framing, same as the interrogation scene so what you see here matches.
        var cam = Camera.main;
        if (cam != null)
        {
            cam.transform.position = new Vector3(0f, 1.5f, -2.4f);
            cam.transform.rotation = Quaternion.identity;
            cam.fieldOfView = 45f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.06f, 0.06f, 0.08f);
        }

        // A key light in front so expressions actually read (the default directional light
        // points away from a camera at -Z and leaves faces flat).
        var lightGO = GameObject.Find("Directional Light");
        if (lightGO != null)
        {
            lightGO.transform.position = new Vector3(0f, 3f, -3f);
            lightGO.transform.rotation = Quaternion.Euler(25f, 15f, 0f);
            var l = lightGO.GetComponent<Light>();
            if (l != null) l.intensity = 1.3f;
        }

        var labGO = new GameObject("CharacterLab");
        var lab = labGO.AddComponent<CharacterLab>();

        // One shared PiperVoice — the lab re-points it at whichever model is selected.
        var voiceGO = new GameObject("LabVoice");
        voiceGO.transform.SetParent(labGO.transform, false);
        lab.voice = voiceGO.AddComponent<PiperVoice>();

        int missing = 0;
        foreach (var (label, path) in Models)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) { Debug.LogWarning($"[CharacterLab] not found, skipping: {path}"); missing++; continue; }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            if (go == null) continue;
            go.name = label;
            go.transform.position = Vector3.zero;
            go.transform.rotation = Quaternion.identity;

            // Add the face rig components if the prefab/FBX doesn't already have them.
            var face = go.GetComponentInChildren<FacialExpressions>() ?? go.AddComponent<FacialExpressions>();
            var lip = go.GetComponentInChildren<MouthLipSync>() ?? go.AddComponent<MouthLipSync>();
            face.piperVoice = lab.voice;
            lip.piperVoice = lab.voice;

            lab.subjects.Add(new CharacterLab.Subject
            {
                label = label, instance = go, face = face, lipSync = lip
            });
            go.SetActive(false); // lab activates the selected one on Start
        }

        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log($"[CharacterLab] Scene saved to {ScenePath} with {lab.subjects.Count} model(s)" +
                  (missing > 0 ? $", {missing} missing" : "") +
                  ". Press Play, then use the left panel to switch models, fire expressions, " +
                  "change shader, pick a TTS voice, and hit SPEAK to watch lipsync.");
    }
}
