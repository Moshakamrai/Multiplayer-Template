using System;
using System.Collections;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

// Local LLM "understanding layer" — no cloud, no subscriptions, shipped with the game.
// Same pattern as PiperVoice: llama.cpp's llama-server.exe (MIT) lives in
// StreamingAssets/llama/ next to a .gguf model and runs as a SEPARATE subprocess;
// Unity queries it over localhost. The LLM only CLASSIFIES what the player means
// (intent + offered amount) — GrizBrain's state machine and authored lines still
// decide what he says, so no hallucinated prices and no broken character.
//
// Setup: Tools > Griz > Check Llama Setup. Not installed → IsReady stays false and
// callers use the keyword classifier, so this is always safe to have in the scene.
public class LlamaIntentService : MonoBehaviour
{
    public int port = 8731;
    [Tooltip("Seconds to wait for a classification before falling back to keywords.")]
    public float requestTimeout = 6f;

    public bool IsReady { get; private set; }

    System.Diagnostics.Process _proc;
    string _url;

    const string SystemPrompt =
        "You classify one customer utterance from a fantasy weapon-shop negotiation.\n" +
        "Reply with ONLY one line of JSON, nothing else: {\"intent\":\"...\",\"offer\":0}\n" +
        "intent must be one of: greet, smalltalk, askinfo, backstory, inventory, haggle, offer, accept, buy, barter, flatter, beg, insult, threaten, unknown.\n" +
        "offer = the gold amount if the customer proposes a price, else 0.\n" +
        "Meanings: inventory = asking what is for sale or saying they want/need an item. " +
        "askinfo = asking about the item itself. backstory = asking about the merchant himself. " +
        "haggle = wants a lower price without naming an amount. offer = names an amount. " +
        "accept = agrees to the merchant's open counteroffer (only if counteroffer_open is true). " +
        "buy = wants to complete the purchase. barter = proposes paying with anything besides gold. " +
        "threaten = any threat of violence or consequences. flatter = compliments. insult = mockery or abuse.";

    void Start()
    {
        string dir = Path.Combine(Application.streamingAssetsPath, "llama");
        string exe = Path.Combine(dir, "llama-server.exe");
        string model = null;
        if (Directory.Exists(dir))
            foreach (var f in Directory.GetFiles(dir, "*.gguf")) { model = f; break; }

        if (!File.Exists(exe) || model == null)
        {
            Debug.Log("[Llama] not installed — keyword classifier in use. Tools > Griz > Check Llama Setup for instructions.");
            return;
        }

        _url = $"http://127.0.0.1:{port}/";
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = exe,
            Arguments = $"-m \"{model}\" --port {port} -c 1024",
            WorkingDirectory = dir,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        try { _proc = System.Diagnostics.Process.Start(psi); }
        catch (Exception e)
        {
            Debug.LogWarning($"[Llama] failed to start llama-server: {e.Message}");
            return;
        }
        StartCoroutine(WaitUntilReady());
    }

    IEnumerator WaitUntilReady()
    {
        float deadline = Time.realtimeSinceStartup + 90f;
        while (Time.realtimeSinceStartup < deadline)
        {
            using (var req = UnityWebRequest.Get(_url + "health"))
            {
                req.timeout = 2;
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success)
                {
                    IsReady = true;
                    Debug.Log("[Llama] ✅ ready — LLM understanding active.");
                    yield break;
                }
            }
            yield return new WaitForSeconds(1f);
        }
        Debug.LogWarning("[Llama] server never became ready — keyword classifier in use.");
    }

    /// <summary>Classify an utterance. done(intentString, offer, success).</summary>
    public IEnumerator Classify(string utterance, bool counterOpen, Action<string, int, bool> done)
    {
        if (!IsReady) { done(null, 0, false); yield break; }

        string user = $"counteroffer_open: {(counterOpen ? "true" : "false")}\nutterance: \"{utterance.Replace('"', '\'')}\"";
        string body = "{\"temperature\":0,\"max_tokens\":48,\"messages\":[" +
                      $"{{\"role\":\"system\",\"content\":{Json(SystemPrompt)}}}," +
                      $"{{\"role\":\"user\",\"content\":{Json(user)}}}]}}";

        using (var req = new UnityWebRequest(_url + "v1/chat/completions", "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = Mathf.Max(2, Mathf.CeilToInt(requestTimeout));
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                done(null, 0, false);
                yield break;
            }

            // The assistant's JSON sits escaped inside the API response — match loosely.
            string content = req.downloadHandler.text;
            var mi = Regex.Match(content, @"intent[\\""]*\s*:\s*[\\""]*(\w+)");
            var mo = Regex.Match(content, @"offer[\\""]*\s*:\s*[\\""]*(\d+)");
            if (!mi.Success) { done(null, 0, false); yield break; }
            int offer = mo.Success ? int.Parse(mo.Groups[1].Value) : 0;
            done(mi.Groups[1].Value, offer, true);
        }
    }

    static string Json(string s) =>
        "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";

    void OnDestroy() { Kill(); }
    void OnApplicationQuit() { Kill(); }

    void Kill()
    {
        try { if (_proc != null && !_proc.HasExited) _proc.Kill(); } catch { }
        _proc = null;
        IsReady = false;
    }
}
