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

    // ── Stage 1 runtime ────────────────────────────────────────────────────
    private static readonly string[] S1_LABELS = { "PUNCH", "BLAST", "HOOK", "BLOCK", "CAGE", "BOOM" };

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

        EnterStage(Stage.VoiceCheck);
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space)) SkipCurrentStage();

        switch (CurrentStage)
        {
            case Stage.VoiceCheck: UpdateStage0(); break;
            case Stage.ShadowBag:  UpdateStage1(); break;
            case Stage.TimingOnly: UpdateStage2(); break;
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
                break;
        }
    }

    private void ExitStage(Stage s)
    {
        if (s == Stage.ShadowBag && voskInstance != null)
            voskInstance.OnPartialResult -= Stage1HandlePartial;
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

    private void PlayBeat()
    {
        if (tutorialAudioSource != null && stage2BeatClip != null)
            tutorialAudioSource.PlayOneShot(stage2BeatClip);
    }

    // ══════════════════════════════════════════════════════════════════════
    // OnGUI
    // ══════════════════════════════════════════════════════════════════════
    void OnGUI()
    {
        EnsureStyles();
        switch (CurrentStage)
        {
            case Stage.VoiceCheck: DrawStage0(); break;
            case Stage.ShadowBag:  DrawStage1(); break;
            case Stage.TimingOnly: DrawStage2(); break;
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

        _headStyle.fontSize = Mathf.Clamp((int)(sw / 16f), 30, 64);
        GUI.color = _s2_complete ? new Color(0.2f, 1f, 0.4f) : Color.white;
        GUI.Label(new Rect(0, sh * 0.08f, sw, 80), _s2_complete ? "TIMING MASTERED!" : "TIMING ONLY", _headStyle);
        GUI.color = Color.white;

        _bodyStyle.fontSize = Mathf.Clamp((int)(sw / 44f), 16, 28);
        GUI.color = new Color(1f, 1f, 1f, 0.75f);
        GUI.Label(new Rect(0, sh * 0.18f, sw, 44), _s2_complete ? "MOVING ON..." : "SHOUT ON THE BEAT", _bodyStyle);
        GUI.color = Color.white;

        // Grade text
        if (_s2_gradeFade > 0f && !string.IsNullOrEmpty(_s2_lastGrade))
        {
            float alpha    = Mathf.Clamp01(_s2_gradeFade / GRADE_FADE_DUR);
            Color gradeCol = _s2_lastGrade.StartsWith("EX") ? new Color(0.2f, 1f, 0.4f, alpha)
                           : _s2_lastGrade == "GOOD"        ? new Color(0.4f, 0.85f, 1f, alpha)
                           :                                   new Color(1f, 0.3f, 0.2f, alpha);
            _headStyle.fontSize = Mathf.Clamp((int)(sw / 18f), 28, 56);
            GUI.color = gradeCol;
            GUI.Label(new Rect(0, sh * 0.35f, sw, 70), _s2_lastGrade, _headStyle);
            GUI.color = Color.white;
        }

        // Timing bar
        float barW = sw * 0.72f, barH = 28f;
        float barX = sw * 0.14f, barY = sh * 0.52f;

        // Track background
        GUI.color = new Color(1f, 1f, 1f, 0.08f);
        GUI.DrawTexture(new Rect(barX, barY, barW, barH), _px);

        // Good zone (green)
        float goodFrac = Mathf.Clamp01(goodWindow / stage2BeatInterval);
        float exFrac   = Mathf.Clamp01(excellentWindow / stage2BeatInterval);
        GUI.color = new Color(0.1f, 0.85f, 0.2f, 0.30f);
        GUI.DrawTexture(new Rect(barX + barW * (1f - goodFrac), barY, barW * goodFrac, barH), _px);

        // Excellent zone (bright green)
        GUI.color = new Color(0.2f, 1f, 0.3f, 0.60f);
        GUI.DrawTexture(new Rect(barX + barW * (1f - exFrac), barY, barW * exFrac, barH), _px);

        // Cursor
        float cursorFrac = Mathf.Clamp01((_s2_elapsed % stage2BeatInterval) / stage2BeatInterval);
        float cursorX    = barX + barW * cursorFrac - 3f;
        Color cursorCol  = _s2_beatFlash > 0f
            ? Color.Lerp(Color.white, new Color(1f, 0.85f, 0.1f), 1f - _s2_beatFlash / BEAT_FLASH_DUR)
            : new Color(1f, 0.85f, 0.1f);
        GUI.color = cursorCol;
        GUI.DrawTexture(new Rect(cursorX, barY - 7f, 6f, barH + 14f), _px);

        // Border
        GUI.color = new Color(1f, 1f, 1f, 0.28f);
        GUI.DrawTexture(new Rect(barX,           barY,           barW, 2f),   _px);
        GUI.DrawTexture(new Rect(barX,           barY + barH,    barW, 2f),   _px);
        GUI.DrawTexture(new Rect(barX,           barY,           2f,   barH), _px);
        GUI.DrawTexture(new Rect(barX + barW,    barY,           2f,   barH), _px);
        GUI.color = Color.white;

        // Beat dots (progress indicator)
        DrawBeatDots(barX, barY + barH + 16f, barW);

        // Hit counter
        _bodyStyle.fontSize = Mathf.Clamp((int)(sw / 36f), 20, 36);
        GUI.color = new Color(1f, 1f, 1f, 0.85f);
        GUI.Label(new Rect(0, sh * 0.70f, sw, 50), $"{_s2_hitCount} / {hitsToPassStage2} HITS", _bodyStyle);
        GUI.color = Color.white;

        DrawBackButton();
    }

    private void DrawBeatDots(float x, float y, float w)
    {
        float spacing = w / MAX_S2_BEATS;
        float dotSize = Mathf.Clamp(spacing - 4f, 5f, 16f);
        for (int i = 0; i < MAX_S2_BEATS; i++)
        {
            bool done = i < _s2_totalBeats;
            GUI.color = done ? new Color(0.3f, 0.9f, 0.4f, 0.8f) : new Color(1f, 1f, 1f, 0.18f);
            GUI.DrawTexture(new Rect(x + i * spacing, y, dotSize, dotSize), _px);
        }
        GUI.color = Color.white;
    }

    // ══════════════════════════════════════════════════════════════════════
    // Helpers
    // ══════════════════════════════════════════════════════════════════════
    private void DrawBackButton()
    {
        if (GUI.Button(new Rect(20, 20, 120, 36), "← BACK"))
            SceneManager.LoadScene(menuSceneName);
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
        if (voskInstance != null) voskInstance.OnPartialResult -= Stage1HandlePartial;
    }
}
