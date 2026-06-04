using UnityEngine;

/// <summary>
/// Lets an Animation Event fire the sword slash at the exact frame the blade cuts.
///
/// SETUP:
///  1. Put this component on the SAME GameObject as the Animator (the character model).
///     (Animation Events can only call functions on a component attached to the Animator's object.)
///  2. On PlayerCombat, tick "Slash Via Animation Event" (so the timed code path stops firing it).
///  3. Open each swing clip, add an Animation Event at the frame the blade cuts, and pick the
///     "Slash" function from this component.
/// </summary>
public class SlashAnimationEvent : MonoBehaviour
{
    private PlayerCombat _combat;

    void Awake() => _combat = GetComponentInParent<PlayerCombat>();

    // ← This is the function you select on the Animation Event.
    public void Slash()
    {
        if (_combat == null) _combat = GetComponentInParent<PlayerCombat>();
        if (_combat != null) _combat.SpawnSlashNow();
    }
}
