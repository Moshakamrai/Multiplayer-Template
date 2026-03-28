using Mirror;
using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(NetworkIdentity))]
public class PlayerController : NetworkBehaviour
{
    private CharacterController _characterController;
    private PlayerCombat _combat;
    private float _rotationYLocal;
    [SyncVar] private float _rotationYRemote;

    [Header("Dash Settings")]
    public float dashDistance = 2f;
    public float dashDuration = 0.15f;
    public float dashCooldown = 0.25f; 

    [Header("Equidistance Settings")]
    public float DesiredDistance = 2.5f;
    public float SpacingSpeed = 2.0f;
    [SyncVar] private bool _isSpacingActive = false;

    private Queue<Vector3> _dashQueue = new Queue<Vector3>();
    private bool _isDashing;
    private float _lastDashTime;
    private Vector3 _dashDirection;
    private float _dashElapsed;
    private float _velocityY;

    public float LockOnPitch = 15f;

    private CapsuleCollider playerCollider;

    [Header("Lock-On Settings")]
    public float LockOnSpeed = 25f; 

    public Transform CameraPosition => cameraPosition;
    [SerializeField] private Transform cameraPosition;

    [field: SyncVar] public string PlayerName { get; private set; }
    [field: SyncVar] public bool Ready { get; private set; }

    [Command]
    public void SetReadyCmd(bool ready) { Ready = ready; }

    private void Awake() { if (GameManager.players != null) GameManager.players.Add(this); }
    private void OnDestroy() { if (GameManager.players != null) GameManager.players.Remove(this); }

    private void Start()
    {
        _characterController = GetComponent<CharacterController>();
        _combat = GetComponent<PlayerCombat>();
        playerCollider = GetComponent<CapsuleCollider>();
        _characterController.enabled = isLocalPlayer || isServer;
        
        if (isLocalPlayer)
        {
            GameManager.localPlayer = this;
            SetNameCmd(GameManager.PlayerName);
        }
    }

    private void Update()
    {
        if (!isLocalPlayer || _combat.IsDead || _combat.IsHurting) return;
        ProcessDashQueue();
        Movement();
    }

    private void FixedUpdate()
    {
        if (!isServer || !_isSpacingActive) return;

        PlayerController opponent = GetOpponent();
        if (opponent == null) return;

        float currentDist = Vector3.Distance(transform.position, opponent.transform.position);
        
        if (!_isDashing && !_combat.IsHurting && !opponent._isDashing && !opponent._combat.IsHurting)
        {
            if (Mathf.Abs(currentDist - DesiredDistance) > 0.1f)
            {
                Vector3 dir = (opponent.transform.position - transform.position).normalized;
                float moveDir = (currentDist > DesiredDistance) ? 1f : -1f;
                _characterController.Move(dir * moveDir * SpacingSpeed * Time.fixedDeltaTime);
            }
        }
    }

    [Command]
    public void CmdRhythmDash(Vector3 dir)
    {
        ApplyDash(dir); 
        RpcSyncDash(dir);
    }

    [Command] 
    void CmdDash(Vector3 dir) 
    { 
        ApplyDash(dir); 
        RpcSyncDash(dir); 
    }

    // FIXED: Removed !isLocalPlayer so the character moves when the server tells it to
    [ClientRpc] void RpcSyncDash(Vector3 dir) { ApplyDash(dir); }

    private void ApplyDash(Vector3 dir)
    {
        _lastDashTime = Time.time;
        _dashDirection = dir;
        _dashElapsed = 0f;
        _isDashing = true;

        if (_combat != null && _combat.animator != null)
        {
            if (dir == Vector3.left) _combat.animator.Play("MoveLeft");
            else if (dir == Vector3.right) _combat.animator.Play("MoveRight");
        }
    }

    public void ApplyDashExternal(Vector3 dir) { ApplyDash(dir); }

    private void Movement()
    {
        PlayerController opponent = GetOpponent();
        if (opponent != null)
        {
            if (!_combat.isAttacking) 
            {
                Vector3 directionToOpponent = opponent.transform.position - transform.position;
                directionToOpponent.y = 0;
                if (directionToOpponent != Vector3.zero)
                {
                    Quaternion targetRotation = Quaternion.LookRotation(directionToOpponent);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * LockOnSpeed);
                }
                _rotationYLocal = Mathf.Lerp(_rotationYLocal, LockOnPitch, Time.deltaTime * LockOnSpeed); 
            }
        }
        else
        {
            transform.Rotate(0, GameManager.Look.x * GameManager.Sensitivity * Time.deltaTime, 0);
            _rotationYLocal = Mathf.Clamp(_rotationYLocal + -GameManager.Look.y * GameManager.Sensitivity * Time.deltaTime, -90, 90);
        }

        if (NetworkClient.ready) UpdateLookRotationCmd(_rotationYLocal);
        cameraPosition.localRotation = Quaternion.Euler(_rotationYLocal, 0, 0);

        if (_isDashing)
        {
            _characterController.Move(transform.TransformDirection(_dashDirection) * (dashDistance / dashDuration) * Time.deltaTime);
            _dashElapsed += Time.deltaTime;
            playerCollider.enabled = false;
            if (_dashElapsed >= dashDuration) _isDashing = false;
            return;
        }
        else { playerCollider.enabled = true; }

        Vector3 autoSpacingVelocity = Vector3.zero;
        if (opponent != null && !_isDashing && !_combat.isAttacking && !_combat.IsHurting)
        {
            float currentDist = Vector3.Distance(transform.position, opponent.transform.position);
            if (Mathf.Abs(currentDist - DesiredDistance) > 0.2f)
            {
                Vector3 dirToOpponent = opponent.transform.position - transform.position;
                dirToOpponent.y = 0;
                dirToOpponent.Normalize();
                float moveDir = (currentDist > DesiredDistance) ? 1f : -1f;
                autoSpacingVelocity = dirToOpponent * moveDir * SpacingSpeed;
            }
        }

        _velocityY += Physics.gravity.y * Time.deltaTime;
        Vector3 targetVelocity = (GameManager.Move.y * transform.forward + GameManager.Move.x * transform.right) * GameManager.Speed;
        targetVelocity += autoSpacingVelocity;
        targetVelocity.y = _velocityY;
        _characterController.Move(targetVelocity * Time.deltaTime);
    }

    public void InterruptMovement() { _dashQueue.Clear(); _isDashing = false; }
    private void ProcessDashQueue()
    {
        if (_isDashing || _combat.isAttacking || Time.time - _lastDashTime < dashCooldown || _dashQueue.Count == 0) return;
        Vector3 nextDir = _dashQueue.Dequeue();
        ApplyDash(nextDir);
        CmdDash(nextDir);
    }

    public void VoiceDashLeft() { if (_dashQueue.Count < 4) _dashQueue.Enqueue(Vector3.left); }
    public void VoiceDashRight() { if (_dashQueue.Count < 4) _dashQueue.Enqueue(Vector3.right); }

    [Command] private void UpdateLookRotationCmd(float rot) { _rotationYRemote = rot; }
    [Command] private void SetNameCmd(string playerName) { PlayerName = playerName; }

    public PlayerController GetOpponent()
    {
        foreach (var p in GameManager.players) { if (p != null && p != this) return p; }
        return null;
    }
}