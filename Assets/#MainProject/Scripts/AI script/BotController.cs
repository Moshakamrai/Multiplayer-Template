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

        // In single mode the bot can only play its currently-drawn hand (one card per family,
        // minus the locked type) — same rule as the human. Fall back to the full pool otherwise.
        List<string> avail;
        var rmmAvail = RhythmRoundManager.Instance;
        if (_myCards != null && rmmAvail != null && rmmAvail.IsSingleMoveMode())
        {
            avail = _myCards.GetHandTriggers();
            if (avail.Count == 0) avail = new List<string>(_myCards.availableCardsForRound);
        }
        else
        {
            avail = _myCards != null && _myCards.availableCardsForRound.Count > 0
                ? _myCards.availableCardsForRound
                : new List<string> { "Jab", "Cross", "Hook", "Block", "Left", "Right", "UnbreakablePunch", "ParryIntent" };
        }

        // Difficulty ramp: rounds 1-2 are easy (sloppy timing, rarely hard-counters),
        // rounds 3-4 medium, round 5+ semi-pro.
        var rmm = RhythmRoundManager.Instance;
        int round = rmm != null ? rmm.currentRoundNumber : 1;
        GetTimingOffset(round, out float minOff, out float maxOff);

        // COMBO MODE: fill buffer with random moves from available pool
        if (rmm != null && !rmm.IsSingleMoveMode())
        {
            while (_combat._comboBuffer.Count < rmm.currentComboCount)
            {
                string move = avail[Random.Range(0, avail.Count)];
                _combat._comboBuffer.Add(new PlayerCombat.RhythmAction { attack = move, dash = Vector3.zero });
            }
            // Looser timing in combo mode, and looser still in early rounds.
            _combat.lastVocalSpikeTime = rmm.GetNextBeatTime() - Random.Range(minOff + 0.15f, maxOff + 0.25f);
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
                                   "Grapple","Fake","Clutch",
                                   "Uppercut","Sweep","Focus","Taunt",
                                   "Overclock","Reverse","Trap","Cage","Mirror" })
            hasCard[c] = avail.Contains(c);

        bool canDash = hasCard["Left"] || hasCard["Right"];

        // 3. ADAPTIVE COUNTER LOGIC
        string attack = "";
        Vector3 dash = Vector3.zero;

        string mostSpammed = GetMostSpammedMove();
        float adaptiveChance = Random.value;

        // Early rounds rarely hard-counter the player; later rounds punish heavily.
        float adaptiveThreshold = (round <= 2) ? 0.15f : (round <= 4) ? 0.40f : 0.65f;

        if (adaptiveChance < adaptiveThreshold && !string.IsNullOrEmpty(mostSpammed))
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

        // 6. TIMING — scaled by difficulty (early rounds = sloppy, loses timing clashes)
        float targetBeat = RhythmRoundManager.Instance.GetNextBeatTime();
        _combat.lastVocalSpikeTime = targetBeat - Random.Range(minOff, maxOff);

        _combat.QueueRhythmMove(attack, dash);
        if (_myCards != null) _myCards.ConsumeSlot(trigger);
    }

    // Bot timing offset from the beat, by round. Bigger offset = worse timing =
    // less damage and loses same-family timing clashes to a competent player.
    private void GetTimingOffset(int round, out float min, out float max)
    {
        if (round <= 2)      { min = 0.28f; max = 0.50f; } // EASY — frequently mistimes
        else if (round <= 4) { min = 0.14f; max = 0.30f; } // MEDIUM
        else                 { min = 0.05f; max = 0.21f; } // HARD — semi-pro
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