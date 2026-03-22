using Mirror;
using UnityEngine;
using System.Collections;

// THIS WAS MISSING: The enum must be outside the class or public inside
public enum RoundType { SlowRhythm, FastCombo }

public class RhythmRoundManager : NetworkBehaviour
{
    public static RhythmRoundManager Instance;

    [Header("Music Settings")]
    public AudioSource musicSource; 
    public float bpm = 100f;

    [Header("State")]
    [SyncVar] public RoundType currentType = RoundType.SlowRhythm; // THIS WAS MISSING
    
    [SyncVar(hook = nameof(OnRoundStateChanged))]
    public bool isRoundActive = false;

    [SyncVar] private double _startTime; 
    private int _lastPulseExecuted = 0;

    private void Awake() { if (Instance == null) Instance = this; }

    [Server]
    public void ToggleRound()
    {
        if (!isRoundActive)
        {
            _startTime = NetworkTime.time + 1.0; 
            _lastPulseExecuted = 0;
            isRoundActive = true;
        }
        else
        {
            isRoundActive = false;
            _startTime = 0;
        }
    }

   private void Update()
{
    if (!isRoundActive || _startTime == 0) return;

    double elapsed = NetworkTime.time - _startTime;
    if (elapsed < 0) return;

    // Fast Combo uses an 8-second window for those 4 moves
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

    private void ExecutePulseImpact()
    {
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
        if (NetworkServer.active && isServer && !isRoundActive)
        {
            GUILayout.BeginArea(new Rect(10, 10, 200, 250));
            if (GUILayout.Button("START ROUND", GUILayout.Height(50))) ToggleRound();
            
            GUILayout.Label("Current Mode: " + currentType);
            if (GUILayout.Button("CHANGE MODE"))
            {
                currentType = (currentType == RoundType.SlowRhythm) ? RoundType.FastCombo : RoundType.SlowRhythm;
            }
            GUILayout.EndArea();
        }

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
                // Warn the player when they have less than 1.5s left in the 8s window
                style.normal.textColor = (timer > (interval - 1.5f)) ? Color.red : Color.white;
                GUI.Box(new Rect(Screen.width / 2 - 100, 50, 200, 70), $"WINDOW\n{timer.ToString("F1")}s / {interval}s", style);
            }
        }
    }
}