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
    public string runState        = "Run";          // run-in (healthy)
    public string injuredRunState = "Run_Injured";  // run-in AFTER getting hit, until a clean round
    public string knockbackState  = "Knockback";    // got hit → flung back toward home
    public string backDashState   = "BackDash";     // landed a hit → retreat
    public string idleState       = "Boxing Idle";  // neutral fallback
    [Header("Move animation STATE names (by card family)")]
    public string strikeAttackState = "Attack1";    // Strike-family move animation
    public string throwAttackState  = "Attack2";    // Throw-family move animation

    // Injured = the bot took a hit; it runs with the limp (injuredRunState) until it survives a full
    // round without being hit again, then goes back to the normal run. Server-driven.
    private bool _injured;
    private bool _hitThisRound; // did the bot take a hit during the current round?

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
    [Tooltip("Min seconds an Attack1/Attack2 swing is held at the plant before the recovery (BackDash) " +
             "crossfades over it — so the swing reads instead of being cut short. ~0.3 = a clear swing.")]
    [Range(0.1f, 0.6f)] public float attackPlayTime = 0.3f;

    // Defended = the bot played a defensive move (block/parry/dodge) and successfully avoided damage —
    // treated as a GOOD outcome, so it gets the same confident back-dash recovery as a landed hit.
    public enum Outcome { Pending, GotHit, LandedHit, Defended, Clash }
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

        // If a previous approach/return is still mid-flight when the next beat's run-in begins (lead time
        // can exceed the gap between beats on dense sections), the bot would otherwise start the new run
        // from wherever it was stranded — creeping toward the player a little more each beat until it
        // walks through them. Kill the old routine and SNAP the bot back to its spawn so every run-in
        // starts clean from home.
        if (_routine != null) StopCoroutine(_routine);
        _pc.SetApproachOverride(_pc.HomePosition); // snap home before the new run starts

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
        // Got hit → limp from now on, and mark that the bot was hit THIS round so the injured state
        // can't clear at this round's end. It clears only after a full round with no hit.
        if (outcome == Outcome.GotHit) { _injured = true; _hitThisRound = true; }
    }

    /// Server: call at the END of each round. If the bot got through the round WITHOUT being hit, it
    /// heals — back to the normal run animation. Otherwise it stays injured for the next round too.
    [Server]
    public void NotifyRoundEnded()
    {
        if (!_hitThisRound) _injured = false; // survived clean → heal
        _hitThisRound = false;                 // reset for the next round
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

        Vector3 start = home;

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
        // Use the INJURED run (limp) if the bot has been hit and hasn't survived a clean round yet.
        RpcPlayState(_injured ? injuredRunState : runState, 0.12f, animSpeed);

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

        // ── Choose & TRIGGER the recovery animation by outcome ──
        // The bot plays BackDash on EVERY beat EXCEPT when it got hit (then it's the hurt/knockback
        // recoil). So any time it does a card move and isn't hurt — landed a hit, defended, or even a
        // neutral whiff/clash — it retreats with the confident BackDash.
        if (_outcome == Outcome.GotHit)
            RpcPlayState(knockbackState, 0.05f, 1f); // hurt recoil
        else
            RpcPlayState(backDashState, 0.08f, 1f);  // BackDash retreat for all non-hurt outcomes

        // STAY PLANTED on the attack point for the full hold (default 0.4s) so the recovery animation
        // (hurt / BackDash fall-back) plays out IN PLACE before we move — no zapping back the instant the
        // beat resolves. Uses UNSCALED time so the hurt slow-motion doesn't stretch the hold. After this,
        // it slides home (the slide can overlap the tail of the animation — that's fine).
        float held = 0f;
        while (held < postBeatHold)
        {
            held += Time.unscaledDeltaTime;
            _pc.SetApproachOverride(plant);
            FaceOpponent();
            yield return null;
        }

        // ── Slide home. Quick, fixed glide (unscaled, so hurt slow-mo can't stall it). ──
        float returnTime = returnSlideTime;
        float r = 0f;
        // Start the return from the KNOWN attack point, NOT transform.position. Reading the live transform
        // could capture a root-motion/controller-shifted value, and lerping home from there leaks a small
        // offset into the bot's resting spot every cycle — the cumulative drift. ComputeTarget() is a
        // fixed scene point, so the return is deterministic and always lands exactly on home.
        Vector3 from = ComputeTarget();
        while (r < returnTime)
        {
            r += Time.unscaledDeltaTime;
            float e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(r / returnTime));
            _pc.SetApproachOverride(Vector3.Lerp(from, home, e));
            yield return null;
        }
        // Snap exactly home and HAND BACK to the normal spawn-pin.
        _pc.SetApproachOverride(home);
        _pc.ClearApproachOverride();
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
        if (PCDroneSegment.AnySegmentActive) return; // a drone segment owns the bot (VR or PC)

        float nextBeat = rmm.GetNextBeatTime();
        if (nextBeat <= 0f) return;
        if (nextBeat == _startedForBeat) return;          // don't re-fire for a beat we already ran

        float secsToBeat = nextBeat - rmm.GetCurrentTrackTime();
        if (secsToBeat <= 0.05f) return;                  // beat already here/passed
        if (secsToBeat > maxLeadTime) return;             // too early — idle a moment first

        _startedForBeat = nextBeat;
        BeginApproach(nextBeat);
    }

    // SAFETY NET: if the routine ever gets killed mid-flight (StopCoroutine on a new approach, the bot
    // being staggered/disabled, the round ending…), the approach override could be left stuck pointing
    // at the player → the bot freezes near you. This guarantees it's released the moment we're not
    // actively approaching, so the normal home-pin always takes back over.
    private void LateUpdate()
    {
        if (!_active && _routine == null && _pc != null)
            _pc.ClearApproachOverride();
    }


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
