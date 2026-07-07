using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

// Tools > CyberPixel:
//  1) "Add Pixelate Feature To Renderer" — adds PixelateFeature to Assets/Settings/Renderer.asset
//  2) "Convert Open Scene To CyberPixel"  — swaps Lit-family materials on scene renderers for
//     generated CyberPixel/RimLit clones (originals untouched, mapping saved for revert)
//  3) "Revert Open Scene"                 — swaps clones back to the original materials
public static class CyberPixelConverter
{
    const string ShaderName = "CyberPixel/RimLit";
    const string GenFolder = "Assets/#MainProject/Shaders/CyberPixel/Generated";
    const string MapPath = GenFolder + "/conversion-map.json";
    const string RendererAssetPath = "Assets/Settings/Renderer.asset";

    // Only these shader families get converted — VFX/particles/skybox/UI are left alone
    // (the full-screen pixelate covers them anyway).
    static readonly string[] ConvertibleShaderNames =
    {
        "Universal Render Pipeline/Lit",
        "Universal Render Pipeline/Simple Lit",
        "Universal Render Pipeline/Complex Lit",
        "Universal Render Pipeline/Baked Lit",
        "Universal Render Pipeline/Unlit",
        "Standard",
    };

    [Serializable]
    class MaterialPair { public string clonePath; public string originalPath; }

    [Serializable]
    class ConversionMap { public List<MaterialPair> pairs = new List<MaterialPair>(); }

    [MenuItem("Tools/CyberPixel/Add Pixelate Feature To Renderer")]
    static void AddPixelateFeature()
    {
        var rendererData = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(RendererAssetPath);
        if (rendererData == null)
        {
            Debug.LogError($"[CyberPixel] Renderer asset not found at {RendererAssetPath}");
            return;
        }
        foreach (var f in rendererData.rendererFeatures)
        {
            if (f is PixelateFeature)
            {
                Debug.Log("[CyberPixel] Pixelate feature already on the renderer — nothing to do.");
                return;
            }
        }

        var feature = ScriptableObject.CreateInstance<PixelateFeature>();
        feature.name = "CyberPixel Pixelate";
        AssetDatabase.AddObjectToAsset(feature, rendererData);
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId);

        var so = new SerializedObject(rendererData);
        var features = so.FindProperty("m_RendererFeatures");
        features.arraySize++;
        features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
        var featureMap = so.FindProperty("m_RendererFeatureMap");
        featureMap.arraySize++;
        featureMap.GetArrayElementAtIndex(featureMap.arraySize - 1).longValue = localId;
        so.ApplyModifiedProperties();

        EditorUtility.SetDirty(rendererData);
        AssetDatabase.SaveAssets();
        Debug.Log("[CyberPixel] Pixelate feature added to Renderer.asset (pixelHeight=360 — tune it there).");
    }

    [MenuItem("Tools/CyberPixel/Convert Open Scene To CyberPixel")]
    static void ConvertOpenScene()
    {
        var shader = Shader.Find(ShaderName);
        if (shader == null)
        {
            Debug.LogError($"[CyberPixel] Shader '{ShaderName}' not found — did CyberPixelRim.shader fail to compile?");
            return;
        }
        Directory.CreateDirectory(GenFolder);

        var map = LoadMap();
        var cloneByOriginal = new Dictionary<Material, Material>();
        // Rebuild cache from a previous run so re-converting reuses clones
        foreach (var pair in map.pairs)
        {
            var orig = AssetDatabase.LoadAssetAtPath<Material>(pair.originalPath);
            var clone = AssetDatabase.LoadAssetAtPath<Material>(pair.clonePath);
            if (orig != null && clone != null) cloneByOriginal[orig] = clone;
        }

        int rendererCount = 0, materialCount = 0, skipped = 0;
        Scene scene = SceneManager.GetActiveScene();
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                if (r.GetComponentInParent<Canvas>() != null) continue;

                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var mat = mats[i];
                    if (mat == null || mat.shader == null) continue;
                    if (mat.shader.name == ShaderName) continue;

                    if (!cloneByOriginal.TryGetValue(mat, out var clone))
                    {
                        if (!IsConvertible(mat)) { skipped++; continue; }
                        clone = MakeClone(mat, shader, map);
                        cloneByOriginal[mat] = clone;
                    }
                    mats[i] = clone;
                    changed = true;
                    materialCount++;
                }
                if (changed)
                {
                    Undo.RecordObject(r, "CyberPixel Convert");
                    r.sharedMaterials = mats;
                    rendererCount++;
                }
            }
        }

        SaveMap(map);
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"[CyberPixel] Converted {materialCount} material slots on {rendererCount} renderers " +
                  $"({cloneByOriginal.Count} unique materials, {skipped} slots skipped as non-Lit/VFX). " +
                  "Save the scene (Ctrl+S) to keep it. Revert any time via Tools > CyberPixel > Revert Open Scene.");
    }

    [MenuItem("Tools/CyberPixel/Revert Open Scene")]
    static void RevertOpenScene()
    {
        var map = LoadMap();
        var originalByClone = new Dictionary<Material, Material>();
        foreach (var pair in map.pairs)
        {
            var clone = AssetDatabase.LoadAssetAtPath<Material>(pair.clonePath);
            var orig = AssetDatabase.LoadAssetAtPath<Material>(pair.originalPath);
            if (clone != null && orig != null) originalByClone[clone] = orig;
        }
        if (originalByClone.Count == 0)
        {
            Debug.LogWarning("[CyberPixel] No conversion map found — nothing to revert.");
            return;
        }

        int restored = 0;
        Scene scene = SceneManager.GetActiveScene();
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] != null && originalByClone.TryGetValue(mats[i], out var orig))
                    {
                        mats[i] = orig;
                        changed = true;
                        restored++;
                    }
                }
                if (changed)
                {
                    Undo.RecordObject(r, "CyberPixel Revert");
                    r.sharedMaterials = mats;
                }
            }
        }
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"[CyberPixel] Restored {restored} material slots to originals. Save the scene to keep it.");
    }

    static bool IsConvertible(Material mat)
    {
        string path = AssetDatabase.GetAssetPath(mat);
        if (string.IsNullOrEmpty(path)) return false; // scene-instance material, can't map it back safely
        string shaderName = mat.shader.name;
        foreach (var ok in ConvertibleShaderNames)
            if (shaderName == ok) return true;
        return false;
    }

    static Material MakeClone(Material original, Shader shader, ConversionMap map)
    {
        var clone = new Material(shader) { name = original.name + "_CyberPixel" };

        // Base texture + color from either URP or legacy property names
        Texture baseTex = null;
        if (original.HasProperty("_BaseMap")) baseTex = original.GetTexture("_BaseMap");
        if (baseTex == null && original.HasProperty("_MainTex")) baseTex = original.GetTexture("_MainTex");
        if (baseTex != null) clone.SetTexture("_BaseMap", baseTex);

        if (original.HasProperty("_BaseColor")) clone.SetColor("_BaseColor", original.GetColor("_BaseColor"));
        else if (original.HasProperty("_Color")) clone.SetColor("_BaseColor", original.GetColor("_Color"));

        if (original.HasProperty("_BaseMap")) clone.SetTextureScale("_BaseMap", original.GetTextureScale("_BaseMap"));
        else if (original.HasProperty("_MainTex")) clone.SetTextureScale("_BaseMap", original.GetTextureScale("_MainTex"));

        if (original.IsKeywordEnabled("_EMISSION") && original.HasProperty("_EmissionColor"))
            clone.SetColor("_EmissionColor", original.GetColor("_EmissionColor"));

        // Alpha-clipped originals (foliage etc.) keep their cutout
        if (original.HasProperty("_AlphaClip") && original.GetFloat("_AlphaClip") > 0.5f)
        {
            clone.SetFloat("_AlphaTest", 1f);
            clone.EnableKeyword("_ALPHATEST_ON");
            if (original.HasProperty("_Cutoff")) clone.SetFloat("_Cutoff", original.GetFloat("_Cutoff"));
        }

        string clonePath = AssetDatabase.GenerateUniqueAssetPath($"{GenFolder}/{clone.name}.mat");
        AssetDatabase.CreateAsset(clone, clonePath);
        map.pairs.Add(new MaterialPair
        {
            clonePath = clonePath,
            originalPath = AssetDatabase.GetAssetPath(original)
        });
        return clone;
    }

    static ConversionMap LoadMap()
    {
        if (!File.Exists(MapPath)) return new ConversionMap();
        try { return JsonUtility.FromJson<ConversionMap>(File.ReadAllText(MapPath)) ?? new ConversionMap(); }
        catch { return new ConversionMap(); }
    }

    static void SaveMap(ConversionMap map)
    {
        File.WriteAllText(MapPath, JsonUtility.ToJson(map, true));
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }
}
