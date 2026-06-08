using Mirror;
using UnityEngine;
using System.Collections;
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
    public float DesiredDistance = 5.0f;
    public float SpacingSpeed = 2.0f;
    [SyncVar] private bool _isSpacingActive = false;

    [Header("Home Anchoring")]
    [SyncVar] private Vector3 _homePosition;
    [SyncVar] private bool    _hasHome = false;

    [Header("Dodge Move VFX (pre-placed in the player prefab — toggled on/off, not spawned)")]
    public GameObject moveLeftVfx;
    public GameObject moveRightVfx;
    [Tooltip("How long a dodge VFX stays on after a left/right dash.")]
    public float moveVfxDuration = 0.4f;
    private Coroutine _moveVfxRoutine;

    // Server stamps each fighter's fixed home (its spawn spot) at round setup. Both the
    // human and the bot ease back to it when idle, so neither drifts via knockback / hurt
    // / dodges and the gap between them stays correct. (SyncVar so the local human reads it.)
    [Server]
    public void SetHome(Vector3 pos)
    {
        _homePosition = pos;
        _hasHome = true;
    }

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

    // Replace your existing SetReadyCmd with these two functions
    public void SetReady(bool ready)
    {
        if (isServer) Ready = ready; // Bots set it directly
        else SetReadyCmd(ready);    // Humans send a Command
    }

    [Command]
    private void SetReadyCmd(bool ready) { Ready = ready; }

    private void Awake() { if (GameManager.players != null) GameManager.players.Add(this); }
    private void OnDestroy() { if (GameManager.players != null) GameManager.players.Remove(this); }

    private void Start()
    {
        _characterController = GetComponent<CharacterController>();
        _combat = GetComponent<PlayerCombat>();
        playerCollider = GetComponent<CapsuleCollider>();
        _characterController.enabled = isLocalPlayer || isServer;

        // Ensure PlayerInventory exists (for shop system)
        if (GetComponent<PlayerInventory>() == null)
            gameObject.AddComponent<PlayerInventory>();

        if (isLocalPlayer)
        {
            GameManager.localPlayer = this;
            SetNameCmd(GameManager.PlayerName);
        }
    }

    private void Update()
    {
        // Allow the code to run if:
        // 1. It's the human playing (isLocalPlayer)
        // 2. OR it's the Bot running on the Server (isServer && !isLocalPlayer)
        bool isBotOnServer = isServer && !isLocalPlayer;

        if (!(isLocalPlayer || isBotOnServer) || _combat.IsDead || _combat.IsHurting) return;

        ProcessDashQueue();
        Movement();
    }

    private void FixedUpdate()
    {
        if (!isServer || !_isSpacingActive) return;

        // Only the BOT closes distance — the player stands still.
        bool isBot = GetComponent<BotController>() != null;
        if (!isBot) return;

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

    // Hard-anchor each fighter to its spawn spot — applied AFTER animation root motion every
    // frame, so the body can NEVER drift (no snap, because nothing ever accumulates):
    //   • BOT    — fully pinned (X + Z). It never moves, so the gap stays exactly constant.
    //   • PLAYER — Z locked to spawn (no forward/back drift); X is free so sidestep dodges work.
    // Vertical (Y / gravity) is always left untouched.
    private void LateUpdate()
    {
        if (VRCameraDriver.VRActive) return; // VR positions fighters in Movement() instead
        bool isBotOnServer = isServer && !isLocalPlayer;
        if (!(isLocalPlayer || isBotOnServer)) return;
        if (!_hasHome) return;

        var rmm = RhythmRoundManager.Instance;
        if (rmm == null || !rmm.isRoundActive) return;

        Vector3 pos = transform.position;
        bool isBot = GetComponent<BotController>() != null;

        transform.position = isBot
            ? new Vector3(_homePosition.x, pos.y, _homePosition.z)  // bot: locked in place
            : new Vector3(pos.x,          pos.y, _homePosition.z);  // player: Z locked, X free
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

        if (dir == Vector3.left)       PlayMoveVfx(moveLeftVfx);
        else if (dir == Vector3.right) PlayMoveVfx(moveRightVfx);
    }

    // Toggle a pre-placed dodge VFX on, then auto-off after moveVfxDuration. Mirrors the
    // defense-VFX pattern in PlayerCombat. Runs everywhere ApplyDash does (local + RPC).
    private void PlayMoveVfx(GameObject vfx)
    {
        if (vfx == null) return;
        if (_moveVfxRoutine != null) StopCoroutine(_moveVfxRoutine);
        // Make sure the other side's VFX isn't left lit if you dodge the opposite way quickly.
        if (moveLeftVfx != null)  moveLeftVfx.SetActive(false);
        if (moveRightVfx != null) moveRightVfx.SetActive(false);
        _moveVfxRoutine = StartCoroutine(MoveVfxRoutine(vfx));
    }

    private IEnumerator MoveVfxRoutine(GameObject vfx)
    {
        vfx.SetActive(true);
        yield return new WaitForSeconds(moveVfxDuration);
        if (vfx != null) vfx.SetActive(false);
        _moveVfxRoutine = null;
    }

    public void ApplyDashExternal(Vector3 dir) { ApplyDash(dir); }

    public void ApplyKnockback(Vector3 dir)
    {
        if (_characterController != null && _characterController.enabled)
            StartCoroutine(KnockbackRoutine(dir));
    }

    private IEnumerator KnockbackRoutine(Vector3 dir)
    {
        float elapsed = 0f;
        const float duration = 0.12f;
        while (elapsed < duration)
        {
            _characterController.Move(dir * 2.5f * (1f - elapsed / duration) * Time.deltaTime);
            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    private void Movement()
    {
        PlayerController opponent = GetOpponent();

        // 1. Handle Rotation and Aiming
        if (opponent != null)
        {
            if (!_combat.isAttacking)
            {
                Vector3 directionToOpponent = opponent.transform.position - transform.position;
                directionToOpponent.y = 0;
                if (directionToOpponent != Vector3.zero)
                {
                    Quaternion targetRotation = Quaternion.LookRotation(directionToOpponent);
                    // Both Local Player and Server (Bot) can rotate to face the opponent
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * LockOnSpeed);
                }

                // Only update the vertical pitch calculation if we are the local player
                if (isLocalPlayer)
                {
                    _rotationYLocal = Mathf.Lerp(_rotationYLocal, LockOnPitch, Time.deltaTime * LockOnSpeed);
                }
            }
        }
        else if (isLocalPlayer) // Manual mouse rotation only for the human player
        {
            transform.Rotate(0, GameManager.Look.x * GameManager.Sensitivity * Time.deltaTime, 0);
            _rotationYLocal = Mathf.Clamp(_rotationYLocal + -GameManager.Look.y * GameManager.Sensitivity * Time.deltaTime, -90, 90);
        }

        // 2. Networking and Camera (Authority Fix)
        // ONLY the local player should send rotation commands to the server
        if (isLocalPlayer && NetworkClient.ready)
        {
            UpdateLookRotationCmd(_rotationYLocal);
        }

        // In VR, VRCameraDriver overwrites this in LateUpdate — skip to avoid fighting it.
        if (cameraPosition != null && !VRCameraDriver.VRActive)
        {
            cameraPosition.localRotation = Quaternion.Euler(_rotationYLocal, 0, 0);
        }

        // 3. Dash Execution
        if (_isDashing)
        {
            _characterController.Move(transform.TransformDirection(_dashDirection) * (dashDistance / dashDuration) * Time.deltaTime);
            _dashElapsed += Time.deltaTime;
            playerCollider.enabled = false;
            if (_dashElapsed >= dashDuration) _isDashing = false;
            return;
        }
        else
        {
            playerCollider.enabled = true;
        }

        // 4. Home anchoring is handled continuously in LateUpdate (after animation root
        //    motion), so the body can't drift via hurt/attack clips. Nothing to add here.
        Vector3 autoSpacingVelocity = Vector3.zero;
        // VR mode keeps the bot in front of the physically-moving player (mirror X, hold Z gap).
        // On flat-screen the bot is hard-locked to its spawn in LateUpdate, so no spacing here.
        bool isBot = GetComponent<BotController>() != null;
        if (isBot && opponent != null && VRCameraDriver.VRActive &&
            !_isDashing && !_combat.isAttacking && !_combat.IsHurting)
        {
            Vector3 botTarget = new Vector3(
                opponent.transform.position.x,
                transform.position.y,
                opponent.transform.position.z + DesiredDistance
            );
            Vector3 toTarget = botTarget - transform.position;
            toTarget.y = 0;
            if (toTarget.magnitude > 0.05f)
                autoSpacingVelocity = toTarget.normalized * SpacingSpeed * 2f;
        }

        // 5. Visual Cleanup
        if (isLocalPlayer)
        {
            // Hide the mesh for the local player so you don't see the inside of the head
            var renderer = GetComponentInChildren<SkinnedMeshRenderer>();
            if (renderer != null && renderer.enabled)
            {
                renderer.enabled = false;
            }
        }

        // 6. Final Movement Calculation
        _velocityY += Physics.gravity.y * Time.deltaTime;

        // VR: strafe only (left/right). Flat-screen: NO WASD — fighters hold position, only voice
        // dodges move them on X, and home pinning is handled in LateUpdate.
        Vector3 inputMovement = (isLocalPlayer && VRCameraDriver.VRActive)
            ? GameManager.Move.x * transform.right
            : Vector3.zero;

        Vector3 targetVelocity = inputMovement * GameManager.Speed;
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