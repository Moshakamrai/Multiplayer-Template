using UnityEngine;

/// Single source of truth for "should the new-player beat coaching be on screen right now?"
/// Both the approach RING and the tutorial TEXT ask this so they hide/show together.
///
/// Rule (what the player asked for):
///   • Coaching starts ON for a fresh player.
///   • Once they clearly GET IT — a streak of good on-beat hits with no mistakes — it turns OFF.
///   • If they then FAIL (mistime / fire too early) TWICE IN A ROW, it comes BACK to re-teach them,
///     and stays until they string together good hits again.
///
/// It watches the same signals the rest of the game already produces:
///   • PlayerCombat.VRTimingText / VRTimingTime  → a graded on-beat result (EXCELLENT/GOOD = success).
///   • PlayerCombat.VREarlyTime                  → the player jumped the gun (fired before the window).
///
/// Pure logic + static state; nothing to place in a scene. Self-bootstraps a watcher so the streak
/// is tracked even when neither visual is currently showing.
[DefaultExecutionOrder(10002)]
public class BeatCoach : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        var go = new GameObject("~BeatCoach");
        DontDestroyOnLoad(go);
        go.AddComponent<BeatCoach>();
    }

    // How many clean on-beat hits in a row before we trust the player and hide coaching.
    private const int GOOD_TO_HIDE = 3;
    // How many failures (mistime / too-early) in a row before we bring coaching back.
    private const int FAILS_TO_SHOW = 2;
    // Ignore an "early" flag that lands right after a successful fire (debounce double-counting).
    private const float EVENT_DEBOUNCE = 0.25f;

    // Visible until proven otherwise — new players see it immediately.
    private static bool _coachingVisible = true;
    public static bool CoachingVisible => _coachingVisible;

    private int   _goodStreak;
    private int   _failStreak;
    private float _lastTimingSeen = -999f;
    private float _lastEarlySeen  = -999f;

    private void Update()
    {
        // A fresh graded result arrived?
        if (PlayerCombat.VRTimingTime > _lastTimingSeen + 0.001f)
        {
            _lastTimingSeen = PlayerCombat.VRTimingTime;
            string r = PlayerCombat.VRTimingText;
            if (r == "EXCELLENT" || r == "GOOD") RegisterSuccess();
            else                                  RegisterFailure(); // bad timing grade
        }

        // A fresh "too early" attempt arrived (and not just an echo of a hit we already scored)?
        if (PlayerCombat.VREarlyTime > _lastEarlySeen + 0.001f)
        {
            _lastEarlySeen = PlayerCombat.VREarlyTime;
            if (Time.time - PlayerCombat.VRTimingTime > EVENT_DEBOUNCE)
                RegisterFailure();
        }
    }

    private void RegisterSuccess()
    {
        _failStreak = 0;
        _goodStreak++;
        if (_goodStreak >= GOOD_TO_HIDE) _coachingVisible = false; // they've got it — fade the help out
    }

    private void RegisterFailure()
    {
        _goodStreak = 0;
        _failStreak++;
        if (_failStreak >= FAILS_TO_SHOW) _coachingVisible = true; // struggling again — bring help back
    }
}
