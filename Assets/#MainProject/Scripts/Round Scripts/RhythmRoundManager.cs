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

    

    // Updated ResolveRhythmCombat in RhythmRoundManager.cs
    [Server]
    private void ResolveRhythmCombat()
    {
        var playerList = new List<PlayerController>(GameManager.players);
        if (playerList.Count < 2) return;

        PlayerCombat p1 = playerList[0].GetComponent<PlayerCombat>();
        PlayerCombat p2 = playerList[1].GetComponent<PlayerCombat>();

        var m1 = p1.GetLockedMove();
        var m2 = p2.GetLockedMove();

        // 1. INTERRUPTION CHECK (Jab beats Cross/Hook)
        bool p1Interrupted = (m2.attack == "Jab" && (m1.attack == "Cross" || m1.attack == "Hook"));
        bool p2Interrupted = (m1.attack == "Jab" && (m2.attack == "Cross" || m2.attack == "Hook"));

        // If interrupted, trigger the "Hurt" animation immediately on the server/clients
        if (p1Interrupted) p1.GetComponent<PlayerCombat>().TakeDamage(5); // Small sting for interrupt
        if (p2Interrupted) p2.GetComponent<PlayerCombat>().TakeDamage(5);

        // 2. RESOLVE ACTUAL DAMAGE
        ProcessDamage(p1, m1, p2, m2, p1Interrupted);
        ProcessDamage(p2, m2, p1, m1, p2Interrupted);
    }

    [Server]
    private void ProcessDamage(PlayerCombat attacker, PlayerCombat.RhythmAction move, PlayerCombat defender, PlayerCombat.RhythmAction defMove, bool isInterrupted)
    {
        if (isInterrupted || string.IsNullOrEmpty(move.attack)) return;

        bool hits = false;
        int damage = (move.attack == "Hook") ? 25 : (move.attack == "Cross") ? 15 : 10;

        // NEW RPS LOGIC WITH BLOCK BREAKING
        // Cross: Catches them if they are moving (Dashing)
        if (move.attack == "Cross") 
            hits = (defMove.dash != Vector3.zero);

        // Hook: Breaks blocks or hits static players
        else if (move.attack == "Hook") 
            hits = (defender.CurrentShield > 0 || (defMove.dash == Vector3.zero && string.IsNullOrEmpty(defMove.attack)));

        // Jab: Hits if they aren't dodging
        else if (move.attack == "Jab") 
            hits = (defMove.dash == Vector3.zero);

        if (hits) defender.TakeDamage(damage);
    }

    // Update this in RhythmRoundManager.cs
    private void ExecutePulseImpact()
    {
        if (GameManager.players.Count >= 2) ResolveRhythmCombat();

        foreach (var player in GameManager.players)
        {
            if (player != null) 
            {
                PlayerCombat combat = player.GetComponent<PlayerCombat>();

                // If it's the Host's player, run it normally
                if (player.isServer && player.isLocalPlayer)
                {
                    combat.ExecuteRhythmImpact();
                }
                // If it's a Remote Client, tell them to trigger their own move
                else
                {
                    combat.TargetTriggerRhythmImpact();
                }
            }
        }
    }

    void OnRoundStateChanged(bool oldVal, bool newVal)
    {
        if (newVal && musicSource != null) musicSource.Play();
        else if (musicSource != null) musicSource.Stop();
    }

    private void OnGUI()
    {
        // Only the Host/Server sees the controls
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

        // Shared Round UI
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
    }
}