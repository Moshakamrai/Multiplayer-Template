using System;
using System.Collections;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

namespace OfflineAINPC
{
    /// <summary>Result of a classification call.</summary>
    public struct NpcIntentResult
    {
        /// <summary>Intent id from your NpcIntentSet. Never a value outside that set.</summary>
        public string intent;
        /// <summary>Number extracted, when the set enables it. 0 otherwise.</summary>
        public int amount;
        /// <summary>True if an LLM produced this; false if it came from the keyword fallback.</summary>
        public bool fromLlm;
    }

    /// <summary>
    /// Runs a local llama.cpp server as a child process and talks to it over localhost.
    /// No cloud, no API key, no per-user cost — the model ships with your game.
    ///
    /// Design: the model is used to UNDERSTAND the player (Classify), and optionally to
    /// phrase a line (GenerateReply) within limits you set. It is never the authority on
    /// what happens; your own code is. See NpcIntentSet for the reasoning.
    ///
    /// Always safe to leave in a scene. If the binary or model is missing, IsReady stays
    /// false, Classify silently uses the keyword fallback, and GenerateReply reports
    /// failure so you can use an authored line. Nothing throws.
    /// </summary>
    [AddComponentMenu("Offline AI NPC/Local LLM Service")]
    public class LocalLlmService : MonoBehaviour
    {
        [Header("Server")]
        [Tooltip("Localhost port for llama-server. Change only if something else uses it.")]
        public int port = 8731;

        [Tooltip("Folder under StreamingAssets holding llama-server.exe and one .gguf model. " +
                 "Populate it with Tools > Offline AI NPC > Setup Wizard.")]
        public string streamingAssetsSubfolder = "llama";

        [Tooltip("Layers to offload to GPU. 0 = CPU only (slowest, works everywhere). " +
                 "20-35 suits most 7B models on a mid-range GPU. Too high fails to allocate.")]
        public int gpuLayers = 20;

        [Tooltip("Context window in tokens. Must comfortably exceed persona + prompt + history: " +
                 "if it overflows, requests are silently truncated and every line falls back " +
                 "with no error shown.")]
        public int contextSize = 4096;

        [Header("Timeouts (seconds)")]
        [Tooltip("Classification budget before falling back to keywords. Keep tight — this is " +
                 "in the player's conversational path.")]
        public float classifyTimeout = 6f;

        [Tooltip("Reply-generation budget before using your authored line.")]
        public float generateTimeout = 14f;

        [Tooltip("How long to wait for the server to finish loading the model at startup. " +
                 "Large models on a cold disk genuinely can take over a minute.")]
        public float startupTimeout = 90f;

        [Header("Debugging")]
        [Tooltip("Log every raw server response. Turn on when classification quality looks wrong " +
                 "— it is usually the prompt, and this shows exactly what came back.")]
        public bool logRawResponses;

        /// <summary>True once the server answers /health. False means fallbacks are in use.</summary>
        public bool IsReady { get; private set; }

        /// <summary>Why the service is unavailable, for display in your own setup UI.</summary>
        public string StatusMessage { get; private set; } = "Not started.";

        System.Diagnostics.Process _proc;
        string _url;

        void Start() { TryStartServer(); }

        void TryStartServer()
        {
            // Path.GetFullPath normalises separators. Without it, streamingAssetsPath's forward
            // slashes combined with Path.Combine's backslashes produce a MIXED path such as
            // ".../StreamingAssets\llama\llama-server.exe". The Editor tolerates it; a Windows
            // standalone build's Process.Start (UseShellExecute = false) can silently fail to
            // launch, reporting the useless "Native error= Success" with nothing in Task Manager.
            string dir = Path.GetFullPath(Path.Combine(Application.streamingAssetsPath, streamingAssetsSubfolder));
            string exe = Path.Combine(dir, "llama-server.exe");

            string model = null;
            if (Directory.Exists(dir))
            {
                foreach (var f in Directory.GetFiles(dir, "*.gguf")) { model = f; break; }
            }

            if (!File.Exists(exe))
            {
                StatusMessage = $"llama-server.exe not found in StreamingAssets/{streamingAssetsSubfolder}. " +
                                "Run Tools > Offline AI NPC > Setup Wizard. Keyword fallback in use.";
                Debug.Log("[OfflineAINPC] " + StatusMessage);
                return;
            }
            if (model == null)
            {
                StatusMessage = $"No .gguf model found in StreamingAssets/{streamingAssetsSubfolder}. " +
                                "Run Tools > Offline AI NPC > Setup Wizard. Keyword fallback in use.";
                Debug.Log("[OfflineAINPC] " + StatusMessage);
                return;
            }

            _url = $"http://127.0.0.1:{port}/";

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = exe,
                // --parallel 1 is deliberate. With llama-server's default 4 slots, the classify
                // and generate calls for one conversation can land on different slots and never
                // reuse each other's KV cache, forcing a full prompt re-process every turn. A
                // single slot keeps the persona/history prefix warm (visible as cached_tokens > 0
                // in the server's own timings) and is markedly faster in practice.
                Arguments = $"-m \"{model}\" --port {port} -c {Mathf.Max(512, contextSize)} " +
                            $"-ngl {Mathf.Max(0, gpuLayers)} --parallel 1 -b 512",
                WorkingDirectory = dir,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            try
            {
                _proc = System.Diagnostics.Process.Start(psi);
            }
            catch (Exception e)
            {
                StatusMessage = "Failed to start llama-server: " + e.Message;
                Debug.LogWarning("[OfflineAINPC] " + StatusMessage);
                return;
            }

            StatusMessage = "Loading model...";
            StartCoroutine(WaitUntilReady());
        }

        IEnumerator WaitUntilReady()
        {
            float deadline = Time.realtimeSinceStartup + startupTimeout;
            while (Time.realtimeSinceStartup < deadline)
            {
                using (var req = UnityWebRequest.Get(_url + "health"))
                {
                    req.timeout = 2;
                    yield return req.SendWebRequest();
                    if (req.result == UnityWebRequest.Result.Success)
                    {
                        IsReady = true;
                        StatusMessage = "Ready.";
                        Debug.Log("[OfflineAINPC] Local LLM ready.");
                        yield break;
                    }
                }
                yield return new WaitForSeconds(1f);
            }
            StatusMessage = "Server never became ready. Keyword fallback in use.";
            Debug.LogWarning("[OfflineAINPC] " + StatusMessage);
        }

        /// <summary>
        /// Works out what the player meant. Always calls back — on any failure it falls back to
        /// the intent set's keyword matcher, so you never need a null check on the result.
        /// </summary>
        /// <param name="utterance">What the player said or typed.</param>
        /// <param name="intents">The intent set defining valid answers.</param>
        /// <param name="done">Receives the result.</param>
        /// <param name="extraContext">
        /// Optional "key: value" lines giving the model state it needs to disambiguate — the
        /// current price, whether an offer is open, the item being discussed. Short agreements
        /// like "fine" are unclassifiable without this.
        /// </param>
        public IEnumerator Classify(string utterance, NpcIntentSet intents,
                                    Action<NpcIntentResult> done, string extraContext = null)
        {
            if (intents == null)
            {
                Debug.LogWarning("[OfflineAINPC] Classify called with no NpcIntentSet.");
                done(new NpcIntentResult { intent = null, amount = 0, fromLlm = false });
                yield break;
            }

            NpcIntentResult Fallback() => new NpcIntentResult
            {
                intent = intents.MatchKeywords(utterance),
                amount = intents.extractAmount ? NpcIntentSet.ExtractFirstNumber(utterance) : 0,
                fromLlm = false,
            };

            if (!IsReady || string.IsNullOrWhiteSpace(utterance)) { done(Fallback()); yield break; }

            var sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(extraContext)) sb.Append(extraContext.Trim()).Append('\n');
            sb.Append("utterance: \"").Append(utterance.Replace('"', '\'')).Append('"');

            // temperature 0: classification is not creative work, and determinism makes
            // player-reported bugs reproducible. max_tokens is sized to the JSON only.
            string body = "{\"temperature\":0,\"max_tokens\":24,\"stream\":false,\"messages\":[" +
                          $"{{\"role\":\"system\",\"content\":{Json(intents.BuildClassificationPrompt())}}}," +
                          $"{{\"role\":\"user\",\"content\":{Json(sb.ToString())}}}]}}";

            string response = null;
            yield return Post("v1/chat/completions", body, classifyTimeout, "Classify",
                              r => response = r);

            if (response == null) { done(Fallback()); yield break; }

            var mi = Regex.Match(response, @"intent[\\""]*\s*:\s*[\\""]*([A-Za-z_]+)");
            if (!mi.Success)
            {
                Debug.LogWarning("[OfflineAINPC] Classify: no intent in response, using fallback. Raw: " + response);
                done(Fallback()); yield break;
            }

            string id = mi.Groups[1].Value;
            // Reject anything outside the declared set. A model inventing an intent name is the
            // exact failure this architecture exists to prevent, so it must not reach game code.
            if (!intents.IsValidIntent(id))
            {
                Debug.LogWarning($"[OfflineAINPC] Classify: model returned unknown intent '{id}', using fallback.");
                done(Fallback()); yield break;
            }

            int amount = 0;
            if (intents.extractAmount)
            {
                var mo = Regex.Match(response, @"amount[\\""]*\s*:\s*[\\""]*(\d+)");
                if (mo.Success) int.TryParse(mo.Groups[1].Value, out amount);
                // Models sometimes classify the intent correctly but omit the number; the
                // utterance itself is a reliable backstop.
                if (amount == 0) amount = NpcIntentSet.ExtractFirstNumber(utterance);
            }

            done(new NpcIntentResult { intent = id, amount = amount, fromLlm = true });
        }

        /// <summary>
        /// Generates a line of dialogue. Optional: many games only need Classify and keep all
        /// text authored. Returns success = false whenever anything is off, so the caller can
        /// use its own line — never show the player a failure.
        /// </summary>
        /// <param name="persona">Who the character is, how they speak, and their hard rules.</param>
        /// <param name="context">Conversation so far plus facts the character may state.</param>
        /// <param name="done">(text, success)</param>
        /// <param name="speakerName">Used to strip a "NAME:" prefix the model may add.</param>
        public IEnumerator GenerateReply(string persona, string context, Action<string, bool> done,
                                         string speakerName = "NPC", int maxTokens = 70,
                                         float temperature = 0.85f)
        {
            if (!IsReady) { done(null, false); yield break; }

            // stream is explicitly false. A streamed response read with DownloadHandlerBuffer can
            // sit waiting on a final chunk that never arrives cleanly, and this does NOT trip
            // UnityWebRequest's timeout because the connection still looks alive — it hangs
            // forever with no error logged. The watchdog in Post() is the second line of defence.
            string body = $"{{\"temperature\":{temperature.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}," +
                          $"\"top_p\":0.95,\"max_tokens\":{Mathf.Max(8, maxTokens)},\"stream\":false,\"messages\":[" +
                          $"{{\"role\":\"system\",\"content\":{Json(persona ?? "")}}}," +
                          $"{{\"role\":\"user\",\"content\":{Json(context ?? "")}}}]}}";

            string response = null;
            yield return Post("v1/chat/completions", body, generateTimeout, "GenerateReply",
                              r => response = r);

            if (response == null) { done(null, false); yield break; }

            var m = Regex.Match(response, "\"message\"[\\s\\S]*?\"content\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            if (!m.Success)
            {
                Debug.LogWarning("[OfflineAINPC] GenerateReply: could not parse response. Raw: " + response);
                done(null, false); yield break;
            }

            string line = CleanGenerated(Regex.Unescape(m.Groups[1].Value), speakerName);
            if (string.IsNullOrWhiteSpace(line)) { done(null, false); yield break; }
            done(line, true);
        }

        /// <summary>
        /// Shared POST with a hard watchdog. req.timeout alone has been observed not to fire on
        /// hung-but-"alive" connections, so elapsed time is also checked every frame.
        /// Calls back with null on any failure.
        /// </summary>
        IEnumerator Post(string path, string body, float timeout, string label, Action<string> done)
        {
            using (var req = new UnityWebRequest(_url + path, "POST"))
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.timeout = Mathf.Max(2, Mathf.CeilToInt(timeout));

                var op = req.SendWebRequest();
                float deadline = Time.realtimeSinceStartup + timeout + 3f;
                while (!op.isDone)
                {
                    if (Time.realtimeSinceStartup > deadline)
                    {
                        Debug.LogWarning($"[OfflineAINPC] {label} watchdog aborted a hung request.");
                        req.Abort();
                        break;
                    }
                    yield return null;
                }

                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"[OfflineAINPC] {label} failed: {req.result} / {req.error}");
                    done(null);
                    yield break;
                }

                if (logRawResponses) Debug.Log($"[OfflineAINPC] {label} raw: {req.downloadHandler.text}");
                done(req.downloadHandler.text);
            }
        }

        /// <summary>
        /// Guardrails on generated text: strip self-prefixes and stage directions, flatten
        /// newlines, cap length at a sentence boundary, and reject lines that break character.
        /// Cheap insurance — a single "As an AI language model" shipped to a player is worse
        /// than any amount of fallback dialogue.
        /// </summary>
        public static string CleanGenerated(string raw, string speakerName = "NPC")
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            string t = raw.Trim();
            string escaped = Regex.Escape(speakerName ?? "NPC");
            t = Regex.Replace(t, $@"^({escaped})\s*:\s*", "", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"\*[^*]{0,80}\*", " ");   // *leans back, twirling moustache*
            t = t.Trim('"', '“', '”', ' ');
            t = Regex.Replace(t, @"\s*\n+\s*", " ");
            t = Regex.Replace(t, @"\s{2,}", " ").Trim();

            if (Regex.IsMatch(t, "language model|as an ai|i am an ai|assistant|system prompt",
                              RegexOptions.IgnoreCase))
                return null;

            if (t.Length > 320)
            {
                int cut = t.LastIndexOfAny(new[] { '.', '!', '?' }, 319);
                t = cut > 40 ? t.Substring(0, cut + 1) : t.Substring(0, 320);
            }
            return t;
        }

        /// <summary>
        /// Full JSON string escaping, including \r. This matters more than it looks: building
        /// context with StringBuilder.AppendLine() emits \r\n on Windows, and a raw CR makes
        /// llama-server reject the whole request ("control character U+000D must be escaped").
        /// One unescaped character silently disables generation everywhere.
        /// </summary>
        public static string Json(string s)
        {
            if (s == null) return "\"\"";
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

        /// <summary>
        /// Kills the child process. Without this the server survives leaving Play mode and holds
        /// the port, so the next run cannot bind and silently falls back forever.
        /// </summary>
        void Kill()
        {
            try { if (_proc != null && !_proc.HasExited) _proc.Kill(); } catch { }
            _proc = null;
            IsReady = false;
            StatusMessage = "Stopped.";
        }
    }
}
