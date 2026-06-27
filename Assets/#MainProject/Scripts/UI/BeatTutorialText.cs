using UnityEngine;
using UnityEngine.UI;

/// New-player onboarding: a big world-space instruction panel BEHIND the bot that walks the player
/// through the basics one step at a time, advancing as they actually do each step:
///   1. "PICK A CARD"            → advances when a card is selected (a move is pending)
///   2. "WAIT FOR THE RING..."   → tells them to time it; flashes a TOO-EARLY warning if they
///                                  shout/fire before the beat window opens.
///   3. "NICE! KEEP THE RHYTHM"  → shown after a clean on-beat hit.
///
/// Visibility is governed by BeatCoach: it's up while the player still needs help, hides once they
/// hit the beat consistently, and returns if they start missing again. Works in VR and flat.
[DefaultExecutionOrder(10003)]
public class BeatTutorialText : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        var go = new GameObject("~BeatTutorialText");
        DontDestroyOnLoad(go);
        go.AddComponent<BeatTutorialText>();
    }

    private const float ABOVE_BOT  = 2.6f;   // metres above the bot's feet
    private const float BEHIND_BOT = 1.4f;   // metres further from the player (so it sits BEHIND the bot)
    private const float NICE_HOLD  = 1.6f;   // seconds to flash the "nice!" praise after a clean hit
    private const float EARLY_HOLD = 1.6f;   // seconds to flash the "too early" warning

    private enum Step { PickCard, TimeIt }
    private Step _step = Step.PickCard;

    private float _niceTimer;   // >0 while flashing the success praise
    private float _earlyTimer;  // >0 while flashing the too-early warning
    private float _lastTimingSeen = -999f;
    private float _lastEarlySeen  = -999f;

    private Canvas _canvas;
    private Text _line, _sub;
    private Image _bg;
    private Font _font;
    private float _alpha = 1f;
    private bool _built;

    private void Awake()
    {
        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
             ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
    }

    private void Update()
    {
        var rmm = RhythmRoundManager.Instance;
        Transform bot = LocalOpponent();
        PlayerCombat self = LocalSelf();
        Camera cam = CachedCamera.Main;

        // Always watch for events (so the timers/streaks stay live), but only DRAW when the coach
        // says this player still needs help and we have everything to position the panel.
        bool active = rmm != null && rmm.isRoundActive && bot != null && cam != null && self != null;

        WatchEvents();

        if (!active || !BeatCoach.CoachingVisible)
        {
            if (_built) _canvas.enabled = false;
            return;
        }

        if (!_built) Build();
        _canvas.enabled = true;

        AdvanceStep(self);
        PositionBehindBot(bot, cam);
        Render();
    }

    // Catch fresh success / too-early events and arm the matching flash timer.
    private void WatchEvents()
    {
        // Read the dedicated grade signal (not VRTimingText, which the element system clobbers with
        // status names right after a hit).
        if (PlayerCombat.VRGradeTime > _lastTimingSeen + 0.001f)
        {
            _lastTimingSeen = PlayerCombat.VRGradeTime;
            string r = PlayerCombat.VRGradeText;
            if (r == "EXCELLENT" || r == "GOOD") { _niceTimer = NICE_HOLD; _earlyTimer = 0f; }
        }
        if (PlayerCombat.VREarlyTime > _lastEarlySeen + 0.001f)
        {
            _lastEarlySeen = PlayerCombat.VREarlyTime;
            _earlyTimer = EARLY_HOLD;
        }
        if (_niceTimer  > 0f) _niceTimer  -= Time.deltaTime;
        if (_earlyTimer > 0f) _earlyTimer -= Time.deltaTime;
    }

    private void AdvanceStep(PlayerCombat self)
    {
        // Step 1 → 2: once a card is chosen, switch from "pick" to the timing coaching.
        // If the card slot empties again (e.g. cancelled), fall back to the pick prompt.
        bool hasCard = !string.IsNullOrEmpty(self.PendingMoveTrigger);
        _step = hasCard ? Step.TimeIt : Step.PickCard;
        _alpha = 1f;
    }

    private void Render()
    {
        // Transient flashes take priority over the steady step prompt.
        if (_earlyTimer > 0f)
        {
            _line.text  = "TOO EARLY!";
            _sub.text   = "Wait for the ring to close on the bot — fire ON the beat, not before.";
            _line.color = WithA(new Color(1f, 0.45f, 0.2f), 1f);
            ApplyTail();
            return;
        }
        if (_niceTimer > 0f)
        {
            _line.text  = "NICE! ON THE BEAT";
            _sub.text   = "Same card keeps playing — pick a new one to switch.";
            _line.color = WithA(new Color(0.3f, 1f, 0.45f), 1f);
            ApplyTail();
            return;
        }

        switch (_step)
        {
            case Step.PickCard:
                _line.text  = "PICK A CARD";
                _sub.text   = "Choose the move you want to play";
                _line.color = WithA(new Color(1f, 0.85f, 0.2f), _alpha);
                break;
            case Step.TimeIt:
                _line.text  = "WAIT FOR THE RING...";
                _sub.text   = "Fire right as the ring snaps shut on the bot — don't rush it!";
                _line.color = WithA(new Color(0.2f, 0.9f, 1f), _alpha);
                break;
        }
        ApplyTail();
    }

    // Shared sub-text + backing-panel tint, so every render path styles them the same.
    private void ApplyTail()
    {
        _sub.color = WithA(new Color(0.85f, 0.88f, 0.95f), _alpha);
        if (_bg != null) _bg.color = WithA(new Color(0.02f, 0.03f, 0.06f), 0.55f * _alpha);
    }

    private void PositionBehindBot(Transform bot, Camera cam)
    {
        // Sit above + slightly behind the bot (further from the player). Use the FIXED spawn-to-spawn
        // direction for placement and rotation so the panel doesn't spin as the bot tracks the player.
        Vector3 awayFromPlayer = FixedAwayFromPlayer();

        Vector3 pos = bot.position + Vector3.up * ABOVE_BOT + awayFromPlayer * BEHIND_BOT;
        transform.position = pos;
        transform.rotation = Quaternion.LookRotation(-awayFromPlayer); // face the player, fixed angle
    }

    // A stable "behind the bot" direction based on the scene spawn points, not the live camera.
    private Vector3 FixedAwayFromPlayer()
    {
        var rmm = RhythmRoundManager.Instance;
        if (rmm != null && rmm.playerStartPosition != null && rmm.botStartPosition != null)
        {
            Vector3 d = rmm.botStartPosition.position - rmm.playerStartPosition.position;
            d.y = 0f;
            if (d.sqrMagnitude > 0.0001f) return d.normalized;
        }

        // Fallback: use the current view direction (only used before spawn points are set).
        Camera cam = CachedCamera.Main;
        if (cam != null)
        {
            Vector3 d = (transform.position - cam.transform.position); d.y = 0f;
            if (d.sqrMagnitude > 0.0001f) return d.normalized;
        }
        return Vector3.forward;
    }

    private void Build()
    {
        var canvasGo = new GameObject("Canvas");
        canvasGo.transform.SetParent(transform, false);
        _canvas = canvasGo.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.WorldSpace;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 4f;
        var crt = (RectTransform)canvasGo.transform;
        crt.sizeDelta = new Vector2(1400, 500);
        crt.localScale = Vector3.one * 0.004f;

        // faint backing panel so text is readable against the arena
        var bgGo = new GameObject("BG", typeof(RectTransform));
        var bgRt = (RectTransform)bgGo.transform;
        bgRt.SetParent(_canvas.transform, false);
        bgRt.anchoredPosition = Vector2.zero; bgRt.sizeDelta = new Vector2(1400, 500);
        _bg = bgGo.AddComponent<Image>();
        _bg.color = new Color(0.02f, 0.03f, 0.06f, 0.55f);
        bgRt.SetAsFirstSibling();

        _line = MakeText("", 110, new Vector2(0, 70), new Vector2(1360, 220));
        _line.fontStyle = FontStyle.Bold;
        _sub = MakeText("", 52, new Vector2(0, -110), new Vector2(1360, 160));

        _built = true;
    }

    private Text MakeText(string content, int size, Vector2 pos, Vector2 dim)
    {
        var go = new GameObject("Txt", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(_canvas != null ? _canvas.transform : transform, false);
        rt.anchoredPosition = pos; rt.sizeDelta = dim;
        var t = go.AddComponent<Text>();
        t.font = _font; t.text = content; t.fontSize = size;
        t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.color = Color.white;
        return t;
    }

    private static Color WithA(Color c, float a) => new Color(c.r, c.g, c.b, a);

    private Transform LocalOpponent()
    {
        var s = LocalSelf();
        var o = s != null ? s.GetComponent<PlayerController>()?.GetOpponent() : null;
        return o != null ? o.transform : null;
    }

    private PlayerCombat LocalSelf()
    {
        foreach (var p in GameManager.players)
            if (p != null && p.isLocalPlayer) return p.GetComponent<PlayerCombat>();
        return null;
    }
}
