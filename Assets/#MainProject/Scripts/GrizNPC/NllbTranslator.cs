using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

// 4th local subprocess in the stack (alongside Vosk in-process, whisper-server.exe,
// llama-server.exe, piper.exe): a dedicated ENGLISH<->BANGLA translation model
// (NLLB-200, run via ctranslate2 + a small Python/Flask server), used ONLY for the
// NPC's Bangla REPLY text.
//
// Why this exists: Whisper's --translate mode (Bangla speech -> English text) is
// solid for understanding the PLAYER. But asking the chat LLM (Qwen) to WRITE its
// reply directly in Bangla produces broken, ungrammatical Bangla — it's a
// low-resource language in its training data. NLLB is a model built SPECIFICALLY
// for translation and is dramatically better at it. So the flow becomes:
//   Player speaks Bangla -> Whisper translates to English -> Qwen reasons + writes
//   reply in ENGLISH (its actual strength) -> NLLB translates that English line to
//   real Bangla -> Piper speaks the Bangla.
// English reasoning in, English wit, Bangla only at the very last step.
//
// Setup (separate from the game's C#/C++ subprocess stack — this one is Python):
//   1. python -m pip install ctranslate2 transformers==4.40.2 ctranslate2==4.4.0 sentencepiece flask
//      (version pins matter — newer ctranslate2 + older transformers, or vice versa,
//      hit a `dtype` kwarg incompatibility in the HF conversion step. Confirmed 2026-07-16.)
//   2. python -m ctranslate2.converters.transformers --model facebook/nllb-200-distilled-600M
//      --output_dir nllb-600m-ct2 --quantization int8   (~1.5GB download, ~620MB output)
//   3. Point pythonExe / serverScript / modelDir at that setup below.
// This whole project lives in nllb-tools/ at the repo root — NOT inside Assets/ (Python
// venvs, HF model caches, and training artifacts have no business in Unity's asset
// pipeline or in a Unity-focused git history).
public class NllbTranslator : MonoBehaviour
{
    [Header("Python + server location (outside Assets/ — see nllb-tools/ at repo root)")]
    [Tooltip("Full path to python.exe (or just \"python\" if it's on PATH).")]
    public string pythonExe = "python";
    [Tooltip("Full path to translate_server.py.")]
    public string serverScript = "";
    [Tooltip("Full path to the converted ctranslate2 model directory (nllb-600m-ct2).")]
    public string modelDir = "";
    public int port = 8733;
    [Tooltip("cpu is fine for single-line NPC replies (sub-second). Set cuda only if you " +
             "also verify your GPU isn't already saturated by the 7B llama + Whisper.")]
    public string device = "cpu";

    public float requestTimeout = 12f;
    public bool logRawResponses = false;

    public bool IsReady { get; private set; }

    System.Diagnostics.Process _proc;
    string _url;

    void Awake()
    {
        // Sensible defaults relative to the project root, so a fresh machine mostly just
        // works once nllb-tools/ exists — override in the Inspector if paths differ.
        if (string.IsNullOrEmpty(serverScript))
            serverScript = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "nllb-tools", "translate_server.py");
        if (string.IsNullOrEmpty(modelDir))
            modelDir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "nllb-tools", "nllb-600m-ct2");
    }

    void Start()
    {
        if (!File.Exists(serverScript))
        {
            Debug.Log($"[NLLB] server script not found at '{serverScript}' — Bangla replies will fall back " +
                      "to whatever the LLM writes directly (not recommended, see NllbTranslator.cs header).");
            return;
        }
        if (!Directory.Exists(modelDir))
        {
            Debug.Log($"[NLLB] model dir not found at '{modelDir}' — run the conversion step in " +
                      "nllb-tools/ first (see NllbTranslator.cs header for the exact command).");
            return;
        }

        _url = $"http://127.0.0.1:{port}/";
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = pythonExe,
            Arguments = $"\"{serverScript}\" --port {port} --model_dir \"{modelDir}\" --device {device}",
            WorkingDirectory = Path.GetDirectoryName(serverScript),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        try { _proc = System.Diagnostics.Process.Start(psi); }
        catch (Exception e)
        {
            Debug.LogWarning($"[NLLB] failed to start translate_server.py: {e.Message}");
            return;
        }
        StartCoroutine(WaitUntilReady());
    }

    IEnumerator WaitUntilReady()
    {
        // The Python process loads torch + the model on first start — slower than the
        // C++ subprocesses (llama/whisper/piper), give it real time before giving up.
        float deadline = Time.realtimeSinceStartup + 60f;
        while (Time.realtimeSinceStartup < deadline)
        {
            if (_proc != null && _proc.HasExited)
            {
                Debug.LogWarning("[NLLB] translate_server.py exited before becoming ready — check it runs standalone first.");
                yield break;
            }
            using (var req = UnityWebRequest.Get(_url + "health"))
            {
                req.timeout = 2;
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success)
                {
                    IsReady = true;
                    Debug.Log("[NLLB] ✅ ready — Bangla replies will be translated by NLLB, not written directly by the LLM.");
                    yield break;
                }
            }
            yield return new WaitForSeconds(1f);
        }
        Debug.LogWarning("[NLLB] server never became ready in 60s — Bangla replies will fall back to raw LLM output.");
    }

    /// <summary>Translate one line of text. done(translatedText, success).</summary>
    public IEnumerator Translate(string text, string targetLang, string sourceLang, Action<string, bool> done)
    {
        if (!IsReady || string.IsNullOrWhiteSpace(text)) { done(null, false); yield break; }

        string body = $"{{\"text\":{Json(text)},\"target_lang\":{Json(targetLang)},\"source_lang\":{Json(sourceLang)}}}";
        using (var req = new UnityWebRequest(_url + "translate", "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json; charset=utf-8");
            req.timeout = Mathf.Max(3, Mathf.CeilToInt(requestTimeout));

            var op = req.SendWebRequest();
            float deadline = Time.realtimeSinceStartup + requestTimeout + 3f;
            while (!op.isDone)
            {
                if (Time.realtimeSinceStartup > deadline)
                {
                    Debug.LogWarning("[NLLB] Translate watchdog aborted a hung request.");
                    req.Abort();
                    break;
                }
                yield return null;
            }

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[NLLB] Translate request failed: {req.result} / {req.error}");
                done(null, false); yield break;
            }

            if (logRawResponses) Debug.Log($"[NLLB] raw: {req.downloadHandler.text}");

            var m = System.Text.RegularExpressions.Regex.Match(
                req.downloadHandler.text, "\"translation\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            if (!m.Success)
            {
                Debug.LogWarning($"[NLLB] couldn't parse response: {req.downloadHandler.text}");
                done(null, false); yield break;
            }
            done(System.Text.RegularExpressions.Regex.Unescape(m.Groups[1].Value), true);
        }
    }

    // Convenience wrappers for this project's two directions.
    public IEnumerator ToBangla(string englishText, Action<string, bool> done) =>
        Translate(englishText, "ben_Beng", "eng_Latn", done);
    public IEnumerator ToEnglish(string banglaText, Action<string, bool> done) =>
        Translate(banglaText, "eng_Latn", "ben_Beng", done);

    static string Json(string s)
    {
        var sb = new StringBuilder(s.Length + 16);
        sb.Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    void OnDestroy() { Kill(); }
    void OnApplicationQuit() { Kill(); }

    void Kill()
    {
        try { if (_proc != null && !_proc.HasExited) _proc.Kill(); } catch { }
        _proc = null;
        IsReady = false;
    }
}
