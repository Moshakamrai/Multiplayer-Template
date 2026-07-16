using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

// Test harness for CompanionBrain — the OPEN-DOMAIN conversational NPC, sibling to
// GrizTestConsole but deliberately simpler: there is no negotiation state machine, no
// intent classification pass, no price math. Every reply is a single LlamaIntentService
// .GenerateReply() call against CompanionBrain's persona + a short FACTS block (mood,
// remembered player facts) — the model itself does almost all the work.
//
// This is what to run to demo "you can genuinely talk to this NPC about anything."
public class CompanionConsole : MonoBehaviour
{
    public VoskSpeechToText Vosk;
    public CompanionBrain Brain;
    public GrizVoice Voice;         // gibberish fallback if Piper isn't installed
    public PiperVoice Piper;
    public GrizAnimatorLink AnimLink;
    public LlamaIntentService Llm;  // SHARED service — same llama-server as Griz, different persona per call
    public WhisperTranscriber Whisper;
    public NllbTranslator Translator; // Bangla REPLY output only — Qwen always writes English

    bool _utteranceOpen;
    bool _bangla; // EN/BN toggle button state — Whisper does the actual language switch
    int _pendingRequests;
    readonly List<string> _history = new List<string>();

    [Tooltip("F1 toggles between the full debug console and cinematic mode (corner dialog box only).")]
    public bool cinematicMode = false;
    string _lastNpcLine = "";
    float _lastNpcLineTime = -99f;
    string _lastYouLine = "";
    float _lastYouLineTime = -99f;

    [Tooltip("Show the rapport/energy/memory debug panel.")]
    public bool showDebugState = true;

    [Tooltip("Your sentence only ENDS after this much real mic silence — keep talking and it all stays one message.")]
    public float silenceToSendSeconds = 1f;
    [Tooltip("Mic volume above this counts as still-speaking (keeps the sentence open).")]
    public float speakingVolume = 0.06f;
    [Tooltip("Voice utterances whose PEAK volume never exceeded this are dropped instead of sent — " +
             "breath/noise blips shipped to Whisper produce classic hallucinations (\"it\", \"the\", " +
             "multilingual token soup, especially on the large model).")]
    public float minSendPeak = 0.05f;
    string _pendingText = "";
    float _pendingVolume;
    float _lastVoiceActivity;

    readonly List<string> _log = new List<string>();
    string _typed = "";
    string _partial = "";
    float _peakVolume;
    bool _micStarted;
    Vector2 _scroll;

    void Awake()
    {
        if (Vosk == null) Vosk = FindObjectOfType<VoskSpeechToText>();
        if (Vosk != null) Vosk.FreeDictation = true;
    }

    void Start()
    {
        if (Brain == null) Brain = FindObjectOfType<CompanionBrain>();
        if (Vosk == null) Vosk = FindObjectOfType<VoskSpeechToText>();
        if (Voice == null) Voice = Brain != null ? Brain.gameObject.AddComponent<GrizVoice>() : null;
        if (Piper == null && Brain != null)
        {
            var go = new GameObject("PiperVoice");
            go.transform.SetParent(Brain.transform, false);
            Piper = go.AddComponent<PiperVoice>();
        }
        // If an NpcSwitcher owns a SHARED model, it's the sole owner of that
        // GrizAnimatorLink's wiring — don't fight it. Otherwise scope the lookup to THIS
        // NPC's own hierarchy first: a scene-wide FindObjectOfType would grab whichever
        // GrizAnimatorLink exists first in a MULTI-NPC scene (e.g. Griz's instead of
        // Sana's), silently animating/lip-syncing the wrong character.
        var switcher = FindObjectOfType<NpcSwitcher>();
        bool sharedModelOwnedBySwitcher = switcher != null && switcher.sharedModel != null;
        if (!sharedModelOwnedBySwitcher)
        {
            if (AnimLink == null && Brain != null) AnimLink = Brain.GetComponentInChildren<GrizAnimatorLink>();
            if (AnimLink == null && Brain != null) AnimLink = Brain.GetComponentInParent<GrizAnimatorLink>();
            if (AnimLink == null) AnimLink = FindObjectOfType<GrizAnimatorLink>(); // last resort, single-NPC scenes
        }
        // Reuse a Llama service already in the scene (e.g. Griz's) if one exists — one
        // llama-server can serve both NPCs sequentially, no need to run two model processes.
        if (Llm == null) Llm = FindObjectOfType<LlamaIntentService>();
        if (Llm == null && Brain != null)
        {
            var go = new GameObject("LlamaIntent");
            go.transform.SetParent(Brain.transform, false);
            Llm = go.AddComponent<LlamaIntentService>();
        }
        if (Whisper == null) Whisper = FindObjectOfType<WhisperTranscriber>();
        if (Whisper == null && Brain != null)
        {
            var go = new GameObject("Whisper");
            go.transform.SetParent(Brain.transform, false);
            Whisper = go.AddComponent<WhisperTranscriber>();
        }
        if (Translator == null) Translator = FindObjectOfType<NllbTranslator>(); // shared, like Llm/Whisper
        if (Translator == null && Brain != null)
        {
            var go = new GameObject("NllbTranslator");
            go.transform.SetParent(Brain.transform, false);
            Translator = go.AddComponent<NllbTranslator>();
        }
        if (AnimLink != null)
        {
            AnimLink.piper = Piper;
            AnimLink.gibberish = Voice;
            AnimLink.useAnimatorStates = false; // open-domain NPC: no shop states/sword logic
            AnimLink.PushPiperToLipSync(); // multi-NPC safe — see GrizAnimatorLink
        }
        if (Vosk != null)
        {
            Vosk.OnTranscriptionResult += OnFinalResult;
            Vosk.OnPartialResult += OnPartial; // named (not a lambda) so it can be unsubscribed
            StartCoroutine(KickMicrophone());
        }
        string name = Brain != null ? Brain.npcName : "the NPC";
        Say(NpcTag(), $"(looks up, genuinely curious) Oh — hey. I'm {name}. What's on your mind?");
    }

    void OnPartial(string p)
    {
        if (!Active) return;
        _partial = ExtractText(p);
        if (!string.IsNullOrEmpty(_partial))
        {
            _lastVoiceActivity = Time.time;
            if (!_utteranceOpen)
            {
                _utteranceOpen = true;
                if (Whisper != null) Whisper.MarkUtteranceStart();
            }
        }
    }

    // ── Multi-NPC support (NpcSwitcher) — see GrizTestConsole.Pause/Resume for the reasoning. ──
    public bool Active { get; private set; } = true;

    public void Pause()
    {
        if (!Active) return;
        Active = false;
        if (Voice != null) Voice.Stop();
        if (Piper != null) Piper.Stop();
        if (Vosk != null) Vosk.StopRecordingManual();
        _pendingText = "";
        _partial = "";
    }

    public void Resume()
    {
        if (Active) return;
        Active = true;
        if (Vosk != null) Vosk.StartRecordingManual();
        _lastVoiceActivity = Time.time;
    }

    void OnDestroy()
    {
        if (Vosk != null)
        {
            Vosk.OnTranscriptionResult -= OnFinalResult;
            Vosk.OnPartialResult -= OnPartial;
        }
    }

    string NpcTag() => (Brain != null ? Brain.npcName : "NPC").ToUpperInvariant();

    IEnumerator KickMicrophone()
    {
        for (int i = 0; i < 30 && !_micStarted; i++)
        {
            yield return new WaitForSeconds(1f);
            var vp = Vosk.VoiceProcessor;
            if (vp != null && vp.IsRecording) { _micStarted = true; break; }
            try { Vosk.StartRecordingManual(); } catch { }
        }
    }

    void Update()
    {
        if (!Active) return; // paused via NpcSwitcher
        var vp = Vosk != null ? Vosk.VoiceProcessor : null;
        if (vp != null && vp.IsRecording)
        {
            _peakVolume = Mathf.Max(_peakVolume, vp.CurrentRawVolume);
            if (vp.CurrentRawVolume >= speakingVolume) _lastVoiceActivity = Time.time;
        }

        if (!string.IsNullOrEmpty(_pendingText) &&
            Time.time - _lastVoiceActivity >= silenceToSendSeconds)
            ReplyNow();
    }

    void OnFinalResult(string json)
    {
        if (!Active) return; // paused via NpcSwitcher
        string text = ExtractText(json);
        _partial = "";
        if (string.IsNullOrWhiteSpace(text)) { _peakVolume = 0f; return; }
        HandleUtterance(text, _peakVolume, immediate: false);
        _peakVolume = 0f;
    }

    void HandleUtterance(string text, float peakVolume, bool immediate)
    {
        bool wasSpeaking = (Voice != null && Voice.IsSpeaking) || (Piper != null && Piper.IsSpeaking);
        bool interrupted = false;
        if (wasSpeaking)
        {
            if (Voice != null) Voice.Stop();
            if (Piper != null) Piper.Stop();
            interrupted = true;
        }
        if (Brain != null) Brain.NudgeFromVolume(peakVolume, interrupted);

        _pendingText = string.IsNullOrEmpty(_pendingText) ? text : _pendingText + " " + text;
        _pendingVolume = Mathf.Max(_pendingVolume, peakVolume);
        _lastVoiceActivity = Time.time;

        if (immediate) ReplyNow(skipWhisper: true);
    }

    void ReplyNow(bool skipWhisper = false)
    {
        string text = _pendingText;
        float vol = _pendingVolume;
        _pendingText = "";
        _pendingVolume = 0f;
        if (string.IsNullOrWhiteSpace(text)) { _utteranceOpen = false; return; }

        // Near-silent "utterance": Vosk partials fire on breath/background noise (especially
        // in BN mode where English Vosk mishears everything), and shipping that near-empty
        // audio to Whisper hallucinates garbage. Drop it. (skipWhisper = typed input, exempt.)
        if (!skipWhisper && vol < minSendPeak) { _utteranceOpen = false; return; }

        if (!skipWhisper && Whisper != null && Whisper.IsReady && _utteranceOpen)
            StartCoroutine(RefineThenSend(text, vol));
        else
        {
            _utteranceOpen = false;
            SendToBrain(text, vol);
        }
    }

    IEnumerator RefineThenSend(string voskText, float vol)
    {
        _utteranceOpen = false;
        string refined = null;
        yield return Whisper.EndUtteranceAndTranscribe(t => refined = t);
        bool useWhisper = !string.IsNullOrWhiteSpace(refined);
        SendToBrain(useWhisper ? refined : voskText, vol, useWhisper ? "·w" : "·v");
    }

    void SendToBrain(string text, float vol, string srcTag = "")
    {
        Say("YOU", $"{text}   <vol {vol:0.00}{srcTag}>");
        _lastYouLine = text;
        _lastYouLineTime = Time.time;
        _history.Add($"Player: {text}");
        if (_history.Count > 16) _history.RemoveAt(0);

        if (Llm != null)
        {
            _pendingRequests++;
            StartCoroutine(GenerateThenFinish(text));
        }
        else
        {
            FinishReply("...(no local model installed — Tools > Griz > Check Llama Setup)", "no-llm");
        }
    }

    IEnumerator GenerateThenFinish(string playerText)
    {
        string npcName = Brain != null ? Brain.npcName : "NPC";
        string facts = BuildFacts();
        string gen = null;
        // ALWAYS generate in English, even in BN mode — Qwen's Bangla grammar is genuinely
        // broken (confirmed in testing: garbled conjuncts, wrong verb forms), because Bangla
        // is low-resource in its training data. NLLB (a model built specifically for
        // translation) does the EN->BN step afterward and is dramatically better at it.
        // English reasoning + English wit in, dedicated-translator Bangla out.
        yield return Llm.GenerateReply(string.Join("\n", _history), facts, (t, ok) => { if (ok) gen = t; },
            systemPromptOverride: Brain != null ? Brain.persona : null,
            npcName: npcName,
            closingInstruction: "Reply with dialogue only, in ENGLISH, then on a NEW final line write exactly " +
                                 "\"SENTIMENT: x\" where x is one of warm, cold, funny, rude, neutral " +
                                 "describing the TONE THE PLAYER used toward you just now.",
            maxTokens: 110, temperature: 0.9f);

        _pendingRequests--;
        if (string.IsNullOrWhiteSpace(gen))
        {
            FinishReply("(trails off, distracted for a second) ...sorry, what were you saying?", "gen-fail");
            yield break;
        }

        string sentiment = ExtractSentiment(gen, out string dialogueOnly);
        if (Brain != null) Brain.ApplySentiment(sentiment);
        MaybeRemember(playerText);

        if (_bangla && Translator != null && Translator.IsReady)
        {
            string bn = null;
            yield return Translator.ToBangla(dialogueOnly, (t, ok) => { if (ok) bn = t; });
            if (!string.IsNullOrWhiteSpace(bn))
            {
                FinishReply(bn, $"gen·{sentiment}·nllb");
                yield break;
            }
            // NLLB failed for this line — better to speak the (grammatically correct)
            // English than silently say nothing, or worse, fall back to Qwen's broken Bangla.
            Debug.LogWarning("[NLLB] translation failed for this reply — speaking English instead of guessing at Bangla.");
        }
        FinishReply(dialogueOnly, $"gen·{sentiment}");
    }

    string BuildFacts()
    {
        var sb = new StringBuilder();
        if (Brain == null) return "";
        sb.AppendLine("CONTEXT:");
        sb.AppendLine($"- your current mood: {Brain.MoodWord()} (rapport {Brain.Rapport:0}/100)");
        if (Brain.RememberedFacts.Count > 0)
        {
            sb.AppendLine("- REMEMBERED FACTS about the player from earlier this conversation:");
            foreach (var f in Brain.RememberedFacts) sb.AppendLine($"  - {f}");
        }
        return sb.ToString();
    }

    // Cheap heuristic: if the player states something personal ("I have a...", "my name
    // is...", "I love/hate..."), keep it verbatim as a remembered fact. This is NOT an
    // LLM call - it's a lightweight net so the "remembers what you said" demo works
    // without a third round-trip per turn. Good enough for a showcase; a proper version
    // would ask the LLM to extract facts explicitly.
    static readonly Regex MemoryTrigger = new Regex(
        @"\b(my name is|i'm called|i am called|i have a|i love|i hate|i work as|i'm a |i am a |my favorite|my favourite)\b",
        RegexOptions.IgnoreCase);

    void MaybeRemember(string playerText)
    {
        if (Brain == null) return;
        if (MemoryTrigger.IsMatch(playerText))
            Brain.Remember(playerText.Length > 140 ? playerText.Substring(0, 140) : playerText);
    }

    static string ExtractSentiment(string raw, out string dialogueOnly)
    {
        var m = Regex.Match(raw, @"SENTIMENT:\s*(\w+)\s*$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            dialogueOnly = raw.Substring(0, m.Index).TrimEnd('\n', '\r', ' ');
            return m.Groups[1].Value.ToLowerInvariant();
        }
        dialogueOnly = raw;
        return "neutral";
    }

    void FinishReply(string line, string tag)
    {
        _history.Add($"{(Brain != null ? Brain.npcName : "NPC")}: {line}");
        if (_history.Count > 16) _history.RemoveAt(0);
        Say(NpcTag(), $"{line}   <{tag}>");
        _lastNpcLine = line;
        _lastNpcLineTime = Time.time;
        SpeakLine(line);
        if (AnimLink != null) AnimLink.PlayForLine(line, GrizBrain.Intent.Smalltalk, null);
    }

    void SpeakLine(string line)
    {
        if (Piper != null && Piper.Available) Piper.Speak(line);
        else if (Voice != null) Voice.Speak(line, 1f);
    }

    void Say(string who, string line)
    {
        _log.Add($"[{who}] {line}");
        if (_log.Count > 60) _log.RemoveAt(0);
        _scroll.y = float.MaxValue;
    }

    static string ExtractText(string json)
    {
        if (string.IsNullOrEmpty(json)) return "";
        var m = Regex.Match(json, "\"(?:text|partial)\"\\s*:\\s*\"([^\"]*)\"");
        return m.Success ? m.Groups[1].Value : json;
    }

    void OnGUI()
    {
        if (!Active) return; // paused via NpcSwitcher — the switcher draws its own tab bar
        var e = Event.current;
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.F1)
        {
            cinematicMode = !cinematicMode;
            e.Use();
        }
        if (cinematicMode) { DrawCinematic(); return; }

        int margin = Mathf.RoundToInt(Screen.width * 0.03f);
        int W = Screen.width - margin * 2;
        int H = Screen.height - margin * 2;
        float k = Screen.height / 1080f;
        int fBig = Mathf.RoundToInt(34 * k);
        int fLog = Mathf.RoundToInt(26 * k);
        int fSmall = Mathf.RoundToInt(20 * k);

        GUILayout.BeginArea(new Rect(margin, margin, W, H), GUI.skin.box);

        string npcName = Brain != null ? Brain.npcName.ToUpperInvariant() : "NPC";
        GUILayout.Label($"<b>{npcName} — open conversation prototype</b>   <color=#888888>(talk, or type below — F1: cinematic mode)</color>",
            Rich(fBig));

        var vp = Vosk != null ? Vosk.VoiceProcessor : null;
        bool mic = vp != null && vp.IsRecording;
        float vol = mic ? vp.CurrentRawVolume : 0f;
        string voiceMode = Piper != null && Piper.Available
            ? "<color=#66ff66>Piper TTS</color>"
            : "gibberish <color=#888888>(run Tools > Griz > Check Piper Setup)</color>";
        voiceMode += Llm != null && Llm.IsReady ? "   Brain: <color=#66ff66>LLM (shared)</color>" : "   Brain: <color=#ff6666>no LLM</color>";
        voiceMode += Whisper != null && Whisper.IsReady
            ? $"   Ears: <color=#66ff66>Whisper ({(_bangla ? "BN→EN" : "EN")}, {Whisper.LoadedModelName})</color>"
            : Whisper != null ? "   Ears: <color=#ffe066>Whisper reloading…</color>" : "   Ears: Vosk";
        GUILayout.Label($"Mic: <b>{(mic ? "<color=#66ff66>LIVE</color>" : "starting…")}</b>   " +
                        $"Vol: {Bar(vol)}   Peak: {_peakVolume:0.00}   Voice: {voiceMode}", Rich(fSmall));
        if (!string.IsNullOrEmpty(_partial) || !string.IsNullOrEmpty(_pendingText))
        {
            string composing = (_pendingText + " " + _partial).Trim();
            float quiet = Time.time - _lastVoiceActivity;
            GUILayout.Label($"<color=#ffe066>hearing: “{composing}…”  (sends after {Mathf.Max(0f, silenceToSendSeconds - quiet):0.0}s of silence)</color>", Rich(fSmall));
        }

        float reserved = fBig + fSmall * 4 + 90 * k + 60;
        _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(H - reserved));
        foreach (var line in _log)
        {
            string colored = line.StartsWith("[YOU]") ? $"<color=#99ccff>{line}</color>" : $"<color=#ffb366>{line}</color>";
            GUILayout.Label(colored, Rich(fLog));
            GUILayout.Space(6 * k);
        }
        GUILayout.EndScrollView();

        GUI.SetNextControlName("companionInput");
        var inputStyle = new GUIStyle(GUI.skin.textField) { fontSize = fLog };
        _typed = GUILayout.TextField(_typed, inputStyle, GUILayout.Height(46 * k));
        bool submit = Event.current.type == EventType.KeyUp &&
                      (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter) &&
                      GUI.GetNameOfFocusedControl() == "companionInput";
        var btnStyle = new GUIStyle(GUI.skin.button) { fontSize = fSmall };
        GUILayout.BeginHorizontal();
        if ((GUILayout.Button("Send (typed)", btnStyle, GUILayout.Width(220 * k), GUILayout.Height(40 * k)) || submit)
            && !string.IsNullOrWhiteSpace(_typed))
        {
            float vol2 = _typed.EndsWith("!") ? 0.6f : 0.1f;
            HandleUtterance(_typed.TrimEnd('!'), vol2, immediate: true);
            _typed = "";
            GUI.FocusControl("companionInput");
        }
        if (GUILayout.Button("Reset", btnStyle, GUILayout.Width(140 * k), GUILayout.Height(40 * k)))
        {
            if (Brain != null) Brain.Reset();
            _log.Clear();
            _history.Clear();
            Say(NpcTag(), "(shakes it off) Okay — clean slate. Where were we?");
        }
        var toggleStyle = new GUIStyle(GUI.skin.toggle) { fontSize = fSmall };
        showDebugState = GUILayout.Toggle(showDebugState, " state", toggleStyle);

        if (Whisper != null)
        {
            string label = _bangla ? "Speaking: BN (switch to EN)" : "Speaking: EN (switch to BN)";
            Color prevColor = GUI.color;
            if (_bangla) GUI.color = new Color(0.6f, 1f, 0.6f);
            if (GUILayout.Button(label, btnStyle, GUILayout.Width(260 * k), GUILayout.Height(40 * k)))
            {
                _bangla = !_bangla;
                Whisper.Restart(_bangla ? "bn" : "en", translate: _bangla);
                Say("*", _bangla
                    ? "── Switched to Bangla input (spoken Bangla, understood as English, replies in Bangla) ──"
                    : "── Switched back to English input ──");
            }
            GUI.color = prevColor;
        }
        GUILayout.FlexibleSpace();
        GUILayout.Label("<color=#888888>tip: typed input ending in ! counts as shouting</color>", Rich(fSmall));
        GUILayout.EndHorizontal();

        if (showDebugState && Brain != null)
        {
            string mem = Brain.RememberedFacts.Count > 0 ? string.Join("  |  ", Brain.RememberedFacts) : "(nothing yet)";
            GUILayout.Label($"rapport <b>{Brain.Rapport:0}</b>  |  energy {Brain.Energy:0}  |  mood: {Brain.MoodWord()}", Rich(fSmall));
            GUILayout.Label($"<color=#888888>remembers: {mem}</color>", Rich(fSmall));
        }

        GUILayout.EndArea();
    }

    void DrawCinematic()
    {
        float k = Screen.height / 1080f;
        int fName = Mathf.RoundToInt(30 * k);
        int fLine = Mathf.RoundToInt(38 * k);
        int fSmall = Mathf.RoundToInt(20 * k);

        GUI.Label(new Rect(10, 6, 500, 34), "<color=#66666688>F1 — debug console</color>", Rich(fSmall));

        bool composingNow = !string.IsNullOrEmpty(_partial) || !string.IsNullOrEmpty(_pendingText);
        string youText = null;
        if (composingNow)
        {
            float quiet = Time.time - _lastVoiceActivity;
            youText = $"<color=#ffe066>{(_pendingText + " " + _partial).Trim()}…   " +
                      $"<size={fSmall}>({Mathf.Max(0f, silenceToSendSeconds - quiet):0.0}s)</size></color>";
        }
        else if (Time.time - _lastYouLineTime < 4f && !string.IsNullOrEmpty(_lastYouLine))
        {
            youText = $"<color=#ffffff>{_lastYouLine}</color>";
        }
        if (youText != null)
        {
            float yw = Mathf.Max(480f, Screen.width * 0.36f);
            int fYou = Mathf.RoundToInt(30 * k);
            var youStyle = Rich(fYou);
            float yh = youStyle.CalcHeight(new GUIContent(youText), yw - 36 * k) + 48 * k;
            var yr = new Rect(Screen.width - yw - 24 * k, Screen.height - yh - 24 * k, yw, yh);
            GUI.Box(yr, GUIContent.none);
            GUI.Box(yr, GUIContent.none);
            GUI.Label(new Rect(yr.x + 18 * k, yr.y + 8 * k, yw - 36 * k, fSmall + 8),
                "<b><color=#99ccff>YOU</color></b>", Rich(fSmall));
            GUI.Label(new Rect(yr.x + 18 * k, yr.y + fSmall + 16 * k, yw - 36 * k, yh - fSmall - 20 * k),
                youText, youStyle);
        }

        bool speaking = (Voice != null && Voice.IsSpeaking) || (Piper != null && Piper.IsSpeaking);
        bool show = !string.IsNullOrEmpty(_lastNpcLine) && (speaking || Time.time - _lastNpcLineTime < 6f);
        if (!show) return;

        string npcName = Brain != null ? Brain.npcName.ToUpperInvariant() : "NPC";
        float w = Mathf.Max(520f, Screen.width * 0.42f);
        var lineStyle = Rich(fLine);
        float textH = lineStyle.CalcHeight(new GUIContent(_lastNpcLine), w - 44 * k);
        float h = textH + fName + 44 * k;
        var rect = new Rect(24 * k, Screen.height - h - 24 * k, w, h);
        GUI.Box(rect, GUIContent.none);
        GUI.Box(rect, GUIContent.none);
        GUI.Label(new Rect(rect.x + 22 * k, rect.y + 10 * k, w - 44 * k, fName + 10),
            $"<b><color=#ffb366>{npcName}</color></b>", Rich(fName));
        GUI.Label(new Rect(rect.x + 22 * k, rect.y + fName + 22 * k, w - 44 * k, textH + 8),
            $"<color=#ffffff>{_lastNpcLine}</color>", lineStyle);
    }

    static GUIStyle Rich(int size)
    {
        return new GUIStyle(GUI.skin.label) { richText = true, fontSize = size, wordWrap = true };
    }

    static string Bar(float v)
    {
        int n = Mathf.Clamp(Mathf.RoundToInt(v * 10), 0, 10);
        return new string('█', n) + new string('░', 10 - n);
    }
}
