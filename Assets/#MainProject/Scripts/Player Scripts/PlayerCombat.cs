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

    public override void OnStartServer()
    {
        // Bots keep 100 HP; real players get 250 HP
        if (GetComponent<BotController>() == null)
            CurrentHealth = 250;
        MaxHealth = CurrentHealth;
    }

    private void Start()
    {
        if (isLocalPlayer && ShieldText == null) ShieldText = GameObject.Find("ShieldText")?.GetComponent<Text>();
        _vcm = GetComponent<VoiceCommandManager>();
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

        // Local Parry Spike Check: Only runs for the local player
        if (isLocalPlayer && !IsDead && !IsHurting)
        {
            CheckLocalParryTiming();
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
            _spikeLockedThisBeat  = false;
            _lastTrackedBeatFire  = beatFireTime;
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
            // --- HUMAN PLAYER LOGIC ---
            // If you are a pure Client, update locally for the UI.
            // (If you are the Host, this skips so you don't double-count).
            if (!isServer) QueueLogic(attackTrigger, dashDir); 
            
            // Send the command to the Server.
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
        // This ensures the Server copy of the player also has the full buffer
        QueueLogic(attack, dash);

        if (RhythmRoundManager.Instance.IsWindUpActive)
        {
            // Only trigger wind-up for the first move in a chain
            if (_comboBuffer.Count == 1 || RhythmRoundManager.Instance.IsSingleMoveMode())
                TargetTriggerRhythmWindUp(attack, dash);
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

        // --- INITIALIZE TEXTURE ---
        if (_whiteTexture == null)
        {
            _whiteTexture = new Texture2D(1, 1);
            _whiteTexture.SetPixel(0, 0, Color.white);
            _whiteTexture.Apply();
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

        // --- 2. VOLUME DEBUGGER (Bottom Right) ---
        if (vp != null)
        {
            float vol = vp.CurrentRawVolume;
            float thr = (_vcm != null) ? _vcm.parryVolumeThreshold : 0.4f;
            float width = 260f; float height = 140f;
            float pX = Screen.width - width - 20f; float pY = Screen.height - height - 20f;

            GUI.Box(new Rect(pX, pY, width, height), "");
            GUILayout.BeginArea(new Rect(pX + 10f, pY + 10f, width - 20f, height - 20f));
            GUI.color = vol >= thr ? Color.green : Color.yellow;
            GUILayout.Label("<b>--- MIC MONITOR ---</b>");
            GUILayout.HorizontalSlider(vol, 0f, 1f, GUILayout.Width(200));
            GUILayout.Label($"VOL: {vol:F3} / THR: {thr:F3}");
            if (vol >= thr) GUILayout.Label("<color=green>!!! SPIKE DETECTED !!!</color>");
            GUILayout.EndArea();
            GUI.color = Color.white;
        }

        // --- 3. COMBAT QUEUE (Bottom Left) ---
        if (RhythmRoundManager.Instance != null && RhythmRoundManager.Instance.isRoundActive)
        {
            GUILayout.BeginArea(new Rect(20, Screen.height - 300, 350, 280));
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