using UnityEngine;

public enum CommentaryEvent
{
    Excellent  = 0,
    Good       = 1,
    Parry      = 2,
    Dodge      = 3,
    Boom       = 4,
    Combo      = 5,
    Knockout   = 6,
    BadTiming  = 7,
    HeavyHit   = 8,
    LowHealth  = 9,
}

/// Client-side only — no networking. Drop on any persistent GameObject in Level scene.
/// Other systems call CommentaryManager.Instance?.Trigger(evt) from their ClientRpc/TargetRpc callbacks.
public class CommentaryManager : MonoBehaviour
{
    public static CommentaryManager Instance { get; private set; }

    [Header("Display")]
    [Range(1f,  4f)] public float holdDuration  = 2f;
    [Range(0.08f, 0.4f)] public float slideInDuration = 0.14f;
    [Range(0.15f, 0.6f)] public float fadeOutDuration = 0.30f;
    [Range(0.5f, 4f)] public float globalCooldown = 1.4f;
    [Tooltip("Vertical position as fraction of screen height from top.")]
    [Range(0.05f, 0.5f)] public float screenY = 0.12f;

    [Header("Lines — Excellent Timing")]
    public string[] excellentLines =
    {
        "HE HEARD THE BEAT IN HIS SOUL!",
        "THAT PUNCH WAS ON SPOTIFY!",
        "CONDUCTED BY BEETHOVEN HIMSELF!",
        "HE IS LITERALLY DANCING ON HIS FACE!",
        "RIGHT ON THE DROP!",
        "TIMED TO THE MILLISECOND!"
    };

    [Header("Lines — Good Timing")]
    public string[] goodLines =
    {
        "NOT BAD! NOT BAD AT ALL!",
        "HIS MOTHER WOULD BE PROUD!",
        "ADEQUATE! VERY ADEQUATE!",
        "WE HAVE SEEN WORSE!",
        "HE IS GETTING WARMER!"
    };

    [Header("Lines — Parry")]
    public string[] parryLines =
    {
        "CAGED! LIKE A HAMSTER!",
        "HE WALKED RIGHT INTO THAT ONE!",
        "HE READ HIM LIKE A CHILDREN'S BOOK!",
        "THE AUDACITY TO TRY THAT MOVE!",
        "DENIED! ABSOLUTELY DENIED!",
        "SOMEBODY CALL A DOCTOR AND A MUSICIAN!"
    };

    [Header("Lines — Dodge")]
    public string[] dodgeLines =
    {
        "YOU CANNOT HIT WHAT YOU CANNOT SEE!",
        "WHERE DID HE GO?!",
        "SLIPPERY AS A WET FLOOR SIGN!",
        "HE JUST MOONWALKED OUT OF DANGER!"
    };

    [Header("Lines — Boom / Unbreakable")]
    public string[] boomLines =
    {
        "NOTHING IN THIS UNIVERSE STOPS THAT!",
        "PHYSICS HAS LEFT THE CHAT!",
        "UNSTOPPABLE MEETS IMMOVABLE — IMMOVABLE LOST!",
        "THAT BLOCK DID NOT STAND A CHANCE AND NEITHER DID THE WALL BEHIND IT!",
        "THE CROWD'S INSURANCE IS NOT GOING TO COVER THIS!"
    };

    [Header("Lines — Combo Chain")]
    public string[] comboLines =
    {
        "HE IS SPEEDRUNNING THIS MAN'S FACE!",
        "ONE! TWO! THREE! SOMEBODY COUNT HIM OUT!",
        "MATHEMATICALLY SPEAKING, THAT IS A LOT OF PUNCHES!",
        "THE COMBO IS COOKING WITH GAS!",
        "HE DID NOT COME HERE TO MAKE FRIENDS!"
    };

    [Header("Lines — Knockout")]
    public string[] knockoutLines =
    {
        "AND HE IS TAKING A NAP! RIGHT HERE! ON THE CANVAS!",
        "LIGHTS OUT! SOMEBODY CALL HIS WIFI PASSWORD!",
        "HE IS SLEEPING BETTER THAN I DID LAST NIGHT!",
        "GOOD NIGHT, SWEET PRINCE!",
        "HE IS DONE! COOKED! MEDIUM RARE!",
        "SOMEBODY CALL HIS MANAGER!"
    };

    [Header("Lines — Bad Timing")]
    public string[] badTimingLines =
    {
        "OH... HE TRIED.",
        "SLIGHTLY OFF. LIKE STORE BRAND CEREAL.",
        "THE BEAT DISAGREED WITH HIM.",
        "HE HEARD A DIFFERENT SONG APPARENTLY.",
        "NOT THE WORST I HAVE SEEN. THIS WEEK."
    };

    [Header("Lines — Heavy Hit")]
    public string[] heavyHitLines =
    {
        "HIS ANCESTORS FELT THAT!",
        "THAT IS GOING TO REQUIRE DENTAL WORK!",
        "SIR, ARE YOU OKAY? NO. NO HE IS NOT.",
        "THE DOCTOR HAS ENTERED THE BUILDING!",
        "FRAME ONE OF THAT IS SOMEONE'S WALLPAPER NOW!"
    };

    [Header("Lines — Low Health")]
    public string[] lowHealthLines =
    {
        "HE IS OPERATING ON VIBES AND PRAYER!",
        "MEDICALLY SPEAKING, THIS IS CONCERNING!",
        "HE HAS THE HEALTH BAR OF A TUTORIAL ENEMY!",
        "ONE MORE HIT AND HE RESPAWNS!",
        "RUNNING ON FUMES AND STUBBORNNESS!"
    };

    [Header("Audio — Commentary Source")]
    public AudioSource commentaryAudio;
    [Range(0f, 3f)] public float voiceVolume = 1.5f;

    [Header("Audio — Voice Clips (one or more per event, picked randomly)")]
    public AudioClip[] excellentClips;
    public AudioClip[] goodClips;
    public AudioClip[] parryClips;
    public AudioClip[] dodgeClips;
    public AudioClip[] boomClips;
    public AudioClip[] comboClips;
    public AudioClip[] knockoutClips;
    public AudioClip[] badTimingClips;
    public AudioClip[] heavyHitClips;
    public AudioClip[] lowHealthClips;

    // ── Priority per event (higher = interrupts lower) ────────────────────
    private static readonly int[] Priority = { 4, 2, 6, 2, 6, 3, 10, 1, 4, 3 };

    // ── Per-event cooldown durations (seconds) ────────────────────────────
    private static readonly float[] CooldownDur = { 3f, 2f, 5f, 2f, 6f, 3f, 99f, 2f, 3f, 8f };

    // ── Runtime ───────────────────────────────────────────────────────────
    private enum State { Idle, SlideIn, Hold, FadeOut }

    private State  _state           = State.Idle;
    private string _currentLine     = "";
    private int    _currentPriority = 0;
    private float  _timer           = 0f;
    private float  _alpha           = 0f;
    private float  _slideY          = 0f;    // pixel offset, animates from –80 → 0
    private float  _lastFireTime    = -999f;
    private bool   _lowHealthFired  = false;

    private readonly float[] _cooldowns = new float[10];
    private readonly int[]   _lastIdx   = new int[10]; // avoids same line twice in a row

    private Texture2D  _px;
    private GUIStyle   _style;

    // ─────────────────────────────────────────────────────────────────────
    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        for (int i = 0; i < _lastIdx.Length; i++) _lastIdx[i] = -1;
    }

    void Update()
    {
        for (int i = 0; i < _cooldowns.Length; i++)
            if (_cooldowns[i] > 0f) _cooldowns[i] -= Time.deltaTime;

        TickDisplay();
        MonitorLowHealth();
    }

    // ── Public API ────────────────────────────────────────────────────────

    /// Trigger a commentary event. forceInterrupt bypasses global cooldown and priority.
    public void Trigger(CommentaryEvent evt, bool forceInterrupt = false)
    {
        int idx      = (int)evt;
        int priority = Priority[idx];

        if (!forceInterrupt)
        {
            if (Time.time - _lastFireTime < globalCooldown) return;
            if (_cooldowns[idx] > 0f) return;
            if (_state != State.Idle && priority <= _currentPriority) return;
        }

        string line = PickLine(evt, idx);
        if (string.IsNullOrEmpty(line)) return;

        AudioClip clip = PickClip(evt);
        BeginDisplay(line, priority, clip);
        _cooldowns[idx] = CooldownDur[idx];
        _lastFireTime   = Time.time;
    }

    /// Called from RpcLogCombatTrade — parses move strings so callers stay clean.
    public void OnTradeResolved(string p1Move, int p1Dmg, string p2Move, int p2Dmg,
                                bool comboActive)
    {
        // Parry
        if (p1Move.Contains("PARRY") || p2Move.Contains("PARRY"))
        { Trigger(CommentaryEvent.Parry); return; }

        // Boom
        if (p1Move.Contains("BOOM") || p2Move.Contains("BOOM"))
        { Trigger(CommentaryEvent.Boom); return; }

        // Combo
        if (comboActive && (p1Dmg > 0 || p2Dmg > 0))
        { Trigger(CommentaryEvent.Combo); return; }

        // Heavy damage
        int maxDmg = Mathf.Max(p1Dmg, p2Dmg);
        if (maxDmg >= 15) { Trigger(CommentaryEvent.HeavyHit); return; }
    }

    // ── Internal ──────────────────────────────────────────────────────────

    private void MonitorLowHealth()
    {
        if (!RhythmRoundManager.Instance || !RhythmRoundManager.Instance.isRoundActive)
        { _lowHealthFired = false; return; }

        foreach (var player in GameManager.players)
        {
            if (player == null) continue;
            var pc = player.GetComponent<PlayerCombat>();
            if (pc == null || pc.IsDead) continue;

            float frac = (float)pc.CurrentHealth / pc.MaxHealth;
            if (frac < 0.25f && !_lowHealthFired)
            {
                _lowHealthFired = true;
                Trigger(CommentaryEvent.LowHealth);
                return;
            }
        }
    }

    private string PickLine(CommentaryEvent evt, int idx)
    {
        string[] pool = evt switch
        {
            CommentaryEvent.Excellent => excellentLines,
            CommentaryEvent.Good      => goodLines,
            CommentaryEvent.Parry     => parryLines,
            CommentaryEvent.Dodge     => dodgeLines,
            CommentaryEvent.Boom      => boomLines,
            CommentaryEvent.Combo     => comboLines,
            CommentaryEvent.Knockout  => knockoutLines,
            CommentaryEvent.BadTiming => badTimingLines,
            CommentaryEvent.HeavyHit  => heavyHitLines,
            CommentaryEvent.LowHealth => lowHealthLines,
            _                         => null
        };

        if (pool == null || pool.Length == 0) return "";

        // Avoid repeating the same line twice in a row
        int pick;
        if (pool.Length == 1)
        {
            pick = 0;
        }
        else
        {
            do { pick = Random.Range(0, pool.Length); }
            while (pick == _lastIdx[idx]);
        }
        _lastIdx[idx] = pick;
        return pool[pick];
    }

    private void BeginDisplay(string line, int priority, AudioClip clip = null)
    {
        _currentLine     = line;
        _currentPriority = priority;
        _state           = State.SlideIn;
        _timer           = slideInDuration;
        _slideY          = -80f;
        _alpha           = 0f;

        if (clip != null && commentaryAudio != null)
            commentaryAudio.PlayOneShot(clip, voiceVolume);
    }

    private AudioClip PickClip(CommentaryEvent evt)
    {
        AudioClip[] pool = evt switch
        {
            CommentaryEvent.Excellent => excellentClips,
            CommentaryEvent.Good      => goodClips,
            CommentaryEvent.Parry     => parryClips,
            CommentaryEvent.Dodge     => dodgeClips,
            CommentaryEvent.Boom      => boomClips,
            CommentaryEvent.Combo     => comboClips,
            CommentaryEvent.Knockout  => knockoutClips,
            CommentaryEvent.BadTiming => badTimingClips,
            CommentaryEvent.HeavyHit  => heavyHitClips,
            CommentaryEvent.LowHealth => lowHealthClips,
            _                         => null
        };
        if (pool == null || pool.Length == 0) return null;
        return pool[Random.Range(0, pool.Length)];
    }

    private void TickDisplay()
    {
        if (_state == State.Idle) return;

        _timer -= Time.deltaTime;

        switch (_state)
        {
            case State.SlideIn:
                float t = 1f - Mathf.Clamp01(_timer / slideInDuration);
                _slideY = Mathf.Lerp(-80f, 0f, t);
                _alpha  = t;
                if (_timer <= 0f) { _state = State.Hold; _timer = holdDuration; _slideY = 0f; _alpha = 1f; }
                break;

            case State.Hold:
                if (_timer <= 0f) { _state = State.FadeOut; _timer = fadeOutDuration; }
                break;

            case State.FadeOut:
                _alpha = Mathf.Clamp01(_timer / fadeOutDuration);
                if (_timer <= 0f) { _state = State.Idle; _alpha = 0f; }
                break;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    void OnGUI()
    {
        if (_state == State.Idle || _alpha < 0.01f) return;

        if (_px == null)
        {
            _px = new Texture2D(1, 1);
            _px.SetPixel(0, 0, Color.white);
            _px.Apply();
        }

        if (_style == null)
        {
            _style = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
        }

        float sw       = Screen.width;
        float sh       = Screen.height;
        _style.fontSize = Mathf.Clamp((int)(sw / 20f), 26, 58);

        Vector2 size = _style.CalcSize(new GUIContent(_currentLine));
        float w  = size.x + 64f;
        float h  = size.y + 22f;
        float x  = sw * 0.5f - w * 0.5f;
        float y  = sh * screenY + _slideY;

        // Dark background
        GUI.color = new Color(0f, 0f, 0f, 0.78f * _alpha);
        GUI.DrawTexture(new Rect(x, y, w, h), _px);

        // Red accent bars (matches the ring rope aesthetic)
        GUI.color = new Color(0.85f, 0.04f, 0.03f, _alpha);
        GUI.DrawTexture(new Rect(x,         y, 4f, h), _px); // left
        GUI.DrawTexture(new Rect(x + w - 4, y, 4f, h), _px); // right
        GUI.DrawTexture(new Rect(x,         y, w,  3f), _px); // top
        GUI.DrawTexture(new Rect(x,         y + h - 3f, w, 3f), _px); // bottom

        // Text
        _style.normal.textColor = Color.white;
        GUI.color = new Color(1f, 1f, 1f, _alpha);
        GUI.Label(new Rect(x, y, w, h), _currentLine, _style);

        GUI.color = Color.white;
    }
}
