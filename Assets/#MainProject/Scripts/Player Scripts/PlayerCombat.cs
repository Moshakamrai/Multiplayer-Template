using Mirror;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class PlayerCombat : NetworkBehaviour
{
    [SyncVar] public int CurrentHealth = 100;
    public Animator animator;

    // Use this as the "Master Lock" for the queue
    public bool isAttacking = false; 

    public bool IsDead { get; private set; }
    public bool IsHurting { get; private set; }
    
    private Queue<string> _attackQueue = new Queue<string>();

    // VOICE WRAPPERS
    public void VoiceAttackJab() { if (isLocalPlayer && !IsDead) _attackQueue.Enqueue("Jab"); }
    public void VoiceAttackCross() { if (isLocalPlayer && !IsDead) _attackQueue.Enqueue("Cross"); }
    public void VoiceAttackHook() { if (isLocalPlayer && !IsDead) _attackQueue.Enqueue("Hook"); }
    public void VoiceAttackUppercut() { if (isLocalPlayer && !IsDead) _attackQueue.Enqueue("Uppercut"); }

    private void Update()
    {
        if (!isLocalPlayer || IsDead || IsHurting) return;

        // Only pull from queue if we are NOT currently in an attack animation
        if (!isAttacking && _attackQueue.Count > 0) 
        {
            StartCoroutine(PerformAttack(_attackQueue.Dequeue()));
        }

        if (Input.GetKeyDown(KeyCode.E)) CmdTestDamage(); 
    }

    private IEnumerator PerformAttack(string trigger)
    {
        // 1. Lock the system
        isAttacking = true;

        // 2. Trigger animations
        if (animator) animator.SetTrigger(trigger);
        CmdTriggerAttack(trigger);

        // 3. WAIT until the Animation Event calls EndAttackWindow()
        // This ensures the next attack in the queue waits for the current one to finish
        yield return new WaitUntil(() => isAttacking == false);
        
        // Small buffer to prevent instant "snapping" between animations
        yield return new WaitForSeconds(0.1f);
    }

    // --- ANIMATION EVENTS ---
    // Make sure these are placed at the very start and very end of your attack clips!
    public void StartAttackWindow()
    {
        isAttacking = true;
    }

    public void EndAttackWindow()
    {
        isAttacking = false;
    }

    // --- NETWORK COMMANDS ---
    [Command] void CmdTestDamage() => TakeDamage(10);

    [Server]
    public void TakeDamage(int damage)
    {
        if (IsDead) return;
        CurrentHealth -= damage;
        if (CurrentHealth <= 0) RpcKnockout();
        else RpcTriggerHurt("Hurt " + Random.Range(1, 5));
    }

    [ClientRpc]
    void RpcTriggerHurt(string trigger)
    {
        if (animator) animator.SetTrigger(trigger);
        if (isLocalPlayer) 
        { 
            _attackQueue.Clear(); 
            isAttacking = false; // Reset lock on hurt
        }
    }

    [ClientRpc] void RpcKnockout() { IsDead = true; animator.SetTrigger("Knockout"); }

    [Command] void CmdTriggerAttack(string t) => RpcTriggerAttack(t);
    [ClientRpc] void RpcTriggerAttack(string t) { if (!isLocalPlayer && animator != null) animator.SetTrigger(t); }
}