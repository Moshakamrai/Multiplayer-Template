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
        int damageToSet = (trigger == "Cross") ? 15 : (trigger == "Hook") ? 25 : (trigger == "Uppercut") ? 30 : 10;

        weaponGloveLeft.GetComponent<HitboxProperties>().currentDamage = damageToSet;
        weaponGloveRight.GetComponent<HitboxProperties>().currentDamage = damageToSet;

        if (animator) animator.SetTrigger(trigger);
        CmdTriggerAttack(trigger, damageToSet);

        yield return new WaitUntil(() => isAttacking == false);
        yield return new WaitForSeconds(0.1f);
    }

    public void QueueRhythmMove(string attackTrigger, Vector3 dashDir)
    {
        // SLOW RHYTHM: Last command wipes the buffer entirely
        _pendingAttackTrigger = attackTrigger;
        _pendingDashDirection = dashDir;
        Debug.Log($"<color=orange>BUFFER UPDATED: {attackTrigger} | {dashDir}</color>");
    }

    public void ExecuteRhythmImpact()
    {
        if (!string.IsNullOrEmpty(_pendingAttackTrigger)) 
            StartCoroutine(PerformAttack(_pendingAttackTrigger));
        
        if (_pendingDashDirection != Vector3.zero) 
            GetComponent<PlayerController>().CmdRhythmDash(_pendingDashDirection);

        _pendingAttackTrigger = "";
        _pendingDashDirection = Vector3.zero;
    }

    public void StartAttackWindow() { isAttacking = true; }
    public void EndAttackWindow() { isAttacking = false; }

    private void OnTriggerEnter(Collider other)
    {
        if (!isServer || !other.CompareTag("Jabbed")) return;
        HitboxProperties hitbox = other.GetComponent<HitboxProperties>();
        if (hitbox == null || hitbox.owner.netId == this.netId) return;

        if (!IsDead && !IsHurting) 
        {
            ParticlePoolManager.Instance.PlayParticle("Hit", other.transform.position);
            TakeDamage(hitbox.currentDamage);
        }
    }

    [Command] void CmdTriggerAttack(string t, int damage) 
    { 
        weaponGloveLeft.GetComponent<HitboxProperties>().currentDamage = damage;
        weaponGloveRight.GetComponent<HitboxProperties>().currentDamage = damage;
        RpcTriggerAttack(t); 
    }

    [ClientRpc] void RpcTriggerAttack(string t) 
    { if (!isLocalPlayer && animator != null) animator.SetTrigger(t); }

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

    [ClientRpc] void RpcTriggerHurt(string trigger)
    {
        if (animator) animator.SetTrigger(trigger);
        if (isLocalPlayer) 
        { 
            StopAllCoroutines(); _attackQueue.Clear(); isAttacking = false; 
            GetComponent<PlayerController>().InterruptMovement();
            StartCoroutine(HurtStunTimer());
        }
    }

    private IEnumerator HurtStunTimer() { IsHurting = true; yield return new WaitForSeconds(1f); IsHurting = false; }
    
    public void RequestCancelAttack()
    {
        if (!isLocalPlayer || !isAttacking) return;
        if (!weaponGloveLeft.enabled && !weaponGloveRight.enabled)
        {
            StopAllCoroutines(); isAttacking = false;
            CmdNotifyCancel();
        }
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
}