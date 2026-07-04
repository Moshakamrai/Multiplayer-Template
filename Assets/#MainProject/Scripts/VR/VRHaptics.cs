using System.Collections;
using UnityEngine;
using UnityEngine.XR;

/// One-stop VR controller vibration for combat juice. Static so any system can fire an effect
/// with one line — every method silently no-ops on flat-screen (no headset) or if the controller
/// doesn't support impulses, so callers never need to gate.
///
/// Full vocabulary:
///   CardCue         — tick on the hand that must fire the card.
///   CardIdleRumble  — gentle hum on cue hand while card is pending (call per-frame).
///   BeatApproach    — escalating taps as beat nears (call per-frame with approach 0→1).
///   BeatTick        — solid tap on the beat.
///   FullCharge      — double-blip when charge bar maxes.
///   LockGood        — crisp double-tap on tight lock-in.
///   LockBad         — mushy fizzle on sloppy lock-in.
///   StrikeLanded    — crack on striking hand + transfer shudder on both.
///   GotHit          — shockwave spike → rapid decay, scaled by damage.
///   BlockSuccess    — solid thud on left (block) hand only.
///   GotParried      — hard clang + stutter fade.
///   ParrySuccess    — "shing" on left hand.
///   StaggerStart    — disorienting rapid double-buzz.
///   StaggerRecovery — rising reward taps as you mash out.
///   Knockout        — long decaying rumble.
///   Victory         — rising triple tick.
public static class VRHaptics
{
    public enum Hand { Left, Right, Both }

    /// Master switch — expose in a settings menu if needed.
    public static bool Enabled = true;

    /// Global strength multiplier for all haptic effects.
    public static float StrengthMultiplier = 3f;

    // ── Low-level ─────────────────────────────────────────────────────────

    private static bool Ready => Enabled && XRSettings.isDeviceActive;

    /// One-shot impulse. amplitude 0–1, duration in seconds.
    public static void Pulse(Hand hand, float amplitude, float duration)
    {
        if (!Ready) return;
        amplitude = Mathf.Clamp01(amplitude * StrengthMultiplier);
        duration *= StrengthMultiplier;
        if (hand != Hand.Right) Send(XRNode.LeftHand,  amplitude, duration);
        if (hand != Hand.Left)  Send(XRNode.RightHand, amplitude, duration);
    }

    /// Continuous rumble — call every frame; each call covers ~1 frame's worth.
    public static void Rumble(Hand hand, float amplitude) => Pulse(hand, amplitude, 0.06f);

    private static void Send(XRNode node, float amp, float dur)
    {
        InputDevice d = InputDevices.GetDeviceAtXRNode(node);
        if (d.isValid && d.TryGetHapticCapabilities(out HapticCapabilities caps) && caps.supportsImpulse)
            d.SendHapticImpulse(0u, amp, dur);
    }

    // ── Named effects ─────────────────────────────────────────────────────

    /// New card picked — tick on the controller that must fire it.
    public static void CardCue(Hand hand) => Pulse(hand, 0.59f, 0.06f);

    /// Gentle hum on cue hand while a card is pending. Fades out as approach taps take over.
    public static void CardIdleRumble(Hand hand, float approach)
    {
        if (!Ready) return;
        float amp = Mathf.Lerp(0.14f, 0f, approach);
        if (amp > 0.01f) Rumble(hand, amp);
    }

    /// Escalating approach taps in the window before the beat (call per-frame).
    public static void BeatApproach(Hand hand, float approach)
    {
        if (!Ready || approach <= 0f) return;
        float now = Time.time;
        float interval = Mathf.Lerp(0.28f, 0.09f, approach);
        if (now - _lastApproachTap >= interval)
        {
            _lastApproachTap = now;
            Pulse(hand, Mathf.Lerp(0.17f, 0.67f, approach), Mathf.Lerp(0.03f, 0.06f, approach));
        }
    }
    private static float _lastApproachTap = -1f;

    public static void ResetBeatApproach() => _lastApproachTap = -1f;

    /// Solid tap on the beat.
    public static void BeatTick(Hand hand, float strength = 0.5f)
        => Pulse(hand, Mathf.Lerp(0.25f, 0.92f, Mathf.Clamp01(strength)), 0.05f);

    /// Double-blip when charge bar maxes.
    public static void FullCharge(Hand hand) => Play(FullChargeSeq(hand));

    /// Tight lock-in: crisp double-tap, sharper the closer to the beat.
    public static void LockGood(Hand hand, float closeness) => Play(LockGoodSeq(hand, closeness));

    /// Sloppy lock-in: long mushy buzz.
    public static void LockBad(Hand hand) => Pulse(hand, 0.36f, 0.40f);

    /// YOUR punch landed: hard crack on the STRIKING hand (right for offense), then a body-transfer
    /// shudder on both as the weight follows through. Feels like your fist connecting, not a generic buzz.
    public static void StrikeLanded(Hand strikingHand = Hand.Right) => Play(StrikeSeq(strikingHand));

    /// YOU took a hit: shockwave spike → rapid staggered decay, scaled by damage.
    /// Heavy hits add a low aftershock so you feel the full weight of big moves.
    public static void GotHit(float damage01) => Play(GotHitSeq(Mathf.Clamp01(damage01)));

    /// Successful block: solid deflection thud on the LEFT (block) hand. Right hand untouched —
    /// asymmetry tells your body which arm absorbed the hit.
    public static void BlockSuccess() => Play(BlockSeq());

    /// Your attack got parried: hard clang then stutter fade as the bolt flies back at you.
    public static void GotParried() => Play(GotParriedSeq());

    /// You reflected an attack: satisfying "shing" snap on the LEFT (parry) hand.
    public static void ParrySuccess() => Play(ParrySeq());

    /// You entered stagger: disorienting rapid double-buzz on both hands.
    public static void StaggerStart() => Play(StaggerStartSeq());

    /// You broke out of stagger: rising reward taps — you earned it.
    public static void StaggerEscape() => Play(StaggerEscapeSeq());

    /// You got knocked out: long rumble decaying to silence.
    public static void Knockout() => Play(KnockoutSeq());

    /// Drone-rush BALL PUNCH: a single sharp IMPACT JOLT (not a buzz), harder the more power you put
    /// in — knuckle crack → fast decay. This is the "I hit something solid" feel.
    public static void BallImpact(Hand strikingHand, float power01) => Play(BallImpactSeq(strikingHand, Mathf.Clamp01(power01)));

    /// One crisp tick the instant the ball enters strike range — a "NOW" cue, fired ONCE per ball
    /// (not every frame), so the continuous approach buzz is gone.
    public static void BallReady(Hand hand) => Pulse(hand, 0.5f, 0.05f);

    /// Opponent knocked out: rising triple tick.
    public static void Victory() => Play(VictorySeq());

    // ── Pattern sequences ──────────────────────────────────────────────────

    private static IEnumerator FullChargeSeq(Hand h)
    {
        Pulse(h, 1.0f,  0.07f);
        yield return new WaitForSeconds(0.09f);
        Pulse(h, 1.0f,  0.10f);
    }

    private static IEnumerator LockGoodSeq(Hand h, float closeness)
    {
        float s = Mathf.Lerp(0.7f, 1f, Mathf.Clamp01(closeness));
        Pulse(h, 1.0f  * s, 0.10f);
        yield return new WaitForSeconds(0.10f);
        Pulse(h, 0.70f * s, 0.07f);
    }

    // Crack on the striking hand first, then a softer both-hand shudder (weight transfer).
    private static IEnumerator StrikeSeq(Hand strikingHand)
    {
        Pulse(strikingHand, 1.0f, 0.10f);        // knuckle impact — hard and short
        yield return new WaitForSeconds(0.07f);
        Pulse(Hand.Both,    0.55f, 0.08f);        // body recoil — both hands feel the follow-through
        yield return new WaitForSeconds(0.09f);
        Pulse(strikingHand, 0.28f, 0.06f);        // settling echo on strike hand
    }

    // Shockwave: sharp spike → three decaying pulses to simulate impact radiating up the arm.
    private static IEnumerator GotHitSeq(float dmg)
    {
        float peak = Mathf.Lerp(0.65f, 1.0f, dmg);
        Pulse(Hand.Both, peak, 0.08f);            // initial spike — short and hard
        yield return new WaitForSeconds(0.07f);
        Pulse(Hand.Both, peak * 0.75f, 0.07f);   // first decay
        yield return new WaitForSeconds(0.06f);
        Pulse(Hand.Both, peak * 0.45f, 0.06f);   // second decay
        yield return new WaitForSeconds(0.06f);
        Pulse(Hand.Both, peak * 0.20f, 0.05f);   // tail
        if (dmg > 0.55f)                          // heavy hit: low-freq aftershock
        {
            yield return new WaitForSeconds(0.14f);
            Pulse(Hand.Both, 0.60f, 0.20f);
        }
    }

    // Block: single solid thud on the left hand only — asymmetry = physical convincingness.
    private static IEnumerator BlockSeq()
    {
        Pulse(Hand.Left, 1.0f, 0.10f);
        yield return new WaitForSeconds(0.12f);
        Pulse(Hand.Left, 0.40f, 0.07f);          // small rebound
    }

    private static IEnumerator GotParriedSeq()
    {
        Pulse(Hand.Both, 1.0f, 0.08f);            // clang
        yield return new WaitForSeconds(0.11f);
        for (int i = 0; i < 4; i++)
        {
            Pulse(Hand.Both, 0.70f - i * 0.16f, 0.06f);
            yield return new WaitForSeconds(0.07f);
        }
    }

    private static IEnumerator ParrySeq()
    {
        Pulse(Hand.Left, 0.55f, 0.05f);
        yield return new WaitForSeconds(0.05f);
        Pulse(Hand.Left, 1.0f,  0.14f);          // the shing — longer so it registers clearly
        yield return new WaitForSeconds(0.10f);
        Pulse(Hand.Left, 0.35f, 0.06f);          // ring-out
    }

    // Two rapid buzzes close together — disorienting, not rhythmic.
    private static IEnumerator StaggerStartSeq()
    {
        Pulse(Hand.Both, 1.0f, 0.07f);
        yield return new WaitForSeconds(0.06f);
        Pulse(Hand.Both, 0.85f, 0.07f);
        yield return new WaitForSeconds(0.10f);
        Pulse(Hand.Both, 0.50f, 0.12f);          // sustained low buzz — you're dazed
    }

    // Rising taps: quiet→louder, signals effort and rewards mashing out.
    private static IEnumerator StaggerEscapeSeq()
    {
        for (int i = 0; i < 4; i++)
        {
            Pulse(Hand.Both, Mathf.Lerp(0.30f, 1.0f, i / 3f), 0.06f);
            yield return new WaitForSeconds(0.07f);
        }
    }

    // A solid ball strike: hard knuckle crack on the striking hand scaled by power, then two quick
    // decays so it reads as ONE impact that radiates and dies — never a sustained rumble.
    private static IEnumerator BallImpactSeq(Hand strikingHand, float power)
    {
        float peak = Mathf.Lerp(0.6f, 1.0f, power);
        Pulse(strikingHand, peak, 0.09f);            // the crack
        yield return new WaitForSeconds(0.05f);
        Pulse(strikingHand, peak * 0.5f, 0.05f);     // first decay
        yield return new WaitForSeconds(0.05f);
        Pulse(strikingHand, peak * 0.22f, 0.04f);    // tail
    }

    private static IEnumerator KnockoutSeq()
    {
        for (int i = 0; i < 6; i++)
        {
            Pulse(Hand.Both, 1.0f - i * 0.16f, 0.22f);
            yield return new WaitForSeconds(0.24f);
        }
    }

    private static IEnumerator VictorySeq()
    {
        Pulse(Hand.Both, 0.50f, 0.07f);
        yield return new WaitForSeconds(0.13f);
        Pulse(Hand.Both, 0.75f, 0.07f);
        yield return new WaitForSeconds(0.13f);
        Pulse(Hand.Both, 1.0f,  0.14f);
    }

    // ── Coroutine runner ──────────────────────────────────────────────────

    private class HapticRunner : MonoBehaviour { }
    private static HapticRunner _runner;

    private static void Play(IEnumerator seq)
    {
        if (!Ready) return;
        if (_runner == null)
        {
            var go = new GameObject("~VRHaptics");
            Object.DontDestroyOnLoad(go);
            _runner = go.AddComponent<HapticRunner>();
        }
        _runner.StartCoroutine(seq);
    }
}
