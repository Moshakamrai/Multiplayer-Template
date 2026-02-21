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

    public Transform CameraPosition => cameraPosition;
    [SerializeField] private Transform cameraPosition;

    [field: SyncVar] public string PlayerName { get; private set; }
    [field: SyncVar] public bool Ready { get; private set; }

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
        
        // CRITICAL: Enabled on LocalPlayer for input, and on Server for authoritative movement
        _characterController.enabled = isLocalPlayer || isServer;
        
        if (isLocalPlayer)
        {
            GameManager.localPlayer = this;
            SetNameCmd(GameManager.PlayerName);
        }
    }

    // Add this inside PlayerController.cs
[Command]
public void SetReadyCmd(bool ready)
{
    Ready = ready;
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
        if (Input.GetKeyDown(KeyCode.P)) CmdTriggerEquidistance();
        
        // Dash Testing
        if (Input.GetKeyDown(KeyCode.I)) VoiceDashForward();
        if (Input.GetKeyDown(KeyCode.K)) VoiceDashBack();
        if (Input.GetKeyDown(KeyCode.J)) VoiceDashLeft();  // Added J
        if (Input.GetKeyDown(KeyCode.L)) VoiceDashRight(); // Added L
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

    // VOICE DASH FUNCTIONS
    public void VoiceDashForward() { if (_dashQueue.Count < 4) _dashQueue.Enqueue(Vector3.forward); }
    public void VoiceDashBack() { if (_dashQueue.Count < 4) _dashQueue.Enqueue(Vector3.back); }
    public void VoiceDashLeft() { if (_dashQueue.Count < 4) _dashQueue.Enqueue(Vector3.left); }
    public void VoiceDashRight() { if (_dashQueue.Count < 4) _dashQueue.Enqueue(Vector3.right); }

    private void ProcessDashQueue()
    {
        if (_isDashing || Time.time - _lastDashTime < dashCooldown || _dashQueue.Count == 0) return;
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

    private void Movement()
    {
        transform.Rotate(0, GameManager.Look.x * GameManager.Sensitivity * Time.deltaTime, 0);
        _rotationYLocal = Mathf.Clamp(_rotationYLocal + -GameManager.Look.y * GameManager.Sensitivity * Time.deltaTime, -90, 90);
        
        if (NetworkClient.ready) UpdateLookRotationCmd(_rotationYLocal);
        cameraPosition.localRotation = Quaternion.Euler(_rotationYLocal, 0, 0);

        if (_isDashing)
        {
            _characterController.Move(transform.TransformDirection(_dashDirection) * (dashDistance / dashDuration) * Time.deltaTime);
            _dashElapsed += Time.deltaTime;
            if (_dashElapsed >= dashDuration) _isDashing = false;
            return;
        }

        _velocityY += Physics.gravity.y * Time.deltaTime;
        Vector3 targetVelocity = (GameManager.Move.y * transform.forward + GameManager.Move.x * transform.right) * GameManager.Speed;
        targetVelocity.y = _velocityY;
        _characterController.Move(targetVelocity * Time.deltaTime);
    }

    [Command] private void UpdateLookRotationCmd(float rot) => _rotationYRemote = rot;
    [Command] private void SetNameCmd(string playerName) => PlayerName = playerName;

    public PlayerController GetOpponent()
    {
        foreach (var p in GameManager.players) 
            if (p != null && p != this) return p;
        return null;
    }
}