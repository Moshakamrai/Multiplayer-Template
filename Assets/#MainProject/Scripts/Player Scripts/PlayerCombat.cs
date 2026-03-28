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

    public void VoiceAttackBlock() { if (isLocalPlayer && !IsDead) _attackQueue.Enqueue("Block"); }

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

        if (isLocalPlayer && animator != null) 
        {
            animator.SetTrigger(trigger);
        }

        // UPDATED: Added Block handling (deals 0 damage)
        int damageToSet = (trigger == "Hook") ? 25 : (trigger == "Cross") ? 15 : (trigger == "Jab") ? 10 : 0; 

        CmdTriggerAttack(trigger, damageToSet);

        yield return new WaitUntil(() => isAttacking == false);
        yield return new WaitForSeconds(0.1f);
    }
    // 1. The local script (VoiceCommandManager) calls this
    public void QueueRhythmMove(string attackTrigger, Vector3 dashDir)
    {
        if (IsDead || IsHurting) return;

        // PREVENT DOUBLE-INPUT FOR THE HOST:
        // If we are a Remote Client, we update our local GUI instantly.
        // If we are the Host, we SKIP THIS, because our local GUI *is* the server!
        if (!isServer) 
        {
            QueueLogic(attackTrigger, dashDir);
        }

        // EVERYONE tells the Server to execute the move.
        // For the Host, this will run the QueueLogic exactly once on the server side.
        if (isLocalPlayer) 
        {
            CmdQueueRhythmMove(attackTrigger, dashDir);
        }
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
        // 1. Capture the variables BEFORE clearing them so the Host doesn't overwrite its own memory
        string attackToSend = _pendingAttackTrigger;
        Vector3 dashToSend = _pendingDashDirection;

        // 2. Tell the owner to execute using the safely captured variables
        TargetTriggerRhythmImpact(attackToSend, dashToSend);
        
        // 3. Clear the server's cache safely
        if (RhythmRoundManager.Instance.currentType == RoundType.SlowRhythm)
        {
            _pendingAttackTrigger = "";
            _pendingDashDirection = Vector3.zero;
        }
    }

    [TargetRpc]
    public void TargetTriggerRhythmImpact(string attack, Vector3 dash)
    {
        // This runs ONLY on the specific Client who owns this player
        if (RhythmRoundManager.Instance.currentType == RoundType.SlowRhythm)
        {
            // 1. Trigger the Attack locally using the passed parameter
            if (!string.IsNullOrEmpty(attack))
            {
                StartCoroutine(PerformAttack(attack));
            }
            
            // 2. Trigger the Dash locally using the passed parameter
            if (dash != Vector3.zero)
            {
                GetComponent<PlayerController>().CmdRhythmDash(dash);
            }

            // Clean up the local UI Cache
            _pendingAttackTrigger = "";
            _pendingDashDirection = Vector3.zero; 
        }
        else
        {
            StartCoroutine(PlayComboRoutine());
        }
    }

    [TargetRpc]
    public void TargetAddEnergy(int amount)
    {
        // This securely awards the energy only to the specific client who earned it
        if (isLocalPlayer)
        {
            GetComponent<PlayerEnergy>().AddBonusEnergy(amount);
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
        
        StartCoroutine(FlashEffectRoutine());

        // 1. F**k the Shield. Pure health damage.
        CurrentHealth -= damage;

        // 2. Set the delay to match 50% of the attack animation 
        // (Tune this float to perfectly match the moment the fist hits the face)
        float impactDelay = 0.35f; 

        if (CurrentHealth <= 0) 
        {
            StartCoroutine(DelayedKnockout(impactDelay));
        }
        else 
        {
            // Send the delay over the network so everyone syncs the impact
            RpcTriggerHurt("Hurt " + Random.Range(1, 5), impactDelay);
        }
    }

    // Helper for knocking out exactly on the beat impact
    [Server]
    private IEnumerator DelayedKnockout(float delay)
    {
        yield return new WaitForSeconds(delay);
        RpcKnockout();
    }

    [Server] private IEnumerator ShieldRechargeRoutine()
    {
        yield return new WaitForSeconds(6f);
        while (CurrentShield < MaxShield && !IsDead) { CurrentShield++; yield return new WaitForSeconds(0.05f); }
    }

    // Inside PlayerCombat.cs...



    [ClientRpc]
    void RpcTriggerHurt(string trigger, float delay)
    {
        // Start the delayed reaction locally
        StartCoroutine(DelayedHurtRoutine(trigger, delay));
    }

    private IEnumerator DelayedHurtRoutine(string trigger, float delay)
    {
        // Wait for the exact moment the opponent's fist connects
        yield return new WaitForSeconds(delay);

        if (animator) animator.SetTrigger(trigger);
        
        if (isLocalPlayer) 
        { 
            _attackQueue.Clear(); 
            _pendingAttackTrigger = "";
            _pendingDashDirection = Vector3.zero;

            isAttacking = false; 
            
            // This violently snaps them out of whatever dash or attack they were doing
            GetComponent<PlayerController>().InterruptMovement();
            
            StartCoroutine(HurtStunTimer());
        }
    }

    // Checks if the player's queue is full so they don't waste energy
    public bool HasOpenSlot(bool isMovement)
    {
        if (RhythmRoundManager.Instance != null && RhythmRoundManager.Instance.isRoundActive)
        {
            if (RhythmRoundManager.Instance.currentType == RoundType.FastCombo)
            {
                // Max 4 moves in the echo round
                return _comboBuffer.Count < 4; 
            }
            else 
            {
                // Slow rhythm: Only 1 attack and 1 movement allowed per beat
                if (isMovement) return _pendingDashDirection == Vector3.zero;
                else return string.IsNullOrEmpty(_pendingAttackTrigger);
            }
        }
        
        // In Free Roam, limit the attack queue so they don't spam 10 punches and drain their bar
        if (!isMovement) return _attackQueue.Count < 2; 
        
        return true; 
    }

    private IEnumerator HurtStunTimer() 
    { 
        IsHurting = true; 
        yield return new WaitForSeconds(0.2f); 
        IsHurting = false; 
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