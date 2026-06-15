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

    // How many clean on-beat hits in a row before we trust the player and hide coaching.
    private const int GOOD_TO_HIDE = 2;
    // How many BAD graded hits in a row before we bring coaching back.
    private const int FAILS_TO_SHOW = 2;

    // Visible until proven otherwise — new players see it immediately.
    private static bool _coachingVisible = true;
    public static bool CoachingVisible => _coachingVisible;

    private int   _goodStreak;
    private int   _failStreak;
    private float _lastTimingSeen = -999f;

    private void Update()
    {
        // Only GRADED results drive the hide/show streak. We deliberately do NOT count "too early"
        // attempts as failures here — an early shout often happens on the very same beat you then land
        // cleanly, which was wrongly wiping the good-streak so coaching never hid. ("Too early" still
        // shows its own coaching flash via BeatTutorialText; it just doesn't reset the streak.)
        if (PlayerCombat.VRGradeTime > _lastTimingSeen + 0.001f)
        {
            _lastTimingSeen = PlayerCombat.VRGradeTime;
            string r = PlayerCombat.VRGradeText;
            if (r == "EXCELLENT" || r == "GOOD") RegisterSuccess();
            else                                  RegisterFailure(); // a graded BAD
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
