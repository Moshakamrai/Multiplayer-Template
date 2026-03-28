using Mirror;
using UnityEngine;
using System.Collections.Generic;

public enum RoundType { SlowRhythm, FastCombo }

public class RhythmRoundManager : NetworkBehaviour
{
    public static RhythmRoundManager Instance;
    public AudioSource musicSource; 
    
    [SyncVar] public RoundType currentType = RoundType.SlowRhythm;
    [SyncVar(hook = nameof(OnRoundStateChanged))]
    public bool isRoundActive = false;

    [SyncVar] private double _startTime; 
    private int _lastPulseExecuted = 0;

    // --- NEW: Combat Log Variables ---
    private struct CombatLogEntry
    {
        public string p1Name; public string p1Move; public int p1State;
        public string p2Name; public string p2Move; public int p2State;
        public float timestamp;
    }
    private List<CombatLogEntry> combatLogs = new List<CombatLogEntry>();

    private void Awake() { if (Instance == null) Instance = this; }

    // --- MULTIPLAYER START COMMANDS ---
    [Server]
    public void StartSlowRound()
    {
        if (isRoundActive) return;
        currentType = RoundType.SlowRhythm;
        SetupRound();
    }

    [Server]
    public void StartFastRound()
    {
        if (isRoundActive) return;
        currentType = RoundType.FastCombo;
        SetupRound();
    }

    [Server]
    private void SetupRound()
    {
        // 1.0s delay ensures clients have time to receive the SyncVar update
        _startTime = NetworkTime.time + 1.0; 
        _lastPulseExecuted = 0;
        isRoundActive = true;
    }

    [Server]
    public void StopRound()
    {
        isRoundActive = false;
        _startTime = 0;
    }

    private void Update()
    {
        // NEW: Clean up old combat logs (they fade out after 4 seconds)
        if (combatLogs.Count > 0)
        {
            combatLogs.RemoveAll(log => Time.time - log.timestamp > 4f);
        }

        if (!isRoundActive || _startTime == 0) return;
        double elapsed = NetworkTime.time - _startTime;
        if (elapsed < 0) return;

        // Deterministic timing based on mode
        float interval = (currentType == RoundType.FastCombo) ? 8.0f : 4.0f;
        int maxPulses = (currentType == RoundType.FastCombo) ? 3 : 6;

        if (isServer)
        {
            for (int i = 1; i <= maxPulses; i++)
            {
                float impactTime = i * interval;
                if (elapsed >= impactTime && _lastPulseExecuted < i)
                {
                    _lastPulseExecuted = i;
                    ExecutePulseImpact();
                }
            }
            if (elapsed >= (interval * maxPulses) + 0.1f) isRoundActive = false;
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

        var m1 = p1.GetLockedMove();
        var m2 = p2.GetLockedMove();

        // 1. UPDATED INTERRUPTION CHECK: Jab beats Cross/Hook. Cross beats Hook.
        bool p1Interrupted = (m2.attack == "Jab" && (m1.attack == "Cross" || m1.attack == "Hook")) || 
                             (m2.attack == "Cross" && m1.attack == "Hook");
        bool p2Interrupted = (m1.attack == "Jab" && (m2.attack == "Cross" || m2.attack == "Hook")) || 
                             (m1.attack == "Cross" && m2.attack == "Hook");

        if (p1Interrupted) p1.TakeDamage(5); 
        if (p2Interrupted) p2.TakeDamage(5);

        // 2. RESOLVE DAMAGE & CAPTURE RESULT (1 = Hit, -1 = Dodged/Missed, 0 = Neutral/Block)
        int p1Result = ProcessDamage(p1, m1, p2, m2, p1Interrupted);
        int p2Result = ProcessDamage(p2, m2, p1, m1, p2Interrupted);

        // 3. EVALUATE LOG STATES: 1 (Green/Win), -1 (Red/Loss), 0 (White/Neutral)
        int p1State = 0;
        if (p1Interrupted || p2Result == 1) p1State = -1; // Got interrupted or hit by P2 -> Lost trade
        else if (p1Result == 1 || p2Result == -1) p1State = 1; // Landed hit or successfully dodged P2 -> Won trade

        int p2State = 0;
        if (p2Interrupted || p1Result == 1) p2State = -1; 
        else if (p2Result == 1 || p1Result == -1) p2State = 1; 

        // 4. SEND TO UI LOG
        string p1MoveStr = FormatMove(m1);
        string p2MoveStr = FormatMove(m2);
        RpcLogCombatTrade(pc1.PlayerName, p1MoveStr, p1State, pc2.PlayerName, p2MoveStr, p2State);
    }

    [Server]
    private int ProcessDamage(PlayerCombat attacker, PlayerCombat.RhythmAction move, PlayerCombat defender, PlayerCombat.RhythmAction defMove, bool isInterrupted)
    {
        if (isInterrupted || string.IsNullOrEmpty(move.attack) || move.attack == "Block") return 0;

        bool hits = false;
        int damage = 0;

        if (move.attack == "Jab") { damage = 10; hits = (defMove.dash == Vector3.zero && defMove.attack != "Block"); }
        else if (move.attack == "Cross") { damage = 15; hits = (defMove.attack != "Block"); }
        else if (move.attack == "Hook") { damage = 25; hits = (defMove.dash == Vector3.zero); }

        if (hits) 
        {
            defender.TakeDamage(damage);
            if (move.attack == "Hook") attacker.TargetAddEnergy(2);
            return 1; // Attacker won the trade
        }
        else 
        {
            if (defMove.dash != Vector3.zero) 
            {
                defender.TargetAddEnergy(1);
                return -1; // Defender won by dodging
            }
            return 0; // Neutral (Blocked)
        }
    }

    // Helper to turn the invisible Vector3 data into clean English words for the UI
    private string FormatMove(PlayerCombat.RhythmAction move)
    {
        if (!string.IsNullOrEmpty(move.attack)) return move.attack;
        if (move.dash == Vector3.left || move.dash == new Vector3(-1, 0, 0)) return "Dodge Left";
        if (move.dash == Vector3.right || move.dash == new Vector3(1, 0, 0)) return "Dodge Right";
        return "Idle";
    }

    [ClientRpc]
    private void RpcLogCombatTrade(string p1Name, string p1Move, int p1State, string p2Name, string p2Move, int p2State)
    {
        // Add the newest trade to the log (Client side)
        combatLogs.Add(new CombatLogEntry {
            p1Name = string.IsNullOrEmpty(p1Name) ? "Player 1" : p1Name, p1Move = p1Move, p1State = p1State,
            p2Name = string.IsNullOrEmpty(p2Name) ? "Player 2" : p2Name, p2Move = p2Move, p2State = p2State,
            timestamp = Time.time
        });
    }

    private void ExecutePulseImpact()
    {
        if (GameManager.players.Count >= 2) ResolveRhythmCombat();

        foreach (var player in GameManager.players)
        {
            if (player != null) player.GetComponent<PlayerCombat>().ExecuteRhythmImpact();
        }
    }

    void OnRoundStateChanged(bool oldVal, bool newVal)
    {
        if (newVal && musicSource != null) musicSource.Play();
        else if (musicSource != null) musicSource.Stop();
    }

    private void OnGUI()
    {
        // Host Controls
        if (NetworkServer.active && isServer)
        {
            GUILayout.BeginArea(new Rect(10, 10, 220, 300));
            if (!isRoundActive)
            {
                GUI.color = Color.cyan;
                if (GUILayout.Button("START SLOW ROUND", GUILayout.Height(60))) StartSlowRound();
                GUI.color = Color.magenta;
                if (GUILayout.Button("START FAST ROUND", GUILayout.Height(60))) StartFastRound();
                GUI.color = Color.white;
            }
            else
            {
                if (GUILayout.Button("STOP ROUND", GUILayout.Height(40))) StopRound();
                GUILayout.Label($"ACTIVE: {currentType}", GUI.skin.box);
            }
            GUILayout.EndArea();
        }

        // Rhythm Timer
        if (isRoundActive && _startTime != 0)
        {
            float elapsed = (float)(NetworkTime.time - _startTime);
            float interval = (currentType == RoundType.FastCombo) ? 8.0f : 4.0f;
            GUIStyle style = new GUIStyle(GUI.skin.box) { fontSize = 24, alignment = TextAnchor.MiddleCenter };

            if (elapsed < 0)
            {
                style.normal.textColor = Color.yellow;
                GUI.Box(new Rect(Screen.width / 2 - 100, 50, 200, 70), "READY?", style);
            }
            else
            {
                float timer = elapsed % interval;
                style.normal.textColor = (timer > (interval - 1.5f)) ? Color.red : Color.white;
                GUI.Box(new Rect(Screen.width / 2 - 100, 50, 200, 70), $"WINDOW\n{timer.ToString("F1")}s / {interval}s", style);
            }
        }

        // --- NEW: Combat Log UI (Right Side) ---
        if (combatLogs.Count > 0)
        {
            // Positioned dynamically on the right side of the screen
            GUILayout.BeginArea(new Rect(Screen.width - 420, 100, 400, 400));
            
            GUIStyle logStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            GUIStyle boxStyle = new GUIStyle(GUI.skin.box);
            
            GUILayout.Label("COMBAT LOG", logStyle);
            GUILayout.Space(5);

            foreach (var log in combatLogs)
            {
                GUILayout.BeginHorizontal(boxStyle);
                
                // P1 Segment
                logStyle.normal.textColor = GetStateColor(log.p1State);
                GUILayout.Label($"{log.p1Name}: {log.p1Move}", logStyle, GUILayout.Width(170));
                
                // VS text
                logStyle.normal.textColor = Color.white;
                GUILayout.Label(" vs ", logStyle, GUILayout.Width(40));
                
                // P2 Segment
                logStyle.normal.textColor = GetStateColor(log.p2State);
                GUILayout.Label($"{log.p2Name}: {log.p2Move}", logStyle, GUILayout.Width(170));
                
                GUILayout.EndHorizontal();
                GUILayout.Space(2);
            }
            GUILayout.EndArea();
            GUI.color = Color.white; // Reset safety
        }
    }

    // Assigns the Green/Red/White colors based on the winning logic
    private Color GetStateColor(int state)
    {
        if (state == 1) return Color.green; // Won the trade
        if (state == -1) return Color.red;  // Lost the trade
        return Color.white;                 // Blocked/Tied
    }
}