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
    
    private Queue<string> _attackQueue = new Queue<string>();
    public SphereCollider weaponGloveLeft;
    public SphereCollider weaponGloveRight;

    public struct RhythmAction { public string attack; public Vector3 dash; }
    public List<RhythmAction> _comboBuffer = new List<RhythmAction>();

    [Header("VFX Settings")]
    public Renderer playerRenderer;
    public Material flashMaterial;
    public Material _originalMaterial;

    private string _pendingAttackTrigger = "";
    private Vector3 _pendingDashDirection = Vector3.zero;

    private void Start()
    {
        if (isLocalPlayer && ShieldText == null) ShieldText = GameObject.Find("ShieldText")?.GetComponent<Text>();
    }

    public void VoiceAttackJab() { if (isLocalPlayer && !IsDead) _attackQueue.Enqueue("Jab"); }
    public void VoiceAttackCross() { if (isLocalPlayer && !IsDead) _attackQueue.Enqueue("Cross"); }
    public void VoiceAttackHook() { if (isLocalPlayer && !IsDead) _attackQueue.Enqueue("Hook"); }
    public void VoiceAttackBlock() { if (isLocalPlayer && !IsDead) _attackQueue.Enqueue("Block"); }

    private void Update()
    {
        if (isLocalPlayer && ShieldText != null) ShieldText.text = CurrentShield.ToString();
        if (!isLocalPlayer || IsDead || IsHurting) return;

        if (!isAttacking && _attackQueue.Count > 0) StartCoroutine(PerformAttack(_attackQueue.Dequeue()));
    }

    private IEnumerator PerformAttack(string trigger)
    {
        isAttacking = true;
        
        if (isLocalPlayer && animator != null) 
        {
            // Instead of SetTrigger, this forces the animator to instantly restart the punch!
            animator.Play(trigger, 0, 0f); 
        }

        int damageToSet = (trigger == "Hook") ? 25 : (trigger == "Cross") ? 15 : (trigger == "Jab") ? 10 : 0; 
        CmdTriggerAttack(trigger, damageToSet);

        // We remove the WaitUntil dead-lock so rapid-fire beats don't get stuck
        yield return new WaitForSeconds(0.1f);
    }

    public void QueueRhythmMove(string attackTrigger, Vector3 dashDir)
    {
        if (IsDead || IsHurting) return;
        if (!isServer) QueueLogic(attackTrigger, dashDir);
        if (isLocalPlayer) CmdQueueRhythmMove(attackTrigger, dashDir);
    }

    [Command] 
    private void CmdQueueRhythmMove(string attack, Vector3 dash) 
    { 
        QueueLogic(attack, dash); 
        
        // --- THE LATECOMER FIX ---
        // If Vosk took too long and the Wind-Up train already left the station, 
        // instantly force the animation to play so we don't miss the beat!
        if (RhythmRoundManager.Instance.IsWindUpActive)
        {
            bool isCurrentBeat = RhythmRoundManager.Instance.IsSingleMoveMode() || _comboBuffer.Count == 1;
            if (isCurrentBeat) 
            {
                TargetTriggerRhythmWindUp(attack, dash);
            }
        }
    }

    private void QueueLogic(string attackTrigger, Vector3 dashDir)
    {
        if (RhythmRoundManager.Instance.IsSingleMoveMode())
        {
            if (!string.IsNullOrEmpty(attackTrigger)) { _pendingAttackTrigger = attackTrigger; _pendingDashDirection = Vector3.zero; }
            if (dashDir != Vector3.zero) { _pendingDashDirection = dashDir; _pendingAttackTrigger = ""; }
        }
        else
        {
            if (_comboBuffer.Count < RhythmRoundManager.Instance.currentComboCount) 
                _comboBuffer.Add(new RhythmAction { attack = attackTrigger, dash = dashDir });
        }
    }

    // --- NEW: THE PEEK AND CONSUME SYSTEM ---
    public RhythmAction PeekNextMove()
    {
        if (RhythmRoundManager.Instance.IsSingleMoveMode())
            return new RhythmAction { attack = _pendingAttackTrigger, dash = _pendingDashDirection };

        return (_comboBuffer.Count > 0) ? _comboBuffer[0] : new RhythmAction { attack = "", dash = Vector3.zero };
    }

    [Server]
    public void ConsumeNextMove()
    {
        if (RhythmRoundManager.Instance.IsSingleMoveMode())
        {
            _pendingAttackTrigger = "";
            _pendingDashDirection = Vector3.zero;
        }
        else if (_comboBuffer.Count > 0)
        {
            _comboBuffer.RemoveAt(0);
        }
    }

    // --- PHASE 1: THE WIND UP (Fired exactly 0.5s early) ---
    [Server]
    public void ExecuteRhythmWindUp()
    {
        var move = PeekNextMove();
        TargetTriggerRhythmWindUp(move.attack, move.dash);
    }

    [TargetRpc]
    public void TargetTriggerRhythmWindUp(string attack, Vector3 dash)
    {
        if (!string.IsNullOrEmpty(attack)) StartCoroutine(PerformAttack(attack));
        if (dash != Vector3.zero) GetComponent<PlayerController>().CmdRhythmDash(dash);
    }

    [TargetRpc] public void TargetAddEnergy(int amount) { if (isLocalPlayer) GetComponent<PlayerEnergy>().AddBonusEnergy(amount); }

    public void StartAttackWindow() { isAttacking = true; }
    public void EndAttackWindow() { isAttacking = false; }

    [Command] void CmdTriggerAttack(string t, int damage) { weaponGloveLeft.GetComponent<HitboxProperties>().currentDamage = damage; weaponGloveRight.GetComponent<HitboxProperties>().currentDamage = damage; RpcTriggerAttack(t); }

    [ClientRpc] void RpcTriggerAttack(string t) { if (isLocalPlayer) return; if (animator != null) animator.SetTrigger(t); }

    [Server]
    public void TakeDamage(int damage)
    {
        if (IsDead) return;
        StartCoroutine(FlashEffectRoutine());
        CurrentHealth -= damage;
        
        // FIXED: Delay is now 0! The hit happens instantly on the beat!
        float impactDelay = 0.0f; 

        if (CurrentHealth <= 0) StartCoroutine(DelayedKnockout(impactDelay));
        else RpcTriggerHurt("Hurt " + Random.Range(1, 5), impactDelay);
    }

    [Server] private IEnumerator DelayedKnockout(float delay) { yield return new WaitForSeconds(delay); RpcKnockout(); }

    [ClientRpc] void RpcTriggerHurt(string trigger, float delay) { StartCoroutine(DelayedHurtRoutine(trigger, delay)); }

    private IEnumerator DelayedHurtRoutine(string trigger, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (animator) animator.SetTrigger(trigger);
        if (isLocalPlayer) 
        { 
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
            if (!RhythmRoundManager.Instance.IsSingleMoveMode())
                return _comboBuffer.Count < RhythmRoundManager.Instance.currentComboCount; 
            else 
            {
                if (isMovement) return _pendingDashDirection == Vector3.zero;
                else return string.IsNullOrEmpty(_pendingAttackTrigger);
            }
        }
        if (!isMovement) return _attackQueue.Count < 2; 
        return true; 
    }

    private IEnumerator HurtStunTimer() { IsHurting = true; yield return new WaitForSeconds(0.2f); IsHurting = false; }

    [Command] void CmdNotifyCancel() => RpcSyncCancel();
    [ClientRpc] void RpcSyncCancel() { if (animator != null) animator.SetTrigger("Cancel"); isAttacking = false; }

    private IEnumerator FlashEffectRoutine() { if (playerRenderer == null || flashMaterial == null) yield break; playerRenderer.material = flashMaterial; yield return new WaitForSeconds(0.1f); playerRenderer.material = _originalMaterial; }

    [ClientRpc] void RpcKnockout() { IsDead = true; animator.SetTrigger("Knock out"); if (isServer) StartCoroutine(ServerRestartMatchRoutine()); }
    [Server] private IEnumerator ServerRestartMatchRoutine() { yield return new WaitForSeconds(4f); NetworkManager.singleton.ServerChangeScene(SceneManager.GetActiveScene().name); }

    private void OnGUI()
    {
        if (!isLocalPlayer) return;
        if (RhythmRoundManager.Instance == null || !RhythmRoundManager.Instance.isRoundActive) return;

        GUILayout.BeginArea(new Rect(20, Screen.height - 300, 350, 280));
        GUIStyle headerStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 20 };
        GUIStyle itemStyle = new GUIStyle(GUI.skin.label) { fontSize = 18 };
        headerStyle.normal.textColor = Color.green;

        if (RhythmRoundManager.Instance.IsSingleMoveMode())
        {
            GUILayout.Label("LOCKED ACTION:", headerStyle);
            string atkText = string.IsNullOrEmpty(_pendingAttackTrigger) ? "None" : _pendingAttackTrigger;
            string dshText = (_pendingDashDirection == Vector3.zero) ? "None" : GetDirectionName(_pendingDashDirection);
            GUILayout.Label($"Attack: {atkText}", itemStyle);
            GUILayout.Label($"Move:   {dshText}", itemStyle);
        }
        else
        {
            int maxSlots = RhythmRoundManager.Instance.currentComboCount;
            GUILayout.Label($"CHAIN COMMAND ({_comboBuffer.Count}/{maxSlots}):", headerStyle);
            
            for (int i = 0; i < maxSlots; i++)
            {
                if (i < _comboBuffer.Count)
                {
                    string atk = _comboBuffer[i].attack;
                    string dsh = (_comboBuffer[i].dash == Vector3.zero) ? "" : GetDirectionName(_comboBuffer[i].dash);
                    string displayLine = (atk != "" && dsh != "") ? $"{atk} + {dsh}" : (atk != "" ? atk : dsh);
                    GUILayout.Label($"{i + 1}: {displayLine}", itemStyle);
                }
                else
                {
                    GUI.color = new Color(1, 1, 1, 0.5f);
                    GUILayout.Label($"{i + 1}: [ Empty ]", itemStyle);
                    GUI.color = Color.white;
                }
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