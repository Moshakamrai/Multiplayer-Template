using System.Collections;
using UnityEngine;

/// PER-FIGHTER VFX slots — put one on EACH player prefab and EACH bot, then fill the slots
/// for that fighter in the Inspector. No global manager; every fighter owns its look.
///
/// THE 3-STAGE FAMILY SYSTEM (5 families × 3 slots):
///   1. WEAPON AURA — activates ON THE WEAPON the moment a card of this family triggers, and
///      clears when the attack is done. Drag the weapon bone into "Weapon Mount".
///   2. MAIN VFX    — the flying slash / orb / projectile for this family's attacks. FighterCardVFX
///      only PROVIDES the prefab (MainVfxFor); PlayerCombat.SpawnSlashNow() spawns and drives it the
///      SAME WAY as the glove character's slash — timed spawn, aimed at the opponent, blade-angle up,
///      slashSizeScale shrink, and a SlashProjectile so it flies. Author the prefab like the working
///      glove slash; don't re-implement motion/aim/scale on it.
///   3. HIT VFX     — impact burst spawned ON THE VICTIM when this family's attack LANDS.
///
/// Plus optional sword tinting per family (sword blade + SwordArcTrail arc recolor per swing),
/// and the ELEMENT status/reaction slots (BURN/SHOCK/CHILL/ROOT/EXPOSE visuals on this fighter).
/// Every empty slot is skipped silently — fill in what you have, defaults still work.
public class FighterCardVFX : MonoBehaviour
{
    // ── Per-family 3-stage VFX ─────────────────────────────────────────────
    [System.Serializable]
    public class FamilyVfx
    {
        [Tooltip("1) Weapon aura: a VFX object ALREADY INSIDE this prefab (child of the weapon). " +
                 "The script just turns it ON when a card of this family executes and OFF when done.")]
        public GameObject weaponAuraVfx;
        [Tooltip("2) The flying slash/orb/projectile PREFAB for this family's attacks. Empty = default slash prefab.\n" +
                 "IMPORTANT: this slot is NOT spawned by FighterCardVFX. It is handed to PlayerCombat.SpawnSlashNow() " +
                 "(via MainVfxFor) and driven EXACTLY like the glove character's slash — same path, same behaviour:\n" +
                 "  • spawned after slashSpawnDelay, at slashSpawnPoint (chest height + forward fallback)\n" +
                 "  • aimed at the opponent (slashAimsAtOpponent) with the blade-angle 'up' from SwordArcTrail\n" +
                 "  • uniformly shrunk by slashSizeScale\n" +
                 "  • given a SlashProjectile so it FLIES (speed/lifetime guarded to sane values), and tracked\n" +
                 "    for trade-loss dissolves.\n" +
                 "So author this prefab the same way the working glove slash prefab is authored (forward = local +Z, " +
                 "real-world scale, optional SlashProjectile for custom speed/lifetime). All the motion/aim/scale is " +
                 "applied for you — don't duplicate it on the prefab.")]
        public GameObject mainVfx;
        [Tooltip("3) Impact burst PREFAB at the VICTIM when this family's attack successfully lands.")]
        public GameObject hitVfx;

        [Header("Sword recolor for this family's swings (optional)")]
        public bool tintSword = false;
        [ColorUsage(false, true)] public Color swordColor = Color.white;
        [Tooltip("SwordArcTrail arc color for this family (HDR — crank it for glow).")]
        [ColorUsage(false, true)] public Color arcColor = new Color(1.4f, 2.6f, 3.4f, 1f);
    }

    [Header("STRIKE family (Jab, Cross, Hook, Boom, Uppercut, Overclock)")]
    public FamilyVfx strike = new FamilyVfx();
    [Header("THROW family (Grapple, Fake, Sweep)")]
    public FamilyVfx throwFamily = new FamilyVfx();
    [Header("BLOCK family (Block, Left, Right)")]
    public FamilyVfx block = new FamilyVfx();
    [Header("PARRY family (Parry, Clutch, Reverse, Mirror)")]
    public FamilyVfx parry = new FamilyVfx();
    [Header("SUPPORT family (Focus, Taunt, Trap, Cage)")]
    public FamilyVfx support = new FamilyVfx();

    // Weapon auras are beat-synced: ON when the card executes, OFF when the NEXT beat fires —
    // so the next card-choose cycle always starts with a clean weapon. No duration to tune.

    [Header("Sword Tinting (drag the sword blade Renderer(s))")]
    [Tooltip("Renderers recolored when a family has Tint Sword on. Leave empty for glove fighters.")]
    public Renderer[] swordRenderers;
    [Tooltip("Seconds the tint lasts before restoring (≈ the swing length).")]
    public float tintDuration = 0.6f;

    // ── Element status / reaction VFX (played ON this fighter) ───────────
    [System.Serializable]
    public class ElementVfx
    {
        [Tooltip("One-shot burst when this element's STATUS lands on this fighter.")]
        public GameObject statusApplyVfx;
        [Tooltip("LOOPING effect attached while the status is active (auto-destroyed when it ends).")]
        public GameObject statusLoopVfx;
        [Tooltip("Big one-shot burst when this fighter's status is DETONATED into the reaction.")]
        public GameObject reactionVfx;
    }

    [Header("ELEMENT — FIRE (status: BURN, reaction: FIRESTORM)")]
    public ElementVfx fire = new ElementVfx();
    [Header("ELEMENT — LIGHTNING (status: SHOCK, reaction: ELECTROCUTE)")]
    public ElementVfx lightning = new ElementVfx();
    [Header("ELEMENT — WATER (status: CHILL, reaction: SHATTER)")]
    public ElementVfx water = new ElementVfx();
    [Header("ELEMENT — EARTH (status: ROOT, reaction: EROSION)")]
    public ElementVfx earth = new ElementVfx();
    [Header("ELEMENT — WIND (status: EXPOSE, reaction: COMBUST)")]
    public ElementVfx wind = new ElementVfx();

    [Header("Spawn Settings")]
    [Tooltip("BLOOD-ONLY MODE: when ON, the element status/reaction VFX (fire BURN loop, etc.) are NOT " +
             "spawned — only the game's blood-on-hit shows. The status MECHANICS (damage multipliers) " +
             "still work; just the big lingering elemental visuals are suppressed. Turn ON to go blood-only.")]
    public bool suppressElementVfx = true;
    [Tooltip("Chest-height offset for status/reaction/hit effects.")]
    public float effectHeight = 1.2f;
    [Tooltip("Seconds before one-shot VFX instances are cleaned up.")]
    public float oneShotLifetime = 3f;
    [Tooltip("Uniform scale applied to spawned status / reaction VFX. Lower this if the imported effects look too big (1 = prefab's own size).")]
    [Range(0.05f, 3f)] public float effectSizeScale = 0.5f;

    // Apply the global size scale to a freshly-spawned one-shot VFX instance.
    private void ScaleEffect(GameObject go)
    {
        if (go != null && !Mathf.Approximately(effectSizeScale, 1f))
            go.transform.localScale *= effectSizeScale;
    }

    // ── Runtime ───────────────────────────────────────────────────────────
    private SwordArcTrail _arc;
    private CardManager _cardManager;
    private MaterialPropertyBlock _mpb;
    private Color _arcDefault;
    private bool _arcDefaultCached;
    private Coroutine _tintRoutine;
    private GameObject _activeAura;
    private Coroutine _auraSafetyRoutine;    // backstop in case the animation-end event never fires
    [Tooltip("Hard cap (seconds) the weapon aura stays on if the attack's animation-end event is missed. " +
             "Keep this close to your swing length — most clips don't have the EndAttackWindow event, so " +
             "this timeout is what actually turns the aura off. Too high = the aura lingers between beats.")]
    public float auraSafetyTimeout = 0.6f;
    static readonly int ID_BaseColor = Shader.PropertyToID("_BaseColor");
    static readonly int ID_Color     = Shader.PropertyToID("_Color");
    static readonly int ID_Emission  = Shader.PropertyToID("_EmissionColor");

    void Awake()
    {
        _arc = GetComponent<SwordArcTrail>();
        _cardManager = GetComponent<CardManager>();
        _mpb = new MaterialPropertyBlock();

        // Weapon auras start OFF — even if one was left enabled in the prefab.
        foreach (var set in new[] { strike, throwFamily, block, parry, support })
            SetAura(set?.weaponAuraVfx, false);
    }

    // Turn a weapon aura on/off. Dead simple on purpose: just toggle the GameObject active state.
    //
    // The aura is a child object you've already placed exactly where you want it on the glove/weapon.
    // Toggling SetActive(true/false) turns the whole thing on and off without ever touching its
    // transform, its simulation space, or particle internals — so it spawns precisely where you put it
    // and follows the parent normally. SetActive(false) fully halts a looping VFX Graph; SetActive(true)
    // restarts it clean (the graph plays from the start on enable). No Stop/Play/Reinit gymnastics.
    private static void SetAura(GameObject aura, bool on)
    {
        if (aura == null) return;
        aura.SetActive(on);
    }

    public FamilyVfx SetForFamily(CardFamily fam) => fam switch
    {
        CardFamily.Strike  => strike,
        CardFamily.Throw   => throwFamily,
        CardFamily.Block   => block,
        CardFamily.Parry   => parry,
        CardFamily.Support => support,
        _                  => null,
    };

    private CardFamily FamilyOf(string trigger)
    {
        if (_cardManager == null) _cardManager = GetComponent<CardManager>();
        return _cardManager != null ? _cardManager.FamilyOfTrigger(trigger) : CardFamily.Support;
    }

    /// Per-family main VFX override for this attack, or null to use the fighter's default slash.
    /// This only RETURNS the prefab — PlayerCombat.SpawnSlashNow() then spawns and drives it identically
    /// to the glove character's slash (timed spawn, opponent-aim, blade-angle up, slashSizeScale, flies
    /// via SlashProjectile, dissolve-tracked). Keep that contract in mind when authoring mainVfx prefabs.
    public GameObject MainVfxFor(string trigger) => SetForFamily(FamilyOf(trigger))?.mainVfx;

    /// Called when a card of any family starts executing (local + remote views):
    /// turns ON the family's weapon-aura object (a child of this prefab) and applies the sword tint.
    public void OnCardTriggered(string trigger)
    {
        // DRONE/FOOTBALL SEGMENT: only the drone hit VFX + blood are allowed — no weapon aura, no
        // sword tint, no card VFX of any kind while the rush is running.
        if (DroneRushSegment.Instance != null && DroneRushSegment.Instance.SegmentActive) return;

        var set = SetForFamily(FamilyOf(trigger));
        if (set == null) return;

        // 1) Weapon aura — switch OFF the previous family's aura, switch this one ON.
        //    It turns OFF when the attack ANIMATION ends (PlayerCombat.EndAttackWindow → StopAura),
        //    with a safety timeout so a missed animation event can't leave it stuck on.
        if (_activeAura != null && _activeAura != set.weaponAuraVfx) SetAura(_activeAura, false);
        if (set.weaponAuraVfx != null)
        {
            _activeAura = set.weaponAuraVfx;
            SetAura(_activeAura, true);
            if (_auraSafetyRoutine != null) StopCoroutine(_auraSafetyRoutine);
            _auraSafetyRoutine = StartCoroutine(AuraSafetyTimeout());
        }

        // Sword + arc tint for the swing.
        if (set.tintSword)
        {
            if (_arc != null && !_arcDefaultCached) { _arcDefault = _arc.arcColor; _arcDefaultCached = true; }
            if (_tintRoutine != null) StopCoroutine(_tintRoutine);
            _tintRoutine = StartCoroutine(TintRoutine(set));
        }
    }

    /// Turn the active weapon aura OFF. Called from PlayerCombat when the attack animation ends
    /// (EndAttackWindow), and by the safety timeout. Safe to call when nothing is active.
    public void StopAura()
    {
        if (_auraSafetyRoutine != null) { StopCoroutine(_auraSafetyRoutine); _auraSafetyRoutine = null; }
        if (_activeAura == null) return;
        SetAura(_activeAura, false);
        _activeAura = null;
    }

    // Backstop: if the attack's animation-end event is missing/mistimed, kill the aura anyway so it
    // can never stay stuck on.
    private IEnumerator AuraSafetyTimeout()
    {
        yield return new WaitForSeconds(auraSafetyTimeout);
        if (_activeAura != null) { SetAura(_activeAura, false); _activeAura = null; }
        _auraSafetyRoutine = null;
    }

    /// 3) Hit effect: this fighter's attack of `family` landed on `victimPos` — burst there.
    // DISABLED: the project already has a dedicated blood effect, so these imported hit bursts are
    // skipped entirely.
    public void PlayHit(CardFamily family, Vector3 victimPos) { }

    private IEnumerator TintRoutine(FamilyVfx set)
    {
        if (_arc != null) _arc.arcColor = set.arcColor;
        SetSwordColor(set.swordColor, true);

        yield return new WaitForSeconds(tintDuration);

        if (_arc != null && _arcDefaultCached) _arc.arcColor = _arcDefault;
        SetSwordColor(Color.white, false);
        _tintRoutine = null;
    }

    private void SetSwordColor(Color c, bool apply)
    {
        if (swordRenderers == null) return;
        foreach (var r in swordRenderers)
        {
            if (r == null) continue;
            if (apply)
            {
                _mpb.Clear();
                _mpb.SetColor(ID_BaseColor, c);
                _mpb.SetColor(ID_Color, c);
                _mpb.SetColor(ID_Emission, c * 0.6f);
                r.SetPropertyBlock(_mpb);
            }
            else
            {
                r.SetPropertyBlock(null); // restore the material's own colors
            }
        }
    }

    // ── Element status / reaction playback (called by PlayerCombat) ──────
    public ElementVfx SetOf(Element e) => e switch
    {
        Element.Fire      => fire,
        Element.Lightning => lightning,
        Element.Water     => water,
        Element.Earth     => earth,
        Element.Wind      => wind,
        _                 => null,
    };

    // ELEMENT VFX HARD-DISABLED (permanent, per design): the fire BURN loop + status/reaction bursts
    // are eye-blinding red spam. These are unconditional no-ops — deliberately NOT gated on the
    // suppressElementVfx flag anymore, because a stale serialized inspector value on the prefabs kept
    // re-enabling them. The element MECHANICS (damage multipliers, statuses) still work; only the
    // visuals are dead. To ever bring them back, restore the Instantiate bodies from git history.
    public void PlayStatusApply(Element e) { }

    /// Returns the loop instance; PlayerCombat destroys it when the status ends. (Always null now.)
    public GameObject AttachLoop(Element e) { return null; }

    public void PlayReaction(Element statusElement) { }
}
