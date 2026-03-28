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

    // Must be public so RhythmRoundManager can access it
    public struct RhythmAction { public string attack; public Vector3 dash; }
    public List<RhythmAction> _comboBuffer = new List<RhythmAction>();

    [Header("VFX Settings")]
    public Renderer playerRenderer;
    public Material flashMaterial;
    public Material _originalMaterial;

    private string _pendingAttackTrigger = "";
    private Vector3 _pendingDashDirection = Vector3.zero;
    private Coroutine _rechargeCoroutine; 

    private void Start()
    {
        if (isLocalPlayer && ShieldText == null)
            ShieldText = GameObject.Find("ShieldText")?.GetComponent<Text>();
    }

    public void VoiceAttackJab() { if (isLocalPlayer && !IsDead) _attackQueue.Enqueue("Jab"); }
    public void VoiceAttackCross() { if (isLocalPlayer && !IsDead) _attackQueue.Enqueue("Cross"); }
    public void VoiceAttackHook() { if (isLocalPlayer && !IsDead) _attackQueue.Enqueue("Hook"); }
    public void VoiceAttackUppercut() { if (isLocalPlayer && !IsDead) _attackQueue.Enqueue("Uppercut"); }

    private void Update()
    {
        if (isLocalPlayer && ShieldText != null) ShieldText.text = CurrentShield.ToString();
        if (!isLocalPlayer || IsDead || IsHurting) return;

        if (!isAttacking && _attackQueue.Count > 0) 
            StartCoroutine(PerformAttack(_attackQueue.Dequeue()));
    }

    private IEnumerator PerformAttack(string trigger)
    {
        isAttacking = true;

        // IMMEDIATE LOCAL FEEDBACK: Trigger for the local player now
        if (isLocalPlayer && animator != null) 
        {
            animator.SetTrigger(trigger);
        }

        int damageToSet = (trigger == "Cross") ? 15 : (trigger == "Hook") ? 25 : (trigger == "Uppercut") ? 30 : 10;

        // Send to server to sync with everyone else
        CmdTriggerAttack(trigger, damageToSet);

        yield return new WaitUntil(() => isAttacking == false);
        yield return new WaitForSeconds(0.1f);
    }
    // 1. The local script (VoiceCommandManager) calls this
    public void QueueRhythmMove(string attackTrigger, Vector3 dashDir)
    {
        if (IsDead || IsHurting) return;

        // Update local variables so the GUI shows the moves immediately
        QueueLogic(attackTrigger, dashDir);

        // 2. CRITICAL: Tell the server what we just queued!
        if (isLocalPlayer) CmdQueueRhythmMove(attackTrigger, dashDir);
    }

    [Command]
    private void CmdQueueRhythmMove(string attack, Vector3 dash)
    {
        // This updates the server's version of the client's player
        QueueLogic(attack, dash);
    }

    // Move the core logic into a shared private method
    private void QueueLogic(string attackTrigger, Vector3 dashDir)
    {
        if (RhythmRoundManager.Instance.currentType == RoundType.SlowRhythm)
        {
            if (!string.IsNullOrEmpty(attackTrigger)) { _pendingAttackTrigger = attackTrigger; _pendingDashDirection = Vector3.zero; }
            if (dashDir != Vector3.zero) { _pendingDashDirection = dashDir; _pendingAttackTrigger = ""; }
        }
        else
        {
            if (_comboBuffer.Count < 4) _comboBuffer.Add(new RhythmAction { attack = attackTrigger, dash = dashDir });
        }
    }

    public RhythmAction GetLockedMove()
    {
        if (RhythmRoundManager.Instance.currentType == RoundType.SlowRhythm)
        {
            return new RhythmAction { attack = _pendingAttackTrigger, dash = _pendingDashDirection };
        }

        // Return the first move of the combo for RPS resolution
        // FIX: Added to _comboBuffer
        return (_comboBuffer.Count > 0) 
    ? _comboBuffer[0] 
    : new RhythmAction { attack = "", dash = Vector3.zero };
    }

    private void OnTriggerEnter(Collider other)
    {
        if (RhythmRoundManager.Instance != null && RhythmRoundManager.Instance.isRoundActive) return;

        if (!isServer || !other.CompareTag("Jabbed")) return;
        HitboxProperties hitbox = other.GetComponent<HitboxProperties>();
        if (hitbox == null || hitbox.owner.netId == this.netId) return;

        if (!IsDead && !IsHurting) 
        {
            TakeDamage(hitbox.currentDamage);
        }
    }

    [Server]
    public void ExecuteRhythmImpact()
    {
        // 1. Tell the owner of this specific player object to execute their queued move locally.
        // If it's the Host's character, it runs on the Host. If it's the Client's, it runs on the Client.
        TargetTriggerRhythmImpact();
        
        // 2. Clear the server's cache of the move so it doesn't fire twice
        if (RhythmRoundManager.Instance.currentType == RoundType.SlowRhythm)
        {
            _pendingAttackTrigger = "";
            _pendingDashDirection = Vector3.zero;
        }
    }

    [TargetRpc]
    public void TargetTriggerRhythmImpact()
    {
        // This runs ONLY on the specific Client who owns this player
        if (RhythmRoundManager.Instance.currentType == RoundType.SlowRhythm)
        {
            // 1. Trigger the Attack locally
            if (!string.IsNullOrEmpty(_pendingAttackTrigger))
            {
                StartCoroutine(PerformAttack(_pendingAttackTrigger));
                _pendingAttackTrigger = "";
            }
            
            // 2. CRITICAL FIX: Trigger the queued Dash across the network
            if (_pendingDashDirection != Vector3.zero)
            {
                GetComponent<PlayerController>().CmdRhythmDash(_pendingDashDirection);
                _pendingDashDirection = Vector3.zero; // Clear local cache
            }
        }
        else
        {
            StartCoroutine(PlayComboRoutine());
        }
    }



    private IEnumerator PlayComboRoutine()
    {
        foreach (var action in _comboBuffer)
        {
            yield return new WaitUntil(() => isAttacking == false);

            if (!string.IsNullOrEmpty(action.attack))
            {
                yield return StartCoroutine(PerformAttack(action.attack));
            }

            if (action.dash != Vector3.zero)
            {
                GetComponent<PlayerController>().CmdRhythmDash(action.dash);
                yield return new WaitForSeconds(0.2f); 
            }
        }
        _comboBuffer.Clear();
    }

    public void StartAttackWindow() { isAttacking = true; }
    public void EndAttackWindow() { isAttacking = false; }

    [Command] void CmdTriggerAttack(string t, int damage) 
    { 
        weaponGloveLeft.GetComponent<HitboxProperties>().currentDamage = damage;
        weaponGloveRight.GetComponent<HitboxProperties>().currentDamage = damage;
        RpcTriggerAttack(t); 
    }

    [ClientRpc] 
    void RpcTriggerAttack(string t) 
    { 
        // IMPORTANT: If we are the local player, we already played the animation!
        // This prevents the "double trigger" or stuttering.
        if (isLocalPlayer) return; 
    
        if (animator != null) animator.SetTrigger(t); 
    }

    [Server]
    public void TakeDamage(int damage)
    {
        if (IsDead) return;
        if (_rechargeCoroutine != null) StopCoroutine(_rechargeCoroutine);
        _rechargeCoroutine = StartCoroutine(ShieldRechargeRoutine());
        StartCoroutine(FlashEffectRoutine());

        if (CurrentShield > 0)
        {
            CurrentShield -= damage;
            if (CurrentShield < 0) { CurrentHealth += CurrentShield; CurrentShield = 0; }
        }
        else { CurrentHealth -= damage; }

        if (CurrentHealth <= 0) RpcKnockout();
        else if (CurrentShield == 0 || damage > 15) RpcTriggerHurt("Hurt " + Random.Range(1, 5));
    }

    [Server] private IEnumerator ShieldRechargeRoutine()
    {
        yield return new WaitForSeconds(6f);
        while (CurrentShield < MaxShield && !IsDead) { CurrentShield++; yield return new WaitForSeconds(0.05f); }
    }

    // Inside PlayerCombat.cs...



[ClientRpc]
void RpcTriggerHurt(string trigger)
{
    if (animator) animator.SetTrigger(trigger);
    
    if (isLocalPlayer) 
    { 
        StopAllCoroutines(); 
        _attackQueue.Clear(); 
        _comboBuffer.Clear(); 
        _pendingAttackTrigger = "";
        _pendingDashDirection = Vector3.zero;

        isAttacking = false; 
        GetComponent<PlayerController>().InterruptMovement();
        StartCoroutine(HurtStunTimer());
    }
}

    private IEnumerator HurtStunTimer() { IsHurting = true; yield return new WaitForSeconds(1f); IsHurting = false; }
    
    public void RequestCancelAttack()
    {
        if (!isLocalPlayer || !isAttacking) return;
        StopAllCoroutines(); isAttacking = false;
        CmdNotifyCancel();
    }

    [Command] void CmdNotifyCancel() => RpcSyncCancel();
    [ClientRpc] void RpcSyncCancel() { if (animator != null) animator.SetTrigger("Cancel"); isAttacking = false; }

    private IEnumerator FlashEffectRoutine()
    {
        if (playerRenderer == null || flashMaterial == null) yield break;
        playerRenderer.material = flashMaterial; yield return new WaitForSeconds(0.1f);
        playerRenderer.material = _originalMaterial;
    }

    [ClientRpc] void RpcKnockout() { IsDead = true; animator.SetTrigger("Knock out"); if (isServer) StartCoroutine(ServerRestartMatchRoutine()); }
    [Server] private IEnumerator ServerRestartMatchRoutine() { yield return new WaitForSeconds(4f); NetworkManager.singleton.ServerChangeScene(SceneManager.GetActiveScene().name); }

    private void OnGUI()
    {
        if (!isLocalPlayer) return;
        if (RhythmRoundManager.Instance == null || !RhythmRoundManager.Instance.isRoundActive) return;

        GUILayout.BeginArea(new Rect(20, Screen.height - 180, 300, 160));
        GUIStyle headerStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 18 };
        headerStyle.normal.textColor = Color.green;

        if (RhythmRoundManager.Instance.currentType == RoundType.SlowRhythm)
        {
            GUILayout.Label("LOCKED ACTION:", headerStyle);
            string atkText = string.IsNullOrEmpty(_pendingAttackTrigger) ? "None" : _pendingAttackTrigger;
            string dshText = (_pendingDashDirection == Vector3.zero) ? "None" : GetDirectionName(_pendingDashDirection);
            GUILayout.Label($"Attack: {atkText}");
            GUILayout.Label($"Move:   {dshText}");
        }
        else
        {
            GUILayout.Label($"COMBO CHAIN ({_comboBuffer.Count}/4):", headerStyle);
            for (int i = 0; i < 4; i++)
            {
                if (i < _comboBuffer.Count)
                {
                    string atk = _comboBuffer[i].attack;
                    string dsh = (_comboBuffer[i].dash == Vector3.zero) ? "" : GetDirectionName(_comboBuffer[i].dash);
                    string displayLine = (atk != "" && dsh != "") ? $"{atk} + {dsh}" : (atk != "" ? atk : dsh);
                    GUILayout.Label($"{i + 1}: {displayLine}");
                }
                else
                {
                    GUI.color = new Color(1, 1, 1, 0.5f);
                    GUILayout.Label($"{i + 1}: [ Empty ]");
                    GUI.color = Color.white;
                }
            }
        }
        GUILayout.EndArea();
    }

    private string GetDirectionName(Vector3 dir)
    {
        if (dir == Vector3.forward) return "Forward";
        if (dir == Vector3.back) return "Back";
        if (dir == Vector3.left || dir == new Vector3(-1, 0, 0)) return "Left";
        if (dir == Vector3.right || dir == new Vector3(1, 0, 0)) return "Right";
        return dir.ToString();
    }
}