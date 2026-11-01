using System.IO;
using UnityEditor;
using UnityEngine;

namespace OfflineAINPC.EditorTools
{
    /// <summary>
    /// Guides the user through installing llama.cpp and a model, and verifies the result.
    ///
    /// Deliberately does NOT bundle binaries or weights. Two reasons: the package stays a few
    /// hundred KB instead of several GB, and redistribution licensing stays the user's own
    /// decision — model licences vary wildly and several forbid commercial use.
    /// </summary>
    public class SetupWizard : EditorWindow
    {
        const string LlamaReleases = "https://github.com/ggml-org/llama.cpp/releases";
        const string ModelSuggestion = "https://huggingface.co/models?library=gguf&sort=trending";

        string _subfolder = "llama";
        Vector2 _scroll;

        [MenuItem("Tools/Offline AI NPC/Setup Wizard", priority = 1)]
        public static void Open()
        {
            var w = GetWindow<SetupWizard>(true, "Offline AI NPC — Setup", true);
            w.minSize = new Vector2(540, 560);
        }

        void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.LabelField("Offline AI NPC — Setup", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Your NPCs run on a local LLM, so there is nothing to sign up for and no " +
                "per-user cost. Two files are needed: the llama.cpp server and a model.",
                EditorStyles.wordWrappedLabel);

            EditorGUILayout.Space(8);
            _subfolder = EditorGUILayout.TextField("StreamingAssets subfolder", _subfolder);

            string dir = Path.GetFullPath(Path.Combine(Application.streamingAssetsPath, _subfolder));
            bool hasExe = File.Exists(Path.Combine(dir, "llama-server.exe"));
            string model = null;
            if (Directory.Exists(dir))
                foreach (var f in Directory.GetFiles(dir, "*.gguf")) { model = f; break; }

            EditorGUILayout.Space(10);
            DrawStatus(hasExe, model, dir);

            EditorGUILayout.Space(14);
            EditorGUILayout.LabelField("Step 1 — the server", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Download a llama.cpp release build for your platform and put llama-server.exe " +
                "(with its DLLs) in the folder below. Pick a CUDA build for NVIDIA GPUs, " +
                "otherwise the CPU build.",
                EditorStyles.wordWrappedLabel);
            if (GUILayout.Button("Open llama.cpp releases")) Application.OpenURL(LlamaReleases);

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Step 2 — a model", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Put one .gguf model in the same folder. A 7-8B model at Q4_K_M is a good " +
                "starting point: strong enough for reliable intent classification, small enough " +
                "to ship. 3B models work for classification but phrase dialogue poorly.\n\n" +
                "Check the model's licence before shipping commercially — they differ, and some " +
                "forbid it outright.",
                EditorStyles.wordWrappedLabel);
            if (GUILayout.Button("Browse GGUF models on Hugging Face")) Application.OpenURL(ModelSuggestion);

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Step 3 — verify", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(!Directory.Exists(dir)))
            {
                if (GUILayout.Button("Reveal folder")) EditorUtility.RevealInFinder(dir);
            }
            if (!Directory.Exists(dir) && GUILayout.Button("Create folder"))
            {
                Directory.CreateDirectory(dir);
                AssetDatabase.Refresh();
            }

            EditorGUILayout.Space(14);
            EditorGUILayout.HelpBox(
                "The package works without any of this — NPCs fall back to keyword matching, so " +
                "your game still builds and runs. Install the model when you want real language " +
                "understanding.",
                MessageType.Info);

            EditorGUILayout.Space(6);
            EditorGUILayout.HelpBox(
                "Shipping: StreamingAssets contents are copied into your build, so the server and " +
                "model travel with the game. Budget for the model's size (a 7B Q4_K_M is ~4.5 GB) " +
                "and confirm its licence permits redistribution.",
                MessageType.Warning);

            EditorGUILayout.EndScrollView();
        }

        void DrawStatus(bool hasExe, string model, string dir)
        {
            EditorGUILayout.LabelField("Status", EditorStyles.boldLabel);

            if (!Directory.Exists(dir))
            {
                EditorGUILayout.HelpBox("Folder does not exist yet.", MessageType.Warning);
                return;
            }

            EditorGUILayout.LabelField(hasExe
                ? "✔  llama-server.exe found"
                : "✘  llama-server.exe missing");

            if (model != null)
            {
                var info = new FileInfo(model);
                EditorGUILayout.LabelField(
                    $"✔  model: {Path.GetFileName(model)} ({info.Length / 1048576f / 1024f:0.0} GB)");
            }
            else
            {
                EditorGUILayout.LabelField("✘  no .gguf model found");
            }

            if (hasExe && model != null)
                EditorGUILayout.HelpBox("Ready. Add a Local LLM Service component and press Play.",
                                        MessageType.Info);
        }
    }
}
