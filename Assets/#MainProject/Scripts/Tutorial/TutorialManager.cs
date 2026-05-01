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
        SlotExplain     = 6,
        BotFight        = 7,
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

    [Header("Stage 4 — Slow Rhythm")]
    [Tooltip("AudioSource component that plays looping background music during Stage 4.")]
    public AudioSource stage4MusicSource;
    public AudioClip   stage4BeatClip;     // optional override for beat click
    [Range(1f, 5f)] public float stage4BeatInterval = 3f;
    public int beatsToPassStage4 = 6;

    [Header("Stage 5 — Counter Fight")]
    [Tooltip("Animator on the tutorial bot character (non-networked, in Tutorial scene).")]
    public Animator tutorialBotAnimator;
    [Range(1f, 4f)] public float stage5BeatInterval = 2.5f;

    [Header("Stage 7 — Assisted Bot Fight")]
    [Range(1.5f, 6f)] public float stage7BeatInterval = 3.5f;

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
    private int    _s2_animIdx          = 0;
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

    private enum S3Phase { SelectCard, ShoutOnBeat }
    private S3Phase _s3_phase = S3Phase.SelectCard;

    // ── Intro / tutorial popup system ─────────────────────────────────────
    private int  _s2_introStep   = 0;
    private bool _s2_introActive = true;
    private int  _s3_introStep   = 0;
    private bool _s3_introActive = true;
    private const int S2_INTRO_STEPS = 3;
    private const int S3_INTRO_STEPS = 5;

    // ── Stage 4 runtime ────────────────────────────────────────────────────
    private float  _s4_elapsed              = 0f;
    private float  _s4_nextBeat             = 0f;
    private float  _s4_lastBeatTime         = -999f;
    private float  _s4_lastSpikeElapsed     = -999f;
    private bool   _s4_spikeLockedThisCycle = false;
    private int    _s4_spokenCmdIndex       = -1;   // index into S1_LABELS of the spoken command
    private float[] _s4_cardFlash;                  // per-card flash timer
    private bool[]  _s4_cardHit;                    // true = green (hit), false = orange (miss)
    private int    _s4_hitCount             = 0;
    private int    _s4_totalBeats           = 0;
    private string _s4_lastGrade            = "";
    private float  _s4_gradeFade            = 0f;
    private float  _s4_beatFlash            = 0f;
    private bool   _s4_complete             = false;
    private float  _s4_successTimer         = 0f;
    private const int   MAX_S4_BEATS        = 12;
    private const float STAGE4_LINGER       = 2.5f;
    private const float S4_DEAD_ZONE        = 0.2f;
    private const float S4_CARD_FLASH       = 0.40f;

    // ── Stage 5 runtime ────────────────────────────────────────────────────
    private enum S5Phase { Announce, WaitInput, WrongFeedback, CorrectFlash }
    private S5Phase _s5_phase            = S5Phase.Announce;
    private int[]   _s5_order            = new int[] { 0, 1, 2, 3, 4, 5 };
    private int     _s5_round            = 0;
    private float   _s5_playerHp         = 1f;
    private float   _s5_botHp            = 1f;
    private float   _s5_phaseTimer       = 0f;
    private int     _s5_spokenCtrIdx     = -1;
    private string  _s5_wrongWord        = "";
    private float   _s5_lastWordTime     = -999f;
    private float   _s5_elapsed          = 0f;
    private float   _s5_nextBeat         = 0f;
    private float   _s5_lastBeatTime     = -999f;
    private bool    _s5_spikeLockedThisCycle = false;
    private float   _s5_lastSpikeElapsed = -999f;
    private float   _s5_beatFlash        = 0f;
    private string  _s5_gradeText        = "";
    private float   _s5_gradeFade        = 0f;
    private bool    _s5_complete         = false;
    private float   _s5_successTimer     = 0f;
    private const float S5_DEAD_ZONE     = 0.2f;
    private const float S5_WORD_COOLDOWN = 0.5f;
    private const float S5_CORRECT_FLASH = 1.5f;
    private const float STAGE5_LINGER   = 2.5f;

    // ── Stage 6 runtime ────────────────────────────────────────────────────
    private int   _s6_step           = 0;
    private bool  _s6_complete       = false;
    private float _s6_successTimer   = 0f;
    private const int   S6_SLIDES    = 5;
    private const float STAGE6_LINGER = 2f;

    // ── Stage 7 runtime ────────────────────────────────────────────────────
    private enum S7Phase { Announce, WaitInput, WrongFeedback, CorrectFlash, Lost, Won }
    private S7Phase _s7_phase            = S7Phase.Announce;
    private int[]   _s7_order;
    private int     _s7_round            = 0;
    private float   _s7_playerHp         = 1f;
    private float   _s7_botHp            = 1f;
    private int     _s7_atkSlots         = 4;
    private int     _s7_defSlots         = 4;
    private float   _s7_phaseTimer       = 0f;
    private int     _s7_spokenCtrIdx     = -1;
    private string  _s7_wrongWord        = "";
    private float   _s7_lastWordTime     = -999f;
    private float   _s7_elapsed          = 0f;
    private float   _s7_nextBeat         = 0f;
    private float   _s7_lastBeatTime     = -999f;
    private bool    _s7_spikeLockedThisCycle = false;
    private float   _s7_lastSpikeElapsed = -999f;
    private float   _s7_beatFlash        = 0f;
    private string  _s7_gradeText        = "";
    private float   _s7_gradeFade        = 0f;
    private bool    _s7_complete         = false;
    private float   _s7_successTimer     = 0f;
    private const float S7_DEAD_ZONE     = 0.2f;
    private const float S7_WORD_COOLDOWN = 0.5f;
    private const float S7_CORRECT_FLASH = 1.5f;
    private const float STAGE7_LINGER   = 2.5f;
    private const int   S7_ROUNDS        = 8;

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

    // S5_COUNTERS[i] = j  →  move j counters move i
    // Cycle: PUNCH→BLOCK→BOOM→BLAST→CAGE→HOOK→PUNCH
    private static readonly int[] S5_COUNTERS = { 3, 4, 0, 5, 2, 1 };
    private static readonly string[] S5_COUNTER_FLAVOR =
    {
        "BLOCK absorbs the PUNCH's impact",
        "CAGE nullifies an energy BLAST",
        "PUNCH cuts through the wide HOOK",
        "BOOM shatters through a BLOCK",
        "HOOK slips inside the CAGE",
        "BLAST disperses the BOOM",
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
        _vp           = GetComponent<VoiceProcessor>();
        _cmdLearned   = new bool[S1_LABELS.Length];
        _cmdFlash     = new float[S1_LABELS.Length];
        _s4_cardFlash = new float[S1_LABELS.Length];
        _s4_cardHit   = new bool[S1_LABELS.Length];
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
            if (introActive)                                                          AdvanceIntro();
            else if (CurrentStage == Stage.ComboCards   && !_s5_complete)             HandleStage5Space();
            else if (CurrentStage == Stage.SlotExplain  && !_s6_complete)             AdvanceSlide6();
            else if (CurrentStage == Stage.BotFight     && _s7_phase == S7Phase.WrongFeedback) HandleStage7Space();
            else if (CurrentStage == Stage.BotFight     && _s7_phase == S7Phase.Lost)          HandleStage7Space();
        }
        if (Input.GetKeyDown(KeyCode.P))
            SkipCurrentStage();

        switch (CurrentStage)
        {
            case Stage.VoiceCheck:      UpdateStage0(); break;
            case Stage.ShadowBag:       UpdateStage1(); break;
            case Stage.TimingOnly:      UpdateStage2(); break;
            case Stage.CommandsTiming:  UpdateStage3(); break;
            case Stage.SlowRhythm:      UpdateStage4(); break;
            case Stage.ComboCards:      UpdateStage5(); break;
            case Stage.SlotExplain:     UpdateStage6(); break;
            case Stage.BotFight:        UpdateStage7(); break;
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
        if (next > Stage.BotFight) return;
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

            case Stage.SlowRhythm:
                _s4_elapsed              = 0f;
                _s4_nextBeat             = stage4BeatInterval;
                _s4_lastBeatTime         = -stage4BeatInterval;
                _s4_lastSpikeElapsed     = -999f;
                _s4_spikeLockedThisCycle = false;
                _s4_spokenCmdIndex       = -1;
                _s4_hitCount             = 0;
                _s4_totalBeats           = 0;
                _s4_lastGrade            = "";
                _s4_gradeFade            = 0f;
                _s4_beatFlash            = 0f;
                _s4_complete             = false;
                _s4_successTimer         = 0f;
                for (int i = 0; i < S1_LABELS.Length; i++) { _s4_cardFlash[i] = 0f; _s4_cardHit[i] = false; }
                if (voskInstance != null)
                    voskInstance.OnPartialResult += Stage4HandlePartial;
                if (stage4MusicSource != null)
                    stage4MusicSource.Play();
                break;

            case Stage.ComboCards:
                _s5_phase            = S5Phase.Announce;
                _s5_order            = ShuffleRange(S1_LABELS.Length);
                _s5_round            = 0;
                _s5_playerHp         = 1f;
                _s5_botHp            = 1f;
                _s5_phaseTimer       = 0f;
                _s5_spokenCtrIdx     = -1;
                _s5_wrongWord        = "";
                _s5_lastWordTime     = -999f;
                _s5_elapsed          = 0f;
                _s5_nextBeat         = stage5BeatInterval;
                _s5_lastBeatTime     = -stage5BeatInterval;
                _s5_spikeLockedThisCycle = false;
                _s5_lastSpikeElapsed = -999f;
                _s5_beatFlash        = 0f;
                _s5_gradeText        = "";
                _s5_gradeFade        = 0f;
                _s5_complete         = false;
                _s5_successTimer     = 0f;
                TriggerBotAnimation(_s5_order[0]);
                if (voskInstance != null)
                    voskInstance.OnPartialResult += Stage5HandlePartial;
                break;

            case Stage.CommandsTiming:
                _s3_elapsed              = 0f;
                _s3_nextBeat             = stage3BeatInterval;
                _s3_lastBeatTime         = -stage3BeatInterval;
                _s3_lastSpikeElapsed     = -999f;
                _s3_phase                = S3Phase.SelectCard;
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

            case Stage.SlotExplain:
                _s6_step         = 0;
                _s6_complete     = false;
                _s6_successTimer = 0f;
                break;

            case Stage.BotFight:
            {
                _s7_phase            = S7Phase.Announce;
                int[] baseOrder      = ShuffleRange(S1_LABELS.Length);
                int[] full           = new int[S7_ROUNDS];
                for (int i = 0; i < S7_ROUNDS; i++) full[i] = baseOrder[i % S1_LABELS.Length];
                _s7_order            = full;
                _s7_round            = 0;
                _s7_playerHp         = 1f;
                _s7_botHp            = 1f;
                _s7_atkSlots         = 4;
                _s7_defSlots         = 4;
                _s7_phaseTimer       = 0f;
                _s7_spokenCtrIdx     = -1;
                _s7_wrongWord        = "";
                _s7_lastWordTime     = -999f;
                _s7_elapsed          = 0f;
                _s7_nextBeat         = stage7BeatInterval;
                _s7_lastBeatTime     = -stage7BeatInterval;
                _s7_spikeLockedThisCycle = false;
                _s7_lastSpikeElapsed = -999f;
                _s7_beatFlash        = 0f;
                _s7_gradeText        = "";
                _s7_gradeFade        = 0f;
                _s7_complete         = false;
                _s7_successTimer     = 0f;
                TriggerBotAnimation(_s7_order[0]);
                if (voskInstance != null)
                    voskInstance.OnPartialResult += Stage7HandlePartial;
                break;
            }
        }
    }

    private void ExitStage(Stage s)
    {
        if (s == Stage.SlowRhythm)
        {
            if (voskInstance != null)    voskInstance.OnPartialResult -= Stage4HandlePartial;
            if (stage4MusicSource != null) stage4MusicSource.Stop();
        }
        if (voskInstance == null) return;
        if (s == Stage.ShadowBag)       voskInstance.OnPartialResult -= Stage1HandlePartial;
        if (s == Stage.CommandsTiming)  voskInstance.OnPartialResult -= Stage3HandlePartial;
        if (s == Stage.ComboCards)      voskInstance.OnPartialResult -= Stage5HandlePartial;
        if (s == Stage.BotFight)        voskInstance.OnPartialResult -= Stage7HandlePartial;
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
                TriggerCommandAnimation(_s2_animIdx++ % S1_LABELS.Length);
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
            case Stage.SlowRhythm:      DrawStage4(); break;
            case Stage.ComboCards:      DrawStage5(); break;
            case Stage.SlotExplain:     DrawStage6(); break;
            case Stage.BotFight:        DrawStage7(); break;
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
        if (_s3_complete || _s3_introActive) return;
        if (_s3_cmdDetectedThisCycle) return;           // already selected; locked in ShoutOnBeat
        if (_s3_elapsed - _s3_lastBeatTime < S3_DEAD_ZONE) return;

        string text = ParsePartial(json).ToLower().Trim();
        if (string.IsNullOrEmpty(text)) return;
        int cmdIdx = _s3_cmdOrder[_s3_cmdOrder != null ? _s3_orderPos % _s3_cmdOrder.Length : 0];
        foreach (string word in text.Split(' '))
        {
            if (string.IsNullOrEmpty(word)) continue;
            if (MatchesCommand(word, S1_WORDS[cmdIdx]))
            {
                _s3_cmdDetectedThisCycle = true;
                TriggerCommandAnimation(cmdIdx);
                // Card selected — start the beat countdown for Pause 2
                _s3_phase        = S3Phase.ShoutOnBeat;
                _s3_nextBeat     = _s3_elapsed + stage3BeatInterval;
                _s3_lastBeatTime = _s3_elapsed - S3_DEAD_ZONE; // clear dead zone immediately
                break;
            }
        }
    }

    private void TriggerCommandAnimation(int cmdIdx)
    {
        if (tutorialPlayerAnimator == null) return;
        tutorialPlayerAnimator.Play(S3_ANIM_STATES[cmdIdx], 0, 0f);
    }

    private void TriggerBotAnimation(int cmdIdx)
    {
        if (tutorialBotAnimator == null) return;
        tutorialBotAnimator.Play(S3_ANIM_STATES[cmdIdx], 0, 0f);
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

        // ── Pause 1: SelectCard ── beat is frozen until player says the command word
        if (_s3_phase == S3Phase.SelectCard) return;

        // ── Pause 2: ShoutOnBeat ── beat is live; wait for a successful timing hit
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
        _s3_beatFlash    = BEAT_FLASH_DUR;
        _s3_lastBeatTime = _s3_nextBeat;
        _s3_totalBeats++;

        bool spikeInWindow = _s3_lastSpikeElapsed >= _s3_nextBeat - goodWindow
                          && _s3_lastSpikeElapsed <= _s3_nextBeat + goodWindow * 0.4f;

        if (spikeInWindow)
        {
            float absDelta = Mathf.Abs(_s3_lastSpikeElapsed - _s3_nextBeat);
            _s3_lastGrade = absDelta <= excellentWindow ? "EXCELLENT!" : "GOOD";
            _s3_hitCount++;
            // Hit — return to SelectCard for the next command
            _s3_phase                = S3Phase.SelectCard;
            _s3_cmdDetectedThisCycle = false;
            _s3_orderPos++;
            if (_s3_orderPos % S1_LABELS.Length == 0)
                _s3_cmdOrder = ShuffleRange(S1_LABELS.Length);
        }
        else
        {
            // Miss — stay in ShoutOnBeat; re-arm beat so they try again
            _s3_lastGrade = "MISS";
        }

        _s3_gradeFade            = GRADE_FADE_DUR;
        _s3_nextBeat            += stage3BeatInterval;
        _s3_spikeLockedThisCycle = false;
        _s3_lastSpikeElapsed     = -999f;

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

        // Timing state — bar is frozen (0) in SelectCard, fills toward beat in ShoutOnBeat
        float beatFrac = (_s3_phase == S3Phase.ShoutOnBeat && _s3_nextBeat > _s3_elapsed)
            ? Mathf.Clamp01(1f - (_s3_nextBeat - _s3_elapsed) / stage3BeatInterval)
            : 0f;
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

        // ── SelectCard phase: spotlight the card, prompt for command word ─
        if (_s3_phase == S3Phase.SelectCard && !_s3_introActive && !_s3_complete)
        {
            Rect sp = new Rect(cardX - 16f, cardY - 16f, cardW + 32f, cardH + 32f);
            GUI.color = new Color(0f, 0f, 0f, 0.58f);
            GUI.DrawTexture(new Rect(0f,       0f,      sw,           sp.y),          _px);
            GUI.DrawTexture(new Rect(0f,       sp.yMax, sw,           sh - sp.yMax),  _px);
            GUI.DrawTexture(new Rect(0f,       sp.y,    sp.x,         sp.height),     _px);
            GUI.DrawTexture(new Rect(sp.xMax,  sp.y,    sw - sp.xMax, sp.height),     _px);

            float pulse = (Mathf.Sin(Time.time * 5f) + 1f) * 0.5f;
            float gw    = Mathf.Lerp(3f, 8f, pulse);
            GUI.color   = Color.Lerp(new Color(0f, 0.78f, 1f, 0.85f), new Color(0.5f, 1f, 1f, 1f), pulse);
            GUI.DrawTexture(new Rect(sp.x,         sp.y,         sp.width, gw),        _px);
            GUI.DrawTexture(new Rect(sp.x,         sp.yMax - gw, sp.width, gw),        _px);
            GUI.DrawTexture(new Rect(sp.x,         sp.y,         gw,       sp.height), _px);
            GUI.DrawTexture(new Rect(sp.xMax - gw, sp.y,         gw,       sp.height), _px);
            GUI.color = Color.white;

            // Big command prompt above the card
            _headStyle.fontSize = Mathf.Clamp((int)(sw / 14f), 34, 68);
            GUI.color = new Color(0f, 0.92f, 1f, 1f);
            GUI.Label(new Rect(0, sp.y - 80f, sw, 62f), $"SAY:  \"{cmdWord}\"", _headStyle);
            _bodyStyle.fontSize = Mathf.Clamp((int)(sw / 54f), 11, 18);
            GUI.color = new Color(1f, 1f, 1f, 0.55f);
            GUI.Label(new Rect(0, sp.y - 20f, sw, 22f), "SPEAK THE COMMAND WORD TO SELECT YOUR CARD", _bodyStyle);
            GUI.color = Color.white;

            // "P = skip" hint
            _bodyStyle.fontSize = Mathf.Clamp((int)(sw / 72f), 9, 14);
            GUI.color = new Color(1f, 1f, 1f, 0.30f);
            GUI.Label(new Rect(0, sh - 46f, sw, 22f), "P = SKIP STAGE", _bodyStyle);
            GUI.color = Color.white;
        }

        // ── ShoutOnBeat phase: remind player to shout on the beat ─────────
        if (_s3_phase == S3Phase.ShoutOnBeat && !_s3_introActive && !_s3_complete && _s3_beatFlash <= 0f)
        {
            _bodyStyle.fontSize = Mathf.Clamp((int)(sw / 42f), 15, 26);
            GUI.color = new Color(1f, 0.82f, 0.1f, 0.88f);
            GUI.Label(new Rect(0, sh * 0.06f + 50f, sw, 30f), "CARD SELECTED — NOW SHOUT ON THE BEAT!", _bodyStyle);
            GUI.color = Color.white;

            // "P = skip" hint
            _bodyStyle.fontSize = Mathf.Clamp((int)(sw / 72f), 9, 14);
            GUI.color = new Color(1f, 1f, 1f, 0.30f);
            GUI.Label(new Rect(0, sh - 46f, sw, 22f), "P = SKIP STAGE", _bodyStyle);
            GUI.color = Color.white;
        }

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

    // ══════════════════════════════════════════════════════════════════════
    // STAGE 4 — Slow Rhythm
    // ══════════════════════════════════════════════════════════════════════
    private void Stage4HandlePartial(string json)
    {
        if (_s4_complete) return;
        // Dead zone + lock: block Vosk echoes that bleed into the next cycle
        if (_s4_elapsed - _s4_lastBeatTime < S4_DEAD_ZONE) return;
        if (_s4_spokenCmdIndex >= 0) return;
        string text = ParsePartial(json).ToLower().Trim();
        if (string.IsNullOrEmpty(text)) return;
        foreach (string word in text.Split(' '))
        {
            if (string.IsNullOrEmpty(word)) continue;
            for (int i = 0; i < S1_WORDS.Length; i++)
            {
                if (MatchesCommand(word, S1_WORDS[i]))
                {
                    _s4_spokenCmdIndex = i;
                    return; // animate at beat-fire, not here
                }
            }
        }
    }

    private void UpdateStage4()
    {
        if (_s4_complete)
        {
            _s4_successTimer += Time.deltaTime;
            if (_s4_successTimer >= STAGE4_LINGER) AdvanceTo(Stage.ComboCards);
            return;
        }

        _s4_elapsed += Time.deltaTime;
        if (_s4_gradeFade > 0f) _s4_gradeFade -= Time.deltaTime;
        if (_s4_beatFlash > 0f) _s4_beatFlash -= Time.deltaTime;
        for (int i = 0; i < _s4_cardFlash.Length; i++)
            if (_s4_cardFlash[i] > 0f) _s4_cardFlash[i] -= Time.deltaTime;

        float timeSinceLastBeat = _s4_elapsed - _s4_lastBeatTime;
        if (timeSinceLastBeat >= S4_DEAD_ZONE && !_s4_spikeLockedThisCycle
            && _vp.CurrentRawVolume >= micThreshold)
        {
            _s4_lastSpikeElapsed     = _s4_elapsed;
            _s4_spikeLockedThisCycle = true;
        }

        if (_s4_elapsed < _s4_nextBeat) return;

        // ── Beat fires ────────────────────────────────────────────────────
        PlayBeat(stage4BeatClip);
        _s4_beatFlash    = BEAT_FLASH_DUR;
        _s4_lastBeatTime = _s4_nextBeat;
        _s4_totalBeats++;

        bool spikeInWindow = _s4_lastSpikeElapsed >= _s4_nextBeat - goodWindow
                          && _s4_lastSpikeElapsed <= _s4_nextBeat + goodWindow * 0.4f;
        bool cmdSpoken = _s4_spokenCmdIndex >= 0;

        // Animate the move at beat-fire time, not when word is spoken
        if (cmdSpoken) TriggerCommandAnimation(_s4_spokenCmdIndex);

        if (cmdSpoken && spikeInWindow)
        {
            float delta = Mathf.Abs(_s4_lastSpikeElapsed - _s4_nextBeat);
            _s4_lastGrade = delta <= excellentWindow ? "EXCELLENT!" : "GOOD";
            _s4_cardFlash[_s4_spokenCmdIndex] = S4_CARD_FLASH;
            _s4_cardHit[_s4_spokenCmdIndex]   = true;
            _s4_hitCount++;
        }
        else if (cmdSpoken)
        {
            _s4_lastGrade = "WRONG TIME!";
            _s4_cardFlash[_s4_spokenCmdIndex] = S4_CARD_FLASH;
            _s4_cardHit[_s4_spokenCmdIndex]   = false;
        }
        else
        {
            _s4_lastGrade = "MISS";
        }

        _s4_gradeFade = GRADE_FADE_DUR;
        _s4_nextBeat += stage4BeatInterval;
        _s4_spikeLockedThisCycle = false;
        _s4_spokenCmdIndex       = -1;
        _s4_lastSpikeElapsed     = -999f;

        if (_s4_hitCount >= beatsToPassStage4 || _s4_totalBeats >= MAX_S4_BEATS)
            _s4_complete = true;
    }

    // ── Stage 4 GUI ────────────────────────────────────────────────────────
    private void DrawStage4()
    {
        float sw = Screen.width, sh = Screen.height;

        // ── Title ─────────────────────────────────────────────────────────
        _headStyle.fontSize = Mathf.Clamp((int)(sw / 16f), 30, 64);
        GUI.color = _s4_complete ? new Color(0.2f, 1f, 0.4f) : Color.white;
        GUI.Label(new Rect(0, sh * 0.04f, sw, 70), _s4_complete ? "RHYTHM LOCKED IN!" : "SLOW RHYTHM", _headStyle);
        GUI.color = Color.white;

        _bodyStyle.fontSize = Mathf.Clamp((int)(sw / 52f), 13, 22);
        GUI.color = new Color(1f, 1f, 1f, 0.48f);
        GUI.Label(new Rect(0, sh * 0.13f, sw, 32), _s4_complete ? "MOVING ON..." : "CHOOSE ANY COMMAND — SHOUT IT ON THE BEAT", _bodyStyle);
        GUI.color = Color.white;

        // ── Global approach bar ───────────────────────────────────────────
        float beatFrac = Mathf.Clamp01(_s4_elapsed % stage4BeatInterval / stage4BeatInterval);
        float goodFrac = Mathf.Clamp01(goodWindow / stage4BeatInterval);
        float exFrac   = Mathf.Clamp01(excellentWindow / stage4BeatInterval);
        bool  inGood   = beatFrac >= (1f - goodFrac);
        bool  inEx     = beatFrac >= (1f - exFrac);
        float glow     = Mathf.Pow(beatFrac, 2.2f);
        float flashT   = _s4_beatFlash / BEAT_FLASH_DUR;

        float barW = sw * 0.72f, barH = 26f;
        float barX = sw * 0.14f, barY = sh * 0.22f;

        GUI.color = new Color(0f, 0f, 0f, 0.5f);
        GUI.DrawTexture(new Rect(barX, barY, barW, barH), _px);

        // Good + excellent zones
        GUI.color = new Color(0.1f, 0.85f, 0.2f, 0.28f);
        GUI.DrawTexture(new Rect(barX + barW * (1f - goodFrac), barY, barW * goodFrac, barH), _px);
        GUI.color = new Color(0.2f, 1f, 0.3f, 0.55f);
        GUI.DrawTexture(new Rect(barX + barW * (1f - exFrac), barY, barW * exFrac, barH), _px);

        // Moving fill
        Color fillCol;
        if (_s4_beatFlash > 0f)
            fillCol = Color.Lerp(new Color(1f, 0.85f, 0.1f, 0.92f), Color.white, flashT * 0.8f);
        else if (inGood)
            fillCol = Color.Lerp(new Color(1f, 0.85f, 0.1f, 0.88f), new Color(0.2f, 1f, 0.35f, 0.92f),
                                 (beatFrac - (1f - goodFrac)) / Mathf.Max(goodFrac, 0.001f));
        else
            fillCol = new Color(0f, 0.75f, 1f, Mathf.Lerp(0.42f, 0.78f, glow));
        GUI.color = fillCol;
        GUI.DrawTexture(new Rect(barX, barY, barW * beatFrac, barH), _px);

        // Bar border
        GUI.color = new Color(1f, 1f, 1f, 0.22f);
        GUI.DrawTexture(new Rect(barX,        barY,        barW, 1.5f), _px);
        GUI.DrawTexture(new Rect(barX,        barY + barH, barW, 1.5f), _px);
        GUI.DrawTexture(new Rect(barX,        barY,        1.5f, barH), _px);
        GUI.DrawTexture(new Rect(barX + barW, barY,        1.5f, barH), _px);
        GUI.color = Color.white;

        // ── Beat state label ──────────────────────────────────────────────
        string stateText;
        Color  stateCol;
        if (_s4_beatFlash > 0f)
        {
            stateText = _s4_lastGrade;
            stateCol  = _s4_lastGrade.StartsWith("EX") ? new Color(0.2f, 1f, 0.4f)
                      : _s4_lastGrade == "GOOD"        ? new Color(0.4f, 0.85f, 1f)
                      : _s4_lastGrade.Contains("TIME") ? new Color(1f, 0.6f, 0.1f)
                      :                                   new Color(1f, 0.3f, 0.2f);
        }
        else if (inEx)            { stateText = "NOW!";          stateCol = new Color(0.25f, 1f, 0.4f); }
        else if (inGood)          { stateText = "SHOUT!";        stateCol = new Color(0.5f, 1f, 0.6f); }
        else if (beatFrac > 0.55f)
        {
            float r = (beatFrac - 0.55f) / 0.45f;
            stateText = "GET READY..."; stateCol = new Color(1f, Mathf.Lerp(0.65f, 0.92f, r), 0.15f, Mathf.Lerp(0.4f, 0.9f, r));
        }
        else { stateText = "WAIT..."; stateCol = new Color(1f, 1f, 1f, 0.2f); }

        _bodyStyle.fontSize = Mathf.Clamp((int)(sw / 28f), 26, 46);
        GUI.color = stateCol;
        GUI.Label(new Rect(0, barY + barH + 8f, sw, 50f), stateText, _bodyStyle);
        GUI.color = Color.white;

        // ── Grade float (above bar) ────────────────────────────────────────
        if (_s4_gradeFade > 0f && _s4_beatFlash <= 0f && !string.IsNullOrEmpty(_s4_lastGrade))
        {
            float alpha = Mathf.Clamp01(_s4_gradeFade / GRADE_FADE_DUR);
            float rise  = (1f - alpha) * 22f;
            Color gc    = _s4_lastGrade.StartsWith("EX") ? new Color(0.2f, 1f, 0.4f, alpha)
                        : _s4_lastGrade == "GOOD"        ? new Color(0.4f, 0.85f, 1f, alpha)
                        : _s4_lastGrade.Contains("TIME") ? new Color(1f, 0.6f, 0.1f, alpha)
                        :                                   new Color(1f, 0.3f, 0.2f, alpha);
            _headStyle.fontSize = Mathf.Clamp((int)(sw / 17f), 28, 56);
            GUI.color = gc;
            GUI.Label(new Rect(0, barY - 64f - rise, sw, 60f), _s4_lastGrade, _headStyle);
            GUI.color = Color.white;
        }

        // ── Command cards (all 6, horizontal) ─────────────────────────────
        int   numCards  = S1_LABELS.Length;
        float gridW     = sw * 0.82f;
        float cW        = (gridW - (numCards - 1) * 14f) / numCards;
        float cH        = Mathf.Clamp(sh * 0.20f, 80f, 130f);
        float cStartX   = sw * 0.5f - gridW * 0.5f;
        float cY        = sh * 0.56f;

        _cardStyle.fontSize  = Mathf.Clamp((int)(cW / 5f), 16, 32);
        _bodyStyle.fontSize  = Mathf.Clamp((int)(sw / 72f), 9, 14);

        for (int i = 0; i < numCards; i++)
        {
            float cx   = cStartX + i * (cW + 14f);
            Rect  rect = new(cx, cY, cW, cH);

            float flash    = _s4_cardFlash[i];
            bool  wasHit   = _s4_cardHit[i];
            float flashP   = Mathf.Clamp01(flash / S4_CARD_FLASH);
            bool  selected = (i == _s4_spokenCmdIndex) && _s4_beatFlash <= 0f;

            // Card background
            Color bg = flash > 0f
                ? Color.Lerp(new Color(0.06f, 0.06f, 0.12f, 0.92f),
                    wasHit ? new Color(0.05f, 0.22f, 0.08f, 0.92f) : new Color(0.22f, 0.10f, 0.03f, 0.92f),
                    flashP)
                : selected
                    ? new Color(0.03f, 0.16f, 0.22f, 0.95f)
                    : new Color(0.06f, 0.06f, 0.12f, 0.88f);
            GUI.color = bg;
            GUI.DrawTexture(rect, _px);

            // Top charge strip
            float chargeAlpha = Mathf.Lerp(0.12f, 0.85f, glow);
            GUI.color = selected
                ? new Color(0f, 1f, 0.92f, chargeAlpha)
                : inGood
                    ? new Color(0.2f, 1f, 0.4f, chargeAlpha)
                    : new Color(0f, 0.75f, 1f, chargeAlpha);
            GUI.DrawTexture(new Rect(cx, cY, cW * beatFrac, 3f), _px);

            // Corner brackets
            float bw   = Mathf.Lerp(1.5f, 3.5f, glow);
            float bLen = Mathf.Clamp(cW * 0.22f, 10f, 22f);
            Color bc   = flash > 0f
                ? Color.Lerp(new Color(1f, 1f, 1f, 0.5f),
                    wasHit ? new Color(0.3f, 1f, 0.4f) : new Color(1f, 0.55f, 0.1f), flashP)
                : selected
                    ? Color.Lerp(new Color(0f, 0.7f, 0.9f, 0.7f), new Color(0f, 1f, 0.95f, 1f), glow)
                    : Color.Lerp(new Color(0.85f, 0f, 1f, 0.4f), new Color(0.85f, 0f, 1f, 0.9f), glow);
            GUI.color = bc;
            // TL
            GUI.DrawTexture(new Rect(cx,           cY,            bLen, bw),  _px);
            GUI.DrawTexture(new Rect(cx,           cY,            bw,   bLen),_px);
            // TR
            GUI.DrawTexture(new Rect(cx + cW - bLen, cY,          bLen, bw),  _px);
            GUI.DrawTexture(new Rect(cx + cW - bw,   cY,          bw,   bLen),_px);
            // BL
            GUI.DrawTexture(new Rect(cx,           cY + cH - bw,  bLen, bw),  _px);
            GUI.DrawTexture(new Rect(cx,           cY + cH - bLen,bw,   bLen),_px);
            // BR
            GUI.DrawTexture(new Rect(cx + cW - bLen, cY + cH - bw, bLen, bw), _px);
            GUI.DrawTexture(new Rect(cx + cW - bw,   cY + cH - bLen, bw, bLen),_px);

            // Label
            Color labelCol = flash > 0f
                ? Color.Lerp(new Color(0.7f, 0.7f, 0.7f),
                    wasHit ? new Color(0.4f, 1f, 0.5f) : new Color(1f, 0.65f, 0.2f), flashP)
                : selected
                    ? Color.Lerp(new Color(0f, 0.85f, 1f, 0.88f), Color.white, glow)
                    : Color.Lerp(new Color(0.3f, 0.3f, 0.35f), new Color(0.85f, 0f, 1f), glow);
            GUI.color = labelCol;
            GUI.Label(rect, S1_LABELS[i], _cardStyle);

            // READY! overlay when pre-selected
            if (selected)
            {
                float pulse = (Mathf.Sin(Time.time * 5f) + 1f) * 0.5f;
                _bodyStyle.fontSize = Mathf.Clamp((int)(cW / 7.5f), 9, 15);
                GUI.color = new Color(0f, 1f, 0.92f, Mathf.Lerp(0.65f, 0.95f, pulse));
                GUI.Label(new Rect(cx, cY + cH * 0.62f, cW, 22f), "● READY!", _bodyStyle);
            }
            GUI.color = Color.white;
        }

        // ── Beat dots + hit counter ────────────────────────────────────────
        float dotsY = cY + cH + 16f;
        DrawBeatDots(cStartX, dotsY, gridW, MAX_S4_BEATS, _s4_totalBeats);

        _bodyStyle.fontSize = Mathf.Clamp((int)(sw / 40f), 18, 30);
        GUI.color = new Color(1f, 1f, 1f, 0.78f);
        GUI.Label(new Rect(0, dotsY + 22f, sw, 38f), $"{_s4_hitCount} / {beatsToPassStage4} HITS", _bodyStyle);
        GUI.color = Color.white;

        DrawBackButton();
    }

    // ══════════════════════════════════════════════════════════════════════
    // STAGE 5 — Counter Fight
    // ══════════════════════════════════════════════════════════════════════
    private void Stage5HandlePartial(string json)
    {
        if (_s5_complete) return;
        if (_s5_phase != S5Phase.WaitInput) return;
        if (_s5_spokenCtrIdx >= 0) return;
        if (Time.time - _s5_lastWordTime < S5_WORD_COOLDOWN) return;
        string text = ParsePartial(json).ToLower().Trim();
        if (string.IsNullOrEmpty(text)) return;
        foreach (string word in text.Split(' '))
        {
            if (string.IsNullOrEmpty(word)) continue;
            for (int i = 0; i < S1_WORDS.Length; i++)
            {
                if (MatchesCommand(word, S1_WORDS[i]))
                { _s5_spokenCtrIdx = i; return; }
            }
        }
    }

    private void HandleStage5Space()
    {
        switch (_s5_phase)
        {
            case S5Phase.Announce:
            case S5Phase.CorrectFlash:
                S5GoToWaitInput();
                break;
            case S5Phase.WrongFeedback:
                _s5_spokenCtrIdx = -1;
                _s5_lastWordTime = Time.time;
                S5GoToWaitInput();
                break;
            default:
                SkipCurrentStage();
                break;
        }
    }

    private void S5GoToWaitInput()
    {
        _s5_phase            = S5Phase.WaitInput;
        _s5_phaseTimer       = 0f;
        _s5_spokenCtrIdx     = -1;
        _s5_lastWordTime     = Time.time;
        _s5_elapsed          = 0f;
        _s5_nextBeat         = stage5BeatInterval;
        _s5_lastBeatTime     = -stage5BeatInterval;
        _s5_spikeLockedThisCycle = false;
        _s5_lastSpikeElapsed = -999f;
        _s5_beatFlash        = 0f;
        _s5_gradeText        = "";
        _s5_gradeFade        = 0f;
    }

    private void UpdateStage5()
    {
        if (_s5_complete)
        {
            _s5_successTimer += Time.deltaTime;
            if (_s5_successTimer >= STAGE5_LINGER)
                AdvanceTo(Stage.SlotExplain);
            return;
        }

        _s5_phaseTimer += Time.deltaTime;

        switch (_s5_phase)
        {
            case S5Phase.Announce:
                // Auto-advance after a pause so player can read
                if (_s5_phaseTimer >= stage5BeatInterval)
                    S5GoToWaitInput();
                break;

            case S5Phase.WaitInput:
            {
                _s5_elapsed += Time.deltaTime;
                if (_s5_beatFlash > 0f) _s5_beatFlash -= Time.deltaTime;
                if (_s5_gradeFade > 0f) _s5_gradeFade -= Time.deltaTime;

                // Shout spike detection inside shout zone
                float timeSinceLast = _s5_elapsed - _s5_lastBeatTime;
                float timeToNext    = _s5_nextBeat - _s5_elapsed;
                bool  inShoutZone   = timeToNext > 0f && timeToNext <= 0.5f;
                if (timeSinceLast >= S5_DEAD_ZONE && inShoutZone
                    && !_s5_spikeLockedThisCycle && _vp.CurrentRawVolume >= micThreshold)
                {
                    _s5_lastSpikeElapsed     = _s5_elapsed;
                    _s5_spikeLockedThisCycle = true;
                }

                if (_s5_elapsed < _s5_nextBeat) break;

                // ── Beat fires ───────────────────────────────────────────
                PlayBeat(stage4BeatClip);
                _s5_beatFlash    = BEAT_FLASH_DUR;
                _s5_lastBeatTime = _s5_nextBeat;

                int   roundIdx    = _s5_order[_s5_round];
                int   expectedCtr = S5_COUNTERS[roundIdx];
                bool  spike       = _s5_lastSpikeElapsed >= _s5_nextBeat - goodWindow
                                 && _s5_lastSpikeElapsed <= _s5_nextBeat + goodWindow * 0.4f;
                bool  correctCtr  = _s5_spokenCtrIdx == expectedCtr;
                bool  anySaid     = _s5_spokenCtrIdx >= 0;

                if (correctCtr && spike)
                {
                    TriggerCommandAnimation(expectedCtr);
                    _s5_botHp      = Mathf.Max(0f, _s5_botHp - 1f / S1_LABELS.Length);
                    _s5_phase      = S5Phase.CorrectFlash;
                    _s5_phaseTimer = 0f;
                }
                else if (anySaid && spike)
                {
                    // Wrong word, shouted on beat → bot attack lands
                    _s5_wrongWord  = S1_LABELS[_s5_spokenCtrIdx];
                    TriggerBotAnimation(roundIdx);
                    _s5_playerHp   = Mathf.Max(0f, _s5_playerHp - 1f / S1_LABELS.Length);
                    _s5_phase      = S5Phase.WrongFeedback;
                    _s5_phaseTimer = 0f;
                }
                else if (!anySaid && spike)
                {
                    // Shouted with no word said → wrong counter (empty)
                    _s5_wrongWord  = "nothing";
                    TriggerBotAnimation(roundIdx);
                    _s5_playerHp   = Mathf.Max(0f, _s5_playerHp - 1f / S1_LABELS.Length);
                    _s5_phase      = S5Phase.WrongFeedback;
                    _s5_phaseTimer = 0f;
                }
                else
                {
                    // No shout (or correct word but no shout) — just retry the beat
                    _s5_gradeText  = correctCtr ? "SHOUT ON THE BEAT!" : "MISS — SAY THE WORD!";
                    _s5_gradeFade  = GRADE_FADE_DUR;
                    _s5_nextBeat  += stage5BeatInterval;
                    // Keep _s5_spokenCtrIdx if they had the right word so card stays filled
                    if (!correctCtr) { _s5_spokenCtrIdx = -1; _s5_lastWordTime = Time.time; }
                }

                _s5_spikeLockedThisCycle = false;
                _s5_lastSpikeElapsed     = -999f;
                break;
            }

            case S5Phase.WrongFeedback:
                // Paused — only Space (handled in HandleStage5Space) resumes
                break;

            case S5Phase.CorrectFlash:
                if (_s5_phaseTimer >= S5_CORRECT_FLASH)
                    S5AdvanceRound();
                break;
        }
    }

    private void S5AdvanceRound()
    {
        _s5_round++;
        if (_s5_round >= S1_LABELS.Length) { _s5_complete = true; return; }
        _s5_phase        = S5Phase.Announce;
        _s5_phaseTimer   = 0f;
        _s5_spokenCtrIdx = -1;
        _s5_wrongWord    = "";
        TriggerBotAnimation(_s5_order[_s5_round]);
    }

    // ── Stage 5 GUI ────────────────────────────────────────────────────────
    private void DrawStage5()
    {
        float sw = Screen.width, sh = Screen.height;

        GUI.color = new Color(0f, 0f, 0f, 0.46f);
        GUI.DrawTexture(new Rect(0, 0, sw, sh), _px);
        GUI.color = Color.white;

        if (_s5_complete)
        {
            _headStyle.fontSize = Mathf.Clamp((int)(sw / 14f), 36, 72);
            GUI.color = new Color(0.2f, 1f, 0.4f);
            GUI.Label(new Rect(0, sh * 0.38f, sw, 80), "COUNTER TRAINING COMPLETE!", _headStyle);
            _bodyStyle.fontSize = Mathf.Clamp((int)(sw / 40f), 18, 30);
            GUI.color = new Color(1f, 1f, 1f, 0.6f);
            GUI.Label(new Rect(0, sh * 0.52f, sw, 40), "NEXT STAGE — SLOT SYSTEM...", _bodyStyle);
            GUI.color = Color.white;
            return;
        }

        // ── Right panel — counter reference table ─────────────────────────
        float panelW = Mathf.Clamp(sw * 0.26f, 190f, 280f);
        float panelH = Mathf.Clamp(sh * 0.62f, 320f, 520f);
        float panelX = sw - panelW - 22f;
        float panelY = sh * 0.19f;
        int   roundIdx = Mathf.Min(_s5_round, S1_LABELS.Length - 1);
        int   attIdx   = (_s5_order != null && roundIdx < _s5_order.Length) ? _s5_order[roundIdx] : 0;
        int   ctrIdx   = S5_COUNTERS[attIdx];
        DrawCounterTablePanel(panelX, panelY, panelW, panelH, sw, attIdx);

        float mainW = sw - panelW - 44f;

        // ── HP bars ────────────────────────────────────────────────────────
        float hpW = mainW * 0.38f, hpH = 18f, hpY = sh * 0.12f;
        DrawHpBar(22f,              hpY, hpW, hpH, _s5_botHp,    "BOT", new Color(1f, 0.35f, 0.35f));
        DrawHpBar(mainW - 22f - hpW, hpY, hpW, hpH, _s5_playerHp, "YOU", new Color(0.3f, 0.8f, 1f));

        // ── Round indicator ────────────────────────────────────────────────
        _bodyStyle.fontSize = Mathf.Clamp((int)(sw / 72f), 9, 13);
        GUI.color = new Color(0.6f, 0.6f, 0.7f, 0.55f);
        GUI.Label(new Rect(0, sh * 0.07f, mainW, 20f), $"COUNTER TRAINING  •  {_s5_round + 1} / {S1_LABELS.Length}", _bodyStyle);
        GUI.color = Color.white;

        // ── Card layout geometry ───────────────────────────────────────────
        float cW       = Mathf.Clamp(mainW * 0.30f, 120f, 210f);
        float cH       = Mathf.Clamp(sh * 0.26f, 120f, 200f);
        float midX     = mainW * 0.5f;
        float gap      = Mathf.Clamp(mainW * 0.10f, 42f, 80f);
        float attCardX = midX - gap * 0.5f - cW;
        float ctrCardX = midX + gap * 0.5f;
        float cardsY   = sh * 0.22f;

        switch (_s5_phase)
        {
            // ── ANNOUNCE ──────────────────────────────────────────────────
            case S5Phase.Announce:
            {
                _headStyle.fontSize = Mathf.Clamp((int)(sw / 15f), 32, 68);
                GUI.color = new Color(1f, 0.72f, 0.1f);
                GUI.Label(new Rect(0, sh * 0.16f, mainW, 60f), "BOT WILL USE:", _headStyle);
                GUI.color = Color.white;

                // Large single attack card centred
                float bigW = Mathf.Clamp(mainW * 0.38f, 160f, 280f);
                float bigH = Mathf.Clamp(sh * 0.30f, 140f, 220f);
                DrawFightCard(mainW * 0.5f - bigW * 0.5f, cardsY, bigW, bigH, S1_LABELS[attIdx], "INCOMING ATTACK", false, sw, large: true);

                // "Get ready" prompt
                float alpha = (Mathf.Sin(Time.time * 2.4f) + 1f) * 0.22f + 0.52f;
                _bodyStyle.fontSize = Mathf.Clamp((int)(sw / 52f), 12, 20);
                GUI.color = new Color(0f, 0.85f, 1f, alpha);
                GUI.Label(new Rect(0, cardsY + bigH + 28f, mainW, 28f),
                    "Get ready to counter — press SPACE when set!", _bodyStyle);
                GUI.color = Color.white;
                break;
            }

            // ── WAIT INPUT ────────────────────────────────────────────────
            case S5Phase.WaitInput:
            {
                _headStyle.fontSize = Mathf.Clamp((int)(sw / 15f), 32, 68);
                GUI.color = new Color(0f, 1f, 0.85f);
                GUI.Label(new Rect(0, sh * 0.16f, mainW, 60f), "COUNTER IT!", _headStyle);
                GUI.color = Color.white;

                DrawFightCard(attCardX, cardsY, cW, cH, S1_LABELS[attIdx], "BOT ATTACKS", false, sw);

                bool spokenAny  = _s5_spokenCtrIdx >= 0;
                bool spokenGood = spokenAny && _s5_spokenCtrIdx == ctrIdx;
                string ctrLabel = spokenAny ? S1_LABELS[_s5_spokenCtrIdx] : "???";
                DrawFightCard(ctrCardX, cardsY, cW, cH, ctrLabel, "YOUR COUNTER", true, sw, isCorrect: spokenGood, hasInput: spokenAny);

                float vsGlow = (Mathf.Sin(Time.time * 2.8f) + 1f) * 0.5f;
                _headStyle.fontSize = Mathf.Clamp((int)(sw / 24f), 20, 40);
                GUI.color = Color.Lerp(new Color(1f, 0.75f, 0.1f, 0.6f), new Color(1f, 0.95f, 0.3f, 1f), vsGlow);
                GUI.Label(new Rect(attCardX + cW, cardsY + cH * 0.35f, gap, cH * 0.3f), "VS", _headStyle);
                GUI.color = Color.white;

                // ── Approach bar ─────────────────────────────────────────
                float beatFrac = Mathf.Clamp01((_s5_elapsed - _s5_lastBeatTime) / stage5BeatInterval);
                float goodFrac = Mathf.Clamp01(goodWindow / stage5BeatInterval);
                float exFrac   = Mathf.Clamp01(excellentWindow / stage5BeatInterval);
                bool  inGood   = beatFrac >= (1f - goodFrac);
                bool  inEx     = beatFrac >= (1f - exFrac);
                float glow     = Mathf.Pow(beatFrac, 2.2f);

                float barW = mainW * 0.72f, barH = 22f;
                float barX = mainW * 0.14f, barY = cardsY + cH + 20f;

                GUI.color = new Color(0f, 0f, 0f, 0.5f);
                GUI.DrawTexture(new Rect(barX, barY, barW, barH), _px);
                GUI.color = new Color(0.1f, 0.85f, 0.2f, 0.28f);
                GUI.DrawTexture(new Rect(barX + barW * (1f - goodFrac), barY, barW * goodFrac, barH), _px);
                GUI.color = new Color(0.2f, 1f, 0.3f, 0.55f);
                GUI.DrawTexture(new Rect(barX + barW * (1f - exFrac), barY, barW * exFrac, barH), _px);
                Color fillCol;
                if (_s5_beatFlash > 0f)
                    fillCol = Color.Lerp(new Color(1f, 0.85f, 0.1f, 0.92f), Color.white, (_s5_beatFlash / BEAT_FLASH_DUR) * 0.8f);
                else if (inGood)
                    fillCol = Color.Lerp(new Color(1f, 0.85f, 0.1f, 0.88f), new Color(0.2f, 1f, 0.35f, 0.92f),
                                         (beatFrac - (1f - goodFrac)) / Mathf.Max(goodFrac, 0.001f));
                else
                    fillCol = new Color(0f, 0.75f, 1f, Mathf.Lerp(0.42f, 0.78f, glow));
                GUI.color = fillCol;
                GUI.DrawTexture(new Rect(barX, barY, barW * beatFrac, barH), _px);
                GUI.color = new Color(1f, 1f, 1f, 0.22f);
                GUI.DrawTexture(new Rect(barX,        barY,        barW, 1.5f), _px);
                GUI.DrawTexture(new Rect(barX,        barY + barH, barW, 1.5f), _px);
                GUI.DrawTexture(new Rect(barX,        barY,        1.5f, barH), _px);
                GUI.DrawTexture(new Rect(barX + barW, barY,        1.5f, barH), _px);
                GUI.color = Color.white;

                // Beat state label
                string stateText; Color stateCol;
                if (_s5_beatFlash > 0f && !string.IsNullOrEmpty(_s5_gradeText))
                { stateText = _s5_gradeText; stateCol = new Color(1f, 0.55f, 0.1f); }
                else if (inEx)   { stateText = "NOW!";         stateCol = new Color(0.25f, 1f, 0.4f); }
                else if (inGood) { stateText = "SHOUT!";       stateCol = new Color(0.5f, 1f, 0.6f); }
                else if (beatFrac > 0.55f)
                {
                    float r = (beatFrac - 0.55f) / 0.45f;
                    stateText = "GET READY..."; stateCol = new Color(1f, Mathf.Lerp(0.65f, 0.92f, r), 0.15f, Mathf.Lerp(0.4f, 0.9f, r));
                }
                else { stateText = "WAIT..."; stateCol = new Color(1f, 1f, 1f, 0.22f); }
                _bodyStyle.fontSize = Mathf.Clamp((int)(sw / 30f), 22, 42);
                GUI.color = stateCol;
                GUI.Label(new Rect(0, barY + barH + 6f, mainW, 46f), stateText, _bodyStyle);

                // Grade fade
                if (_s5_gradeFade > 0f && _s5_beatFlash <= 0f && !string.IsNullOrEmpty(_s5_gradeText))
                {
                    float a   = Mathf.Clamp01(_s5_gradeFade / GRADE_FADE_DUR);
                    float rise = (1f - a) * 18f;
                    _headStyle.fontSize = Mathf.Clamp((int)(sw / 18f), 26, 52);
                    GUI.color = new Color(1f, 0.6f, 0.1f, a);
                    GUI.Label(new Rect(0, barY - 60f - rise, mainW, 56f), _s5_gradeText, _headStyle);
                }

                // Hint text
                _bodyStyle.fontSize = Mathf.Clamp((int)(sw / 58f), 10, 17);
                GUI.color = new Color(0f, 0.85f, 1f, 0.52f);
                GUI.Label(new Rect(0, barY + barH + 52f, mainW, 24f),
                    $"1. Say \"{S1_LABELS[ctrIdx]}\"   2. Shout on the beat!", _bodyStyle);
                GUI.color = Color.white;
                break;
            }

            // ── WRONG FEEDBACK ────────────────────────────────────────────
            case S5Phase.WrongFeedback:
            {
                // Faded background cards so context is still readable
                GUI.color = new Color(1f, 1f, 1f, 0.35f);
                DrawFightCard(attCardX, cardsY, cW, cH, S1_LABELS[attIdx], "BOT ATTACKED", false, sw);
                DrawFightCard(ctrCardX, cardsY, cW, cH, _s5_wrongWord, "YOU SAID", true, sw, isCorrect: false, hasInput: true);
                GUI.color = Color.white;

                // Dark overlay behind popup
                GUI.color = new Color(0f, 0f, 0f, 0.72f);
                GUI.DrawTexture(new Rect(0, 0, mainW, sh), _px);
                GUI.color = Color.white;

                // ── Popup box ─────────────────────────────────────────────
                float bxW = Mathf.Clamp(mainW * 0.80f, 260f, 540f);
                float bxH = 220f;
                float bxX = mainW * 0.5f - bxW * 0.5f;
                float bxY = sh * 0.5f - bxH * 0.5f;

                GUI.color = new Color(0.06f, 0.03f, 0.12f, 0.98f);
                GUI.DrawTexture(new Rect(bxX, bxY, bxW, bxH), _px);

                // Red top strip
                GUI.color = new Color(1f, 0.22f, 0.22f, 0.90f);
                GUI.DrawTexture(new Rect(bxX, bxY, bxW, 4f), _px);

                // Border
                float bl = 1.5f;
                GUI.color = new Color(1f, 0.25f, 0.25f, 0.38f);
                GUI.DrawTexture(new Rect(bxX,        bxY,        bxW, bl),   _px);
                GUI.DrawTexture(new Rect(bxX,        bxY + bxH,  bxW, bl),   _px);
                GUI.DrawTexture(new Rect(bxX,        bxY,        bl,  bxH),  _px);
                GUI.DrawTexture(new Rect(bxX + bxW,  bxY,        bl,  bxH),  _px);

                // Title
                _headStyle.fontSize = Mathf.Clamp((int)(sw / 20f), 28, 52);
                GUI.color = new Color(1f, 0.35f, 0.35f);
                GUI.Label(new Rect(bxX, bxY + 10f, bxW, 52f), "ATTACK LANDED!", _headStyle);

                // Explanation
                int    correctIdx = ctrIdx;
                string attackName = S1_LABELS[attIdx];
                string counterName = S1_LABELS[correctIdx];
                string explain = $"\"{_s5_wrongWord}\" doesn't counter {attackName}.\n"
                               + $"{attackName}  ▶  countered by  {counterName}";
                _bodyStyle.fontSize = Mathf.Clamp((int)(sw / 46f), 14, 24);
                _bodyStyle.wordWrap = true;
                GUI.color = new Color(0.90f, 0.88f, 0.95f, 0.92f);
                GUI.Label(new Rect(bxX + 20f, bxY + 68f, bxW - 40f, 80f), explain, _bodyStyle);
                _bodyStyle.wordWrap = false;

                // Pulsing retry prompt
                float pa = (Mathf.Sin(Time.time * 2.6f) + 1f) * 0.22f + 0.52f;
                _bodyStyle.fontSize = Mathf.Clamp((int)(sw / 60f), 10, 16);
                GUI.color = new Color(0f, 0.82f, 1f, pa);
                GUI.Label(new Rect(bxX, bxY + bxH - 32f, bxW, 24f),
                    $"Press SPACE to try again — say \"{counterName}\" then shout on the beat!", _bodyStyle);
                GUI.color = Color.white;
                break;
            }

            // ── CORRECT FLASH ─────────────────────────────────────────────
            case S5Phase.CorrectFlash:
            {
                DrawFightCard(attCardX, cardsY, cW, cH, S1_LABELS[attIdx], "BOT ATTACKED", false, sw);
                DrawFightCard(ctrCardX, cardsY, cW, cH, S1_LABELS[ctrIdx], "YOUR COUNTER", true, sw, isCorrect: true, hasInput: true);

                float vsG = (Mathf.Sin(Time.time * 2.8f) + 1f) * 0.5f;
                _headStyle.fontSize = Mathf.Clamp((int)(sw / 24f), 20, 40);
                GUI.color = Color.Lerp(new Color(0.2f, 1f, 0.4f, 0.7f), new Color(0.5f, 1f, 0.6f, 1f), vsG);
                GUI.Label(new Rect(attCardX + cW, cardsY + cH * 0.35f, gap, cH * 0.3f), "✓", _headStyle);
                GUI.color = Color.white;

                _headStyle.fontSize = Mathf.Clamp((int)(sw / 14f), 36, 72);
                GUI.color = new Color(0.2f, 1f, 0.4f);
                GUI.Label(new Rect(0, sh * 0.16f, mainW, 68f), "COUNTER HIT!", _headStyle);

                string flavor = S5_COUNTER_FLAVOR[attIdx];
                _bodyStyle.fontSize = Mathf.Clamp((int)(sw / 52f), 12, 20);
                _bodyStyle.wordWrap = true;
                GUI.color = new Color(0.75f, 0.90f, 0.78f, 0.85f);
                GUI.Label(new Rect(22f, cardsY + cH + 18f, mainW - 44f, 48f), flavor, _bodyStyle);
                _bodyStyle.wordWrap = false;

                // Progress strip (auto-advances after S5_CORRECT_FLASH seconds)
                float prog = Mathf.Clamp01(_s5_phaseTimer / S5_CORRECT_FLASH);
                float stripW = mainW * 0.60f, stripH = 6f;
                float stripX = mainW * 0.5f - stripW * 0.5f;
                float stripY = cardsY + cH + 80f;
                GUI.color = new Color(0.08f, 0.08f, 0.14f, 0.80f);
                GUI.DrawTexture(new Rect(stripX, stripY, stripW, stripH), _px);
                GUI.color = new Color(0.2f, 1f, 0.45f, 0.85f);
                GUI.DrawTexture(new Rect(stripX, stripY, stripW * prog, stripH), _px);
                GUI.color = Color.white;
                break;
            }
        }

        DrawBackButton();
    }

    private void DrawCounterTablePanel(float panelX, float panelY, float panelW, float panelH, float sw, int activeIdx)
    {
        GUI.color = new Color(0.03f, 0.04f, 0.10f, 0.92f);
        GUI.DrawTexture(new Rect(panelX, panelY, panelW, panelH), _px);
        GUI.color = new Color(0f, 0.72f, 1f, 0.50f);
        GUI.DrawTexture(new Rect(panelX, panelY, panelW, 2.5f), _px);
        GUI.color = new Color(0f, 0.72f, 1f, 0.18f);
        GUI.DrawTexture(new Rect(panelX,          panelY, 1.5f, panelH), _px);
        GUI.DrawTexture(new Rect(panelX + panelW, panelY, 1.5f, panelH), _px);
        GUI.DrawTexture(new Rect(panelX, panelY + panelH, panelW, 1.5f), _px);
        GUI.color = Color.white;

        _bodyStyle.fontSize = Mathf.Clamp((int)(sw / 68f), 9, 13);
        GUI.color = new Color(0f, 0.72f, 1f, 0.70f);
        GUI.Label(new Rect(panelX, panelY + 8f, panelW, 20f), "COUNTER TABLE", _bodyStyle);
        GUI.color = Color.white;

        float rowH = (panelH - 36f) / S1_LABELS.Length;
        _bodyStyle.fontSize = Mathf.Clamp((int)(panelW / 14f), 10, 16);
        for (int i = 0; i < S1_LABELS.Length; i++)
        {
            float ry       = panelY + 34f + i * rowH;
            bool  isActive = (i == activeIdx);
            if (isActive)
            {
                GUI.color = new Color(0f, 0.45f, 0.65f, 0.32f);
                GUI.DrawTexture(new Rect(panelX + 2f, ry, panelW - 4f, rowH - 2f), _px);
            }
            GUI.color = isActive ? new Color(1f, 0.35f, 0.35f, 1f) : new Color(0.9f, 0.35f, 0.35f, 0.55f);
            GUI.Label(new Rect(panelX + 8f, ry, panelW * 0.36f, rowH), S1_LABELS[i], _bodyStyle);
            GUI.color = isActive ? new Color(0.7f, 0.7f, 0.7f, 0.9f) : new Color(0.5f, 0.5f, 0.5f, 0.40f);
            GUI.Label(new Rect(panelX + panelW * 0.38f, ry, panelW * 0.16f, rowH), "▶", _bodyStyle);
            GUI.color = isActive ? new Color(0.25f, 1f, 0.45f, 1f) : new Color(0.25f, 1f, 0.45f, 0.55f);
            GUI.Label(new Rect(panelX + panelW * 0.54f, ry, panelW * 0.44f, rowH),
                S1_LABELS[S5_COUNTERS[i]], _bodyStyle);
        }
        GUI.color = Color.white;
    }

    private void DrawHpBar(float x, float y, float w, float h, float pct, string name, Color barColor)
    {
        GUI.color = new Color(0.04f, 0.04f, 0.08f, 0.85f);
        GUI.DrawTexture(new Rect(x, y, w, h), _px);
        float p = Mathf.Clamp01(pct);
        if (p > 0f) { GUI.color = barColor; GUI.DrawTexture(new Rect(x, y, w * p, h), _px); }
        GUI.color = new Color(1f, 1f, 1f, 0.18f);
        GUI.DrawTexture(new Rect(x,     y,     w,    1.5f), _px);
        GUI.DrawTexture(new Rect(x,     y + h, w,    1.5f), _px);
        GUI.DrawTexture(new Rect(x,     y,     1.5f, h),    _px);
        GUI.DrawTexture(new Rect(x + w, y,     1.5f, h),    _px);
        _bodyStyle.fontSize  = Mathf.Clamp((int)(w / 8f), 9, 14);
        _bodyStyle.alignment = TextAnchor.MiddleCenter;
        GUI.color = new Color(1f, 1f, 1f, 0.70f);
        GUI.Label(new Rect(x, y - 18f, w, 16f), name, _bodyStyle);
        GUI.color = Color.white;
    }

    private void DrawFightCard(float x, float y, float w, float h,
                               string label, string badge, bool isPlayer, float sw,
                               bool isCorrect = false, bool hasInput = false, bool large = false)
    {
        Color bgC = isPlayer
            ? (hasInput
                ? (isCorrect ? new Color(0.03f, 0.18f, 0.06f, 0.95f) : new Color(0.18f, 0.04f, 0.04f, 0.88f))
                : new Color(0.06f, 0.06f, 0.16f, 0.88f))
            : new Color(0.18f, 0.04f, 0.04f, 0.88f);
        GUI.color = bgC;
        GUI.DrawTexture(new Rect(x, y, w, h), _px);

        float pulse = (Mathf.Sin(Time.time * 3.2f) + 1f) * 0.5f;
        float bw    = large ? Mathf.Lerp(3f, 6f, pulse) : Mathf.Lerp(2f, 4.5f, pulse);
        float bLen  = Mathf.Clamp(w * 0.22f, 10f, 26f);
        Color bc;
        if      (isPlayer && isCorrect)  bc = Color.Lerp(new Color(0.2f, 1f, 0.4f, 0.65f), new Color(0.4f, 1f, 0.55f, 1f), pulse);
        else if (isPlayer && hasInput)   bc = new Color(1f, 0.3f, 0.2f, 0.8f);
        else if (isPlayer)               bc = new Color(0.6f, 0.6f, 0.8f, 0.5f);
        else if (large)                  bc = Color.Lerp(new Color(1f, 0.28f, 0.28f, 0.6f), new Color(1f, 0.55f, 0.1f, 1f), pulse);
        else                             bc = new Color(1f, 0.28f, 0.28f, 0.7f);
        GUI.color = bc;
        GUI.DrawTexture(new Rect(x,        y,        bLen, bw),   _px);
        GUI.DrawTexture(new Rect(x,        y,        bw,   bLen), _px);
        GUI.DrawTexture(new Rect(x+w-bLen, y,        bLen, bw),   _px);
        GUI.DrawTexture(new Rect(x+w-bw,   y,        bw,   bLen), _px);
        GUI.DrawTexture(new Rect(x,        y+h-bw,   bLen, bw),   _px);
        GUI.DrawTexture(new Rect(x,        y+h-bLen, bw,   bLen), _px);
        GUI.DrawTexture(new Rect(x+w-bLen, y+h-bw,   bLen, bw),   _px);
        GUI.DrawTexture(new Rect(x+w-bw,   y+h-bLen, bw,   bLen), _px);

        Color stripC = isPlayer
            ? (isCorrect ? new Color(0.2f, 1f,   0.4f,  0.35f)
             : hasInput  ? new Color(1f,   0.3f,  0.2f,  0.28f)
             :              new Color(0.4f, 0.4f,  0.7f,  0.25f))
            : new Color(1f, 0.28f, 0.28f, 0.28f);
        GUI.color = stripC;
        GUI.DrawTexture(new Rect(x, y, w, 4f), _px);

        _bodyStyle.fontSize = Mathf.Clamp((int)(sw / 76f), 8, 12);
        GUI.color = isPlayer
            ? (isCorrect ? new Color(0.2f, 1f,   0.4f, 0.80f)
             : hasInput  ? new Color(1f,   0.4f,  0.3f, 0.80f)
             :              new Color(0.6f, 0.6f,  0.9f, 0.65f))
            : new Color(1f, 0.35f, 0.35f, 0.65f);
        GUI.Label(new Rect(x, y + 10f, w, 18f), badge, _bodyStyle);

        float nameSz = large ? w / 3.8f : w / 4.5f;
        _cardStyle.fontSize = Mathf.Clamp((int)nameSz, 22, 52);
        GUI.color = isPlayer
            ? (isCorrect ? new Color(0.35f, 1f,   0.48f)
             : hasInput  ? new Color(1f,    0.38f, 0.38f)
             :              new Color(0.5f,  0.5f,  0.8f))
            : (large ? Color.Lerp(new Color(1f, 0.45f, 0.1f), new Color(1f, 0.25f, 0.25f), pulse)
                     : new Color(1f, 0.38f, 0.38f));
        GUI.Label(new Rect(x, y + h * 0.3f, w, h * 0.45f), label, _cardStyle);
        GUI.color = Color.white;
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
        voskInstance.OnPartialResult -= Stage4HandlePartial;
        voskInstance.OnPartialResult -= Stage5HandlePartial;
        voskInstance.OnPartialResult -= Stage7HandlePartial;
    }

    // ══════════════════════════════════════════════════════════════════════
    // STAGE 6 — Slot Explain
    // ══════════════════════════════════════════════════════════════════════
    private void AdvanceSlide6()
    {
        _s6_step++;
        if (_s6_step >= S6_SLIDES)
            _s6_complete = true;
    }

    private void UpdateStage6()
    {
        if (_s6_complete)
        {
            _s6_successTimer += Time.deltaTime;
            if (_s6_successTimer >= STAGE6_LINGER)
                AdvanceTo(Stage.BotFight);
        }
    }

    // ── Stage 6 GUI ────────────────────────────────────────────────────────
    private void DrawStage6()
    {
        float sw = Screen.width, sh = Screen.height;

        if (_s6_complete)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.85f);
            GUI.DrawTexture(new Rect(0, 0, sw, sh), _px);
            _headStyle.fontSize = Mathf.Clamp((int)(sw / 14f), 36, 72);
            GUI.color = new Color(0.2f, 1f, 0.4f);
            GUI.Label(new Rect(0, sh * 0.40f, sw, 80), "GOT IT!", _headStyle);
            _bodyStyle.fontSize = Mathf.Clamp((int)(sw / 40f), 18, 30);
            GUI.color = new Color(1f, 1f, 1f, 0.6f);
            GUI.Label(new Rect(0, sh * 0.52f, sw, 40), "HEADING TO THE BOT FIGHT...", _bodyStyle);
            GUI.color = Color.white;
            return;
        }

        // Pre-compute the pip illustration rect to use as spotlight so DrawTutorialPopup
        // only darkens the surroundings, leaving the pip area clear.
        float pipSize2 = Mathf.Clamp(sw * 0.028f, 14f, 24f);
        float pipGap2  = pipSize2 * 0.35f;
        float rowH2    = pipSize2 + 6f;
        float illustY2 = sh * 0.11f;
        float rowW2    = 4 * pipSize2 + 3 * pipGap2;
        float baseX2   = sw * 0.5f - rowW2 * 0.5f;
        Rect slotSpot  = new Rect(baseX2 - 80f, illustY2 - 8f, rowW2 + 160f, rowH2 * 2f + 28f);

        string[] titles = {
            "THE SLOT SYSTEM",
            "USING SLOTS",
            "WINNING TRADES",
            "STAGGER!",
            "IDLE RECHARGE",
        };
        string[] bodies = {
            "Every player has ATK slots and DEF slots — like ammo for your moves.\n\nATK slots: how many attack cards you can play.\nDEF slots: how many defense cards you can play.",
            "Playing an ATTACK card costs 1 ATK slot.\nPlaying a DEFENSE card costs 1 DEF slot.\n\nIf a pool hits 0, those cards are greyed out until you recharge.",
            "WIN a counter trade and you earn +1 of the OPPOSITE type.\n\nCounter with ATK? → gain 1 DEF slot.\nBlock with DEF? → gain 1 ATK slot.",
            "Lose ALL your ATK slots → you're STAGGERED!\n\nWhile staggered the opponent gets UNLIMITED attacks and your defense is bypassed. Stay idle to recover.",
            "Do NOTHING on a beat? Both your ATK and DEF pools each gain +1.\n\nUse this breathing room to recover before going back on offence.",
        };

        string stepLbl = $"SLOTS  •  {_s6_step + 1} / {S6_SLIDES}";
        // Draw spotlight surround + popup box, then draw pips on top (inside the clear spotlight)
        bool clicked = DrawTutorialPopup(sw, sh, slotSpot, stepLbl, titles[_s6_step], bodies[_s6_step]);
        DrawSlotIllustration(sw, sh, _s6_step);
        if (clicked) AdvanceSlide6();

        _bodyStyle.fontSize = Mathf.Clamp((int)(sw / 74f), 9, 13);
        GUI.color = new Color(1f, 1f, 1f, 0.28f);
        GUI.Label(new Rect(sw - 130f, sh - 26f, 120f, 20f), "P = SKIP STAGE", _bodyStyle);
        GUI.color = Color.white;
        DrawBackButton();
    }

    private void DrawSlotIllustration(float sw, float sh, int slide)
    {
        float pipSize = Mathf.Clamp(sw * 0.028f, 14f, 24f);
        float pipGap  = pipSize * 0.35f;
        float rowH    = pipSize + 6f;
        float illustY = sh * 0.11f;

        int atkFilled, defFilled;
        switch (slide)
        {
            case 1: atkFilled = 2; defFilled = 4; break;
            case 2: atkFilled = 3; defFilled = 3; break;
            case 3: atkFilled = 0; defFilled = 2; break;
            case 4: atkFilled = 1; defFilled = 1; break;
            default: atkFilled = 4; defFilled = 4; break;
        }
        bool staggered = (slide == 3);

        float rowW = 4 * pipSize + 3 * pipGap;
        float baseX = sw * 0.5f - rowW * 0.5f;

        _bodyStyle.alignment = TextAnchor.MiddleRight;
        _bodyStyle.fontSize = Mathf.Clamp((int)(sw / 68f), 9, 13);
        GUI.color = staggered ? new Color(1f, 0.2f, 0.2f, 0.9f) : new Color(1f, 0.55f, 0.15f, 0.75f);
        GUI.Label(new Rect(baseX - 72f, illustY, 65f, rowH), staggered ? "ATK — STAGGERED" : "ATK", _bodyStyle);
        _bodyStyle.alignment = TextAnchor.MiddleCenter;

        for (int i = 0; i < 4; i++)
        {
            float px = baseX + i * (pipSize + pipGap);
            bool  on = i < atkFilled;
            Color c  = on ? (staggered ? new Color(1f, 0.18f, 0.18f, 0.85f) : new Color(1f, 0.55f, 0.15f, 0.9f))
                           : new Color(0.3f, 0.3f, 0.35f, 0.5f);
            GUI.color = c;
            GUI.DrawTexture(new Rect(px, illustY, pipSize, pipSize), _px);
            if (!on) { GUI.color = new Color(1f, 1f, 1f, 0.12f); DrawPipBorder(px, illustY, pipSize); }
        }

        if (slide == 4)
        {
            _bodyStyle.fontSize = Mathf.Clamp((int)(sw / 38f), 20, 34);
            GUI.color = new Color(0.3f, 1f, 0.5f, 0.85f);
            GUI.Label(new Rect(baseX + rowW + 6f, illustY - 6f, 48f, rowH + 10f), "+1", _bodyStyle);
        }

        float defY = illustY + rowH + 8f;
        _bodyStyle.alignment = TextAnchor.MiddleRight;
        _bodyStyle.fontSize = Mathf.Clamp((int)(sw / 68f), 9, 13);
        GUI.color = new Color(0.3f, 0.65f, 1f, 0.75f);
        GUI.Label(new Rect(baseX - 72f, defY, 65f, rowH), "DEF", _bodyStyle);
        _bodyStyle.alignment = TextAnchor.MiddleCenter;

        for (int i = 0; i < 4; i++)
        {
            float px = baseX + i * (pipSize + pipGap);
            bool  on = i < defFilled;
            GUI.color = on ? new Color(0.3f, 0.65f, 1f, 0.9f) : new Color(0.3f, 0.3f, 0.35f, 0.5f);
            GUI.DrawTexture(new Rect(px, defY, pipSize, pipSize), _px);
            if (!on) { GUI.color = new Color(1f, 1f, 1f, 0.12f); DrawPipBorder(px, defY, pipSize); }
        }

        if (slide == 4)
        {
            _bodyStyle.fontSize = Mathf.Clamp((int)(sw / 38f), 20, 34);
            GUI.color = new Color(0.3f, 1f, 0.5f, 0.85f);
            GUI.Label(new Rect(baseX + rowW + 6f, defY - 6f, 48f, rowH + 10f), "+1", _bodyStyle);
        }

        GUI.color = Color.white;
    }

    private void DrawPipBorder(float x, float y, float sz)
    {
        float b = 1.5f;
        GUI.DrawTexture(new Rect(x,        y,        sz, b),  _px);
        GUI.DrawTexture(new Rect(x,        y + sz-b, sz, b),  _px);
        GUI.DrawTexture(new Rect(x,        y,        b,  sz), _px);
        GUI.DrawTexture(new Rect(x + sz-b, y,        b,  sz), _px);
    }

    // ══════════════════════════════════════════════════════════════════════
    // STAGE 7 — Assisted Bot Fight
    // ══════════════════════════════════════════════════════════════════════
    private void Stage7HandlePartial(string json)
    {
        if (_s7_phase != S7Phase.WaitInput) return;
        if (Time.time - _s7_lastWordTime < S7_WORD_COOLDOWN) return;

        string text = ParsePartial(json).ToLower().Trim();
        if (string.IsNullOrEmpty(text)) return;

        foreach (string word in text.Split(' '))
        {
            if (string.IsNullOrEmpty(word)) continue;
            for (int i = 0; i < S1_WORDS.Length; i++)
            {
                if (MatchesCommand(word, S1_WORDS[i]))
                {
                    _s7_spokenCtrIdx = i;
                    _s7_lastWordTime = Time.time;
                    break;
                }
            }
        }
    }

    private void HandleStage7Space()
    {
        if (_s7_phase == S7Phase.WrongFeedback)
        {
            _s7_spokenCtrIdx = -1;
            _s7_lastWordTime = Time.time;
            S7GoToWaitInput();
        }
        else if (_s7_phase == S7Phase.Lost)
        {
            ExitStage(Stage.BotFight);
            EnterStage(Stage.BotFight);
        }
    }

    private void S7GoToWaitInput()
    {
        _s7_phase            = S7Phase.WaitInput;
        _s7_phaseTimer       = 0f;
        _s7_elapsed          = 0f;
        _s7_nextBeat         = stage7BeatInterval;
        _s7_lastBeatTime     = -stage7BeatInterval;
        _s7_spikeLockedThisCycle = false;
        _s7_lastSpikeElapsed = -999f;
        _s7_beatFlash        = 0f;
        _s7_gradeText        = "";
        _s7_gradeFade        = 0f;
    }

    private void UpdateStage7()
    {
        if (_s7_complete)
        {
            _s7_successTimer += Time.deltaTime;
            if (_s7_successTimer >= STAGE7_LINGER)
                SceneManager.LoadScene(menuSceneName);
            return;
        }

        _s7_phaseTimer += Time.deltaTime;

        switch (_s7_phase)
        {
            case S7Phase.Announce:
                if (_s7_phaseTimer >= stage7BeatInterval)
                {
                    _s7_phase        = S7Phase.WaitInput;
                    _s7_phaseTimer   = 0f;
                    _s7_spokenCtrIdx = -1;
                    _s7_elapsed      = 0f;
                    _s7_nextBeat     = stage7BeatInterval;
                    _s7_lastBeatTime = -stage7BeatInterval;
                    _s7_spikeLockedThisCycle = false;
                    _s7_lastSpikeElapsed     = -999f;
                }
                break;

            case S7Phase.WaitInput:
            {
                _s7_elapsed += Time.deltaTime;
                if (_s7_beatFlash > 0f) _s7_beatFlash -= Time.deltaTime;
                if (_s7_gradeFade > 0f) _s7_gradeFade -= Time.deltaTime;

                if (_vp.CurrentRawVolume >= micThreshold && !_s7_spikeLockedThisCycle)
                {
                    _s7_lastSpikeElapsed     = _s7_elapsed;
                    _s7_spikeLockedThisCycle = true;
                }

                if (_s7_elapsed < _s7_nextBeat) break;

                PlayBeat();
                _s7_beatFlash    = BEAT_FLASH_DUR;
                _s7_lastBeatTime = _s7_elapsed;

                int attIdx    = _s7_order[_s7_round];
                int ctrIdx    = S5_COUNTERS[attIdx];
                bool hasShout = _s7_lastSpikeElapsed >= _s7_nextBeat - goodWindow
                             && _s7_lastSpikeElapsed <= _s7_nextBeat + goodWindow * 0.4f;
                bool correctCtr = _s7_spokenCtrIdx == ctrIdx;

                if (hasShout && correctCtr)
                {
                    float absDelta = Mathf.Abs(_s7_lastSpikeElapsed - _s7_nextBeat);
                    _s7_gradeText = absDelta <= excellentWindow ? "EXCELLENT!" : "GOOD";
                    _s7_gradeFade = GRADE_FADE_DUR;
                    _s7_botHp     = Mathf.Max(0f, _s7_botHp - 1f / S7_ROUNDS);
                    _s7_atkSlots  = Mathf.Max(0, _s7_atkSlots - 1);
                    _s7_defSlots  = Mathf.Min(4, _s7_defSlots + 1);
                    TriggerCommandAnimation(ctrIdx);
                    _s7_phase      = S7Phase.CorrectFlash;
                    _s7_phaseTimer = 0f;
                }
                else if (hasShout && !correctCtr && _s7_spokenCtrIdx >= 0)
                {
                    _s7_wrongWord  = S1_LABELS[_s7_spokenCtrIdx];
                    _s7_playerHp   = Mathf.Max(0f, _s7_playerHp - 1f / S7_ROUNDS);
                    _s7_defSlots   = Mathf.Max(0, _s7_defSlots - 1);
                    _s7_phase      = S7Phase.WrongFeedback;
                    _s7_phaseTimer = 0f;
                }
                else
                {
                    bool idleThisBeat = !hasShout && _s7_spokenCtrIdx < 0;
                    if (idleThisBeat)
                    {
                        _s7_atkSlots  = Mathf.Min(4, _s7_atkSlots + 1);
                        _s7_defSlots  = Mathf.Min(4, _s7_defSlots + 1);
                        _s7_gradeText = "IDLE — +1 RECHARGE";
                    }
                    else
                    {
                        _s7_gradeText = correctCtr ? "SHOUT ON THE BEAT!" : "MISS — SAY THE WORD!";
                        _s7_playerHp  = Mathf.Max(0f, _s7_playerHp - 0.5f / S7_ROUNDS);
                    }
                    _s7_gradeFade = GRADE_FADE_DUR;
                    _s7_nextBeat += stage7BeatInterval;
                    if (!correctCtr) { _s7_spokenCtrIdx = -1; _s7_lastWordTime = Time.time; }
                    _s7_spikeLockedThisCycle = false;
                    _s7_lastSpikeElapsed     = -999f;
                }

                if (_s7_playerHp <= 0f)
                {
                    _s7_phase      = S7Phase.Lost;
                    _s7_phaseTimer = 0f;
                }
                break;
            }

            case S7Phase.WrongFeedback:
                break;

            case S7Phase.CorrectFlash:
                if (_s7_phaseTimer >= S7_CORRECT_FLASH)
                    S7AdvanceRound();
                break;

            case S7Phase.Lost:
                break;

            case S7Phase.Won:
                _s7_successTimer += Time.deltaTime;
                if (_s7_successTimer >= STAGE7_LINGER)
                    _s7_complete = true;
                break;
        }
    }

    private void S7AdvanceRound()
    {
        _s7_round++;
        if (_s7_round >= S7_ROUNDS || _s7_botHp <= 0f)
        {
            _s7_phase        = S7Phase.Won;
            _s7_phaseTimer   = 0f;
            _s7_successTimer = 0f;
            return;
        }
        _s7_phase        = S7Phase.Announce;
        _s7_phaseTimer   = 0f;
        _s7_spokenCtrIdx = -1;
        _s7_wrongWord    = "";
        TriggerBotAnimation(_s7_order[_s7_round]);
    }

    // ── Stage 7 GUI ────────────────────────────────────────────────────────
    private void DrawStage7()
    {
        float sw = Screen.width, sh = Screen.height;

        GUI.color = new Color(0f, 0f, 0f, 0.46f);
        GUI.DrawTexture(new Rect(0, 0, sw, sh), _px);
        GUI.color = Color.white;

        if (_s7_phase == S7Phase.Won || _s7_complete)
        {
            _headStyle.fontSize = Mathf.Clamp((int)(sw / 14f), 36, 72);
            GUI.color = new Color(0.2f, 1f, 0.4f);
            GUI.Label(new Rect(0, sh * 0.38f, sw, 80), "TUTORIAL COMPLETE!", _headStyle);
            _bodyStyle.fontSize = Mathf.Clamp((int)(sw / 40f), 18, 30);
            GUI.color = new Color(1f, 1f, 1f, 0.6f);
            GUI.Label(new Rect(0, sh * 0.52f, sw, 40), "RETURNING TO MENU...", _bodyStyle);
            GUI.color = Color.white;
            return;
        }

        // ── Right panel: slot display + counter table ──────────────────────
        float panelW  = Mathf.Clamp(sw * 0.26f, 190f, 280f);
        float panelX  = sw - panelW - 22f;
        float panelY  = sh * 0.10f;
        int   roundIdx = Mathf.Min(_s7_round, S7_ROUNDS - 1);
        int   attIdx   = _s7_order[roundIdx];
        int   ctrIdx   = S5_COUNTERS[attIdx];

        // Slot pips sub-panel
        float slotH = Mathf.Clamp(sh * 0.22f, 110f, 160f);
        GUI.color = new Color(0.03f, 0.04f, 0.10f, 0.92f);
        GUI.DrawTexture(new Rect(panelX, panelY, panelW, slotH), _px);
        GUI.color = new Color(1f, 0.55f, 0.1f, 0.50f);
        GUI.DrawTexture(new Rect(panelX, panelY, panelW, 2.5f), _px);
        GUI.color = new Color(0f, 0.72f, 1f, 0.18f);
        GUI.DrawTexture(new Rect(panelX,          panelY,        1.5f, slotH), _px);
        GUI.DrawTexture(new Rect(panelX + panelW, panelY,        1.5f, slotH), _px);
        GUI.DrawTexture(new Rect(panelX,          panelY + slotH, panelW, 1.5f), _px);
        GUI.color = Color.white;

        _bodyStyle.fontSize = Mathf.Clamp((int)(sw / 68f), 9, 13);
        GUI.color = new Color(1f, 0.55f, 0.1f, 0.70f);
        GUI.Label(new Rect(panelX, panelY + 8f, panelW, 20f), "YOUR SLOTS", _bodyStyle);
        GUI.color = Color.white;

        float pipSz   = Mathf.Clamp(panelW * 0.14f, 12f, 20f);
        float pipG    = pipSz * 0.28f;
        float pipRowW = 4 * pipSz + 3 * pipG;
        float pipX    = panelX + panelW * 0.5f - pipRowW * 0.5f;
        float atkPipY = panelY + 30f;
        float defPipY = atkPipY + pipSz + 14f;

        _bodyStyle.alignment = TextAnchor.MiddleRight;
        _bodyStyle.fontSize  = Mathf.Clamp((int)(sw / 74f), 9, 12);
        GUI.color = new Color(1f, 0.55f, 0.15f, 0.8f);
        GUI.Label(new Rect(panelX + 4f, atkPipY, pipX - panelX - 8f, pipSz), "ATK", _bodyStyle);
        GUI.color = new Color(0.3f, 0.65f, 1f, 0.8f);
        GUI.Label(new Rect(panelX + 4f, defPipY, pipX - panelX - 8f, pipSz), "DEF", _bodyStyle);
        _bodyStyle.alignment = TextAnchor.MiddleCenter;

        for (int i = 0; i < 4; i++)
        {
            float px = pipX + i * (pipSz + pipG);
            bool  atkOn = i < _s7_atkSlots;
            GUI.color = atkOn ? new Color(1f, 0.55f, 0.15f, 0.9f) : new Color(0.3f, 0.3f, 0.35f, 0.5f);
            GUI.DrawTexture(new Rect(px, atkPipY, pipSz, pipSz), _px);
            if (!atkOn) { GUI.color = new Color(1f, 1f, 1f, 0.12f); DrawPipBorder(px, atkPipY, pipSz); }

            bool defOn = i < _s7_defSlots;
            GUI.color = defOn ? new Color(0.3f, 0.65f, 1f, 0.9f) : new Color(0.3f, 0.3f, 0.35f, 0.5f);
            GUI.DrawTexture(new Rect(px, defPipY, pipSz, pipSz), _px);
            if (!defOn) { GUI.color = new Color(1f, 1f, 1f, 0.12f); DrawPipBorder(px, defPipY, pipSz); }
        }

        // Coaching hint
        float coachY = defPipY + pipSz + 12f;
        string coachText = _s7_atkSlots == 0
            ? "No ATK slots!\nStay idle a beat to recharge."
            : $"Say \"{S1_LABELS[ctrIdx]}\" to counter {S1_LABELS[attIdx]}!";
        _bodyStyle.fontSize = Mathf.Clamp((int)(sw / 68f), 9, 13);
        _bodyStyle.wordWrap = true;
        GUI.color = new Color(0f, 0.85f, 1f, 0.80f);
        GUI.Label(new Rect(panelX + 6f, coachY, panelW - 12f, 50f), coachText, _bodyStyle);
        _bodyStyle.wordWrap  = false;
        GUI.color = Color.white;

        // Counter table below
        float tableY = panelY + slotH + 6f;
        float tableH = Mathf.Clamp(sh - tableY - 20f, 200f, 400f);
        DrawCounterTablePanel(panelX, tableY, panelW, tableH, sw, attIdx);

        float mainW = sw - panelW - 44f;

        // ── HP bars ────────────────────────────────────────────────────────
        float hpW = mainW * 0.38f, hpH = 18f, hpY = sh * 0.12f;
        DrawHpBar(22f,               hpY, hpW, hpH, _s7_botHp,    "BOT", new Color(1f, 0.35f, 0.35f));
        DrawHpBar(mainW - 22f - hpW, hpY, hpW, hpH, _s7_playerHp, "YOU", new Color(0.3f, 0.8f, 1f));

        // ── Round label ────────────────────────────────────────────────────
        _bodyStyle.fontSize = Mathf.Clamp((int)(sw / 72f), 9, 13);
        GUI.color = new Color(0.6f, 0.6f, 0.7f, 0.55f);
        GUI.Label(new Rect(0, sh * 0.07f, mainW, 20f), $"BOT FIGHT  •  ROUND {_s7_round + 1} / {S7_ROUNDS}", _bodyStyle);
        GUI.color = Color.white;

        // ── Card layout geometry ───────────────────────────────────────────
        float cW   = Mathf.Clamp(mainW * 0.30f, 120f, 210f);
        float cH   = Mathf.Clamp(sh * 0.26f, 120f, 200f);
        float midX = mainW * 0.5f;
        float gap  = Mathf.Clamp(mainW * 0.10f, 42f, 80f);
        float attCardX = midX - gap * 0.5f - cW;
        float ctrCardX = midX + gap * 0.5f;
        float cardsY   = sh * 0.22f;

        switch (_s7_phase)
        {
            case S7Phase.Announce:
            {
                _headStyle.fontSize = Mathf.Clamp((int)(sw / 15f), 32, 68);
                GUI.color = new Color(1f, 0.72f, 0.1f);
                GUI.Label(new Rect(0, sh * 0.16f, mainW, 60f), "BOT WILL USE:", _headStyle);
                GUI.color = Color.white;

                float bigW = Mathf.Clamp(mainW * 0.38f, 160f, 280f);
                float bigH = Mathf.Clamp(sh * 0.30f, 140f, 220f);
                DrawFightCard(mainW * 0.5f - bigW * 0.5f, cardsY, bigW, bigH, S1_LABELS[attIdx], "INCOMING ATTACK", false, sw, large: true);

                float alpha = (Mathf.Sin(Time.time * 2.4f) + 1f) * 0.22f + 0.52f;
                _bodyStyle.fontSize = Mathf.Clamp((int)(sw / 52f), 12, 20);
                GUI.color = new Color(0f, 0.85f, 1f, alpha);
                GUI.Label(new Rect(0, cardsY + bigH + 16f, mainW, 28f),
                    $"Counter with: {S1_LABELS[ctrIdx]}  — Get ready!", _bodyStyle);
                GUI.color = Color.white;
                break;
            }

            case S7Phase.WaitInput:
            {
                _headStyle.fontSize = Mathf.Clamp((int)(sw / 15f), 32, 68);
                GUI.color = new Color(0f, 1f, 0.85f);
                GUI.Label(new Rect(0, sh * 0.16f, mainW, 60f), "COUNTER IT!", _headStyle);
                GUI.color = Color.white;

                DrawFightCard(attCardX, cardsY, cW, cH, S1_LABELS[attIdx], "BOT ATTACKS", false, sw);

                bool spokenAny  = _s7_spokenCtrIdx >= 0;
                bool spokenGood = spokenAny && _s7_spokenCtrIdx == ctrIdx;
                string ctrLabel = spokenAny ? S1_LABELS[_s7_spokenCtrIdx] : "???";
                DrawFightCard(ctrCardX, cardsY, cW, cH, ctrLabel, "YOUR COUNTER", true, sw, isCorrect: spokenGood, hasInput: spokenAny);

                float vsGlow = (Mathf.Sin(Time.time * 2.8f) + 1f) * 0.5f;
                _headStyle.fontSize = Mathf.Clamp((int)(sw / 24f), 20, 40);
                GUI.color = Color.Lerp(new Color(1f, 0.75f, 0.1f, 0.6f), new Color(1f, 0.95f, 0.3f, 1f), vsGlow);
                GUI.Label(new Rect(attCardX + cW, cardsY + cH * 0.35f, gap, cH * 0.3f), "VS", _headStyle);
                GUI.color = Color.white;

                float beatFrac = Mathf.Clamp01((_s7_elapsed - _s7_lastBeatTime) / stage7BeatInterval);
                float goodFrac = Mathf.Clamp01(goodWindow / stage7BeatInterval);
                float exFrac   = Mathf.Clamp01(excellentWindow / stage7BeatInterval);
                bool  inGood   = beatFrac >= (1f - goodFrac);
                bool  inEx     = beatFrac >= (1f - exFrac);
                float glow     = Mathf.Pow(beatFrac, 2.2f);

                float barW = mainW * 0.72f, barH = 22f;
                float barX = mainW * 0.14f, barY = cardsY + cH + 20f;

                GUI.color = new Color(0f, 0f, 0f, 0.5f);
                GUI.DrawTexture(new Rect(barX, barY, barW, barH), _px);
                GUI.color = new Color(0.1f, 0.85f, 0.2f, 0.28f);
                GUI.DrawTexture(new Rect(barX + barW * (1f - goodFrac), barY, barW * goodFrac, barH), _px);
                GUI.color = new Color(0.2f, 1f, 0.3f, 0.55f);
                GUI.DrawTexture(new Rect(barX + barW * (1f - exFrac), barY, barW * exFrac, barH), _px);
                Color fillCol;
                if (_s7_beatFlash > 0f)
                    fillCol = Color.Lerp(new Color(1f, 0.85f, 0.1f, 0.92f), Color.white, (_s7_beatFlash / BEAT_FLASH_DUR) * 0.8f);
                else if (inGood)
                    fillCol = Color.Lerp(new Color(1f, 0.85f, 0.1f, 0.88f), new Color(0.2f, 1f, 0.35f, 0.92f),
                                         (beatFrac - (1f - goodFrac)) / Mathf.Max(goodFrac, 0.001f));
                else
                    fillCol = new Color(0f, 0.75f, 1f, Mathf.Lerp(0.42f, 0.78f, glow));
                GUI.color = fillCol;
                GUI.DrawTexture(new Rect(barX, barY, barW * beatFrac, barH), _px);
                GUI.color = new Color(1f, 1f, 1f, 0.22f);
                GUI.DrawTexture(new Rect(barX,        barY,        barW, 1.5f), _px);
                GUI.DrawTexture(new Rect(barX,        barY + barH, barW, 1.5f), _px);
                GUI.DrawTexture(new Rect(barX,        barY,        1.5f, barH), _px);
                GUI.DrawTexture(new Rect(barX + barW, barY,        1.5f, barH), _px);
                GUI.color = Color.white;

                string stateText; Color stateCol;
                if (_s7_beatFlash > 0f && !string.IsNullOrEmpty(_s7_gradeText))
                { stateText = _s7_gradeText; stateCol = new Color(1f, 0.55f, 0.1f); }
                else if (inEx)   { stateText = "NOW!";         stateCol = new Color(0.25f, 1f, 0.4f); }
                else if (inGood) { stateText = "SHOUT!";       stateCol = new Color(0.5f, 1f, 0.6f); }
                else if (beatFrac > 0.55f)
                {
                    float r = (beatFrac - 0.55f) / 0.45f;
                    stateText = "GET READY..."; stateCol = new Color(1f, Mathf.Lerp(0.65f, 0.92f, r), 0.15f, Mathf.Lerp(0.4f, 0.9f, r));
                }
                else { stateText = "WAIT..."; stateCol = new Color(1f, 1f, 1f, 0.22f); }
                _bodyStyle.fontSize = Mathf.Clamp((int)(sw / 30f), 22, 42);
                GUI.color = stateCol;
                GUI.Label(new Rect(0, barY + barH + 6f, mainW, 46f), stateText, _bodyStyle);

                if (_s7_gradeFade > 0f && _s7_beatFlash <= 0f && !string.IsNullOrEmpty(_s7_gradeText))
                {
                    float a   = Mathf.Clamp01(_s7_gradeFade / GRADE_FADE_DUR);
                    float rise = (1f - a) * 18f;
                    _headStyle.fontSize = Mathf.Clamp((int)(sw / 18f), 26, 52);
                    GUI.color = new Color(1f, 0.6f, 0.1f, a);
                    GUI.Label(new Rect(0, barY - 60f - rise, mainW, 56f), _s7_gradeText, _headStyle);
                }

                _bodyStyle.fontSize = Mathf.Clamp((int)(sw / 58f), 10, 17);
                GUI.color = new Color(0f, 0.85f, 1f, 0.52f);
                GUI.Label(new Rect(0, barY + barH + 52f, mainW, 24f),
                    $"1. Say \"{S1_LABELS[ctrIdx]}\"   2. Shout on the beat!", _bodyStyle);
                GUI.color = Color.white;
                break;
            }

            case S7Phase.WrongFeedback:
            {
                GUI.color = new Color(1f, 1f, 1f, 0.35f);
                DrawFightCard(attCardX, cardsY, cW, cH, S1_LABELS[attIdx], "BOT ATTACKED", false, sw);
                string badWord = _s7_wrongWord.Length > 0 ? _s7_wrongWord : "MISS";
                DrawFightCard(ctrCardX, cardsY, cW, cH, badWord, "YOU SAID", true, sw, isCorrect: false, hasInput: true);
                GUI.color = Color.white;

                GUI.color = new Color(0f, 0f, 0f, 0.72f);
                GUI.DrawTexture(new Rect(0, 0, mainW, sh), _px);
                GUI.color = Color.white;

                float bxW = Mathf.Clamp(mainW * 0.80f, 260f, 540f);
                float bxH = 230f;
                float bxX = mainW * 0.5f - bxW * 0.5f;
                float bxY = sh * 0.5f - bxH * 0.5f;

                GUI.color = new Color(0.06f, 0.03f, 0.12f, 0.98f);
                GUI.DrawTexture(new Rect(bxX, bxY, bxW, bxH), _px);
                GUI.color = new Color(1f, 0.22f, 0.22f, 0.90f);
                GUI.DrawTexture(new Rect(bxX, bxY, bxW, 4f), _px);

                _headStyle.fontSize = Mathf.Clamp((int)(sw / 20f), 28, 52);
                GUI.color = new Color(1f, 0.35f, 0.35f);
                GUI.Label(new Rect(bxX, bxY + 10f, bxW, 52f), "ATTACK LANDED!", _headStyle);

                string explain = _s7_wrongWord.Length > 0
                    ? $"\"{_s7_wrongWord}\" doesn't counter {S1_LABELS[attIdx]}.\n{S1_LABELS[attIdx]}  ▶  countered by  {S1_LABELS[ctrIdx]}"
                    : $"Missed the beat! Counter {S1_LABELS[attIdx]} with {S1_LABELS[ctrIdx]}.";
                _bodyStyle.fontSize = Mathf.Clamp((int)(sw / 46f), 14, 24);
                _bodyStyle.wordWrap = true;
                GUI.color = new Color(0.90f, 0.88f, 0.95f, 0.92f);
                GUI.Label(new Rect(bxX + 20f, bxY + 68f, bxW - 40f, 80f), explain, _bodyStyle);
                _bodyStyle.wordWrap = false;

                float pa = (Mathf.Sin(Time.time * 2.6f) + 1f) * 0.22f + 0.52f;
                _bodyStyle.fontSize = Mathf.Clamp((int)(sw / 60f), 10, 16);
                GUI.color = new Color(0f, 0.82f, 1f, pa);
                GUI.Label(new Rect(bxX, bxY + bxH - 32f, bxW, 24f),
                    $"Press SPACE to try again — say \"{S1_LABELS[ctrIdx]}\" then shout on beat!", _bodyStyle);
                GUI.color = Color.white;
                break;
            }

            case S7Phase.CorrectFlash:
            {
                DrawFightCard(attCardX, cardsY, cW, cH, S1_LABELS[attIdx], "BOT ATTACKED", false, sw);
                DrawFightCard(ctrCardX, cardsY, cW, cH, S1_LABELS[ctrIdx], "YOUR COUNTER", true, sw, isCorrect: true, hasInput: true);

                float vsG = (Mathf.Sin(Time.time * 2.8f) + 1f) * 0.5f;
                _headStyle.fontSize = Mathf.Clamp((int)(sw / 24f), 20, 40);
                GUI.color = Color.Lerp(new Color(0.2f, 1f, 0.4f, 0.7f), new Color(0.5f, 1f, 0.6f, 1f), vsG);
                GUI.Label(new Rect(attCardX + cW, cardsY + cH * 0.35f, gap, cH * 0.3f), "✓", _headStyle);
                GUI.color = Color.white;

                _headStyle.fontSize = Mathf.Clamp((int)(sw / 14f), 36, 72);
                GUI.color = new Color(0.2f, 1f, 0.4f);
                GUI.Label(new Rect(0, sh * 0.16f, mainW, 68f), "COUNTER HIT!", _headStyle);

                string flavor = S5_COUNTER_FLAVOR[attIdx];
                _bodyStyle.fontSize = Mathf.Clamp((int)(sw / 52f), 12, 20);
                _bodyStyle.wordWrap = true;
                GUI.color = new Color(0.75f, 0.90f, 0.78f, 0.85f);
                GUI.Label(new Rect(22f, cardsY + cH + 18f, mainW - 44f, 48f), flavor, _bodyStyle);
                _bodyStyle.wordWrap = false;

                float prog   = Mathf.Clamp01(_s7_phaseTimer / S7_CORRECT_FLASH);
                float stripW = mainW * 0.60f, stripH = 6f;
                float stripX = mainW * 0.5f - stripW * 0.5f;
                float stripY = cardsY + cH + 80f;
                GUI.color = new Color(0.08f, 0.08f, 0.14f, 0.80f);
                GUI.DrawTexture(new Rect(stripX, stripY, stripW, stripH), _px);
                GUI.color = new Color(0.2f, 1f, 0.45f, 0.85f);
                GUI.DrawTexture(new Rect(stripX, stripY, stripW * prog, stripH), _px);
                GUI.color = Color.white;
                break;
            }

            case S7Phase.Lost:
            {
                GUI.color = new Color(0f, 0f, 0f, 0.75f);
                GUI.DrawTexture(new Rect(0, 0, mainW, sh), _px);
                GUI.color = Color.white;

                float bxW = Mathf.Clamp(mainW * 0.80f, 260f, 540f);
                float bxH = 200f;
                float bxX = mainW * 0.5f - bxW * 0.5f;
                float bxY = sh * 0.5f - bxH * 0.5f;

                GUI.color = new Color(0.06f, 0.03f, 0.12f, 0.98f);
                GUI.DrawTexture(new Rect(bxX, bxY, bxW, bxH), _px);
                GUI.color = new Color(1f, 0.22f, 0.22f, 0.90f);
                GUI.DrawTexture(new Rect(bxX, bxY, bxW, 4f), _px);

                _headStyle.fontSize = Mathf.Clamp((int)(sw / 14f), 36, 72);
                GUI.color = new Color(1f, 0.35f, 0.35f);
                GUI.Label(new Rect(bxX, bxY + 12f, bxW, 60f), "YOU WERE BEATEN!", _headStyle);

                _bodyStyle.fontSize = Mathf.Clamp((int)(sw / 46f), 14, 22);
                GUI.color = new Color(0.90f, 0.88f, 0.95f, 0.88f);
                GUI.Label(new Rect(bxX, bxY + 80f, bxW, 44f), "Watch the coaching hints — say the right word, then shout on beat!", _bodyStyle);

                float pa = (Mathf.Sin(Time.time * 2.6f) + 1f) * 0.22f + 0.52f;
                _bodyStyle.fontSize = Mathf.Clamp((int)(sw / 60f), 10, 16);
                GUI.color = new Color(0f, 0.82f, 1f, pa);
                GUI.Label(new Rect(bxX, bxY + bxH - 32f, bxW, 24f), "Press SPACE to try again", _bodyStyle);
                GUI.color = Color.white;
                break;
            }
        }

        DrawBackButton();
    }
}
