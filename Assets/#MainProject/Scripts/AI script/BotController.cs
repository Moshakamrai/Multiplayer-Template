using UnityEngine;
using Mirror;
using System.Collections.Generic;

public class BotController : NetworkBehaviour
{
    private PlayerCombat _combat;
    private PlayerController _controller;
    private CardManager _myCards;

    // --- ADAPTIVE AI MEMORY ---
    private Dictionary<string, int> _playerMoveHistory = new Dictionary<string, int>();

    void Start()
    {
        _combat = GetComponent<PlayerCombat>();
        _controller = GetComponent<PlayerController>();
        _myCards = GetComponent<CardManager>();
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
            if (mostSpammed == "Jab") { dash = Random.value > 0.5f ? Vector3.left : Vector3.right; }
            else if (mostSpammed == "Cross") { attack = "Block"; }
            else if (mostSpammed == "Hook") { attack = "Jab"; }
            else if (mostSpammed == "UnbreakablePunch") { dash = Vector3.left; }
        }
        else
        {
            float decision = Random.value;
            if (decision < 0.7f) attack = Random.value < 0.5f ? "Jab" : (Random.value < 0.8f ? "Cross" : "Hook");
            else if (decision < 0.9f) dash = (Random.value > 0.5f) ? Vector3.left : Vector3.right;
            else attack = "Block";
        }

        // 3. SLOT CHECK — respect same slot rules as the player
        string trigger = dash != Vector3.zero ? (dash == Vector3.left ? "Left" : "Right") : attack;

        if (_myCards != null && !_myCards.HasSlot(trigger))
        {
            // Primary move blocked — try the opposite pool
            if (CardManager.IsAttackTrigger(trigger) && _myCards.defenseSlotsRemaining > 0)
            { attack = "Block"; dash = Vector3.zero; trigger = "Block"; }
            else if (!CardManager.IsAttackTrigger(trigger) && _myCards.attackSlotsRemaining > 0)
            { attack = "Jab"; dash = Vector3.zero; trigger = "Jab"; }
            else return; // Both pools exhausted — skip this beat
        }

        // 4. SEMI-PRO TIMING
        float targetBeat = RhythmRoundManager.Instance.GetNextBeatTime();
        _combat.lastVocalSpikeTime = targetBeat - Random.Range(0.05f, 0.21f);

        _combat.QueueRhythmMove(attack, dash);
        if (_myCards != null) _myCards.ConsumeSlot(trigger);
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