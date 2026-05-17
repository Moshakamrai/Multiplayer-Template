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

        // Use equipped cards from PlayerInventory, fallback to defaults
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

        // 2. BUILD AVAILABILITY FLAGS FOR ALL 20 CARDS
        var hasCard = new System.Collections.Generic.Dictionary<string, bool>();
        foreach (var c in new[] { "Jab","Cross","Hook","Block","Left","Right",
                                   "UnbreakablePunch","ParryIntent",
                                   "Grapple","Feint","Clutch",
                                   "Uppercut","Sweep","Focus","Taunt",
                                   "Overclock","Reverse","Trap","Cage","Mirror" })
            hasCard[c] = avail.Contains(c);

        bool canDash = hasCard["Left"] || hasCard["Right"];

        // 3. ADAPTIVE COUNTER LOGIC
        string attack = "";
        Vector3 dash = Vector3.zero;

        string mostSpammed = GetMostSpammedMove();
        float adaptiveChance = Random.value;

        if (adaptiveChance < 0.6f && !string.IsNullOrEmpty(mostSpammed))
        {
            switch (mostSpammed)
            {
                case "Jab":
                    if (canDash) dash = PickDash(hasCard["Left"], hasCard["Right"]);
                    else if (hasCard["Grapple"]) attack = "Grapple";
                    break;
                case "Cross":
                    if (hasCard["Block"]) attack = "Block";
                    else if (hasCard["Clutch"]) attack = "Clutch";
                    break;
                case "Hook":
                    if (hasCard["Jab"]) attack = "Jab";
                    else if (hasCard["Uppercut"]) attack = "Uppercut";
                    break;
                case "UnbreakablePunch":
                    if (hasCard["Left"]) dash = Vector3.left;
                    else if (hasCard["Clutch"]) attack = "Clutch";
                    break;
                case "Grapple":
                    if (hasCard["Jab"]) attack = "Jab";
                    else if (hasCard["Cross"]) attack = "Cross";
                    break;
                case "Uppercut":
                    if (hasCard["Block"]) attack = "Block";
                    else if (hasCard["Cross"]) attack = "Cross";
                    break;
                case "Sweep":
                    if (canDash) dash = PickDash(hasCard["Left"], hasCard["Right"]);
                    break;
                case "Overclock":
                    if (hasCard["Mirror"]) attack = "Mirror";
                    else if (hasCard["Reverse"]) attack = "Reverse";
                    break;
            }
        }

        // 4. RANDOM PICK FROM AVAILABLE POOL
        if (string.IsNullOrEmpty(attack) && dash == Vector3.zero)
        {
            var attackPool = new List<string>();
            var defPool = new List<string>();
            foreach (var c in avail)
            {
                if (CardManager.IsAttackTrigger(c)) attackPool.Add(c);
                else if (CardManager.IsDefenseTrigger(c)) defPool.Add(c);
            }

            float decision = Random.value;
            if (decision < 0.55f && attackPool.Count > 0)
            {
                attack = attackPool[Random.Range(0, attackPool.Count)];
            }
            else if (decision < 0.80f && defPool.Count > 0)
            {
                string defPick = defPool[Random.Range(0, defPool.Count)];
                if (defPick == "Left") { dash = Vector3.left; attack = ""; }
                else if (defPick == "Right") { dash = Vector3.right; attack = ""; }
                else { attack = defPick; dash = Vector3.zero; }
            }
            else if (canDash)
            {
                dash = PickDash(hasCard["Left"], hasCard["Right"]);
            }
            else if (attackPool.Count > 0)
            {
                attack = attackPool[Random.Range(0, attackPool.Count)];
            }
        }

        // 5. SLOT CHECK
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
            else return;
        }

        trigger = dash != Vector3.zero ? (dash == Vector3.left ? "Left" : "Right") : attack;

        // 6. SEMI-PRO TIMING
        float targetBeat = RhythmRoundManager.Instance.GetNextBeatTime();
        _combat.lastVocalSpikeTime = targetBeat - Random.Range(0.05f, 0.21f);

        _combat.QueueRhythmMove(attack, dash);
        if (_myCards != null) _myCards.ConsumeSlot(trigger);
    }

    private Vector3 PickDash(bool hasLeft, bool hasRight)
    {
        if (hasLeft && hasRight) return Random.value > 0.5f ? Vector3.left : Vector3.right;
        if (hasLeft) return Vector3.left;
        if (hasRight) return Vector3.right;
        return Vector3.zero;
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