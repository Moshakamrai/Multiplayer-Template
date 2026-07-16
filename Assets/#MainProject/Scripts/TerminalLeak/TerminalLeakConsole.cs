using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

// TERMINAL LEAK — solo-testable core loop (2P Mirror networking comes after this proves fun).
//
// SOLO HOTSEAT: one machine, two roles. You SPEAK as Intel (describe the secret word in
// disguised clues — the mic is your clue channel) and you TYPE as Operator (commit guesses).
// The SentinelAI plays against you for real: it reads the same radio log (corrupted by the
// current vault layer's wiretap quality) and races you to the word.
//
// STRICT TURN CYCLE (the mic is DEAD outside your line's turn):
//   INTEL TRANSMISSION (speak, burns 1 token — every second on the radio feeds the trace)
//     → OPERATOR INQUIRY (type a guess [burns 1 token if wrong] or PASS)
//       → SENTINEL INQUIRY (free — it's the house)
//         → back to INTEL
//
// THE EXPOSURE ECONOMY (what makes it strategic instead of a quiz):
// - The Sentinel does NOT know the card's theme tag until the final layer — playtesting
//   showed tag + one clue collapses the search space ("OBJECT/WEIGHT" + "ship" = ANCHOR).
// - It cannot guess until it has actually heard minCluesBeforeGuess transmissions.
// - A WRONG Sentinel guess EXPOSES it: wiretap offline for exposureTransmissions full
//   cycles. Those are CLEAN WINDOWS — clues spoken then never reach it. So the human
//   strategy space includes deliberately baiting a confident wrong guess, then rushing
//   the real clue through the deaf window. Its wrong guesses also leak what it's thinking.
//
// Reuses the proven NPC pipeline wholesale: Vosk partials open an utterance, Whisper
// refines it on 1s of silence, llama-server thinks, Piper speaks. Every human line is
// re-synthesized through a Piper "voice mask" — the game's aesthetic (everyone sounds
// like a machine) and its networking shortcut (only strings ever cross the wire) in one.
public class TerminalLeakConsole : MonoBehaviour
{
    public VoskSpeechToText Vosk;
    public WhisperTranscriber Whisper;
    public LlamaIntentService Llm;
    public SentinelAI Sentinel;
    public PiperVoice PlayerMask;   // your own voice, re-synthesized — the mask aesthetic

    [Header("Utterance timing (same tuning as the NPC consoles)")]
    public float silenceToSendSeconds = 1f;
    public float speakingVolume = 0.06f;
    public float minSendPeak = 0.05f;

    [Header("Run structure")]
    [Tooltip("Cards to clear to extract the Core Data Key. Layer index also drives Sentinel hearing quality.")]
    public int vaultLayers = 3;
    [Tooltip("Radio budget per card: each Intel transmission burns 1, each WRONG Operator guess burns 1. 0 = traced.")]
    public int tokensPerCard = 12;
    public float overrideSeconds = 45f;

    [Header("Sentinel balance")]
    [Tooltip("Transmissions the Sentinel must actually HEAR before it is allowed to commit a guess.")]
    public int minCluesBeforeGuess = 2;
    [Tooltip("Full cycles its wiretap stays offline after a wrong guess — the humans' clean window.")]
    public int exposureTransmissions = 2;
    [Tooltip("On the final vault layer the wiretap fully locks on and it learns the theme tag too.")]
    public bool tagKnownOnFinalLayer = true;

    enum Phase { Boot, TurnIntel, TurnOperator, TurnSentinel, Breach, Override, RunLost, RunWon }
    Phase _phase = Phase.Boot;

    // ── crew gadgets — spendable tools layered on the token economy ──
    // JAMMER (1/card):    arm it, and your next transmission is guaranteed clean — spend it on THE clue.
    // TAP-TRACE (1/run):  force the Sentinel to dump its current top suspects. Honest, and terrifying.
    // DECOY (1/card):     type a fake clue only the Sentinel hears — your partner never sees it as real.
    bool _jammerArmed, _jammerUsed, _decoyUsed, _tapTraceUsed, _tapTraceRunning;
    string _decoyTyped = "";

    // deck
    [Serializable] class Card { public string word; public string tag; }
    [Serializable] class Deck { public List<Card> cards; }
    List<Card> _deck = new List<Card>();
    Card _card;
    int _layer;      // 0-based vault layer = cards cleared
    int _tokens;

    // radio: what the PLAYERS said vs what the SENTINEL actually heard (clean windows differ)
    readonly List<string> _feed = new List<string>();          // rendered terminal feed (rich text)
    readonly List<string> _radioSentinel = new List<string>(); // the wiretap's view, fed to the LLM
    int _cluesHeard;     // Intel transmissions the Sentinel actually intercepted this card
    int _sentinelDeaf;   // remaining clean-window cycles after a wrong guess exposed it

    // mic state
    bool _utteranceOpen;
    string _pendingText = "";
    float _pendingVolume, _lastVoiceActivity, _peakVolume;
    string _partial = "";
    bool _micStarted;

    // phase 2
    struct Panel { public Texture2D tex; public string desc; }
    Panel[] _panels;
    int _targetPanel;
    int[] _buttonOrder;
    float _overrideEnds;
    Coroutine _gaslighting;

    string _typed = "";
    Vector2 _scroll;
    bool _stickBottom = true;

    static readonly Color BG = new Color(0.051f, 0.051f, 0.067f);      // #0D0D11
    const string PURPLE = "#a855f7";
    const string CYAN = "#06b6d4";
    const string GREEN = "#4ade80";
    const string RED = "#f87171";

    void Awake()
    {
        if (Vosk == null) Vosk = FindObjectOfType<VoskSpeechToText>();
        if (Vosk != null) Vosk.FreeDictation = true;
    }

    void Start()
    {
        // Reuse services already in the scene; spin up what's missing (NPC-console pattern).
        if (Llm == null) Llm = FindObjectOfType<LlamaIntentService>();
        if (Llm == null) Llm = NewChild("LlamaIntent").AddComponent<LlamaIntentService>();
        if (Whisper == null) Whisper = FindObjectOfType<WhisperTranscriber>();
        if (Whisper == null) Whisper = NewChild("Whisper").AddComponent<WhisperTranscriber>();
        if (PlayerMask == null)
        {
            PlayerMask = NewChild("PlayerVoiceMask").AddComponent<PiperVoice>();
            PlayerMask.pitch = 1.1f;         // crew font: higher + faster than the Sentinel's
            PlayerMask.lengthScale = 0.92f;
            PlayerMask.naturalVariation = 0f; // masks are UNNATURALLY steady — that's the point
        }
        if (Sentinel == null)
        {
            Sentinel = NewChild("Sentinel").AddComponent<SentinelAI>();
            var v = NewChild("SentinelVoice").AddComponent<PiperVoice>();
            v.lengthScale = 1.12f;
            v.naturalVariation = 0f;
            Sentinel.Voice = v;
        }
        if (Sentinel.Llm == null) Sentinel.Llm = Llm;
        Sentinel.ResetVoiceFont();

        LoadDeck();
        if (Vosk != null)
        {
            Vosk.OnTranscriptionResult += OnFinalResult;
            Vosk.OnPartialResult += OnPartial;
            StartCoroutine(KickMicrophone());
        }
        BeginRun();
    }

    void OnDestroy()
    {
        if (Vosk != null)
        {
            Vosk.OnTranscriptionResult -= OnFinalResult;
            Vosk.OnPartialResult -= OnPartial;
        }
    }

    GameObject NewChild(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        return go;
    }

    void LoadDeck()
    {
        _deck.Clear();
        try
        {
            string path = Path.Combine(Application.streamingAssetsPath, "terminal-leak", "cards.json");
            if (File.Exists(path))
            {
                var deck = JsonUtility.FromJson<Deck>(File.ReadAllText(path));
                if (deck != null && deck.cards != null) _deck = deck.cards;
            }
        }
        catch (Exception e) { Debug.LogWarning($"[TerminalLeak] deck load failed: {e.Message}"); }
        if (_deck.Count == 0) // emergency fallback so the scene always runs
            _deck = new List<Card> {
                new Card { word = "FIRE",   tag = "ELEMENT / HAZARD" },
                new Card { word = "MIRROR", tag = "OBJECT / GLASS" },
                new Card { word = "GHOST",  tag = "CREATURE / MYTH" } };
        _deck = _deck.OrderBy(_ => UnityEngine.Random.value).ToList();
    }

    // ── run / card flow ─────────────────────────────────────────────────────────

    void BeginRun()
    {
        _layer = 0;
        _feed.Clear();
        _tapTraceUsed = false;
        Sentinel.ClearMemory(); // its notes on your crew belong to a run, not the install
        Sentinel.ResetVoiceFont();
        Sys($"DEEP GRID uplink established. Vault layers: {vaultLayers}. The Sentinel is listening.");
        Sys("SOLO TEST — you are BOTH roles: SPEAK clues as Intel, TYPE guesses as Operator.");
        Sys("Turn cycle: your transmission → operator inquiry → Sentinel inquiry. Every transmission " +
            "and wrong guess burns a token. A wrong SENTINEL guess exposes it — clean radio while it recalibrates.");
        NextCard();
    }

    void NextCard()
    {
        if (_layer >= vaultLayers) { Win(); return; }
        if (_deck.Count == 0) LoadDeck();
        _card = _deck[0];
        _deck.RemoveAt(0);
        _radioSentinel.Clear();
        _cluesHeard = 0;
        _sentinelDeaf = 0;
        _jammerArmed = false;
        _jammerUsed = false;
        _decoyUsed = false;
        _decoyTyped = "";
        _tokens = tokensPerCard;
        _phase = Phase.TurnIntel;
        bool finalLayer = _layer == vaultLayers - 1;
        Sys($"LAYER {_layer + 1}/{vaultLayers} — wiretap corruption: {Sentinel.corruptionByLayer[Mathf.Clamp(_layer, 0, Sentinel.corruptionByLayer.Length - 1)]:P0}" +
            (finalLayer && tagKnownOnFinalLayer ? "  ▲ FULL LOCK-ON: it knows the theme tag here" : ""));
        Feed($"<color={PURPLE}>CLASSIFIED CARD →  Word: <b>{_card.word}</b>  |  Tag: {_card.tag}</color>  <color=#666>(Intel eyes only — in 2P this hides from the Operator)</color>");
        Sys("YOUR LINE — speak a disguised clue.");
    }

    void CardSolved()
    {
        Feed($"<color={GREEN}>██ LAYER {_layer + 1} CRACKED — node advanced ██</color>");
        // Escalating memory: it studies the transcript it heard and takes a note on your
        // crew's clue style. Runs in the background — capture the log before NextCard clears it.
        if (_radioSentinel.Count > 0)
        {
            Feed($"<color={RED}>SENTINEL ▷</color> <color=#666>…archiving your voice patterns.</color>");
            StartCoroutine(Sentinel.MemorizeCrewStyle(string.Join("\n", _radioSentinel)));
        }
        _layer++;
        NextCard();
    }

    void Win()
    {
        _phase = Phase.RunWon;
        Feed($"<color={GREEN}>██ CORE DATA KEY EXTRACTED. Trace evaded. The Sentinel goes quiet. ██</color>");
        Sys("Press R for another run.");
    }

    void Lose(string why)
    {
        _phase = Phase.RunLost;
        Feed($"<color={RED}>██ CONNECTION TRACED — {why} ██</color>");
        Sys("Press R to re-board the vault.");
    }

    // ── Intel (voice) turn ──────────────────────────────────────────────────────

    void OnPartial(string p)
    {
        if (_phase != Phase.TurnIntel) return; // strict turns: the mic is dead off-turn
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

    void OnFinalResult(string json)
    {
        if (_phase != Phase.TurnIntel) { _peakVolume = 0f; return; }
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
        if ((_phase == Phase.RunLost || _phase == Phase.RunWon) && Input.GetKeyDown(KeyCode.R))
        {
            BeginRun();
            return;
        }
        if (_phase == Phase.Override && Time.time >= _overrideEnds)
        {
            EndGaslighting();
            Lose("atmosphere purged — the override came too late");
            return;
        }

        var vp = Vosk != null ? Vosk.VoiceProcessor : null;
        if (vp != null && vp.IsRecording)
        {
            _peakVolume = Mathf.Max(_peakVolume, vp.CurrentRawVolume);
            if (vp.CurrentRawVolume >= speakingVolume) _lastVoiceActivity = Time.time;
        }
        if (!string.IsNullOrEmpty(_pendingText) && Time.time - _lastVoiceActivity >= silenceToSendSeconds)
        {
            string text = _pendingText;
            float vol = _pendingVolume;
            _pendingText = "";
            _pendingVolume = 0f;
            if (vol < minSendPeak) { _utteranceOpen = false; return; } // breath-blip → Whisper hallucination guard
            StartCoroutine(RefineThenTransmit(text));
        }
    }

    IEnumerator RefineThenTransmit(string voskText)
    {
        string refined = null;
        if (Whisper != null && Whisper.IsReady && _utteranceOpen)
            yield return Whisper.EndUtteranceAndTranscribe(t => refined = t);
        _utteranceOpen = false;
        TransmitClue(string.IsNullOrWhiteSpace(refined) ? voskText : refined);
    }

    void TransmitClue(string clue)
    {
        if (_phase != Phase.TurnIntel) return;
        clue = clue.Trim();
        if (clue.Length == 0) return;

        Feed($"<color={CYAN}>INTEL ▷</color> {clue}");
        if (PlayerMask != null && PlayerMask.Available) PlayerMask.Speak(clue); // the voice mask

        // Saying the clean password out loud IS the instant-loss the fiction promises —
        // and no clean window saves you: a committed password trips the vault lock itself.
        if (ContainsWord(clue, _card.word))
        {
            Feed($"<color={RED}>SENTINEL ▷ You said it in the clear. How considerate.</color>");
            Sentinel.Speak("You said it in the clear. How considerate.");
            Breach(_card.word);
            return;
        }

        if (BurnToken("that transmission")) return; // radio time feeds the trace

        if (_jammerArmed)
        {
            _jammerArmed = false;
            Feed($"<color={GREEN}>▼ JAMMED — that transmission never left the encrypted band. It heard nothing.</color>");
        }
        else if (_sentinelDeaf > 0)
        {
            Feed($"<color={GREEN}>▼ CLEAN WINDOW — the wiretap is still recalibrating; it never heard that.</color>");
        }
        else
        {
            _radioSentinel.Add($"PLAYER-1 (Intel): {clue}");
            _cluesHeard++;
        }
        _phase = Phase.TurnOperator;
        Sys("OPERATOR INQUIRY — type a guess (wrong = 1 token) or PASS the line.");
    }

    // ── Operator (typed) turn ───────────────────────────────────────────────────

    void OperatorGuess(string guess)
    {
        if (_phase != Phase.TurnOperator) return;
        guess = guess.Trim();
        if (guess.Length == 0) return;

        Feed($"<color={GREEN}>OPERATOR ▷</color> guess: <b>{guess.ToUpperInvariant()}</b>");
        if (string.Equals(guess, _card.word, StringComparison.OrdinalIgnoreCase)) { CardSolved(); return; }

        if (_sentinelDeaf <= 0) _radioSentinel.Add($"PLAYER-2 (Operator) GUESSED: {guess} (incorrect)");
        if (BurnToken("that wrong guess")) return;
        StartCoroutine(SentinelTurn());
    }

    void OperatorPass()
    {
        if (_phase != Phase.TurnOperator) return;
        Feed($"<color={GREEN}>OPERATOR ▷</color> <color=#666>…holds the line.</color>");
        StartCoroutine(SentinelTurn());
    }

    // ── crew gadgets ────────────────────────────────────────────────────────────

    void SendDecoy(string fake)
    {
        if (_phase != Phase.TurnIntel || _decoyUsed) return;
        fake = fake.Trim();
        if (fake.Length == 0) return;
        _decoyUsed = true;
        _decoyTyped = "";
        // Costs your transmission turn AND a token — misdirection isn't free radio time.
        Feed($"<color={PURPLE}>DECOY ▷</color> <color=#666>{fake}   (only the wiretap hears this)</color>");
        if (BurnToken("that decoy broadcast")) return;
        _radioSentinel.Add($"PLAYER-1 (Intel): {fake}");
        _cluesHeard++;
        _phase = Phase.TurnOperator;
        Sys("OPERATOR INQUIRY — type a guess (wrong = 1 token) or PASS the line.");
    }

    IEnumerator TapTrace()
    {
        _tapTraceUsed = true;
        _tapTraceRunning = true;
        Sys("TAP-TRACE probe injected — forcing a diagnostic dump…");
        bool finalLayer = _layer == vaultLayers - 1;
        string tag = finalLayer && tagKnownOnFinalLayer ? _card.tag : null;
        string dump = null;
        yield return Sentinel.RevealSuspects(string.Join("\n", _radioSentinel), tag, _layer, t => dump = t);
        _tapTraceRunning = false;
        Feed($"<color={PURPLE}>▒ TAP-TRACE ▷ {dump}</color>");
    }

    // ── Sentinel turn ───────────────────────────────────────────────────────────

    IEnumerator SentinelTurn()
    {
        _phase = Phase.TurnSentinel;

        // Exposed after a wrong guess: it skips its inquiry entirely — this cycle was clean.
        if (_sentinelDeaf > 0)
        {
            _sentinelDeaf--;
            Feed($"<color={RED}>SENTINEL ▷</color> <color=#666>…static. Recalibrating. ({_sentinelDeaf} clean cycle{(_sentinelDeaf == 1 ? "" : "s")} left)</color>");
            _phase = Phase.TurnIntel;
            Sys("YOUR LINE — the wire is clean. Use it.");
            yield break;
        }

        bool finalLayer = _layer == vaultLayers - 1;
        bool mayGuess = _cluesHeard >= minCluesBeforeGuess;
        string tag = finalLayer && tagKnownOnFinalLayer ? _card.tag : null;

        bool guessed = false; string text = null;
        yield return Sentinel.TakeTurn(string.Join("\n", _radioSentinel), tag, _layer, mayGuess,
            (g, t) => { guessed = g; text = t; });
        if (_phase != Phase.TurnSentinel) yield break; // run was reset mid-think

        if (guessed)
        {
            Feed($"<color={RED}>SENTINEL ▷ Committing deduction: <b>{text}</b></color>");
            if (string.Equals(text, _card.word, StringComparison.OrdinalIgnoreCase)) { Breach(text); yield break; }

            // WRONG — it's exposed. Its wiretap goes dark and its miss leaks its reasoning.
            _sentinelDeaf = exposureTransmissions;
            _radioSentinel.Add($"SENTINEL GUESSED: {text} (incorrect — wiretap knocked offline)");
            Feed($"<color={GREEN}>▼ WRONG. SENTINEL EXPOSED — wiretap offline for {exposureTransmissions} cycles. Rush the real clue NOW.</color>");
            Sentinel.Speak($"{text}. …No. Recalibrating. This changes nothing.");
        }
        else
        {
            _radioSentinel.Add($"SENTINEL: {text}");
            Feed($"<color={RED}>SENTINEL ▷</color> {text}");
            Sentinel.Speak(text);
        }
        _phase = Phase.TurnIntel;
        Sys("YOUR LINE — speak a disguised clue.");
    }

    bool BurnToken(string what)
    {
        _tokens--;
        if (_tokens > 0) return false;
        Feed($"<color={RED}>RADIO BUDGET EXHAUSTED — {what} completed the trace.</color>");
        Breach(null);
        return true;
    }

    // ── Phase 2: breach + visual override ───────────────────────────────────────

    void Breach(string crackedWord)
    {
        _phase = Phase.Breach;
        string flip = crackedWord != null
            ? Sentinel.VillainFlipLine(crackedWord)
            : "Trace complete. You talk too much and say too little. Purging atmosphere.";
        if (crackedWord == null && Sentinel.Voice != null) Sentinel.Voice.pitch = Sentinel.overlordPitch;
        Feed($"<color={RED}>██ SYSTEM BREACH ██  SENTINEL ▷ {flip}</color>");
        Sentinel.Speak(flip);
        StartOverride();
    }

    void StartOverride()
    {
        _panels = BuildPanels(4);
        _targetPanel = UnityEngine.Random.Range(0, _panels.Length);
        _buttonOrder = Enumerable.Range(0, _panels.Length).OrderBy(_ => UnityEngine.Random.value).ToArray();
        _overrideEnds = Time.time + overrideSeconds;
        _phase = Phase.Override;
        Sys($"VISUAL OVERRIDE — match the marked image to its description within {overrideSeconds:0}s. The Sentinel will lie to you.");
        _gaslighting = StartCoroutine(GaslightLoop());
    }

    IEnumerator GaslightLoop()
    {
        while (_phase == Phase.Override)
        {
            yield return new WaitForSeconds(UnityEngine.Random.Range(7f, 11f));
            if (_phase != Phase.Override) yield break;
            int wrong;
            do { wrong = UnityEngine.Random.Range(0, _panels.Length); } while (wrong == _targetPanel);
            string line = null;
            yield return Sentinel.GaslightLine(_panels[wrong].desc, l => line = l);
            if (_phase != Phase.Override) yield break;
            Feed($"<color={RED}>SENTINEL ▷</color> {line}");
            Sentinel.Speak(line);
        }
    }

    void EndGaslighting()
    {
        if (_gaslighting != null) StopCoroutine(_gaslighting);
        _gaslighting = null;
    }

    void ClickOverride(int panelIndex)
    {
        EndGaslighting();
        if (panelIndex == _targetPanel)
        {
            Feed($"<color={GREEN}>██ NODE RESET — override accepted. The Sentinel withdraws, for now. ██</color>");
            Sentinel.ResetVoiceFont();
            Sentinel.Speak("Clever. Enjoy the borrowed time.");
            NextCard(); // same layer count — the cracked card is burned, a fresh one is drawn
        }
        else Lose($"wrong override — the Sentinel's lie worked ({_panels[panelIndex].desc})");
    }

    // Placeholder art: drop real horror PNGs into StreamingAssets/terminal-leak/images/
    // (filename becomes the description: melting_clock.png → [MELTING CLOCK]) and they
    // replace these procedural panels automatically.
    Panel[] BuildPanels(int count)
    {
        try
        {
            string dir = Path.Combine(Application.streamingAssetsPath, "terminal-leak", "images");
            if (Directory.Exists(dir))
            {
                var pngs = Directory.GetFiles(dir, "*.png").OrderBy(_ => UnityEngine.Random.value).Take(count).ToArray();
                if (pngs.Length >= count)
                    return pngs.Select(p =>
                    {
                        var t = new Texture2D(2, 2);
                        t.LoadImage(File.ReadAllBytes(p));
                        return new Panel { tex = t, desc = Path.GetFileNameWithoutExtension(p).Replace('_', ' ').ToUpperInvariant() };
                    }).ToArray();
            }
        }
        catch (Exception e) { Debug.LogWarning($"[TerminalLeak] image load failed: {e.Message}"); }

        var ids = Enumerable.Range(0, ArchetypeDescs.Length).OrderBy(_ => UnityEngine.Random.value).Take(count);
        return ids.Select(i => new Panel { tex = DrawArchetype(i), desc = ArchetypeDescs[i] }).ToArray();
    }

    static readonly string[] ArchetypeDescs =
    {
        "A RED ORB DROWNING IN STATIC", "A PALE FIGURE IN A DOORWAY", "A SPIRAL COLLAPSING INWARD",
        "THREE EYES IN A ROW", "A CLOCK MELTING DOWNWARD", "A GRID SHATTERING APART",
        "A LONE TREE ON A DEAD HILL", "A HAND REACHING FROM BELOW",
    };

    static Texture2D DrawArchetype(int id)
    {
        const int S = 128;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        var px = new Color[S * S];
        var rng = new System.Random(id * 7919 + 13);
        for (int i = 0; i < px.Length; i++)
        {
            float n = (float)rng.NextDouble() * 0.12f;
            px[i] = new Color(0.05f + n, 0.05f + n * 0.6f, 0.08f + n, 1f);
        }
        void Dot(int x, int y, Color c)
        {
            if (x >= 0 && x < S && y >= 0 && y < S) px[y * S + x] = c;
        }
        void Disc(int cx, int cy, int r, Color c)
        {
            for (int y = -r; y <= r; y++) for (int x = -r; x <= r; x++)
                if (x * x + y * y <= r * r) Dot(cx + x, cy + y, c);
        }
        void Box(int x0, int y0, int w, int h, Color c)
        {
            for (int y = y0; y < y0 + h; y++) for (int x = x0; x < x0 + w; x++) Dot(x, y, c);
        }
        Color red = new Color(0.97f, 0.44f, 0.44f), pale = new Color(0.85f, 0.85f, 0.9f),
              cyan = new Color(0.02f, 0.71f, 0.83f), purple = new Color(0.66f, 0.33f, 0.97f);
        switch (id)
        {
            case 0: Disc(64, 64, 26, red); for (int i = 0; i < 500; i++) Dot(rng.Next(S), rng.Next(S), new Color(1, 1, 1, 1) * (float)rng.NextDouble()); break;
            case 1: Box(44, 20, 40, 88, new Color(0.15f, 0.1f, 0.2f)); Box(60, 30, 8, 40, pale); Disc(64, 76, 6, pale); break;
            case 2: for (float t = 0; t < 22f; t += 0.02f) Dot(64 + (int)(t * 2.6f * Mathf.Cos(t)), 64 + (int)(t * 2.6f * Mathf.Sin(t)), cyan); break;
            case 3: for (int i = 0; i < 3; i++) { Disc(32 + i * 32, 64, 10, pale); Disc(32 + i * 32, 64, 4, Color.black); } break;
            case 4: Disc(64, 80, 24, pale); Disc(64, 80, 20, new Color(0.1f, 0.1f, 0.14f)); for (int i = 0; i < 5; i++) Box(50 + i * 7, 30 - i * 4, 3, 30 + i * 4, pale); break;
            case 5: for (int i = 0; i < 7; i++) { int off = rng.Next(-6, 7); Box(10 + i * 16 + off, 10, 2, 108, purple); Box(10, 10 + i * 16 - off, 108, 2, purple); } break;
            case 6: for (int x = 0; x < S; x++) { int h = 20 + (int)(14 * Mathf.Sin(x * 0.05f)); Box(x, 0, 1, h, new Color(0.12f, 0.1f, 0.14f)); } Box(62, 30, 4, 26, new Color(0.3f, 0.2f, 0.15f)); Disc(64, 66, 14, new Color(0.1f, 0.2f, 0.12f)); break;
            default: for (int i = 0; i < 5; i++) Box(38 + i * 12, 30 + rng.Next(0, 14), 6, 50, pale); Box(38, 10, 54, 24, pale); break;
        }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    // ── terminal rendering ──────────────────────────────────────────────────────

    void Sys(string s) => Feed($"<color=#8a8a9a>:: {s}</color>");

    void Feed(string s)
    {
        _feed.Add(s);
        if (_feed.Count > 300) _feed.RemoveAt(0);
        _stickBottom = true;
    }

    static string ExtractText(string json)
    {
        if (string.IsNullOrEmpty(json)) return "";
        var m = Regex.Match(json, "\"(?:text|partial)\"\\s*:\\s*\"([^\"]*)\"");
        return m.Success ? m.Groups[1].Value : json;
    }

    static bool ContainsWord(string text, string word) =>
        Regex.IsMatch(text, $@"\b{Regex.Escape(word)}\b", RegexOptions.IgnoreCase);

    IEnumerator KickMicrophone()
    {
        for (int i = 0; i < 30 && !_micStarted; i++)
        {
            yield return new WaitForSeconds(1f);
            var vp = Vosk != null ? Vosk.VoiceProcessor : null;
            if (vp != null && vp.IsRecording) { _micStarted = true; break; }
            try { Vosk.StartRecordingManual(); } catch { }
        }
    }

    void OnGUI()
    {
        float k = Mathf.Max(1f, Screen.height / 720f);
        int f = Mathf.RoundToInt(15 * k);
        var rich = new GUIStyle(GUI.skin.label) { fontSize = f, richText = true, wordWrap = true };

        GUI.color = BG;
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = Color.white;

        GUILayout.BeginArea(new Rect(16 * k, 10 * k, Screen.width - 32 * k, Screen.height - 20 * k));

        // header
        string ears = Whisper != null && Whisper.IsReady ? $"<color={GREEN}>whisper</color>" : "<color=#facc15>whisper booting…</color>";
        string brain = Llm != null && Llm.IsReady ? $"<color={GREEN}>sentinel online</color>" : "<color=#facc15>sentinel booting…</color>";
        string voice = PlayerMask != null && PlayerMask.Available ? $"<color={GREEN}>masks online</color>" : "<color=#f87171>no piper</color>";
        string turn =
            _phase == Phase.TurnIntel ? $"<color={CYAN}>◤ INTEL — SPEAK</color>" :
            _phase == Phase.TurnOperator ? $"<color={GREEN}>◤ OPERATOR — TYPE / PASS</color>" :
            _phase == Phase.TurnSentinel ? $"<color={RED}>◤ SENTINEL — parsing intercept…</color>" : "";
        GUILayout.Label($"<color={PURPLE}><b>▚ TERMINAL LEAK</b></color>  <color={CYAN}>│ deep-grid uplink │</color>  " +
                        $"layer <b>{Mathf.Min(_layer + 1, vaultLayers)}/{vaultLayers}</b>  " +
                        $"radio <b>{new string('█', Mathf.Max(0, _tokens))}{new string('░', Mathf.Max(0, tokensPerCard - _tokens))}</b>  " +
                        $"{turn}   {ears} · {brain} · {voice}", rich);

        // feed
        _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
        foreach (var line in _feed) GUILayout.Label(line, rich);
        if (!string.IsNullOrEmpty(_partial) || !string.IsNullOrEmpty(_pendingText))
            GUILayout.Label($"<color=#666>INTEL (composing) ▷ {(_pendingText + " " + _partial).Trim()}▌</color>", rich);
        if (_stickBottom && Event.current.type == EventType.Repaint) { _scroll.y = float.MaxValue; _stickBottom = false; }
        GUILayout.EndScrollView();

        if (_phase == Phase.Override) DrawOverride(k, f, rich);
        if (_phase == Phase.TurnIntel || _phase == Phase.TurnOperator) DrawGadgets(k, f);

        // operator input row — only live on the Operator's turn (strict turn discipline)
        if (_phase == Phase.TurnOperator)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<color={GREEN}>OPERATOR GUESS ▷</color>", rich, GUILayout.ExpandWidth(false));
            GUI.SetNextControlName("guess");
            _typed = GUILayout.TextField(_typed, new GUIStyle(GUI.skin.textField) { fontSize = f }, GUILayout.ExpandWidth(true));
            bool submit = GUILayout.Button("COMMIT", new GUIStyle(GUI.skin.button) { fontSize = f }, GUILayout.Width(110 * k)) ||
                          (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return &&
                           GUI.GetNameOfFocusedControl() == "guess");
            bool pass = GUILayout.Button("PASS ▷", new GUIStyle(GUI.skin.button) { fontSize = f }, GUILayout.Width(90 * k));
            GUILayout.EndHorizontal();
            if (submit && !string.IsNullOrWhiteSpace(_typed)) { OperatorGuess(_typed); _typed = ""; }
            else if (pass) { _typed = ""; OperatorPass(); }
        }
        GUILayout.EndArea();
    }

    void DrawGadgets(float k, int f)
    {
        var btn = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(f * 0.85f) };
        GUILayout.BeginHorizontal();
        if (_phase == Phase.TurnIntel)
        {
            GUI.enabled = !_jammerUsed;
            if (GUILayout.Button(_jammerArmed ? "▶ JAMMER ARMED — next clue is clean" : "JAMMER (1/card)", btn, GUILayout.Width(250 * k)))
            {
                _jammerUsed = true;
                _jammerArmed = true;
                Sys("JAMMER armed — your NEXT transmission never reaches the wiretap. Spend it on THE clue.");
            }
            // A decoy needs a listener — pointless while the wiretap is jammed or knocked offline.
            GUI.enabled = !_decoyUsed && _sentinelDeaf == 0 && !_jammerArmed;
            _decoyTyped = GUILayout.TextField(_decoyTyped, new GUIStyle(GUI.skin.textField) { fontSize = btn.fontSize }, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("DECOY ▷ (1/card, 1 token)", btn, GUILayout.Width(210 * k)) && !string.IsNullOrWhiteSpace(_decoyTyped))
                SendDecoy(_decoyTyped);
            GUI.enabled = true;
        }
        GUI.enabled = !_tapTraceUsed && !_tapTraceRunning;
        if (GUILayout.Button(_tapTraceRunning ? "TAP-TRACE running…" : "TAP-TRACE (1/run)", btn, GUILayout.Width(180 * k)))
            StartCoroutine(TapTrace());
        GUI.enabled = true;
        GUILayout.EndHorizontal();
    }

    void DrawOverride(float k, int f, GUIStyle rich)
    {
        float left = Time.time > _overrideEnds ? 0 : _overrideEnds - Time.time;
        GUILayout.Label($"<color={RED}><b>ASPHYXIATION IN {left:00.0}s</b></color>  — click the description matching the <color={CYAN}>marked</color> image", rich);
        GUILayout.BeginHorizontal();
        float size = Mathf.Min(150 * k, Screen.width / 5f);
        for (int i = 0; i < _panels.Length; i++)
        {
            GUILayout.BeginVertical(GUILayout.Width(size));
            var r = GUILayoutUtility.GetRect(size, size);
            if (i == _targetPanel) // solo stand-in for the Operator's separate screen
            {
                GUI.color = new Color(0.02f, 0.71f, 0.83f);
                GUI.DrawTexture(new Rect(r.x - 4, r.y - 4, r.width + 8, r.height + 8), Texture2D.whiteTexture);
                GUI.color = Color.white;
            }
            GUI.DrawTexture(r, _panels[i].tex, ScaleMode.ScaleToFit);
            GUILayout.EndVertical();
        }
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        foreach (int idx in _buttonOrder)
            if (GUILayout.Button($"[{_panels[idx].desc}]", new GUIStyle(GUI.skin.button) { fontSize = f, wordWrap = true },
                GUILayout.Width((Screen.width - 64 * k) / _panels.Length)))
                ClickOverride(idx);
        GUILayout.EndHorizontal();
    }
}
