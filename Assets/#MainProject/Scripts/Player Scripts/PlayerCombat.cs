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

    public VoiceProcessor vp ;

    private string _pendingAttackTrigger = "";
    private Vector3 _pendingDashDirection = Vector3.zero;

    private VoiceCommandManager _vcm;

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
        if (_pendingAttackTrigger != "ParryIntent") return;

        // Using your new public variable for the VoiceProcessor
        if (vp == null || _vcm == null || RhythmRoundManager.Instance == null) return;

        float currentVol = vp.CurrentRawVolume;
        float threshold = _vcm.parryVolumeThreshold;
        
        float nextBeat = RhythmRoundManager.Instance.GetNextBeatTime(); 
        float currentTime = RhythmRoundManager.Instance.GetCurrentTrackTime();
        float timeUntilImpact = nextBeat - currentTime;

        // THE TIGHT WINDOW: 0.3s before the beat.
        // This is where it gets tactical—no more early shouting!
        bool isInsideTightWindow = timeUntilImpact > 0 && timeUntilImpact <= 0.3f;
        bool vocalSpike = currentVol >= threshold;

        if (isInsideTightWindow && vocalSpike)
        {
            Debug.Log($"<color=green>ELITE PARRY!</color> Timed at {timeUntilImpact:F2}s.");
            CmdConfirmSuccessfulParry();
            _pendingAttackTrigger = "Parry"; 
        }
        else if (vocalSpike && timeUntilImpact > 0.3f)
        {
            // Optional: Feedback for being too early
            Debug.Log($"<color=red>TOO EARLY:</color> {timeUntilImpact:F2}s left. Wait for the 0.3s window!");
        }
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
        yield return new WaitForSeconds(0.4f); 
        IsParryActive = false;
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
            // Map the internal 'ParryIntent' logic to the actual 'Parry' animation state
            string animToPlay = (attack == "ParryIntent") ? "Parry" : attack;

            if (animator != null) animator.Play(animToPlay, 0, 0f);
            
            if (isLocalPlayer || (isServer && connectionToClient == null)) 
                StartCoroutine(PerformAttack(attack));
        }
        if (dash != Vector3.zero) GetComponent<PlayerController>().ApplyDashExternal(dash);
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
        // 1. SECURITY CHECK: Only show for your own player
        if (!isLocalPlayer) return;
        
        // --- NEW: PARRY MONITOR (Bottom Right) ---
        // This MUST show up if you have a VoiceProcessor component attached
        
        if (vp != null)
        {
            float vol = vp.CurrentRawVolume;
            float thr = (_vcm != null) ? _vcm.parryVolumeThreshold : 0.4f;
            
            // Fixed screen coordinates for the bottom right corner
            float width = 280f;
            float height = 150f;
            float posX = Screen.width - width - 20f;
            float posY = Screen.height - height - 20f;

            // DRAWING A SOLID NEON BORDER BOX (To ensure visibility on black)
            GUI.color = Color.magenta;
            GUI.Box(new Rect(posX - 2, posY - 2, width + 4, height + 4), ""); 
            GUI.color = Color.black;
            GUI.Box(new Rect(posX, posY, width, height), ""); // Solid background
            
            GUILayout.BeginArea(new Rect(posX + 15f, posY + 15f, width - 30f, height - 30f));
            
            // Switch color based on spike detection
            GUI.color = (vol >= thr) ? Color.green : Color.yellow;
            
            GUILayout.Label("<b>[ PARRY MIC MONITOR ]</b>");
            
            // Visual slider for real-time debugging
            GUILayout.HorizontalSlider(vol, 0f, 1f, GUILayout.Width(220));
            
            GUIStyle textStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold };
            GUILayout.Label($"MIC VOL: {vol:F3}", textStyle);
            GUILayout.Label($"TARGET : {thr:F3}", textStyle);

            if (vol >= thr) 
            {
                GUI.color = Color.green;
                GUILayout.Label("<b>>>> SPIKE DETECTED <<<</b>");
            }
            
            GUILayout.EndArea();
            GUI.color = Color.white; // Resetting global GUI color
        }

        // --- COMBAT QUEUE (Bottom Left) ---
        if (RhythmRoundManager.Instance == null || !RhythmRoundManager.Instance.isRoundActive) return;

        GUILayout.BeginArea(new Rect(20, Screen.height - 300, 350, 280));
        GUIStyle headerStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 20 };
        headerStyle.normal.textColor = Color.green;

        if (RhythmRoundManager.Instance.IsSingleMoveMode())
        {
            GUILayout.Label("LOCKED ACTION:", headerStyle);
            string atkText = string.IsNullOrEmpty(_pendingAttackTrigger) ? "None" : _pendingAttackTrigger;
            
            // This turns Cyan when you say "Parry"
            if (atkText == "ParryIntent") GUI.color = Color.cyan;
            GUILayout.Label($"Attack: {atkText}", new GUIStyle(GUI.skin.label) { fontSize = 18 });
            GUI.color = Color.white;
        }
        else
        {
            // Chain Command display logic
            int maxSlots = RhythmRoundManager.Instance.currentComboCount;
            GUILayout.Label($"CHAIN ({_comboBuffer.Count}/{maxSlots})", headerStyle);
            for (int i = 0; i < maxSlots; i++)
            {
                if (i < _comboBuffer.Count)
                {
                    string move = _comboBuffer[i].attack != "" ? _comboBuffer[i].attack : GetDirectionName(_comboBuffer[i].dash);
                    GUILayout.Label($"{i + 1}: {move}");
                }
                else GUILayout.Label($"{i + 1}: [ Empty ]");
            }
        }
        GUILayout.EndArea();
    }

    private string GetDirectionName(Vector3 dir)
    {
        if (dir == Vector3.forward) return "Forward"; if (dir == Vector3.back) return "Back";
        if (dir == Vector3.left || dir == new Vector3(-1, 0, 0)) return "Left";
        if (dir == Vector3.right || dir == new Vector3(1, 0, 0)) return "Right"; return dir.ToString();
    }
}