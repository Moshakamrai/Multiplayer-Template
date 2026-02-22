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
                     
    public SphereCollider weaponGloveLeft;
    public SphereCollider weaponGloveRight;

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

        //if (Input.GetKeyDown(KeyCode.E)) CmdTestDamage(); 
    }

    private IEnumerator PerformAttack(string trigger)
    {
        isAttacking = true;

        // --- SET DAMAGE VALUES ---
        int damageToSet = 10; // Default
        if (trigger == "Cross") damageToSet = 15;
        else if (trigger == "Hook") damageToSet = 25;
        else if (trigger == "Uppercut") damageToSet = 30;

        // Set it locally
        weaponGloveLeft.GetComponent<HitboxProperties>().currentDamage = damageToSet;
        weaponGloveRight.GetComponent<HitboxProperties>().currentDamage = damageToSet;

        if (animator) animator.SetTrigger(trigger);
        
        // NEW: Send the damage value to the Server!
        CmdTriggerAttack(trigger, damageToSet);

        yield return new WaitUntil(() => isAttacking == false);
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

    private void OnTriggerEnter(Collider other)
    {
        if (!isServer || !other.CompareTag("Jabbed")) return;

        // Get the properties script from the glove that hit us
        HitboxProperties hitbox = other.GetComponent<HitboxProperties>();
        if (hitbox == null || hitbox.owner == null) return;

        // Self-harm protection using the owner reference
        if (hitbox.owner.netId == this.netId) return;

        if (!IsDead && !IsHurting) 
        {
            Debug.Log($"Hit by {hitbox.owner.netId} for {hitbox.currentDamage} damage!");
            
            ParticlePoolManager.Instance.PlayParticle("Hit", other.transform.position);
            
            // USE THE VARIABLE DAMAGE HERE
            TakeDamage(hitbox.currentDamage);
        }
    }

    // --- NETWORK COMMANDS ---
    // --- NETWORK COMMANDS ---
    
    [Command] 
    void CmdTriggerAttack(string t, int damage) 
    { 
        // The Server updates its own copy of the gloves so it knows how hard the punch is!
        weaponGloveLeft.GetComponent<HitboxProperties>().currentDamage = damage;
        weaponGloveRight.GetComponent<HitboxProperties>().currentDamage = damage;
        
        RpcTriggerAttack(t); 
    }

    
    
    [ClientRpc] 
    void RpcTriggerAttack(string t) 
    { 
        if (!isLocalPlayer && animator != null) animator.SetTrigger(t); 
    }

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
            // 1. Stop all current combat actions
            StopAllCoroutines(); 
            _attackQueue.Clear(); 
            isAttacking = false; 

            // 2. Stop all current movement actions
            GetComponent<PlayerController>().InterruptMovement();

            // 3. Start the Stun State
            StartCoroutine(HurtStunTimer());
        }
    }

    // Coroutine to handle the Stun duration
    private IEnumerator HurtStunTimer()
    {
        // Lock the player out of Voice Commands
        IsHurting = true; 

        // Wait for 0.6 seconds (Adjust this to match the exact length of your Hurt animation!)
        yield return new WaitForSeconds(1f);

        // Unlock the player
        IsHurting = false; 
    }

    // Call this from VoiceCommandManager or a Keybind
    public void RequestCancelAttack()
    {
        if (!isLocalPlayer || !isAttacking) return;

        // ONLY cancel if the weapon colliders are currently DISABLED (the punch hasn't "fired" yet)
        if (!weaponGloveLeft.enabled && !weaponGloveRight.enabled)
        {
            StopAllCoroutines(); // Kills PerformAttack
            ResetCombatState();   // Resets local variables
            CmdNotifyCancel();    // Tells everyone else to stop the animation
            Debug.Log("Attack Cancelled!");
        }
    }

    [Command]
    void CmdNotifyCancel() => RpcSyncCancel();

    [ClientRpc]
    void RpcSyncCancel()
    {
        if (animator != null)
        {
            // Play the "Idle" or "Default" state immediately to snap out of the attack
            animator.SetTrigger("Cancel"); 
            
            // Alternatively, use animator.Rebind() for a hard reset:
            // animator.Rebind(); 
        }
        
        if (!isLocalPlayer) ResetCombatState();
    }

    private void ResetCombatState()
    {
        isAttacking = false;
        // Optionally clear the queue if you want a cancel to wipe subsequent attacks
        // _attackQueue.Clear(); 
    }

    [ClientRpc] void RpcKnockout() { IsDead = true; animator.SetTrigger("Knock out"); }

    //[Command] void CmdTriggerAttack(string t) => RpcTriggerAttack(t);
    //[ClientRpc] void RpcTriggerAttack(string t) { if (!isLocalPlayer && animator != null) animator.SetTrigger(t); }
}