using Mirror;
using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

public class PlayerCombat : NetworkBehaviour
{
    [SyncVar] public float CurrentPercentage = 0f;
    [SyncVar] public int roundDamageDealt = 0;
    [SyncVar] public int roundExcellentCount = 0;
    [SyncVar] public int roundCounterCount = 0;

    // ── SCORE (replaces the health % as the win metric) ──────────────────────
    // You gain points for moves that BENEFIT you: a clean on-beat move, a successful defense that
    // takes no damage, a parry/reflect that turns the tables, and especially landing a hard hit.
    // Higher score at the end of the round WINS (the loser plays the knockout anim on the last hit).
    // Server-authoritative, synced to clients; the hook drives the animated 3D count-up display.
    [SyncVar(hook = nameof(OnScoreChanged))] public int Score = 0;

    // Running total across ALL rounds of the match — this is what goes on the leaderboard at match
    // end. Score resets each round; MatchScore banks the round's score into it (see ResetRoundStats),
    // so it survives until the scene reloads (which starts a fresh match).
    [SyncVar] public int MatchScore = 0;

    // Server-side: this player's latest on-beat shout grade ("EXCELLENT"/"GOOD"/"BAD"), stamped by
    // RhythmRoundManager.EvaluateAndSendFeedback right before the trade resolves. The bot reads the
    // ATTACKING human's grade to pick its got-hit reaction + rest position (BotBeatApproach).
    [System.NonSerialized] public string LastTimingRating = "";

    // Score tuning — deliberately BIG ("thousands per move") for arcade dopamine.
    public const int SCORE_ONBEAT_GOOD      = 1000;  // played the move on-beat
    public const int SCORE_ONBEAT_EXCELLENT = 2000;  // nailed the beat
    public const int SCORE_DEFENSE_SUCCESS  = 2500;  // blocked/parried and took no damage (won the exchange)
    public const int SCORE_HIT_PER_DAMAGE   = 250;   // landed a hit: this × damage = "how hard you hit" bonus
    public const int SCORE_COUNTER_BONUS    = 1500;  // extra on top of a hit for a parry/reflect reversal

    // Fired on every client when the synced Score changes — the score HUD animates from old→new.
    private void OnScoreChanged(int oldVal, int newVal)
    {
        ScoreHud.Instance?.OnScoreUpdated(this, oldVal, newVal);
    }

    /// Server-only: grant points for a beneficial action, then let clients animate the count-up.
    /// reason is just for logging/feel. Amounts are in the THOUSANDS (see callers).
    [Server]
    public void AddScore(int amount, string reason = "")
    {
        if (amount <= 0) return;
        Score += amount;
        // VR drone-rush reward segment: fires once per round when a human crosses the score threshold.
        DroneRushSegment.Instance?.NotifyScore(this, Score);
    }
    [SyncVar] public string availableCardsString = "";
    public Animator animator;
    public bool isAttacking = false;
    public bool IsDead { get; private set; }
    public bool IsHurting { get; private set; }

    [SyncVar] public bool IsParryActive  = false;
    [SyncVar] public bool IsStaggered    = false;
    [SyncVar] public int  StaggerBeatsRemaining = 0;

    // ── Persistent card effects (tracked across beats) ───────────────────────
    [SyncVar] public bool HasPendingTrap  = false;
    [SyncVar] public bool HasPendingCage  = false;
    [SyncVar] public bool HasFocusBuff    = false;
    [SyncVar] public bool HasMirrorBuff   = false;
    [SyncVar] public int  FocusBuffBeatsRemaining  = 0;
    [SyncVar] public int  MirrorBuffBeatsRemaining = 0;
    [SyncVar] public bool IsTauntedNextTurn = false;
    [SyncVar] public int TauntTurnsRemaining = 0;

    // ── Per-card perk state ───────────────────────────────────────────────────
    [SyncVar] public int  BleedTurnsRemaining  = 0;  // GrappleBleed: ticks left
    [SyncVar] public int  BleedDamagePerBeat   = 0;  // damage each bleed tick
    [SyncVar] public bool BlockChargeReady     = false; // BlockCounter: next atk +15%
    [SyncVar] public bool DefendedLastBeat     = false; // FakeCounter: was last action defense?
    [SyncVar] public bool AttackedLastBeat     = false; // CounterBonus: was last action attack?
    [SyncVar] public int  PendingTrapCount     = 0;  // TrapPunish Lv3: punish next 2 moves
    [SyncVar] public int  CageBeatsRemaining   = 0;  // CageBreak Lv3: blocks defense 2 beats
    [SyncVar] public bool StaggerNextBeat      = false; // Stagger perk: stun next beat
    [SyncVar] public int  MustStrikeBeats      = 0;  // Taunt: must play a Strike next beat(s) or take damage

    // ── Trait Effects ─────────────────────────────────────────────────────────
    [SyncVar] public string activeTraitId = "";
    [SyncVar] public int ConsecutiveHitsChain = 0; // For Bloodlust / Momentum traits
    [SyncVar] public bool HasStalwartBuff = false; // Stalwart trait: +10% next attack after block

    // ── Elemental status (one at a time — see ElementSystem.cs for the wheel) ──
    // BURN ticks damage, SHOCK shrinks timing windows, CHILL cuts outgoing damage,
    // ROOT makes dodges fail, EXPOSE raises incoming damage. RhythmRoundManager owns
    // all the rules; this just holds the state + drives client VFX via the hook.
    [SyncVar(hook = nameof(OnElementStatusChanged))] public int StatusElement = 0; // (int)Element
    [SyncVar] public int StatusBeatsRemaining = 0;
    private GameObject _statusLoopVfx;

    public Element CurrentElementStatus => (Element)StatusElement;

    [Server]
    public void ServerApplyElementStatus(Element e, int beats)
    {
        StatusElement = (int)e;
        StatusBeatsRemaining = beats;
        RpcElementStatusApplied((int)e);
    }

    [Server]
    public void ServerClearElementStatus()
    {
        StatusElement = (int)Element.None;
        StatusBeatsRemaining = 0;
    }

    // Per-fighter VFX slots — lazily resolved because SyncVar hooks can fire before Start().
    private FighterCardVFX CardVfx => _cardVfx != null ? _cardVfx : (_cardVfx = GetComponent<FighterCardVFX>());

    // SyncVar hook (runs on every client): attach/remove the looping status VFX.
    private void OnElementStatusChanged(int oldVal, int newVal)
    {
        if (_statusLoopVfx != null) { Destroy(_statusLoopVfx); _statusLoopVfx = null; }
        if ((Element)newVal != Element.None && CardVfx != null)
            _statusLoopVfx = CardVfx.AttachLoop((Element)newVal);
    }

    [ClientRpc]
    private void RpcElementStatusApplied(int e)
    {
        Element elem = (Element)e;
        CardVfx?.PlayStatusApply(elem);
        if (isLocalPlayer)
        {
            // You just got tagged — show the status name and give a soft warning buzz.
            VRTimingText  = Elements.StatusName(elem);
            VRTimingColor = Elements.ColorOf(elem);
            VRTimingTime  = Time.time;
            VRHaptics.Pulse(VRHaptics.Hand.Both, 0.45f, 0.12f);
        }
    }

    /// Reaction fired ON this fighter (their status got detonated). Plays the big burst +
    /// announces the reaction name; the damage itself arrives via the normal TakeDamage path.
    [ClientRpc]
    public void RpcElementReaction(int statusElement)
    {
        Element elem = (Element)statusElement;
        CardVfx?.PlayReaction(elem);
        // Everyone sees the reaction name in the timing HUD — it's the highlight moment.
        VRTimingText  = Elements.ReactionName(elem);
        VRTimingColor = Elements.ColorOf(elem);
        VRTimingTime  = Time.time;
        if (isLocalPlayer)
            VRHaptics.GotParried(); // detonated = your power blew up in your face
        else
            VRHaptics.FullCharge(VRHaptics.Hand.Both); // you (likely the triggerer) get the reward blip
    }

    private Queue<string> _attackQueue = new Queue<string>();
    public SphereCollider weaponGloveLeft;
    public SphereCollider weaponGloveRight;

    public struct RhythmAction { public string attack; public Vector3 dash; }
    public List<RhythmAction> _comboBuffer = new List<RhythmAction>();

    // ── Shop Phase (local client state) ────────────────────────────────────
    public List<string> localShopSelection = new List<string>();
    public bool localShopLocked = false;

    [Command]
    public void CmdToggleShopCard(string cardName)
    {
        RhythmRoundManager.Instance?.ToggleShopCard(this, cardName);
    }

    [Command]
    public void CmdLockInShop()
    {
        // Pre-round shop lock-in (pick 4 cards)
        RhythmRoundManager.Instance?.LockInShop(this);
    }

    [Command]
    public void CmdLockInPostRoundShop()
    {
        var inv = GetComponent<PlayerInventory>();
        if (inv != null)
        {
            // Post-round TFT shop lock-in
            ShopPhaseManager.Instance?.LockInShop(inv);
        }
    }

    [Command]
    public void CmdSelectRound(int roundTypeValue)
    {
        var type = (RoundType)roundTypeValue;
        RhythmRoundManager.Instance?.SelectRoundType(type, "");
    }

    [Command]
    public void CmdSelectCustomRound(int roundTypeValue, string customMapName)
    {
        var type = (RoundType)roundTypeValue;
        RhythmRoundManager.Instance?.SelectRoundType(type, customMapName);
    }

    [Command]
    public void CmdBuyCombatCard(int slotIndex)
    {
        var inv = GetComponent<PlayerInventory>();
        if (inv != null) ShopPhaseManager.Instance?.TryBuyCombatCard(inv, slotIndex);
    }

    [Command]
    public void CmdBuyTraitCard(int slotIndex)
    {
        var inv = GetComponent<PlayerInventory>();
        if (inv != null) ShopPhaseManager.Instance?.TryBuyTraitCard(inv, slotIndex);
    }

    [Command]
    public void CmdSelectLoadoutCard(string cardId)
    {
        var inv = GetComponent<PlayerInventory>();
        if (inv != null) inv.SelectCard(cardId);
    }

    [Command]
    public void CmdRemoveCard(string cardId)
    {
        var inv = GetComponent<PlayerInventory>();
        if (inv != null && inv.credits >= 1)
        {
            // Remove from appropriate list
            if (inv.ownedCombatCards.Contains(cardId))
                inv.ownedCombatCards.Remove(cardId);
            else if (inv.equippedTraitId == cardId)
                inv.equippedTraitId = "";

            // Deduct 1 credit
            inv.credits = Mathf.Max(0, inv.credits - 1);
        }
    }

    [Header("VFX Settings")]
    public Renderer playerRenderer;
    public Material flashMaterial;
    public Material _originalMaterial;

    public VoiceProcessor vp;

    private string _pendingAttackTrigger = "";
    private Vector3 _pendingDashDirection = Vector3.zero;
    public string PendingAttackTrigger => _pendingAttackTrigger;

    // ── Sticky / repeating move (single-move mode) ───────────────────────────
    // Once a card is picked it becomes the "active move" and AUTO-REPEATS every beat until the
    // player picks a different card — they no longer have to re-select the same move each beat
    // (they still shout/release on the beat to actually fire it). Carries across rounds + stagger.
    [Tooltip("ON: a picked card stays active and repeats every beat until you pick another card.")]
    public bool stickyMoveEnabled = true;
    private string  _stickyAttackTrigger = "";
    private Vector3 _stickyDashDirection = Vector3.zero;
    // True when the current pending move was auto-armed FROM the sticky move (not freshly picked this
    // beat). A sticky-armed move must still leave the input slot "open" so the player can pick a
    // DIFFERENT card to replace it — otherwise the active move locks out all further card picks.
    private bool _pendingFromSticky = false;

    // --- TIMING FEEDBACK VARIABLES ---
    private string _timingText = "";
    private Color _timingColor = Color.white;
    private float _timingFade = 0f;

    private VoiceCommandManager _vcm;

    [SyncVar] public float lastVocalSpikeTime   = -1f;
    [SyncVar] public float lastVocalSpikeVolume = 0f;
    // The TRUE distance-from-beat (seconds) measured CLIENT-SIDE the instant the shout/release fired.
    // The grader uses this directly instead of recomputing |beat - spikeTime| from absolute times,
    // which was unreliable because the client's audio clock and the server's beat clock can drift —
    // that drift made on-the-beat shouts read BAD. -1 = none this beat.
    [SyncVar] public float lastVocalSpikeOffset = -1f;

    // Spike detection guards — prevent holding voice from gaming timing
    private bool  _spikeLockedThisBeat   = false;
    private float _lastTrackedBeatFire   = -1f;
    // Smaller dead zone = a shout RIGHT ON / just-after the beat still registers (was 0.2s, which ate
    // on-the-beat shouts and made them read BAD). Wider shout window = more forgiving pre-beat timing.
    private const float BEAT_DEAD_ZONE   = 0.08f;
    private const float SHOUT_WINDOW     = 0.6f;

    // Pressure system — recent hits shrink the shout window
    private float _pressureLevel = 0f;
    private const float PRESSURE_GAIN  = 0.3f;
    private const float PRESSURE_DECAY = 0.12f;

    private float _hurtFlashFade = 0f;
    private float _successFlashFade = 0f;

    // --- STAGGER RECOVERY SYSTEM (local player only) ---
    private float _staggerRecoveryCharge = 0f;   // 0-1 bar fill
    private bool  _staggerTimingEscaped  = false; // one timing escape per beat cycle
    private CardManager _cardManager;
    private const float STAGGER_CHARGE_RATE    = 0.90f; // fallback rate; overridden dynamically on stagger entry
    private const float STAGGER_CHARGE_DECAY   = 0.18f; // per second while silent
    private const float STAGGER_CHARGE_VOL_MIN = 0.28f; // mic volume threshold to start charging
    private bool  _wasStaggered       = false;
    private float _dynamicStaggerRate = STAGGER_CHARGE_RATE; // recomputed each stagger using beat timing

    public override void OnStartServer()
    {
        CurrentPercentage = 0f;
        ResetRoundStats();
    }

    [Server]
    public void ResetRoundStats()
    {
        CurrentPercentage = 0f;
        Score = 0;            // points reset each round — most points at the timer wins
        roundDamageDealt = 0;
        roundExcellentCount = 0;
        roundCounterCount = 0;
        IsDead = false;
        IsStaggered = false;
        StaggerBeatsRemaining = 0;
        IsParryActive = false;
        lastVocalSpikeTime = -1f;
        lastVocalSpikeVolume = 0f;
        lastVocalSpikeOffset = -1f;
        _pendingAttackTrigger = "";
        _pendingDashDirection = Vector3.zero;
        // Sticky move carries across rounds: re-arm the pending move from it so the player's last
        // chosen card is already active on the first beat of the new round (no re-pick needed).
        RestickPendingMove();
        if (connectionToClient != null) TargetRestickPendingMove(_stickyAttackTrigger, _stickyDashDirection);
        _attackQueue.Clear();
        _comboBuffer.Clear();
        _pressureLevel = 0f;
        _staggerRecoveryCharge = 0f;
        _staggerTimingEscaped = false;
        _spikeLockedThisBeat = false;

        HasPendingTrap = false;
        HasPendingCage = false;
        HasFocusBuff = false;
        HasMirrorBuff = false;
        FocusBuffBeatsRemaining = 0;
        MirrorBuffBeatsRemaining = 0;
        IsTauntedNextTurn = false;
        TauntTurnsRemaining = 0;
        activeTraitId = "";
        ConsecutiveHitsChain = 0;
        BleedTurnsRemaining = 0;
        BleedDamagePerBeat  = 0;
        BlockChargeReady    = false;
        DefendedLastBeat    = false;
        AttackedLastBeat    = false;
        PendingTrapCount    = 0;
        CageBeatsRemaining  = 0;
        MustStrikeBeats     = 0;
        StaggerNextBeat     = false;
    }

    private GloveBeatGlow _gloveGlow;
    private SwordArcTrail _swordArc;
    private FighterCardVFX _cardVfx;   // per-fighter card/element VFX slots (optional)
    private string _lastSwingTrigger;  // attack trigger of the current swing, for per-card slash lookup
    private string _resolvedAttackState = ""; // server-side: the animation state resolved for this attack
                                              // (bot Strike/Throw randomizes across 3), reused by the RPC

    [Header("Sword Impact VFX (sword characters only — leave empty for glove fighters)")]
    public GameObject swordImpactVfx;

    [Header("Defense VFX (pre-placed in scene/prefab — toggled on/off, not spawned)")]
    [Tooltip("Activated while a BLOCK family card is playing (Block, Dodge). Auto-off on hit or timeout.")]
    public GameObject blockVfx;
    [Tooltip("Activated while a PARRY family card is playing (Reflect, Reverse, Clutch). Auto-off on hit or timeout.")]
    public GameObject parryVfx;
    [Tooltip("Fallback: how long the VFX stays on if no Animation Event turns it off.")]
    public float defenseVfxDuration = 0.6f;

    private Coroutine _blockVfxRoutine;
    private Coroutine _parryVfxRoutine;

    // Detects card family and fires the matching VFX after a 0.2s delay.
    private void TriggerDefenseVfx(string trigger)
    {
        // DRONE/FOOTBALL SEGMENT: only drone hit VFX + blood — no block/parry VFX during the rush.
        if (DroneRushSegment.Instance != null && DroneRushSegment.Instance.SegmentActive) return;

        switch (trigger)
        {
            case "Block": case "Left": case "Right":
                if (blockVfx != null)
                {
                    if (_blockVfxRoutine != null) StopCoroutine(_blockVfxRoutine);
                    _blockVfxRoutine = StartCoroutine(DelayedVfxOn(blockVfx, 0.2f, true));
                }
                break;
            case "ParryIntent": case "Reverse": case "Clutch": case "Mirror":
                if (parryVfx != null)
                {
                    if (_parryVfxRoutine != null) StopCoroutine(_parryVfxRoutine);
                    _parryVfxRoutine = StartCoroutine(DelayedVfxOn(parryVfx, 0.2f, false));
                }
                break;
        }
    }

    private IEnumerator DelayedVfxOn(GameObject vfx, float delay, bool isBlock)
    {
        yield return new WaitForSeconds(delay);
        if (vfx == null) yield break;
        vfx.SetActive(true);
        yield return new WaitForSeconds(defenseVfxDuration);
        if (vfx != null) vfx.SetActive(false);
        if (isBlock) _blockVfxRoutine = null;
        else         _parryVfxRoutine = null;
    }

    private IEnumerator VfxAutoOff(GameObject vfx, float duration)
    {
        yield return new WaitForSeconds(duration);
        if (vfx != null) vfx.SetActive(false);
    }

    public void SetBlockVfx(bool on)  // kept for DefenseAnimationEvent
    {
        if (blockVfx == null) return;
        if (_blockVfxRoutine != null) { StopCoroutine(_blockVfxRoutine); _blockVfxRoutine = null; }
        blockVfx.SetActive(on);
        if (on) _blockVfxRoutine = StartCoroutine(VfxAutoOff(blockVfx, defenseVfxDuration));
    }

    public void SetParryVfx(bool on)  // kept for DefenseAnimationEvent
    {
        if (parryVfx == null) return;
        if (_parryVfxRoutine != null) { StopCoroutine(_parryVfxRoutine); _parryVfxRoutine = null; }
        parryVfx.SetActive(on);
        if (on) _parryVfxRoutine = StartCoroutine(VfxAutoOff(parryVfx, defenseVfxDuration));
    }

    // Called from TakeDamage — immediately kill both defense VFX on hit.
    private void CancelDefenseVfx()
    {
        if (_blockVfxRoutine != null) { StopCoroutine(_blockVfxRoutine); _blockVfxRoutine = null; }
        if (_parryVfxRoutine != null) { StopCoroutine(_parryVfxRoutine); _parryVfxRoutine = null; }
        if (blockVfx != null) blockVfx.SetActive(false);
        if (parryVfx != null) parryVfx.SetActive(false);
    }

    [Header("Sword Slash Projectile (sword characters only)")]
    [Tooltip("Default slash VFX, used for STRIKE-family swings (Jab, Cross, Hook, Boom, Uppercut, Overclock). " +
             "Add SlashProjectile.cs to it to make it fly.")]
    public GameObject slashProjectilePrefab;     // STRIKE family (and the fallback for everything else)
    [Tooltip("Alternate slash VFX used for THROW-family swings (Grapple, Fake, Sweep). " +
             "Leave empty to reuse the default slash prefab above.")]
    public GameObject throwSlashProjectilePrefab; // THROW family
    public Transform  slashSpawnPoint;           // optional; defaults to chest-height, slightly forward
    [Tooltip("Delay before the slash spawns — raise this so it fires LATER in the swing (when the blade actually cuts), not at the start of the anim.")]
    public float slashSpawnDelay = 0.25f;
    [Tooltip("ON = the slash is fired by an Animation Event (SlashAnimationEvent.Slash) at the exact frame, not by the timed code path. Turn this on once you've added events to your swing clips.")]
    public bool slashViaAnimationEvent = false;
    [Tooltip("One-time correction for however your slash art is oriented (applied on top of the auto blade angle).")]
    public Vector3 slashRotationOffset = Vector3.zero;
    [Tooltip("Uniform scale applied to the spawned main/slash VFX. Lower this if your VFX look too big (1 = prefab's own size, 0.5 = half).")]
    [Range(0.05f, 3f)] public float slashSizeScale = 0.5f;
    [Tooltip("ON = the slash always flies straight at the opponent (recommended). OFF = it flies along the fighter's facing direction.")]
    public bool slashAimsAtOpponent = true;

    [Header("Showcase fail-safe (VR)")]
    [Tooltip("ON = a real on-beat SWING of the matching hand fires the move even without a shout or a " +
             "trigger pull — so first-timers at an event who just punch still connect. Shout/trigger " +
             "still work as before and take priority.")]
    public bool swingLockInEnabled = true;
    [Tooltip("Minimum hand speed (m/s) for a swing to count as a fail-safe punch. Higher = needs a more " +
             "committed swing (avoids idle hand-drift firing moves); lower = more forgiving.")]
    public float swingLockInMinSpeed = 1.6f;

    // Timed code path: waits slashSpawnDelay then spawns. (Skipped when slashViaAnimationEvent is on.)
    private void ThrowSlash(string trigger)
    {
        if (!CardManager.IsAttackTrigger(trigger)) return;
        StartCoroutine(SlashRoutine());
    }

    private IEnumerator SlashRoutine()
    {
        if (slashSpawnDelay > 0f) yield return new WaitForSeconds(slashSpawnDelay);
        SpawnSlashNow();
    }

    // Spawn the slash VFX immediately, facing the opponent with the blade angle. PUBLIC so an
    // Animation Event (via SlashAnimationEvent.Slash) can fire it at the exact swing frame.
    public void SpawnSlashNow()
    {
        // DRONE/FOOTBALL SEGMENT: only drone hit VFX + blood — no slash projectiles during the rush.
        if (DroneRushSegment.Instance != null && DroneRushSegment.Instance.SegmentActive) return;

        // No slash projectile when the BOT has run in to melee range (it's close enough to hit with the
        // sword directly). Block/Parry/Support and the human (who attacks from range) still spawn it.
        var approach = GetComponent<BotBeatApproach>();
        if (approach != null && approach.IsApproaching) return;

        Vector3 fwd = transform.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
        fwd.Normalize();

        // Aim straight at the opponent so the projectile actually travels toward them and connects,
        // instead of flying along whatever direction the fighter happens to be facing.
        if (slashAimsAtOpponent)
        {
            var opp = GetComponent<PlayerController>()?.GetOpponent();
            if (opp != null)
            {
                Vector3 toOpp = opp.transform.position - transform.position; toOpp.y = 0f;
                if (toOpp.sqrMagnitude > 0.0001f) fwd = toOpp.normalized;
            }
        }

        // Auto-angle: align the slash's "up" axis to the actual blade direction this frame.
        Vector3 up = Vector3.up;
        if (_swordArc != null && _swordArc.bladeBase != null && _swordArc.bladeTip != null)
        {
            Vector3 bladeDir = _swordArc.bladeTip.position - _swordArc.bladeBase.position;
            if (bladeDir.sqrMagnitude > 0.0001f) up = bladeDir.normalized;
        }

        Quaternion rot = Quaternion.LookRotation(fwd, up) * Quaternion.Euler(slashRotationOffset);
        Vector3 origin = slashSpawnPoint != null
            ? slashSpawnPoint.position
            : transform.position + Vector3.up * 1.2f + fwd * 0.6f;

        // Prefab priority: a per-family main VFX from FighterCardVFX (if assigned) wins; otherwise use
        // the family-routed default slash — THROW family gets throwSlashProjectilePrefab, everything
        // else (Strike, Block, Parry, Support) uses the default slashProjectilePrefab.
        GameObject prefab = _cardVfx != null ? _cardVfx.MainVfxFor(_lastSwingTrigger) : null;
        if (prefab == null)
        {
            CardFamily fam = _cardManager != null ? _cardManager.FamilyOfTrigger(_lastSwingTrigger) : CardFamily.Strike;
            prefab = (fam == CardFamily.Throw && throwSlashProjectilePrefab != null)
                ? throwSlashProjectilePrefab
                : slashProjectilePrefab;
        }

        GameObject go;
        if (prefab != null)
        {
            go = Instantiate(prefab, origin, rot);
            // Scale the VFX down — many of the imported slash/projectile prefabs are authored huge.
            if (!Mathf.Approximately(slashSizeScale, 1f))
                go.transform.localScale *= slashSizeScale;
        }
        else
        {
            // Fallback bright sphere if no VFX prefab is assigned yet (so something always shows).
            go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var col = go.GetComponent<Collider>(); if (col != null) Destroy(col);
            go.transform.SetPositionAndRotation(origin, rot);
            go.transform.localScale = Vector3.one * 0.5f;
            var mr = go.GetComponent<MeshRenderer>();
            var sh = Shader.Find("Unlit/Color");
            if (sh != null) { var m = new Material(sh); m.color = new Color(0.2f, 1f, 1f); mr.material = m; }
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        var slashSp = go.GetComponent<SlashProjectile>();
        if (slashSp == null)
        {
            slashSp = go.AddComponent<SlashProjectile>();
            slashSp.speed = 7f;
            slashSp.lifetime = 1.0f;
        }
        else
        {
            // Per-family prefabs sometimes ship with speed 0 (sits still) or a very long lifetime
            // (lingers between beats). Force sane travel + cleanup so the VFX always flies and clears.
            if (slashSp.speed <= 0.01f) slashSp.speed = 7f;
            if (slashSp.lifetime > 1.5f || slashSp.lifetime <= 0f) slashSp.lifetime = 1.0f;
        }

        // Track for trade-loss dissolves. If the kill order arrived BEFORE the slash spawned
        // (the spawn is delayed into the swing), apply it now.
        _liveSlash = slashSp;
        if (Time.time <= _pendingSlashKillUntil)
        {
            slashSp.DissolveAt(_pendingSlashKillPoint);
            _pendingSlashKillUntil = -1f;
            _liveSlash = null;
        }
    }

    // ── Trade-loss projectile dissolve ───────────────────────────────────────
    // When this fighter LOSES a trade (interrupted, fully blocked, parried), their flying slash
    // shouldn't pass through the winner's VFX — it flies to the clash point and fizzles there.
    private SlashProjectile _liveSlash;            // this client's instance of our latest slash
    private Vector3 _pendingSlashKillPoint;        // kill order that arrived before the slash spawned
    private float   _pendingSlashKillUntil = -1f;  // valid window for the pending order

    [Server] public void ServerDissolveSlash(Vector3 clashPoint) => RpcDissolveSlash(clashPoint);

    [ClientRpc]
    private void RpcDissolveSlash(Vector3 clashPoint)
    {
        if (_liveSlash != null)
        {
            _liveSlash.DissolveAt(clashPoint);
            _liveSlash = null;
        }
        else
        {
            // Slash not spawned yet (slashSpawnDelay) — latch the order for when it appears.
            _pendingSlashKillPoint = clashPoint;
            _pendingSlashKillUntil = Time.time + 1f;
        }
    }

    private void Start()
    {
        _vcm = GetComponent<VoiceCommandManager>();
        _cardManager = GetComponent<CardManager>();
        _gloveGlow = GetComponent<GloveBeatGlow>();
        _swordArc  = GetComponent<SwordArcTrail>();
        _cardVfx   = GetComponent<FighterCardVFX>();
    }


    // ── Glove juice hooks (server triggers, all clients flash) ──────────────
    // moveTrigger passed so the haptic hits the correct hand (Strike/Throw = right, else both).
    [Server] public void GloveStrikeFlash(string moveTrigger = "")
    {
        CardFamily fam = _cardManager != null ? _cardManager.FamilyOfTrigger(moveTrigger) : CardFamily.Strike;
        bool isOffense = fam == CardFamily.Strike || fam == CardFamily.Throw;
        RpcGloveStrike(isOffense);
    }
    [ClientRpc] private void RpcGloveStrike(bool offenseHand)
    {
        if (_gloveGlow != null) _gloveGlow.FlashStrike();
        if (isLocalPlayer)
        {
            var hand = offenseHand ? VRHaptics.Hand.Right : VRHaptics.Hand.Left;
            // Guaranteed immediate JOLT the instant your hit lands (a single hard synchronous pulse),
            // then the fuller StrikeLanded sequence for the follow-through. The lone pulse ensures you
            // ALWAYS feel the connect even if the coroutine sequence gets starved.
            VRHaptics.Pulse(hand, 1f, 0.14f);
            VRHaptics.StrikeLanded(hand);
        }
    }
    [ClientRpc] private void RpcGloveHurt()   { if (_gloveGlow != null) _gloveGlow.FlashHurt(); }

    // ── Per-family HIT effect (server triggers; prefab comes from THIS fighter's FighterCardVFX) ──
    [Server]
    public void SpawnFamilyHit(string trigger, Vector3 victimPos)
    {
        if (_cardManager == null) _cardManager = GetComponent<CardManager>();
        if (_cardManager == null) return;
        RpcFamilyHit((int)_cardManager.FamilyOfTrigger(trigger), victimPos);
    }

    [ClientRpc]
    private void RpcFamilyHit(int family, Vector3 victimPos)
    {
        CardVfx?.PlayHit((CardFamily)family, victimPos);
    }

    // ── Sword impact VFX (server triggers a hit-point burst on all clients) ──
    [Server] public void SpawnSwordImpact(Vector3 pos) { if (swordImpactVfx != null) RpcSwordImpact(pos); }
    [ClientRpc] private void RpcSwordImpact(Vector3 pos)
    {
        if (swordImpactVfx == null) return;
        var fx = Instantiate(swordImpactVfx, pos, Quaternion.identity);
        if (!Mathf.Approximately(slashSizeScale, 1f)) fx.transform.localScale *= slashSizeScale;
        Destroy(fx, 2f);
    }

    // ── Parry reversal VFX ──────────────────────────────────────────────────
    // When a parry/reflect succeeds, this fighter (the ATTACKER who got parried) has their own
    // strike VFX flung BACK at them from the defender. Speed-matched so the bolt covers the gap in
    // exactly `flightTime`, landing the moment the attacker's hurt/blood reaction plays.
    // Called on the SERVER, on the ATTACKER's PlayerCombat, with the defender (parrier) as source.
    [Server]
    public void SpawnReversedStrike(PlayerCombat defender, float flightTime)
    {
        if (slashProjectilePrefab == null || defender == null) return;
        Vector3 from = defender.slashSpawnPoint != null
            ? defender.slashSpawnPoint.position
            : defender.transform.position + Vector3.up * 1.2f;
        Vector3 to = slashSpawnPoint != null
            ? slashSpawnPoint.position
            : transform.position + Vector3.up * 1.2f;
        // The attacker's ORIGINAL forward slash dissolves at the parry point (the defender) so it
        // doesn't pass through — then the reversed slash flies back from there.
        ServerDissolveSlash(from);
        RpcReversedStrike(from, to, Mathf.Max(0.05f, flightTime));
    }

    [ClientRpc]
    private void RpcReversedStrike(Vector3 from, Vector3 to, float flightTime)
    {
        if (slashProjectilePrefab == null) return;

        Vector3 dir = to - from;
        if (dir.sqrMagnitude < 0.0001f) dir = transform.forward;
        Quaternion rot = Quaternion.LookRotation(dir.normalized, Vector3.up) * Quaternion.Euler(slashRotationOffset);

        var go = Instantiate(slashProjectilePrefab, from, rot);

        // Speed-match: cover the distance in exactly flightTime so it lands on the hurt animation.
        float dist = dir.magnitude;
        var sp = go.GetComponent<SlashProjectile>();
        if (sp == null) sp = go.AddComponent<SlashProjectile>();
        sp.speed    = dist / flightTime;
        sp.lifetime = flightTime + 0.15f; // small tail so it isn't culled exactly on impact

        // HAPTIC: this RPC runs on the ATTACKER whose strike got reflected. If that's the local
        // player — clang + fading stutter. Otherwise the local player is the one who PARRIED —
        // give them the satisfying "shing" on the parry hand.
        if (isLocalPlayer) VRHaptics.GotParried();
        else VRHaptics.ParrySuccess();
    }

    public void VoiceAttackJab() { if (isLocalPlayer && !IsDead) _attackQueue.Enqueue("Jab"); }
    public void VoiceAttackCross() { if (isLocalPlayer && !IsDead) _attackQueue.Enqueue("Cross"); }
    public void VoiceAttackHook() { if (isLocalPlayer && !IsDead) _attackQueue.Enqueue("Hook"); }
    public void VoiceAttackBlock() { if (isLocalPlayer && !IsDead) _attackQueue.Enqueue("Block"); }

    private void Update()
    {
        // Drone-rush HELD stagger: keep the bot in the stagger state if something pulls it off, WITHOUT
        // restarting the clip (the old version called Play(..., 0f) every off-frame, which rewound the
        // looping clip to frame 0 → the visible fidget). We only re-issue Play when the animator is
        // genuinely on a DIFFERENT state and not already transitioning INTO the stagger, and we don't
        // pass a normalizedTime so a re-issue can't rewind it.
        if (HeldStaggerActive && animator != null && !string.IsNullOrEmpty(heldStaggerState))
        {
            var cur  = animator.GetCurrentAnimatorStateInfo(0);
            bool onState = cur.IsName(heldStaggerState);
            bool goingTo = animator.IsInTransition(0) &&
                           animator.GetNextAnimatorStateInfo(0).IsName(heldStaggerState);
            if (!onState && !goingTo)
                animator.Play(heldStaggerState); // no time arg = don't rewind if already there
        }

        // Percentage system — no health bar UI updates needed here

        // --- Fade the timing text ---
        if (isLocalPlayer && _timingFade > 0)
            _timingFade -= Time.deltaTime * 1.5f;

        // --- Decay pressure level ---
        if (isLocalPlayer && _pressureLevel > 0f)
            _pressureLevel = Mathf.Max(0f, _pressureLevel - PRESSURE_DECAY * Time.deltaTime);

        // Local Parry Spike Check + Stagger Recovery: Only runs for the local player
        if (isLocalPlayer && !IsDead && !IsHurting)
        {
            CheckLocalParryTiming();
            if (IsStaggered) UpdateStaggerRecovery();
            else             { _staggerRecoveryCharge = 0f; _wasStaggered = false; }

            // FLAT live power feed: in VR the meter fills live from the controller trigger-charge
            // (VRHands → SetLiveCharge). Flat builds had nothing driving it, so the bar looked dead.
            // Feed the live MIC volume so the meter responds to the player's voice in real time, the
            // same way the VR charge does.
            if (!VRCameraDriver.VRActive && vp != null && _vcm != null)
            {
                if (_powerMeter == null) _powerMeter = GetComponentInChildren<PowerMeterReactor>(true);
                if (_powerMeter != null)
                {
                    float thr = _vcm.parryVolumeThreshold;
                    float live = Mathf.Clamp01((vp.CurrentFestivalVolume - thr) / Mathf.Max(0.01f, 1f - thr));
                    _powerMeter.SetLiveCharge(live);
                }
            }
        }

        if (!isLocalPlayer || IsDead || IsHurting) return;
        if (!isAttacking && _attackQueue.Count > 0) StartCoroutine(PerformAttack(_attackQueue.Dequeue()));
    }

    // --- RESTORED ANIMATION EVENT FUNCTIONS ---
    public void StartAttackWindow() { isAttacking = true; }
    public void EndAttackWindow() { isAttacking = false; _cardVfx?.StopAura(); } // attack anim done → kill weapon aura

    private void CheckLocalParryTiming()
    {
        if (RhythmRoundManager.Instance == null) return;

        // During the drone-rush segment the player ONLY punches drones — suppress all normal on-beat
        // card moves (shout/trigger-release won't fire an attack/defense).
        if (DroneRushSegment.Instance != null && DroneRushSegment.Instance.SegmentActive) return;

        var   rmm          = RhythmRoundManager.Instance;
        float currentTime  = rmm.GetCurrentTrackTime();
        float beatFireTime = rmm.lastBeatFireTime;

        // ── Dead zone: silence spike detection for BEAT_DEAD_ZONE seconds after each beat ──
        if (beatFireTime > 0f && currentTime - beatFireTime < BEAT_DEAD_ZONE) return;

        // ── Reset first-spike gate when a new beat cycle begins ───────────────────────────
        if (beatFireTime != _lastTrackedBeatFire)
        {
            _spikeLockedThisBeat   = false;
            _staggerTimingEscaped  = false;
            _lastTrackedBeatFire   = beatFireTime;

            // The move that just fired now becomes the sticky/active move LOCALLY — re-arm it and
            // mark it replaceable so the player can immediately pick a different card this new beat
            // (without waiting for the server's re-stick RPC to round-trip).
            if (rmm.IsSingleMoveMode() && stickyMoveEnabled)
                RestickPendingMove();
        }

        bool isChainMode = !rmm.IsSingleMoveMode();

        // ── Logic gate: need a queued action to care about timing ─────────────────────────
        string currentMove;
        bool   isDashing;

        if (isChainMode)
        {
            if (_comboBuffer.Count == 0) return;
            var latest = _comboBuffer[_comboBuffer.Count - 1];
            currentMove = latest.attack;
            isDashing   = (latest.dash != Vector3.zero);
        }
        else
        {
            currentMove = _pendingAttackTrigger;
            isDashing   = (_pendingDashDirection != Vector3.zero);
            if (string.IsNullOrEmpty(currentMove) && !isDashing) return;
        }

        float nextBeat        = rmm.GetNextBeatTime();
        float timeUntilImpact = nextBeat - currentTime;

        // Under-pressure players get a tighter shout window (max 40% reduction at full pressure)
        float percentageReduction = GetShoutWindowReduction(CurrentPercentage);
        float effectiveWindow = SHOUT_WINDOW * (1f - _pressureLevel * 0.4f - percentageReduction);

        // Trait/Vex card timing window modifiers
        float timingWindowMult = 1f;
        if (!string.IsNullOrEmpty(activeTraitId) && activeTraitId == "quicktrigger")
            timingWindowMult *= 1.20f;
        if (!string.IsNullOrEmpty(activeTraitId) && activeTraitId == "heavy")
            timingWindowMult *= 1.10f;
        if (activeTraitId == "quicktrigger")
            timingWindowMult *= 1.20f;

        PlayerCombat opponent = GetComponent<PlayerController>()?.GetOpponent()?.GetComponent<PlayerCombat>();
        if (opponent != null && !string.IsNullOrEmpty(opponent.activeTraitId) && opponent.activeTraitId == "stunning")
            timingWindowMult *= 0.90f;

        effectiveWindow *= timingWindowMult;
        bool inShoutWindow = timeUntilImpact > 0f && timeUntilImpact <= effectiveWindow;
        if (!inShoutWindow)
        {
            // TOO EARLY coaching: the beat window hasn't opened yet, but if the local player ALREADY
            // shouted or let go of the trigger they jumped the gun (the classic new-player mistake).
            // Flag it so the tutorial can say "wait for the ring to close" — without consuming the
            // input, and only when the beat is still clearly ahead (not a near-miss on the late side).
            if (isLocalPlayer && timeUntilImpact > effectiveWindow)
            {
                bool earlyShout = vp != null && _vcm != null
                    && vp.CurrentFestivalVolume >= _vcm.parryVolumeThreshold;
                bool earlyRelease = VRCameraDriver.VRActive && VRHands.AnyReleasePending();
                if (earlyShout || earlyRelease)
                    VREarlyTime = Time.time;
            }
            return;
        }

        // ── First spike only: once locked, ignore further input until next beat cycle ──────
        if (_spikeLockedThisBeat) return;

        // ── INPUT: the on-beat SHOUT fires the move and LOCKS IN your power (VR + flat). ──
        // VR:   power = the trigger-charge you built by pulsing the controller; the shout locks it
        //       in (a loud shout tops it up a little). The shout's closeness to the beat is what
        //       scales damage — so the closeness reward is shout-only.
        // Flat: the shout's own volume is the power.
        // CurrentFestivalVolume subtracts a configurable noise floor (loud venues read as 0); Vosk
        // speech recognition still works on the raw audio, this only affects timing spikes.
        float shoutVol = vp != null ? vp.CurrentFestivalVolume : -1f;
        bool  gotShout = vp != null && _vcm != null && shoutVol >= _vcm.parryVolumeThreshold;

        // VR: the MATCHING controller's trigger release on the beat fires the move — RIGHT hand for
        // Strike/Throw, LEFT for Block/Parry (Support: either). Shout is an alternative lock-in; both
        // lock the power and scale by beat-closeness.
        bool  gotRelease    = false;
        float releaseCharge = 0f;
        bool  gotSwing      = false;   // FAIL-SAFE: a raw on-beat punch (no shout, no trigger)
        float swingSpeed    = 0f;
        if (VRCameraDriver.VRActive && _cardManager != null)
        {
            CardFamily fam = _cardManager.FamilyOfTrigger(currentMove);
            bool isOffense = fam == CardFamily.Strike || fam == CardFamily.Throw;
            bool isDefense = fam == CardFamily.Block  || fam == CardFamily.Parry;
            if (isOffense)      gotRelease = VRHands.ConsumeRelease(true,  out releaseCharge); // right hand
            else if (isDefense) gotRelease = VRHands.ConsumeRelease(false, out releaseCharge); // left hand
            else                gotRelease = VRHands.ConsumeRelease(true, out releaseCharge)   // Support: either hand
                                          || VRHands.ConsumeRelease(false, out releaseCharge);

            // FAIL-SAFE for showcases: if they neither shouted nor pulled the trigger, a real SWING of
            // the matching hand on the beat still fires the move — so a first-timer who just punches
            // (as they instinctively will) connects. Same hand mapping as the trigger.
            if (!gotShout && !gotRelease && swingLockInEnabled)
            {
                if (isOffense)      gotSwing = VRHands.IsSwinging(true,  swingLockInMinSpeed, out swingSpeed);
                else if (isDefense) gotSwing = VRHands.IsSwinging(false, swingLockInMinSpeed, out swingSpeed);
                else                gotSwing = VRHands.IsSwinging(true,  swingLockInMinSpeed, out swingSpeed)
                                            || VRHands.IsSwinging(false, swingLockInMinSpeed, out swingSpeed);
            }
        }

        if (!gotShout && !gotRelease && !gotSwing) return; // shout OR trigger-release OR a real swing on the beat

        float currentVol;
        bool  usedShout = gotShout;

        if (VRCameraDriver.VRActive)
        {
            // Power = the trigger-charge you built (shout and release both consume the same charge;
            // release also captured the charge at the let-go moment). A loud shout tops it up a little.
            float charge = VRHands.ConsumeCharge();
            if (gotRelease) charge = Mathf.Max(charge, releaseCharge);
            // Swing fail-safe: derive power straight from how hard they swung (no charge built), so a
            // committed punch still lands a solid hit even with an empty charge meter.
            if (gotSwing) charge = Mathf.Max(charge, Mathf.Clamp01(swingSpeed / 6f));
            float shoutBonus = gotShout
                ? Mathf.Clamp01((shoutVol - _vcm.parryVolumeThreshold) / Mathf.Max(0.01f, 1f - _vcm.parryVolumeThreshold))
                : 0f;
            currentVol = Mathf.Clamp(charge + shoutBonus * 0.15f, 0f, 1.25f);
        }
        else
        {
            currentVol = shoutVol;
        }

        // ── Register the timing spike ─────────────────────────────────────────────────────
        _spikeLockedThisBeat = true;

        // Fire the hand card's activation shine the moment you shout on the beat (local feedback).
        _cardManager?.PlayActivationFor(currentMove);

        // Lock the power meter in on the shout: the TIGHTER the shout (closer to the beat), the
        // higher it locks. It holds for ~holdTime, then dissolves again unless you keep pulsing.
        if (_powerMeter == null) _powerMeter = GetComponentInChildren<PowerMeterReactor>(true);
        if (_powerMeter != null)
        {
            float closeness = 1f - Mathf.Clamp01(timeUntilImpact / Mathf.Max(0.001f, effectiveWindow));
            _powerMeter.LockIn(currentVol, closeness);

            // HAPTIC: tight lock-in = crisp double-tap on the firing hand; sloppy = mushy fizzle.
            VRHaptics.Hand lockHand = VRHaptics.Hand.Both;
            if (_cardManager != null)
            {
                CardFamily lockFam = _cardManager.FamilyOfTrigger(currentMove);
                if (lockFam == CardFamily.Strike || lockFam == CardFamily.Throw) lockHand = VRHaptics.Hand.Right;
                else if (lockFam == CardFamily.Block || lockFam == CardFamily.Parry) lockHand = VRHaptics.Hand.Left;
            }
            if (closeness >= _powerMeter.goodLockCloseness) VRHaptics.LockGood(lockHand, closeness);
            else VRHaptics.LockBad(lockHand);
        }

        if (!isChainMode && currentMove == "ParryIntent")
        {
            CmdConfirmEliteParry(currentTime, currentVol);
            Debug.Log($"<color=green>VOCAL SUCCESS:</color> Elite Parry at {timeUntilImpact:F3}s until beat. (vol={currentVol:F2} shout={usedShout})");
            _pendingAttackTrigger = "ParryIntent";
        }
        else
        {
            CmdRegisterVocalSpike(currentTime, currentVol, Mathf.Abs(timeUntilImpact));
            string label = isChainMode ? "CHAIN" : currentMove;
            Debug.Log($"<color=cyan>SPIKE [{label}]:</color> t={currentTime:F3}s  Δbeat={timeUntilImpact:F3}s  vol={currentVol:F2} shout={usedShout}");
        }
    }

    private void UpdateStaggerRecovery()
    {
        var rmm = RhythmRoundManager.Instance;
        if (rmm == null || !rmm.isRoundActive || vp == null) return;

        // On the first frame of stagger, compute a charge rate achievable within the stagger window.
        // estimatedInterval ≈ time to next beat; multiply by beats remaining to get total window.
        // Target: player can fill the bar in 65% of that window at sustained max volume.
        if (!_wasStaggered)
        {
            _wasStaggered = true;
            float trackTime0    = rmm.GetCurrentTrackTime();
            float nextBeat0     = rmm.GetNextBeatTime();
            float estInterval   = (nextBeat0 > 0f) ? Mathf.Max(0.3f, nextBeat0 - trackTime0) : 2.0f;
            float windowPerBeat = Mathf.Max(0.1f, estInterval - 0.4f); // subtract post-beat dead zone
            float totalWindow   = StaggerBeatsRemaining * windowPerBeat;
            _dynamicStaggerRate = Mathf.Clamp(1.0f / (totalWindow * 0.65f), 0.40f, 6.0f);
        }

        float vol       = vp.CurrentFestivalVolume;
        float trackTime = rmm.GetCurrentTrackTime();
        float lastBeat  = rmm.lastBeatFireTime;
        float nextBeat  = rmm.GetNextBeatTime();

        // Brief cooldown after beat fires — don't let impact noise charge the bar
        bool postBeatCooldown = lastBeat > 0f && trackTime - lastBeat < 0.4f;

        if (!postBeatCooldown && vol >= STAGGER_CHARGE_VOL_MIN)
        {
            // Quadratic scale from threshold — loud shouting charges significantly faster
            float volScale = Mathf.Clamp01((vol - STAGGER_CHARGE_VOL_MIN) / (1f - STAGGER_CHARGE_VOL_MIN));
            _staggerRecoveryCharge = Mathf.Min(1f, _staggerRecoveryCharge + _dynamicStaggerRate * (0.25f + 0.75f * volScale * volScale) * Time.deltaTime * 3f * 1.8f);
            if (_staggerRecoveryCharge >= 1f)
            {
                _staggerRecoveryCharge = 0f;
                CmdEscapeStaggerCharge();
                return;
            }
        }
        else
        {
            _staggerRecoveryCharge = Mathf.Max(0f, _staggerRecoveryCharge - STAGGER_CHARGE_DECAY * Time.deltaTime);
        }

        // Timing escape: shout with good volume in the shout window before the next beat
        if (!_staggerTimingEscaped && nextBeat > 0f && _vcm != null)
        {
            float timeToNext = nextBeat - trackTime;
            float escapeWindow = SHOUT_WINDOW * (1f - GetShoutWindowReduction(CurrentPercentage));
            if (timeToNext >= 0f && timeToNext <= escapeWindow && vol >= _vcm.parryVolumeThreshold)
            {
                _staggerTimingEscaped = true;
                CmdEscapeStaggerTiming();
            }
        }
    }

    [Command]
    private void CmdEscapeStaggerCharge()
    {
        if (!IsStaggered) return;
        ClearStagger();
        _cardManager?.ResetSlots();
        RpcOnStaggerEscape(false);
    }

    [Command]
    private void CmdEscapeStaggerTiming()
    {
        if (!IsStaggered) return;
        ClearStagger();
        // Auto-queue a Block so the incoming attack resolves through normal hit detection
        _pendingAttackTrigger = "Block";
        _pendingDashDirection = Vector3.zero;
        _cardManager?.ResetSlots();
        RpcOnStaggerEscape(true);
    }

    [ClientRpc]
    private void RpcOnStaggerEscape(bool wasTiming)
    {
        if (!isLocalPlayer) return;
        _staggerRecoveryCharge = 0f;
        _staggerTimingEscaped  = false;
        if (CameraShake.Instance != null) CameraShake.Instance.Shake(0.15f, 0.22f);
        VRHaptics.StaggerEscape();
    }

    [TargetRpc]
    public void TargetShakeCamera(NetworkConnection target, float duration, float magnitude)
    {
        CameraShake.Instance?.Shake(duration, magnitude);
    }

    [TargetRpc]
    public void TargetFlashSuccess(NetworkConnection target)
    {
        _successFlashFade = 1f;
    }

    [Command]
    void CmdConfirmEliteParry(float spikeTime, float vol)
    {
        lastVocalSpikeTime   = spikeTime;
        lastVocalSpikeVolume = vol;
        IsParryActive = true;


        if (animator != null) animator.Play("ParryIntent");

        TargetAddEnergy(1);
        TargetShakeCamera(connectionToClient, 0.2f, 0.28f);
        TargetFlashSuccess(connectionToClient);
        StartCoroutine(ResetParryFlag());
    }

    [Command]
    void CmdRegisterVocalSpike(float time, float vol, float offset)
    {
        lastVocalSpikeTime   = time;
        lastVocalSpikeVolume = vol;
        lastVocalSpikeOffset = offset;
    }

    [Command]
    void CmdConfirmSuccessfulParry()
    {
        IsParryActive = true;


        if (animator != null)
        {
            // Snapping to the pose in 0.02s for that 'crunchy' pose-to-pose feel
            animator.Play("ParryIntent");
        }

        // Return 1 Energy as a reward for the tight timing
        TargetAddEnergy(1);
        TargetShakeCamera(connectionToClient, 0.2f, 0.28f);
        TargetFlashSuccess(connectionToClient);

        // We keep the hitbox active for 0.3s to match the window
        StartCoroutine(ResetParryFlag());
    }

    [Server]
    public void TriggerStagger(int beats = 3)
    {
        IsStaggered            = true;
        StaggerBeatsRemaining  = beats;
        RpcTriggerStaggerAnim();
    }

    [ClientRpc]
    private void RpcTriggerStaggerAnim()
    {
        if (animator != null) animator.SetTrigger("Stagger");
        if (isLocalPlayer) VRHaptics.StaggerStart();
    }

    [Server]
    public void ClearStagger() { IsStaggered = false; StaggerBeatsRemaining = 0; }

    // ── Drone-rush: enter/exit a HELD stagger pose for the whole segment ─────────────────────────
    // The normal TriggerStagger fires a one-shot "Stagger" trigger that recovers on its own. For the
    // drone segment we want the bot to drop into "Stagger New" and STAY there until told to recover.
    // True (synced) while the drone-rush held stagger is active — drives a per-frame re-assert of the
    // pose so transitions/Any-State can't snap the bot back to Boxing Idle.
    [SyncVar] public bool HeldStaggerActive = false;

    [Server]
    public void EnterHeldStagger()
    {
        IsStaggered = true;
        HeldStaggerActive = true;
        StaggerBeatsRemaining = 9999; // pinned; the segment clears it explicitly
        RpcPlayHeldStagger(true);
    }

    [Server]
    public void ExitHeldStagger()
    {
        IsStaggered = false;
        HeldStaggerActive = false;
        StaggerBeatsRemaining = 0;
        RpcPlayHeldStagger(false);
    }

    [Header("Drone-rush held stagger (match these to the bot's Animator state names)")]
    [Tooltip("Animator STATE name for the held stagger pose.")]
    public string heldStaggerState = "Held Stagger State";
    [Tooltip("Animator STATE name to return to when the segment ends.")]
    public string idleState = "Boxing Idle";
    [Tooltip("THE single hurt animation everyone uses when taking a hit (the old random Hurt 1–4 is " +
             "gone). Match this to the SAME state BotBeatApproach plays for got-hit recovery.")]
    public string hurtState = "Knockback";
    [Tooltip("Playback speed for the BOT's move/attack animation. Now that the swing starts ~windUpTime " +
             "(0.62s) before the beat WHILE already planted, it plays at natural speed (1) so its impact " +
             "lands on the beat instead of finishing early. Nudge up only if the swing still lands late.")]
    [Range(1f, 4f)] public float botMoveAnimSpeed = 1f;

    [ClientRpc]
    private void RpcPlayHeldStagger(bool on)
    {
        if (animator == null) return;
        // animator.Play() forces the state immediately, IGNORING the controller's transition graph —
        // this is what reliably FORCES and HOLDS the pose (CrossFade can be blocked if there's no
        // transition into the state). If you don't see it, the state NAME below is wrong for this rig.
        if (on)
        {
            animator.applyRootMotion = false; // stagger clip must NOT translate/rotate the body off spawn
            animator.Play(heldStaggerState, 0, 0f);
            if (isLocalPlayer) VRHaptics.StaggerStart();
        }
        else
        {
            animator.applyRootMotion = false; // keep it off — combat anims here are in-place
            animator.Play(idleState, 0, 0f);
        }
    }

    IEnumerator ResetParryFlag()
    {
        yield return new WaitForSeconds(0.6f);
        IsParryActive        = false;
        lastVocalSpikeTime   = -1f;
        lastVocalSpikeVolume = 0f;
        lastVocalSpikeOffset = -1f;
    }

    // Maps a logical move trigger to the Animator state/trigger name.
    // The whole Parry family shares ONE animation ("ParryIntent"). Everything else uses its own name.
    private string AnimName(string trigger)
    {
        switch (trigger)
        {
            case "ParryIntent":
            case "Reverse":
            case "Clutch":
            case "Mirror":
                return "ParryIntent";
            default:
                return trigger;
        }
    }

    // The BOT plays one of THREE random animations for any Strike/Throw-family attack so its offense
    // reads with variety. Picked ONCE on the server per attack and threaded through the RPC so every
    // view plays the same one (a per-view roll would desync the bot's local + remote animations).
    // Returns "" for anything that should keep the NORMAL trigger-driven animation (humans, and the
    // bot's Block/Parry/Dash/Support) — an empty string means "no random override, use the usual path".
    private static readonly string[] BotStrikeThrowAnims = { "Attack 1", "Attack 2", "Jab" };
    private string ResolveBotStrikeThrowAnim(string trigger)
    {
        var fam = _cardManager != null ? _cardManager.FamilyOfTrigger(trigger) : CardFamily.Support;
        if (GetComponent<BotController>() != null && (fam == CardFamily.Strike || fam == CardFamily.Throw))
            return BotStrikeThrowAnims[Random.Range(0, BotStrikeThrowAnims.Length)];
        return ""; // not a bot Strike/Throw → no override; the normal AnimName/trigger path is used
    }

    private IEnumerator PerformAttack(string trigger)
    {
        isAttacking = true;
        if (isLocalPlayer && animator != null) animator.Play(AnimName(trigger), 0, 0f);
        // Weapon aura + family sword tint for ANY card (attack, block, parry, support).
        if (isLocalPlayer) _cardVfx?.OnCardTriggered(trigger);
        // Your own blade arc + slash VFX (local view) on offensive swings.
        if (isLocalPlayer && CardManager.IsAttackTrigger(trigger))
        {
            _lastSwingTrigger = trigger;
            if (_swordArc != null) _swordArc.StartSwing();
            if (!slashViaAnimationEvent) ThrowSlash(trigger);
        }
        TriggerDefenseVfx(trigger); // block/parry VFX (0.2s into the anim)
        int damageToSet = trigger switch
        {
            "Jab"              => 10,
            "Cross"            => 15,
            "Hook"             => 25,
            "UnbreakablePunch" => 30,
            "Grapple"          => 18,
            "Fake"            => 5,
            "Uppercut"         => 20,
            "Sweep"            => 16,
            "Overclock"        => 35,
            "Reverse"          => 0,
            _                  => 0
        };
        // The bot is server-owned (no client authority), so it can't call a [Command]. On the
        // server, run the attack logic directly; only a remote client player goes through the Command.
        if (isServer) ServerDoAttack(trigger, damageToSet);
        else if (isLocalPlayer) CmdTriggerAttack(trigger, damageToSet);
        yield return new WaitForSeconds(0.1f);
    }

    // Latest timing feedback for the local player — read by VRWorldHud to show it in world space.
    public static string VRTimingText = "";
    public static Color  VRTimingColor = Color.white;
    public static float  VRTimingTime = -999f;

    // "Too early" coaching signal: set when the local player shouts / releases the trigger well
    // BEFORE the beat window opens (a very common new-player mistake — they fire as soon as they're
    // ready instead of waiting for the ring to close). The tutorial reads this to coach them, and it
    // also counts toward the "are they getting it?" streak that hides/shows the tutorial.
    public static float  VREarlyTime = -999f;

    // DEDICATED timing-grade signal for the beat coach — separate from VRTimingText because the
    // element system overwrites VRTimingText with status/reaction names ("Frozen", "Burn"…) right
    // after a hit, which would clobber the "GOOD"/"EXCELLENT"/"BAD" grade before the coach reads it.
    // Only TargetShowTimingFeedback writes these, so the streak logic always sees the true grade.
    public static string VRGradeText = "";
    public static float  VRGradeTime = -999f;

    private PowerMeterReactor _powerMeter;

    [TargetRpc]
    public void TargetShowTimingFeedback(string rating)
    {
        _timingText = rating;
        _timingFade = 1.0f;

        if (rating == "EXCELLENT") { _timingColor = Color.cyan;  CommentaryManager.Instance?.Trigger(CommentaryEvent.Excellent); }
        else if (rating == "GOOD") { _timingColor = Color.green; CommentaryManager.Instance?.Trigger(CommentaryEvent.Good); }
        else                       { _timingColor = Color.red;   CommentaryManager.Instance?.Trigger(CommentaryEvent.BadTiming); }

        // Mirror to the VR world-space HUD.
        VRTimingText = rating;
        VRTimingColor = _timingColor;
        VRTimingTime = Time.time;

        // Dedicated grade signal (only set here) — the beat coach reads this so the element system's
        // VRTimingText overwrite can't hide the player's real on-beat result from the streak logic.
        VRGradeText = rating;
        VRGradeTime = Time.time;

        // Drive the cyberpunk power cone from BOTH timing grade AND input power.
        if (_powerMeter == null) _powerMeter = GetComponentInChildren<PowerMeterReactor>(true);
        if (_powerMeter != null)
        {
            _powerMeter.RegisterGrade(rating);
            // Also push the combined power (VR swing + mic shout) to the meter.
            // lastVocalSpikeVolume is synced server→client, so it's current here.
            if (lastVocalSpikeVolume > 0f)
                _powerMeter.RegisterPower(lastVocalSpikeVolume);
        }
    }
    public void QueueRhythmMove(string attackTrigger, Vector3 dashDir)
    {
        if (IsDead || IsHurting) return;

        if (isLocalPlayer)
        {
            if (!isServer)
            {
                QueueLogic(attackTrigger, dashDir);
                // Client-side prediction: play wind-up immediately without waiting for server round-trip
                if (RhythmRoundManager.Instance != null && RhythmRoundManager.Instance.IsWindUpActive)
                {
                    bool isFirstMove = RhythmRoundManager.Instance.IsSingleMoveMode() ||
                                       _comboBuffer.Count == 1;
                    if (isFirstMove) ExecuteMoveEffect(attackTrigger, dashDir);
                }
            }
            CmdQueueRhythmMove(attackTrigger, dashDir);
        }
        else if (isServer)
        {
            // --- BOT LOGIC ---
            // The Bot lives purely on the Server and isn't a "Local Player".
            // It just needs to drop its moves straight into the logic.
            QueueLogic(attackTrigger, dashDir);
        }
    }

    [Command]
    private void CmdQueueRhythmMove(string attack, Vector3 dash)
    {
        QueueLogic(attack, dash);

        if (RhythmRoundManager.Instance.IsWindUpActive)
        {
            if (_comboBuffer.Count == 1 || RhythmRoundManager.Instance.IsSingleMoveMode())
            {
                if (connectionToClient == null)
                    ExecuteMoveEffect(attack, dash); // bot (no client connection)
                else if (isLocalPlayer)
                    TargetTriggerRhythmWindUp(attack, dash); // host's own player: TargetRpc is local, no delay
                // else: pure remote client already ran client-side prediction, skip to avoid double-play
            }
        }
    }

    // THE ONLY QUEUELOGIC YOU NEED
    private void QueueLogic(string attackTrigger, Vector3 dashDir)
    {
        if (RhythmRoundManager.Instance.IsSingleMoveMode())
        {
            if (!string.IsNullOrEmpty(attackTrigger)) { _pendingAttackTrigger = attackTrigger; _pendingDashDirection = Vector3.zero; }
            if (dashDir != Vector3.zero) { _pendingDashDirection = dashDir; _pendingAttackTrigger = ""; }

            // Fresh deliberate pick this beat — no longer just a sticky re-arm.
            _pendingFromSticky = false;

            // Remember this pick as the sticky/active move so it auto-repeats on later beats until
            // the player picks a different card (see ConsumeNextMove / RestickPendingMove).
            if (stickyMoveEnabled)
            {
                _stickyAttackTrigger = _pendingAttackTrigger;
                _stickyDashDirection = _pendingDashDirection;
            }
        }
        else
        {
            // This is what allows the "Masterpiece" multi-attack
            if (_comboBuffer.Count < RhythmRoundManager.Instance.currentComboCount)
            {
                _comboBuffer.Add(new RhythmAction { attack = attackTrigger, dash = dashDir });
            }
        }


    }

    public RhythmAction PeekNextMove()
    {
        if (RhythmRoundManager.Instance.IsSingleMoveMode()) return new RhythmAction { attack = _pendingAttackTrigger, dash = _pendingDashDirection };
        return (_comboBuffer.Count > 0) ? _comboBuffer[0] : new RhythmAction { attack = "", dash = Vector3.zero };
    }

    /// The currently-pending move's trigger (locally readable, e.g. for VR controller-glow cues).
    /// Empty string when nothing is queued. Works in both single-move and chain modes.
    public string PendingMoveTrigger
    {
        get
        {
            if (RhythmRoundManager.Instance == null) return "";
            if (RhythmRoundManager.Instance.IsSingleMoveMode()) return _pendingAttackTrigger ?? "";
            return (_comboBuffer.Count > 0) ? (_comboBuffer[_comboBuffer.Count - 1].attack ?? "") : "";
        }
    }

    [Server]
    public void ConsumeNextMove()
    {
        if (RhythmRoundManager.Instance.IsSingleMoveMode())
        {
            // Sticky move: instead of clearing the pending move after the beat, re-arm it from the
            // last picked card so it auto-repeats next beat. The player still has to shout/release on
            // the beat to fire it; they only need to pick a card again to CHANGE the move.
            _pendingAttackTrigger = "";
            _pendingDashDirection = Vector3.zero;
            RestickPendingMove();
            // Mirror the re-arm to the owning client so its local timing check has a move to fire.
            if (connectionToClient != null) TargetRestickPendingMove(_stickyAttackTrigger, _stickyDashDirection);
        }
        else if (_comboBuffer.Count > 0) _comboBuffer.RemoveAt(0);
    }

    // Re-load the pending move from the sticky/active move (if any). No-op when sticky is off/empty.
    // Marks the move as sticky-armed so HasOpenSlot still lets the player pick a replacement card.
    private void RestickPendingMove()
    {
        if (!stickyMoveEnabled) return;
        if (!string.IsNullOrEmpty(_stickyAttackTrigger))
        {
            _pendingAttackTrigger = _stickyAttackTrigger;
            _pendingDashDirection = Vector3.zero;
            _pendingFromSticky = true;
        }
        else if (_stickyDashDirection != Vector3.zero)
        {
            _pendingDashDirection = _stickyDashDirection;
            _pendingAttackTrigger = "";
            _pendingFromSticky = true;
        }
    }

    [TargetRpc]
    private void TargetRestickPendingMove(string sticky, Vector3 stickyDash)
    {
        if (!isLocalPlayer) return;
        _stickyAttackTrigger = sticky;
        _stickyDashDirection = stickyDash;
        RestickPendingMove();
    }

    [Server]
    public void ExecuteRhythmWindUp()
    {
        // A staggered fighter does nothing this beat — no windup/move animation, so a held stagger pose
        // (e.g. the drone-rush segment) isn't overridden every beat. Hurt/dead also skip.
        if (IsStaggered || IsHurting || IsDead) return;

        var move = PeekNextMove();
        if (connectionToClient == null) ExecuteMoveEffect(move.attack, move.dash);
        else TargetTriggerRhythmWindUp(move.attack, move.dash);
    }

    [TargetRpc]
    public void TargetTriggerRhythmWindUp(string attack, Vector3 dash)
    {
        ExecuteMoveEffect(attack, dash);
        if (RhythmRoundManager.Instance.IsWindUpActive)
        {
            bool isCurrentBeat = RhythmRoundManager.Instance.IsSingleMoveMode() || _comboBuffer.Count == 1;
            if (!isCurrentBeat) return;
        }
    }

    private void ExecuteMoveEffect(string attack, Vector3 dash)
    {
        // Held-stagger fighter never plays a move — keeps the bot frozen in the stagger pose.
        if (HeldStaggerActive) return;

        if (!string.IsNullOrEmpty(attack))
        {
            // Bot Strike/Throw only: pick ONE of the 3 random anims here (server-side), remember it so
            // the RPC broadcasts the SAME pick. Empty for everything else = keep the normal path.
            _resolvedAttackState = ResolveBotStrikeThrowAnim(attack);

            // Play the animation: the random state if one was picked, otherwise the normal AnimName state.
            if (animator != null)
                animator.Play(!string.IsNullOrEmpty(_resolvedAttackState) ? _resolvedAttackState : AnimName(attack), 0, 0f);

            if (isLocalPlayer || (isServer && connectionToClient == null))
            {
                StartCoroutine(PerformAttack(attack));
            }
        }

        if (dash != Vector3.zero)
        {
            GetComponent<PlayerController>().ApplyDashExternal(dash);
        }
    }

    [TargetRpc] public void TargetAddEnergy(int amount) { if (isLocalPlayer) GetComponent<PlayerEnergy>().AddBonusEnergy(amount); }
    [Command] void CmdTriggerAttack(string t, int damage) => ServerDoAttack(t, damage);

    // Server-side attack: set hitbox damage and fire the visual Rpc. Safe to call directly from the
    // bot (server-owned). Null-checks the gloves so sword characters (no glove hitboxes) don't throw.
    [Server]
    void ServerDoAttack(string t, int damage)
    {
        if (weaponGloveLeft != null)
        {
            var h = weaponGloveLeft.GetComponent<HitboxProperties>();
            if (h != null) h.currentDamage = damage;
        }
        if (weaponGloveRight != null)
        {
            var h = weaponGloveRight.GetComponent<HitboxProperties>();
            if (h != null) h.currentDamage = damage;
        }
        // Resolve the state to broadcast HERE on the server. For the bot (server-owned) ExecuteMoveEffect
        // already ran server-side and picked its random Strike/Throw anim into _resolvedAttackState, so
        // reuse that exact pick. For a human (whose ExecuteMoveEffect ran on THEIR client, not here) send
        // empty and let each remote view map the trigger itself — no cross-context stale value.
        bool isBot = GetComponent<BotController>() != null;
        RpcTriggerAttack(t, isBot ? _resolvedAttackState : "");
    }
    [ClientRpc] void RpcTriggerAttack(string t, string resolvedState)
    {
        if (isLocalPlayer) return;
        // A held-stagger fighter (drone segment) must never play a move anim — that's what made the bot
        // stand up + swing on the beat then snap back. Ignore attack anims entirely while held.
        if (HeldStaggerActive) return;
        if (animator != null)
        {
            // Only the bot's random Strike/Throw pick comes through as a non-empty resolvedState — play
            // that state directly so every view shows the SAME random anim. EVERYTHING ELSE (humans, and
            // the bot's Block/Parry/Dash) keeps the ORIGINAL trigger-driven transition (SetTrigger), so
            // block/parry animations fire through the Animator's transition graph exactly as before.
            if (!string.IsNullOrEmpty(resolvedState))
                animator.Play(resolvedState, 0, 0f);
            else
                animator.SetTrigger(AnimName(t));
            // BOT only: speed up the move animation so the swing FINISHES before the beat (it arrives at
            // the attack point only a fraction of a second early). Reset back to 1 shortly after.
            if (GetComponent<BotController>() != null)
            {
                animator.speed = botMoveAnimSpeed;
                StartCoroutine(ResetBotAnimSpeed());
            }
        }
        // Weapon aura + family sword tint for ANY card (this is the copy the human watches).
        _cardVfx?.OnCardTriggered(t);
        // Opponent/bot blade arc + slash VFX.
        if (CardManager.IsAttackTrigger(t))
        {
            _lastSwingTrigger = t;
            if (_swordArc != null) _swordArc.StartSwing();
            if (!slashViaAnimationEvent) ThrowSlash(t);
        }
        TriggerDefenseVfx(t); // block/parry VFX (0.2s into the anim)
    }

    // Drone-rush hit: the bot is in a held stagger and must NOT play the hurt anim or get knocked away
    // (that flung it "to another world"). We only raise CurrentPercentage — which makes BloodOnHit spill
    // blood automatically — and show the damage number. No hurt trigger, no knockback, no stagger reset.
    [Server]
    public void TakeDroneHit(int damage)
    {
        if (damage <= 0) return;
        CurrentPercentage += damage;       // BloodOnHit watches this → blood spills on its own
        RpcShowDamageNumber(damage, true); // green "dealt" number over the bot
    }

    [Server]
    public void TakeDamage(int damage, Vector3 knockbackDir = default, bool isOpponentDamage = false, float hurtDelay = 0f)
    {
        StartCoroutine(FlashEffectRoutine());

        // SCORE: a hit landed → whoever dealt it BENEFITED. Award the dealer (this victim's opponent)
        // points scaled by the damage = "how hard you hit". This single chokepoint covers every clash
        // outcome: a normal landed hit (attacker scores), a mistimed defense (the attacker still
        // scores because the defender took damage), and a parry/reflect that turns a hit back on the
        // attacker (the original defender, now the dealer, scores — see AwardCounterBonus callers).
        // How POWERFUL was this hit (0..1)? Combine the attacker's locked-in power (VR trigger-charge +
        // shout, captured in lastVocalSpikeVolume which can reach ~1.25) so a hard, charged punch lands
        // a heavier reaction (deeper slow-mo, bigger shake). Default 0.5 if there's no attacker power.
        float hitPower01 = 0.5f;
        if (isServer && damage > 0)
        {
            var dealer = GetComponent<PlayerController>()?.GetOpponent()?.GetComponent<PlayerCombat>();
            if (dealer != null)
            {
                dealer.AddScore(damage * SCORE_HIT_PER_DAMAGE, "landed hit");
                hitPower01 = Mathf.Clamp01(dealer.lastVocalSpikeVolume / 1.25f);
            }
        }

        float oldPct = CurrentPercentage;
        CurrentPercentage += damage;
        // Stagger at every 50% damage threshold crossed (50, 100, 150, ...)
        if (isServer && Mathf.FloorToInt(CurrentPercentage / 50f) > Mathf.FloorToInt(oldPct / 50f))
            TriggerStagger(2);
        CancelDefenseVfx(); // kill any active block/parry VFX the moment a hit lands
        // hurtDelay > 0 (e.g. a parry reversal) holds the hurt/blood reaction until the flying VFX lands.
        // Single hurt animation now (was random Hurt 1–4) — the same Knockback state the bot recovers with.
        RpcTriggerHurt(hurtState, hurtDelay, damage, hitPower01);
        RpcShowDamageNumber(damage, isOpponentDamage);
        RpcGloveHurt();
        // GUARANTEED hurt sound: fire it HERE, at the single damage chokepoint, so EVERY hit is
        // audible no matter which trade branch caused it (normal hit, trap, cage, failed reverse/clutch
        // self-damage, a reflected counter landing on the attacker, combo-mode hits…). Individual call
        // sites used to each remember to call this and several didn't, leaving silent hits.
        if (damage > 0 && connectionToClient != null) TargetPlaySuccessSound("Hurt");
        if (knockbackDir != default) RpcNudgeBack(knockbackDir);
    }

    [ClientRpc]
    private void RpcShowDamageNumber(int damage, bool isOpponentDamage)
    {
        if (FloatingDamageTextManager.Instance == null) return;
        // Bright RED = damage YOU took.  Bright GREEN = damage YOU dealt to the opponent.
        Color damageColor = isLocalPlayer ? new Color(1f, 0.22f, 0.18f) : new Color(0.35f, 1f, 0.45f);
        FloatingDamageTextManager.Instance.ShowDamage(damage, damageColor, transform.position + Vector3.up * 2f);
    }

    [ClientRpc]
    void RpcNudgeBack(Vector3 dir)
    {
        if (isLocalPlayer) GetComponent<PlayerController>().ApplyKnockback(dir);
    }

    // Bot dodge reaction (visual only) — plays a left/right slip when the human loses/mistimes the
    // trade, so their punch reads as "dodged". No damage, no movement (the home-pin holds position).
    [Server] public void PlayDodgeAnim() { RpcPlayDodge(Random.value < 0.5f); }
    [ClientRpc]
    private void RpcPlayDodge(bool left)
    {
        if (animator != null) animator.CrossFade(left ? "MoveLeft" : "MoveRight", 0.1f);
    }

    [Server] private IEnumerator DelayedKnockout(float delay) { yield return new WaitForSeconds(delay); RpcKnockout(); }

    /// Server-only: make this fighter play the knockout animation right now. Called on the round
    /// LOSER when the timer ends (points decide the winner; the loser drops as the final beat).
    [Server] public void PlayKnockoutNow() { RpcKnockout(); }
    [ClientRpc] void RpcTriggerHurt(string trigger, float delay, int damage, float power01) { StartCoroutine(DelayedHurtRoutine(trigger, delay, damage, power01)); }

    private IEnumerator DelayedHurtRoutine(string trigger, float delay, int damage, float power01)
    {
        yield return new WaitForSeconds(delay);

        // Reset stagger recovery bar when hit — prevents stale UI from lingering
        if (isLocalPlayer)
        {
            _staggerRecoveryCharge = 0f;
            _staggerTimingEscaped = false;
        }

        // CrossFade to the single hurt STATE (was SetTrigger on one of four). Using a state name keeps it
        // consistent with BotBeatApproach's got-hit recovery and doesn't depend on Animator trigger params.
        if (animator) { animator.applyRootMotion = false; animator.CrossFade(trigger, 0.05f); }

        // power01 (0..1) = how hard the attacker hit (their charged power). Blend it with damage so the
        // whole reaction — shake, flash, slow-mo — is bigger for a powerful, committed punch.
        float p = Mathf.Clamp01(power01);

        if (isLocalPlayer)
        {
            // Camera shake scaled by BOTH damage and the attacker's power.
            float baseDur = damage > 15 ? 0.4f : damage > 8 ? 0.3f : 0.2f;
            float baseMag = damage > 15 ? 0.7f : damage > 8 ? 0.45f : 0.28f;
            CameraShake.Instance?.Shake(baseDur * (0.8f + 0.6f * p), baseMag * (0.7f + 0.8f * p));

            _hurtFlashFade = Mathf.Max(_hurtFlashFade, Mathf.Clamp01(Mathf.Max(damage / 20f, p)));

            // HAPTIC: heavy rumble scaled by the bigger of damage / power.
            VRHaptics.GotHit(Mathf.Clamp01(Mathf.Max(damage / 20f, p)));
        }

        // ── SINGLE MODE ONLY: hurt slow-mo effect, DEPTH + LENGTH scaled by punch power ──
        var rmm = RhythmRoundManager.Instance;
        bool isSingleMode = rmm != null && rmm.IsSingleMoveMode();
        if (isSingleMode && animator != null)
        {
            // Weak hit → shallow, brief dip. Full-power hit → deep, longer slow-mo.
            float slowScale  = Mathf.Lerp(0.45f, 0.12f, p); // higher power = slower (lower timeScale)
            float slowLength = Mathf.Lerp(0.5f, 1.1f, p);   // higher power = longer slow-mo
            animator.speed = slowScale * 0.6f;
            Time.timeScale = slowScale;
            StartCoroutine(RestoreAnimatorSpeed(slowLength));
        }

        if (isLocalPlayer)
        {
            _pressureLevel = Mathf.Min(1f, _pressureLevel + PRESSURE_GAIN);
            _hurtFlashFade = 1f;
            _attackQueue.Clear();
            _pendingAttackTrigger = "";
            _pendingDashDirection = Vector3.zero;
            // Sticky move carries through being hit: re-arm it so the active move resumes after the
            // hurt/stagger instead of forcing the player to re-pick.
            RestickPendingMove();
            isAttacking = false;
            if (string.IsNullOrEmpty(_pendingAttackTrigger) && _pendingDashDirection == Vector3.zero)
        
            GetComponent<PlayerController>().InterruptMovement();
            StartCoroutine(HurtStunTimer());
        }
    }

    private IEnumerator RestoreAnimatorSpeed(float delay)
    {
        yield return new WaitForSecondsRealtime(delay);
        Time.timeScale = 1.0f;
        if (animator != null) animator.speed = 1f;
    }

    // Reset the bot's sped-up move animation back to normal speed after the swing has had time to play.
    private IEnumerator ResetBotAnimSpeed()
    {
        yield return new WaitForSecondsRealtime(0.6f);
        if (animator != null) animator.speed = 1f;
    }

    public bool HasOpenSlot(bool isMovement)
    {
        if (RhythmRoundManager.Instance != null && RhythmRoundManager.Instance.isRoundActive)
        {
            if (!RhythmRoundManager.Instance.IsSingleMoveMode()) return _comboBuffer.Count < RhythmRoundManager.Instance.currentComboCount;

            // A move that's only here because it was auto-armed from the sticky/active move still
            // counts as an OPEN slot — the player must always be able to pick a different card to
            // replace it. Only a move FRESHLY picked this beat locks the slot for that beat.
            if (_pendingFromSticky) return true;
            return isMovement ? _pendingDashDirection == Vector3.zero : string.IsNullOrEmpty(_pendingAttackTrigger);
        }
        return isMovement || _attackQueue.Count < 2;
    }

    private IEnumerator HurtStunTimer() { IsHurting = true; yield return new WaitForSeconds(0.2f); IsHurting = false; }
    private IEnumerator FlashEffectRoutine() { if (playerRenderer == null || flashMaterial == null) yield break; playerRenderer.material = flashMaterial; yield return new WaitForSeconds(0.1f); playerRenderer.material = _originalMaterial; }
    [ClientRpc] void RpcKnockout() { IsDead = true; if (animator != null) animator.SetTrigger("Knock out"); CommentaryManager.Instance?.Trigger(CommentaryEvent.Knockout, forceInterrupt: true); CameraShake.Instance?.Shake(0.45f, 0.9f); if (isLocalPlayer) VRHaptics.Knockout(); else VRHaptics.Victory(); }
    [Server] public IEnumerator ServerRestartMatchRoutine() { yield return new WaitForSeconds(4f); NetworkManager.singleton.ServerChangeScene(SceneManager.GetActiveScene().name); }

    private Texture2D _whiteTexture;

    private float _mustStrikeWarnUntil = 0f;

    [TargetRpc]
    public void TargetMustStrikeWarn(NetworkConnection target)
    {
        _mustStrikeWarnUntil = Time.time + 1.8f;
    }

    private void OnGUI()
    {
        if (VRCameraDriver.VRActive) return; // VR uses VRWorldHud for health/timing
        if (!isLocalPlayer) return;
        if (TiebreakerManager.Instance != null && TiebreakerManager.Instance.IsTiebreakerActive) return;
        if (RhythmRoundManager.Instance != null && RhythmRoundManager.Instance.isShopPhase) return;
        if (ShopPhaseManager.Instance != null && ShopPhaseManager.Instance.isShopPhase) return;

        // Taunt warning: you've been ordered to throw a Strike or take damage.
        if (Time.time < _mustStrikeWarnUntil)
        {
            GUIStyle warnSt = new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            warnSt.normal.textColor = new Color(1f, 0.25f, 0.15f);
            GUI.Label(new Rect(Screen.width / 2 - 300, Screen.height * 0.30f, 600, 50), "⚠ TAUNTED — STRIKE NOW OR TAKE DAMAGE!", warnSt);
        }

        // --- INITIALIZE TEXTURE ---
        if (_whiteTexture == null)
        {
            _whiteTexture = new Texture2D(1, 1);
            _whiteTexture.SetPixel(0, 0, Color.white);
            _whiteTexture.Apply();
        }

        // --- STAGGER RECOVERY PANEL (enhanced visual) ---
        var _staggerRmm = RhythmRoundManager.Instance;
        // Hide panel if charge was reset by a hit (even if still staggered)
        bool showStaggerPanel = IsStaggered && _staggerRmm != null && _staggerRmm.isRoundActive && _staggerRecoveryCharge > 0.001f;
        if (showStaggerPanel)
        {
            float sw = 420f, sh = 220f;
            float sx = Screen.width / 2 - sw / 2;   // centered
            float sy = Screen.height / 2 - 100f;     // centered-upper area

            // Dark gradient background panel
            GUI.color = new Color(0.15f, 0.05f, 0.05f, 0.95f);
            GUI.DrawTexture(new Rect(sx, sy, sw, sh), _whiteTexture);

            // Bright red border glow
            GUI.color = new Color(1f, 0.2f, 0.2f, 0.6f);
            GUI.DrawTexture(new Rect(sx - 2f, sy - 2f, sw + 4f, 4f), _whiteTexture);
            GUI.DrawTexture(new Rect(sx - 2f, sy + sh - 2f, sw + 4f, 4f), _whiteTexture);
            GUI.DrawTexture(new Rect(sx - 2f, sy, 4f, sh), _whiteTexture);
            GUI.DrawTexture(new Rect(sx + sw - 2f, sy, 4f, sh), _whiteTexture);

            GUI.color = Color.white;

            // Title with pulsing effect
            float titlePulse = (Mathf.Sin(Time.time * 3f) + 1f) * 0.5f;
            GUIStyle staggerTitle = new GUIStyle(GUI.skin.label)
                { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 36 };
            Color titleColor = Color.Lerp(Color.white, new Color(1f, 0.3f, 0.3f), titlePulse * 0.5f);
            staggerTitle.normal.textColor = titleColor;
            GUI.Label(new Rect(sx, sy + 12f, sw, 50f), "⚠ STAGGERED ⚠", staggerTitle);

            // Recovery bar container (larger)
            float bx = sx + 30f, bw = sw - 60f, bh = 50f, by = sy + 75f;

            // Dark bar background
            GUI.color = new Color(0.08f, 0.08f, 0.08f, 1f);
            GUI.DrawTexture(new Rect(bx - 3f, by - 3f, bw + 6f, bh + 6f), _whiteTexture);

            // Bar fill with gradient effect
            GUI.color = Color.Lerp(new Color(1f, 0.1f, 0.1f), new Color(0f, 1f, 0.3f), _staggerRecoveryCharge);
            GUI.DrawTexture(new Rect(bx, by, bw * _staggerRecoveryCharge, bh), _whiteTexture);

            // Bar border
            GUI.color = Color.Lerp(new Color(1f, 0.3f, 0.3f), new Color(0.3f, 1f, 0.5f), _staggerRecoveryCharge);
            GUI.DrawTexture(new Rect(bx, by, bw, 2f), _whiteTexture);
            GUI.DrawTexture(new Rect(bx, by + bh - 2f, bw, 2f), _whiteTexture);
            GUI.DrawTexture(new Rect(bx, by, 2f, bh), _whiteTexture);
            GUI.DrawTexture(new Rect(bx + bw - 2f, by, 2f, bh), _whiteTexture);

            // Bar label with percentage
            GUI.color = Color.white;
            GUIStyle barLbl = new GUIStyle(GUI.skin.label)
                { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 20 };
            barLbl.normal.textColor = Color.white;
            GUI.Label(new Rect(bx, by, bw, bh), $"SHOUT: {(_staggerRecoveryCharge * 100f):F0}%", barLbl);

            // Timing escape hint (larger)
            float nextBeat  = _staggerRmm.GetNextBeatTime();
            float trackTime = _staggerRmm.GetCurrentTrackTime();
            float timeToNext = nextBeat - trackTime;
            float staggerWindow = SHOUT_WINDOW * (1f - GetShoutWindowReduction(CurrentPercentage));
            bool  inWindow   = nextBeat > 0f && timeToNext >= 0f && timeToNext <= staggerWindow;
            float pulse      = (Mathf.Sin(Time.time * 12f) + 1f) * 0.5f;

            GUIStyle hintLbl = new GUIStyle(GUI.skin.label)
                { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 18 };

            if (inWindow)
            {
                hintLbl.normal.textColor = Color.Lerp(new Color(0.1f, 1f, 0.4f), Color.white, pulse);
                GUI.Label(new Rect(sx + 20f, sy + 145f, sw - 40f, 40f), "🔊 SHOUT NOW — ESCAPE! 🔊", hintLbl);
            }
            else
            {
                hintLbl.normal.textColor = new Color(1f, 1f, 0.5f, 0.9f);
                string hint = nextBeat > 0f ? $"⏱ Shout timing in: {timeToNext:F1}s" : "🎤 KEEP SHOUTING!";
                GUI.Label(new Rect(sx + 20f, sy + 145f, sw - 40f, 40f), hint, hintLbl);
            }

            // Volume indicator (new visual element)
            float rawVol = vp != null ? vp.CurrentRawVolume : 0f;
            float festVol = vp != null ? vp.CurrentFestivalVolume : 0f;
            GUIStyle volStyle = new GUIStyle(GUI.skin.label)
                { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 12 };
            volStyle.normal.textColor = festVol >= STAGGER_CHARGE_VOL_MIN ? new Color(0f, 1f, 0.5f) : new Color(1f, 0.6f, 0.2f);
            GUI.Label(new Rect(sx, sy + 185f, sw, 20f), $"MIC: {rawVol:F2} | FEST: {festVol:F2} (Min: {STAGGER_CHARGE_VOL_MIN:F2})", volStyle);

            GUI.color = Color.white;
        }

        // --- HURT FLASH PERMANENTLY REMOVED ---
        // The full-screen 65%-opacity red wash + red edge vignette on every hit — eye-blinding, strobes
        // constantly in fast sections. Removed for good; blood-on-hit is the hit feedback. (Fade var
        // still ticks down in case anything else reads it.)
        if (_hurtFlashFade > 0) _hurtFlashFade -= Time.deltaTime * 2.5f;

        // --- PARRY/BLOCK SUCCESS FLASH (cyan vignette) ---
        if (_successFlashFade > 0)
        {
            _successFlashFade -= Time.deltaTime * 5f;
            GUI.color = new Color(0f, 0.85f, 1f, Mathf.Clamp01(_successFlashFade) * 0.38f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _whiteTexture);
            GUI.color = Color.white;
        }

        PlayerController opponent = GetComponent<PlayerController>().GetOpponent();
        if (opponent != null)
        {
            PlayerCombat oppCombat = opponent.GetComponent<PlayerCombat>();
            if (oppCombat != null)
            {
                // OPPONENT score bar REMOVED — the big 3D "BOT" world-space score replaces it.
                #pragma warning disable 0162
                if (false)
                {
                float barWidth = 380f;
                float barHeight = 52f;
                float posX = Screen.width - barWidth - 24f;
                float posY = 16f;

                // Outer shadow/glow
                GUI.color = new Color(1f, 0.15f, 0.15f, 0.15f);
                GUI.DrawTexture(new Rect(posX - 4, posY - 4, barWidth + 8, barHeight + 8), _whiteTexture);

                // Background
                GUI.color = new Color(0.06f, 0.02f, 0.02f, 0.95f);
                GUI.DrawTexture(new Rect(posX, posY, barWidth, barHeight), _whiteTexture);

                // Border — red for enemy
                GUI.color = new Color(1f, 0.2f, 0.2f, 0.6f);
                GUI.DrawTexture(new Rect(posX, posY, barWidth, 2.5f), _whiteTexture);
                GUI.DrawTexture(new Rect(posX, posY + barHeight - 2.5f, barWidth, 2.5f), _whiteTexture);
                GUI.DrawTexture(new Rect(posX, posY, 2.5f, barHeight), _whiteTexture);
                GUI.DrawTexture(new Rect(posX + barWidth - 2.5f, posY, 2.5f, barHeight), _whiteTexture);

                float pctPercent = Mathf.Clamp01(oppCombat.CurrentPercentage / 100f);
                Color barColor = GetPercentageColor(oppCombat.CurrentPercentage);

                // Fill with gradient feel
                GUI.color = barColor;
                GUI.DrawTexture(new Rect(posX + 4, posY + 4, (barWidth - 8) * pctPercent, barHeight - 8), _whiteTexture);

                // Fill glow overlay
                GUI.color = new Color(barColor.r, barColor.g, barColor.b, 0.25f);
                GUI.DrawTexture(new Rect(posX + 4, posY + 4, (barWidth - 8) * pctPercent, (barHeight - 8) * 0.5f), _whiteTexture);

                // Label
                GUI.color = Color.white;
                GUIStyle nameStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleRight,
                    fontStyle = FontStyle.Bold,
                    fontSize = 20
                };

                string oppName = string.IsNullOrEmpty(opponent.PlayerName) ? "OPPONENT" : opponent.PlayerName;
                CyberpunkGUIUtils.DrawGlowText(new Rect(posX + 12f, posY, barWidth - 24f, barHeight),
                    $"{oppName}  |  {oppCombat.Score:N0} PTS", new Color(1f, 0.35f, 0.35f), nameStyle, new Color(1f, 0.15f, 0.15f));
                } // end if(false) opponent score bar
                #pragma warning restore 0162

                // --- OPPONENT CARDS PANEL (Top Left) --- (hidden for cleaner HUD)
#if false
                if (!string.IsNullOrEmpty(oppCombat.availableCardsString))
                {
                    var oppCards = new List<string>(oppCombat.availableCardsString.Split('|'));
                    float panelW = 220f;
                    float panelH = 36f + oppCards.Count * 34f;
                    float panelX = 20f;
                    float panelY = 170f;

                    GUI.color = new Color(0.06f, 0.06f, 0.1f, 0.92f);
                    GUI.DrawTexture(new Rect(panelX, panelY, panelW, panelH), _whiteTexture);

                    GUI.color = new Color(1f, 0f, 0.5f, 0.85f);
                    GUI.DrawTexture(new Rect(panelX, panelY, panelW, 3f), _whiteTexture);
                    GUI.DrawTexture(new Rect(panelX, panelY + panelH - 3f, panelW, 3f), _whiteTexture);
                    GUI.DrawTexture(new Rect(panelX, panelY, 3f, panelH), _whiteTexture);
                    GUI.DrawTexture(new Rect(panelX + panelW - 3f, panelY, 3f, panelH), _whiteTexture);

                    GUIStyle hdrStyle = CyberpunkGUIUtils.CreateCyberpunkStyle(TextAnchor.MiddleCenter, 14);
                    GUI.color = Color.white;
                    CyberpunkGUIUtils.DrawGlowText(new Rect(panelX, panelY + 6f, panelW, 26f), $"{oppName}'s CARDS",
                        CyberpunkGUIUtils.NEON_BLUE, hdrStyle, CyberpunkGUIUtils.NEON_MAGENTA);

                    GUIStyle cardStyle = CyberpunkGUIUtils.CreateCyberpunkStyle(TextAnchor.MiddleLeft, 13);
                    for (int i = 0; i < oppCards.Count; i++)
                    {
                        string card = oppCards[i];
                        string display = card switch
                        {
                            "Jab" => "  PUNCH",
                            "Cross" => "  FLANK",
                            "Hook" => "  HOOK",
                            "Block" => "  BLOCK",
                            "Left" => "  DODGE LEFT",
                            "Right" => "  DODGE RIGHT",
                            "UnbreakablePunch" => "  BOOM",
                            "ParryIntent" => "  CAGE",
                            "Grapple" => "  GRAPPLE",
                            "Fake" => "  FEINT",
                            "Clutch" => "  CLUTCH",
                            "Uppercut" => "  UPPERCUT",
                            "Sweep" => "  SWEEP",
                            "Focus" => "  FOCUS",
                            "Taunt" => "  TAUNT",
                            "Overclock" => "  OVERCLOCK",
                            "Reverse" => "  REVERSE",
                            "Trap" => "  TRAP",
                            "Cage" => "  CAGE",
                            "Mirror" => "  MIRROR",
                            _ => $"  {card.ToUpper()}"
                        };
                        bool isAtk = CardManager.IsAttackTrigger(card);
                        Color cardColor = isAtk ? new Color(1f, 0.4f, 0.4f) : CyberpunkGUIUtils.NEON_BLUE;
                        CyberpunkGUIUtils.DrawGlowText(new Rect(panelX + 8f, panelY + 34f + i * 30f, panelW - 16f, 28f), display, cardColor, cardStyle, cardColor);
                    }
                    GUI.color = Color.white;
                }
#endif
            }
        }

        // LOCAL PLAYER score bar REMOVED — the big 3D "YOU" world-space score replaces it.
        #pragma warning disable 0162
        if (false)
        {
            float barWidth  = 380f;
            float barHeight = 52f;
            float posX = 24f;
            float posY = Screen.height - barHeight - 20f;

            // Outer shadow/glow
            GUI.color = new Color(0.15f, 0.8f, 1f, 0.12f);
            GUI.DrawTexture(new Rect(posX - 4, posY - 4, barWidth + 8, barHeight + 8), _whiteTexture);

            // Background
            GUI.color = new Color(0.02f, 0.05f, 0.08f, 0.95f);
            GUI.DrawTexture(new Rect(posX, posY, barWidth, barHeight), _whiteTexture);

            // Border — cyan for player
            GUI.color = new Color(0.15f, 0.8f, 1f, 0.6f);
            GUI.DrawTexture(new Rect(posX, posY, barWidth, 2.5f), _whiteTexture);
            GUI.DrawTexture(new Rect(posX, posY + barHeight - 2.5f, barWidth, 2.5f), _whiteTexture);
            GUI.DrawTexture(new Rect(posX, posY, 2.5f, barHeight), _whiteTexture);
            GUI.DrawTexture(new Rect(posX + barWidth - 2.5f, posY, 2.5f, barHeight), _whiteTexture);

            float pctPercent = Mathf.Clamp01(CurrentPercentage / 100f);
            Color barColor = GetPercentageColor(CurrentPercentage);

            // Fill
            GUI.color = barColor;
            GUI.DrawTexture(new Rect(posX + 4, posY + 4, (barWidth - 8) * pctPercent, barHeight - 8), _whiteTexture);

            // Fill glow overlay
            GUI.color = new Color(barColor.r, barColor.g, barColor.b, 0.25f);
            GUI.DrawTexture(new Rect(posX + 4, posY + 4, (barWidth - 8) * pctPercent, (barHeight - 8) * 0.5f), _whiteTexture);

            // Label
            GUI.color = Color.white;
            GUIStyle myNameStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontStyle = FontStyle.Bold,
                fontSize  = 20
            };
            string myName = GetComponent<PlayerController>().PlayerName;
            if (string.IsNullOrEmpty(myName)) myName = "YOU";
            CyberpunkGUIUtils.DrawGlowText(new Rect(posX + 12f, posY, barWidth - 24f, barHeight),
                $"{Score:N0} PTS  |  {myName}", new Color(0.2f, 0.9f, 1f), myNameStyle, new Color(0.1f, 0.6f, 0.9f));
        } // end if(false) local player score bar
        #pragma warning restore 0162

        // --- 2. MIC THRESHOLD (Right Bottom Corner) ---
        if (vp != null)
        {
            float vol = vp.CurrentRawVolume;
            float thr = (_vcm != null) ? _vcm.parryVolumeThreshold : 0.4f;
            float w = 400f;
            float h = 100f;
            float mx = Screen.width - 420f;
            float my = Screen.height - h - 20f;

            // Background
            GUI.color = new Color(0.08f, 0.08f, 0.08f, 0.95f);
            GUI.DrawTexture(new Rect(mx, my, w, h), _whiteTexture);

            // Border
            GUI.color = vol >= thr ? new Color(0f, 1f, 0.5f, 0.7f) : new Color(1f, 0.6f, 0.2f, 0.7f);
            GUI.DrawTexture(new Rect(mx, my, w, 2f), _whiteTexture);
            GUI.DrawTexture(new Rect(mx, my + h - 2f, w, 2f), _whiteTexture);
            GUI.DrawTexture(new Rect(mx, my, 2f, h), _whiteTexture);
            GUI.DrawTexture(new Rect(mx + w - 2f, my, 2f, h), _whiteTexture);

            // Text
            GUI.color = Color.white;
            GUIStyle micStyle = CyberpunkGUIUtils.CreateCyberpunkStyle(TextAnchor.MiddleCenter, 14);
            CyberpunkGUIUtils.DrawGlowText(new Rect(mx, my + 8f, w, 25f), "MIC LEVEL", CyberpunkGUIUtils.NEON_CYAN, micStyle, CyberpunkGUIUtils.NEON_CYAN);

            GUIStyle volStyle = CyberpunkGUIUtils.CreateCyberpunkStyle(TextAnchor.MiddleCenter, 12);
            Color volColor = vol >= thr ? CyberpunkGUIUtils.NEON_GREEN : CyberpunkGUIUtils.NEON_ORANGE;
            CyberpunkGUIUtils.DrawGlowText(new Rect(mx, my + 35f, w, 20f), $"{vol:F2} / {thr:F2}", volColor, volStyle, volColor);

            // Bar
            float barW = w - 20f;
            float barX = mx + 10f;
            float barY = my + 60f;
            GUI.color = new Color(0.15f, 0.15f, 0.15f, 1f);
            GUI.DrawTexture(new Rect(barX, barY, barW, 8f), _whiteTexture);
            GUI.color = vol >= thr ? new Color(0f, 1f, 0.5f) : new Color(1f, 0.6f, 0.2f);
            GUI.DrawTexture(new Rect(barX, barY, Mathf.Min(barW, vol * barW), 8f), _whiteTexture);

            GUI.color = Color.white;
        }

        // --- 3. COMBAT QUEUE (Stacked Right Side) ---
        if (RhythmRoundManager.Instance != null && RhythmRoundManager.Instance.isRoundActive)
        {
            float w = 400f;
            float h = 160f;
            float qx = Screen.width - 420f;
            float qy = 364f;

            GUILayout.BeginArea(new Rect(qx, qy, w, h));
            GUIStyle headerStyle = CyberpunkGUIUtils.CreateCyberpunkStyle(TextAnchor.MiddleLeft, 20);
            GUI.color = Color.white;

            if (RhythmRoundManager.Instance.IsSingleMoveMode())
            {
                CyberpunkGUIUtils.DrawGlowText(new Rect(qx + 10, qy + 10, w - 20, 30), "LOCKED ACTION:",
                    CyberpunkGUIUtils.NEON_GREEN, headerStyle, CyberpunkGUIUtils.NEON_GREEN);
                string atk = string.IsNullOrEmpty(_pendingAttackTrigger) ? "None" : _pendingAttackTrigger;
                string atkDisplay = atk switch
                {
                    "ParryIntent" => "CAGE",
                    "UnbreakablePunch" => "BOOM",
                    "Jab" => "PUNCH",
                    "Cross" => "FLANK",
                    _ => atk
                };
                Color atkColor = (atk == "ParryIntent" || atk == "Mirror" || atk == "Trap" || atk == "Cage" || atk == "Reverse" || atk == "Clutch")
                    ? CyberpunkGUIUtils.NEON_CYAN : CyberpunkGUIUtils.NEON_ORANGE;
                GUIStyle atkStyle = CyberpunkGUIUtils.CreateCyberpunkStyle(TextAnchor.MiddleLeft, 18);
                CyberpunkGUIUtils.DrawGlowText(new Rect(qx + 10, qy + 45, w - 20, 30), $"Attack: {atkDisplay}", atkColor, atkStyle, atkColor);
            }
            GUILayout.EndArea();
        }
        
        // --- 4. CHAIN ATTACK INPUT LIST (Bottom Left) ---
        if (RhythmRoundManager.Instance != null && RhythmRoundManager.Instance.isRoundActive && !RhythmRoundManager.Instance.IsSingleMoveMode())
        {
            float w = 240f;
            float h = 280f;
            Rect chainRect = new Rect(20, Screen.height - h - 20f, w, h);

            GUI.Box(chainRect, "");
            GUIStyle chainHeaderStyle = CyberpunkGUIUtils.CreateCyberpunkStyle(TextAnchor.MiddleCenter, 14);
            CyberpunkGUIUtils.DrawGlowText(new Rect(chainRect.x, chainRect.y + 5, chainRect.width, 25), "NEXT COMBO CHAIN",
                CyberpunkGUIUtils.NEON_CYAN, chainHeaderStyle, CyberpunkGUIUtils.NEON_CYAN);

            GUILayout.BeginArea(new Rect(chainRect.x + 10, chainRect.y + 30, w - 20, h - 40));

            int totalNeeded = RhythmRoundManager.Instance.currentComboCount;

            for (int i = 0; i < totalNeeded; i++)
            {
                if (i < _comboBuffer.Count)
                {
                    var move = _comboBuffer[i];
                    string moveName = string.IsNullOrEmpty(move.attack) ? "DASH" : move.attack;

                    moveName = moveName switch
                    {
                        "ParryIntent" => "CAGE",
                        "UnbreakablePunch" => "BOOM",
                        "Jab" => "PUNCH",
                        "Cross" => "FLANK",
                        _ => moveName
                    };

                    GUI.color = CyberpunkGUIUtils.NEON_CYAN;
                    GUILayout.Box($"{i + 1}. {moveName.ToUpper()}", GUILayout.Height(40));
                }
                else
                {
                    GUI.color = new Color(1, 1, 1, 0.15f);
                    GUILayout.Box($"{i + 1}. [WAITING]", GUILayout.Height(40));
                }

                if (i < totalNeeded - 1) GUILayout.Label("      ▼", GUILayout.Height(10));
            }
            GUI.color = Color.white;
            GUILayout.EndArea();
        }

        // --- 5. ACTIVE TRAIT DISPLAY ---
        if (!string.IsNullOrEmpty(activeTraitId) && isLocalPlayer)
        {
            var trait = System.Array.Find(CardDatabase.TraitCards, t => t.traitId == activeTraitId);
            if (trait != null)
            {
                float tx = Screen.width - 180f;
                float ty = Screen.height - 80f;
                GUI.color = new Color(0.05f, 0.12f, 0.08f, 0.88f);
                if (_whiteTexture != null) GUI.DrawTexture(new Rect(tx - 4, ty - 4, 172, 44), _whiteTexture);
                GUI.color = new Color(0.25f, 1f, 0.5f, 0.9f);
                if (_whiteTexture != null) GUI.DrawTexture(new Rect(tx - 4, ty - 4, 3f, 44), _whiteTexture);
                GUI.color = Color.white;
                GUIStyle traitNameStyle = new GUIStyle(GUI.skin.label)
                { alignment = TextAnchor.UpperLeft, fontStyle = FontStyle.Bold, fontSize = 12 };
                traitNameStyle.normal.textColor = new Color(0.25f, 1f, 0.5f);
                GUI.Label(new Rect(tx + 4, ty, 160, 18), "TRAIT: " + trait.displayName.ToUpper(), traitNameStyle);
                GUIStyle traitEffectStyle = new GUIStyle(GUI.skin.label)
                { alignment = TextAnchor.UpperLeft, fontSize = 9, wordWrap = true };
                traitEffectStyle.normal.textColor = new Color(0.8f, 0.9f, 0.8f, 0.85f);
                GUI.Label(new Rect(tx + 4, ty + 18, 160, 22), trait.effect, traitEffectStyle);
            }
        }

        // --- 6. TIMING FEEDBACK FLOATER (Center Screen) ---
        if (_timingFade > 0)
        {
            GUIStyle timingStyle = new GUIStyle(GUI.skin.label) 
            { 
                alignment = TextAnchor.MiddleCenter, 
                fontStyle = FontStyle.Bold, 
                fontSize = 42 
            };
            
            float yOffset = Mathf.Lerp(60f, 0f, _timingFade); 
            
            _timingColor.a = _timingFade; 
            timingStyle.normal.textColor = _timingColor;
            
            GUI.Label(new Rect(Screen.width / 2 - 200, Screen.height / 2 - 150 - yOffset, 400, 100), _timingText, timingStyle);
            GUI.color = Color.white; 
        }
    }

    private string GetDirectionName(Vector3 dir)
    {
        if (dir == Vector3.forward) return "Forward"; if (dir == Vector3.back) return "Back";
        if (dir == Vector3.left || dir == new Vector3(-1, 0, 0)) return "Left";
        if (dir == Vector3.right || dir == new Vector3(1, 0, 0)) return "Right"; return dir.ToString();
    }

    public static Color GetPercentageColor(float percentage)
    {
        if (percentage >= 150f) return new Color(1f, 0f, 1f);     // magenta
        if (percentage >= 100f) return new Color(1f, 0.1f, 0.1f); // red
        if (percentage >= 50f)  return new Color(1f, 0.6f, 0f);   // orange
        return new Color(0f, 1f, 0.5f);                           // green
    }

    public static float GetShoutWindowReduction(float percentage)
    {
        int thresholds = Mathf.FloorToInt(percentage / 50f);
        return Mathf.Min(thresholds * 0.04f, 0.20f); // max 20% reduction
    }

    [TargetRpc]
    public void TargetPlaySuccessSound(string type)
    {
        if (SoundManagerMain.Instance != null)
            SoundManagerMain.Instance.PlaySuccessSFX(type);
        // VR haptic on successful block/parry sounds (TargetRpc = local player only).
        if (type == "Block") VRHaptics.BlockSuccess();
    }

    [ClientRpc]
    public void RpcPlayCombatParticle(string effectType)
    {
        if (ParticlePoolManager.Instance != null)
            ParticlePoolManager.Instance.PlayParticle(effectType, transform.position);
    }

    // Removes the last queued input so the player can replace it with something else
    public void CancelLastInput()
    {
        if (!isLocalPlayer) return;
        var rmm = RhythmRoundManager.Instance;
        if (rmm == null || !rmm.isRoundActive) return;
        if (rmm.IsSingleMoveMode())
        {
            // Explicit cancel also stops the move repeating (clears the sticky/active move).
            _pendingAttackTrigger = "";
            _pendingDashDirection = Vector3.zero;
            _stickyAttackTrigger = "";
            _stickyDashDirection = Vector3.zero;
            _pendingFromSticky = false;
        }
        else if (_comboBuffer.Count > 0)
        {
            _comboBuffer.RemoveAt(_comboBuffer.Count - 1);
        }

        CmdCancelLastInput();
    }

    [Command]
    private void CmdCancelLastInput()
    {
        var rmm = RhythmRoundManager.Instance;
        if (rmm == null || !rmm.isRoundActive) return;
        if (rmm.IsSingleMoveMode())
        {
            _pendingAttackTrigger = "";
            _pendingDashDirection = Vector3.zero;
            _stickyAttackTrigger = "";
            _stickyDashDirection = Vector3.zero;
        }
        else if (_comboBuffer.Count > 0)
        {
            _comboBuffer.RemoveAt(_comboBuffer.Count - 1);
        }

    }
}