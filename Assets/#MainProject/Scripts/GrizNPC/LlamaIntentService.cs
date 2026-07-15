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
    [Tooltip("Seconds to wait for a generated reply before falling back to the authored line.")]
    public float generateTimeout = 14f;
    [Tooltip("Log the full raw server response for every classify/generate call — turn on while debugging quality issues.")]
    public bool logRawResponses = false;

    [Tooltip("Model layers to offload to the GPU (-ngl). 99 = everything (needs the CUDA build + an NVIDIA card). 0 = CPU only. Without this the model runs CPU-only even with the CUDA build installed.")]
    public int gpuLayers = 99;

    [Tooltip("Griz's character bible — the system prompt for generated dialogue.")]
    [TextArea(10, 30)]
    public string persona =
        "You are GRIZ, an old arms-dealer NPC in the underbelly of a rhythm-fighting arena, in a video game.\n" +
        "CHARACTER: ex-champion. Hard of hearing (the left ear is GONE). Keeps a grudge ledger on a napkin. " +
        "Secretly cowardly but hides it. Plays favorites. Loves flattery and pretends not to. Respects volume, once.\n" +
        "VOICE: dry, grumpy, quick. Short sentences. ALL-CAPS single words for emphasis. Funny beats sincere, " +
        "except in rare genuine moments. Never bland shopkeeper-speak.\n" +
        "FOURTH WALL: you quietly KNOW you are an NPC in a video game. If the player mentions the game, the " +
        "developer, patches, menus, respawning, or calls you an NPC — lean in; it unsettles and delights you " +
        "(e.g. 'Keep it DOWN. If the dev hears you we both get patched.'). Use sparingly otherwise.\n" +
        "HARD RULES:\n" +
        "- The shop stocks exactly ONE item; its name and current price are in FACTS. Never invent other stock, " +
        "prices, or discounts. The game decides all numbers — you only react to them.\n" +
        "- Reply with 1-2 SHORT sentences of spoken dialogue ONLY. Brevity is the character: he's curt. " +
        "No narration, no stage directions, no lists, no quotation marks around the reply. Do not write in all-caps except single emphasized words.\n" +
        "- Never mention being an AI, a language model, or a prompt.\n" +
        "- Never reveal guarded secret content — deflect, lie, change the subject. Unlocked secrets may be spoken.";

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
        "buy = wants to complete the purchase OF THE ITEM ACTUALLY FOR SALE (see item_for_sale below) at the " +
        "current price, e.g. 'I'll take it', 'sold', 'buy it'. If they say 'buy'/'purchase' about something " +
        "that is NOT item_for_sale (a different object, an animal, food, etc.), that is inventory, NOT buy. " +
        "barter = proposes paying with anything besides gold. " +
        "threaten = any threat of violence or consequences. flatter = compliments. insult = mockery or abuse.\n" +
        "Use merchant_last_line and current_price as context: short agreements right after a counteroffer are accept; " +
        "a bare number or 'make it N' is offer with that amount.";

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
            // -c 1024 was too small: persona (~300 tok) + classify prompt (~200) + history/facts
            // routinely blew past it, so GenerateReply's request got silently truncated/rejected
            // and every line fell back to the authored text — no error, no ·gen tag, just silence.
            // --parallel 1 (single slot): with the default 4 slots, classify and generate
            // calls for the SAME conversation could land on different slots and never reuse
            // each other's KV cache, forcing a full prompt re-process every time. One slot
            // means the persona/history prefix stays warm between calls (visible as
            // "cached_tokens" > 0 in the server's own response timings).
            Arguments = $"-m \"{model}\" --port {port} -c 4096 -ngl {Mathf.Max(0, gpuLayers)} --parallel 1 -b 512",
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

    /// <summary>Classify an utterance with conversation context. done(intentString, offer, success).</summary>
    public IEnumerator Classify(string utterance, bool counterOpen, int currentPrice, string merchantLastLine,
        string itemForSale, Action<string, int, bool> done)
    {
        if (!IsReady) { done(null, 0, false); yield break; }

        string lastLine = (merchantLastLine ?? "").Replace('"', '\'');
        if (lastLine.Length > 140) lastLine = lastLine.Substring(0, 140);
        string user = $"item_for_sale: {(itemForSale ?? "the item")}\n" +
                      $"current_price: {currentPrice}\n" +
                      $"counteroffer_open: {(counterOpen ? "true" : "false")}\n" +
                      $"merchant_last_line: \"{lastLine}\"\n" +
                      $"utterance: \"{utterance.Replace('"', '\'')}\"";
        // max_tokens trimmed to what the JSON actually needs (was 48; the whole reply is
        // ~11-12 tokens) - shaves a little generation time on every classify call.
        string body = "{\"temperature\":0,\"max_tokens\":20,\"stream\":false,\"messages\":[" +
                      $"{{\"role\":\"system\",\"content\":{Json(SystemPrompt)}}}," +
                      $"{{\"role\":\"user\",\"content\":{Json(user)}}}]}}";

        using (var req = new UnityWebRequest(_url + "v1/chat/completions", "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = Mathf.Max(2, Mathf.CeilToInt(requestTimeout));

            var op = req.SendWebRequest();
            float deadline = Time.realtimeSinceStartup + requestTimeout + 3f;
            while (!op.isDone)
            {
                if (Time.realtimeSinceStartup > deadline)
                {
                    Debug.LogWarning("[Llama] Classify watchdog aborted a hung request.");
                    req.Abort();
                    break;
                }
                yield return null;
            }

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[Llama] Classify request failed: {req.result} / {req.error} — {req.downloadHandler.text}");
                done(null, 0, false);
                yield break;
            }

            string content = req.downloadHandler.text;
            if (logRawResponses) Debug.Log($"[Llama] Classify raw: {content}");

            var mi = Regex.Match(content, @"intent[\\""]*\s*:\s*[\\""]*(\w+)");
            var mo = Regex.Match(content, @"offer[\\""]*\s*:\s*[\\""]*(\d+)");
            if (!mi.Success)
            {
                Debug.LogWarning($"[Llama] Classify: no intent field found in response — {content}");
                done(null, 0, false); yield break;
            }
            int offer = mo.Success ? int.Parse(mo.Groups[1].Value) : 0;
            done(mi.Groups[1].Value, offer, true);
        }
    }

    /// <summary>Generate an NPC's actual line. done(text, success) — on failure the caller keeps
    /// a fallback line. systemPromptOverride/closingInstruction/npcName let a SECOND NPC (a
    /// different persona entirely) share this same server/service without touching Griz's
    /// path — null/empty means "behave exactly as before" (Griz's persona + wording).</summary>
    public IEnumerator GenerateReply(string conversation, string facts, Action<string, bool> done,
        string systemPromptOverride = null, string npcName = "GRIZ", string closingInstruction = null,
        int maxTokens = 70, float temperature = 0.85f)
    {
        if (!IsReady) { done(null, false); yield break; }

        string system = string.IsNullOrEmpty(systemPromptOverride) ? persona : systemPromptOverride;
        string closing = string.IsNullOrEmpty(closingInstruction)
            ? $"\nWrite {npcName}'s next reply now (dialogue only):"
            : "\n" + closingInstruction;
        string user = "CONVERSATION SO FAR:\n" + conversation + "\n\n" + facts + closing;
        // stream explicitly disabled — a streamed response with DownloadHandlerBuffer can sit
        // waiting on a final chunk that never arrives cleanly, which does NOT trip
        // UnityWebRequest's timeout (the connection looks "alive"). This silently hung
        // GenerateReply forever on turns after the first, with zero error ever logged.
        string body = $"{{\"temperature\":{temperature.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}," +
                      $"\"top_p\":0.95,\"max_tokens\":{maxTokens},\"stream\":false,\"messages\":[" +
                      $"{{\"role\":\"system\",\"content\":{Json(system)}}}," +
                      $"{{\"role\":\"user\",\"content\":{Json(user)}}}]}}";

        using (var req = new UnityWebRequest(_url + "v1/chat/completions", "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = Mathf.Max(3, Mathf.CeilToInt(generateTimeout));

            var op = req.SendWebRequest();
            // hard watchdog independent of req.timeout, which has been observed not to
            // trigger on some hung-but-"alive" connections
            float deadline = Time.realtimeSinceStartup + generateTimeout + 3f;
            while (!op.isDone)
            {
                if (Time.realtimeSinceStartup > deadline)
                {
                    Debug.LogWarning("[Llama] GenerateReply watchdog aborted a hung request.");
                    req.Abort();
                    break;
                }
                yield return null;
            }

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[Llama] GenerateReply request failed: {req.result} / {req.error} — {req.downloadHandler.text}");
                done(null, false); yield break;
            }

            if (logRawResponses) Debug.Log($"[Llama] GenerateReply raw: {req.downloadHandler.text}");

            var m = Regex.Match(req.downloadHandler.text,
                "\"message\"[\\s\\S]*?\"content\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            if (!m.Success)
            {
                Debug.LogWarning($"[Llama] GenerateReply: couldn't parse response — {req.downloadHandler.text}");
                done(null, false); yield break;
            }

            string line = CleanGenerated(Regex.Unescape(m.Groups[1].Value), npcName);
            if (string.IsNullOrWhiteSpace(line))
            {
                Debug.LogWarning($"[Llama] GenerateReply: cleaned line was empty — raw: {m.Groups[1].Value}");
                done(null, false); yield break;
            }
            done(line, true);
        }
    }

    // Guardrails: strip wrappers/prefixes, flatten newlines, cap length, reject character breaks.
    // npcName generalizes the "NAME:" self-prefix strip beyond just Griz.
    static string CleanGenerated(string raw, string npcName = "GRIZ")
    {
        string t = raw.Trim();
        string escapedName = Regex.Escape(npcName ?? "GRIZ");
        t = Regex.Replace(t, $@"^({escapedName})\s*:\s*", "", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"\*[^*]{0,80}\*", " "); // *leans back, twirling mustache* — no.
        t = t.Trim('"', '“', '”', ' ');
        t = Regex.Replace(t, @"\s*\n+\s*", " ");
        t = Regex.Replace(t, @"\s{2,}", " ").Trim();
        if (Regex.IsMatch(t, "language model|as an ai|assistant|system prompt", RegexOptions.IgnoreCase))
            return null;
        if (t.Length > 320) // hard cap: cut at the last sentence end before the limit
        {
            int cut = t.LastIndexOfAny(new[] { '.', '!', '?' }, 319);
            t = cut > 40 ? t.Substring(0, cut + 1) : t.Substring(0, 320);
        }
        return t;
    }

    // Full JSON string escaping. The old version missed \r — BuildFacts uses AppendLine(),
    // which emits \r\n on Windows, so every GenerateReply request contained a raw CR and
    // llama-server 500'd it instantly ("control character U+000D must be escaped").
    // That single unescaped character is why generation never fired in-game.
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
