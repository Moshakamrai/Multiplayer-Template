using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// The ~20s "drone rush" reward segment (VR). Triggered when the player's score crosses a threshold:
///   • the bot drops into a no-damage STAGGER for the whole segment,
///   • a PORTAL opens behind/near the bot,
///   • drones fly OUT of the portal toward the player one after another, each timed to ARRIVE on a beat,
///   • the player PUNCHES them (bounces to bot = damage+points, scaled by punch power + on-beat),
///     DODGES with their head (small save points), or MISSES (screen sparks, no real damage),
///   • a combo multiplier builds within the segment; the final drone is a big finisher,
///   • after ~20s the portal closes and the bot recovers.
///
/// Server drives the segment + applies all damage/score (authoritative). The actual drone flight and
/// VR punch/dodge detection run on the LOCAL client (where hand tracking lives) and report outcomes
/// back via Command. No real collision — distance + beat-timing checks only.
public class DroneRushSegment : NetworkBehaviour
{
    public static DroneRushSegment Instance;

    [Header("Prefabs (assign your portal + drone)")]
    [Tooltip("Portal prefab spawned near the bot for the segment.")]
    public GameObject portalPrefab;
    [Tooltip("Drone prefab — must have the Drone component + a TrailRenderer.")]
    public GameObject dronePrefab;

    [Header("Trigger")]
    [Tooltip("Score the player must reach to trigger the segment (moderate). One-shot per round.")]
    public int scoreTrigger = 40000;

    [Header("Timing")]
    [Tooltip("How long the whole segment lasts (seconds).")]
    public float segmentDuration = 20f;
    [Tooltip("Seconds a drone spends flying from the portal to the player.")]
    public float droneTravelTime = 1.35f;
    [Tooltip("Gap (seconds) between launching one drone and the next.")]
    public float spawnGap = 1.7f;

    [Header("Scoring")]
    public int dronePunchPoints = 1500;   // base, ×power ×onBeat ×combo
    public int droneDodgePoints = 400;    // a clean head-dodge save
    public int finisherPoints   = 6000;
    public int droneBaseDamage  = 14;     // bot damage per bounced hit (×power)
    [Tooltip("Points the BOT gets when a drone gets past the player and hits them (a miss).")]
    public int droneMissBotPoints = 1200;

    [Header("Portal placement")]
    public float portalBehindBot = 1.6f;
    public float portalHeight    = 1.6f;

    [Header("Approach variety")]
    [Tooltip("Random HEIGHT variation (m) for each drone, so they're not all at the exact same spot.")]
    public float verticalSpread = 0.4f;
    [Tooltip("Small left/right X offset (m) alternated per drone — mostly middle, just nudged aside so " +
             "they don't stack. Keep small (the player should still reach all of them easily).")]
    public float lateralOffset = 0.45f;
    [Tooltip("Minimum seconds between successive drone ARRIVALS, so two never reach you at once — you " +
             "hit one, then the next. Bigger = more spaced out.")]
    public float minArrivalGap = 0.7f;

    private int _nextThreshold;   // next score multiple of scoreTrigger that fires a segment
    private bool _running;         // a segment is currently in progress
    private GameObject _portal;
    private int _combo;
    private float _trackToUnscaledOffset; // GetCurrentTrackTime() ≈ Time.unscaledTime + this

    [Tooltip("Don't start a new segment if the song has fewer than this many seconds left (so a rush " +
             "can't begin right as the round is about to end).")]
    public float minSongTimeLeftToStart = 6f;

    // Synced so the LOCAL player knows the segment is running and suppresses normal on-beat card moves
    // (during the rush the player should ONLY be hitting drones).
    [SyncVar] public bool SegmentActive = false;

    private void Awake() { if (Instance == null) Instance = this; }

    // Called by PlayerCombat on the SERVER when a fighter's score changes. Fires a segment EVERY time
    // the score crosses a new multiple of scoreTrigger (40k, 80k, 120k…), not just once.
    [Server]
    public void NotifyScore(PlayerCombat pc, int newScore)
    {
        if (_running || pc == null) return;
        if (pc.GetComponent<BotController>() != null) return;          // humans only trigger it
        if (scoreTrigger <= 0) return;
        if (_nextThreshold == 0) _nextThreshold = scoreTrigger;        // first threshold
        if (newScore < _nextThreshold) return;

        var rmm = RhythmRoundManager.Instance;
        if (rmm == null || !rmm.isRoundActive) return;

        // Don't start if the song's almost over — the rush would get cut off mid-way.
        if (SongTimeLeft(rmm) < minSongTimeLeftToStart) return;

        // Advance to the next multiple ABOVE the current score (in case it jumped past several at once).
        while (_nextThreshold <= newScore) _nextThreshold += scoreTrigger;

        _running = true;
        StartCoroutine(RunSegment(pc));
    }

    // Seconds of song left (custom track), or a big number for non-custom rounds.
    private float SongTimeLeft(RhythmRoundManager rmm)
    {
        var src = BeatAnalyzer.Instance != null ? BeatAnalyzer.Instance.audioSource : null;
        if (src != null && src.clip != null && src.isPlaying)
            return src.clip.length - src.time;
        return 999f;
    }

    [Server]
    private IEnumerator RunSegment(PlayerCombat human)
    {
        var humanCtrl = human.GetComponent<PlayerController>();
        var bot = humanCtrl != null ? humanCtrl.GetOpponent() : null;
        var botCombat = bot != null ? bot.GetComponent<PlayerCombat>() : null;
        if (bot == null || botCombat == null) { _running = false; yield break; }

        var rmm = RhythmRoundManager.Instance;
        // Clamp the segment so it can't run past the end of the song (leave a small buffer).
        float dur = segmentDuration;
        if (rmm != null) dur = Mathf.Min(dur, Mathf.Max(2f, SongTimeLeft(rmm) - 2f));

        SegmentActive = true; // suppress the player's normal on-beat card moves for the whole segment

        // 1) Drop the bot into a HELD stagger pose ("Stagger New") for the whole segment — it deals no
        //    damage and won't act (BotController.ThinkNextMove bails while staggered).
        PinBotForSegment(bot);
        botCombat.EnterHeldStagger();

        // 2) Open the portal near the bot (all clients see it).
        Vector3 toBot = (bot.transform.position - human.transform.position); toBot.y = 0f;
        toBot = toBot.sqrMagnitude > 0.001f ? toBot.normalized : Vector3.forward;
        Vector3 portalPos = bot.transform.position + toBot * portalBehindBot + Vector3.up * portalHeight;
        RpcOpenPortal(portalPos, Quaternion.LookRotation(-toBot));

        // 3) Tell the LOCAL human to start spawning + driving drones (VR tracking lives there).
        //    Score-triggered segments now use the ACTUAL mapped beat grid so every drone arrives on a beat.
        float[] beatTrackTimes = null;
        if (rmm != null)
        {
            float track = rmm.GetCurrentTrackTime();
            float startTrack = track + droneTravelTime;
            float endTrack = track + droneTravelTime + dur;
            var beats = rmm.GetUpcomingBeats(startTrack, endTrack);
            if (beats.Count > 0) beatTrackTimes = beats.ToArray();
        }

        if (beatTrackTimes != null && beatTrackTimes.Length > 0)
        {
            TargetBeginForcedRush(human.connectionToClient,
                human.transform.position, bot.transform.position, portalPos,
                beatTrackTimes, droneTravelTime);
        }
        else
        {
            // Fallback if no beat data is available (shouldn't happen in normal play).
            TargetBeginDroneRush(human.connectionToClient,
                human.transform.position, bot.transform.position, portalPos,
                dur, droneTravelTime, spawnGap);
        }

        // 4) Hold the segment (or until the round ends), then end. End EARLY if the round stops.
        //    When using mapped beats, keep the portal open until the last drone has arrived.
        if (beatTrackTimes != null && beatTrackTimes.Length > 0)
        {
            float lastBeat = beatTrackTimes[beatTrackTimes.Length - 1];
            while (rmm != null && rmm.isRoundActive && rmm.GetCurrentTrackTime() < lastBeat + 0.6f)
                yield return null;
        }
        else
        {
            float t = 0f;
            while (t < dur)
            {
                if (rmm != null && !rmm.isRoundActive) break;
                t += Time.deltaTime;
                yield return null;
            }
        }

        RpcClosePortal();
        botCombat.ExitHeldStagger();
        SegmentActive = false; // restore normal on-beat card play
        _running = false;       // ready to fire again at the next score threshold
    }

    // Stop any in-progress beat run-in and lock the bot to its spawn for the whole drone segment, so it
    // stands still in the held-stagger pose instead of charging the player. BotBeatApproach.Update also
    // bails while SegmentActive, but we cancel + snap here in case a run-in was already mid-flight.
    [Server]
    private void PinBotForSegment(PlayerController bot)
    {
        if (bot == null) return;
        var approach = bot.GetComponent<BotBeatApproach>();
        if (approach != null) approach.CancelApproach();   // kills any active run/return
        bot.ClearApproachOverride();                       // resume the home pin
        bot.transform.position = bot.HomePosition;          // snap to spawn immediately
    }

    // ── FORCED segment from a dense beatmap run — one drone per beat ────────────────────────────
    // Called by RhythmRoundManager when the song reaches a dense run. Ignored if a segment's running.
    [Server]
    public void StartForcedSegment(PlayerCombat human, System.Collections.Generic.List<float> beatTrackTimes)
    {
        if (_running || human == null || beatTrackTimes == null || beatTrackTimes.Count == 0) return;
        var rmm = RhythmRoundManager.Instance;
        if (rmm == null || !rmm.isRoundActive) return;
        _running = true;
        StartCoroutine(RunForcedSegment(human, beatTrackTimes.ToArray()));
    }

    [Server]
    private IEnumerator RunForcedSegment(PlayerCombat human, float[] beatTrackTimes)
    {
        var humanCtrl = human.GetComponent<PlayerController>();
        var bot = humanCtrl != null ? humanCtrl.GetOpponent() : null;
        var botCombat = bot != null ? bot.GetComponent<PlayerCombat>() : null;
        if (bot == null || botCombat == null) { _running = false; yield break; }

        var rmm = RhythmRoundManager.Instance;
        SegmentActive = true;
        PinBotForSegment(bot);
        botCombat.EnterHeldStagger();

        Vector3 toBot = (bot.transform.position - human.transform.position); toBot.y = 0f;
        toBot = toBot.sqrMagnitude > 0.001f ? toBot.normalized : Vector3.forward;
        Vector3 portalPos = bot.transform.position + toBot * portalBehindBot + Vector3.up * portalHeight;
        RpcOpenPortal(portalPos, Quaternion.LookRotation(-toBot));

        TargetBeginForcedRush(human.connectionToClient,
            human.transform.position, bot.transform.position, portalPos,
            beatTrackTimes, droneTravelTime);

        // Hold until the last beat has passed (+ travel + a tail), or the round ends.
        float lastBeat = beatTrackTimes[beatTrackTimes.Length - 1];
        while (rmm != null && rmm.isRoundActive && rmm.GetCurrentTrackTime() < lastBeat + 0.6f)
            yield return null;

        RpcClosePortal();
        botCombat.ExitHeldStagger();
        SegmentActive = false;
        _running = false;
    }

    [TargetRpc]
    private void TargetBeginForcedRush(NetworkConnection target, Vector3 playerPos, Vector3 botPos,
                                       Vector3 portalPos, float[] beatTrackTimes, float travel)
    {
        StartCoroutine(LocalForcedRush(botPos, portalPos, beatTrackTimes, travel));
    }

    // Spawn one drone per dense beat, each arriving ON its beat (converts track-time → unscaled time).
    private IEnumerator LocalForcedRush(Vector3 botPos, Vector3 portalPos, float[] beatTrackTimes, float travel)
    {
        _combo = 0;
        var rmm = RhythmRoundManager.Instance;
        Transform playerHead = CachedCamera.Main != null ? CachedCamera.Main.transform : null;
        Transform botT = FindBotTransform(botPos);
        if (playerHead == null || rmm == null) yield break;

        Vector3 sideAxis = playerHead.right; sideAxis.y = 0f;
        sideAxis = sideAxis.sqrMagnitude > 0.001f ? sideAxis.normalized : Vector3.right;

        bool rightNext = true;
        for (int b = 0; b < beatTrackTimes.Length; b++)
        {
            // If the beat has already passed by the time we got the message (network latency), skip it
            // rather than spawning a drone that arrives late. Every spawned drone lands ON its mapped beat.
            if (rmm.GetCurrentTrackTime() > beatTrackTimes[b]) continue;

            // Spawn `travel` seconds before this beat's track time so the drone arrives ON the beat.
            float spawnTrack = beatTrackTimes[b] - travel;
            while (rmm.isRoundActive && rmm.GetCurrentTrackTime() < spawnTrack)
                yield return null;
            if (!rmm.isRoundActive) yield break;

            // Convert this beat's track time to an unscaled arrival time for the drone.
            float now = Time.unscaledTime;
            float arrive = now + Mathf.Max(0.05f, beatTrackTimes[b] - rmm.GetCurrentTrackTime());

            float vert = Random.Range(-verticalSpread, verticalSpread);
            float side = (rightNext ? 1f : -1f) * lateralOffset;
            Vector3 spawnPos = portalPos + Vector3.up * vert + sideAxis * side;
            Vector3 arriveOffset = Vector3.up * (vert * 0.5f) + sideAxis * (side * 0.5f);
            bool finisher = (b == beatTrackTimes.Length - 1); // last dense beat = the big one

            SpawnDrone(playerHead, botT, spawnPos, arriveOffset, arrive, rightNext, finisher);
            rightNext = !rightNext;
        }
    }

    // ── Client: portal visuals ─────────────────────────────────────────────────────────────────
    [ClientRpc]
    private void RpcOpenPortal(Vector3 pos, Quaternion rot)
    {
        if (portalPrefab == null) return;
        if (_portal != null) Destroy(_portal);
        _portal = Instantiate(portalPrefab, pos, rot);
    }

    [ClientRpc]
    private void RpcClosePortal()
    {
        if (_portal != null) Destroy(_portal, 0.3f);
        _combo = 0;
    }

    // ── Local client: spawn + drive the drones ─────────────────────────────────────────────────
    [TargetRpc]
    private void TargetBeginDroneRush(NetworkConnection target, Vector3 playerPos, Vector3 botPos,
                                      Vector3 portalPos, float duration, float travel, float gap)
    {
        StartCoroutine(LocalDroneRush(playerPos, botPos, portalPos, duration, travel, gap));
    }

    private IEnumerator LocalDroneRush(Vector3 playerPos, Vector3 botPos, Vector3 portalPos,
                                       float duration, float travel, float gap)
    {
        _combo = 0;
        var rmm = RhythmRoundManager.Instance;
        Transform playerHead = CachedCamera.Main != null ? CachedCamera.Main.transform : null;
        Transform botT = FindBotTransform(botPos);
        if (playerHead == null) yield break;

        float endTime = Time.unscaledTime + duration;
        bool finisherSpawned = false;
        bool rightNext = true;

        // Player's left/right axis (flattened) for a small X offset so drones aren't dead-center.
        Vector3 sideAxis = playerHead.right; sideAxis.y = 0f;
        sideAxis = sideAxis.sqrMagnitude > 0.001f ? sideAxis.normalized : Vector3.right;

        float lastArrive = 0f; // enforce a minimum spacing between successive drone ARRIVALS

        while (Time.unscaledTime < endTime)
        {
            // Last ~2.5s → spawn the single big finisher instead of another normal drone.
            bool finisher = !finisherSpawned && (endTime - Time.unscaledTime) <= 2.5f;
            if (finisher) finisherSpawned = true;

            // Mostly-middle, with a SMALL left/right X nudge (alternating) + a little height variation,
            // so two drones never stack on the exact same spot. Hand to use is shown by COLOUR.
            float vert = finisher ? 0f : Random.Range(-verticalSpread, verticalSpread);
            float side = finisher ? 0f : (rightNext ? 1f : -1f) * lateralOffset;
            Vector3 spawnPos = portalPos + Vector3.up * vert + sideAxis * side;
            Vector3 arriveOffset = Vector3.up * (vert * 0.5f) + sideAxis * (side * 0.5f);

            // Arrival timing: snap to the beat, but force it to be at least minArrivalGap after the
            // previous drone so they DON'T reach at the same time — player hits one, then the next.
            float arrive = ComputeBeatArrival(rmm, travel);
            if (arrive < lastArrive + minArrivalGap) arrive = lastArrive + minArrivalGap;
            lastArrive = arrive;

            SpawnDrone(playerHead, botT, spawnPos, arriveOffset, arrive, rightNext, finisher);
            rightNext = !rightNext;

            if (finisher) break;
            yield return new WaitForSecondsRealtime(gap);
        }
    }

    // Pick an arrival time ≈ travel seconds out, snapped to the nearest real beat so the punch lands
    // on the metronome. Falls back to "now + travel" if beat info isn't available.
    private float ComputeBeatArrival(RhythmRoundManager rmm, float travel)
    {
        float now = Time.unscaledTime;
        if (rmm == null) return now + travel;

        // Convert the desired arrival (track time) back to unscaled time using the live offset.
        float track = rmm.GetCurrentTrackTime();
        float offset = now - track;                 // unscaled ≈ track + offset
        float desiredTrack = track + travel;
        float beat = rmm.GetNextBeatTime();
        // Walk beats forward to the one closest to desiredTrack (beats are ~ fixed interval apart).
        // GetNextBeatTime only gives the next one; approximate the grid from it.
        float arriveTrack = beat >= desiredTrack ? beat : desiredTrack;
        return arriveTrack + offset;
    }

    private void SpawnDrone(Transform player, Transform bot, Vector3 spawnPos, Vector3 arriveOffset,
                            float arriveTime, bool isRight, bool isFinisher)
    {
        if (dronePrefab == null) return;
        var go = Instantiate(dronePrefab, spawnPos, Quaternion.identity);
        var drone = go.GetComponent<Drone>();
        if (drone == null) { Destroy(go); return; }
        drone.Init(player, bot, arriveOffset, arriveTime, this, isRight, isFinisher);
    }

    private Transform FindBotTransform(Vector3 botPos)
    {
        var lp = GameManager.localPlayer;
        var opp = lp != null ? lp.GetOpponent() : null;
        return opp != null ? opp.transform : null;
    }

    // ── Drone outcome callbacks (local) → route authoritative results to the server ─────────────
    public void OnDronePunched(Drone d, float power, float onBeat, bool rightHand, bool finisher)
    {
        _combo++;
        CmdDroneHit(power, onBeat, _combo, finisher);
    }

    public void OnDroneDodged(Drone d)
    {
        CmdDroneDodge();
    }

    public void OnDroneMissed(Drone d)
    {
        _combo = 0;                       // a true miss breaks the in-segment combo
        ScreenSpark.Flash();              // local screen spark (no real damage to the player)
        CmdDroneMissed();                 // the drone got past you → the BOT scores
    }

    public void OnDroneHitBot(Drone d)
    {
        // Visual only — the actual damage was applied server-side in CmdDroneHit when it was punched.
        // The bot's own hit VFX + blood play via its normal damage path.
    }

    [Command(requiresAuthority = false)]
    private void CmdDroneHit(float power, float onBeat, int combo, bool finisher, NetworkConnectionToClient sender = null)
    {
        var human = sender != null && sender.identity != null ? sender.identity.GetComponent<PlayerCombat>() : null;
        if (human == null) return;
        var bot = human.GetComponent<PlayerController>()?.GetOpponent()?.GetComponent<PlayerCombat>();
        if (bot == null) return;

        power  = Mathf.Clamp01(power);
        onBeat = Mathf.Clamp01(onBeat);
        float comboMult = 1f + Mathf.Min(combo, 10) * 0.15f; // up to ~2.5× at a 10-combo

        int dmg = Mathf.Max(1, Mathf.RoundToInt(droneBaseDamage * (0.5f + power) * (0.5f + onBeat)));
        int pts = Mathf.RoundToInt((finisher ? finisherPoints : dronePunchPoints)
                                   * (0.5f + power) * (0.5f + onBeat) * comboMult);

        bot.TakeDroneHit(dmg);            // blood spill only — no hurt anim, no knockback (stays staggered)
        human.AddScore(pts, "drone bounce");
    }

    [Command(requiresAuthority = false)]
    private void CmdDroneDodge(NetworkConnectionToClient sender = null)
    {
        var human = sender != null && sender.identity != null ? sender.identity.GetComponent<PlayerCombat>() : null;
        if (human != null) human.AddScore(droneDodgePoints, "drone dodge save");
    }

    [Command(requiresAuthority = false)]
    private void CmdDroneMissed(NetworkConnectionToClient sender = null)
    {
        // A drone got past the player → reward the BOT (the human's opponent).
        var human = sender != null && sender.identity != null ? sender.identity.GetComponent<PlayerCombat>() : null;
        if (human == null) return;
        var bot = human.GetComponent<PlayerController>()?.GetOpponent()?.GetComponent<PlayerCombat>();
        if (bot != null) bot.AddScore(droneMissBotPoints, "drone got past player");
    }

    [Server]
    public void ResetForNewRound()
    {
        _running = false;
        _nextThreshold = scoreTrigger; // score resets each round, so the threshold resets too
        if (SegmentActive) SegmentActive = false;
    }
}
