using System.Collections;
using Mirror;
using UnityEngine;

/// The bot's beat RUN-IN telegraph + motivated RETURN home. On an attack beat the bot charges from its
/// home spot toward the player, arriving exactly ON the beat. It holds there while the trade resolves,
/// then returns home with an animation chosen by the OUTCOME:
///   • got hit   → KNOCKBACK: a falling/recoil anim whose backward motion carries it home (brief
///                 slow-mo on impact then speeds up to be home before the next beat).
///   • landed hit→ BACK-DASH: a quick retreat-hop while sliding home.
///   • clash/none→ a neutral back-step slide.
/// The position lerp home is ALWAYS time-budgeted to finish before the next action, so the bot is never
/// caught mid-slide. Server-driven; the animation plays on all clients via RPC.
///
/// Put this on the bot prefab (with BotController/PlayerController/PlayerCombat). Set the state-name
/// fields to your Animator states.
// Runs BEFORE PlayerController (order 0) so the safety override-clear in LateUpdate happens before the
// home-pin reads it — no 1-frame lag where the bot stays stuck at the player.
[DefaultExecutionOrder(-50)]
[RequireComponent(typeof(PlayerController))]
public class BotBeatApproach : NetworkBehaviour
{
    [Header("Animator STATE names (match your Animator exactly)")]
    public string runState      = "Run";        // run-in
    public string knockbackState= "Knockback";  // got hit (EXCELLENT/BAD shout) → flung back
    public string hurt2State    = "Hurt 2";     // got hit off a GOOD-timed shout → staggers to the closer rest spot
    public string backDashState = "BackDash";   // landed a hit / defended / clash → retreat
    public string idleState     = "Boxing Idle";// neutral fallback

    [Header("Geometry / timing")]
    [Tooltip("How close to the player the bot ends up at the peak of the run (metres in front of them).")]
    public float approachStop = 2.2f;
    [Tooltip("Max distance (m) the bot steps in from its spawn toward the attack point. Set this high " +
             "(or leave large) when the spawn is FAR — the bot must be able to cover the whole gap to " +
             "reach the attack point on the beat. 99 = no cap (always reach the point).")]
    public float maxApproachDistance = 99f;
    [Tooltip("Base playback speed of the RUN animation. The run is auto-sped to match how fast the bot " +
             "actually has to travel (far/slow beat = calmer, near/fast beat = quicker), using this as " +
             "the reference at a 'normal' approach pace.")]
    [Range(0.3f, 3f)] public float runAnimSpeed = 1.4f;
    [Tooltip("Upper cap on run animation playback speed (raised so fast beats can really churn the legs).")]
    [Range(1f, 4f)] public float maxRunAnimSpeed = 3f;
    [Tooltip("Extra run-speed multiplier applied when the approach window is very SHORT (fast/dense beat). " +
             "1 = no extra. 1.8 = up to 80% faster legs on the tightest beats, scaling in as the window shrinks.")]
    [Range(1f, 3f)] public float fastBeatBoost = 1.8f;
    [Tooltip("Seconds the bot ARRIVES at the attack point BEFORE the beat. Bigger = it plants earlier, " +
             "giving the card's move animation more time to play out and finish before the beat lands " +
             "(instead of still sliding in / cutting the swing short).")]
    [Range(0f, 1f)] public float arriveEarly = 0.7f;
    [Tooltip("The bot self-drives: as soon as it's home/idle it starts running to the attack point for " +
             "the NEXT beat, pacing its speed to the time left. This caps how early it'll set off so on a " +
             "slow (far-apart) beat it doesn't sprint in and then stand waiting — it idles a moment, then " +
             "runs. On normal/dense beats the gap is shorter than this, so it moves continuously.")]
    public float maxLeadTime = 2.0f;
    [Tooltip("Seconds the bot STAYS PLANTED on the attack point after the beat — the recovery animation " +
             "(hurt / BackDash) plays out in place for this whole time before it moves. Then it slides " +
             "home. Set to 0.4 so it doesn't zap back the instant the beat resolves.")]
    [Range(0f, 0.8f)] public float postBeatHold = 0.4f;
    [Tooltip("Seconds the home slide takes once the post-beat hold ends. Quick glide back to spawn.")]
    [Range(0.05f, 0.5f)] public float returnSlideTime = 0.18f;

    // Defended = the bot played a defensive move (block/parry/dodge) and successfully avoided damage —
    // treated as a GOOD outcome, so it gets the same confident back-dash recovery as a landed hit.
    // GotHit = hit off an EXCELLENT shout (classic knockback home). GotHitGood = hit off a GOOD shout
    // ("Hurt 2", rests at the closer botGoodHitPosition). GotHitBad = hit off a BAD shout (knockback,
    // rests at botBadOrNoHitPosition — shared with all the no-damage outcomes).
    public enum Outcome { Pending, GotHit, GotHitGood, GotHitBad, LandedHit, Defended, Clash }
    private Outcome _outcome = Outcome.Pending;

    [Tooltip("Assumed travel speed (m/s) used to compute how early the bot must set off when it's " +
             "resting CLOSE to the attack point (good/bad rest spots) — instead of the flat maxLeadTime " +
             "it uses from its far-away home. ~3 matches the run animation's reference pace.")]
    public float closeRestSpeedMps = 3f;

    private PlayerController _pc;
    private PlayerCombat _combat;
    private Coroutine _routine;
    private bool _active;
    private float _startedForBeat = -1f; // the beat time we've already kicked a run-in off for (avoid restarts)
    private float _beatTrackTime = -1f;  // this run-in's target beat (track time), for wind-up timing checks

    public bool IsApproaching => _active;

    private void Awake()
    {
        _pc = GetComponent<PlayerController>();
        _combat = GetComponent<PlayerCombat>();
    }

    private void OnDisable() => CancelApproach();

    // Stop an in-progress approach immediately and send the bot back to its home pin.
    public void CancelApproach()
    {
        if (_routine != null) StopCoroutine(_routine);
        _routine = null;
        _active = false;
        _startedForBeat = -1f; // allow the next beat to re-trigger a fresh self-driven run
        _pc?.ClearApproachOverride();
    }

    // The opponent's head/camera position in world space. Falls back to the body transform
    // if the player has no camera slot assigned. This lets the bot look at / step toward the
    // player's actual viewpoint instead of their feet.
    // In VR/host mode, we use the live HMD position when it's available.
    private Vector3 GetOpponentLookPos()
    {
        var opp = _pc.GetOpponent();
        if (opp == null) return transform.position + transform.forward;

        if (VRCameraDriver.VRActive && opp.isLocalPlayer)
        {
            Vector3 head = VRHands.HeadPos;
            if (head != Vector3.zero) return head;
        }

        Transform cam = opp.CameraPosition;
        return cam != null ? cam.position : opp.transform.position;
    }

    /// Server: start a run-in that arrives at the player on `beatTrackTime`.
    [Server]
    public void BeginApproach(float beatTrackTime)
    {
        var rmm = RhythmRoundManager.Instance;
        if (rmm == null) return;
        float secsToBeat = beatTrackTime - rmm.GetCurrentTrackTime();
        if (secsToBeat <= 0.05f) return;

        // If a previous approach/return is still mid-flight when the next beat's run-in begins (lead time
        // can exceed the gap between beats on dense sections), the bot would creep toward the player a
        // little more each beat until it walks through them — kill the old routine and SNAP it back to
        // spawn. A CLEAN rest (home OR one of the outcome rest spots) is left alone: RunIn starts from
        // the bot's true current position, so resting closer is a legitimate starting point, not drift.
        if (_routine != null)
        {
            StopCoroutine(_routine);
            _pc.SetApproachOverride(_pc.HomePosition); // stranded mid-flight → reset clean from home
        }

        _active = true;                 // mark active immediately so LateUpdate's safety doesn't release
        _outcome = Outcome.Pending;
        _beatTrackTime = beatTrackTime; // remembered so RunIn can tell if the wind-up has fired yet
        _routine = StartCoroutine(RunIn(secsToBeat));
        // Run anim speed is set INSIDE RunIn once we know the travel distance/time, so the run reads at
        // a pace that matches the actual movement (no frantic sprint on a slow beat).
    }

    /// Server: the trade resolution tells the approach what happened so it picks the right return anim.
    [Server]
    public void ReportOutcome(Outcome outcome)
    {
        if (_active) _outcome = outcome;
    }

    private void FaceOpponent()
    {
        Vector3 lookPos = GetOpponentLookPos();
        Vector3 dir = lookPos - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(dir);
    }

    [Server]
    private IEnumerator RunIn(float secsToBeat)
    {
        var opp = _pc.GetOpponent();
        if (opp == null) yield break;
        _active = true;

        Vector3 home = _pc.HomePosition;

        // Compute the STOP point for the attack. If a manual BotAttackPosition is assigned in
        // RhythmRoundManager, use that fixed point (deterministic, never drifts). Otherwise fall
        // back to a point in front of the player's head/camera.
        Vector3 ComputeTarget()
        {
            var rmm = RhythmRoundManager.Instance;
            if (rmm != null && rmm.botAttackPosition != null)
            {
                Vector3 manual = rmm.botAttackPosition.position;
                manual.y = home.y; // keep the bot's floor height
                return manual;
            }

            Vector3 oppPos = GetOpponentLookPos();
            oppPos.y = home.y;
            Vector3 toP = oppPos - home; toP.y = 0f;
            Vector3 d = toP.sqrMagnitude > 0.0001f ? toP.normalized : transform.forward;
            Vector3 full = oppPos - d * approachStop;
            float stepDist = Mathf.Min(Vector3.Distance(home, full), maxApproachDistance);
            return home + d * stepDist;
        }

        // Start from wherever the bot is actually resting (home OR an outcome rest spot) — starting
        // from a hardcoded `home` would teleport it there the frame the run begins.
        Vector3 start = transform.position;

        // ── Travel for (almost) the ENTIRE time until the beat so the bot covers the WHOLE gap from its
        // spawn to the attack point and is planted there ON the beat — no last-second teleport. We arrive
        // a hair early (arriveEarly) so the move animation fires while the bot is already standing on the
        // point, not still sliding in. The bot moves SLOW on a far-apart (slow) beat and FAST on a close
        // (dense) beat purely because inTime shrinks — same run clip, pace matched to distance/time. ──
        float inTime = Mathf.Max(0.05f, secsToBeat - arriveEarly);

        // Scale the RUN animation playback to the real travel speed so the legs match the slide. We base
        // it on metres/second over a "normal" reference of ~3 m/s → speed 1. Clamped so it never looks
        // frozen or absurdly fast.
        float dist = Vector3.Distance(start, ComputeTarget());
        float speedMps = dist / inTime;
        // Base scale: run clip sped to match travel speed (reference ~3 m/s → runAnimSpeed). PLUS an extra
        // urgency boost the SHORTER the approach window is (fast/dense beats), so the legs really churn
        // when the bot has little time. inTime 1.2s+ = no boost; 0.3s = full fastBeatBoost on top.
        float urgency = Mathf.InverseLerp(1.2f, 0.3f, inTime);             // 0 (slow beat) → 1 (fast beat)
        float boost   = Mathf.Lerp(1f, fastBeatBoost, urgency);
        float animSpeed = Mathf.Clamp(runAnimSpeed * (speedMps / 3f) * boost, 0.6f, maxRunAnimSpeed);
        RpcPlayState(runState, 0.12f, animSpeed);

        // Position = lerp(start → target) by elapsed/inTime (linear = constant velocity). Re-aims at the
        // player's live position each frame so it heads where they actually are.
        float t = 0f;
        while (t < inTime)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / inTime);
            Vector3 target = ComputeTarget();        // track the player's current position
            _pc.SetApproachOverride(Vector3.Lerp(start, target, p));
            FaceOpponent();                          // keep facing the player while stepping in
            yield return null;
        }
        // PLANTED on the attack point. STOP moving: pin exactly on the point.
        Vector3 plant = ComputeTarget();
        _pc.SetApproachOverride(plant);
        FaceOpponent();
        // Only drop to idle if the SWING HASN'T STARTED YET. The wind-up fires at windUpTime before the
        // beat; with windUpTime > arriveEarly it fires while the bot is still running in, so by the time
        // it plants the swing is already playing — forcing idle here would STOMP it back mid-swing (why
        // the swing looked cut short). Compare the time left to the beat against windUpTime to know.
        var rmmPlant = RhythmRoundManager.Instance;
        bool swingAlreadyStarted = false;
        if (rmmPlant != null)
        {
            float secsLeftToBeat = _beatTrackTime - rmmPlant.GetCurrentTrackTime();
            swingAlreadyStarted = secsLeftToBeat <= rmmPlant.windUpTime;
        }
        if (!swingAlreadyStarted)
            RpcPlayState(idleState, 0.06f, 1f); // no swing yet → halt the run, wait in idle for the beat

        // ── WAIT FOR THE REAL OUTCOME. The bot plants ~arriveEarly (0.28s) BEFORE the beat, but the
        // trade only resolves ON the beat — so we must keep waiting past the beat until ReportOutcome
        // actually lands. (The old code capped this wait at 0.12s, which expired BEFORE the beat even
        // fired, so _outcome was always still Pending → it always took the no-hit branch and never
        // went home on an EXCELLENT hit. THAT was the bug.) We wait up to the plant time plus a margin
        // so a late network resolve still counts, then hold whatever's left of postBeatHold in place. ──
        float outcomeWait = 0f;
        float outcomeWaitCap = arriveEarly + 0.25f; // covers the arrive-early gap to the beat + margin
        while (_outcome == Outcome.Pending && outcomeWait < outcomeWaitCap)
        {
            outcomeWait += Time.unscaledDeltaTime;
            _pc.SetApproachOverride(plant); // frozen on the plant point — no chasing while it acts
            FaceOpponent();
            yield return null;
        }
        Debug.Log($"<color=magenta>[BOT REST]</color> outcome={_outcome} after {outcomeWait:F2}s → " +
                  (_outcome == Outcome.GotHit ? "HOME (excellent)" :
                   _outcome == Outcome.GotHitGood ? "GOOD spot" : "BAD/no-hit spot"));

        // ── Choose the recovery animation + REST SPOT by the outcome (now that it's real) ──
        // EXCELLENT-shout hit → classic Knockback, back to home (spawn) like always.
        // GOOD-shout hit      → "Hurt 2" stagger, rests at the CLOSER botGoodHitPosition.
        // BAD-shout hit + every no-damage beat (landed/defended/clash) → rests at botBadOrNoHitPosition.
        // Unassigned rest Transforms fall back to home, preserving the old behaviour.
        if (_outcome == Outcome.GotHit || _outcome == Outcome.GotHitBad)
            RpcPlayState(knockbackState, 0.05f, 1f); // hurt recoil
        else if (_outcome == Outcome.GotHitGood)
            RpcPlayState(hurt2State, 0.05f, 1f);     // staggered — settles closer to the player
        else
            RpcPlayState(backDashState, 0.08f, 1f);  // BackDash retreat for all non-hurt outcomes

        Vector3 restTarget = home;
        {
            var rmmRest = RhythmRoundManager.Instance;
            Transform rest = null;
            if (rmmRest != null)
            {
                if (_outcome == Outcome.GotHitGood)   rest = rmmRest.botGoodHitPosition;
                else if (_outcome != Outcome.GotHit)  rest = rmmRest.botBadOrNoHitPosition;
                // GotHit (EXCELLENT) leaves rest == null → restTarget stays home. Exactly the ask.
            }
            if (rest != null) { restTarget = rest.position; restTarget.y = home.y; } // keep floor height
        }

        // STAY PLANTED for whatever is LEFT of postBeatHold (we already spent some of it waiting for the
        // outcome) so the recovery animation plays out in place before the slide. Unscaled so the hurt
        // slow-mo can't stretch it.
        float held = Mathf.Min(outcomeWait, postBeatHold);
        while (held < postBeatHold)
        {
            held += Time.unscaledDeltaTime;
            _pc.SetApproachOverride(plant);
            FaceOpponent();
            yield return null;
        }

        // ── Slide to the REST spot. Quick, fixed glide (unscaled, so hurt slow-mo can't stall it). ──
        float returnTime = returnSlideTime;
        float r = 0f;
        // Start the return from the KNOWN attack point, NOT transform.position. Reading the live transform
        // could capture a root-motion/controller-shifted value, and lerping from there leaks a small
        // offset into the bot's resting spot every cycle — the cumulative drift. ComputeTarget() is a
        // fixed scene point, so the return is deterministic and always lands exactly on the rest spot.
        Vector3 from = ComputeTarget();
        while (r < returnTime)
        {
            r += Time.unscaledDeltaTime;
            float e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(r / returnTime));
            _pc.SetApproachOverride(Vector3.Lerp(from, restTarget, e));
            yield return null;
        }
        // Land exactly on the rest spot. HOME hands back to the normal spawn-pin; a non-home rest spot
        // deliberately LEAVES the override set so the bot actually stays there between beats — the next
        // RunIn re-overrides it anyway, and CancelApproach clears it for real interruptions.
        _pc.SetApproachOverride(restTarget);
        bool restIsHome = (restTarget - home).sqrMagnitude < 0.0001f;
        if (restIsHome) _pc.ClearApproachOverride();
        FaceOpponent();
        _active = false;
        _routine = null;
    }

    // SELF-DRIVE: the bot doesn't wait for a one-shot "go" signal anymore. Every frame on the server,
    // the moment it's free (not mid run-in/return) and idle at home, it looks at the NEXT mapped beat
    // and — if that beat is close enough (within maxLeadTime) — sets off running, pacing its speed to
    // the time remaining (RunIn does the pacing). On dense beats the gap is smaller than maxLeadTime so
    // it's basically always moving; on a slow beat it idles a beat then runs. The card decision is still
    // made by ThinkNextMove ~windUp before the beat and queued, riding along with the movement.
    [Server]
    private void Update()
    {
        if (_active || _routine != null) return;          // already running this beat
        if (_pc == null || _combat == null) return;
        if (_combat.IsHurting || _combat.IsDead || _combat.IsStaggered || _combat.HeldStaggerActive) return;

        var rmm = RhythmRoundManager.Instance;
        if (rmm == null || !rmm.isRoundActive) return;
        if (DroneRushSegment.Instance != null && DroneRushSegment.Instance.SegmentActive) return; // drone segment owns the bot

        float nextBeat = rmm.GetNextBeatTime();
        if (nextBeat <= 0f) return;
        if (nextBeat == _startedForBeat) return;          // don't re-fire for a beat we already ran

        float secsToBeat = nextBeat - rmm.GetCurrentTrackTime();
        if (secsToBeat <= 0.05f) return;                  // beat already here/passed

        // DISTANCE-CALIBRATED lead: from its far-away home the flat maxLeadTime head-start is right,
        // but resting at one of the CLOSE outcome spots the bot barely has to travel — setting off
        // maxLeadTime early would have it sprint in and stand waiting. When resting near a rest spot,
        // compute the lead it actually needs from the real distance to the attack point.
        float lead = maxLeadTime;
        if (IsRestingCloseToTarget(rmm, out float distToAttack))
            lead = Mathf.Clamp(arriveEarly + distToAttack / Mathf.Max(0.5f, closeRestSpeedMps), 0.3f, maxLeadTime);
        if (secsToBeat > lead) return;                    // too early — idle a moment first

        _startedForBeat = nextBeat;
        BeginApproach(nextBeat);
    }

    // Is the bot currently resting at (or near) one of the outcome rest spots? If so, also give the
    // real remaining distance to the attack point so the lead time can be computed from it.
    private bool IsRestingCloseToTarget(RhythmRoundManager rmm, out float distToAttack)
    {
        distToAttack = 0f;
        if (rmm == null || rmm.botAttackPosition == null) return false;
        bool near = IsNear(rmm.botGoodHitPosition) || IsNear(rmm.botBadOrNoHitPosition);
        if (!near) return false;
        Vector3 attack = rmm.botAttackPosition.position;
        attack.y = transform.position.y;
        distToAttack = Vector3.Distance(transform.position, attack);
        return true;
    }

    private bool IsNear(Transform t)
    {
        if (t == null) return false;
        Vector3 p = t.position; p.y = transform.position.y; // compare on the floor plane
        return (transform.position - p).sqrMagnitude < 0.35f * 0.35f;
    }

    // NOTE: the old LateUpdate safety net (force-clearing the override every idle frame) was REMOVED —
    // it would erase the persistent rest-spot override the same frame the bot settled there. Real
    // interruptions (disable, drone segment, round reset) still release it via CancelApproach.

    [ClientRpc]
    private void RpcPlayState(string state, float fade, float animSpeed)
    {
        if (_combat != null && _combat.animator != null && !string.IsNullOrEmpty(state))
        {
            // Root motion MUST stay off — the run/knockback/backdash clips have baked translation that
            // would shove the bot off its spawn (the slow drift). The body is driven purely by the
            // position pin/override, so kill root motion before every state we play.
            _combat.animator.applyRootMotion = false;
            _combat.animator.speed = animSpeed; // slow the run clip; restored to 1 for return states
            _combat.animator.CrossFade(state, fade);
        }
    }
}
