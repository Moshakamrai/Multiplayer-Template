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

        var avail = _myCards != null && _myCards.availableCardsForRound.Count > 0
            ? _myCards.availableCardsForRound
            : new List<string> { "Jab", "Cross", "Hook", "Block", "Left", "Right", "UnbreakablePunch", "ParryIntent" };

        // COMBO MODE: fill buffer with random moves from available pool
        var rmm = RhythmRoundManager.Instance;
        if (rmm != null && !rmm.IsSingleMoveMode())
        {
            while (_combat._comboBuffer.Count < rmm.currentComboCount)
            {
                string move = avail[Random.Range(0, avail.Count)];
                _combat._comboBuffer.Add(new PlayerCombat.RhythmAction { attack = move, dash = Vector3.zero });
            }
            _combat.lastVocalSpikeTime = rmm.GetNextBeatTime() - Random.Range(0.3f, 0.7f);
            return;
        }

        // 1. ANALYZE PLAYER
        PlayerController opponent = _controller.GetOpponent();
        if (opponent != null)
        {
            var oppMove = opponent.GetComponent<PlayerCombat>().PeekNextMove();
            UpdatePlayerHistory(oppMove.attack);
        }

        // 2. CHOOSE COUNTER OR RANDOM (filtered by available cards)
        string attack = "";
        Vector3 dash = Vector3.zero;

        bool hasJab = avail.Contains("Jab");
        bool hasCross = avail.Contains("Cross");
        bool hasHook = avail.Contains("Hook");
        bool hasBlock = avail.Contains("Block");
        bool hasLeft = avail.Contains("Left");
        bool hasRight = avail.Contains("Right");
        bool hasBoom = avail.Contains("UnbreakablePunch");
        bool hasCage = avail.Contains("ParryIntent");
        bool canDash = hasLeft || hasRight;

        string mostSpammed = GetMostSpammedMove();
        float adaptiveChance = Random.value;

        if (adaptiveChance < 0.6f && !string.IsNullOrEmpty(mostSpammed))
        {
            if (mostSpammed == "Jab" && canDash)
            { dash = hasLeft && hasRight ? (Random.value > 0.5f ? Vector3.left : Vector3.right) : (hasLeft ? Vector3.left : Vector3.right); }
            else if (mostSpammed == "Cross" && hasBlock) { attack = "Block"; }
            else if (mostSpammed == "Hook" && hasJab) { attack = "Jab"; }
            else if (mostSpammed == "UnbreakablePunch" && hasLeft) { dash = Vector3.left; }
        }

        // If counter didn't fire or was invalid, pick randomly from available pool
        if (string.IsNullOrEmpty(attack) && dash == Vector3.zero)
        {
            var attackPool = new List<string>();
            if (hasJab) attackPool.Add("Jab");
            if (hasCross) attackPool.Add("Cross");
            if (hasHook) attackPool.Add("Hook");
            if (hasBlock) attackPool.Add("Block");
            if (hasBoom) attackPool.Add("UnbreakablePunch");
            if (hasCage) attackPool.Add("ParryIntent");

            float decision = Random.value;
            if (decision < 0.7f && attackPool.Count > 0)
            {
                attack = attackPool[Random.Range(0, attackPool.Count)];
            }
            else if (decision < 0.9f && canDash)
            {
                dash = hasLeft && hasRight ? (Random.value > 0.5f ? Vector3.left : Vector3.right) : (hasLeft ? Vector3.left : Vector3.right);
            }
            else if (attackPool.Count > 0)
            {
                attack = attackPool[Random.Range(0, attackPool.Count)];
            }
        }

        // 3. SLOT CHECK — respect same slot rules as the player
        string trigger = dash != Vector3.zero ? (dash == Vector3.left ? "Left" : "Right") : attack;

        if (_myCards != null && !_myCards.HasSlot(trigger))
        {
            var availAttacks = avail.FindAll(c => CardManager.IsAttackTrigger(c));
            var availDefs = avail.FindAll(c => CardManager.IsDefenseTrigger(c));

            if (CardManager.IsAttackTrigger(trigger) && availDefs.Count > 0)
            {
                string defPick = availDefs[Random.Range(0, availDefs.Count)];
                if (defPick == "Left") { dash = Vector3.left; attack = ""; }
                else if (defPick == "Right") { dash = Vector3.right; attack = ""; }
                else { attack = defPick; dash = Vector3.zero; }
            }
            else if (!CardManager.IsAttackTrigger(trigger) && availAttacks.Count > 0)
            {
                attack = availAttacks[Random.Range(0, availAttacks.Count)];
                dash = Vector3.zero;
            }
            else return; // Both pools exhausted — skip this beat
        }

        // Recompute trigger after fallback
        trigger = dash != Vector3.zero ? (dash == Vector3.left ? "Left" : "Right") : attack;

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