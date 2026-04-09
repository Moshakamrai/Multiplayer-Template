using UnityEngine;
using Mirror;
using System.Collections.Generic;

public class BotController : NetworkBehaviour
{
    private PlayerCombat _combat;
    private PlayerController _controller;

    // --- ADAPTIVE AI MEMORY ---
    private Dictionary<string, int> _playerMoveHistory = new Dictionary<string, int>();
    // REMOVED the unused _lastObservedPlayerMove variable

    void Start()
    {
        _combat = GetComponent<PlayerCombat>();
        _controller = GetComponent<PlayerController>();
        if (isServer) _controller.SetReady(true);
    }

    [Server]
    public void ThinkNextMove()
    {
        if (_combat.IsHurting || _combat.IsDead) return;

        // 1. ANALYZE PLAYER
        PlayerController opponent = _controller.GetOpponent();
        if (opponent != null)
        {
            var oppMove = opponent.GetComponent<PlayerCombat>().PeekNextMove();
            UpdatePlayerHistory(oppMove.attack);
        }

        // 2. CHOOSE COUNTER OR RANDOM
        string attack = "";
        Vector3 dash = Vector3.zero;

        string mostSpammed = GetMostSpammedMove();
        float adaptiveChance = Random.value;

        if (adaptiveChance < 0.6f && !string.IsNullOrEmpty(mostSpammed)) 
        {
            // Counter Logic: 60% chance to specifically counter your spam
            if (mostSpammed == "Jab") { dash = Random.value > 0.5f ? Vector3.left : Vector3.right; } // Jab is dodgable
            else if (mostSpammed == "Cross") { attack = "Block"; } // Block is great vs Cross
            else if (mostSpammed == "Hook") { attack = "Jab"; } // Jab interrupts Hook
            else if (mostSpammed == "UnbreakablePunch") { dash = Vector3.left; } // BOOM must be dodged
        }
        else
        {
            // Standard Random Logic (40% fallback)
            float decision = Random.value;
            if (decision < 0.7f) attack = Random.value < 0.5f ? "Jab" : (Random.value < 0.8f ? "Cross" : "Hook");
            else if (decision < 0.9f) dash = (Random.value > 0.5f) ? Vector3.left : Vector3.right;
            else attack = "Block";
        }

        // 3. SEMI-PRO TIMING (Tighter but not perfect)
        // Instead of 0.01 - 0.4, we use 0.05 - 0.2 to make it much harder to out-time
        float targetBeat = RhythmRoundManager.Instance.GetNextBeatTime();
        float proDelay = Random.Range(0.05f, 0.21f); 
        _combat.lastVocalSpikeTime = targetBeat - proDelay;

        _combat.QueueRhythmMove(attack, dash);
    }

    private void UpdatePlayerHistory(string move)
    {
        if (string.IsNullOrEmpty(move) || move == "IDLE") return;
        if (!_playerMoveHistory.ContainsKey(move)) _playerMoveHistory[move] = 0;
        _playerMoveHistory[move]++;
    }

    private string GetMostSpammedMove()
    {
        string best = ""; int max = 0;
        foreach (var pair in _playerMoveHistory)
        {
            if (pair.Value > max) { max = pair.Value; best = pair.Key; }
        }
        return (max >= 3) ? best : ""; // Only counter if they've used it 3+ times
    }
}