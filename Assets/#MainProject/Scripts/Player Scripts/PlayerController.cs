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

    private CapsuleCollider playerCollider;

    [Header("Lock-On Settings")]
    public float LockOnSpeed = 25f; // Higher = more instantaneous tracking

    public Transform CameraPosition => cameraPosition;
    [SerializeField] private Transform cameraPosition;

    [field: SyncVar] public string PlayerName { get; private set; }
    [field: SyncVar] public bool Ready { get; private set; }

    [Command]
    public void SetReadyCmd(bool ready)
    {
        Ready = ready;
    }

    private void Awake()
    {
        if (GameManager.players != null) GameManager.players.Add(this);
    }

    private void OnDestroy()
    {
        if (GameManager.players != null) GameManager.players.Remove(this);
    }

    private void Start()
    {
        _characterController = GetComponent<CharacterController>();
        _combat = GetComponent<PlayerCombat>();
        playerCollider = GetComponent<CapsuleCollider>();
        
        // CharacterController must be enabled on LocalPlayer and on the Server (for spacing)
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
        HandleTestingInput();
        Movement();
    }

    private void HandleTestingInput()
    {
        //if (Input.GetKeyDown(KeyCode.P)) CmdTriggerEquidistance();
        
        // Testing shortcuts
        //if (Input.GetKeyDown(KeyCode.I)) VoiceDashForward();
        //if (Input.GetKeyDown(KeyCode.K)) VoiceDashBack();
       // if (Input.GetKeyDown(KeyCode.J)) VoiceDashLeft();
        //if (Input.GetKeyDown(KeyCode.L)) VoiceDashRight();
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

    [Command] void CmdTriggerEquidistance() => _isSpacingActive = !_isSpacingActive;

    public void VoiceDashForward() { if (_dashQueue.Count < 4) _dashQueue.Enqueue(Vector3.forward); }
    public void VoiceDashBack() { if (_dashQueue.Count < 4) _dashQueue.Enqueue(Vector3.back); }
    public void VoiceDashLeft() { if (_dashQueue.Count < 4) _dashQueue.Enqueue(Vector3.left); }
    public void VoiceDashRight() { if (_dashQueue.Count < 4) _dashQueue.Enqueue(Vector3.right); }

    private void ProcessDashQueue()
    {
        if (_isDashing || _combat.isAttacking || Time.time - _lastDashTime < dashCooldown || _dashQueue.Count == 0) return;
        Vector3 nextDir = _dashQueue.Dequeue();
        ApplyDash(nextDir);
        CmdDash(nextDir);
    }

    [Command] void CmdDash(Vector3 dir) { ApplyDash(dir); RpcSyncDash(dir); }
    [ClientRpc] void RpcSyncDash(Vector3 dir) { if (!isLocalPlayer) ApplyDash(dir); }

    private void ApplyDash(Vector3 dir)
    {
        _lastDashTime = Time.time;
        _dashDirection = dir;
        _dashElapsed = 0f;
        _isDashing = true;
    }

    // ==========================================
    // MOVEMENT & AUTO-LOCK SYSTEM
    // ==========================================
    private void Movement()
    {
        PlayerController opponent = GetOpponent();

        if (opponent != null)
        {
            // 1. ATTACK COMMITMENT (Freeze rotation while attacking)
            if (!_combat.isAttacking) 
            {
                // HORIZONTAL AUTO-LOCK
                Vector3 directionToOpponent = opponent.transform.position - transform.position;
                directionToOpponent.y = 0; // Prevent tilting into the floor

                if (directionToOpponent != Vector3.zero)
                {
                    Quaternion targetRotation = Quaternion.LookRotation(directionToOpponent);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * LockOnSpeed);
                }

                // VERTICAL AUTO-LOCK (Center the camera instantly and smoothly)
                _rotationYLocal = Mathf.Lerp(_rotationYLocal, 0f, Time.deltaTime * LockOnSpeed); 
            }
        }
        else
        {
            // 2. FREE LOOK (Manual Mouse Control) - Only runs if alone in the lobby
            transform.Rotate(0, GameManager.Look.x * GameManager.Sensitivity * Time.deltaTime, 0);
            _rotationYLocal = Mathf.Clamp(_rotationYLocal + -GameManager.Look.y * GameManager.Sensitivity * Time.deltaTime, -90, 90);
        }

        // Apply Vertical Camera Rotation (Pitch)
        if (NetworkClient.ready) UpdateLookRotationCmd(_rotationYLocal);
        cameraPosition.localRotation = Quaternion.Euler(_rotationYLocal, 0, 0);

        // 3. DASH LOGIC
        if (_isDashing)
        {
            _characterController.Move(transform.TransformDirection(_dashDirection) * (dashDistance / dashDuration) * Time.deltaTime);
            _dashElapsed += Time.deltaTime;
            playerCollider.enabled = false; // Disable collider during dash to prevent self-collisions
            if (_dashElapsed >= dashDuration) _isDashing = false;
            return;
        }
        else
        {
            playerCollider.enabled = true; // Re-enable collider after dash
        }

        // 4. AUTO-SPACING (EQUIDISTANCE) LOGIC
        Vector3 autoSpacingVelocity = Vector3.zero;

        // Only drift if there is an opponent and NO ONE is currently throwing a punch or taking damage
        if (opponent != null && !_isDashing && !_combat.isAttacking && !_combat.IsHurting)
        {
            float currentDist = Vector3.Distance(transform.position, opponent.transform.position);

            // Add a 0.2f Deadzone so they don't rapidly jitter back and forth when they reach the perfect distance
            if (Mathf.Abs(currentDist - DesiredDistance) > 0.2f)
            {
                Vector3 dirToOpponent = opponent.transform.position - transform.position;
                dirToOpponent.y = 0; // Keep the drift perfectly horizontal
                dirToOpponent.Normalize();

                // If we are too far, move forward (1). If we are too close, move backward (-1).
                float moveDir = (currentDist > DesiredDistance) ? 1f : -1f;
                
                // Calculate the gentle drift velocity
                autoSpacingVelocity = dirToOpponent * moveDir * SpacingSpeed;
            }
        }

        // 5. GRAVITY & COMBINED MOVEMENT
        _velocityY += Physics.gravity.y * Time.deltaTime;
        
        // Combine manual input movement (if any) with the automatic spacing drift
        Vector3 targetVelocity = (GameManager.Move.y * transform.forward + GameManager.Move.x * transform.right) * GameManager.Speed;
        targetVelocity += autoSpacingVelocity; // Apply the auto-spacing here!
        targetVelocity.y = _velocityY;
        
        _characterController.Move(targetVelocity * Time.deltaTime);
    }

    // Add this to PlayerController.cs
    public void InterruptMovement()
    {
        _dashQueue.Clear();
        _isDashing = false; // Immediately stop current dash
    }

    // ==========================================
    // MISSING HELPER FUNCTIONS RESTORED
    // ==========================================

    [Command] 
    private void UpdateLookRotationCmd(float rot) 
    { 
        _rotationYRemote = rot; 
    }
    
    [Command] 
    private void SetNameCmd(string playerName) 
    { 
        PlayerName = playerName; 
    }

    public PlayerController GetOpponent()
    {
        foreach (var p in GameManager.players) 
        {
            if (p != null && p != this) 
            {
                return p;
            }
        }
        return null;
    }
}