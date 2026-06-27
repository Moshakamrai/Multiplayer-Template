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

    private void LateUpdate()
    {
        if (!isServer) return;
        if (_combat == null || _combat.IsDead || _combat.IsHurting || _combat.IsStaggered || _combat.isAttacking) return;
        // BotBeatApproach already handles rotation during the beat run-in; skip to avoid fighting it.
        if (GetComponent<BotBeatApproach>() is { IsApproaching: true }) return;
        FaceOpponent();
    }

    private void FaceOpponent()
    {
        Vector3 lookPos = GetOpponentLookPos();
        Vector3 dir = lookPos - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(dir);
    }

    // The opponent's head/camera position in world space. Falls back to the body transform
    // if the player has no camera slot assigned. This lets the bot look at the player's head
    // rather than their feet.
    // In VR/host mode, we use the live HMD position when it's available.
    private Vector3 GetOpponentLookPos()
    {
        if (_controller == null) return transform.position + transform.forward;
        PlayerController opponent = _controller.GetOpponent();
        if (opponent == null) return transform.position + transform.forward;

        if (VRCameraDriver.VRActive && opponent.isLocalPlayer)
        {
            Vector3 head = VRHands.HeadPos;
            if (head != Vector3.zero) return head;
        }

        Transform cam = opponent.CameraPosition;
        return cam != null ? cam.position : opponent.transform.position;
    }

    [Server]
    public void ThinkNextMove()
    {
        // Staggered too → don't act (this is what keeps the bot frozen during the drone-rush segment;
        // otherwise it kept queuing attacks and animating over the held stagger pose).
        if (_combat.IsHurting || _combat.IsDead || _combat.IsStaggered) return;

        // In single mode the bot can only play its currently-drawn hand (one card per family,
        // minus the locked type) — same rule as the human.
        List<string> avail = null;
        var rmmAvail = RhythmRoundManager.Instance;
        if (_myCards != null && rmmAvail != null && rmmAvail.IsSingleMoveMode())
        {
            avail = _myCards.GetHandTriggers();
            if (avail.Count == 0 && _myCards.availableCardsForRound.Count > 0)
                avail = new List<string>(_myCards.availableCardsForRound);
        }
        else if (_myCards != null && _myCards.availableCardsForRound.Count > 0)
        {
            avail = new List<string>(_myCards.availableCardsForRound);
        }

        // Ultimate fallback: the Tier-0 starter trio ONLY (never the full 20). This keeps the bot
        // limited to its real deck — e.g. it can't spam Throws that would bypass the player's Reflect.
        if (avail == null || avail.Count == 0)
            avail = new List<string> { "Jab", "Block", "ParryIntent" };

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

            // BEAT RUN-IN (combo mode too): start charging to the attack point NOW so the bot arrives on
            // the beat. Without this, combo rounds never triggered the approach and the bot just stood
            // at spawn. Uses the same beat the manager is counting down to.
            StartApproach();
            return;
        }

        // 0. TAUNTED — must throw a Strike this beat or take damage. Comply if we can.
        if (_combat.MustStrikeBeats > 0)
        {
            var strikes = avail.FindAll(c => CardManager.IsAttackTrigger(c)
                && c != "Grapple" && c != "Fake" && c != "Sweep"); // attacks that are Strikes (not Throws)
            if (strikes.Count > 0)
            {
                string pick = strikes[Random.Range(0, strikes.Count)];
                float beat = RhythmRoundManager.Instance.GetNextBeatTime();
                _combat.lastVocalSpikeTime = beat - Random.Range(minOff, maxOff);
                _combat.QueueRhythmMove(pick, Vector3.zero);
                if (_myCards != null) _myCards.ConsumeSlot(pick);
                StartApproach();
                return;
            }
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

        // Round 1 rarely hard-counters; round 2 punishes your spammed moves much more often.
        float adaptiveThreshold = (round <= 1) ? 0.15f : (round <= 2) ? 0.45f : 0.65f;

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

        // BEAT RUN-IN: the bot charges in from its spawn to perform WHATEVER move it chose at close
        // range — strike, throw, block, parry, dash, anything — arriving on the beat, then recovers
        // and returns home. (Previously gated to Strike/Throw only; now every beat the bot acts on.)
        StartApproach();
    }

    // No-op: BotBeatApproach now SELF-DRIVES the run-in (it starts running the moment the bot is home
    // and the next beat is within its lead window, pacing speed to the time left). The card decision is
    // still made here in ThinkNextMove and queued; the movement is owned by BotBeatApproach.Update.
    private void StartApproach() { }

    // Bot timing offset from the beat, by round. Bigger offset = worse timing =
    // less damage and loses same-family timing clashes to a competent player.
    // Match is 2 rounds: round 1 = ease them in, round 2 = step it up so it's a real fight.
    private void GetTimingOffset(int round, out float min, out float max)
    {
        if (round <= 1)      { min = 0.26f; max = 0.46f; } // round 1 — frequently mistimes
        else if (round <= 2) { min = 0.12f; max = 0.24f; } // round 2 — TOUGHER, tighter timing
        else                 { min = 0.05f; max = 0.18f; } // beyond — semi-pro
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