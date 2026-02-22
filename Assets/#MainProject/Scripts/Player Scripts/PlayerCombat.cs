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

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Jabbed")) return;

        

        PlayerCombat attacker = other.GetComponentInParent<PlayerCombat>();
        if (attacker == null) return;

        // 1. Log exactly who hit who, on every screen!
        Debug.Log($"[Physics] Victim: {gameObject.name} | Attacker: Player {attacker.netId} | isServer: {isServer}");

        // 2. Stop Clients from dealing damage
        if (!isServer) 
        {
            // Debug.Log("Hit ignored: This window is a Client, not the Server.");
            return; 
        }

        // 3. Stop Self-Harm
        if (attacker.netId == this.netId) 
        {
            Debug.Log("Hit ignored: Player hit themselves.");
            return; 
        }

        // 4. WE HAVE A VALID MULTIPLAYER HIT!
        Debug.Log($"[Physics] BOOM! VALID HIT! Player {attacker.netId} punched {gameObject.name}!");
        
        if (!IsDead && !IsHurting) 
        {
            ParticlePoolManager.Instance.PlayParticle("HitSpatter", other.gameObject.transform.position);
            TakeDamage(10);
        }
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

    [ClientRpc] void RpcKnockout() { IsDead = true; animator.SetTrigger("Knockout"); }

    [Command] void CmdTriggerAttack(string t) => RpcTriggerAttack(t);
    [ClientRpc] void RpcTriggerAttack(string t) { if (!isLocalPlayer && animator != null) animator.SetTrigger(t); }
}