using System.Collections;
using Mirror;
using UnityEngine;

/// The bot's beat RUN-IN telegraph + motivated RETURN. On an attack beat the bot charges from its
/// current rest spot toward the player, arriving exactly ON the beat. It holds there while the trade
/// resolves, then returns to a REST POSITION chosen by the OUTCOME (and, for a got-hit, by how well the
/// human's shout was timed — read from PlayerCombat.LastTimingRating):
///   • EXCELLENT-timed hit → hard KNOCKBACK, flies to RhythmRoundManager.botHardHitPosition.
///   • GOOD-timed hit      → lighter "Hurt 2" reaction, settles at RhythmRoundManager.botGoodHitPosition.
///   • BAD-timed hit       → classic knockback recoil, stays at home (no special fly-off).
///   • landed/defended/clash → confident BACK-DASH to RhythmRoundManager.botNoHitPosition.
/// Any of those position Transforms can be left unassigned in the inspector, in which case that outcome
/// falls back to the bot's normal home spot. The slide is ALWAYS time-budgeted to finish before the next
/// action, so the bot is never caught mid-slide. Server-driven; the animation plays on all clients via RPC.
///
/// Put this on the bot prefab (with BotController/PlayerController/PlayerCombat). Set the state-name
/// fields to your Animator states, and the three botXxxPosition Transforms on RhythmRoundManager.
[DefaultExecutionOrder(-50)]
[RequireComponent(typeof(PlayerController))]
public class BotBeatApproach : NetworkBehaviour
{
    [Header("Animator STATE names (match your Animator exactly)")]
    public string runState        = "Run";          // run-in (healthy)
    public string injuredRunState = "Run_Injured";  // run-in AFTER getting hit, until a clean round
    public string knockbackState  = "Knockback";    // got hit EXCELLENT → hard knockback, flies far
    public string hurt2State      = "Hurt 2";       // got hit GOOD (not excellent/bad) → lighter hurt reaction
    public string backDashState   = "BackDash";     // landed a hit / defended / clash → retreat
    public string idleState       = "Boxing Idle";  // neutral fallback
    [Header("Move animation STATE names (by card family)")]
    public string strikeAttackState = "Attack1";    // Strike-family move animation
    public string throwAttackState  = "Attack2";    // Throw-family move animation

    // Injured = the bot was just hit with EXCELLENT shout timing; it limps (injuredRunState) for
    // exactly the NEXT run-in, then clears automatically. Server-driven.
    private bool _injured;

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
    [Range(0f, 0.6f)] public float arriveEarly = 0.28f;
    [Tooltip("Reference travel speed (m/s) used to estimate how much lead time is actually needed when the " +
             "bot is resting close to the attack point (good-hit / no-hit rest spots) — matches the ~3 m/s " +
             "the run animation is scaled against in RunIn, so the estimate lines up with the real pace.")]
    public float closeRestSpeedMps = 3f;
    [Tooltip("The bot self-drives: as soon as it's home/idle it starts running to the attack point for " +
             "the NEXT beat, pacing its speed to the time left. This caps how early it'll set off so on a " +
             "slow (far-apart) beat it doesn't sprint in and then stand waiting — it idles a moment, then " +
             "runs. On normal/dense beats the gap is shorter than this, so it moves continuously. Only " +
             "applies from HOME/hard-hit rest — when resting at the good-hit/no-hit spot (already close), " +
             "the lead time is instead calculated from the real distance (see closeRestSpeedMps).")]
    public float maxLeadTime = 2.0f;
    [Tooltip("Seconds the bot STAYS PLANTED on the attack point after the beat — the recovery animation " +
             "(hurt / BackDash) plays out in place for this whole time before it moves. Then it slides " +
             "home. Set to 0.4 so it doesn't zap back the instant the beat resolves.")]
    [Range(0f, 0.8f)] public float postBeatHold = 0.4f;
    [Tooltip("Seconds the home slide takes once the post-beat hold ends. Quick glide back to spawn.")]
    [Range(0.05f, 0.5f)] public float returnSlideTime = 0.18f;
    [Tooltip("Min seconds an Attack1/Attack2 swing is held at the plant before the recovery (BackDash) " +
             "crossfades over it — so the swing reads instead of being cut short. ~0.3 = a clear swing.")]
    [Range(0.1f, 0.6f)] public float attackPlayTime = 0.3f;

    // Defended = the bot played a defensive move (block/parry/dodge) and successfully avoided damage —
    // treated as a GOOD outcome, so it gets the same confident back-dash recovery as a landed hit.
    // GotHitExcellent/GotHitGood split the old single GotHit by the ATTACKER's shout-timing rating:
    // EXCELLENT → hard knockback to hardHitPosition; GOOD → lighter Hurt2 to goodHitPosition;
    // BAD-timed hits still land (damage doesn't care about timing) but keep the classic GotHit reaction.
    public enum Outcome { Pending, GotHit, GotHitExcellent, GotHitGood, LandedHit, Defended, Clash }
    private Outcome _outcome = Outcome.Pending;

    private PlayerController _pc;
    private PlayerCombat _combat;
    private CardManager _cards;
    private Coroutine _routine;
    private bool _active;
    private float _startedForBeat = -1f; // the beat time we've already kicked a run-in off for (avoid restarts)

    public bool IsApproaching => _active;

    private void Awake()
    {
        _pc = GetComponent<PlayerController>();
        _combat = GetComponent<PlayerCombat>();
        _cards = GetComponent<CardManager>();
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

        // If a previous approach/return is still mid-FLIGHT (actively moving) when the next beat's run-in
        // begins (lead time can exceed the gap between beats on dense sections), the bot would otherwise
        // start the new run from wherever it was stranded mid-slide — creeping toward the player a little
        // more each beat until it walks through them. Only snap home in THAT case. If the bot is simply
        // RESTING (previous run-in finished cleanly and left it at a hard-hit/good-hit/no-hit/home spot),
        // don't snap — RunIn starts from transform.position, i.e. wherever it's actually resting, which is
        // exactly the point of the outcome-based rest positions.
        if (_routine != null)
        {
            StopCoroutine(_routine);
            _pc.SetApproachOverride(_pc.HomePosition); // was genuinely mid-flight — snap home to recover
        }

        _active = true;                 // mark active immediately so LateUpdate's safety doesn't release
        _outcome = Outcome.Pending;
        _routine = StartCoroutine(RunIn(secsToBeat));
        // Run anim speed is set INSIDE RunIn once we know the travel distance/time, so the run reads at
        // a pace that matches the actual movement (no frantic sprint on a slow beat).
    }

    /// Server: the trade resolution tells the approach what happened so it picks the right return anim.
    [Server]
    public void ReportOutcome(Outcome outcome)
    {
        if (_active) _outcome = outcome;
        // Only an EXCELLENT-timed hit triggers the injured limp — and only for the SINGLE next run-in
        // (consumed in RunIn as soon as it's used). GOOD/BAD-timed hits don't limp at all.
        if (outcome == Outcome.GotHitExcellent) _injured = true;
    }

    /// Server: call at the END of each round. Kept for compatibility with existing callers; the injured
    /// limp is now consumed after one run rather than tracked per-round, so this just clears any leftover
    /// flag as a safety net.
    [Server]
    public void NotifyRoundEnded()
    {
        _injured = false;
    }

    /// Server: the move animation STATE for the bot's CURRENTLY-QUEUED card, by family.
    ///   Strike → strikeAttackState (Attack1),  Throw → throwAttackState (Attack2),  else "" (idle).
    private string MoveAnimStateForChosen()
    {
        if (_combat == null) return "";
        string trigger = _combat.PeekNextMove().attack;
        if (string.IsNullOrEmpty(trigger) || _cards == null) return "";
        CardFamily fam = _cards.FamilyOfTrigger(trigger);
        if (fam == CardFamily.Strike) return strikeAttackState;
        if (fam == CardFamily.Throw)  return throwAttackState;
        return ""; // block / parry / support / dodge — no melee attack anim
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

        // Start from wherever the bot is ACTUALLY resting — not always `home`. Since the last beat's
        // outcome may have left it at a hard-hit/good-hit/no-hit position instead of home, transform.
        // position (pinned every LateUpdate to the last SetApproachOverride) is the true current rest
        // spot. Using `home` here would teleport it back to home the instant this run-in starts.
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
        // Use the INJURED run (limp) for exactly ONE run-in right after an EXCELLENT-timed hit, then
        // clear it — back to the normal run on the very next approach, not waiting for round end.
        RpcPlayState(_injured ? injuredRunState : runState, 0.12f, animSpeed);
        _injured = false;

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
        // PLANTED on the attack point. STOP moving: pin exactly on the point. Then fire the MOVE animation
        // by the chosen card's FAMILY — Strike → Attack1, Throw → Attack2 — from a standstill, exactly on
        // the beat (driven HERE so the trigger lands on time and never gets skipped by a competing anim).
        // Non-Strike/Throw moves (block/parry/support/dodge) just halt to idle and resolve in place.
        Vector3 plant = ComputeTarget();
        _pc.SetApproachOverride(plant);
        FaceOpponent();

        string moveState = MoveAnimStateForChosen(); // Attack1 / Attack2 / "" (idle)
        if (!string.IsNullOrEmpty(moveState))
            RpcPlayState(moveState, 0.04f, 1f);      // snap the attack on, fast crossfade so it can't be skipped
        else
            RpcPlayState(idleState, 0.06f, 1f);      // halt the run; defense/support resolves in place

        // ── Hold at the player while the move plays out + the trade outcome arrives. If an ATTACK anim
        // (Attack1/Attack2) is playing, hold a bit longer so the swing READS before the recovery anim
        // crossfades over it (otherwise BackDash cuts the swing short). ──
        float minPlay = string.IsNullOrEmpty(moveState) ? 0.10f : attackPlayTime;
        float waited = 0f;
        while ((_outcome == Outcome.Pending || waited < minPlay) && waited < minPlay + 0.25f)
        {
            waited += Time.deltaTime;
            _pc.SetApproachOverride(plant); // frozen on the plant point — no chasing while it acts
            FaceOpponent();                 // still face the player
            yield return null;
        }

        // ── Choose the recovery animation AND the rest-position by outcome ──
        // EXCELLENT-timed hit → hard knockback, flies to botHardHitPosition.
        // GOOD-timed hit      → lighter Hurt2 reaction, settles at botGoodHitPosition.
        // Anything else (old plain GotHit / landed / defended / clash) → confident BackDash to
        // botNoHitPosition (landed/defended/clash) or the classic knockback recoil in place (GotHit,
        // BAD-timed hit still lands but doesn't get a special fly-off).
        var rmmPos = RhythmRoundManager.Instance;
        Vector3 restTarget = home;
        switch (_outcome)
        {
            case Outcome.GotHitExcellent:
                RpcPlayState(knockbackState, 0.05f, 1f);
                if (rmmPos != null && rmmPos.botHardHitPosition != null)
                    restTarget = HeightMatched(rmmPos.botHardHitPosition.position, home);
                break;
            case Outcome.GotHitGood:
                RpcPlayState(hurt2State, 0.05f, 1f);
                if (rmmPos != null && rmmPos.botGoodHitPosition != null)
                    restTarget = HeightMatched(rmmPos.botGoodHitPosition.position, home);
                break;
            case Outcome.GotHit:
                RpcPlayState(knockbackState, 0.05f, 1f); // hurt recoil (BAD-timed hit, no special fly-off)
                break;
            default: // LandedHit, Defended, Clash
                RpcPlayState(backDashState, 0.08f, 1f);
                if (rmmPos != null && rmmPos.botNoHitPosition != null)
                    restTarget = HeightMatched(rmmPos.botNoHitPosition.position, home);
                break;
        }

        // STAY PLANTED on the attack point for the full hold (default 0.4s) so the recovery animation
        // (hurt / BackDash fall-back) plays out IN PLACE before we move — no zapping back the instant the
        // beat resolves. Uses UNSCALED time so the hurt slow-motion doesn't stretch the hold. After this,
        // it slides to the chosen rest position (the slide can overlap the tail of the animation — fine).
        float held = 0f;
        while (held < postBeatHold)
        {
            held += Time.unscaledDeltaTime;
            _pc.SetApproachOverride(plant);
            FaceOpponent();
            yield return null;
        }

        // ── Slide to the rest position. Quick, fixed glide (unscaled, so hurt slow-mo can't stall it). ──
        float returnTime = returnSlideTime;
        float r = 0f;
        // Start the return from the KNOWN attack point, NOT transform.position. Reading the live transform
        // could capture a root-motion/controller-shifted value, and lerping from there leaks a small
        // offset into the bot's resting spot every cycle — the cumulative drift. ComputeTarget() is a
        // fixed scene point, so the return is deterministic and always lands exactly on the target.
        Vector3 from = ComputeTarget();
        while (r < returnTime)
        {
            r += Time.unscaledDeltaTime;
            float e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(r / returnTime));
            _pc.SetApproachOverride(Vector3.Lerp(from, restTarget, e));
            yield return null;
        }
        // Rest at the chosen position — KEEP the approach override set (do NOT ClearApproachOverride)
        // so PlayerController's home-pin doesn't snap it back to HomePosition. The override stays until
        // the NEXT RunIn starts (which immediately re-overrides it for the new run-in anyway).
        _pc.SetApproachOverride(restTarget);
        FaceOpponent();
        _active = false;
        _routine = null;
    }

    // Keeps the target's X/Z but the bot's own home Y, so an inspector-placed empty at the wrong height
    // doesn't sink/float the bot off the floor.
    private static Vector3 HeightMatched(Vector3 target, Vector3 home)
    {
        target.y = home.y;
        return target;
    }

    // True if the bot is CURRENTLY resting at the good-hit or no-hit position (both meant to sit close
    // to the attack point). If so, outputs the ACTUAL lead time needed to cover the real remaining
    // distance (arriveEarly + distance/closeRestSpeedMps, floored at a small minimum) instead of the
    // flat maxLeadTime, so it doesn't set off early from a spot it's already almost standing on.
    private bool IsRestingCloseToTarget(RhythmRoundManager rmm, out float neededLead)
    {
        neededLead = maxLeadTime;
        Transform good = rmm.botGoodHitPosition;
        Transform none = rmm.botNoHitPosition;
        if (good == null && none == null) return false;

        const float RESTING_TOLERANCE = 0.35f; // metres — "am I basically standing at that spot"
        Vector3 pos = transform.position;
        bool atGood = good != null && Vector3.Distance(pos, HeightMatched(good.position, pos)) <= RESTING_TOLERANCE;
        bool atNone = none != null && Vector3.Distance(pos, HeightMatched(none.position, pos)) <= RESTING_TOLERANCE;
        if (!atGood && !atNone) return false;

        Vector3 attackPoint = rmm.botAttackPosition != null
            ? HeightMatched(rmm.botAttackPosition.position, pos)
            : GetOpponentLookPos(); // fall back to a rough estimate — still much better than the flat cap
        float dist = Vector3.Distance(pos, attackPoint);
        neededLead = Mathf.Max(0.3f, arriveEarly + dist / Mathf.Max(0.1f, closeRestSpeedMps));
        return true;
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
        if (DroneRushSegment.AnySegmentActive) return; // the drone segment owns the bot

        float nextBeat = rmm.GetNextBeatTime();
        if (nextBeat <= 0f) return;
        if (nextBeat == _startedForBeat) return;          // don't re-fire for a beat we already ran

        float secsToBeat = nextBeat - rmm.GetCurrentTrackTime();
        if (secsToBeat <= 0.05f) return;                  // beat already here/passed

        // If the bot is currently resting at the GOOD-HIT or NO-HIT position (both intentionally close
        // to the attack point), it doesn't need the full maxLeadTime head start — calculate the actual
        // lead needed from the real remaining distance instead, so it doesn't start jogging absurdly
        // early from a spot it's already almost standing on.
        float allowedLead = maxLeadTime;
        if (IsRestingCloseToTarget(rmm, out float neededLead))
            allowedLead = neededLead;

        if (secsToBeat > allowedLead) return;             // too early — idle a moment first

        _startedForBeat = nextBeat;
        BeginApproach(nextBeat);
    }

    // NOTE: there used to be a LateUpdate safety net here that force-cleared the approach override
    // whenever the bot was idle (!_active && _routine == null) — that was to guard against a routine
    // being killed mid-flight and leaving the bot stuck at the player. But it also fought the new
    // hit-reaction rest positions: RunIn now deliberately leaves the override SET at the chosen rest
    // spot (hard-hit / good-hit / no-hit position) instead of HomePosition, and this safety net would
    // snap it back to HomePosition every single frame. CancelApproach() already explicitly clears the
    // override for the real interruption cases (disable, drone segment, round end), so the net was
    // redundant for those and actively harmful for the normal resting case. Removed.


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
