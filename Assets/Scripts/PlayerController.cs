using Mirror;
using UnityEngine;
using UnityEngine.Rendering;
using System.Collections;

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(CapsuleCollider))]
[RequireComponent(typeof(NetworkIdentity))]
[RequireComponent(typeof(NetworkTransformReliable))]
public class PlayerController : NetworkBehaviour
{
    private float _rotationYLocal;

    [field: SyncVar] public bool Ready { get; private set; }
    [field: SyncVar] public string PlayerName { get; private set; }

    public Transform CameraPosition => cameraPosition;

    [SerializeField] private Transform cameraPosition;
    [SerializeField] private Transform headPosition;

    [SyncVar] private float _rotationYRemote;

    private CharacterController _characterController;
    private CapsuleCollider _capsuleCollider;

    private float _velocityY;
    private bool _airborne;

    // =========================
    // DASH VARIABLES
    // =========================

    [Header("Dash Settings")]
    [SerializeField] private float dashDistance = 2f;
    [SerializeField] private float dashDuration = 0.15f;
    [SerializeField] private float dashCooldown = 0.6f;

    private bool _isDashing;
    private float _lastDashTime;

    private Vector3 _dashDirection;
    private float _dashElapsed;

    // =========================

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
        _capsuleCollider = GetComponent<CapsuleCollider>();

        _characterController.height -= _characterController.skinWidth * 2;
        _characterController.radius -= _characterController.skinWidth;

        _capsuleCollider.enabled = !isLocalPlayer;
        _characterController.enabled = isLocalPlayer;
    }

    public override void OnStartLocalPlayer()
    {
        GameManager.localPlayer = this;
        SetNameCmd(GameManager.PlayerName);

        foreach (MeshRenderer mr in GetComponentsInChildren<MeshRenderer>())
        {
            mr.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
        }
    }

    private void Update()
    {
        if (isLocalPlayer)
        {
            Movement();
            return;
        }

        // Remote players (Ghosts) only update visuals
        _characterController.enabled = false;
        headPosition.localRotation = Quaternion.Euler(Mathf.Clamp(_rotationYRemote, -45, 45), 0, 0);
    }

    private void Movement()
    {
        if (!_characterController.enabled)
            return;

        // =========================
        // DASH INPUT (KEYBOARD)
        // =========================
        if (!_isDashing)
        {
            if (Input.GetKeyDown(KeyCode.I)) VoiceDashForward();
            if (Input.GetKeyDown(KeyCode.K)) VoiceDashBack();
            if (Input.GetKeyDown(KeyCode.J)) VoiceDashLeft();
            if (Input.GetKeyDown(KeyCode.L)) VoiceDashRight();
        }

        // =========================
        // ROTATION
        // =========================
        transform.Rotate(0, GameManager.Look.x * GameManager.Sensitivity * Time.deltaTime, 0);

        _rotationYLocal = Mathf.Clamp(
            _rotationYLocal + -GameManager.Look.y * GameManager.Sensitivity * Time.deltaTime,
            -90, 90);

        if (NetworkClient.ready)
        {
            UpdateLookRotationCmd(_rotationYLocal);
        }

        cameraPosition.localRotation = Quaternion.Euler(_rotationYLocal, 0, 0);

        // =========================
        // DASH LOGIC (RUNS LOCALLY NOW!)
        // =========================
        if (_isDashing)
        {
            float dashSpeed = dashDistance / dashDuration;
            Vector3 move = transform.TransformDirection(_dashDirection) * dashSpeed;
            _characterController.Move(move * Time.deltaTime);

            _dashElapsed += Time.deltaTime;

            if (_dashElapsed >= dashDuration)
            {
                _isDashing = false;
            }

            return; // Skip normal movement/gravity while dashing
        }

        // =========================
        // NORMAL MOVEMENT
        // =========================

        if (_characterController.isGrounded)
        {
            _velocityY = 0;
            if (GameManager.Jump)
            {
                _airborne = true;
                _velocityY += Mathf.Sqrt(-GameManager.JumpForce * Physics.gravity.y);
            }
            else _airborne = false;
        }

        if (!_airborne)
        {
            if (Physics.Raycast(transform.position + new Vector3(0, _characterController.center.y, 0), Vector3.down, GameManager.GroundedDistance))
                _velocityY = Physics.gravity.y;
            else
                _airborne = true;
        }

        _velocityY += Physics.gravity.y * Time.deltaTime;

        Transform tr = transform;
        Vector3 targetVelocity = (GameManager.Move.y * tr.forward + GameManager.Move.x * tr.right) * GameManager.Speed;
        targetVelocity.y = _velocityY;
        _characterController.Move(targetVelocity * Time.deltaTime);
    }

    // =========================================================
    // VOICE WRAPPERS (UPDATED)
    // =========================================================
    // Now we trigger the dash LOCALLY instantly, then tell the server.
    
    public void VoiceDashForward() 
    { 
        if (!isLocalPlayer) return;
        ApplyDash(Vector3.forward); // Local Move (Instant)
        CmdDashForward();           // Server Sync
    }

    public void VoiceDashBack()    
    { 
        if (!isLocalPlayer) return;
        ApplyDash(Vector3.back); 
        CmdDashBack(); 
    }

    public void VoiceDashLeft()    
    { 
        if (!isLocalPlayer) return;
        ApplyDash(Vector3.left); 
        CmdDashLeft(); 
    }

    public void VoiceDashRight()   
    { 
        if (!isLocalPlayer) return;
        ApplyDash(Vector3.right); 
        CmdDashRight(); 
    }

    // =========================
    // SHARED DASH LOGIC
    // =========================
    // This function runs on both Client and Server to ensure they match.
    private void ApplyDash(Vector3 localDirection)
    {
        // Don't dash if already dashing or on cooldown
        if (_isDashing) return;
        if (Time.time - _lastDashTime < dashCooldown) return;

        _lastDashTime = Time.time;
        _dashDirection = localDirection;
        _dashElapsed = 0f;
        _isDashing = true;
    }

    // =========================
    // SERVER COMMANDS
    // =========================

    [Command] 
    private void CmdDashForward() 
    { 
        ApplyDash(Vector3.forward); 
        RpcSyncDash(Vector3.forward); // Tell other clients (Ghosts) to look like they are dashing
    }

    [Command] private void CmdDashBack()    { ApplyDash(Vector3.back); RpcSyncDash(Vector3.back); }
    [Command] private void CmdDashLeft()    { ApplyDash(Vector3.left); RpcSyncDash(Vector3.left); }
    [Command] private void CmdDashRight()   { ApplyDash(Vector3.right); RpcSyncDash(Vector3.right); }

    [ClientRpc]
    private void RpcSyncDash(Vector3 direction)
    {
        // Ignore LocalPlayer because we already moved instantly in VoiceDashForward!
        if (isLocalPlayer) return; 
        
        // This makes the ghost on other people's screens dash
        ApplyDash(direction);
    }

    [Command] private void UpdateLookRotationCmd(float rotationY) { _rotationYRemote = rotationY; }
    [Command] private void SetNameCmd(string playerName) { PlayerName = playerName; }
}