using Mirror;
using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

public class PlayerCombat : NetworkBehaviour
{
    [SyncVar] public int CurrentHealth = 100;
    [SyncVar] public int MaxHealth = 100;
    [SyncVar] public int CurrentShield = 25;
    public int MaxShield = 25;
    public Text ShieldText;
    public Animator animator;
    public bool isAttacking = false;
    public bool IsDead { get; private set; }
    public bool IsHurting { get; private set; }

    [SyncVar] public bool IsParryActive  = false;
    [SyncVar] public bool IsStaggered    = false;
    [SyncVar] public int  StaggerBeatsRemaining = 0;

    private Queue<string> _attackQueue = new Queue<string>();
    public SphereCollider weaponGloveLeft;
    public SphereCollider weaponGloveRight;

    public struct RhythmAction { public string attack; public Vector3 dash; }
    public List<RhythmAction> _comboBuffer = new List<RhythmAction>();

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
        // Bots: 250 HP; real players: 400 HP
        if (GetComponent<BotController>() == null)
            CurrentHealth = 400;
        else
            CurrentHealth = 250;
        MaxHealth = CurrentHealth;
    }

    private void Start()
    {
        if (isLocalPlayer && ShieldText == null) ShieldText = GameObject.Find("ShieldText")?.GetComponent<Text>();
        _vcm = GetComponent<VoiceCommandManager>();
        _cardManager = GetComponent<CardManager>();
    }

    public void VoiceAttackJab() { if (isLocalPlayer && !IsDead) _attackQueue.Enqueue("Jab"); }
    public void VoiceAttackCross() { if (isLocalPlayer && !IsDead) _attackQueue.Enqueue("Cross"); }
    public void VoiceAttackHook() { if (isLocalPlayer && !IsDead) _attackQueue.Enqueue("Hook"); }
    public void VoiceAttackBlock() { if (isLocalPlayer && !IsDead) _attackQueue.Enqueue("Block"); }

    private void Update()
    {
        if (isLocalPlayer && ShieldText != null) ShieldText.text = CurrentShield.ToString();

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
        float effectiveWindow = SHOUT_WINDOW * (1f - _pressureLevel * 0.4f);
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
            Debug.Log($"<color=green>VOCAL SUCCESS:</color> Parry (CAGE) at {timeUntilImpact:F3}s until beat.");
            _pendingAttackTrigger = "ParryLocked";
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
            if (timeToNext >= 0f && timeToNext <= SHOUT_WINDOW && vol >= _vcm.parryVolumeThreshold)
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

        if (animator != null) animator.Play("Parry");

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
            animator.Play("Parry");
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

    private IEnumerator PerformAttack(string trigger)
    {
        isAttacking = true;
        if (isLocalPlayer && animator != null) animator.Play(trigger, 0, 0f);
        int damageToSet = (trigger == "Hook") ? 25 : (trigger == "Cross") ? 15 : (trigger == "Jab") ? 10 : 0;
        CmdTriggerAttack(trigger, damageToSet);
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
            // Map the internal logical triggers to the actual Animator state names
            string animToPlay = attack;

            if (attack == "ParryIntent")
            {
                animToPlay = "Parry";
            }
            else if (attack == "UnbreakablePunch")
            {
                // Mapping the "Boom" command logic to your "UpperCut" animation
                animToPlay = "Uppercut";
            }

            if (animator != null)
            {
                // Play the mapped animation state
                animator.Play(animToPlay, 0, 0f);
            }

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
    [Command] void CmdTriggerAttack(string t, int damage) { weaponGloveLeft.GetComponent<HitboxProperties>().currentDamage = damage; weaponGloveRight.GetComponent<HitboxProperties>().currentDamage = damage; RpcTriggerAttack(t); }
    [ClientRpc] void RpcTriggerAttack(string t) { if (isLocalPlayer) return; if (animator != null) animator.SetTrigger(t); }

    [Server]
    public void TakeDamage(int damage, Vector3 knockbackDir = default)
    {
        if (IsDead) return;
        StartCoroutine(FlashEffectRoutine());
        CurrentHealth -= damage;
        if (CurrentHealth <= 0) StartCoroutine(DelayedKnockout(0f));
        else
        {
            RpcTriggerHurt("Hurt " + Random.Range(1, 5), 0f, damage);
            if (knockbackDir != default) RpcNudgeBack(knockbackDir);
        }
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
        if (animator) animator.SetTrigger(trigger);

        float shakeDur = damage > 15 ? 0.35f : damage > 8 ? 0.20f : 0.10f;
        float shakeMag = damage > 15 ? 0.65f : damage > 8 ? 0.38f : 0.18f;
        CameraShake.Instance?.Shake(shakeDur, shakeMag);

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
    [ClientRpc] void RpcKnockout() { IsDead = true; if (animator != null) animator.SetTrigger("Knock out"); CommentaryManager.Instance?.Trigger(CommentaryEvent.Knockout, forceInterrupt: true); CameraShake.Instance?.Shake(0.45f, 0.9f); if (isServer) StartCoroutine(ServerRestartMatchRoutine()); }
    [Server] private IEnumerator ServerRestartMatchRoutine() { yield return new WaitForSeconds(4f); NetworkManager.singleton.ServerChangeScene(SceneManager.GetActiveScene().name); }

    private Texture2D _whiteTexture;

    private void OnGUI()
    {
        if (!isLocalPlayer) return;
        if (TiebreakerManager.Instance != null && TiebreakerManager.Instance.IsTiebreakerActive) return;

        // --- INITIALIZE TEXTURE ---
        if (_whiteTexture == null)
        {
            _whiteTexture = new Texture2D(1, 1);
            _whiteTexture.SetPixel(0, 0, Color.white);
            _whiteTexture.Apply();
        }

        // --- STAGGER RECOVERY PANEL (enhanced visual) ---
        var _staggerRmm = RhythmRoundManager.Instance;
        if (IsStaggered && _staggerRmm != null && _staggerRmm.isRoundActive)
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
            bool  inWindow   = nextBeat > 0f && timeToNext >= 0f && timeToNext <= SHOUT_WINDOW;
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
            _hurtFlashFade -= Time.deltaTime * 3.5f;
            GUI.color = new Color(0.9f, 0f, 0f, Mathf.Clamp01(_hurtFlashFade) * 0.45f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _whiteTexture);
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
                float posX = Screen.width - barWidth - 20f;
                float posY = 20f;

                GUI.color = new Color(0.1f, 0.1f, 0.1f, 1f);
                GUI.DrawTexture(new Rect(posX, posY, barWidth, barHeight), _whiteTexture);

                float healthPercent = oppCombat.MaxHealth > 0 ? (float)oppCombat.CurrentHealth / oppCombat.MaxHealth : 0f;
                GUI.color = Color.red;
                GUI.DrawTexture(new Rect(posX + 5, posY + 5, (barWidth - 10) * healthPercent, barHeight - 10), _whiteTexture);

                GUI.color = Color.white;
                GUIStyle nameStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold,
                    fontSize = 24 
                };

                string oppName = string.IsNullOrEmpty(opponent.PlayerName) ? "BOT UNIT" : opponent.PlayerName;
                GUI.Label(new Rect(posX, posY, barWidth, barHeight), $"{oppName}: {oppCombat.CurrentHealth} HP", nameStyle);
            }
        }

        // --- 2. MIC THRESHOLD (Middle Right, Above Cards) ---
        if (vp != null)
        {
            float vol = vp.CurrentRawVolume;
            float thr = (_vcm != null) ? _vcm.parryVolumeThreshold : 0.4f;
            float w = 240f;
            float h = 100f;
            float mx = Screen.width - w - 20f;
            float my = Screen.height / 2f - 150f;

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
            GUIStyle micStyle = new GUIStyle(GUI.skin.label)
            { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 14 };
            GUI.Label(new Rect(mx, my + 8f, w, 25f), "MIC LEVEL", micStyle);

            GUIStyle volStyle = new GUIStyle(GUI.skin.label)
            { alignment = TextAnchor.MiddleCenter, fontSize = 12 };
            volStyle.normal.textColor = vol >= thr ? new Color(0f, 1f, 0.5f) : new Color(1f, 0.6f, 0.2f);
            GUI.Label(new Rect(mx, my + 35f, w, 20f), $"{vol:F2} / {thr:F2}", volStyle);

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

        // --- 3. COMBAT QUEUE (Middle Right) ---
        if (RhythmRoundManager.Instance != null && RhythmRoundManager.Instance.isRoundActive)
        {
            float w = 280f;
            float h = 200f;
            float qx = Screen.width - w - 20f;
            float qy = 140f;

            GUILayout.BeginArea(new Rect(qx, qy, w, h));
            GUIStyle headerStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 20 };
            headerStyle.normal.textColor = Color.green;

            if (RhythmRoundManager.Instance.IsSingleMoveMode())
            {
                GUILayout.Label("LOCKED ACTION:", headerStyle);
                string atk = string.IsNullOrEmpty(_pendingAttackTrigger) ? "None" : _pendingAttackTrigger;
                if (atk == "ParryIntent") GUI.color = Color.cyan;
                GUILayout.Label($"Attack: {atk}", new GUIStyle(GUI.skin.label) { fontSize = 18 });
                GUI.color = Color.white;
            }
            GUILayout.EndArea();
        }
        
        // --- 4. CHAIN ATTACK INPUT LIST (Bottom Left) ---
        if (RhythmRoundManager.Instance != null && RhythmRoundManager.Instance.isRoundActive && !RhythmRoundManager.Instance.IsSingleMoveMode())
        {
            float w = 240f;
            float h = 280f;
            Rect chainRect = new Rect(20, Screen.height - h - 180f, w, h);

            GUI.Box(chainRect, "<b>NEXT COMBO CHAIN</b>");

            GUILayout.BeginArea(new Rect(chainRect.x + 10, chainRect.y + 30, w - 20, h - 40));

            int totalNeeded = RhythmRoundManager.Instance.currentComboCount;

            for (int i = 0; i < totalNeeded; i++)
            {
                if (i < _comboBuffer.Count)
                {
                    var move = _comboBuffer[i];
                    string moveName = string.IsNullOrEmpty(move.attack) ? "DASH" : move.attack;

                    if (moveName == "ParryIntent") moveName = "CAGE";
                    if (moveName == "UnbreakablePunch") moveName = "BOOM";

                    GUI.color = Color.cyan;
                    GUILayout.Box($"{i + 1}. {moveName.ToUpper()}", GUILayout.Height(40));
                }
                else
                {
                    GUI.color = new Color(1, 1, 1, 0.2f);
                    GUILayout.Box($"{i + 1}. [WAITING]", GUILayout.Height(40));
                }

                if (i < totalNeeded - 1) GUILayout.Label("      ▼", GUILayout.Height(10));
            }
            GUI.color = Color.white;
            GUILayout.EndArea();
        }

        // --- 5. TIMING FEEDBACK FLOATER (Center Screen) ---
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