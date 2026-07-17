using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

// GNOMES & GASLIGHT — one suspect's live interrogation. Reuses the proven NPC pipeline
// wholesale (Vosk partials -> Whisper refine -> local LLM -> Piper voice, same pattern as
// CompanionConsole/GrizTestConsole) but the BRAIN here is SuspectBrain, not open chat: a
// classifier reads the player's pressure KIND (comfort/evidence/flattery/blunt), the state
// machine (patience/false-give/broken) decides what's ALLOWED to be said, and only THEN
// does the LLM perform it — same facts-cage principle as Griz's price, GrizNPC's secrets,
// the Sentinel's deck. The model never holds the case; it only ever voices what the code
// says this suspect is allowed to reveal right now.
public class InterrogationConsole : MonoBehaviour
{
    public VoskSpeechToText Vosk;
    public WhisperTranscriber Whisper;
    public LlamaIntentService Llm;
    public PiperVoice Voice;
    public SuspectBrain Brain;
    public CaseBoard Board;
    public CaseRunner Runner;

    [Header("Utterance timing (same tuning as the NPC consoles)")]
    public float silenceToSendSeconds = 1f;
    public float speakingVolume = 0.06f;
    public float minSendPeak = 0.05f;

    [Tooltip("Currently 'held' evidence item id the player can present this turn (set by the UI evidence tray). Cleared after one use.")]
    public string presentedEvidenceId;

    bool _utteranceOpen;
    string _pendingText = "";
    float _pendingVolume, _lastVoiceActivity, _peakVolume;
    string _partial = "";
    readonly List<string> _history = new List<string>();
    int _pendingRequests;

    public bool Active { get; private set; } = true;
    public event Action<string, string> OnLine; // (who, text) — "YOU" or the suspect's name

    void Awake()
    {
        if (Vosk == null) Vosk = FindObjectOfType<VoskSpeechToText>();
        if (Vosk != null) Vosk.FreeDictation = true;
    }

    void Start()
    {
        if (Llm == null) Llm = FindObjectOfType<LlamaIntentService>();
        if (Whisper == null) Whisper = FindObjectOfType<WhisperTranscriber>();
        if (Voice == null && Brain != null)
        {
            var go = new GameObject($"{Brain.suspectName}Voice");
            go.transform.SetParent(transform, false);
            Voice = go.AddComponent<PiperVoice>();
        }
        if (Vosk != null)
        {
            Vosk.OnTranscriptionResult += OnFinalResult;
            Vosk.OnPartialResult += OnPartial;
        }
    }

    void OnDestroy()
    {
        if (Vosk != null)
        {
            Vosk.OnTranscriptionResult -= OnFinalResult;
            Vosk.OnPartialResult -= OnPartial;
        }
    }

    /// <summary>Called by CaseRunner/UI when this suspect's interrogation slot starts.</summary>
    public void BeginInterview()
    {
        Active = true;
        _history.Clear();
        if (Vosk != null) Vosk.StartRecordingManual();
    }

    public void EndInterview()
    {
        Active = false;
        if (Vosk != null) Vosk.StopRecordingManual();
        if (Voice != null) Voice.Stop();
    }

    public void Pause() => EndInterview();
    public void Resume() => BeginInterview();

    void OnPartial(string p)
    {
        if (!Active) return;
        _partial = ExtractText(p);
        if (!string.IsNullOrEmpty(_partial))
        {
            _lastVoiceActivity = Time.time;
            if (!_utteranceOpen) { _utteranceOpen = true; Whisper?.MarkUtteranceStart(); }
        }
    }

    void OnFinalResult(string json)
    {
        if (!Active) return;
        string text = ExtractText(json);
        _partial = "";
        if (string.IsNullOrWhiteSpace(text)) { _peakVolume = 0f; return; }
        _pendingText = string.IsNullOrEmpty(_pendingText) ? text : _pendingText + " " + text;
        _pendingVolume = Mathf.Max(_pendingVolume, _peakVolume);
        _lastVoiceActivity = Time.time;
        _peakVolume = 0f;
    }

    void Update()
    {
        if (!Active) return;
        var vp = Vosk != null ? Vosk.VoiceProcessor : null;
        if (vp != null && vp.IsRecording)
        {
            _peakVolume = Mathf.Max(_peakVolume, vp.CurrentRawVolume);
            if (vp.CurrentRawVolume >= speakingVolume) _lastVoiceActivity = Time.time;
        }
        if (!string.IsNullOrEmpty(_pendingText) && Time.time - _lastVoiceActivity >= silenceToSendSeconds)
        {
            string text = _pendingText; float vol = _pendingVolume;
            _pendingText = ""; _pendingVolume = 0f;
            if (vol < minSendPeak) { _utteranceOpen = false; return; }
            StartCoroutine(RefineThenSend(text));
        }
    }

    IEnumerator RefineThenSend(string voskText)
    {
        string refined = null;
        if (Whisper != null && Whisper.IsReady && _utteranceOpen)
            yield return Whisper.EndUtteranceAndTranscribe(t => refined = t);
        _utteranceOpen = false;
        SendToSuspect(string.IsNullOrWhiteSpace(refined) ? voskText : refined);
    }

    /// <summary>Typed input path — same entrypoint voice uses, for testing/accessibility.</summary>
    public void SendTyped(string text) => SendToSuspect(text);

    // ── pressure-kind classification: cheap keyword read, same spirit as GrizBrain's
    // keyword scorer — good enough to route to the right axis, the LLM never sees this
    // logic, only its outcome via SuspectBrain.BuildFactsCage(). ──
    static SuspectBrain.PressureKind ClassifyPressure(string text, bool hasEvidence)
    {
        if (hasEvidence) return SuspectBrain.PressureKind.Evidence;
        string t = (text ?? "").ToLowerInvariant();
        if (Score(t, "sorry", "must be hard", "i understand", "take your time", "i know this is difficult", "are you okay", "how are you holding up") > 0)
            return SuspectBrain.PressureKind.Comfort;
        if (Score(t, "wonderful", "lovely", "you're so", "youre so", "impressive", "i heard you", "everyone says", "between us") > 0)
            return SuspectBrain.PressureKind.Flattery;
        if (Score(t, "tell me the truth", "you're lying", "youre lying", "i know you", "confess", "admit it", "where were you", "explain yourself") > 0)
            return SuspectBrain.PressureKind.Blunt;
        return SuspectBrain.PressureKind.Blunt; // default: generic questioning reads as blunt pressure
    }

    static int Score(string text, params string[] keys)
    {
        int s = 0;
        foreach (var k in keys) if (text.Contains(k)) s++;
        return s;
    }

    void SendToSuspect(string playerText)
    {
        if (Brain == null) return;
        playerText = (playerText ?? "").Trim();
        if (playerText.Length == 0) return;

        OnLine?.Invoke("YOU", playerText);
        _history.Add($"Player: {playerText}");
        if (_history.Count > 16) _history.RemoveAt(0);

        string evidenceId = presentedEvidenceId;
        presentedEvidenceId = null; // one use per presentation
        bool hasEvidence = !string.IsNullOrEmpty(evidenceId) &&
                           (Board == null || Board.IsEvidenceDiscovered(evidenceId));
        var kind = ClassifyPressure(playerText, hasEvidence);
        var result = Brain.ApplyPressure(kind, hasEvidence ? evidenceId : null);

        if (result.endedEarly)
        {
            OnLine?.Invoke(Brain.suspectName, "(stands, straightens their collar) I think that's quite enough for today.");
            Runner?.EndInterrogation();
            return;
        }

        // Cross-suspect finger-pointing (GDD suspicion system): only fires if the player's
        // OWN words named another suspect — never volunteered unasked. A cheap "asking
        // about someone" phrase check keeps this distinct from ordinary questioning so
        // "where were you" doesn't accidentally trigger it just because a name is nearby.
        var suspicion = MentionsAskingAboutSomeone(playerText) ? Brain.FindSuspicionAbout(playerText) : null;

        _pendingRequests++;
        StartCoroutine(GenerateThenSpeak(playerText, result, suspicion));
    }

    static bool MentionsAskingAboutSomeone(string text)
    {
        string t = (text ?? "").ToLowerInvariant();
        return Score(t, "what do you think of", "what do you think about", "tell me about", "do you trust",
                        "do you suspect", "what about", "your opinion of", "your opinion on") > 0;
    }

    IEnumerator GenerateThenSpeak(string playerText, SuspectBrain.PressureResult result, SuspectBrain.Suspicion suspicion = null)
    {
        string facts = Brain.BuildFactsCage(suspicion);
        string gen = null;
        yield return Llm.GenerateReply(string.Join("\n", _history), facts, (t, ok) => { if (ok) gen = t; },
            systemPromptOverride: Brain.persona, npcName: Brain.suspectName,
            closingInstruction:
                "Reply with dialogue only, in character, reacting SPECIFICALLY to what was just said. " +
                "Never state facts outside YOUR GUARDED SECRET/FALSE GIVE/CURRENT PATIENCE context above. " +
                $"Write {Brain.suspectName}'s next line now:",
            maxTokens: 90, temperature: 0.75f);
        _pendingRequests--;

        string line = string.IsNullOrWhiteSpace(gen)
            ? "(hesitates, loses their train of thought for a moment) ...I'm sorry, what was the question?"
            : gen.Trim();

        _history.Add($"{Brain.suspectName}: {line}");
        OnLine?.Invoke(Brain.suspectName, line);
        if (Voice != null && Voice.Available) Voice.Speak(line);

        if (Board != null)
        {
            int round = Runner != null ? Runner.RoundIndex : 0;
            Board.AddCard(Brain.suspectName, line, "statement", round);
            if (result.falseGiveTriggered && !string.IsNullOrEmpty(Brain.falseGiveContent))
                Board.AddCard(Brain.suspectName, Brain.falseGiveContent, "false-give", round, isFalseGive: true);
            if (result.brokenTriggered && !string.IsNullOrEmpty(Brain.secretContent))
                Board.AddCard(Brain.suspectName, Brain.secretContent, "broken-reveal", round, isBrokenReveal: true);
            if (suspicion != null)
                Board.AddCard(Brain.suspectName, $"On {suspicion.aboutWhom}: {suspicion.belief}", "accusation", round);
        }
    }

    static string ExtractText(string json)
    {
        if (string.IsNullOrEmpty(json)) return "";
        var m = Regex.Match(json, "\"(?:text|partial)\"\\s*:\\s*\"([^\"]*)\"");
        return m.Success ? m.Groups[1].Value : json;
    }
}
