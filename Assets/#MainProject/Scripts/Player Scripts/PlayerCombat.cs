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

    [SyncVar] public bool IsParryActive = false;

    private Queue<string> _attackQueue = new Queue<string>();
    public SphereCollider weaponGloveLeft;
    public SphereCollider weaponGloveRight;

    public struct RhythmAction { public string attack; public Vector3 dash; }
    public List<RhythmAction> _comboBuffer = new List<RhythmAction>();

    [Header("VFX Settings")]
    public Renderer playerRenderer;
    public Material flashMaterial;
    public Material _originalMaterial;

    public VoiceProcessor vp;

    private string _pendingAttackTrigger = "";
    private Vector3 _pendingDashDirection = Vector3.zero;

    // --- TIMING FEEDBACK VARIABLES ---
    private string _timingText = "";
    private Color _timingColor = Color.white;
    private float _timingFade = 0f;

    private VoiceCommandManager _vcm;

    [SyncVar] public float lastVocalSpikeTime = -1f; // Timestamp of the loudest peak

    public override void OnStartServer()
    {
        // Bots keep the default 100 HP; real players get 250 HP
        if (GetComponent<BotController>() == null)
            CurrentHealth = 250;
    }

    private void Start()
    {
        if (isLocalPlayer && ShieldText == null) ShieldText = GameObject.Find("ShieldText")?.GetComponent<Text>();
        _vcm = GetComponent<VoiceCommandManager>();
    }

    public void VoiceAttackJab() { if (isLocalPlayer && !IsDead) _attackQueue.Enqueue("Jab"); }
    public void VoiceAttackCross() { if (isLocalPlayer && !IsDead) _attackQueue.Enqueue("Cross"); }
    public void VoiceAttackHook() { if (isLocalPlayer && !IsDead) _attackQueue.Enqueue("Hook"); }
    public void VoiceAttackBlock() { if (isLocalPlayer && !IsDead) _attackQueue.Enqueue("Block"); }

    private void Update()
    {
        if (isLocalPlayer && ShieldText != null) ShieldText.text = CurrentShield.ToString();

        // --- Fade the timing text ---
        if (isLocalPlayer && _timingFade > 0)
        {
            _timingFade -= Time.deltaTime * 1.5f; // Fades out completely in ~0.66 seconds
        }

        // Local Parry Spike Check: Only runs for the local player
        if (isLocalPlayer && !IsDead && !IsHurting)
        {
            CheckLocalParryTiming();
        }

        if (!isLocalPlayer || IsDead || IsHurting) return;
        if (!isAttacking && _attackQueue.Count > 0) StartCoroutine(PerformAttack(_attackQueue.Dequeue()));
    }

    // --- RESTORED ANIMATION EVENT FUNCTIONS ---
    public void StartAttackWindow() { isAttacking = true; }
    public void EndAttackWindow() { isAttacking = false; }

    private void CheckLocalParryTiming()
    {
        // 1. Logic Gate: Only process if a move is actually queued
        string currentMove = _pendingAttackTrigger;
        bool isDashing = (_pendingDashDirection != Vector3.zero);
        if (string.IsNullOrEmpty(currentMove) && !isDashing) return;

        if (vp == null || _vcm == null || RhythmRoundManager.Instance == null) return;

        float currentVol = vp.CurrentRawVolume;
        float threshold = _vcm.parryVolumeThreshold;

        float nextBeat = RhythmRoundManager.Instance.GetNextBeatTime();
        float currentTime = RhythmRoundManager.Instance.GetCurrentTrackTime();
        float timeUntilImpact = nextBeat - currentTime;

        bool vocalSpike = currentVol >= threshold;

        // 2. Window Mapping
        float window = 0.3f;
        if (currentMove == "ParryIntent") window = 0.3f;
        else if (currentMove == "UnbreakablePunch") window = 0.2f;
        else if (currentMove == "Block") window = 0.4f;
        else if (isDashing) window = 0.3f;

        bool isInsideWindow = timeUntilImpact > 0 && timeUntilImpact <= window;

        // 3. Local Confirmation for Parry
        if (vocalSpike && isInsideWindow)
        {
            // If it's a Parry, we tell the server "This is verified" immediately
            if (currentMove == "ParryIntent")
            {
                // We send the currentTime so the server knows exactly when it happened
                CmdConfirmEliteParry(currentTime);
                Debug.Log($"<color=green>VOCAL SUCCESS:</color> Parry (CAGE) verified locally at {timeUntilImpact:F3}s.");

                // Change intent to 'Locked' so we don't spam the server
                _pendingAttackTrigger = "ParryLocked";
            }
            else
            {
                // For regular attacks (like BOOM), just sync the spike time for damage calculation
                CmdRegisterVocalSpike(currentTime);
                Debug.Log($"<color=green>VOCAL SUCCESS:</color> {currentMove} spike registered.");
            }
        }
    }

    [Command]
    void CmdConfirmEliteParry(float spikeTime)
    {
        lastVocalSpikeTime = spikeTime;
        IsParryActive = true; // Set instantly on server

        if (animator != null) animator.Play("Parry");

        TargetAddEnergy(1);
        StartCoroutine(ResetParryFlag());
    }

    [Command]
    void CmdRegisterVocalSpike(float time)
    {
        lastVocalSpikeTime = time;
    }

    [Command]
    void CmdConfirmSuccessfulParry()
    {
        IsParryActive = true;

        if (animator != null)
        {
            // Snapping to the pose in 0.02s for that 'crunchy' pose-to-pose feel
            animator.Play("Parry");
        }

        // Return 1 Energy as a reward for the tight timing
        TargetAddEnergy(1);

        // We keep the hitbox active for 0.3s to match the window
        StartCoroutine(ResetParryFlag());
    }

    IEnumerator ResetParryFlag()
    {
        // Keep it active long enough for the server pulse to see it
        yield return new WaitForSeconds(0.6f);
        IsParryActive = false;
        lastVocalSpikeTime = -1f; // Clear the spike for the next round
    }

    private IEnumerator PerformAttack(string trigger)
    {
        isAttacking = true;
        if (isLocalPlayer && animator != null) animator.Play(trigger, 0, 0f);
        int damageToSet = (trigger == "Hook") ? 25 : (trigger == "Cross") ? 15 : (trigger == "Jab") ? 10 : 0;
        CmdTriggerAttack(trigger, damageToSet);
        yield return new WaitForSeconds(0.1f);
    }

    [TargetRpc]
    public void TargetShowTimingFeedback(string rating)
    {
        _timingText = rating;
        _timingFade = 1.0f; // Reset fade timer to max
        
        if (rating == "EXCELLENT") _timingColor = Color.cyan;
        else if (rating == "GOOD") _timingColor = Color.green;
        else _timingColor = Color.red;
    }
    public void QueueRhythmMove(string attackTrigger, Vector3 dashDir)
    {
        if (IsDead || IsHurting) return;

        if (isLocalPlayer)
        {
            // --- HUMAN PLAYER LOGIC ---
            // If you are a pure Client, update locally for the UI.
            // (If you are the Host, this skips so you don't double-count).
            if (!isServer) QueueLogic(attackTrigger, dashDir); 
            
            // Send the command to the Server.
            CmdQueueRhythmMove(attackTrigger, dashDir); 
        }
        else if (isServer)
        {
            // --- BOT LOGIC ---
            // The Bot lives purely on the Server and isn't a "Local Player".
            // It just needs to drop its moves straight into the logic.
            QueueLogic(attackTrigger, dashDir);
        }
    }

    [Command]
    private void CmdQueueRhythmMove(string attack, Vector3 dash)
    {
        // This ensures the Server copy of the player also has the full buffer
        QueueLogic(attack, dash);

        if (RhythmRoundManager.Instance.IsWindUpActive)
        {
            // Only trigger wind-up for the first move in a chain
            if (_comboBuffer.Count == 1 || RhythmRoundManager.Instance.IsSingleMoveMode())
                TargetTriggerRhythmWindUp(attack, dash);
        }
    }

    // THE ONLY QUEUELOGIC YOU NEED
    private void QueueLogic(string attackTrigger, Vector3 dashDir)
    {
        if (RhythmRoundManager.Instance.IsSingleMoveMode())
        {
            if (!string.IsNullOrEmpty(attackTrigger)) { _pendingAttackTrigger = attackTrigger; _pendingDashDirection = Vector3.zero; }
            if (dashDir != Vector3.zero) { _pendingDashDirection = dashDir; _pendingAttackTrigger = ""; }
        }
        else
        {
            // This is what allows the "Masterpiece" multi-attack
            if (_comboBuffer.Count < RhythmRoundManager.Instance.currentComboCount)
            {
                _comboBuffer.Add(new RhythmAction { attack = attackTrigger, dash = dashDir });
            }
        }
    }

    public RhythmAction PeekNextMove()
    {
        if (RhythmRoundManager.Instance.IsSingleMoveMode()) return new RhythmAction { attack = _pendingAttackTrigger, dash = _pendingDashDirection };
        return (_comboBuffer.Count > 0) ? _comboBuffer[0] : new RhythmAction { attack = "", dash = Vector3.zero };
    }

    [Server]
    public void ConsumeNextMove()
    {
        if (RhythmRoundManager.Instance.IsSingleMoveMode()) { _pendingAttackTrigger = ""; _pendingDashDirection = Vector3.zero; }
        else if (_comboBuffer.Count > 0) _comboBuffer.RemoveAt(0);
    }

    [Server]
    public void ExecuteRhythmWindUp()
    {
        var move = PeekNextMove();
        if (connectionToClient == null) ExecuteMoveEffect(move.attack, move.dash);
        else TargetTriggerRhythmWindUp(move.attack, move.dash);
    }

    [TargetRpc]
    public void TargetTriggerRhythmWindUp(string attack, Vector3 dash)
    {
        ExecuteMoveEffect(attack, dash);
        if (RhythmRoundManager.Instance.IsWindUpActive)
        {
            bool isCurrentBeat = RhythmRoundManager.Instance.IsSingleMoveMode() || _comboBuffer.Count == 1;
            if (!isCurrentBeat) return;
        }
    }

    private void ExecuteMoveEffect(string attack, Vector3 dash)
    {
        if (!string.IsNullOrEmpty(attack))
        {
            // Map the internal logical triggers to the actual Animator state names
            string animToPlay = attack;

            if (attack == "ParryIntent")
            {
                animToPlay = "Parry";
            }
            else if (attack == "UnbreakablePunch")
            {
                // Mapping the "Boom" command logic to your "UpperCut" animation
                animToPlay = "Uppercut";
            }

            if (animator != null)
            {
                // Play the mapped animation state
                animator.Play(animToPlay, 0, 0f);
            }

            if (isLocalPlayer || (isServer && connectionToClient == null))
            {
                StartCoroutine(PerformAttack(attack));
            }
        }

        if (dash != Vector3.zero)
        {
            GetComponent<PlayerController>().ApplyDashExternal(dash);
        }
    }

    [TargetRpc] public void TargetAddEnergy(int amount) { if (isLocalPlayer) GetComponent<PlayerEnergy>().AddBonusEnergy(amount); }
    [Command] void CmdTriggerAttack(string t, int damage) { weaponGloveLeft.GetComponent<HitboxProperties>().currentDamage = damage; weaponGloveRight.GetComponent<HitboxProperties>().currentDamage = damage; RpcTriggerAttack(t); }
    [ClientRpc] void RpcTriggerAttack(string t) { if (isLocalPlayer) return; if (animator != null) animator.SetTrigger(t); }

    [Server]
    public void TakeDamage(int damage)
    {
        if (IsDead) return;
        StartCoroutine(FlashEffectRoutine());
        CurrentHealth -= damage;
        if (CurrentHealth <= 0) StartCoroutine(DelayedKnockout(0f));
        else RpcTriggerHurt("Hurt " + Random.Range(1, 5), 0f);
    }

    [Server] private IEnumerator DelayedKnockout(float delay) { yield return new WaitForSeconds(delay); RpcKnockout(); }
    [ClientRpc] void RpcTriggerHurt(string trigger, float delay) { StartCoroutine(DelayedHurtRoutine(trigger, delay)); }

    private IEnumerator DelayedHurtRoutine(string trigger, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (animator) animator.SetTrigger(trigger);
        if (isLocalPlayer) { _attackQueue.Clear(); _pendingAttackTrigger = ""; _pendingDashDirection = Vector3.zero; isAttacking = false; GetComponent<PlayerController>().InterruptMovement(); StartCoroutine(HurtStunTimer()); }
    }

    public bool HasOpenSlot(bool isMovement)
    {
        if (RhythmRoundManager.Instance != null && RhythmRoundManager.Instance.isRoundActive)
        {
            if (!RhythmRoundManager.Instance.IsSingleMoveMode()) return _comboBuffer.Count < RhythmRoundManager.Instance.currentComboCount;
            return isMovement ? _pendingDashDirection == Vector3.zero : string.IsNullOrEmpty(_pendingAttackTrigger);
        }
        return isMovement || _attackQueue.Count < 2;
    }

    private IEnumerator HurtStunTimer() { IsHurting = true; yield return new WaitForSeconds(0.2f); IsHurting = false; }
    private IEnumerator FlashEffectRoutine() { if (playerRenderer == null || flashMaterial == null) yield break; playerRenderer.material = flashMaterial; yield return new WaitForSeconds(0.1f); playerRenderer.material = _originalMaterial; }
    [ClientRpc] void RpcKnockout() { IsDead = true; if (animator != null) animator.SetTrigger("Knock out"); if (isServer) StartCoroutine(ServerRestartMatchRoutine()); }
    [Server] private IEnumerator ServerRestartMatchRoutine() { yield return new WaitForSeconds(4f); NetworkManager.singleton.ServerChangeScene(SceneManager.GetActiveScene().name); }

    private Texture2D _whiteTexture;

    private void OnGUI()
    {
        if (!isLocalPlayer) return;

        // --- INITIALIZE TEXTURE ---
        if (_whiteTexture == null)
        {
            _whiteTexture = new Texture2D(1, 1);
            _whiteTexture.SetPixel(0, 0, Color.white);
            _whiteTexture.Apply();
        }
        
        PlayerController opponent = GetComponent<PlayerController>().GetOpponent();
        if (opponent != null)
        {
            PlayerCombat oppCombat = opponent.GetComponent<PlayerCombat>();
            if (oppCombat != null)
            {
                float barWidth = 400f;
                float barHeight = 60f; 
                float posX = Screen.width - barWidth - 20f;
                float posY = 20f;

                GUI.color = new Color(0.1f, 0.1f, 0.1f, 1f);
                GUI.DrawTexture(new Rect(posX, posY, barWidth, barHeight), _whiteTexture);

                float healthPercent = (float)oppCombat.CurrentHealth / 100f;
                GUI.color = Color.red;
                GUI.DrawTexture(new Rect(posX + 5, posY + 5, (barWidth - 10) * healthPercent, barHeight - 10), _whiteTexture);

                GUI.color = Color.white;
                GUIStyle nameStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold,
                    fontSize = 24 
                };

                string oppName = string.IsNullOrEmpty(opponent.PlayerName) ? "BOT UNIT" : opponent.PlayerName;
                GUI.Label(new Rect(posX, posY, barWidth, barHeight), $"{oppName}: {oppCombat.CurrentHealth} HP", nameStyle);
            }
        }

        // --- 2. VOLUME DEBUGGER (Bottom Right) ---
        if (vp != null)
        {
            float vol = vp.CurrentRawVolume;
            float thr = (_vcm != null) ? _vcm.parryVolumeThreshold : 0.4f;
            float width = 260f; float height = 140f;
            float pX = Screen.width - width - 20f; float pY = Screen.height - height - 20f;

            GUI.Box(new Rect(pX, pY, width, height), "");
            GUILayout.BeginArea(new Rect(pX + 10f, pY + 10f, width - 20f, height - 20f));
            GUI.color = vol >= thr ? Color.green : Color.yellow;
            GUILayout.Label("<b>--- MIC MONITOR ---</b>");
            GUILayout.HorizontalSlider(vol, 0f, 1f, GUILayout.Width(200));
            GUILayout.Label($"VOL: {vol:F3} / THR: {thr:F3}");
            if (vol >= thr) GUILayout.Label("<color=green>!!! SPIKE DETECTED !!!</color>");
            GUILayout.EndArea();
            GUI.color = Color.white;
        }

        // --- 3. COMBAT QUEUE (Bottom Left) ---
        if (RhythmRoundManager.Instance != null && RhythmRoundManager.Instance.isRoundActive)
        {
            GUILayout.BeginArea(new Rect(20, Screen.height - 300, 350, 280));
            GUIStyle headerStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 20 };
            headerStyle.normal.textColor = Color.green;

            if (RhythmRoundManager.Instance.IsSingleMoveMode())
            {
                GUILayout.Label("LOCKED ACTION:", headerStyle);
                string atk = string.IsNullOrEmpty(_pendingAttackTrigger) ? "None" : _pendingAttackTrigger;
                if (atk == "ParryIntent") GUI.color = Color.cyan;
                GUILayout.Label($"Attack: {atk}", new GUIStyle(GUI.skin.label) { fontSize = 18 });
                GUI.color = Color.white;
            }
            GUILayout.EndArea();
        }
        
        // --- 4. CHAIN ATTACK INPUT LIST (Bottom Left) ---
        if (RhythmRoundManager.Instance != null && RhythmRoundManager.Instance.isRoundActive && !RhythmRoundManager.Instance.IsSingleMoveMode())
        {
            float w = 240f;
            float h = 280f;
            Rect chainRect = new Rect(20, Screen.height - h - 180f, w, h);

            GUI.Box(chainRect, "<b>NEXT COMBO CHAIN</b>");

            GUILayout.BeginArea(new Rect(chainRect.x + 10, chainRect.y + 30, w - 20, h - 40));

            int totalNeeded = RhythmRoundManager.Instance.currentComboCount;

            for (int i = 0; i < totalNeeded; i++)
            {
                if (i < _comboBuffer.Count)
                {
                    var move = _comboBuffer[i];
                    string moveName = string.IsNullOrEmpty(move.attack) ? "DASH" : move.attack;

                    if (moveName == "ParryIntent") moveName = "CAGE";
                    if (moveName == "UnbreakablePunch") moveName = "BOOM";

                    GUI.color = Color.cyan;
                    GUILayout.Box($"{i + 1}. {moveName.ToUpper()}", GUILayout.Height(40));
                }
                else
                {
                    GUI.color = new Color(1, 1, 1, 0.2f);
                    GUILayout.Box($"{i + 1}. [WAITING]", GUILayout.Height(40));
                }

                if (i < totalNeeded - 1) GUILayout.Label("      ▼", GUILayout.Height(10));
            }
            GUI.color = Color.white;
            GUILayout.EndArea();
        }

        // --- 5. TIMING FEEDBACK FLOATER (Center Screen) ---
        if (_timingFade > 0)
        {
            GUIStyle timingStyle = new GUIStyle(GUI.skin.label) 
            { 
                alignment = TextAnchor.MiddleCenter, 
                fontStyle = FontStyle.Bold, 
                fontSize = 42 
            };
            
            float yOffset = Mathf.Lerp(60f, 0f, _timingFade); 
            
            _timingColor.a = _timingFade; 
            timingStyle.normal.textColor = _timingColor;
            
            GUI.Label(new Rect(Screen.width / 2 - 200, Screen.height / 2 - 150 - yOffset, 400, 100), _timingText, timingStyle);
            GUI.color = Color.white; 
        }
    }

    private string GetDirectionName(Vector3 dir)
    {
        if (dir == Vector3.forward) return "Forward"; if (dir == Vector3.back) return "Back";
        if (dir == Vector3.left || dir == new Vector3(-1, 0, 0)) return "Left";
        if (dir == Vector3.right || dir == new Vector3(1, 0, 0)) return "Right"; return dir.ToString();
    }

    [TargetRpc]
    public void TargetPlaySuccessSound(string type)
    {
        if (SoundManagerMain.Instance != null)
            SoundManagerMain.Instance.PlaySuccessSFX(type);
    }

    /// <summary>
    /// Plays a particle from the pool at this player's position on all clients.
    /// Pool names to set up in ParticlePoolManager: "Hit", "Block", "Parry", "Dodge"
    /// </summary>
    [ClientRpc]
    public void RpcPlayCombatParticle(string effectType)
    {
        if (ParticlePoolManager.Instance != null)
            ParticlePoolManager.Instance.PlayParticle(effectType, transform.position);
    }
}