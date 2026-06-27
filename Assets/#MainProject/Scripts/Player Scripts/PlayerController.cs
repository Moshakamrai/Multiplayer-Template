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

    public Vector3 HomePosition => _homePosition;

    // Temporary position override for the bot's beat run-in. While set, LateUpdate pins to this point
    // instead of home, so the run isn't snapped back. Cleared (ClearApproachOverride) to resume pinning.
    private bool _useApproachOverride;
    private Vector3 _approachPos;
    public void SetApproachOverride(Vector3 pos) { _useApproachOverride = true; _approachPos = pos; }
    public void ClearApproachOverride() { _useApproachOverride = false; }

    private Queue<Vector3> _dashQueue = new Queue<Vector3>();
    private bool _isDashing;
    public bool IsDashing => _isDashing;
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

        // Held stagger (drone segment): the bot does NOT move/rotate/dash at all — it stays frozen on
        // its spawn. Skip the whole movement pipeline so nothing drifts or spins it.
        if (_combat.HeldStaggerActive) { _dashQueue.Clear(); _isDashing = false; return; }

        ProcessDashQueue();
        Movement();
    }

    // Bot auto-spacing DISABLED — both fighters are now hard-pinned to their spawn in LateUpdate, so
    // the bot must never try to "close distance" (it just fought the pin and shoved the bot around).
    private void FixedUpdate() { }

    // Hard-anchor each fighter to its spawn spot — applied AFTER animation root motion every
    // frame, so the body can NEVER drift (no snap, because nothing ever accumulates):
    //   • BOT    — fully pinned (X + Z). It never moves, so the gap stays exactly constant.
    //   • PLAYER — Z locked to spawn (no forward/back drift); X is free so sidestep dodges work.
    // Vertical (Y / gravity) is always left untouched.
    // HARD-ANCHOR both fighters to their spawn spot, EVERY frame, ALWAYS (not just mid-round).
    // Applied in LateUpdate (after animation root motion + the CharacterController.Move in Movement),
    // so nothing — idle/hurt/dash/knockback root motion, gravity drift, bot spacing — can ever walk a
    // fighter off its spawn. The body is pinned on ALL THREE axes; only the rotation is allowed to
    // turn to face the opponent. Dodges still play their lean animation but never relocate the body.
    private void LateUpdate()
    {
        bool isBotOnServer = isServer && !isLocalPlayer;
        if (!(isLocalPlayer || isBotOnServer)) return;
        if (!_hasHome) return;

        // Both the bot AND the human are hard-pinned to their spawn every frame. The player is not
        // allowed to leave their spot — only rotation is permitted. The bot still respects its temporary
        // approach override for beat run-ins.
        bool isBot = GetComponent<BotController>() != null;

        // Full pin to spawn on every axis — UNLESS a temporary approach override is set (the bot's
        // beat run-in drives its position itself; we pin to that point instead of home so the run isn't
        // snapped back every frame).
        transform.position = _useApproachOverride ? _approachPos : _homePosition;

        // Face the opponent (animations can twist the rig; enforce the facing here).
        PlayerController opp = GetOpponent();
        if (opp != null)
        {
            Vector3 dir = opp.transform.position - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.LookRotation(dir);
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
        // DISABLED: the bot is positioned ENTIRELY by the LateUpdate pin / BotBeatApproach override now.
        // Physically Move()-ing it here on knockback shoved it off spawn and (via collider depenetration
        // against the player) leaked cumulative drift. The hurt/knockback ANIMATION still plays; the body
        // stays pin-driven. No physical relocation.
        yield break;
    }

    private void Movement()
    {
        bool isBot = GetComponent<BotController>() != null;
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
            // NEITHER fighter physically relocates on a dash anymore — the dodge ANIMATION + VFX play for
            // feedback, but the body stays pin-driven (LateUpdate). The bot used to Move() its controller
            // here, which depenetrated against the player and leaked cumulative drift off its spawn.
            _dashElapsed += Time.deltaTime;
            playerCollider.enabled = false;
            if (_dashElapsed >= dashDuration) _isDashing = false;
            return;
        }
        else
        {
            playerCollider.enabled = true;
        }

        // 4. Home anchoring is handled in LateUpdate (full pin to spawn, every frame). The bot used to
        //    "auto-space" toward the player here in VR, but that fought the pin and shoved it around —
        //    removed. Both fighters now simply hold their spawn.
        Vector3 autoSpacingVelocity = Vector3.zero;

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

        // NEITHER fighter uses the CharacterController to move anymore. The bot is positioned ENTIRELY
        // by LateUpdate (home pin OR the beat-approach override set by BotBeatApproach). Letting the
        // CharacterController also Move() here caused slow DRIFT: while the bot overlaps the player at the
        // attack point, the controller depenetrates it sideways and accumulates gravity, nudging it off
        // its spawn a little more each return. Pinning via transform.position is the single source of
        // truth, so we skip the controller move for the bot too.
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

    /// Public entry to (re)sync this player's networked name — used by the VR start-menu initials picker.
    public void SetNameNetworked(string playerName)
    {
        if (isServer) PlayerName = playerName;
        else if (isLocalPlayer) SetNameCmd(playerName);
    }

    public PlayerController GetOpponent()
    {
        foreach (var p in GameManager.players) { if (p != null && p != this) return p; }
        return null;
    }
}