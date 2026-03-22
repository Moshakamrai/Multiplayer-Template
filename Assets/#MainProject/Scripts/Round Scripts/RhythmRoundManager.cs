using Mirror;
using UnityEngine;
using System.Collections;

public class RhythmRoundManager : NetworkBehaviour
{
    public static RhythmRoundManager Instance;

    [Header("Music Settings")]
    public AudioSource musicSource; 
    public float bpm = 100f;

    [Header("State")]
    [SyncVar(hook = nameof(OnRoundStateChanged))]
    public bool isRoundActive = false;

    [SyncVar] private double _startTime; // The time the 4s pulses begin
    private int _lastPulseExecuted = 0;

    private void Awake()
    {
        if (Instance == null) Instance = this;
    }

    [Server]
    public void ToggleRound()
    {
        if (!isRoundActive)
        {
            // Start the rhythm logic 1.0s in the future
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

        // ONLY the Server should decide when an impact happens to prevent de-sync
        if (isServer) 
        {
            for (int i = 1; i <= 6; i++)
            {
                float impactTime = i * 4.0f;
                if (elapsed >= impactTime && _lastPulseExecuted < i)
                {
                    _lastPulseExecuted = i;
                    ExecutePulseImpact();
                }
            }

            if (elapsed >= 24.1f) isRoundActive = false;
        }
    }

    private void ExecutePulseImpact()
    {
        foreach (var player in GameManager.players)
        {
            // Using the centralized execution method in PlayerCombat
            if (player != null) player.GetComponent<PlayerCombat>().ExecuteRhythmImpact();
        }
    }

    // This hook fires the MOMENT the server sets isRoundActive = true
    void OnRoundStateChanged(bool oldVal, bool newVal)
    {
        if (newVal)
        {
            if (musicSource != null) musicSource.Play(); // Starts INSTANTLY
        }
        else if (musicSource != null)
        {
            musicSource.Stop();
        }
    }

    private void OnGUI()
    {
        if (NetworkServer.active && isServer && !isRoundActive)
        {
            if (GUI.Button(new Rect(10, 10, 150, 50), "START ROUND")) ToggleRound();
        }

        if (isRoundActive && _startTime != 0)
        {
            float elapsed = (float)(NetworkTime.time - _startTime);
            GUIStyle style = new GUIStyle(GUI.skin.box) { fontSize = 24, alignment = TextAnchor.MiddleCenter };

            if (elapsed < 0)
            {
                // UI shows the 1s countdown while music is already playing
                style.normal.textColor = Color.yellow;
                GUI.Box(new Rect(Screen.width / 2 - 100, 50, 200, 70), "READY?", style);
            }
            else
            {
                float timer = elapsed % 4.0f;
                style.normal.textColor = (timer > 3.0f) ? Color.red : Color.white;
                GUI.Box(new Rect(Screen.width / 2 - 100, 50, 200, 70), $"PULSE\n{timer.ToString("F1")}s / 4.0s", style);
            }
        }
    }
}