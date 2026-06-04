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

    // --- TIMING FEEDBACK VARIABLES ---
    private string _timingText = "";
    private Color _timingColor = Color.white;
    private float _timingFade = 0f;

    private VoiceCommandManager _vcm;

    [SyncVar] public float lastVocalSpikeTime   = -1f;
    [SyncVar] public float lastVocalSpikeVolume = 0f;

    // Spike detection guards — prevent holding voice from gaming timing
    private bool  _spikeLockedThisBeat   = false;
    private float _lastTrackedBeatFire   = -1f;
    private const float BEAT_DEAD_ZONE   = 0.2f;
    private const float SHOUT_WINDOW     = 0.5f;

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
        roundDamageDealt = 0;
        roundExcellentCount = 0;
        roundCounterCount = 0;
        IsDead = false;
        IsStaggered = false;
        StaggerBeatsRemaining = 0;
        IsParryActive = false;
        lastVocalSpikeTime = -1f;
        lastVocalSpikeVolume = 0f;
        _pendingAttackTrigger = "";
        _pendingDashDirection = Vector3.zero;
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
    public GameObject slashProjectilePrefab;     // a slash VFX; add SlashProjectile.cs to it to make it fly
    public Transform  slashSpawnPoint;           // optional; defaults to chest-height, slightly forward
    [Tooltip("Delay before the slash spawns — raise this so it fires LATER in the swing (when the blade actually cuts), not at the start of the anim.")]
    public float slashSpawnDelay = 0.25f;
    [Tooltip("ON = the slash is fired by an Animation Event (SlashAnimationEvent.Slash) at the exact frame, not by the timed code path. Turn this on once you've added events to your swing clips.")]
    public bool slashViaAnimationEvent = false;
    [Tooltip("One-time correction for however your slash art is oriented (applied on top of the auto blade angle).")]
    public Vector3 slashRotationOffset = Vector3.zero;

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
        Vector3 fwd = transform.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
        fwd.Normalize();

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

        GameObject go;
        if (slashProjectilePrefab != null)
        {
            go = Instantiate(slashProjectilePrefab, origin, rot);
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

        if (go.GetComponent<SlashProjectile>() == null)
        {
            var sp = go.AddComponent<SlashProjectile>();
            sp.speed = 7f;
            sp.lifetime = 1.0f;
        }
    }

    private void Start()
    {
        _vcm = GetComponent<VoiceCommandManager>();
        _cardManager = GetComponent<CardManager>();
        _gloveGlow = GetComponent<GloveBeatGlow>();
        _swordArc  = GetComponent<SwordArcTrail>();
    }

    // ── Glove juice hooks (server triggers, all clients flash) ──────────────
    [Server] public void GloveStrikeFlash() => RpcGloveStrike();
    [ClientRpc] private void RpcGloveStrike() { if (_gloveGlow != null) _gloveGlow.FlashStrike(); }
    [ClientRpc] private void RpcGloveHurt()   { if (_gloveGlow != null) _gloveGlow.FlashHurt(); }

    // ── Sword impact VFX (server triggers a hit-point burst on all clients) ──
    [Server] public void SpawnSwordImpact(Vector3 pos) { if (swordImpactVfx != null) RpcSwordImpact(pos); }
    [ClientRpc] private void RpcSwordImpact(Vector3 pos)
    {
        if (swordImpactVfx == null) return;
        var fx = Instantiate(swordImpactVfx, pos, Quaternion.identity);
        Destroy(fx, 2f);
    }

    public void VoiceAttackJab() { if (isLocalPlayer && !IsDead) _attackQueue.Enqueue("Jab"); }
    public void VoiceAttackCross() { if (isLocalPlayer && !IsDead) _attackQueue.Enqueue("Cross"); }
    public void VoiceAttackHook() { if (isLocalPlayer && !IsDead) _attackQueue.Enqueue("Hook"); }
    public void VoiceAttackBlock() { if (isLocalPlayer && !IsDead) _attackQueue.Enqueue("Block"); }

    private void Update()
    {
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

        }

        if (!isLocalPlayer || IsDead || IsHurting) return;
        if (!isAttacking && _attackQueue.Count > 0) StartCoroutine(PerformAttack(_attackQueue.Dequeue()));
    }

    // --- RESTORED ANIMATION EVENT FUNCTIONS ---
    public void StartAttackWindow() { isAttacking = true; }
    public void EndAttackWindow() { isAttacking = false; }

    private void CheckLocalParryTiming()
    {
        if (vp == null || _vcm == null || RhythmRoundManager.Instance == null) return;

        var   rmm          = RhythmRoundManager.Instance;
        float currentTime  = rmm.GetCurrentTrackTime();
        float beatFireTime = rmm.lastBeatFireTime;

        // ── Dead zone: silence spike detection for BEAT_DEAD_ZONE seconds after each beat ──
        // This prevents a shout from the current action bleeding into the next timing window.
        if (beatFireTime > 0f && currentTime - beatFireTime < BEAT_DEAD_ZONE) return;

        // ── Reset first-spike gate when a new beat cycle begins ───────────────────────────
        if (beatFireTime != _lastTrackedBeatFire)
        {
            _spikeLockedThisBeat   = false;
            _staggerTimingEscaped  = false;
            _lastTrackedBeatFire   = beatFireTime;
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
        // Plus additional reduction based on damage percentage (every 50% = -4% window, max -20%)
        float percentageReduction = GetShoutWindowReduction(CurrentPercentage);
        float effectiveWindow = SHOUT_WINDOW * (1f - _pressureLevel * 0.4f - percentageReduction);

        // Trait/Vex card timing window modifiers
        float timingWindowMult = 1f;
        // Quicktrigger trait: +0.1s timing window (easier for this player)
        if (!string.IsNullOrEmpty(activeTraitId) && activeTraitId == "quicktrigger")
            timingWindowMult *= 1.20f; // +0.1s / 0.5s = 20% increase

        // Heavy trait: +0.05s (easier for this player)
        if (!string.IsNullOrEmpty(activeTraitId) && activeTraitId == "heavy")
            timingWindowMult *= 1.10f; // 0.05s / 0.5s = 10% increase

        // Quicktrigger trait: +0.1s timing window
        if (activeTraitId == "quicktrigger")
            timingWindowMult *= 1.20f;

        // Check opponent's Stunning trait (makes our window harder)
        PlayerCombat opponent = GetComponent<PlayerController>()?.GetOpponent()?.GetComponent<PlayerCombat>();
        if (opponent != null && !string.IsNullOrEmpty(opponent.activeTraitId) && opponent.activeTraitId == "stunning")
            timingWindowMult *= 0.90f; // 0.05s reduction = ~10% tighter

        effectiveWindow *= timingWindowMult;
        bool inShoutWindow = timeUntilImpact > 0f && timeUntilImpact <= effectiveWindow;
        if (!inShoutWindow) return;

        // ── First spike only: once locked, ignore further volume until next beat cycle ─────
        if (_spikeLockedThisBeat) return;

        float currentVol = vp.CurrentRawVolume;
        float threshold  = _vcm.parryVolumeThreshold;
        if (currentVol < threshold) return;

        // ── Register the timing spike ─────────────────────────────────────────────────────
        _spikeLockedThisBeat = true;

        if (!isChainMode && currentMove == "ParryIntent")
        {
            CmdConfirmEliteParry(currentTime, currentVol);
            Debug.Log($"<color=green>VOCAL SUCCESS:</color> Elite Parry at {timeUntilImpact:F3}s until beat.");
            // Keep the move as "ParryIntent" so the family reflect logic resolves it.
            // (The old "ParryLocked" rename made it read as Support → interrupted by Strikes.)
            _pendingAttackTrigger = "ParryIntent";
        }
        else
        {
            CmdRegisterVocalSpike(currentTime, currentVol);
            string label = isChainMode ? "CHAIN" : currentMove;
            Debug.Log($"<color=cyan>SPIKE [{label}]:</color> t={currentTime:F3}s  Δbeat={timeUntilImpact:F3}s");
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

        float vol       = vp.CurrentRawVolume;
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
    void CmdRegisterVocalSpike(float time, float vol)
    {
        lastVocalSpikeTime   = time;
        lastVocalSpikeVolume = vol;
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
    }

    [Server]
    public void ClearStagger() { IsStaggered = false; StaggerBeatsRemaining = 0; }

    IEnumerator ResetParryFlag()
    {
        yield return new WaitForSeconds(0.6f);
        IsParryActive        = false;
        lastVocalSpikeTime   = -1f;
        lastVocalSpikeVolume = 0f;
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

    private IEnumerator PerformAttack(string trigger)
    {
        isAttacking = true;
        if (isLocalPlayer && animator != null) animator.Play(AnimName(trigger), 0, 0f);
        // Your own blade arc + slash VFX (local view) on offensive swings.
        if (isLocalPlayer && CardManager.IsAttackTrigger(trigger))
        {
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

    [TargetRpc]
    public void TargetShowTimingFeedback(string rating)
    {
        _timingText = rating;
        _timingFade = 1.0f;

        if (rating == "EXCELLENT") { _timingColor = Color.cyan;  CommentaryManager.Instance?.Trigger(CommentaryEvent.Excellent); }
        else if (rating == "GOOD") { _timingColor = Color.green; CommentaryManager.Instance?.Trigger(CommentaryEvent.Good); }
        else                       { _timingColor = Color.red;   CommentaryManager.Instance?.Trigger(CommentaryEvent.BadTiming); }
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

    [Server]
    public void ConsumeNextMove()
    {
        if (RhythmRoundManager.Instance.IsSingleMoveMode()) { _pendingAttackTrigger = ""; _pendingDashDirection = Vector3.zero; }
        else if (_comboBuffer.Count > 0) _comboBuffer.RemoveAt(0);
    }

    [Server]
    public void ExecuteRhythmWindUp()
    {
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
        if (!string.IsNullOrEmpty(attack))
        {
            // Map the logical trigger to its Animator state name (see AnimName).
            if (animator != null)
                animator.Play(AnimName(attack), 0, 0f);

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
        RpcTriggerAttack(t);
    }
    [ClientRpc] void RpcTriggerAttack(string t)
    {
        if (isLocalPlayer) return;
        if (animator != null) animator.SetTrigger(AnimName(t));
        // Opponent/bot blade arc + slash VFX (this is the copy the human watches).
        if (CardManager.IsAttackTrigger(t))
        {
            if (_swordArc != null) _swordArc.StartSwing();
            if (!slashViaAnimationEvent) ThrowSlash(t);
        }
        TriggerDefenseVfx(t); // block/parry VFX (0.2s into the anim)
    }

    [Server]
    public void TakeDamage(int damage, Vector3 knockbackDir = default, bool isOpponentDamage = false)
    {
        StartCoroutine(FlashEffectRoutine());
        float oldPct = CurrentPercentage;
        CurrentPercentage += damage;
        // Stagger at every 50% damage threshold crossed (50, 100, 150, ...)
        if (isServer && Mathf.FloorToInt(CurrentPercentage / 50f) > Mathf.FloorToInt(oldPct / 50f))
            TriggerStagger(2);
        CancelDefenseVfx(); // kill any active block/parry VFX the moment a hit lands
        RpcTriggerHurt("Hurt " + Random.Range(1, 5), 0f, damage);
        RpcShowDamageNumber(damage, isOpponentDamage);
        RpcGloveHurt();
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

    [Server] private IEnumerator DelayedKnockout(float delay) { yield return new WaitForSeconds(delay); RpcKnockout(); }
    [ClientRpc] void RpcTriggerHurt(string trigger, float delay, int damage) { StartCoroutine(DelayedHurtRoutine(trigger, delay, damage)); }

    private IEnumerator DelayedHurtRoutine(string trigger, float delay, int damage)
    {
        yield return new WaitForSeconds(delay);

        // Reset stagger recovery bar when hit — prevents stale UI from lingering
        if (isLocalPlayer)
        {
            _staggerRecoveryCharge = 0f;
            _staggerTimingEscaped = false;
        }

        if (animator) animator.SetTrigger(trigger);

        if (isLocalPlayer)
        {
            // Camera shake on hit with damage scaling
            float shakeDur = damage > 15 ? 0.4f : damage > 8 ? 0.3f : 0.2f;
            float shakeMag = damage > 15 ? 1.5f : damage > 8 ? 1.0f : 0.6f;
            CameraShake.Instance?.Shake(shakeDur, shakeMag);

            _hurtFlashFade = Mathf.Max(_hurtFlashFade, Mathf.Min(1f, damage / 20f));
        }

        // ── SINGLE MODE ONLY: hurt slow-mo effect ──
        var rmm = RhythmRoundManager.Instance;
        bool isSingleMode = rmm != null && rmm.IsSingleMoveMode();
        if (isSingleMode && animator != null)
        {
            animator.speed = 0.15f;
            Time.timeScale = 0.25f;
            StartCoroutine(RestoreAnimatorSpeed(0.9f));
        }

        if (isLocalPlayer)
        {
            _pressureLevel = Mathf.Min(1f, _pressureLevel + PRESSURE_GAIN);
            _hurtFlashFade = 1f;
            _attackQueue.Clear();
            _pendingAttackTrigger = "";
            _pendingDashDirection = Vector3.zero;
            isAttacking = false;
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

    public bool HasOpenSlot(bool isMovement)
    {
        if (RhythmRoundManager.Instance != null && RhythmRoundManager.Instance.isRoundActive)
        {
            if (!RhythmRoundManager.Instance.IsSingleMoveMode()) return _comboBuffer.Count < RhythmRoundManager.Instance.currentComboCount;
            return isMovement ? _pendingDashDirection == Vector3.zero : string.IsNullOrEmpty(_pendingAttackTrigger);
        }
        return isMovement || _attackQueue.Count < 2;
    }

    private IEnumerator HurtStunTimer() { IsHurting = true; yield return new WaitForSeconds(0.2f); IsHurting = false; }
    private IEnumerator FlashEffectRoutine() { if (playerRenderer == null || flashMaterial == null) yield break; playerRenderer.material = flashMaterial; yield return new WaitForSeconds(0.1f); playerRenderer.material = _originalMaterial; }
    [ClientRpc] void RpcKnockout() { IsDead = true; if (animator != null) animator.SetTrigger("Knock out"); CommentaryManager.Instance?.Trigger(CommentaryEvent.Knockout, forceInterrupt: true); CameraShake.Instance?.Shake(0.45f, 0.9f); }
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
            float vol = vp != null ? vp.CurrentRawVolume : 0f;
            GUIStyle volStyle = new GUIStyle(GUI.skin.label)
                { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 14 };
            volStyle.normal.textColor = vol >= STAGGER_CHARGE_VOL_MIN ? new Color(0f, 1f, 0.5f) : new Color(1f, 0.6f, 0.2f);
            GUI.Label(new Rect(sx, sy + 190f, sw, 25f), $"MIC: {vol:F2} (Min: {STAGGER_CHARGE_VOL_MIN:F2})", volStyle);

            GUI.color = Color.white;
        }

        // --- HURT FLASH (red vignette) ---
        if (_hurtFlashFade > 0)
        {
            _hurtFlashFade -= Time.deltaTime * 2.5f;
            float flashAlpha = Mathf.Clamp01(_hurtFlashFade) * 0.65f;
            GUI.color = new Color(1f, 0.1f, 0.1f, flashAlpha);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _whiteTexture);
            GUI.color = Color.white;

            float vignetteAlpha = Mathf.Clamp01(_hurtFlashFade) * 0.4f;
            float vignetteSize = Mathf.Lerp(100f, 300f, Mathf.Clamp01(_hurtFlashFade));
            GUI.color = new Color(0.8f, 0f, 0f, vignetteAlpha);
            for (int i = 0; i < 4; i++)
            {
                float offset = vignetteSize * (1f - Mathf.Clamp01(_hurtFlashFade));
                if (i == 0) GUI.DrawTexture(new Rect(-offset, -offset, Screen.width + offset * 2, vignetteSize), _whiteTexture);
                else if (i == 1) GUI.DrawTexture(new Rect(-offset, Screen.height - vignetteSize + offset, Screen.width + offset * 2, vignetteSize), _whiteTexture);
                else if (i == 2) GUI.DrawTexture(new Rect(-offset, 0, vignetteSize, Screen.height), _whiteTexture);
                else GUI.DrawTexture(new Rect(Screen.width - vignetteSize + offset, 0, vignetteSize, Screen.height), _whiteTexture);
            }
            GUI.color = Color.white;
        }

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
                float barWidth = 400f;
                float barHeight = 60f;
                float posX = Screen.width / 2f - barWidth / 2f;
                float posY = 20f;

                GUI.color = new Color(0.1f, 0.1f, 0.1f, 1f);
                GUI.DrawTexture(new Rect(posX, posY, barWidth, barHeight), _whiteTexture);

                float pctPercent = Mathf.Clamp01(oppCombat.CurrentPercentage / 100f);
                Color barColor = GetPercentageColor(oppCombat.CurrentPercentage);
                GUI.color = barColor;
                GUI.DrawTexture(new Rect(posX + 5, posY + 5, (barWidth - 10) * pctPercent, barHeight - 10), _whiteTexture);

                GUI.color = Color.white;
                GUIStyle nameStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold,
                    fontSize = 24 
                };

                string oppName = string.IsNullOrEmpty(opponent.PlayerName) ? "BOT UNIT" : opponent.PlayerName;
                CyberpunkGUIUtils.DrawGlowText(new Rect(posX, posY, barWidth, barHeight), $"{oppName}: {oppCombat.CurrentPercentage:F0}%",
                    CyberpunkGUIUtils.NEON_CYAN, nameStyle, CyberpunkGUIUtils.NEON_CYAN);

                // --- OPPONENT CARDS PANEL (Top Left) ---
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
            }
        }

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
            _pendingAttackTrigger = "";
            _pendingDashDirection = Vector3.zero;
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
        }
        else if (_comboBuffer.Count > 0)
        {
            _comboBuffer.RemoveAt(_comboBuffer.Count - 1);
        }
    }
}