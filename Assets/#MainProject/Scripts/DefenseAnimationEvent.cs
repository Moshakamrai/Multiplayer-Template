using UnityEngine;

/// <summary>
/// Relay for Animation Events that toggle the Block / Parry defense VFX.
///
/// SETUP:
///  1. Put this on the SAME GameObject as the Animator (the character model).
///  2. Drag your Block VFX and Parry VFX GameObjects into PlayerCombat's matching slots.
///  3. In each defense animation clip, add events at the right frames:
///       • Start of block/dodge anim  → BlockOn()
///       • End   of block/dodge anim  → BlockOff()
///       • Start of parry anim        → ParryOn()
///       • End   of parry anim        → ParryOff()
///  4. If no events are set, the VFX auto-turns off after "Defense Vfx Duration" seconds.
/// </summary>
public class DefenseAnimationEvent : MonoBehaviour
{
    private PlayerCombat _combat;

    void Awake() => _combat = GetComponentInParent<PlayerCombat>();

    // ── Block family (Block, Dodge Left, Dodge Right) ──────────────────────
    public void BlockOn()  => Get()?.SetBlockVfx(true);
    public void BlockOff() => Get()?.SetBlockVfx(false);

    // ── Parry family (Reflect / Reverse / Clutch) ──────────────────────────
    public void ParryOn()  => Get()?.SetParryVfx(true);
    public void ParryOff() => Get()?.SetParryVfx(false);

    private PlayerCombat Get()
    {
        if (_combat == null) _combat = GetComponentInParent<PlayerCombat>();
        return _combat;
    }
}
