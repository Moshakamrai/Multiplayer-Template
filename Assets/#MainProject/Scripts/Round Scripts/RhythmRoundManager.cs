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
    
    // NEW: Syncing the Dynamic Combo lengths
    [SyncVar] public int currentComboCount = 1;
    [SyncVar] public bool customIsCombo = false; 

    [SyncVar] private double _startTime; 
    private int _lastPulseExecuted = 0;

    private List<float> _upcomingImpacts = new List<float>();
    private List<int> _clusterSizes = new List<int>(); // Maps perfectly to _upcomingImpacts

    private struct CombatLogEntry { public string p1Name; public string p1Move; public int p1State; public string p2Name; public string p2Move; public int p2State; public float timestamp; }
    private List<CombatLogEntry> combatLogs = new List<CombatLogEntry>();

    private void Awake() { if (Instance == null) Instance = this; }

    public bool IsSingleMoveMode()
    {
        if (currentType == RoundType.SlowRhythm) return true;
        if (currentType == RoundType.CustomTrack && !customIsCombo) return true;
        return false;
    }

    [Server] public void StartSlowRound() { if (isRoundActive) return; currentType = RoundType.SlowRhythm; currentComboCount = 1; customIsCombo = false; SetupRound(); }
    [Server] public void StartFastRound() { if (isRoundActive) return; currentType = RoundType.FastCombo; currentComboCount = 4; customIsCombo = true; SetupRound(); }
    
    [Server] 
    public void StartCustomRound() 
    { 
        if (isRoundActive || !BeatAnalyzer.Instance.isAnalyzed) return; 
        currentType = RoundType.CustomTrack; 
        
        List<float> allBeats = BeatAnalyzer.Instance.GetAllActionTriggers();
        _upcomingImpacts.Clear();
        _clusterSizes.Clear();
        
        int tempComboSize = 1;

        // 1. Group the clustered beats (INCREASED to 2.0s gap to link more beats into Combos)
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

        // 2. Prep-Time Math Rule (DECREASED from 2.0s to 1.2s per hit for faster gameplay)
        for (int i = 0; i < _upcomingImpacts.Count; i++)
        {
            float prepTime = (i == 0) ? _upcomingImpacts[i] : (_upcomingImpacts[i] - _upcomingImpacts[i - 1]);
            
            // Now you only need 1.2 seconds of warning per combo hit!
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

    [Server] private void SetupRound() { _startTime = NetworkTime.time + 1.0; _lastPulseExecuted = 0; isRoundActive = true; }
    [Server] public void StopRound() { isRoundActive = false; _startTime = 0; _upcomingImpacts.Clear(); }

    private void Update()
    {
        if (combatLogs.Count > 0) combatLogs.RemoveAll(log => Time.time - log.timestamp > 4f);

        if (!isRoundActive || _startTime == 0) return;
        double elapsed = NetworkTime.time - _startTime;
        if (elapsed < 0) return;

        if (isServer)
        {
            if (currentType == RoundType.CustomTrack)
            {
                float trackTime = BeatAnalyzer.Instance.audioSource.time;

                if (_upcomingImpacts.Count > 0 && trackTime >= _upcomingImpacts[0])
                {
                    _upcomingImpacts.RemoveAt(0); 
                    _clusterSizes.RemoveAt(0);

                    ExecutePulseImpact();
                    RpcTriggerHitStop(); 

                    // Queue up the next dynamic mode instantly
                    if (_upcomingImpacts.Count > 0) 
                    {
                        currentComboCount = _clusterSizes[0];
                        customIsCombo = (currentComboCount > 1);
                    }
                }

                if (!BeatAnalyzer.Instance.audioSource.isPlaying) StopRound();
            }
            else
            {
                float interval = (currentType == RoundType.FastCombo) ? 8.0f : 4.0f;
                int maxPulses = (currentType == RoundType.FastCombo) ? 3 : 6;
                for (int i = 1; i <= maxPulses; i++)
                {
                    float impactTime = i * interval;
                    if (elapsed >= impactTime && _lastPulseExecuted < i) { _lastPulseExecuted = i; ExecutePulseImpact(); }
                }
                if (elapsed >= (interval * maxPulses) + 0.1f) StopRound();
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
        var m1 = p1.GetLockedMove(); var m2 = p2.GetLockedMove();

        bool p1Interrupted = (m2.attack == "Jab" && (m1.attack == "Cross" || m1.attack == "Hook")) || (m2.attack == "Cross" && m1.attack == "Hook");
        bool p2Interrupted = (m1.attack == "Jab" && (m2.attack == "Cross" || m2.attack == "Hook")) || (m1.attack == "Cross" && m2.attack == "Hook");

        if (p1Interrupted) p1.TakeDamage(5); if (p2Interrupted) p2.TakeDamage(5);

        int p1Result = ProcessDamage(p1, m1, p2, m2, p1Interrupted); int p2Result = ProcessDamage(p2, m2, p1, m1, p2Interrupted);
        int p1State = 0; if (p1Interrupted || p2Result == 1) p1State = -1; else if (p1Result == 1 || p2Result == -1) p1State = 1; 
        int p2State = 0; if (p2Interrupted || p1Result == 1) p2State = -1; else if (p2Result == 1 || p1Result == -1) p2State = 1; 

        RpcLogCombatTrade(pc1.PlayerName, FormatMove(m1), p1State, pc2.PlayerName, FormatMove(m2), p2State);
    }

    [Server]
    private int ProcessDamage(PlayerCombat attacker, PlayerCombat.RhythmAction move, PlayerCombat defender, PlayerCombat.RhythmAction defMove, bool isInterrupted)
    {
        if (isInterrupted || string.IsNullOrEmpty(move.attack) || move.attack == "Block") return 0;
        bool hits = false; int damage = 0;
        if (move.attack == "Jab") { damage = 10; hits = (defMove.dash == Vector3.zero && defMove.attack != "Block"); }
        else if (move.attack == "Cross") { damage = 15; hits = (defMove.attack != "Block"); }
        else if (move.attack == "Hook") { damage = 25; hits = (defMove.dash == Vector3.zero); }

        if (hits) { defender.TakeDamage(damage); if (move.attack == "Hook") attacker.TargetAddEnergy(2); return 1; }
        else { if (defMove.dash != Vector3.zero) { defender.TargetAddEnergy(1); return -1; } return 0; }
    }

    private string FormatMove(PlayerCombat.RhythmAction move)
    {
        if (!string.IsNullOrEmpty(move.attack)) return move.attack;
        if (move.dash == Vector3.left || move.dash == new Vector3(-1, 0, 0)) return "Dodge Left";
        if (move.dash == Vector3.right || move.dash == new Vector3(1, 0, 0)) return "Dodge Right";
        return "Idle";
    }

    [ClientRpc] private void RpcLogCombatTrade(string p1Name, string p1Move, int p1State, string p2Name, string p2Move, int p2State) { combatLogs.Add(new CombatLogEntry { p1Name = string.IsNullOrEmpty(p1Name) ? "Player 1" : p1Name, p1Move = p1Move, p1State = p1State, p2Name = string.IsNullOrEmpty(p2Name) ? "Player 2" : p2Name, p2Move = p2Move, p2State = p2State, timestamp = Time.time }); }
    private void ExecutePulseImpact() { if (GameManager.players.Count >= 2) ResolveRhythmCombat(); foreach (var player in GameManager.players) { if (player != null) player.GetComponent<PlayerCombat>().ExecuteRhythmImpact(); } }

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
                        GUI.Box(new Rect(Screen.width / 2 - 100, 50, 200, 70), $"{modeText}\nNEXT DROP\n{timeToNextBeat.ToString("F1")}s", style);
                    }
                    else { GUI.Box(new Rect(Screen.width / 2 - 100, 50, 200, 70), "FINISHING...", style); }
                }
                else
                {
                    float interval = (currentType == RoundType.FastCombo) ? 8.0f : 4.0f; float timer = elapsed % interval;
                    style.normal.textColor = (timer > (interval - 1.5f)) ? Color.red : Color.white;
                    GUI.Box(new Rect(Screen.width / 2 - 100, 50, 200, 70), $"WINDOW\n{timer.ToString("F1")}s / {interval}s", style);
                }
            }
        }

        if (combatLogs.Count > 0)
        {
            GUILayout.BeginArea(new Rect(Screen.width - 420, 100, 400, 400));
            GUIStyle logStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter }; GUIStyle boxStyle = new GUIStyle(GUI.skin.box);
            GUILayout.Label("COMBAT LOG", logStyle); GUILayout.Space(5);
            foreach (var log in combatLogs)
            {
                GUILayout.BeginHorizontal(boxStyle);
                logStyle.normal.textColor = GetStateColor(log.p1State); GUILayout.Label($"{log.p1Name}: {log.p1Move}", logStyle, GUILayout.Width(170));
                logStyle.normal.textColor = Color.white; GUILayout.Label(" vs ", logStyle, GUILayout.Width(40));
                logStyle.normal.textColor = GetStateColor(log.p2State); GUILayout.Label($"{log.p2Name}: {log.p2Move}", logStyle, GUILayout.Width(170));
                GUILayout.EndHorizontal(); GUILayout.Space(2);
            }
            GUILayout.EndArea(); GUI.color = Color.white; 
        }
    }

    private Color GetStateColor(int state) { if (state == 1) return Color.green; if (state == -1) return Color.red; return Color.white; }
}