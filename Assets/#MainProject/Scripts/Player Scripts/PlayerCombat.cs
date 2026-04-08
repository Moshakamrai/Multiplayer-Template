using Mirror;
using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

public class PlayerCombat : NetworkBehaviour
{
    [SyncVar] public int CurrentHealth = 100;
    [SyncVar] public int CurrentShield = 25;
    public int MaxShield = 25;
    public Text ShieldText;
    public Animator animator;
    public bool isAttacking = false;
    public bool IsDead { get; private set; }
    public bool IsHurting { get; private set; }

    [SyncVar] public bool IsParryActive = false;

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

    private VoiceCommandManager _vcm;

    [SyncVar] public float lastVocalSpikeTime = -1f; // Timestamp of the loudest peak

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
        // 1. Logic Gate: Only process if a move is actually queued
        string currentMove = _pendingAttackTrigger;
        bool isDashing = (_pendingDashDirection != Vector3.zero);
        if (string.IsNullOrEmpty(currentMove) && !isDashing) return;

        if (vp == null || _vcm == null || RhythmRoundManager.Instance == null) return;

        float currentVol = vp.CurrentRawVolume;
        float threshold = _vcm.parryVolumeThreshold;

        float nextBeat = RhythmRoundManager.Instance.GetNextBeatTime();
        float currentTime = RhythmRoundManager.Instance.GetCurrentTrackTime();
        float timeUntilImpact = nextBeat - currentTime;

        bool vocalSpike = currentVol >= threshold;

        // 2. Window Mapping
        float window = 0.3f;
        if (currentMove == "ParryIntent") window = 0.3f;
        else if (currentMove == "UnbreakablePunch") window = 0.2f;
        else if (currentMove == "Block") window = 0.4f;
        else if (isDashing) window = 0.3f;

        bool isInsideWindow = timeUntilImpact > 0 && timeUntilImpact <= window;

        // 3. Local Confirmation for Parry
        if (vocalSpike && isInsideWindow)
        {
            // If it's a Parry, we tell the server "This is verified" immediately
            if (currentMove == "ParryIntent")
            {
                // We send the currentTime so the server knows exactly when it happened
                CmdConfirmEliteParry(currentTime);
                Debug.Log($"<color=green>VOCAL SUCCESS:</color> Parry (CAGE) verified locally at {timeUntilImpact:F3}s.");

                // Change intent to 'Locked' so we don't spam the server
                _pendingAttackTrigger = "ParryLocked";
            }
            else
            {
                // For regular attacks (like BOOM), just sync the spike time for damage calculation
                CmdRegisterVocalSpike(currentTime);
                Debug.Log($"<color=green>VOCAL SUCCESS:</color> {currentMove} spike registered.");
            }
        }
    }

    [Command]
    void CmdConfirmEliteParry(float spikeTime)
    {
        lastVocalSpikeTime = spikeTime;
        IsParryActive = true; // Set instantly on server

        if (animator != null) animator.Play("Parry");

        TargetAddEnergy(1);
        StartCoroutine(ResetParryFlag());
    }

    [Command]
    void CmdRegisterVocalSpike(float time)
    {
        lastVocalSpikeTime = time;
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

        // We keep the hitbox active for 0.3s to match the window
        StartCoroutine(ResetParryFlag());
    }

    IEnumerator ResetParryFlag()
    {
        // Keep it active long enough for the server pulse to see it
        yield return new WaitForSeconds(0.6f);
        IsParryActive = false;
        lastVocalSpikeTime = -1f; // Clear the spike for the next round
    }

    private IEnumerator PerformAttack(string trigger)
    {
        isAttacking = true;
        if (isLocalPlayer && animator != null) animator.Play(trigger, 0, 0f);
        int damageToSet = (trigger == "Hook") ? 25 : (trigger == "Cross") ? 15 : (trigger == "Jab") ? 10 : 0;
        CmdTriggerAttack(trigger, damageToSet);
        yield return new WaitForSeconds(0.1f);
    }

    public void QueueRhythmMove(string attackTrigger, Vector3 dashDir)
    {
        if (IsDead || IsHurting) return;
        if (isServer || isLocalPlayer) QueueLogic(attackTrigger, dashDir);
        if (isLocalPlayer && !isServer) CmdQueueRhythmMove(attackTrigger, dashDir);
    }

    [Command]
    private void CmdQueueRhythmMove(string attack, Vector3 dash)
    {
        QueueLogic(attack, dash);
        if (RhythmRoundManager.Instance.IsWindUpActive)
        {
            bool isCurrentBeat = RhythmRoundManager.Instance.IsSingleMoveMode() || _comboBuffer.Count == 1;
            if (isCurrentBeat) TargetTriggerRhythmWindUp(attack, dash);
        }
    }

    private void QueueLogic(string attackTrigger, Vector3 dashDir)
    {
        if (RhythmRoundManager.Instance.IsSingleMoveMode())
        {
            if (!string.IsNullOrEmpty(attackTrigger)) { _pendingAttackTrigger = attackTrigger; _pendingDashDirection = Vector3.zero; }
            if (dashDir != Vector3.zero) { _pendingDashDirection = dashDir; _pendingAttackTrigger = ""; }
        }
        else if (_comboBuffer.Count < RhythmRoundManager.Instance.currentComboCount)
            _comboBuffer.Add(new RhythmAction { attack = attackTrigger, dash = dashDir });
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
    public void TakeDamage(int damage)
    {
        if (IsDead) return;
        StartCoroutine(FlashEffectRoutine());
        CurrentHealth -= damage;
        if (CurrentHealth <= 0) StartCoroutine(DelayedKnockout(0f));
        else RpcTriggerHurt("Hurt " + Random.Range(1, 5), 0f);
    }

    [Server] private IEnumerator DelayedKnockout(float delay) { yield return new WaitForSeconds(delay); RpcKnockout(); }
    [ClientRpc] void RpcTriggerHurt(string trigger, float delay) { StartCoroutine(DelayedHurtRoutine(trigger, delay)); }

    private IEnumerator DelayedHurtRoutine(string trigger, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (animator) animator.SetTrigger(trigger);
        if (isLocalPlayer) { _attackQueue.Clear(); _pendingAttackTrigger = ""; _pendingDashDirection = Vector3.zero; isAttacking = false; GetComponent<PlayerController>().InterruptMovement(); StartCoroutine(HurtStunTimer()); }
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
    [ClientRpc] void RpcKnockout() { IsDead = true; if (animator != null) animator.SetTrigger("Knock out"); if (isServer) StartCoroutine(ServerRestartMatchRoutine()); }
    [Server] private IEnumerator ServerRestartMatchRoutine() { yield return new WaitForSeconds(4f); NetworkManager.singleton.ServerChangeScene(SceneManager.GetActiveScene().name); }

    private void OnGUI()
    {
        if (!isLocalPlayer) return;

        // --- 1. BOT/OPPONENT HEALTH GUI (Top Middle) ---
        PlayerController opponent = GetComponent<PlayerController>().GetOpponent();
        if (opponent != null)
        {
            PlayerCombat oppCombat = opponent.GetComponent<PlayerCombat>();
            if (oppCombat != null)
            {
                // Centering the health bar at the top
                float barWidth = 400f;
                float barHeight = 40f;
                float posX = (Screen.width / 2) - (barWidth / 2);
                float posY = 20f;

                // Dark background for the bar
                GUI.Box(new Rect(posX, posY, barWidth, barHeight), "");

                // Red foreground for the health
                float healthPercent = (float)oppCombat.CurrentHealth / 100f;
                GUI.color = Color.red;
                GUI.Box(new Rect(posX + 5, posY + 5, (barWidth - 10) * healthPercent, barHeight - 10), "");

                // Text label for name and HP
                GUI.color = Color.white;
                GUIStyle nameStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 16 };
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
    }

    private string GetDirectionName(Vector3 dir)
    {
        if (dir == Vector3.forward) return "Forward"; if (dir == Vector3.back) return "Back";
        if (dir == Vector3.left || dir == new Vector3(-1, 0, 0)) return "Left";
        if (dir == Vector3.right || dir == new Vector3(1, 0, 0)) return "Right"; return dir.ToString();
    }

    // Add this inside the PlayerCombat class
    [TargetRpc]
    public void TargetPlaySuccessSound(string type)
    {
        // This looks for SoundManagerMain specifically since that is your new class name
        if (SoundManagerMain.Instance != null)
        {
            SoundManagerMain.Instance.PlaySuccessSFX(type);
        }
    }
}