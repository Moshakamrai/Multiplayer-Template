using Mirror;
using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;
using System.IO;

public enum RoundType { SlowRhythm, FastCombo, CustomTrack }

public class RhythmRoundManager : NetworkBehaviour
{
    public static RhythmRoundManager Instance;

    [SyncVar] public RoundType currentType = RoundType.SlowRhythm;
    [SyncVar(hook = nameof(OnRoundStateChanged))] public bool isRoundActive = false;

    [SyncVar] public int currentComboCount = 1;
    [SyncVar] public bool customIsCombo = false;
    [SyncVar] public int currentChainPosition = 0;
    [SyncVar] public float lastBeatFireTime = 0f;
    private int _clusterBeatsLeftToFire = 0;

    [SyncVar] private double _startTime;

    [Header("Animation Sync")]
    public float windUpTime = 0.53f;

    [Header("Custom Tracks")]
    public AudioClip[] availableTracks; // Drag all your MP3s/WAVs here in the Inspector!

    public bool IsWindUpActive { get { return _isWindUpFired; } }
    private bool _isWindUpFired = false;

    // We now use this exact same list for ALL 3 game modes!
    private List<float> _upcomingImpacts = new List<float>();
    private List<int> _clusterSizes = new List<int>();


    

    private struct CombatLogEntry
    {
        public string p1Name;
        public string p1Move;
        public int p1State;
        public int p1Damage;
        public string p2Name;
        public string p2Move;
        public int p2State;
        public int p2Damage;
        public float timeAdded; // NEW: Tracks when this log was created
    }
    private List<CombatLogEntry> combatLogs = new List<CombatLogEntry>();

    [Header("Single Player Settings")]
    public GameObject botPrefab; // Drag your Bot Prefab here in the Inspector
    private GameObject _activeBot;

    // Runtime-loaded clips from SmartBeatMapper file paths
    private Dictionary<string, AudioClip> _runtimeClips = new Dictionary<string, AudioClip>();
    private bool _isLoadingClip = false;
    private string _loadingClipName = "";

    private void Awake() { if (Instance == null) Instance = this; }

    public bool IsSingleMoveMode()
    {
        if (currentType == RoundType.SlowRhythm) return true;
        if (currentType == RoundType.CustomTrack && !customIsCombo) return true;
        return false;
    }

    [Server]
    public void StartSlowRound()
    {
        if (isRoundActive) return;
        currentType = RoundType.SlowRhythm;
        currentComboCount = 1;
        customIsCombo = false;

        _upcomingImpacts.Clear();
        _clusterSizes.Clear();
        _isWindUpFired = false;

        // We want 90 seconds total. 
        // At 4-second intervals, i <= 22 gives us 88 seconds.
        for (int i = 1; i <= 22; i++)
        {
            float t = i * 4.0f;
            _upcomingImpacts.Add(t);
        }

        SetupRound();
    }

    [Server]
    public void StartFastRound()
    {
        if (isRoundActive) return;
        currentType = RoundType.FastCombo;
        _upcomingImpacts.Clear();
        _clusterSizes.Clear();

        // 1. The Intro (Impacts at 8.0, 8.6, 9.2, 9.8) - Wraps up right before 10s
        AddComboWindow(0f, 8f, 4, 0.6f);

        // 2. The 13s Half-Beat Burst (Impacts at 13.0, 13.3, 13.6, 13.9)
        // 4 hits using 0.3f gap for that fast double-time feel
        AddComboWindow(10f, 3f, 4, 0.3f);

        // 3. Engagement Filler (15s to 38s)
        // Single strikes spaced 4 seconds apart to keep the player active
        AddComboWindow(14f, 4f, 1, 0.6f); // Hits at 18.0s
        AddComboWindow(18f, 4f, 1, 0.6f); // Hits at 22.0s
        AddComboWindow(22f, 4f, 1, 0.6f); // Hits at 26.0s
        AddComboWindow(26f, 4f, 1, 0.6f); // Hits at 30.0s
        AddComboWindow(30f, 4f, 1, 0.6f); // Hits at 34.0s

        // 4. The 38.4s BIG DROP
        // Starts exactly at 38.4s and runs 4 heavy hits
        AddComboWindow(34f, 4.4f, 4, 0.6f);

        // 5. Final Burst before the 52s Loop
        // Fast half-beats ending right at 48.9s
        AddComboWindow(41f, 7f, 4, 0.3f);

        currentComboCount = 4; // Ensures the buffer is open
        customIsCombo = true;

        SetupRound();
    }

    // Added 'beatGap' parameter to control how fast the chain executes
    private void AddComboWindow(float startTime, float inputWindow, int hits, float beatGap = 0.6f)
    {
        for (int j = 0; j < hits; j++)
        {
            // Hits fire sequentially based on the specific beat gap
            float t = startTime + inputWindow + (j * beatGap);
            _upcomingImpacts.Add(t);
        }
        for (int j = 0; j < hits; j++) _clusterSizes.Add(hits);
    }

    [Server]
    public void StartCustomRound()
    {
        if (isRoundActive) return;
        currentType = RoundType.CustomTrack;

        string saveKey = "CustomMap_" + BeatAnalyzer.Instance.audioSource.clip.name;
        if (!PlayerPrefs.HasKey(saveKey)) return;

        // Uses our new helper to build the timeline
        LoadCustomMapData();

        BeatAnalyzer.Instance.audioSource.Stop();
        BeatAnalyzer.Instance.audioSource.time = 0f;
        BeatAnalyzer.Instance.audioSource.Play();

        _isWindUpFired = false;
        SetupRound();
    }

    // Replace only this part of SetupRound in RhythmRoundManager.cs
    [Server]
    private void SetupRound()
    {
        EnsureBotExists();

        foreach (var player in GameManager.players)
        {
            if (player != null)
            {
                // Only deal cards to players with a CardManager and NO BotController
                CardManager cm = player.GetComponent<CardManager>();
                if (cm != null && player.GetComponent<BotController>() == null)
                {
                    cm.currentHandIndices.Clear();
                    cm.DealInitialHand(); // Force DrawCard() x4
                    Debug.Log($"<color=green>SERVER:</color> Dealt cards to {player.PlayerName}");
                }
            }
        }

        _startTime = NetworkTime.time + 1.0;
        lastBeatFireTime = 0f;
        currentChainPosition = 0;
        _clusterBeatsLeftToFire = (_clusterSizes.Count > 0) ? _clusterSizes[0] : 1;
        isRoundActive = true;
        RpcClearLogs();
    }

    [Server]
    public void StopRound()
    {
        isRoundActive = false;
        _startTime = 0;
        _upcomingImpacts.Clear();
        _isWindUpFired = false;
        currentChainPosition = 0;
        lastBeatFireTime = 0f;
        _clusterBeatsLeftToFire = 0;

        // Clear player hands so the GUI hides
        foreach (var player in GameManager.players)
        {
            if (player != null)
            {
                CardManager cm = player.GetComponent<CardManager>();
                if (cm != null) cm.currentHandIndices.Clear();
            }
        }
    }

    [ClientRpc] private void RpcClearLogs() { combatLogs.Clear(); }

    private void Update()
    {
        if (!isRoundActive || _startTime == 0) return;

        // --- NEW: DEATH CHECK ---
        // The round now only ends if someone is dead
        bool anyoneDead = false;
        foreach (var p in GameManager.players)
        {
            if (p != null && p.GetComponent<PlayerCombat>().CurrentHealth <= 0)
                anyoneDead = true;
        }

        if (anyoneDead)
        {
            StopRound();
            return;
        }

        if (isServer)
        {
            float currentTime = GetCurrentTrackTime();

            // --- NEW: MUSIC LOOPING LOGIC ---
            // If we run out of impacts but the round is still active, refill the list
            if (_upcomingImpacts.Count == 0)
            {
                StopRound();
                return;
            }

            if (_upcomingImpacts.Count > 0)
            {
                float targetBeat = _upcomingImpacts[0];

                // Wind Up Logic
                if (!_isWindUpFired && currentTime >= targetBeat - windUpTime)
                {
                    _isWindUpFired = true;
                    foreach (var player in GameManager.players)
                    {
                        if (player != null)
                        {
                            BotController bot = player.GetComponent<BotController>();
                            if (bot != null) bot.ThinkNextMove();
                            player.GetComponent<PlayerCombat>().ExecuteRhythmWindUp();
                        }
                    }
                }

                // Impact Logic
                if (currentTime >= targetBeat)
                {
                    _isWindUpFired = false;
                    ExecutePulseImpact();

                    _upcomingImpacts.RemoveAt(0);
                    if (_clusterSizes.Count > 0) _clusterSizes.RemoveAt(0);

                    // Update input timing bar state
                    lastBeatFireTime = targetBeat;
                    _clusterBeatsLeftToFire--;
                    if (_clusterBeatsLeftToFire <= 0)
                    {
                        currentChainPosition = 0;
                        _clusterBeatsLeftToFire = (_clusterSizes.Count > 0) ? _clusterSizes[0] : 0;
                    }
                    else
                    {
                        currentChainPosition++;
                    }

                    if (_clusterSizes.Count > 0)
                    {
                        currentComboCount = _clusterSizes[0];
                        if (currentType == RoundType.CustomTrack)
                            customIsCombo = (currentComboCount > 1);
                    }

                    RpcTriggerHitStop();
                }
            }
        }
    }

    [Server]
    private void LoadCustomMapData()
    {
        string saveKey = "CustomMap_" + BeatAnalyzer.Instance.audioSource.clip.name;
        if (!PlayerPrefs.HasKey(saveKey)) return;

        _upcomingImpacts.Clear();
        _clusterSizes.Clear();
        string rawData = PlayerPrefs.GetString(saveKey);
        string[] rawTimes = rawData.Split('|');

        List<float> loadedTaps = new List<float>();
        foreach (string tStr in rawTimes)
        {
            if (float.TryParse(tStr, out float t)) loadedTaps.Add(t);
        }
        loadedTaps.Sort(); // Ensure perfect chronological order

        int i = 0;
        while (i < loadedTaps.Count)
        {
            int chainCount = 1;

            // Look ahead: Gap must be < 1.4s AND the chain cannot exceed 4 hits
            while (i + chainCount < loadedTaps.Count &&
                  (loadedTaps[i + chainCount] - loadedTaps[i + chainCount - 1]) < 1.4f &&
                  chainCount < 5)
            {
                chainCount++;
            }

            for (int j = 0; j < chainCount; j++)
            {
                _upcomingImpacts.Add(loadedTaps[i + j]);
                _clusterSizes.Add(chainCount);
            }

            i += chainCount; 
        }

        if (_clusterSizes.Count > 0)
        {
            currentComboCount = _clusterSizes[0];
            customIsCombo = (currentComboCount > 1);
        }
    }




   [Server]
    private void ResolveRhythmCombat()
    {
        var playerList = new List<PlayerController>(GameManager.players);
        if (playerList.Count < 2) return;

        PlayerController pc1 = playerList[0];
        PlayerController pc2 = playerList[1];
        PlayerCombat p1 = pc1.GetComponent<PlayerCombat>();
        PlayerCombat p2 = pc2.GetComponent<PlayerCombat>();

        var m1 = p1.PeekNextMove();
        var m2 = p2.PeekNextMove();

        // --- NEW: GRADE TIMING BEFORE DAMAGE ---
        EvaluateAndSendFeedback(p1, m1);
        EvaluateAndSendFeedback(p2, m2);

        float targetBeat = GetNextBeatTime();

        // --- CLOSENESS TIE-BREAKER ---
        bool p1WinsTie = false;
        bool p2WinsTie = false;
        if (m1.attack == m2.attack && !string.IsNullOrEmpty(m1.attack))
        {
            float p1Off = Mathf.Abs(targetBeat - p1.lastVocalSpikeTime);
            float p2Off = Mathf.Abs(targetBeat - p2.lastVocalSpikeTime);
            if (p1Off < p2Off) p1WinsTie = true;
            else if (p2Off < p1Off) p2WinsTie = true;
        }

        // --- BULLETPROOF PROTECTION ---
        bool p1Protected = (m1.attack.Contains("Parry") || m1.attack == "ParryIntent" || m1.attack == "UnbreakablePunch");
        bool p2Protected = (m2.attack.Contains("Parry") || m2.attack == "ParryIntent" || m2.attack == "UnbreakablePunch");

        // --- INTERRUPTION LOGIC ---
        bool p1Interrupted = !p1Protected && (p2WinsTie || (m2.attack == "Jab" && (m1.attack == "Cross" || m1.attack == "Strike" || m1.attack == "Blast" || m1.attack == "Hook")) || ((m2.attack == "Cross" || m2.attack == "Strike" || m2.attack == "Blast") && m1.attack == "Hook"));
        bool p2Interrupted = !p2Protected && (p1WinsTie || (m1.attack == "Jab" && (m2.attack == "Cross" || m2.attack == "Strike" || m2.attack == "Blast" || m2.attack == "Hook")) || ((m1.attack == "Cross" || m1.attack == "Strike" || m1.attack == "Blast") && m2.attack == "Hook"));

        int p1DamageTaken = 0;
        int p2DamageTaken = 0;

        // Penalty only applies if NOT protected
        if (p1Interrupted) { p1.TakeDamage(5); p1DamageTaken += 5; }
        if (p2Interrupted) { p2.TakeDamage(5); p2DamageTaken += 5; }

        // PROCESS ACTUAL HITS
        int p1Result = ProcessDamage(p1, m1, p2, m2, p1Interrupted, out int dmgToP2);
        int p2Result = ProcessDamage(p2, m2, p1, m1, p2Interrupted, out int dmgToP1);

        p2DamageTaken += dmgToP2;
        p1DamageTaken += dmgToP1;

        // Green if Result is 1 (Hit) or -1 (Successful Parry Reflection)
        int p1State = (p1Result == 1 || p1Result == -1) ? 1 : (p1DamageTaken > 0 ? -1 : 0);
        int p2State = (p2Result == 1 || p2Result == -1) ? 1 : (p2DamageTaken > 0 ? -1 : 0);

        RpcLogCombatTrade(pc1.PlayerName, FormatMove(m1), p1State, p1DamageTaken, pc2.PlayerName, FormatMove(m2), p2State, p2DamageTaken);
    }

    [Server]
    private int ProcessDamage(PlayerCombat attacker, PlayerCombat.RhythmAction move, PlayerCombat defender, PlayerCombat.RhythmAction defMove, bool isInterrupted, out int damageDealt)
    {
        damageDealt = 0;
        if (string.IsNullOrEmpty(move.attack) || move.attack == "Block") return 0;

        // --- 1. PARRY REFLECTION ---
        if (defender.IsParryActive)
        {
            if (defender.connectionToClient != null) defender.TargetPlaySuccessSound("Parry");
            defender.RpcPlayCombatParticle("Parry");

            bool isUnbreakable = (move.attack == "UnbreakablePunch");
            int baseRef = isUnbreakable ? 15 : ((move.attack == "Hook") ? 25 : 10);
            Vector3 parryKbDir = (attacker.transform.position - defender.transform.position).normalized;
            attacker.TakeDamage(Mathf.CeilToInt(baseRef * 1.2f), parryKbDir);

            damageDealt = 0;
            return -1;
        }

        // --- 2. MOVEMENT & MITIGATION ---
        bool moveSuccessful = false;
        float blockMitigation = 0f;

        if (defMove.dash != Vector3.zero)
        {
            float dSpike = defender.lastVocalSpikeTime;
            if (dSpike > 0 && (GetNextBeatTime() - dSpike) <= 0.3f)
            {
                moveSuccessful = true;
                if (defender.connectionToClient != null) defender.TargetPlaySuccessSound("Dash");
                defender.RpcPlayCombatParticle("Dodge");
            }
        }

        if (defMove.attack == "Block")
        {
            float bSpike = defender.lastVocalSpikeTime;
            float targetBeat = GetNextBeatTime();
            float offset = Mathf.Abs(targetBeat - bSpike);

            blockMitigation = 0f; 

            if (bSpike > 0 && offset <= 0.4f)
            {
                if (move.attack == "Hook") 
                {
                    // Hook wraps around the guard! Block fails entirely.
                    blockMitigation = 0.0f; 
                }
                else
                {
                    // 100% Mitigation against Jab, Cross, etc.
                    blockMitigation = 1.0f;
                    if (defender.connectionToClient != null) defender.TargetPlaySuccessSound("Block");
                    defender.RpcPlayCombatParticle("Block");
                }
            }
        }

        // --- 3. ATTACK DAMAGE ---
        float targetBeatTime = GetNextBeatTime();
        float atkSpike = attacker.lastVocalSpikeTime;
        bool moveIsUnbreakable = (move.attack == "UnbreakablePunch");
        int finalDmg = moveIsUnbreakable ? 15 : 5;
        float window = moveIsUnbreakable ? 0.2f : 0.3f;

        if (atkSpike > 0 && (targetBeatTime - atkSpike) <= window)
        {
            float bonus = Mathf.Lerp(10, 0, Mathf.Max(0, targetBeatTime - atkSpike) / window);
            finalDmg += Mathf.RoundToInt(bonus);
        }

        // --- 4. HIT DETECTION ---
        bool hits = false;
        if (isInterrupted && !moveIsUnbreakable) hits = false;
        else
        {
            if (moveIsUnbreakable) hits = !moveSuccessful;
            else if (move.attack == "Jab") hits = !moveSuccessful; // Jab misses dodges
            else if (move.attack == "Cross" || move.attack == "Strike" || move.attack == "Blast") hits = true; // Tracks dodges perfectly!
            else if (move.attack == "Hook") hits = !moveSuccessful; // Hook misses dodges
        }

        if (hits)
        {
            float multiplier = 1f - blockMitigation;
            damageDealt = Mathf.RoundToInt(finalDmg * multiplier);

            if (damageDealt > 0)
            {
                // 1. Attacker gets the "Hit" sound
                if (attacker.connectionToClient != null) attacker.TargetPlaySuccessSound("Attack");

                // 2. Defender gets the "Hurt" sound + hit particle
                if (defender.connectionToClient != null) defender.TargetPlaySuccessSound("Hurt");
                defender.RpcPlayCombatParticle("Hit");

                Vector3 kbDir = (defender.transform.position - attacker.transform.position).normalized;
                defender.TakeDamage(damageDealt, kbDir);
                return 1;
            }
        }
        return 0;
    }

    // [Server]
    // private int ProcessDamage(PlayerCombat attacker, PlayerCombat.RhythmAction move, PlayerCombat defender, PlayerCombat.RhythmAction defMove, bool isInterrupted, out int damageDealt)
    // {
    //     damageDealt = 0;
    //     if (string.IsNullOrEmpty(move.attack) || move.attack == "Block") return 0;

    //     // --- 1. PARRY REFLECTION ---
    //     if (defender.IsParryActive)
    //     {
    //         defender.TargetPlaySuccessSound("Parry"); // Calls the Rpc on PlayerCombat

    //         bool isUnbreakable = (move.attack == "UnbreakablePunch");
    //         int baseRef = isUnbreakable ? 15 : ((move.attack == "Hook") ? 25 : 10);
    //         attacker.TakeDamage(Mathf.CeilToInt(baseRef * 1.2f));

    //         damageDealt = 0;
    //         return -1;
    //     }

    //     // --- 2. MOVEMENT & MITIGATION ---
    //     bool moveSuccessful = false;
    //     float blockMitigation = 0f;

    //     if (defMove.dash != Vector3.zero)
    //     {
    //         float dSpike = defender.lastVocalSpikeTime;
    //         if (dSpike > 0 && (GetNextBeatTime() - dSpike) <= 0.3f)
    //         {
    //             moveSuccessful = true;
    //             if (defender.connectionToClient != null)
    //             {
    //                 defender.TargetPlaySuccessSound("Dash"); // Or "Block", "Parry", etc.
    //             }
    //         }
    //     }

    //     if (defMove.attack == "Block")
    //     {
    //         float bSpike = defender.lastVocalSpikeTime;
    //         float targetBeat = GetNextBeatTime();
    //         float offset = Mathf.Abs(targetBeat - bSpike);

    //         blockMitigation = 0.7f; // Always 70% min

    //         if (bSpike > 0 && offset <= 0.4f)
    //         {
    //             blockMitigation = 0.7f + Mathf.Lerp(0.3f, 0f, offset / 0.4f);
    //             defender.TargetPlaySuccessSound("Block"); // Successful timed block sound
    //         }
    //     }

    //     // --- 3. ATTACK DAMAGE ---
    //     float targetBeatTime = GetNextBeatTime();
    //     float atkSpike = attacker.lastVocalSpikeTime;
    //     bool moveIsUnbreakable = (move.attack == "UnbreakablePunch");
    //     int finalDmg = moveIsUnbreakable ? 15 : 5;
    //     float window = moveIsUnbreakable ? 0.2f : 0.3f;

    //     if (atkSpike > 0 && (targetBeatTime - atkSpike) <= window)
    //     {
    //         float bonus = Mathf.Lerp(10, 0, Mathf.Max(0, targetBeatTime - atkSpike) / window);
    //         finalDmg += Mathf.RoundToInt(bonus);
    //     }

    //     // --- 4. HIT DETECTION ---
    //     bool hits = false;
    //     if (isInterrupted && !moveIsUnbreakable) hits = false;
    //     else
    //     {
    //         if (moveIsUnbreakable) hits = !moveSuccessful;
    //         else if (move.attack == "Jab") hits = !moveSuccessful;
    //         else if (move.attack == "Cross") hits = true;
    //         else if (move.attack == "Hook") hits = !moveSuccessful;
    //     }

    //     if (hits)
    //     {
    //         float multiplier = 1f - blockMitigation;
    //         damageDealt = Mathf.RoundToInt(finalDmg * multiplier);

    //         if (damageDealt > 0)
    //         {
    //             // 1. Attacker gets the "Hit" sound
    //             if (attacker.connectionToClient != null) attacker.TargetPlaySuccessSound("Attack");

    //             // 2. Defender gets the "Hurt" sound
    //             if (defender.connectionToClient != null) defender.TargetPlaySuccessSound("Hurt");

    //             defender.TakeDamage(damageDealt);
    //             return 1;
    //         }
    //     }
    //     return 0;
    // }
    private string FormatMove(PlayerCombat.RhythmAction move)
    {
        if (!string.IsNullOrEmpty(move.attack))
        {
            if (move.attack == "ParryIntent") return "PARRY (CAGE)"; // Shows both in the log
            if (move.attack == "UnbreakablePunch") return "BOOM";
            return move.attack;
        }
        return (move.dash != Vector3.zero) ? "DODGE" : "IDLE";
    }

    [ClientRpc]
    private void RpcLogCombatTrade(string p1Name, string p1Move, int p1State, int p1Dmg, string p2Name, string p2Move, int p2State, int p2Dmg)
    {
        combatLogs.Add(new CombatLogEntry
        {
            p1Name = string.IsNullOrEmpty(p1Name) ? "Player 1" : p1Name,
            p1Move = p1Move,
            p1State = p1State,
            p1Damage = p1Dmg,
            p2Name = string.IsNullOrEmpty(p2Name) ? "Player 2" : p2Name,
            p2Move = p2Move,
            p2State = p2State,
            p2Damage = p2Dmg,
            timeAdded = Time.time // Stamps the exact moment it appeared
        });
    }

    private void ExecutePulseImpact()
    {
        // 1. Resolve combat logic for players
        if (GameManager.players.Count >= 2) ResolveRhythmCombat();

        foreach (var player in GameManager.players)
        {
            if (player != null)
            {
                PlayerCombat pc = player.GetComponent<PlayerCombat>();
                pc.ConsumeNextMove();

                // 2. HARD RESET: Clear all flags so lag cannot roll into the next beat
                // In chain mode, preserve the spike so hits 2-4 still grade correctly
                if (!customIsCombo)
                    pc.lastVocalSpikeTime = -1f;
                pc.IsParryActive = false;

                // 3. REFILL HANDS: Draw cards until the hand is full (4 cards).
                // Skip entirely during combo mode — the combo hand is managed by CardManager.Update()
                // and DrawCard() is blocked, so looping here would spin forever.
                if (isServer)
                {
                    CardManager cm = player.GetComponent<CardManager>();
                    if (cm != null && !cm.IsComboHandActive)
                    {
                        while (cm.currentHandIndices.Count < 4)
                            cm.DrawCard();
                    }
                    else if (cm != null && cm.IsComboHandActive)
                    {
                        Debug.Log($"<color=yellow>[RhythmRoundManager]</color> Skipping card refill for {player.PlayerName} — combo hand is active.");
                    }
                }
            }
        }
    }

    [Server]
    private void EvaluateAndSendFeedback(PlayerCombat pc, PlayerCombat.RhythmAction move)
    {
        // Don't grade them if they didn't do anything
        if (string.IsNullOrEmpty(move.attack) && move.dash == Vector3.zero) return;

        // Bots don't need UI feedback, so we skip if there is no client connection
        if (pc.connectionToClient == null) return;

        float targetBeat = GetNextBeatTime();

        // --- CHAIN MODE: one shout covers the whole cluster ---
        // Hits 2-4 will have a growing offset from the beat, but the spike is still valid.
        // Give GOOD for any existing spike; BAD only if there is no spike at all.
        if (customIsCombo)
        {
            if (pc.lastVocalSpikeTime <= 0)
            {
                Debug.Log($"<color=orange>[TIMING]</color> {pc.name} - CHAIN | No Vocal Spike -> BAD");
                pc.TargetShowTimingFeedback("BAD");
                return;
            }
            float chainOffset = Mathf.Abs(targetBeat - pc.lastVocalSpikeTime);
            string chainRating = (chainOffset <= 0.1f) ? "EXCELLENT" : "GOOD";
            Debug.Log($"<color=cyan>[TIMING EVAL]</color> {pc.name} | CHAIN Beat: {targetBeat:F3}s | Spike: {pc.lastVocalSpikeTime:F3}s | Offset: {chainOffset:F3}s => <color=yellow>{chainRating}</color>");
            pc.TargetShowTimingFeedback(chainRating);
            return;
        }

        // --- SINGLE MODE: original narrow-window logic ---
        if (pc.lastVocalSpikeTime <= 0)
        {
            Debug.Log($"<color=orange>[TIMING]</color> {pc.name} - Move: {(string.IsNullOrEmpty(move.attack) ? "DODGE" : move.attack)} | No Vocal Spike Detected -> BAD");
            pc.TargetShowTimingFeedback("BAD");
            return;
        }

        float offset = Mathf.Abs(targetBeat - pc.lastVocalSpikeTime);

        float window = 0.3f;
        if (move.attack == "Block") window = 0.4f;
        else if (move.attack == "UnbreakablePunch") window = 0.2f;

        string rating = "BAD";
        if (offset <= 0.1f) rating = "EXCELLENT";
        else if (offset <= window) rating = "GOOD";

        Debug.Log($"<color=cyan>[TIMING EVAL]</color> {pc.name} | Beat: {targetBeat:F3}s | Voice Spike: {pc.lastVocalSpikeTime:F3}s | Offset: {offset:F3}s | Window: {window}s => <color=yellow>{rating}</color>");
        pc.TargetShowTimingFeedback(rating);
    }

    [ClientRpc] private void RpcTriggerHitStop() { StartCoroutine(HitStopRoutine()); if (CameraShake.Instance != null) CameraShake.Instance.Shake(0.1f, 0.07f); }
    private IEnumerator HitStopRoutine() { Time.timeScale = 0.05f; yield return new WaitForSecondsRealtime(0.06f); Time.timeScale = 1.0f; }

    void OnRoundStateChanged(bool oldVal, bool newVal) { if (BeatAnalyzer.Instance != null && BeatAnalyzer.Instance.audioSource != null && currentType != RoundType.CustomTrack) { if (newVal) BeatAnalyzer.Instance.audioSource.Play(); else BeatAnalyzer.Instance.audioSource.Stop(); } }

   private void OnGUI()
    {
        // --- 1. SERVER CONTROLS (Top Left) ---
        if (NetworkServer.active && isServer)
        {
            // Increased the height to 500 to fit multiple track buttons
            GUILayout.BeginArea(new Rect(10, 10, 220, 500));
            if (!isRoundActive)
            {
                GUIStyle instrStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 13,
                    fontStyle = FontStyle.Italic,
                    wordWrap = true,
                    alignment = TextAnchor.MiddleCenter
                };
                instrStyle.normal.textColor = new Color(0.8f, 0.8f, 0.8f, 1f);
                GUILayout.Label("Select a track or mode\nbelow to start the fight", instrStyle, GUILayout.Height(36));
                GUILayout.Space(4);

                GUI.color = Color.cyan; if (GUILayout.Button("START SLOW ROUND", GUILayout.Height(40))) StartSlowRound();
                GUI.color = Color.magenta; if (GUILayout.Button("START FAST ROUND", GUILayout.Height(40))) StartFastRound();
                GUILayout.Space(10);

                // --- DYNAMIC CUSTOM MAP LOADER ---
                // Collect all map names: from SmartBeatMapper registry + Inspector array
                var allMapNames = new HashSet<string>();

                string registry = PlayerPrefs.GetString("CustomMapRegistry", "");
                if (!string.IsNullOrEmpty(registry))
                    foreach (string n in registry.Split('|'))
                        if (!string.IsNullOrEmpty(n) && PlayerPrefs.HasKey("CustomMap_" + n))
                            allMapNames.Add(n);

                if (availableTracks != null)
                    foreach (AudioClip t in availableTracks)
                        if (t != null && PlayerPrefs.HasKey("CustomMap_" + t.name))
                            allMapNames.Add(t.name);

                if (allMapNames.Count > 0)
                {
                    GUILayout.Label("<b>--- CUSTOM MAPS ---</b>", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, richText = true });

                    foreach (string mapName in allMapNames)
                    {
                        // Prefer Inspector asset, then runtime-cached clip
                        AudioClip clip = null;
                        if (availableTracks != null)
                            foreach (AudioClip t in availableTracks)
                                if (t != null && t.name == mapName) { clip = t; break; }
                        if (clip == null && _runtimeClips.ContainsKey(mapName))
                            clip = _runtimeClips[mapName];

                        bool hasPath  = PlayerPrefs.HasKey("CustomMapPath_" + mapName);
                        bool loading  = _isLoadingClip && _loadingClipName == mapName;

                        if (loading)
                        {
                            GUI.color = Color.yellow;
                            GUILayout.Box($"LOADING: {mapName}...", GUILayout.Height(45));
                        }
                        else if (clip != null || hasPath)
                        {
                            GUI.color = Color.green;
                            if (GUILayout.Button($"PLAY: {mapName}", GUILayout.Height(45)))
                            {
                                if (BeatAnalyzer.Instance != null && BeatAnalyzer.Instance.audioSource != null)
                                {
                                    if (clip != null)
                                    {
                                        BeatAnalyzer.Instance.audioSource.clip = clip;
                                        StartCustomRound();
                                    }
                                    else
                                    {
                                        StartCoroutine(LoadClipThenStartRound(mapName, PlayerPrefs.GetString("CustomMapPath_" + mapName)));
                                    }
                                }
                            }
                        }
                        GUILayout.Space(5);
                    }
                }
                else
                {
                    GUI.color = Color.red;
                    GUILayout.Box("NO MAPS FOUND.\nUse SmartBeatMapper to create one.", GUILayout.Height(60));
                }
                GUI.color = Color.white;
            }
            else
            {
                if (GUILayout.Button("STOP ROUND", GUILayout.Height(40))) StopRound();
                GUILayout.Label($"ACTIVE: {currentType}", GUI.skin.box);
            }
            GUILayout.EndArea();
        }

        // --- 2. PLAYER HP (Center Left) ---
        GUILayout.BeginArea(new Rect(10, Screen.height / 2 - 100, 250, 200));
        foreach (var p in GameManager.players)
        {
            if (p != null)
            {
                PlayerCombat pc = p.GetComponent<PlayerCombat>();
                GUIStyle healthStyle = new GUIStyle(GUI.skin.box) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
                healthStyle.normal.textColor = pc.CurrentHealth <= pc.MaxHealth * 0.3f ? Color.red : Color.green;
                GUILayout.Label($"{p.PlayerName} Health: {pc.CurrentHealth}", healthStyle, GUILayout.Height(40));
            }
        }
        GUILayout.EndArea();

        // --- 3. THE MASTERPIECE TIMER (Top Center) ---
        if (isRoundActive && _startTime != 0)
        {
            float elapsed = (float)(NetworkTime.time - _startTime);
            GUIStyle style = new GUIStyle(GUI.skin.label) { fontSize = 24, alignment = TextAnchor.MiddleCenter };

            if (elapsed < 0)
            {
                style.normal.textColor = Color.yellow;
                GUI.Label(new Rect(Screen.width / 2 - 125, 50, 250, 70), "READY?", style);
            }
            else if (currentType == RoundType.CustomTrack || currentType == RoundType.FastCombo)
            {
                if (_upcomingImpacts.Count > 0)
                {
                    float trackTime = GetCurrentTrackTime();
                    float timeToNextImpact = _upcomingImpacts[0] - trackTime;
                    bool isAttacking = timeToNextImpact < (currentComboCount * 0.8f);

                    if (isAttacking)
                    {
                        style.normal.textColor = Color.red;
                        GUI.Label(new Rect(Screen.width / 2 - 150, 50, 300, 120), $"DANGER\nATTACK CHAIN ACTIVE\n{currentComboCount}x HITS", style);
                    }
                    else
                    {
                        style.normal.textColor = Color.cyan;
                        string modeText = (currentComboCount > 1) ? $"CHAIN ({currentComboCount}x)" : "SINGLE";
                        GUI.Label(new Rect(Screen.width / 2 - 150, 50, 300, 120), $"{modeText}\nTHINK TIME\n{timeToNextImpact:F1}s", style);
                    }
                }
                else { GUI.Label(new Rect(Screen.width / 2 - 125, 50, 250, 70), "FINISHING...", style); }
            }
            else // Standard Slow Rhythm
            {
                float interval = 4.0f;
                float timer = elapsed % interval;
                style.normal.textColor = (timer > (interval - 1.5f)) ? Color.red : Color.white;
                GUI.Label(new Rect(Screen.width / 2 - 125, 50, 250, 100), $"WINDOW\n{timer:F1}s / {interval}s", style);
            }
        }

        // --- 4. COMBAT LOG (Right Side) ---
        if (combatLogs.Count > 0)
        {
            GUILayout.BeginArea(new Rect(Screen.width - 550, 100, 530, 600));
            GUIStyle logStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            GUIStyle boxStyle = new GUIStyle(GUI.skin.box);
            GUILayout.Label("COMBAT LOG", logStyle); GUILayout.Space(5);

            // Show the last 10 entries to keep it clean
            int start = Mathf.Max(0, combatLogs.Count - 10);
            for (int i = start; i < combatLogs.Count; i++)
            {
                var log = combatLogs[i];
                GUILayout.BeginHorizontal(boxStyle);
                string p1DmgStr = log.p1Damage > 0 ? $" (-{log.p1Damage} HP)" : "";
                string p2DmgStr = log.p2Damage > 0 ? $" (-{log.p2Damage} HP)" : "";

                logStyle.normal.textColor = GetStateColor(log.p1State);
                GUILayout.Label($"{log.p1Name}: {log.p1Move}{p1DmgStr}", logStyle, GUILayout.Width(230));

                logStyle.normal.textColor = Color.white;
                GUILayout.Label(" vs ", logStyle, GUILayout.Width(40));

                logStyle.normal.textColor = GetStateColor(log.p2State);
                GUILayout.Label($"{log.p2Name}: {log.p2Move}{p2DmgStr}", logStyle, GUILayout.Width(230));
                GUILayout.EndHorizontal(); GUILayout.Space(2);
            }
            GUILayout.EndArea();
            GUI.color = Color.white;
        }
        DrawBackButton();
    }

    private void DrawBackButton()
    {
        // Cursor hint — always visible in-game
        GUIStyle hintStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            fontStyle = FontStyle.Italic,
            alignment = TextAnchor.MiddleLeft
        };
        hintStyle.normal.textColor = new Color(0.75f, 0.75f, 0.75f, 1f);
        GUI.Label(new Rect(20f, Screen.height - 80f, 260f, 22f), "Hold [Tab] to free cursor", hintStyle);

        if (isRoundActive) return;

        if (GUI.Button(new Rect(20f, Screen.height - 55f, 160f, 40f), "← BACK TO MENU"))
        {
            if (NetworkServer.active)
                NetworkManager.singleton.StopHost();
            else
                NetworkManager.singleton.StopClient();
        }
    }

    private Color GetStateColor(int state) { if (state == 1) return Color.green; if (state == -1) return Color.red; return Color.white; }
    // Replace only the EnsureBotExists function
    [Server]
    private void EnsureBotExists()
    {
        if (GameManager.players.Count == 1 && _activeBot == null)
        {
            Vector3 spawnPos = new Vector3(0, 0, 5);
            _activeBot = Instantiate(botPrefab, spawnPos, Quaternion.identity);

            NetworkServer.Spawn(_activeBot);

            // Use the new public wrapper to signal the bot is ready
            _activeBot.GetComponent<PlayerController>().SetReady(true);
        }
    }

    // Add these to RhythmRoundManager.cs
    public float GetNextBeatTime()
    {
        return (_upcomingImpacts.Count > 0) ? _upcomingImpacts[0] : 0f;
    }

    public float GetCurrentTrackTime()
    {
        return (currentType == RoundType.CustomTrack) ? BeatAnalyzer.Instance.audioSource.time : (float)(NetworkTime.time - _startTime);
    }

    public int GetNextClusterSize() => _clusterSizes.Count > 0 ? _clusterSizes[0] : 1;

    [TargetRpc]
    public void TargetPlaySuccessSound(string type)
    {
        if (SoundManagerMain.Instance != null)
            SoundManagerMain.Instance.PlaySuccessSFX(type);
    }

    private IEnumerator LoadClipThenStartRound(string clipName, string filePath)
    {
        _isLoadingClip = true;
        _loadingClipName = clipName;

        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        AudioType audioType = ext switch
        {
            ".mp3"            => AudioType.MPEG,
            ".wav"            => AudioType.WAV,
            ".ogg"            => AudioType.OGGVORBIS,
            ".aiff" or ".aif" => AudioType.AIFF,
            _                 => AudioType.UNKNOWN
        };

        if (audioType == AudioType.UNKNOWN)
        {
            Debug.LogWarning($"[RhythmRoundManager] Unsupported audio format for: {filePath}");
            _isLoadingClip = false;
            _loadingClipName = "";
            yield break;
        }

        string url = new System.Uri(filePath).AbsoluteUri;
        using UnityWebRequest req = UnityWebRequestMultimedia.GetAudioClip(url, audioType);
        yield return req.SendWebRequest();

        if (req.result == UnityWebRequest.Result.Success)
        {
            AudioClip clip = DownloadHandlerAudioClip.GetContent(req);
            clip.name = clipName;
            _runtimeClips[clipName] = clip;

            if (BeatAnalyzer.Instance != null && BeatAnalyzer.Instance.audioSource != null)
            {
                BeatAnalyzer.Instance.audioSource.clip = clip;
                StartCustomRound();
            }
        }
        else
        {
            Debug.LogWarning($"[RhythmRoundManager] Failed to load clip '{clipName}': {req.error}");
        }

        _isLoadingClip = false;
        _loadingClipName = "";
    }
}