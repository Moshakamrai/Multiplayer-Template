using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// Drop on a persistent GameObject in Tutorial.unity.
/// Requires a VoiceProcessor on the same GameObject.
/// Drag the VoskSpeechToText component into the Inspector — set AutoStart = true on it.
/// Do NOT start VoiceProcessor manually; VoskSpeechToText owns it.
[RequireComponent(typeof(VoiceProcessor))]
public class TutorialManager : MonoBehaviour
{
    public static TutorialManager Instance { get; private set; }

    // ── Stages ─────────────────────────────────────────────────────────────
    public enum Stage
    {
        VoiceCheck      = 0,
        ShadowBag       = 1,
        TimingOnly      = 2,
        CommandsTiming  = 3,
        SlowRhythm      = 4,
        ComboCards      = 5,
    }
    public Stage CurrentStage { get; private set; } = Stage.VoiceCheck;

    // ── Inspector ──────────────────────────────────────────────────────────
    [Header("Stage 0 — Voice Check")]
    [Range(0.05f, 0.5f)] public float micThreshold      = 0.15f;
    [Range(0.5f,  3f)]   public float holdToPassSeconds = 1.5f;

    [Header("Stage 2 — Timing Only")]
    public AudioSource tutorialAudioSource;
    public AudioClip   stage2BeatClip;
    [Range(0.5f, 4f)]   public float stage2BeatInterval = 2f;
    [Range(0.02f, 0.2f)] public float excellentWindow   = 0.08f;
    [Range(0.1f, 0.4f)]  public float goodWindow        = 0.22f;
    public int hitsToPassStage2 = 5;

    [Header("Music")]
    [Tooltip("Background music that plays through the entire tutorial.")]
    public AudioSource musicSource;
    public AudioClip   tutorialMusic;
    [Range(0f, 1f)] public float musicVolume = 0.45f;

    [Header("Stage 3 — Commands + Timing")]
    public AudioClip stage3BeatClip;   // optional override; falls back to stage2BeatClip
    [Range(0.5f, 8f)] public float stage3BeatInterval = 4.0f;
    public int hitsToPassStage3 = 8;

    [Header("Stage 3 — Character")]
    [Tooltip("Animator on a non-networked character placed in the Tutorial scene.")]
    public Animator tutorialPlayerAnimator;

    [Header("Voice (shared)")]
    [Tooltip("Set AutoStart = true on this component. Do not manually start VoiceProcessor.")]
    public VoskSpeechToText voskInstance;

    [Header("Scene")]
    public string menuSceneName = "Menu";

    // ── Shared runtime ─────────────────────────────────────────────────────
    private VoiceProcessor _vp;
    private Texture2D      _px;
    private GUIStyle       _headStyle;
    private GUIStyle       _bodyStyle;
    private GUIStyle       _cardStyle;

    // ── Stage 0 runtime ────────────────────────────────────────────────────
    private float _timeAboveThreshold = 0f;
    private bool  _stage0Passed       = false;
    private float _stage0SuccessTimer = 0f;
    private const float STAGE0_LINGER = 2f;

    // ── Stage 2 runtime ────────────────────────────────────────────────────
    private float  _s2_elapsed          = 0f;
    private float  _s2_nextBeat         = 0f;
    private float  _s2_lastSpikeElapsed = -999f;
    private int    _s2_hitCount         = 0;
    private int    _s2_totalBeats       = 0;
    private string _s2_lastGrade        = "";
    private float  _s2_gradeFade        = 0f;
    private float  _s2_beatFlash        = 0f;
    private bool   _s2_complete         = false;
    private float  _s2_successTimer     = 0f;
    private const float STAGE2_LINGER   = 2f;
    private const float GRADE_FADE_DUR  = 1.2f;
    private const float BEAT_FLASH_DUR  = 0.18f;
    private const int   MAX_S2_BEATS    = 12;

    // ── Stage 3 runtime ────────────────────────────────────────────────────
    private float  _s3_elapsed              = 0f;
    private float  _s3_nextBeat             = 0f;
    private float  _s3_lastBeatTime         = -999f;
    private float  _s3_lastSpikeElapsed     = -999f;
    private bool   _s3_spikeLockedThisCycle = false;
    private bool   _s3_cmdDetectedThisCycle = false;
    private int    _s3_orderPos             = 0;
    private int[]  _s3_cmdOrder;
    private int    _s3_hitCount             = 0;
    private int    _s3_totalBeats           = 0;
    private string _s3_lastGrade            = "";
    private float  _s3_gradeFade            = 0f;
    private float  _s3_beatFlash            = 0f;
    private bool   _s3_complete             = false;
    private float  _s3_successTimer         = 0f;
    private const int   MAX_S3_BEATS        = 18;
    private const float STAGE3_LINGER       = 2f;
    private const float S3_DEAD_ZONE        = 0.2f;

    // ── Intro / tutorial popup system ─────────────────────────────────────
    private int  _s2_introStep   = 0;
    private bool _s2_introActive = true;
    private int  _s3_introStep   = 0;
    private bool _s3_introActive = true;
    private const int S2_INTRO_STEPS = 3;
    private const int S3_INTRO_STEPS = 5;

    // ── Stage 1 runtime ────────────────────────────────────────────────────
    private static readonly string[] S1_LABELS = { "PUNCH", "BLAST", "HOOK", "BLOCK", "CAGE", "BOOM" };

    // Animation states matching each S1_LABELS entry — used in Stage 3
    private static readonly string[] S3_ANIM_STATES =
        { "Jab", "Cross", "Hook", "Block", "Parry", "UnbreakablePunch" };

    private static readonly string[][] S1_WORDS =
    {
        new[] { "punch", "jab" },
        new[] { "blast", "last", "fast", "cast" },
        new[] { "hook" },
        new[] { "block", "guard" },
        new[] { "cage", "page", "engage" },
        new[] { "boom", "room", "doom" },
    };

    private bool[]  _cmdLearned;
    private float[] _cmdFlash;
    private int   _cmdLearnedCount   = 0;
    private bool  _stage1Complete    = false;
    private float _stage1SuccessTimer = 0f;
    private const float STAGE1_LINGER  = 2.5f;
    private const float FLASH_DURATION = 0.45f;

    // ── Lifecycle ──────────────────────────────────────────────────────────
    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        _vp         = GetComponent<VoiceProcessor>();
        _cmdLearned = new bool[S1_LABELS.Length];
        _cmdFlash   = new float[S1_LABELS.Length];
    }

    void Start()
    {
        // Explicitly kick off Vosk — safe to call even if AutoStart already did it
        // (StartVoskStt guards against double-init internally).
        // Do NOT call _vp.StartRecording() here — Vosk must own that call so its
        // ThreadedWorkCoroutine starts correctly.
        if (voskInstance != null)
            voskInstance.StartVoskStt();
        else
            Debug.LogError("[TutorialManager] VoskSpeechToText not assigned in Inspector.");

        if (musicSource != null && tutorialMusic != null)
        {
            musicSource.clip   = tutorialMusic;
            musicSource.volume = musicVolume;
            musicSource.loop   = true;
            musicSource.Play();
        }

        EnterStage(Stage.VoiceCheck);
    }

    void Update()
    {
        bool introActive = (CurrentStage == Stage.TimingOnly      && _s2_introActive)
                        || (CurrentStage == Stage.CommandsTiming  && _s3_introActive);
        if (Input.GetKeyDown(KeyCode.Space))
        {
            if (introActive) AdvanceIntro();
            else             SkipCurrentStage();
        }

        switch (CurrentStage)
        {
            case Stage.VoiceCheck:      UpdateStage0(); break;
            case Stage.ShadowBag:       UpdateStage1(); break;
            case Stage.TimingOnly:      UpdateStage2(); break;
            case Stage.CommandsTiming:  UpdateStage3(); break;
        }
    }

    private void AdvanceIntro()
    {
        if (CurrentStage == Stage.TimingOnly)
        {
            _s2_introStep++;
            if (_s2_introStep >= S2_INTRO_STEPS)
            {
                _s2_introActive = false;
                _s2_nextBeat    = _s2_elapsed + stage2BeatInterval;
            }
        }
        else if (CurrentStage == Stage.CommandsTiming)
        {
            _s3_introStep++;
            if (_s3_introStep >= S3_INTRO_STEPS)
            {
                _s3_introActive = false;
                _s3_nextBeat    = _s3_elapsed + stage3BeatInterval;
            }
        }
    }

    private void SkipCurrentStage()
    {
        Stage next = CurrentStage + 1;
        if (next > Stage.ComboCards) return;
        AdvanceTo(next);
    }

    // ── Stage transitions ──────────────────────────────────────────────────
    private void EnterStage(Stage s)
    {
        CurrentStage = s;

        switch (s)
        {
            case Stage.VoiceCheck:
                _timeAboveThreshold = 0f;
                _stage0Passed       = false;
                _stage0SuccessTimer = 0f;
                break;

            case Stage.ShadowBag:
                for (int i = 0; i < S1_LABELS.Length; i++) { _cmdLearned[i] = false; _cmdFlash[i] = 0f; }
                _cmdLearnedCount    = 0;
                _stage1Complete     = false;
                _stage1SuccessTimer = 0f;

                if (voskInstance != null)
                    // OnPartialResult is fired from VoskSpeechToText.Update — main thread, no lock needed.
                    voskInstance.OnPartialResult += Stage1HandlePartial;
                else
                    Debug.LogWarning("[TutorialManager] VoskSpeechToText not assigned — voice detection won't work in Stage 1.");
                break;

            case Stage.TimingOnly:
                _s2_elapsed          = 0f;
                _s2_nextBeat         = stage2BeatInterval;
                _s2_lastSpikeElapsed = -999f;
                _s2_hitCount         = 0;
                _s2_totalBeats       = 0;
                _s2_lastGrade        = "";
                _s2_gradeFade        = 0f;
                _s2_beatFlash        = 0f;
                _s2_complete         = false;
                _s2_successTimer     = 0f;
                _s2_introStep        = 0;
                _s2_introActive      = true;
                break;

            case Stage.CommandsTiming:
                _s3_elapsed              = 0f;
                _s3_nextBeat             = stage3BeatInterval;
                _s3_lastBeatTime         = -stage3BeatInterval;
                _s3_lastSpikeElapsed     = -999f;
                _s3_spikeLockedThisCycle = false;
                _s3_cmdDetectedThisCycle = false;
                _s3_orderPos             = 0;
                _s3_cmdOrder             = ShuffleRange(S1_LABELS.Length);
                _s3_hitCount             = 0;
                _s3_totalBeats           = 0;
                _s3_lastGrade            = "";
                _s3_gradeFade            = 0f;
                _s3_beatFlash            = 0f;
                _s3_complete             = false;
                _s3_successTimer         = 0f;
                _s3_introStep        = 0;
                _s3_introActive      = true;
                if (voskInstance != null)
                    voskInstance.OnPartialResult += Stage3HandlePartial;
                else
                    Debug.LogWarning("[TutorialManager] VoskSpeechToText not assigned — Stage 3 won't work.");
                break;
        }
    }

    private void ExitStage(Stage s)
    {
        if (voskInstance == null) return;
        if (s == Stage.ShadowBag)       voskInstance.OnPartialResult -= Stage1HandlePartial;
        if (s == Stage.CommandsTiming)  voskInstance.OnPartialResult -= Stage3HandlePartial;
    }

    private void AdvanceTo(Stage next)
    {
        ExitStage(CurrentStage);
        EnterStage(next);
    }

    // ══════════════════════════════════════════════════════════════════════
    // STAGE 0 — Voice Check
    // ══════════════════════════════════════════════════════════════════════
    private void UpdateStage0()
    {
        if (!_vp.IsRecording) return;

        if (_stage0Passed)
        {
            _stage0SuccessTimer += Time.deltaTime;
            if (_stage0SuccessTimer >= STAGE0_LINGER)
                AdvanceTo(Stage.ShadowBag);
            return;
        }

        float vol = _vp.CurrentRawVolume;
        if (vol >= micThreshold)
        {
            _timeAboveThreshold += Time.deltaTime;
            if (_timeAboveThreshold >= holdToPassSeconds)
                _stage0Passed = true;
        }
        else
            _timeAboveThreshold = Mathf.Max(0f, _timeAboveThreshold - Time.deltaTime * 1.5f);
    }

    // ══════════════════════════════════════════════════════════════════════
    // STAGE 1 — Shadow Bag
    // ══════════════════════════════════════════════════════════════════════
    private void Stage1HandlePartial(string json)
    {
        if (_stage1Complete) return;

        string text = ParsePartial(json).ToLower().Trim();
        if (string.IsNullOrEmpty(text)) return;

        foreach (string word in text.Split(' '))
        {
            if (string.IsNullOrEmpty(word)) continue;
            for (int i = 0; i < S1_WORDS.Length; i++)
            {
                if (_cmdLearned[i]) continue;
                if (MatchesCommand(word, S1_WORDS[i]))
                {
                    _cmdLearned[i]  = true;
                    _cmdFlash[i]    = FLASH_DURATION;
                    _cmdLearnedCount++;
                    break;
                }
            }
        }
    }

    private void UpdateStage1()
    {
        for (int i = 0; i < _cmdFlash.Length; i++)
            if (_cmdFlash[i] > 0f) _cmdFlash[i] -= Time.deltaTime;

        if (_stage1Complete)
        {
            _stage1SuccessTimer += Time.deltaTime;
            if (_stage1SuccessTimer >= STAGE1_LINGER)
                AdvanceTo(Stage.TimingOnly);
            return;
        }

        if (_cmdLearnedCount >= S1_LABELS.Length)
            _stage1Complete = true;
    }

    // ══════════════════════════════════════════════════════════════════════
    // STAGE 2 — Timing Only
    // ══════════════════════════════════════════════════════════════════════
    private void UpdateStage2()
    {
        if (_s2_introActive) return;

        if (_s2_complete)
        {
            _s2_successTimer += Time.deltaTime;
            if (_s2_successTimer >= STAGE2_LINGER) AdvanceTo(Stage.CommandsTiming);
            return;
        }

        _s2_elapsed += Time.deltaTime;
        if (_s2_gradeFade > 0f) _s2_gradeFade -= Time.deltaTime;
        if (_s2_beatFlash > 0f) _s2_beatFlash -= Time.deltaTime;

        if (_vp.CurrentRawVolume >= micThreshold)
            _s2_lastSpikeElapsed = _s2_elapsed;

        if (_s2_elapsed >= _s2_nextBeat)
        {
            PlayBeat();
            _s2_beatFlash = BEAT_FLASH_DUR;
            _s2_totalBeats++;

            // Spike must land within goodWindow before the beat (or a tiny bit after)
            bool spikeInWindow = _s2_lastSpikeElapsed >= _s2_nextBeat - goodWindow
                              && _s2_lastSpikeElapsed <= _s2_nextBeat + goodWindow * 0.4f;

            if (spikeInWindow)
            {
                float absDelta = Mathf.Abs(_s2_lastSpikeElapsed - _s2_nextBeat);
                _s2_lastGrade = absDelta <= excellentWindow ? "EXCELLENT!" : "GOOD";
                _s2_hitCount++;
            }
            else
            {
                _s2_lastGrade = "MISS";
            }

            _s2_gradeFade = GRADE_FADE_DUR;
            _s2_nextBeat += stage2BeatInterval;

            if (_s2_hitCount >= hitsToPassStage2 || _s2_totalBeats >= MAX_S2_BEATS)
                _s2_complete = true;
        }
    }

    private void PlayBeat(AudioClip clip = null)
    {
        AudioClip c = clip != null ? clip : stage2BeatClip;
        if (tutorialAudioSource != null && c != null)
            tutorialAudioSource.PlayOneShot(c);
    }

    // ══════════════════════════════════════════════════════════════════════
    // OnGUI
    // ══════════════════════════════════════════════════════════════════════
    void OnGUI()
    {
        EnsureStyles();
        switch (CurrentStage)
        {
            case Stage.VoiceCheck:      DrawStage0(); break;
            case Stage.ShadowBag:       DrawStage1(); break;
            case Stage.TimingOnly:      DrawStage2(); break;
            case Stage.CommandsTiming:  DrawStage3(); break;
        }
    }

    // ── Stage 0 GUI ────────────────────────────────────────────────────────
    private void DrawStage0()
    {
        float sw = Screen.width, sh = Screen.height;

        GUI.color = new Color(0f, 0f, 0f, 0.82f);
        GUI.DrawTexture(new Rect(0, 0, sw, sh), _px);
        GUI.color = Color.white;

        _headStyle.fontSize = Mathf.Clamp((int)(sw / 14f), 36, 72);

        if (!_vp.IsRecording)
        {
            GUI.color = new Color(1f, 0.8f, 0.2f);
            GUI.Label(new Rect(0, sh * 0.22f, sw, 90), "LOADING MIC...", _headStyle);
            GUI.color = Color.white;
            _bodyStyle.fontSize = Mathf.Clamp((int)(sw / 44f), 16, 28);
            GUI.color = new Color(1f, 1f, 1f, 0.5f);
            GUI.Label(new Rect(0, sh * 0.34f, sw, 44), "SETTING UP VOICE RECOGNITION", _bodyStyle);
            GUI.color = Color.white;
            DrawBackButton();
            return;
        }

        string title    = _stage0Passed ? "MIC IS READY!" : "VOICE CHECK";
        Color  titleCol = _stage0Passed ? new Color(0.2f, 1f, 0.4f) : Color.white;
        GUI.color = titleCol;
        GUI.Label(new Rect(0, sh * 0.18f, sw, 90), title, _headStyle);
        GUI.color = Color.white;

        _bodyStyle.fontSize = Mathf.Clamp((int)(sw / 40f), 18, 32);
        string instruction = _stage0Passed ? "GREAT — MOVING ON..." : "SHOUT ANYTHING TO TEST YOUR MIC";
        GUI.color = new Color(1f, 1f, 1f, 0.75f);
        GUI.Label(new Rect(0, sh * 0.30f, sw, 50), instruction, _bodyStyle);
        GUI.color = Color.white;

        float barW = sw * 0.55f, barH = 34f;
        float barX = sw * 0.5f - barW * 0.5f, barY = sh * 0.42f;

        GUI.color = new Color(1f, 1f, 1f, 0.15f);
        GUI.DrawTexture(new Rect(barX, barY, barW, barH), _px);

        float fillFrac = Mathf.Clamp01(_vp.CurrentRawVolume / micThreshold);
        Color fillCol  = _stage0Passed
            ? new Color(0.2f, 1f, 0.4f, 0.9f)
            : Color.Lerp(new Color(0.9f, 0.3f, 0.1f, 0.9f), new Color(0.2f, 0.9f, 0.3f, 0.9f), fillFrac);
        GUI.color = fillCol;
        GUI.DrawTexture(new Rect(barX, barY, barW * fillFrac, barH), _px);

        GUI.color = new Color(1f, 1f, 1f, 0.4f);
        GUI.DrawTexture(new Rect(barX,             barY,        barW, 2f), _px);
        GUI.DrawTexture(new Rect(barX,             barY + barH, barW, 2f), _px);
        GUI.DrawTexture(new Rect(barX,             barY,        2f, barH), _px);
        GUI.DrawTexture(new Rect(barX + barW - 2f, barY,        2f, barH), _px);
        GUI.color = Color.white;

        if (!_stage0Passed)
        {
            float holdFrac = Mathf.Clamp01(_timeAboveThreshold / holdToPassSeconds);
            float holdY    = barY + barH + 10f;
            GUI.color = new Color(1f, 1f, 1f, 0.12f);
            GUI.DrawTexture(new Rect(barX, holdY, barW, 8f), _px);
            GUI.color = new Color(0.2f, 0.85f, 1f, 0.9f);
            GUI.DrawTexture(new Rect(barX, holdY, barW * holdFrac, 8f), _px);
            GUI.color = Color.white;

            _bodyStyle.fontSize = Mathf.Clamp((int)(sw / 55f), 14, 22);
            GUI.color = new Color(1f, 1f, 1f, 0.5f);
            GUI.Label(new Rect(barX, holdY + 14f, barW, 30f), "HOLD TO CONFIRM", _bodyStyle);
            GUI.color = Color.white;
        }

        DrawBackButton();
    }

    // ── Stage 1 GUI ────────────────────────────────────────────────────────
    private void DrawStage1()
    {
        float sw = Screen.width, sh = Screen.height;

        GUI.color = new Color(0f, 0f, 0f, 0.72f);
        GUI.DrawTexture(new Rect(0, 0, sw, sh), _px);
        GUI.color = Color.white;

        _headStyle.fontSize = Mathf.Clamp((int)(sw / 16f), 30, 64);
        string title    = _stage1Complete ? "MOVE SET LEARNED!" : "SHADOW BAG";
        Color  titleCol = _stage1Complete ? new Color(0.2f, 1f, 0.4f) : Color.white;
        GUI.color = titleCol;
        GUI.Label(new Rect(0, sh * 0.06f, sw, 80), title, _headStyle);
        GUI.color = Color.white;

        _bodyStyle.fontSize = Mathf.Clamp((int)(sw / 44f), 16, 28);
        string sub = _stage1Complete
            ? "MOVING ON..."
            : $"SHOUT EACH COMMAND — NO TIMING NEEDED     {_cmdLearnedCount} / {S1_LABELS.Length}";
        GUI.color = new Color(1f, 1f, 1f, 0.75f);
        GUI.Label(new Rect(0, sh * 0.17f, sw, 44), sub, _bodyStyle);
        GUI.color = Color.white;

        const int COLS = 3;
        float cardW  = Mathf.Clamp(sw * 0.22f, 140f, 210f);
        float cardH  = Mathf.Clamp(sh * 0.14f,  60f,  90f);
        float gapX   = cardW * 0.18f;
        float gapY   = cardH * 0.25f;
        float gridW  = COLS * cardW + (COLS - 1) * gapX;
        float startX = sw * 0.5f - gridW * 0.5f;
        float startY = sh * 0.30f;

        _cardStyle.fontSize = Mathf.Clamp((int)(sw / 26f), 22, 46);

        for (int i = 0; i < S1_LABELS.Length; i++)
        {
            int   col  = i % COLS;
            int   row  = i / COLS;
            float cx   = startX + col * (cardW + gapX);
            float cy   = startY + row * (cardH + gapY);
            Rect  rect = new Rect(cx, cy, cardW, cardH);

            bool  learned = _cmdLearned[i];
            float flash   = _cmdFlash[i];

            Color bgCol = flash > 0f
                ? Color.Lerp(new Color(0.15f, 0.55f, 0.15f, 0.95f), new Color(1f, 0.85f, 0.1f, 0.95f), flash / FLASH_DURATION)
                : learned
                    ? new Color(0.1f,  0.45f, 0.1f,  0.88f)
                    : new Color(0.18f, 0.18f, 0.22f, 0.88f);

            GUI.color = bgCol;
            GUI.DrawTexture(rect, _px);

            float b         = 2f;
            Color borderCol = learned ? new Color(0.3f, 1f, 0.4f, 0.8f) : new Color(1f, 1f, 1f, 0.2f);
            GUI.color = borderCol;
            GUI.DrawTexture(new Rect(cx,         cy,           cardW, b),     _px);
            GUI.DrawTexture(new Rect(cx,         cy+cardH-b,   cardW, b),     _px);
            GUI.DrawTexture(new Rect(cx,         cy,           b,     cardH), _px);
            GUI.DrawTexture(new Rect(cx+cardW-b, cy,           b,     cardH), _px);

            GUI.color = learned ? new Color(0.5f, 1f, 0.6f) : new Color(1f, 1f, 1f, 0.6f);
            GUI.Label(rect, learned ? $"✓  {S1_LABELS[i]}" : S1_LABELS[i], _cardStyle);
        }

        GUI.color = Color.white;
        DrawBackButton();
    }

    // ── Stage 2 GUI ────────────────────────────────────────────────────────
    private void DrawStage2()
    {
        float sw = Screen.width, sh = Screen.height;

        GUI.color = new Color(0f, 0f, 0f, 0.82f);
        GUI.DrawTexture(new Rect(0, 0, sw, sh), _px);
        GUI.color = Color.white;

        // Title
        _headStyle.fontSize = Mathf.Clamp((int)(sw / 16f), 30, 64);
        GUI.color = _s2_complete ? new Color(0.2f, 1f, 0.4f) : Color.white;
        GUI.Label(new Rect(0, sh * 0.05f, sw, 70), _s2_complete ? "TIMING MASTERED!" : "TIMING ONLY", _headStyle);
        GUI.color = Color.white;

        // Sub-instruction
        _bodyStyle.fontSize = Mathf.Clamp((int)(sw / 52f), 13, 22);
        GUI.color = new Color(1f, 1f, 1f, 0.50f);
        GUI.Label(new Rect(0, sh * 0.14f, sw, 34), _s2_complete ? "MOVING ON..." : "SHOUT WHEN THE BAR REACHES THE GREEN ZONE", _bodyStyle);
        GUI.color = Color.white;

        // ── Timing state ──────────────────────────────────────────────────
        float beatFrac = Mathf.Clamp01((_s2_elapsed % stage2BeatInterval) / stage2BeatInterval);
        float goodFrac = Mathf.Clamp01(goodWindow     / stage2BeatInterval);
        float exFrac   = Mathf.Clamp01(excellentWindow / stage2BeatInterval);
        bool  inGood   = beatFrac >= (1f - goodFrac);
        bool  inEx     = beatFrac >= (1f - exFrac);
        float glow     = Mathf.Pow(beatFrac, 2.2f); // dim → bright as beat approaches
        float flashT   = _s2_beatFlash / BEAT_FLASH_DUR;

        // ── Card ──────────────────────────────────────────────────────────
        float cardW = Mathf.Clamp(sw * 0.38f, 260f, 430f);
        float cardH = Mathf.Clamp(sh * 0.29f, 150f, 210f);
        float cardX = sw * 0.5f - cardW * 0.5f;
        float cardY = sh * 0.26f;

        // Card background — shifts green inside zone, flashes white on beat
        Color cardBg = _s2_beatFlash > 0f
            ? Color.Lerp(new Color(0.12f, 0.12f, 0.16f, 0.95f), new Color(0.75f, 0.95f, 0.75f, 0.95f), flashT)
            : inGood
                ? Color.Lerp(new Color(0.12f, 0.12f, 0.16f, 0.95f), new Color(0.05f, 0.20f, 0.07f, 0.95f),
                             (beatFrac - (1f - goodFrac)) / Mathf.Max(goodFrac, 0.001f))
                : new Color(0.12f, 0.12f, 0.16f, 0.95f);
        GUI.color = cardBg;
        GUI.DrawTexture(new Rect(cardX, cardY, cardW, cardH), _px);

        // Card border — glows white→green as beat approaches, flashes on hit
        float bw = Mathf.Lerp(2f, 5f, glow);
        Color borderCol = inGood
            ? Color.Lerp(new Color(0.3f, 1f, 0.4f, 0.75f), new Color(0.55f, 1f, 0.55f, 1f),
                         (beatFrac - (1f - goodFrac)) / Mathf.Max(goodFrac, 0.001f))
            : Color.Lerp(new Color(1f, 1f, 1f, 0.15f), new Color(0.3f, 1f, 0.4f, 0.75f), glow);
        if (_s2_beatFlash > 0f) borderCol = Color.Lerp(borderCol, Color.white, flashT);
        GUI.color = borderCol;
        GUI.DrawTexture(new Rect(cardX,           cardY,              cardW, bw),    _px);
        GUI.DrawTexture(new Rect(cardX,           cardY + cardH - bw, cardW, bw),    _px);
        GUI.DrawTexture(new Rect(cardX,           cardY,              bw,    cardH), _px);
        GUI.DrawTexture(new Rect(cardX + cardW - bw, cardY,           bw,    cardH), _px);

        // "SHOUT!" label inside card (dims when waiting, lights up in zone)
        _cardStyle.fontSize = Mathf.Clamp((int)(sw / 20f), 26, 54);
        Color labelCol = inGood
            ? Color.Lerp(new Color(0.75f, 0.75f, 0.75f), new Color(0.35f, 1f, 0.45f),
                         (beatFrac - (1f - goodFrac)) / Mathf.Max(goodFrac, 0.001f))
            : Color.Lerp(new Color(0.35f, 0.35f, 0.35f), new Color(0.80f, 0.80f, 0.80f), glow);
        if (_s2_beatFlash > 0f) labelCol = Color.Lerp(labelCol, Color.white, flashT);
        GUI.color = labelCol;
        GUI.Label(new Rect(cardX, cardY, cardW, cardH * 0.52f), "SHOUT!", _cardStyle);
        GUI.color = Color.white;

        // ── Fill bar inside card ──────────────────────────────────────────
        float pad  = cardW * 0.07f;
        float barW = cardW - pad * 2f;
        float barH = Mathf.Clamp(cardH * 0.17f, 12f, 22f);
        float barX = cardX + pad;
        float barY = cardY + cardH * 0.65f;

        // Background
        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.DrawTexture(new Rect(barX, barY, barW, barH), _px);

        // Good zone (always visible)
        GUI.color = new Color(0.1f, 0.7f, 0.15f, 0.30f);
        GUI.DrawTexture(new Rect(barX + barW * (1f - goodFrac), barY, barW * goodFrac, barH), _px);

        // Excellent zone
        GUI.color = new Color(0.2f, 1f, 0.3f, 0.58f);
        GUI.DrawTexture(new Rect(barX + barW * (1f - exFrac), barY, barW * exFrac, barH), _px);

        // Moving fill — yellow → bright green as it enters zone
        Color fillCol = inGood
            ? Color.Lerp(new Color(1f, 0.85f, 0.1f, 0.92f), new Color(0.25f, 1f, 0.35f, 0.92f),
                         (beatFrac - (1f - goodFrac)) / Mathf.Max(goodFrac, 0.001f))
            : new Color(1f, 0.85f, 0.1f, 0.78f);
        if (_s2_beatFlash > 0f) fillCol = Color.Lerp(fillCol, Color.white, flashT * 0.85f);
        GUI.color = fillCol;
        GUI.DrawTexture(new Rect(barX, barY, barW * beatFrac, barH), _px);

        // Bar border
        GUI.color = new Color(1f, 1f, 1f, 0.20f);
        GUI.DrawTexture(new Rect(barX,        barY,        barW,  1.5f),  _px);
        GUI.DrawTexture(new Rect(barX,        barY + barH, barW,  1.5f),  _px);
        GUI.DrawTexture(new Rect(barX,        barY,        1.5f,  barH),  _px);
        GUI.DrawTexture(new Rect(barX + barW, barY,        1.5f,  barH),  _px);
        GUI.color = Color.white;

        // "SHOUT ZONE" micro-label above the green zone
        _bodyStyle.fontSize = Mathf.Clamp((int)(sw / 72f), 9, 14);
        GUI.color = new Color(0.3f, 1f, 0.4f, 0.65f);
        GUI.Label(new Rect(barX + barW * (1f - goodFrac), barY - 17f, barW * goodFrac, 17f), "SHOUT ZONE", _bodyStyle);
        GUI.color = Color.white;

        // ── State label below card ─────────────────────────────────────────
        string statusText;
        Color  statusCol;
        if (_s2_beatFlash > 0f)
        {
            statusText = _s2_lastGrade;
            statusCol  = _s2_lastGrade.StartsWith("EX") ? new Color(0.2f, 1f, 0.4f)
                       : _s2_lastGrade == "GOOD"        ? new Color(0.4f, 0.85f, 1f)
                       :                                   new Color(1f, 0.35f, 0.2f);
        }
        else if (inEx)
            { statusText = "NOW!";          statusCol = new Color(0.25f, 1f, 0.4f); }
        else if (inGood)
            { statusText = "SHOUT!";        statusCol = new Color(0.55f, 1f, 0.6f); }
        else if (beatFrac > 0.55f)
        {
            float ramp = (beatFrac - 0.55f) / 0.45f;
            statusText = "GET READY...";   statusCol = new Color(1f, Mathf.Lerp(0.7f, 0.95f, ramp), 0.2f, Mathf.Lerp(0.4f, 0.9f, ramp));
        }
        else
            { statusText = "WAIT...";       statusCol = new Color(1f, 1f, 1f, 0.22f); }

        float statusY = cardY + cardH + 14f;
        _bodyStyle.fontSize = Mathf.Clamp((int)(sw / 30f), 24, 42);
        GUI.color = statusCol;
        GUI.Label(new Rect(0, statusY, sw, 50f), statusText, _bodyStyle);
        GUI.color = Color.white;

        // ── Grade float (above card) ───────────────────────────────────────
        if (_s2_gradeFade > 0f && _s2_beatFlash <= 0f && !string.IsNullOrEmpty(_s2_lastGrade))
        {
            float alpha    = Mathf.Clamp01(_s2_gradeFade / GRADE_FADE_DUR);
            float rise     = (1f - alpha) * 28f;
            Color gradeCol = _s2_lastGrade.StartsWith("EX") ? new Color(0.2f, 1f, 0.4f, alpha)
                           : _s2_lastGrade == "GOOD"        ? new Color(0.4f, 0.85f, 1f, alpha)
                           :                                   new Color(1f, 0.35f, 0.2f, alpha);
            _headStyle.fontSize = Mathf.Clamp((int)(sw / 17f), 28, 58);
            GUI.color = gradeCol;
            GUI.Label(new Rect(0, cardY - 72f - rise, sw, 68f), _s2_lastGrade, _headStyle);
            GUI.color = Color.white;
        }

        // ── Beat dots ─────────────────────────────────────────────────────
        float dotsY = statusY + 56f;
        DrawBeatDots(cardX, dotsY, cardW, MAX_S2_BEATS, _s2_totalBeats);

        // ── Hit counter ───────────────────────────────────────────────────
        _bodyStyle.fontSize = Mathf.Clamp((int)(sw / 40f), 18, 30);
        GUI.color = new Color(1f, 1f, 1f, 0.78f);
        GUI.Label(new Rect(0, dotsY + 26f, sw, 40f), $"{_s2_hitCount} / {hitsToPassStage2} HITS", _bodyStyle);
        GUI.color = Color.white;

        // ── Stage 2 intro overlay ─────────────────────────────────────────
        if (_s2_introActive)
        {
            float cW = Mathf.Clamp(sw * 0.38f, 260f, 430f);
            float cH = Mathf.Clamp(sh * 0.29f, 150f, 210f);
            float cX = sw * 0.5f - cW * 0.5f, cY = sh * 0.26f;
            float pB = cW * 0.07f;
            float bX = cX + pB, bY = cY + cH * 0.65f;
            float bW = cW - pB * 2f, bH = Mathf.Clamp(cH * 0.17f, 12f, 22f);
            float gF = Mathf.Clamp01(goodWindow / stage2BeatInterval);
            bool  adv = false;
            switch (_s2_introStep)
            {
                case 0:
                    adv = DrawTutorialPopup(sw, sh, new Rect(0,0,0,0),
                        "STAGE 2 OF 5", "BEAT TIMING",
                        "Listen for the beat click and SHOUT at exactly the right moment.\nPerfect timing = EXCELLENT. Sloppy timing = MISS.");
                    break;
                case 1:
                    adv = DrawTutorialPopup(sw, sh, new Rect(cX, cY, cW, cH),
                        "THE CARD", "WATCH THE TIMING BAR",
                        "This bar fills left to right as the beat approaches. Get ready to shout when it enters the green zone on the right!");
                    break;
                case 2:
                    float zX = bX + bW * (1f - gF);
                    adv = DrawTutorialPopup(sw, sh, new Rect(zX - 10f, bY - 22f, bW * gF + 10f, bH + 44f),
                        "AIM FOR THIS", "THE SHOUT ZONE",
                        "SHOUT when the bar hits the green!\n· EXCELLENT — bright green center\n· GOOD — outer green area\n· MISS — too early or too late");
                    break;
            }
            if (adv) AdvanceIntro();
            return;
        }

        DrawBackButton();
    }

    private void DrawBeatDots(float x, float y, float w, int total, int done)
    {
        float spacing = w / total;
        float dotSize = Mathf.Clamp(spacing - 4f, 5f, 14f);
        for (int i = 0; i < total; i++)
        {
            GUI.color = i < done ? new Color(0.3f, 0.9f, 0.4f, 0.8f) : new Color(1f, 1f, 1f, 0.16f);
            GUI.DrawTexture(new Rect(x + i * spacing, y, dotSize, dotSize), _px);
        }
        GUI.color = Color.white;
    }

    // ══════════════════════════════════════════════════════════════════════
    // STAGE 3 — Commands + Timing
    // ══════════════════════════════════════════════════════════════════════
    private void Stage3HandlePartial(string json)
    {
        if (_s3_complete) return;
        string text = ParsePartial(json).ToLower().Trim();
        if (string.IsNullOrEmpty(text)) return;
        int cmdIdx = _s3_cmdOrder[_s3_orderPos % _s3_cmdOrder.Length];
        foreach (string word in text.Split(' '))
        {
            if (string.IsNullOrEmpty(word)) continue;
            if (MatchesCommand(word, S1_WORDS[cmdIdx]))
            {
                _s3_cmdDetectedThisCycle = true;
                TriggerCommandAnimation(cmdIdx);
                break;
            }
        }
    }

    private void TriggerCommandAnimation(int cmdIdx)
    {
        if (tutorialPlayerAnimator == null) return;
        tutorialPlayerAnimator.Play(S3_ANIM_STATES[cmdIdx], 0, 0f);
    }

    private void UpdateStage3()
    {
        if (_s3_introActive) return;

        if (_s3_complete)
        {
            _s3_successTimer += Time.deltaTime;
            if (_s3_successTimer >= STAGE3_LINGER) AdvanceTo(Stage.SlowRhythm);
            return;
        }

        _s3_elapsed += Time.deltaTime;
        if (_s3_gradeFade > 0f) _s3_gradeFade -= Time.deltaTime;
        if (_s3_beatFlash > 0f) _s3_beatFlash -= Time.deltaTime;

        // Only record timing spike inside the shout zone (last 0.5s before beat),
        // and after the dead zone following the previous beat.
        float timeSinceLastBeat = _s3_elapsed - _s3_lastBeatTime;
        float timeToNextBeat    = _s3_nextBeat - _s3_elapsed;
        bool  inShoutZone       = timeToNextBeat > 0f && timeToNextBeat <= 0.5f;
        if (timeSinceLastBeat >= S3_DEAD_ZONE && inShoutZone
            && !_s3_spikeLockedThisCycle && _vp.CurrentRawVolume >= micThreshold)
        {
            _s3_lastSpikeElapsed     = _s3_elapsed;
            _s3_spikeLockedThisCycle = true;
        }

        if (_s3_elapsed < _s3_nextBeat) return;

        // ── Beat fires ────────────────────────────────────────────────────
        PlayBeat(stage3BeatClip);
        _s3_beatFlash  = BEAT_FLASH_DUR;
        _s3_lastBeatTime = _s3_nextBeat;
        _s3_totalBeats++;

        bool spikeInWindow = _s3_lastSpikeElapsed >= _s3_nextBeat - goodWindow
                          && _s3_lastSpikeElapsed <= _s3_nextBeat + goodWindow * 0.4f;
        bool cmdOk = _s3_cmdDetectedThisCycle;

        if (cmdOk && spikeInWindow)
        {
            float absDelta = Mathf.Abs(_s3_lastSpikeElapsed - _s3_nextBeat);
            _s3_lastGrade = absDelta <= excellentWindow ? "EXCELLENT!" : "GOOD";
            _s3_hitCount++;
        }
        else if (cmdOk)   _s3_lastGrade = "WRONG TIME!";
        else if (spikeInWindow) _s3_lastGrade = "WRONG WORD!";
        else              _s3_lastGrade = "MISS";

        _s3_gradeFade = GRADE_FADE_DUR;
        _s3_nextBeat += stage3BeatInterval;

        // Reset per-cycle state
        _s3_spikeLockedThisCycle = false;
        _s3_cmdDetectedThisCycle = false;
        _s3_lastSpikeElapsed     = -999f;

        // Advance command — reshuffle when all 6 have been shown
        _s3_orderPos++;
        if (_s3_orderPos % S1_LABELS.Length == 0)
            _s3_cmdOrder = ShuffleRange(S1_LABELS.Length);

        if (_s3_hitCount >= hitsToPassStage3 || _s3_totalBeats >= MAX_S3_BEATS)
            _s3_complete = true;
    }

    // ── Stage 3 GUI ────────────────────────────────────────────────────────
    private void DrawStage3()
    {
        float sw = Screen.width, sh = Screen.height;

        // Lighter overlay so the player character in the scene remains visible
        GUI.color = new Color(0f, 0f, 0f, 0.46f);
        GUI.DrawTexture(new Rect(0, 0, sw, sh), _px);
        GUI.color = Color.white;

        // Title
        _headStyle.fontSize = Mathf.Clamp((int)(sw / 16f), 30, 64);
        GUI.color = _s3_complete ? new Color(0.2f, 1f, 0.4f) : Color.white;
        GUI.Label(new Rect(0, sh * 0.04f, sw, 70), _s3_complete ? "COMMAND TIMING LEARNED!" : "COMMANDS + TIMING", _headStyle);
        GUI.color = Color.white;

        bool cmdSelected = _s3_cmdDetectedThisCycle;

        // Two-step instructions — highlight the active step
        _bodyStyle.fontSize = Mathf.Clamp((int)(sw / 46f), 13, 22);
        if (!_s3_complete)
        {
            if (!cmdSelected)
            {
                GUI.color = new Color(0f, 0.9f, 1f, 0.95f);
                GUI.Label(new Rect(0, sh * 0.125f, sw, 32), "STEP 1 — SAY THE COMMAND WORD TO SELECT THE CARD", _bodyStyle);
                GUI.color = new Color(1f, 1f, 1f, 0.22f);
                GUI.Label(new Rect(0, sh * 0.165f, sw, 28), "STEP 2 — SHOUT ON THE BEAT!", _bodyStyle);
            }
            else
            {
                GUI.color = new Color(0.3f, 1f, 0.4f, 0.45f);
                GUI.Label(new Rect(0, sh * 0.125f, sw, 32), "STEP 1 — CARD SELECTED!", _bodyStyle);
                GUI.color = new Color(1f, 0.82f, 0.1f, 0.95f);
                GUI.Label(new Rect(0, sh * 0.165f, sw, 28), "STEP 2 — NOW SHOUT ON THE BEAT!", _bodyStyle);
            }
        }
        else
        {
            GUI.color = new Color(1f, 1f, 1f, 0.50f);
            GUI.Label(new Rect(0, sh * 0.14f, sw, 34), "MOVING ON...", _bodyStyle);
        }
        GUI.color = Color.white;

        // Timing state
        float beatFrac = Mathf.Clamp01((_s3_elapsed % stage3BeatInterval) / stage3BeatInterval);
        float goodFrac = Mathf.Clamp01(goodWindow / stage3BeatInterval);
        float exFrac   = Mathf.Clamp01(excellentWindow / stage3BeatInterval);
        bool  inGood   = beatFrac >= (1f - goodFrac);
        bool  inEx     = beatFrac >= (1f - exFrac);
        float rawGlow  = Mathf.Pow(beatFrac, 2.2f);
        float glow     = cmdSelected ? rawGlow : rawGlow * 0.3f;
        float flashT   = _s3_beatFlash / BEAT_FLASH_DUR;

        int    cmdIdx  = _s3_cmdOrder != null ? _s3_cmdOrder[_s3_orderPos % _s3_cmdOrder.Length] : 0;
        string cmdWord = S1_LABELS[cmdIdx];

        // Card
        float cardW = Mathf.Clamp(sw * 0.38f, 260f, 430f);
        float cardH = Mathf.Clamp(sh * 0.29f, 150f, 210f);
        float cardX = sw * 0.5f - cardW * 0.5f;
        float cardY = sh * 0.25f;

        // Card background — dark blue unselected → dark green selected → flashes on beat
        Color cardBg;
        if (_s3_beatFlash > 0f && cmdSelected)
            cardBg = Color.Lerp(new Color(0.04f, 0.12f, 0.06f, 0.95f), new Color(0.5f, 0.95f, 0.55f, 0.95f), flashT);
        else if (cmdSelected && inGood)
            cardBg = Color.Lerp(new Color(0.04f, 0.12f, 0.06f, 0.95f), new Color(0.02f, 0.22f, 0.07f, 0.95f),
                                (beatFrac - (1f - goodFrac)) / Mathf.Max(goodFrac, 0.001f));
        else if (cmdSelected)
            cardBg = new Color(0.04f, 0.12f, 0.06f, 0.95f);
        else
            cardBg = new Color(0.06f, 0.06f, 0.14f, 0.95f);
        GUI.color = cardBg;
        GUI.DrawTexture(new Rect(cardX, cardY, cardW, cardH), _px);

        // Card border — cyan (dim) when unselected, gold/green when selected
        float bw = Mathf.Lerp(2f, 5f, glow);
        Color borderCol;
        if (cmdSelected)
        {
            borderCol = Color.Lerp(new Color(0.9f, 0.72f, 0.1f, 0.6f), new Color(0.3f, 1f, 0.45f, 1f), rawGlow);
            if (inGood) borderCol = Color.Lerp(borderCol, new Color(0.4f, 1f, 0.5f, 1f),
                                               (beatFrac - (1f - goodFrac)) / Mathf.Max(goodFrac, 0.001f));
            if (_s3_beatFlash > 0f) borderCol = Color.Lerp(borderCol, Color.white, flashT);
        }
        else
        {
            borderCol = Color.Lerp(new Color(0f, 0.55f, 0.75f, 0.18f), new Color(0f, 0.7f, 0.95f, 0.42f), rawGlow);
        }
        GUI.color = borderCol;
        GUI.DrawTexture(new Rect(cardX,              cardY,              cardW, bw),    _px);
        GUI.DrawTexture(new Rect(cardX,              cardY + cardH - bw, cardW, bw),    _px);
        GUI.DrawTexture(new Rect(cardX,              cardY,              bw,    cardH), _px);
        GUI.DrawTexture(new Rect(cardX + cardW - bw, cardY,              bw,    cardH), _px);

        // Command word — dim cyan when unselected, bright green with checkmark when selected
        _cardStyle.fontSize = Mathf.Clamp((int)(sw / 16f), 32, 64);
        Color  wordCol;
        string wordDisplay;
        if (cmdSelected)
        {
            wordDisplay = "✓  " + cmdWord;
            wordCol = inGood
                ? Color.Lerp(new Color(0.3f, 1f, 0.45f), new Color(0.55f, 1f, 0.65f),
                             (beatFrac - (1f - goodFrac)) / Mathf.Max(goodFrac, 0.001f))
                : new Color(0.25f, 1f, 0.38f);
            if (_s3_beatFlash > 0f) wordCol = Color.Lerp(wordCol, Color.white, flashT);
        }
        else
        {
            wordDisplay = cmdWord;
            wordCol = Color.Lerp(new Color(0f, 0.32f, 0.48f, 0.72f), new Color(0f, 0.65f, 0.88f, 0.88f), rawGlow);
        }
        GUI.color = wordCol;
        GUI.Label(new Rect(cardX, cardY, cardW, cardH * 0.52f), wordDisplay, _cardStyle);
        GUI.color = Color.white;

        // "SAY THIS WORD FIRST" hint (only when unselected)
        if (!cmdSelected)
        {
            _bodyStyle.fontSize = Mathf.Clamp((int)(sw / 68f), 9, 15);
            GUI.color = new Color(0f, 0.65f, 0.88f, 0.50f);
            GUI.Label(new Rect(cardX, cardY + cardH * 0.50f, cardW, 22f), "SAY THIS WORD FIRST", _bodyStyle);
            GUI.color = Color.white;
        }

        // Fill bar inside card — dim when unselected, fully active when selected
        float pad      = cardW * 0.07f;
        float barW     = cardW - pad * 2f;
        float barH     = Mathf.Clamp(cardH * 0.17f, 12f, 22f);
        float barX     = cardX + pad;
        float barY     = cardY + cardH * 0.65f;
        float barAlpha = cmdSelected ? 1f : 0.28f;

        GUI.color = new Color(0f, 0f, 0f, 0.55f * barAlpha);
        GUI.DrawTexture(new Rect(barX, barY, barW, barH), _px);

        GUI.color = new Color(0f, 0.8f, 0.2f, 0.28f * barAlpha);
        GUI.DrawTexture(new Rect(barX + barW * (1f - goodFrac), barY, barW * goodFrac, barH), _px);

        GUI.color = new Color(0.2f, 1f, 0.3f, 0.58f * barAlpha);
        GUI.DrawTexture(new Rect(barX + barW * (1f - exFrac), barY, barW * exFrac, barH), _px);

        Color fillCol;
        if (cmdSelected)
        {
            fillCol = inGood
                ? Color.Lerp(new Color(0.3f, 1f, 0.42f, 0.92f), new Color(0.55f, 1f, 0.58f, 0.92f),
                             (beatFrac - (1f - goodFrac)) / Mathf.Max(goodFrac, 0.001f))
                : new Color(0.28f, 1f, 0.38f, Mathf.Lerp(0.5f, 0.85f, rawGlow));
            if (_s3_beatFlash > 0f) fillCol = Color.Lerp(fillCol, Color.white, flashT * 0.85f);
        }
        else
        {
            fillCol = new Color(0f, 0.45f, 0.65f, 0.28f);
        }
        GUI.color = fillCol;
        GUI.DrawTexture(new Rect(barX, barY, barW * beatFrac, barH), _px);

        GUI.color = new Color(1f, 1f, 1f, 0.18f * barAlpha);
        GUI.DrawTexture(new Rect(barX,        barY,        barW,  1.5f), _px);
        GUI.DrawTexture(new Rect(barX,        barY + barH, barW,  1.5f), _px);
        GUI.DrawTexture(new Rect(barX,        barY,        1.5f,  barH), _px);
        GUI.DrawTexture(new Rect(barX + barW, barY,        1.5f,  barH), _px);
        GUI.color = Color.white;

        // "SHOUT ZONE" label above green zone (only when selected)
        if (cmdSelected)
        {
            _bodyStyle.fontSize = Mathf.Clamp((int)(sw / 72f), 9, 14);
            GUI.color = new Color(0.3f, 1f, 0.4f, 0.65f);
            GUI.Label(new Rect(barX + barW * (1f - goodFrac), barY - 17f, barW * goodFrac, 17f), "SHOUT ZONE", _bodyStyle);
            GUI.color = Color.white;
        }

        // State label below card
        string statusText;
        Color  statusCol;
        if (_s3_beatFlash > 0f)
        {
            statusText = _s3_lastGrade;
            statusCol  = _s3_lastGrade.StartsWith("EX") ? new Color(0.2f, 1f, 0.4f)
                       : _s3_lastGrade == "GOOD"        ? new Color(0.4f, 0.85f, 1f)
                       : _s3_lastGrade.Contains("TIME") ? new Color(1f, 0.6f, 0.1f)
                       : _s3_lastGrade.Contains("WORD") ? new Color(1f, 0.35f, 0.5f)
                       :                                   new Color(1f, 0.25f, 0.2f);
        }
        else if (!cmdSelected)
        {
            statusText = "SAY THE COMMAND WORD";
            statusCol  = new Color(0f, 0.82f, 1f, 0.70f);
        }
        else if (inEx)   { statusText = "NOW!";          statusCol = new Color(0.25f, 1f, 0.5f); }
        else if (inGood) { statusText = "SHOUT!";        statusCol = new Color(0.5f, 1f, 0.6f); }
        else if (beatFrac > 0.55f)
        {
            float r = (beatFrac - 0.55f) / 0.45f;
            statusText = "GET READY...";
            statusCol  = new Color(1f, Mathf.Lerp(0.7f, 0.95f, r), 0.2f, Mathf.Lerp(0.4f, 0.9f, r));
        }
        else { statusText = "WAIT..."; statusCol = new Color(1f, 1f, 1f, 0.22f); }

        float statusY = cardY + cardH + 14f;
        _bodyStyle.fontSize = Mathf.Clamp((int)(sw / 30f), 24, 42);
        GUI.color = statusCol;
        GUI.Label(new Rect(0, statusY, sw, 50f), statusText, _bodyStyle);
        GUI.color = Color.white;

        // Grade float above card
        if (_s3_gradeFade > 0f && _s3_beatFlash <= 0f && !string.IsNullOrEmpty(_s3_lastGrade))
        {
            float alpha    = Mathf.Clamp01(_s3_gradeFade / GRADE_FADE_DUR);
            float rise     = (1f - alpha) * 28f;
            Color gradeCol = _s3_lastGrade.StartsWith("EX") ? new Color(0.2f, 1f, 0.4f, alpha)
                           : _s3_lastGrade == "GOOD"        ? new Color(0.4f, 0.85f, 1f, alpha)
                           : _s3_lastGrade.Contains("TIME") ? new Color(1f, 0.6f, 0.1f, alpha)
                           : _s3_lastGrade.Contains("WORD") ? new Color(1f, 0.35f, 0.5f, alpha)
                           :                                   new Color(1f, 0.25f, 0.2f, alpha);
            _headStyle.fontSize = Mathf.Clamp((int)(sw / 17f), 28, 58);
            GUI.color = gradeCol;
            GUI.Label(new Rect(0, cardY - 72f - rise, sw, 68f), _s3_lastGrade, _headStyle);
            GUI.color = Color.white;
        }

        // Beat dots
        float dotsY = statusY + 56f;
        DrawBeatDots(cardX, dotsY, cardW, MAX_S3_BEATS, _s3_totalBeats);

        // Hit counter
        _bodyStyle.fontSize = Mathf.Clamp((int)(sw / 40f), 18, 30);
        GUI.color = new Color(1f, 1f, 1f, 0.78f);
        GUI.Label(new Rect(0, dotsY + 26f, sw, 40f), $"{_s3_hitCount} / {hitsToPassStage3} CORRECT", _bodyStyle);
        GUI.color = Color.white;

        // ── Stage 3 intro overlay ─────────────────────────────────────────
        if (_s3_introActive)
        {
            float cW = Mathf.Clamp(sw * 0.38f, 260f, 430f);
            float cH = Mathf.Clamp(sh * 0.29f, 150f, 210f);
            float cX = sw * 0.5f - cW * 0.5f, cY = sh * 0.25f;
            float pB = cW * 0.07f;
            float bX = cX + pB, bY = cY + cH * 0.65f;
            float bW = cW - pB * 2f, bH = Mathf.Clamp(cH * 0.17f, 12f, 22f);
            bool  adv = false;
            switch (_s3_introStep)
            {
                case 0:
                    adv = DrawTutorialPopup(sw, sh, new Rect(0,0,0,0),
                        "STAGE 3 OF 5", "COMMANDS + TIMING",
                        "Combine voice commands with beat timing. Each round has TWO steps: say the command word first, then shout on the beat.");
                    break;
                case 1:
                    adv = DrawTutorialPopup(sw, sh, new Rect(cX, cY, cW, cH),
                        "STEP 1 — THE CARD", "COMMAND CARD",
                        "A random command word appears on this card. Say it out loud — voice recognition will detect it and select the card.");
                    break;
                case 2:
                    adv = DrawTutorialPopup(sw, sh, new Rect(cX, cY, cW, cH * 0.56f),
                        "STEP 1 — SELECTION", "SAY THE WORD",
                        "When detected, the card turns GREEN with a ✓ checkmark. That means it's selected and you're ready for step 2!");
                    break;
                case 3:
                    adv = DrawTutorialPopup(sw, sh, new Rect(bX - 6f, bY - 22f, bW + 12f, bH + 44f),
                        "STEP 2 — TIMING", "SHOUT ON THE BEAT",
                        "Once selected, shout again when the bar hits the green zone. EXCELLENT = perfect timing. GOOD = close. MISS = off the beat.");
                    break;
                case 4:
                    adv = DrawTutorialPopup(sw, sh, new Rect(0,0,0,0),
                        "LET'S GO!", "READY?",
                        "Land 8 correct hits to pass.\n① Say the word to select the card\n② Shout again when the bar hits green\nListen for the beat click!");
                    break;
            }
            if (adv) AdvanceIntro();
            return;
        }

        DrawBackButton();
    }

    // ══════════════════════════════════════════════════════════════════════
    // Helpers
    // ══════════════════════════════════════════════════════════════════════
    // Returns true when the player clicks to advance (call AdvanceIntro in response).
    private bool DrawTutorialPopup(float sw, float sh, Rect spotlight, string stepLabel, string title, string body)
    {
        const float PAD = 14f;

        // Spotlight cutout: draw 4 dark rects around the highlighted area so it shows through naturally
        if (spotlight.width > 0f)
        {
            Rect sp = new Rect(spotlight.x - PAD, spotlight.y - PAD,
                               spotlight.width + PAD * 2f, spotlight.height + PAD * 2f);
            GUI.color = new Color(0f, 0f, 0f, 0.76f);
            GUI.DrawTexture(new Rect(0f,      0f,      sw,         sp.y),          _px); // top
            GUI.DrawTexture(new Rect(0f,      sp.yMax, sw,         sh - sp.yMax),  _px); // bottom
            GUI.DrawTexture(new Rect(0f,      sp.y,    sp.x,       sp.height),     _px); // left
            GUI.DrawTexture(new Rect(sp.xMax, sp.y,    sw - sp.xMax, sp.height),   _px); // right

            // Pulsing gold border around the spotlight
            float pulse = (Mathf.Sin(Time.time * 5f) + 1f) * 0.5f;
            float gw    = Mathf.Lerp(2.5f, 7f, pulse);
            GUI.color   = Color.Lerp(new Color(1f, 0.78f, 0.05f, 0.85f), new Color(1f, 1f, 0.5f, 1f), pulse);
            GUI.DrawTexture(new Rect(sp.x,        sp.y,         sp.width, gw),       _px);
            GUI.DrawTexture(new Rect(sp.x,        sp.yMax - gw, sp.width, gw),       _px);
            GUI.DrawTexture(new Rect(sp.x,        sp.y,         gw,       sp.height), _px);
            GUI.DrawTexture(new Rect(sp.xMax - gw, sp.y,        gw,       sp.height), _px);
        }
        else
        {
            GUI.color = new Color(0f, 0f, 0f, 0.76f);
            GUI.DrawTexture(new Rect(0f, 0f, sw, sh), _px);
        }
        GUI.color = Color.white;

        // Popup box placement: below spotlight if room, else centred
        float boxW = Mathf.Clamp(sw * 0.50f, 280f, 560f);
        float boxH = 218f;
        float boxX = sw * 0.5f - boxW * 0.5f;
        float boxY;
        if (spotlight.width > 0f)
        {
            float below = spotlight.yMax + PAD + 18f;
            boxY = (below + boxH < sh - 16f) ? below : spotlight.y - PAD - 18f - boxH;
        }
        else
        {
            boxY = sh * 0.5f - boxH * 0.5f;
        }

        // Box background + top accent
        GUI.color = new Color(0.03f, 0.05f, 0.11f, 0.97f);
        GUI.DrawTexture(new Rect(boxX, boxY, boxW, boxH), _px);
        GUI.color = new Color(0f, 0.88f, 1f, 0.90f);
        GUI.DrawTexture(new Rect(boxX, boxY, boxW, 3f), _px);

        // Outline
        float bl = 1.5f;
        GUI.color = new Color(0f, 0.62f, 0.85f, 0.38f);
        GUI.DrawTexture(new Rect(boxX,          boxY,          boxW, bl),   _px);
        GUI.DrawTexture(new Rect(boxX,          boxY + boxH,   boxW, bl),   _px);
        GUI.DrawTexture(new Rect(boxX,          boxY,          bl,   boxH), _px);
        GUI.DrawTexture(new Rect(boxX + boxW,   boxY,          bl,   boxH), _px);
        GUI.color = Color.white;

        // Step label (top-left inside box)
        _bodyStyle.alignment = TextAnchor.MiddleLeft;
        _bodyStyle.fontSize  = Mathf.Clamp((int)(sw / 74f), 9, 13);
        GUI.color = new Color(0f, 0.70f, 0.88f, 0.58f);
        GUI.Label(new Rect(boxX + 14f, boxY + 9f, boxW - 28f, 18f), stepLabel, _bodyStyle);

        // Title
        _headStyle.fontSize = Mathf.Clamp((int)(sw / 22f), 26, 44);
        GUI.color = Color.white;
        GUI.Label(new Rect(boxX, boxY + 26f, boxW, 54f), title, _headStyle);

        // Body text (word-wrapped)
        _bodyStyle.alignment = TextAnchor.UpperCenter;
        _bodyStyle.fontSize  = Mathf.Clamp((int)(sw / 53f), 11, 18);
        _bodyStyle.wordWrap  = true;
        GUI.color = new Color(0.82f, 0.84f, 0.91f, 0.88f);
        GUI.Label(new Rect(boxX + 16f, boxY + 86f, boxW - 32f, 90f), body, _bodyStyle);
        _bodyStyle.wordWrap  = false;
        _bodyStyle.alignment = TextAnchor.MiddleCenter;
        GUI.color = Color.white;

        // Pulsing "continue" prompt
        float alpha = (Mathf.Sin(Time.time * 2.6f) + 1f) * 0.22f + 0.52f;
        _bodyStyle.fontSize = Mathf.Clamp((int)(sw / 62f), 10, 16);
        GUI.color = new Color(0f, 0.82f, 1f, alpha);
        GUI.Label(new Rect(boxX, boxY + boxH - 28f, boxW, 22f), "— CLICK OR SPACE TO CONTINUE —", _bodyStyle);
        GUI.color = Color.white;

        // Invisible button over the whole popup (click to advance)
        return GUI.Button(new Rect(boxX, boxY, boxW, boxH), GUIContent.none, GUIStyle.none);
    }

    private void DrawBackButton()
    {
        if (GUI.Button(new Rect(20, 20, 120, 36), "← BACK"))
            SceneManager.LoadScene(menuSceneName);
    }

    private int[] ShuffleRange(int n)
    {
        int[] arr = new int[n];
        for (int i = 0; i < n; i++) arr[i] = i;
        for (int i = n - 1; i > 0; i--)
        {
            int j   = Random.Range(0, i + 1);
            int tmp = arr[i]; arr[i] = arr[j]; arr[j] = tmp;
        }
        return arr;
    }

    private bool MatchesCommand(string word, string[] vocab)
    {
        foreach (string v in vocab)
            if (word == v || Similarity.Compare(word, v) > 0.64f) return true;
        return false;
    }

    private string ParsePartial(string json)
    {
        int s = json.IndexOf("partial\" : \"");
        if (s == -1) return "";
        s += 12;
        int e = json.LastIndexOf("\"");
        return e > s ? json.Substring(s, e - s) : "";
    }

    private void EnsureStyles()
    {
        if (_px == null)
        {
            _px = new Texture2D(1, 1);
            _px.SetPixel(0, 0, Color.white);
            _px.Apply();
        }
        if (_headStyle == null)
            _headStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        if (_bodyStyle == null)
            _bodyStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        if (_cardStyle == null)
            _cardStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
    }

    void OnDestroy()
    {
        if (voskInstance == null) return;
        voskInstance.OnPartialResult -= Stage1HandlePartial;
        voskInstance.OnPartialResult -= Stage3HandlePartial;
    }
}
