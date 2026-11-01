using System.IO;
using OfflineAINPC.Demo;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OfflineAINPC.EditorTools
{
    /// <summary>
    /// Builds the demo scene from code rather than shipping a .unity file.
    ///
    /// Two reasons: a generated scene cannot arrive with broken GUID references after an
    /// import, and the build code doubles as a readable, copy-pasteable example of exactly
    /// how the components wire together — which a binary scene file never is.
    /// </summary>
    public static class DemoSceneBuilder
    {
        const string SceneDir = "Assets/OfflineAINPC/Demo";
        const string ScenePath = SceneDir + "/MerchantDemo.unity";
        const string IntentsPath = SceneDir + "/MerchantIntents.asset";

        [MenuItem("Tools/Offline AI NPC/Create Demo Scene", priority = 20)]
        static void CreateScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects,
                                                    NewSceneMode.Single);

            // One service for the scene. Several NPCs can share it — one model in memory.
            var serviceGO = new GameObject("Local LLM Service");
            var llm = serviceGO.AddComponent<LocalLlmService>();

            // The intent set ships with the package; load it rather than generating one, so
            // editing the asset is reflected in the demo.
            var intents = AssetDatabase.LoadAssetAtPath<NpcIntentSet>(IntentsPath);
            if (intents == null)
                Debug.LogWarning($"[OfflineAINPC] Intent set not found at {IntentsPath}. " +
                                 "Assign one on the Merchant before pressing Play.");

            var merchantGO = new GameObject("Merchant");
            var merchant = merchantGO.AddComponent<DemoMerchant>();
            merchant.llm = llm;
            merchant.intentSet = intents;
            merchant.npcName = "GRIZ";
            merchant.itemName = "iron sword";
            merchant.askingPrice = 100;
            merchant.minPrice = 60;
            merchant.playerGold = 80;

            // Off by default: the demo should be deterministic and instant on first run.
            // Turning this on is the "now make it feel alive" step.
            merchant.useLlmPhrasing = false;
            merchant.persona =
                "You are Griz, a gruff one-eared weapon merchant. You speak in 1-2 short, " +
                "clipped sentences. You are not friendly, but you are fair. Never mention " +
                "being an AI. Never invent prices, items, or discounts — state only what you " +
                "are told.";

            var consoleGO = new GameObject("Demo Console");
            var console = consoleGO.AddComponent<DemoConsole>();
            console.npc = merchant;
            console.llm = llm;

            if (!Directory.Exists(SceneDir)) Directory.CreateDirectory(SceneDir);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();

            Debug.Log($"[OfflineAINPC] Demo scene saved to {ScenePath}.\n" +
                      "Press Play and type at the merchant. With no model installed it uses the " +
                      "keyword fallback — try \"hello\", \"that's too much\", \"I'll give you 70 gold\", " +
                      "\"40 gold or else\", \"I'll take it\".");

            EditorGUIUtility.PingObject(merchantGO);
        }
    }
}
