using Mirror;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public enum RoundType { SlowRhythm, FastCombo, CustomTrack }

public class RhythmRoundManager : NetworkBehaviour
{
    public static RhythmRoundManager Instance;
    
    [SyncVar] public RoundType currentType = RoundType.SlowRhythm;
    [SyncVar(hook = nameof(OnRoundStateChanged))] public bool isRoundActive = false;
    
    [SyncVar] public int currentComboCount = 1;
    [SyncVar] public bool customIsCombo = false; 

    [SyncVar] private double _startTime; 

    [Header("Animation Sync")]
    public float windUpTime = 0.53f; 
    
    public bool IsWindUpActive { get { return _isWindUpFired; } }
    private bool _isWindUpFired = false;

    // We now use this exact same list for ALL 3 game modes!
    private List<float> _upcomingImpacts = new List<float>();
    private List<int> _clusterSizes = new List<int>(); 
    
    // NEW: Tracks the very last beat to know when to cleanly end standard rounds
    private float _finalStandardBeat = 0f; 

    private struct CombatLogEntry { public string p1Name; public string p1Move; public int p1State; public int p1Damage; public string p2Name; public string p2Move; public int p2State; public int p2Damage; }
    private List<CombatLogEntry> combatLogs = new List<CombatLogEntry>();

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

        // Automatically map out 6 punches, exactly 4 seconds apart
        for (int i = 1; i <= 6; i++) 
        {
            float t = i * 4.0f;
            _upcomingImpacts.Add(t);
            _finalStandardBeat = t;
        }
        
        SetupRound(); 
    }

    [Server] 
    public void StartFastRound() 
    { 
        if (isRoundActive) return; 
        currentType = RoundType.FastCombo; 
        currentComboCount = 4; 
        customIsCombo = true; 
        
        _upcomingImpacts.Clear();
        _clusterSizes.Clear();
        _isWindUpFired = false;

        // Map out 3 windows (8s, 16s, 24s). 
        for (int i = 1; i <= 3; i++)
        {
            float baseTime = i * 8.0f;
            for (int j = 0; j < 4; j++) 
            {
                // INCREASED FROM 0.6f TO 1.2f!
                // Now the animations have exactly 1.2 seconds to finish before the next punch fires.
                float t = baseTime + (j * 1.2f);
                _upcomingImpacts.Add(t);
                _finalStandardBeat = t;
            }
        }
        
        SetupRound(); 
    }
    
    [Server] 
    public void StartCustomRound() 
    { 
        if (isRoundActive || !BeatAnalyzer.Instance.isAnalyzed) return; 
        currentType = RoundType.CustomTrack; 
        
        List<float> allBeats = BeatAnalyzer.Instance.GetAllActionTriggers();
        _upcomingImpacts.Clear();
        _clusterSizes.Clear();
        _isWindUpFired = false;
        
        int tempComboSize = 1;

        for (int i = 0; i < allBeats.Count; i++)
        {
            if (i == 0) { _upcomingImpacts.Add(allBeats[i]); tempComboSize = 1; }
            else
            {
                if (allBeats[i] - allBeats[i - 1] < 2.0f) tempComboSize++;
                else
                {
                    _clusterSizes.Add(tempComboSize);
                    _upcomingImpacts.Add(allBeats[i]); 
                    tempComboSize = 1;
                }
            }
        }
        if (allBeats.Count > 0) _clusterSizes.Add(tempComboSize);

        for (int i = 0; i < _upcomingImpacts.Count; i++)
        {
            float prepTime = (i == 0) ? _upcomingImpacts[i] : (_upcomingImpacts[i] - _upcomingImpacts[i - 1]);
            float reqPrep = _clusterSizes[i] * 1.2f; 
            
            if (prepTime < reqPrep && _clusterSizes[i] > 1)
            {
                int maxAllowed = Mathf.Max(1, Mathf.FloorToInt(prepTime / 1.2f));
                _clusterSizes[i] = Mathf.Min(_clusterSizes[i], maxAllowed);
            }
            if (_clusterSizes[i] > 4) _clusterSizes[i] = 4; 
        }

        if (_upcomingImpacts.Count > 0) 
        {
            currentComboCount = _clusterSizes[0];
            customIsCombo = (currentComboCount > 1);
        }

        BeatAnalyzer.Instance.audioSource.Stop();
        BeatAnalyzer.Instance.audioSource.time = 0f;
        BeatAnalyzer.Instance.audioSource.Play();
        
        SetupRound(); 
    }

    [Server] private void SetupRound() 
    { 
        _startTime = NetworkTime.time + 1.0; 
        isRoundActive = true; 
        RpcClearLogs(); 
    }
    
    [Server] public void StopRound() { isRoundActive = false; _startTime = 0; _upcomingImpacts.Clear(); _isWindUpFired = false; }

    [ClientRpc] private void RpcClearLogs() { combatLogs.Clear(); }

    private void Update()
    {
        if (!isRoundActive || _startTime == 0) return;
        double elapsed = NetworkTime.time - _startTime;
        if (elapsed < 0) return;

        if (isServer)
        {
            // THE UNIFIED TIME ENGINE: Custom uses track time, Standard uses pure math time
            float currentTime = (currentType == RoundType.CustomTrack) ? BeatAnalyzer.Instance.audioSource.time : (float)elapsed;

            if (_upcomingImpacts.Count > 0)
            {
                float targetBeat = _upcomingImpacts[0];

                // --- PHASE 1: THE WIND UP ---
                if (!_isWindUpFired && currentTime >= targetBeat - windUpTime)
                {
                    _isWindUpFired = true;
                    foreach (var player in GameManager.players) { if (player != null) player.GetComponent<PlayerCombat>().ExecuteRhythmWindUp(); }
                }

                // --- PHASE 2: THE IMPACT ---
                if (currentTime >= targetBeat)
                {
                    _isWindUpFired = false;
                    _upcomingImpacts.RemoveAt(0); 
                    
                    if (_clusterSizes.Count > 0) _clusterSizes.RemoveAt(0); 

                    ExecutePulseImpact();
                    RpcTriggerHitStop(); 

                    // Only dynamically scale the UI if it's a Custom Track, otherwise leave it locked at 4
                    if (currentType == RoundType.CustomTrack && _upcomingImpacts.Count > 0) 
                    {
                        currentComboCount = _clusterSizes[0];
                        customIsCombo = (currentComboCount > 1);
                    }
                }
            }
            else
            {
                // Graceful Round Ending
                if (currentType == RoundType.CustomTrack)
                {
                    if (!BeatAnalyzer.Instance.audioSource.isPlaying) StopRound();
                }
                else
                {
                    // For standard modes, give the final hit 1.5s to finish animating before cutting the round
                    if (currentTime >= _finalStandardBeat + 1.5f) StopRound();
                }
            }
        }
    }

    [Server]
    private void ResolveRhythmCombat()
    {
        var playerList = new List<PlayerController>(GameManager.players);
        if (playerList.Count < 2) return;

        PlayerController pc1 = playerList[0]; PlayerController pc2 = playerList[1];
        PlayerCombat p1 = pc1.GetComponent<PlayerCombat>(); PlayerCombat p2 = pc2.GetComponent<PlayerCombat>();
        
        var m1 = p1.PeekNextMove(); 
        var m2 = p2.PeekNextMove();

        bool p1Interrupted = (m2.attack == "Jab" && (m1.attack == "Cross" || m1.attack == "Hook")) || (m2.attack == "Cross" && m1.attack == "Hook");
        bool p2Interrupted = (m1.attack == "Jab" && (m2.attack == "Cross" || m2.attack == "Hook")) || (m1.attack == "Cross" && m2.attack == "Hook");

        int p1DamageTaken = 0;
        int p2DamageTaken = 0;

        if (p1Interrupted) { p1.TakeDamage(5); p1DamageTaken += 5; } 
        if (p2Interrupted) { p2.TakeDamage(5); p2DamageTaken += 5; }

        int p1Result = ProcessDamage(p1, m1, p2, m2, p1Interrupted, out int dmgToP2); 
        int p2Result = ProcessDamage(p2, m2, p1, m1, p2Interrupted, out int dmgToP1);

        p2DamageTaken += dmgToP2;
        p1DamageTaken += dmgToP1;

        int p1State = 0; if (p1Interrupted || p2Result == 1) p1State = -1; else if (p1Result == 1 || p2Result == -1) p1State = 1; 
        int p2State = 0; if (p2Interrupted || p1Result == 1) p2State = -1; else if (p2Result == 1 || p1Result == -1) p2State = 1; 

        RpcLogCombatTrade(pc1.PlayerName, FormatMove(m1), p1State, p1DamageTaken, pc2.PlayerName, FormatMove(m2), p2State, p2DamageTaken);
    }

    [Server]
    private int ProcessDamage(PlayerCombat attacker, PlayerCombat.RhythmAction move, PlayerCombat defender, PlayerCombat.RhythmAction defMove, bool isInterrupted, out int damageDealt)
    {
        damageDealt = 0;
        if (isInterrupted || string.IsNullOrEmpty(move.attack) || move.attack == "Block") return 0;
        
        bool hits = false; 
        if (move.attack == "Jab") { damageDealt = 10; hits = (defMove.dash == Vector3.zero && defMove.attack != "Block"); }
        else if (move.attack == "Cross") { damageDealt = 15; hits = (defMove.attack != "Block"); }
        else if (move.attack == "Hook") { damageDealt = 25; hits = (defMove.dash == Vector3.zero); }

        if (hits) { defender.TakeDamage(damageDealt); if (move.attack == "Hook") attacker.TargetAddEnergy(2); return 1; }
        else { damageDealt = 0; if (defMove.dash != Vector3.zero) { defender.TargetAddEnergy(1); return -1; } return 0; }
    }

    private string FormatMove(PlayerCombat.RhythmAction move)
    {
        if (!string.IsNullOrEmpty(move.attack)) return move.attack;
        if (move.dash == Vector3.left || move.dash == new Vector3(-1, 0, 0)) return "Dodge Left";
        if (move.dash == Vector3.right || move.dash == new Vector3(1, 0, 0)) return "Dodge Right";
        return "Idle";
    }

    [ClientRpc] 
    private void RpcLogCombatTrade(string p1Name, string p1Move, int p1State, int p1Dmg, string p2Name, string p2Move, int p2State, int p2Dmg) 
    { 
        combatLogs.Add(new CombatLogEntry { 
            p1Name = string.IsNullOrEmpty(p1Name) ? "Player 1" : p1Name, p1Move = p1Move, p1State = p1State, p1Damage = p1Dmg,
            p2Name = string.IsNullOrEmpty(p2Name) ? "Player 2" : p2Name, p2Move = p2Move, p2State = p2State, p2Damage = p2Dmg
        }); 
    }
    
    private void ExecutePulseImpact() 
    { 
        if (GameManager.players.Count >= 2) ResolveRhythmCombat(); 
        foreach (var player in GameManager.players) { if (player != null) player.GetComponent<PlayerCombat>().ConsumeNextMove(); } 
    }

    [ClientRpc] private void RpcTriggerHitStop() { StartCoroutine(HitStopRoutine()); }
    private IEnumerator HitStopRoutine() { Time.timeScale = 0.05f; yield return new WaitForSecondsRealtime(0.06f); Time.timeScale = 1.0f; }

    void OnRoundStateChanged(bool oldVal, bool newVal) { if (BeatAnalyzer.Instance != null && BeatAnalyzer.Instance.audioSource != null && currentType != RoundType.CustomTrack) { if (newVal) BeatAnalyzer.Instance.audioSource.Play(); else BeatAnalyzer.Instance.audioSource.Stop(); } }

    private void OnGUI()
    {
        if (NetworkServer.active && isServer)
        {
            GUILayout.BeginArea(new Rect(10, 10, 220, 300));
            if (!isRoundActive)
            {
                GUI.color = Color.cyan; if (GUILayout.Button("START SLOW ROUND", GUILayout.Height(40))) StartSlowRound();
                GUI.color = Color.magenta; if (GUILayout.Button("START FAST ROUND", GUILayout.Height(40))) StartFastRound();
                GUILayout.Space(10);
                
                if (!BeatAnalyzer.Instance.isAnalyzing && !BeatAnalyzer.Instance.isAnalyzed) { GUI.color = Color.yellow; if (GUILayout.Button("ANALYZE CUSTOM TRACK", GUILayout.Height(60))) BeatAnalyzer.Instance.StartAnalysis(); }
                else if (BeatAnalyzer.Instance.isAnalyzing) { GUI.color = Color.gray; float prog = (BeatAnalyzer.Instance.audioSource.time / BeatAnalyzer.Instance.audioSource.clip.length) * 100f; GUILayout.Box($"ANALYZING... {prog.ToString("F0")}%", GUILayout.Height(60)); }
                else if (BeatAnalyzer.Instance.isAnalyzed) { GUI.color = Color.green; if (GUILayout.Button("START CUSTOM ROUND", GUILayout.Height(60))) StartCustomRound(); }
                GUI.color = Color.white;
            }
            else
            {
                if (GUILayout.Button("STOP ROUND", GUILayout.Height(40))) StopRound();
                GUILayout.Label($"ACTIVE: {currentType}", GUI.skin.box);
            }
            GUILayout.EndArea();
        }

        GUILayout.BeginArea(new Rect(10, Screen.height / 2 - 100, 250, 200));
        foreach (var p in GameManager.players)
        {
            if (p != null)
            {
                PlayerCombat pc = p.GetComponent<PlayerCombat>();
                GUIStyle healthStyle = new GUIStyle(GUI.skin.box) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
                healthStyle.normal.textColor = pc.CurrentHealth <= 30 ? Color.red : Color.green; 
                GUILayout.Label($"{p.PlayerName} Health: {pc.CurrentHealth}", healthStyle, GUILayout.Height(40));
            }
        }
        GUILayout.EndArea();

        if (isRoundActive && _startTime != 0)
        {
            float elapsed = (float)(NetworkTime.time - _startTime);
            GUIStyle style = new GUIStyle(GUI.skin.box) { fontSize = 24, alignment = TextAnchor.MiddleCenter };

            if (elapsed < 0) { style.normal.textColor = Color.yellow; GUI.Box(new Rect(Screen.width / 2 - 100, 50, 200, 70), "READY?", style); }
            else
            {
                if (currentType == RoundType.CustomTrack)
                {
                    if (_upcomingImpacts.Count > 0)
                    {
                        float trackTime = BeatAnalyzer.Instance.audioSource.time;
                        float timeToNextBeat = _upcomingImpacts[0] - trackTime;
                        
                        style.normal.textColor = (timeToNextBeat < 1.0f) ? Color.red : Color.cyan;
                        string modeText = customIsCombo ? $"CHAIN MODE ({currentComboCount}x)" : "SINGLE MODE";
                        GUI.Box(new Rect(Screen.width / 2 - 125, 50, 250, 120), $"{modeText}\nNEXT DROP\n{timeToNextBeat.ToString("F1")}s", style);
                    }
                    else { GUI.Box(new Rect(Screen.width / 2 - 125, 50, 250, 70), "FINISHING...", style); }
                }
                else
                {
                    float interval = (currentType == RoundType.FastCombo) ? 8.0f : 4.0f; float timer = elapsed % interval;
                    style.normal.textColor = (timer > (interval - 1.5f)) ? Color.red : Color.white;
                    GUI.Box(new Rect(Screen.width / 2 - 125, 50, 250, 100), $"WINDOW\n{timer.ToString("F1")}s / {interval}s", style);
                }
            }
        }

        if (combatLogs.Count > 0)
        {
            GUILayout.BeginArea(new Rect(Screen.width - 550, 100, 530, 600)); 
            GUIStyle logStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter }; 
            GUIStyle boxStyle = new GUIStyle(GUI.skin.box);
            GUILayout.Label("COMBAT LOG", logStyle); GUILayout.Space(5);
            
            foreach (var log in combatLogs)
            {
                GUILayout.BeginHorizontal(boxStyle);
                string p1DmgStr = log.p1Damage > 0 ? $" (-{log.p1Damage} HP)" : "";
                string p2DmgStr = log.p2Damage > 0 ? $" (-{log.p2Damage} HP)" : "";
                logStyle.normal.textColor = GetStateColor(log.p1State); GUILayout.Label($"{log.p1Name}: {log.p1Move}{p1DmgStr}", logStyle, GUILayout.Width(230));
                logStyle.normal.textColor = Color.white; GUILayout.Label(" vs ", logStyle, GUILayout.Width(40));
                logStyle.normal.textColor = GetStateColor(log.p2State); GUILayout.Label($"{log.p2Name}: {log.p2Move}{p2DmgStr}", logStyle, GUILayout.Width(230));
                GUILayout.EndHorizontal(); GUILayout.Space(2);
            }
            GUILayout.EndArea(); GUI.color = Color.white; 
        }
    }

    private Color GetStateColor(int state) { if (state == 1) return Color.green; if (state == -1) return Color.red; return Color.white; }
}