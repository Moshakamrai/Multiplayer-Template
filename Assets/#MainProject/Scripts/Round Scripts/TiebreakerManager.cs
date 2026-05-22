using Mirror;
using UnityEngine;
using UnityEngine.Rendering;
using System.Collections;
using System.Collections.Generic;

// Inspector setup:
//   tiebreakerPostProcess — Volume named "TiebreakerVolume" (weight=0, ColorAdjustments sat=-100)
//   Music pitch follows Time.timeScale automatically — no separate audio clip needed.
public class TiebreakerManager : NetworkBehaviour
{
    public static TiebreakerManager Instance;

    [Header("Post Processing")]
    public Volume tiebreakerPostProcess;
    public Volume mainVolume; // assign MainVolume here — we'll disable it during tiebreaker

    [Header("SFX")]
    public AudioClip slowMoEnterSfx; // drag a whoosh/warp clip here
    private AudioSource _sfxSource;

    [SyncVar] public bool IsTiebreakerActive = false;
    private string _tiedCardName = ""; // card that triggered tiebreaker (needs to be blocked)

    // ── Word Burst config ──────────────────────────────────────────────────────
    private static readonly string[] WORD_POOL =
        { "punch", "flank", "hook", "crush", "block", "cage" };

    private const int   WORD_COUNT     = 6;
    private const float COUNTDOWN_DUR  = 1.5f;
    private const float WORD_WINDOW    = 1.6f;   // rapid-fire — each word is tight
    private const float PEAK_MIN       = 0.55f;  // peak can land anywhere in the window
    private const float PEAK_MAX       = 1.30f;
    private const float BETWEEN_DELAY  = 0.05f;  // nearly instant gap so words hammer fast
    private const int   TIEBREAKER_DMG = 40;
    private const float SLOW_TIMESCALE = 0.1f;

    // ── Smooth slow-motion ────────────────────────────────────────────────────
    private float _targetTimeScale = 1.0f;
    private const float TIMESCALE_SPEED = 3.5f;

    // ── Color burst ───────────────────────────────────────────────────────────
    private float _colorBurstEnd = -1f;
    private const float COLOR_BURST_DUR = 1.2f;

    private enum Phase { Countdown, WordActive, BetweenWords, Done }
    private Phase _phase;

    private string[] _sequence;
    private int      _wordIndex;
    private float    _phaseTimer;
    private float    _wordX, _wordY;
    private float    _wordPeak;          // randomized per word so timing varies
    private float    _totalRealElapsed;

    // Per-word tracking
    private bool  _humanSaidWord = false;
    private bool  _botSaidWord   = false;
    private float _botSayAtTime  = 0f;

    // Flash feedback
    private float _humanFlashEnd = -1f;
    private float _botFlashEnd   = -1f;

    // Decoy words — fake words to create visual noise
    private struct DecoyWord { public string text; public float x, y, endTime; }
    private readonly DecoyWord[] _decoys    = new DecoyWord[3];
    private int                  _decoyCount = 0;

    // Character lerp + animation loop during tiebreaker
    private Transform[]          _playerTransforms;
    private Vector3[]            _playerStartPos;
    private CharacterController[] _playerCCs;

    // Results
    private readonly List<float> _humanOffsets = new List<float>();
    private readonly List<float> _botOffsets   = new List<float>();
    private int _humanCorrect = 0;
    private int _botCorrect   = 0;

    private bool        _effectsApplied = false;
    private AudioSource _normalMusic;
    private Texture2D   _whiteTex;

    // ── Lifecycle ──────────────────────────────────────────────────────────────
    private void Awake() { if (Instance == null) Instance = this; }

    private void Start()
    {
        if (BeatAnalyzer.Instance != null)
            _normalMusic = BeatAnalyzer.Instance.audioSource;

        if (tiebreakerPostProcess == null)
        {
            GameObject go = GameObject.Find("TiebreakerVolume");
            if (go != null) tiebreakerPostProcess = go.GetComponent<Volume>();
        }

        if (mainVolume == null)
        {
            GameObject go = GameObject.Find("MainVolume");
            if (go != null) mainVolume = go.GetComponent<Volume>();
        }

        if (tiebreakerPostProcess != null) tiebreakerPostProcess.weight = 0f;
        if (mainVolume != null) mainVolume.weight = 1f; // ensure it's on at start

        _sfxSource = gameObject.AddComponent<AudioSource>();
        _sfxSource.playOnAwake = false;
        _sfxSource.spatialBlend = 0f; // 2D so it's always audible
    }

    // ── Called by RhythmRoundManager on same-move clash ────────────────────────
    public void StartTiebreaker(string tiedCard = "")
    {
        if (IsTiebreakerActive) return;

        _tiedCardName = tiedCard; // store for blocking after tiebreaker

        var pool = new List<string>(WORD_POOL);
        _sequence = new string[WORD_COUNT];
        for (int i = 0; i < WORD_COUNT; i++)
        {
            int r = Random.Range(0, pool.Count);
            _sequence[i] = pool[r];
            pool.RemoveAt(r);
        }

        _wordIndex         = 0;
        _phaseTimer        = 0f;
        _totalRealElapsed  = 0f;
        _phase             = Phase.Countdown;
        _humanOffsets.Clear();
        _botOffsets.Clear();
        _humanCorrect   = 0;
        _botCorrect     = 0;
        _colorBurstEnd  = -1f;
        _decoyCount     = 0;
        _effectsApplied = false;
        IsTiebreakerActive = true;

        ApplyTiebreakerEffects();
        if (NetworkServer.active) RpcOnTiebreakerStart();
    }

    [ClientRpc]
    private void RpcOnTiebreakerStart()
    {
        if (!_effectsApplied) ApplyTiebreakerEffects();
    }

    private void ApplyTiebreakerEffects()
    {
        _effectsApplied = true;

        if (_normalMusic == null && BeatAnalyzer.Instance != null)
            _normalMusic = BeatAnalyzer.Instance.audioSource;

        // Snap instantly — ConsumeNextMove() fires right after this returns, so the
        // attack animation starts in slow-mo without us needing to replay it.
        Time.timeScale   = SLOW_TIMESCALE;
        _targetTimeScale = SLOW_TIMESCALE;

        // Disable main volume so tiebreaker B&W doesn't blend with normal colors
        if (mainVolume != null)
            mainVolume.weight = 0f;

        if (tiebreakerPostProcess != null)
            tiebreakerPostProcess.weight = 1f;

        // Warp-in sound (plays at real speed — pitch not tied to timeScale)
        if (_sfxSource != null && slowMoEnterSfx != null)
        {
            _sfxSource.pitch = 1f;
            _sfxSource.PlayOneShot(slowMoEnterSfx);
        }

        // Cache player transforms for the converging lerp
        var players = new List<PlayerController>(GameManager.players);
        _playerTransforms = new Transform[players.Count];
        _playerStartPos   = new Vector3[players.Count];
        _playerCCs        = new CharacterController[players.Count];

        for (int i = 0; i < players.Count; i++)
        {
            if (players[i] == null) continue;
            _playerTransforms[i] = players[i].transform;
            _playerStartPos[i]   = players[i].transform.position;
            _playerCCs[i]        = players[i].GetComponent<CharacterController>();
            if (_playerCCs[i] != null) _playerCCs[i].enabled = false;
        }
    }

    // ── Update ─────────────────────────────────────────────────────────────────
    private void Update()
    {
        // Always: smooth timeScale lerp + music pitch
        Time.timeScale = Mathf.Lerp(Time.timeScale, _targetTimeScale,
            Time.unscaledDeltaTime * TIMESCALE_SPEED);
        if (_normalMusic != null)
            _normalMusic.pitch = Time.timeScale;

        if (!IsTiebreakerActive)
        {
            UpdateColorBurst();
            return;
        }

        if (!_effectsApplied) ApplyTiebreakerEffects();

        float udt = Time.unscaledDeltaTime;
        _phaseTimer       += udt;
        _totalRealElapsed += udt;

        UpdateColorBurst();
        UpdatePlayerLerp();

        switch (_phase)
        {
            case Phase.Countdown:
                if (_phaseTimer >= COUNTDOWN_DUR)
                {
                    _phase = Phase.WordActive;
                    _phaseTimer = 0f;
                    BeginWord();
                }
                break;

            case Phase.WordActive:
                if (NetworkServer.active && !_botSaidWord && _phaseTimer >= _botSayAtTime)
                {
                    _botSaidWord = true;
                    _botOffsets.Add(Mathf.Abs(_botSayAtTime - _wordPeak));
                    _botCorrect++;
                    _botFlashEnd = Time.unscaledTime + 0.5f;
                }
                if (_phaseTimer >= WORD_WINDOW)
                {
                    _phase = Phase.BetweenWords;
                    _phaseTimer = 0f;
                }
                break;

            case Phase.BetweenWords:
                if (_phaseTimer >= BETWEEN_DELAY)
                {
                    _wordIndex++;
                    if (_wordIndex >= WORD_COUNT)
                    {
                        _phase = Phase.Done;
                        EndTiebreaker();
                    }
                    else
                    {
                        _phase = Phase.WordActive;
                        _phaseTimer = 0f;
                        BeginWord();
                    }
                }
                break;
        }
    }

    // Color burst: quickly drop B&W weight (snap to color), then slowly fade back to B&W
    // During the burst, the main volume's saturation shows through, creating a color flash.
    private void UpdateColorBurst()
    {
        if (tiebreakerPostProcess == null) return;

        if (Time.unscaledTime < _colorBurstEnd)
        {
            float elapsed = COLOR_BURST_DUR - (_colorBurstEnd - Time.unscaledTime);
            float t = Mathf.Clamp01(elapsed / COLOR_BURST_DUR);
            // Quick drop to 0 (full color) in first 12%, slow return to 1 (full B&W) in remaining 88%
            tiebreakerPostProcess.weight = t < 0.12f
                ? (1f - t / 0.12f)         // drop weight fast → color shows
                : (t - 0.12f) / 0.88f;    // slowly increase weight back → fade to B&W
        }
        else if (IsTiebreakerActive)
        {
            tiebreakerPostProcess.weight = 1f;
        }
    }

    // Lerp both fighters toward each other over the segment duration
    private void UpdatePlayerLerp()
    {
        if (_playerTransforms == null || _playerTransforms.Length < 2) return;
        if (_playerTransforms[0] == null || _playerTransforms[1] == null) return;
        if (_playerStartPos == null) return;

        float totalDur = COUNTDOWN_DUR + WORD_COUNT * (WORD_WINDOW + BETWEEN_DELAY);
        float progress = Mathf.Clamp01(_totalRealElapsed / totalDur);
        // SmoothStep so the advance feels weighted, not linear — stop at 55% gap
        float amount = Mathf.SmoothStep(0f, 0.55f, progress);

        Vector3 mid = (_playerStartPos[0] + _playerStartPos[1]) * 0.5f;
        for (int i = 0; i < 2 && i < _playerTransforms.Length; i++)
            _playerTransforms[i].position = Vector3.Lerp(_playerStartPos[i], mid, amount);
    }

    private void BeginWord()
    {
        _humanSaidWord = false;
        _botSaidWord   = false;

        _wordX = Random.Range(240f, Screen.width  - 240f);
        _wordY = Random.Range(220f, Screen.height - 220f);

        // Each word peaks at a different point — player must read the bar, not muscle-memory
        _wordPeak = Random.Range(PEAK_MIN, PEAK_MAX);

        // Bot spread scaled to window — ±0.35s keeps it beatable but not trivial
        bool botSucceeds = Random.value < 0.65f;
        _botSayAtTime = botSucceeds
            ? Mathf.Clamp(_wordPeak + Random.Range(-0.35f, 0.35f), 0.15f, WORD_WINDOW - 0.1f)
            : WORD_WINDOW + 5f;

        // 1–2 decoy words, lifetime capped to the shorter window
        _decoyCount = Random.Range(1, 3);
        for (int i = 0; i < _decoyCount; i++)
        {
            string decoyText;
            do { decoyText = WORD_POOL[Random.Range(0, WORD_POOL.Length)]; }
            while (decoyText == _sequence[_wordIndex]);

            float dx, dy;
            int  tries = 0;
            do
            {
                dx = Random.Range(160f, Screen.width  - 160f);
                dy = Random.Range(160f, Screen.height - 160f);
                tries++;
            }
            while (tries < 20 && Mathf.Abs(dx - _wordX) < 200f && Mathf.Abs(dy - _wordY) < 110f);

            _decoys[i] = new DecoyWord
            {
                text    = decoyText,
                x       = dx,
                y       = dy,
                endTime = Time.unscaledTime + Random.Range(0.25f, WORD_WINDOW - 0.15f)
            };
        }
    }

    // ── Called from VoiceCommandManager ───────────────────────────────────────
    public void OnHumanVoicePartial(string text)
    {
        if (_phase != Phase.WordActive || _humanSaidWord) return;

        string target = _sequence[_wordIndex];
        foreach (string w in text.Split(' '))
        {
            bool matched = false;

            // Exact match
            if (w == target)
                matched = true;
            // Similarity threshold
            else if (GetSimilarity(w, target) > 0.75f)
                matched = true;
            // Phonetic variants for Hook (sounds like "book", "look", "cook", etc.)
            else if (target == "hook" && (w == "book" || w == "look" || w == "cook" || w == "took" || w == "nook"))
                matched = true;
            // Phonetic variants for Crush (sounds like "crash", "crush", "crunch", etc.)
            else if (target == "crush" && (w == "crash" || w == "crunch" || w == "crust" || w == "krush" || w == "brush"))
                matched = true;

            if (matched)
            {
                _humanSaidWord = true;
                _humanOffsets.Add(Mathf.Abs(_phaseTimer - _wordPeak));
                _humanCorrect++;
                _humanFlashEnd = Time.unscaledTime + 0.5f;
                _colorBurstEnd = Time.unscaledTime + COLOR_BURST_DUR;
                break;
            }
        }
    }

    // ── End ────────────────────────────────────────────────────────────────────
    private void EndTiebreaker()
    {
        IsTiebreakerActive = false;

        float hAvg   = _humanOffsets.Count > 0 ? ListAvg(_humanOffsets) : 999f;
        float bAvg   = _botOffsets.Count   > 0 ? ListAvg(_botOffsets)   : 999f;
        float hScore = _humanCorrect * 1000f - hAvg * 100f;
        float bScore = _botCorrect   * 1000f - bAvg * 100f;
        bool humanWins = hScore >= bScore;

        var players = new List<PlayerController>(GameManager.players);
        foreach (var p in players)
        {
            if (p == null) continue;
            PlayerCombat pc = p.GetComponent<PlayerCombat>();
            if (pc == null) continue;
            bool isBot = p.GetComponent<BotController>() != null;
            if ( humanWins && isBot)  pc.TakeDamage(TIEBREAKER_DMG);
            if (!humanWins && !isBot) pc.TakeDamage(TIEBREAKER_DMG);

            // Block the tied card from being reused immediately
            if (!string.IsNullOrEmpty(_tiedCardName))
            {
                CardManager cm = pc.GetComponent<CardManager>();
                if (cm != null)
                    cm.justUsedTrigger = _tiedCardName;
            }
        }

        RhythmRoundManager.Instance?.ResumeAfterTiebreaker(_totalRealElapsed);

        RestoreTiebreakerEffects();
        if (NetworkServer.active) RpcOnTiebreakerEnd();
    }

    [ClientRpc]
    private void RpcOnTiebreakerEnd()
    {
        RestoreTiebreakerEffects();
    }

    private void RestoreTiebreakerEffects()
    {
        _effectsApplied  = false;
        _targetTimeScale = 1.0f; // smooth speed-up; pitch follows in Update

        // Restore main volume colors
        if (mainVolume != null)
            mainVolume.weight = 1f;

        if (tiebreakerPostProcess != null)
            tiebreakerPostProcess.weight = 0f;

        // Restore positions and re-enable CharacterControllers
        if (_playerTransforms != null)
        {
            for (int i = 0; i < _playerTransforms.Length; i++)
            {
                if (_playerTransforms[i] != null && _playerStartPos != null)
                    _playerTransforms[i].position = _playerStartPos[i];
                if (_playerCCs != null && i < _playerCCs.Length && _playerCCs[i] != null)
                    _playerCCs[i].enabled = true;
            }
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────
    private float ListAvg(List<float> list)
    {
        float s = 0f; foreach (float v in list) s += v; return s / list.Count;
    }

    private float GetSimilarity(string s, string t)
    {
        if (s == t) return 1f;
        return 1f - (float)LevenshteinDistance(s, t) / Mathf.Max(s.Length, t.Length);
    }

    private int LevenshteinDistance(string s, string t)
    {
        int n = s.Length, m = t.Length;
        int[,] d = new int[n + 1, m + 1];
        for (int i = 0; i <= n; d[i, 0] = i++) {}
        for (int j = 0; j <= m; d[0, j] = j++) {}
        for (int i = 1; i <= n; i++)
            for (int j = 1; j <= m; j++)
                d[i, j] = Mathf.Min(Mathf.Min(d[i-1,j]+1, d[i,j-1]+1),
                    d[i-1,j-1] + (t[j-1] == s[i-1] ? 0 : 1));
        return d[n, m];
    }

    // ── GUI ────────────────────────────────────────────────────────────────────
    private void OnGUI()
    {
        if (RhythmRoundManager.Instance != null && RhythmRoundManager.Instance.isShopPhase) return;
        if (!IsTiebreakerActive) return;

        if (_whiteTex == null)
        {
            _whiteTex = new Texture2D(1, 1);
            _whiteTex.SetPixel(0, 0, Color.white);
            _whiteTex.Apply();
        }

        // Dark overlay
        GUI.color = new Color(0f, 0f, 0f, 0.52f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _whiteTex);
        GUI.color = Color.white;

        float pulse = (Mathf.Sin(Time.unscaledTime * 5f) + 1f) * 0.5f;

        // ── Header ────────────────────────────────────────────────────────────
        GUIStyle hdr = new GUIStyle(GUI.skin.label)
        { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 42 };
        hdr.normal.textColor = Color.Lerp(Color.white, new Color(1f, 0.85f, 0f), pulse);
        GUI.Label(new Rect(0, 18f, Screen.width, 60f), "⚡  TIE BREAKER  ⚡", hdr);

        // ── Score ─────────────────────────────────────────────────────────────
        GUIStyle sc = new GUIStyle(GUI.skin.label)
        { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 20 };
        sc.normal.textColor = new Color(0.85f, 0.85f, 0.85f);
        GUI.Label(new Rect(0, 80f, Screen.width, 30f),
            $"YOU  {_humanCorrect}/{WORD_COUNT}    OPP  {_botCorrect}/{WORD_COUNT}", sc);

        // ── Countdown ─────────────────────────────────────────────────────────
        if (_phase == Phase.Countdown)
        {
            int num = Mathf.CeilToInt(COUNTDOWN_DUR - _phaseTimer);
            GUIStyle cs = new GUIStyle(GUI.skin.label)
            { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 130 };
            cs.normal.textColor = Color.Lerp(Color.yellow, Color.white,
                (Mathf.Sin(Time.unscaledTime * 8f) + 1f) * 0.5f);
            GUI.Label(new Rect(0, Screen.height / 2f - 100f, Screen.width, 200f),
                num > 0 ? num.ToString() : "GO!", cs);
            return;
        }

        // ── Decoy words (no ring, no bar — just visual noise) ─────────────────
        if (_phase == Phase.WordActive)
        {
            GUIStyle ds = new GUIStyle(GUI.skin.label)
            { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 46 };
            for (int i = 0; i < _decoyCount; i++)
            {
                float remaining = _decoys[i].endTime - Time.unscaledTime;
                if (remaining <= 0f) continue;
                float alpha = Mathf.Clamp01(remaining * 2f) * 0.4f;
                ds.normal.textColor = new Color(0.55f, 0.55f, 0.55f, alpha);
                GUI.Label(new Rect(_decoys[i].x - 190f, _decoys[i].y - 32f, 380f, 64f),
                    _decoys[i].text.ToUpper(), ds);
            }
        }

        // ── Active target word ────────────────────────────────────────────────
        if (_phase == Phase.WordActive && _wordIndex < WORD_COUNT)
        {
            float tNorm   = _phaseTimer / WORD_WINDOW;          // 0→1 across window
            float peakN   = _wordPeak   / WORD_WINDOW;          // peak's position on bar (0→1)
            string word   = _sequence[_wordIndex];

            // Word size: grows to peak, slightly shrinks after
            float sizeRatio = tNorm <= peakN
                ? tNorm / peakN
                : 1f - (tNorm - peakN) / (1f - peakN) * 0.35f;
            int fontSize = Mathf.RoundToInt(Mathf.Lerp(30f, 104f, sizeRatio));

            // Word color: blue→yellow approaching peak, yellow→red after
            Color wColor;
            if (Time.unscaledTime < _humanFlashEnd)
                wColor = Color.Lerp(new Color(0.1f, 1f, 0.45f), Color.white,
                    1f - (_humanFlashEnd - Time.unscaledTime) / 0.5f);
            else if (tNorm > peakN)
                wColor = Color.Lerp(Color.yellow, Color.red, (tNorm - peakN) / (1f - peakN));
            else
                wColor = Color.Lerp(new Color(0.5f, 0.6f, 1f), Color.yellow, tNorm / peakN);

            GUIStyle ws = new GUIStyle(GUI.skin.label)
            { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = fontSize };
            ws.normal.textColor = wColor;

            float bw = 420f, bh = fontSize + 28f;
            GUI.Label(new Rect(_wordX - bw / 2f, _wordY - bh / 2f, bw, bh),
                word.ToUpper(), ws);

            // ── Converging ring — shrinks from large onto the word at peak ────
            // As the ring closes in, the shout moment is approaching.
            if (tNorm <= peakN + 0.08f)
            {
                float rp    = Mathf.Clamp01(tNorm / peakN);  // 0=huge, 1=tight at peak
                float rSize = Mathf.Lerp(310f, 90f, rp);
                float rAlpha;
                if (tNorm <= peakN)
                    rAlpha = Mathf.Lerp(0.25f, 1.0f, rp);    // brighten as it closes in
                else
                    rAlpha = 1f - (tNorm - peakN) / 0.08f;   // quick fade after peak

                Color rc = Color.Lerp(new Color(0.35f, 0.65f, 1f), new Color(0.1f, 1f, 0.45f), rp);
                rc.a = rAlpha;
                float rx  = _wordX - rSize / 2f;
                float ry  = _wordY - rSize / 2f;
                float brd = Mathf.Lerp(5f, 2.5f, rp);
                GUI.color = rc;
                GUI.DrawTexture(new Rect(rx,              ry,              rSize, brd  ), _whiteTex);
                GUI.DrawTexture(new Rect(rx,              ry+rSize-brd,    rSize, brd  ), _whiteTex);
                GUI.DrawTexture(new Rect(rx,              ry,              brd,   rSize), _whiteTex);
                GUI.DrawTexture(new Rect(rx+rSize-brd,    ry,              brd,   rSize), _whiteTex);
                GUI.color = Color.white;
            }

            // ── Timing bar below the word ─────────────────────────────────────
            // Bar sweeps left→right across the word window.
            // A gold band marks the peak (sweet spot) — shout when the needle hits it.
            float barW = 340f, barH = 20f;
            float barX = _wordX - barW / 2f;
            float barY = _wordY + bh / 2f + 16f;

            // Background track
            GUI.color = new Color(0.1f, 0.1f, 0.1f, 0.92f);
            GUI.DrawTexture(new Rect(barX - 3f, barY - 3f, barW + 6f, barH + 6f), _whiteTex);

            // Gold peak zone (15% wide, centred on peak position)
            float pzW = barW * 0.15f;
            float pzX = barX + barW * peakN - pzW * 0.5f;
            GUI.color = new Color(1f, 0.85f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(pzX, barY, pzW, barH), _whiteTex);

            // Sweeping fill — blue→green up to peak, green→red after
            Color fillColor = tNorm <= peakN
                ? Color.Lerp(new Color(0.25f, 0.5f, 1f), new Color(0.1f, 1f, 0.3f), tNorm / peakN)
                : Color.Lerp(new Color(0.1f, 1f, 0.3f), new Color(1f, 0.15f, 0.1f), (tNorm - peakN) / (1f - peakN));
            GUI.color = fillColor;
            GUI.DrawTexture(new Rect(barX, barY, tNorm * barW, barH), _whiteTex);

            // Needle (white tick at current position)
            GUI.color = Color.white;
            float needleX = barX + tNorm * barW;
            GUI.DrawTexture(new Rect(needleX - 2f, barY - 4f, 4f, barH + 8f), _whiteTex);

            // "SHOUT NOW!" label — fades in at peak, fades out after
            float distFromPeak = Mathf.Abs(_phaseTimer - _wordPeak);
            if (distFromPeak < 0.16f && !_humanSaidWord)
            {
                float sa = 1f - distFromPeak / 0.28f;
                GUIStyle shout = new GUIStyle(GUI.skin.label)
                { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 36 };
                shout.normal.textColor = new Color(1f, 1f, 0f, sa);
                GUI.Label(new Rect(barX, barY + barH + 10f, barW, 52f), "SHOUT NOW!", shout);
            }

            // Bot confirm badge
            if (Time.unscaledTime < _botFlashEnd)
            {
                float ba = Mathf.Clamp01((_botFlashEnd - Time.unscaledTime) / 0.5f);
                GUIStyle bs = new GUIStyle(GUI.skin.label)
                { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold, fontSize = 20 };
                bs.normal.textColor = new Color(1f, 0.45f, 0.1f, ba);
                GUI.Label(new Rect(_wordX + bw / 2f + 10f, _wordY - 14f, 90f, 30f), "OPP ✓", bs);
            }
        }

        // ── Between-words hint ────────────────────────────────────────────────
        if (_phase == Phase.BetweenWords && _wordIndex < WORD_COUNT - 1)
        {
            GUIStyle nxt = new GUIStyle(GUI.skin.label)
            { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 22 };
            nxt.normal.textColor = new Color(0.65f, 0.65f, 0.65f,
                Mathf.Lerp(0f, 1f, _phaseTimer / BETWEEN_DELAY));
            GUI.Label(new Rect(0, Screen.height / 2f - 20f, Screen.width, 40f), "next...", nxt);
        }
    }
}
