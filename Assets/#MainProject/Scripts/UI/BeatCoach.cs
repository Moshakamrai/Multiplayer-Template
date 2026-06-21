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
/// It watches one signal:
///   • PlayerCombat.VRGradeText / VRGradeTime    → a graded on-beat result (EXCELLENT/GOOD = success,
///     anything else = a miss). "Too early" attempts are coached by BeatTutorialText but do NOT count
///     toward this streak (they were wrongly wiping the good-streak on the same beat as a clean land).
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

    // How many clean on-beat hits in a row before we trust the player and hide coaching FOR GOOD.
    private const int GOOD_TO_HIDE = 2;

    // Visible until proven otherwise — new players see it immediately.
    private static bool _coachingVisible = true;
    // Once dismissed, it stays gone for the rest of the run — it does NOT come back on later misses
    // (the reappearing coaching was jamming the view). Persists across rounds (static).
    private static bool _dismissedForGood;
    // Tutorial coaching (approach RING + tutorial TEXT) is DISABLED entirely per design — it was
    // jamming the player's view. Always off; the streak logic below is kept but inert.
    public static bool CoachingVisible => false;

    private int   _goodStreak;
    private float _lastTimingSeen = -999f;

    private void Update()
    {
        if (_dismissedForGood) return; // nothing more to track once it's permanently off

        // Only GRADED results drive the hide streak. ("Too early" attempts are coached by
        // BeatTutorialText but don't count here.)
        if (PlayerCombat.VRGradeTime > _lastTimingSeen + 0.001f)
        {
            _lastTimingSeen = PlayerCombat.VRGradeTime;
            string r = PlayerCombat.VRGradeText;
            if (r == "EXCELLENT" || r == "GOOD")
            {
                _goodStreak++;
                if (_goodStreak >= GOOD_TO_HIDE)
                {
                    _coachingVisible = false;
                    _dismissedForGood = true; // they've got it — gone for the rest of the run
                }
            }
            else
            {
                _goodStreak = 0; // a miss just resets progress toward hiding; never re-shows it
            }
        }
    }
}
