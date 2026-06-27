using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// PC DRONE SEGMENT — the flat/PC version of the drone-rush reward break.
///
/// On a dense beat run the bot drops into stagger and a portal opens near it. Then, timed to the beats:
///   • MOSTLY drones fly out at the player from LEFT or RIGHT — scroll-dodge AWAY from the drone's side.
///       Scroll UP = dodge LEFT,  Scroll DOWN = dodge RIGHT.
///       Correct dodge swoops the camera and deflects the drone into the bot.
///   • OCCASIONALLY a slash VFX flies in — SHOUT on the beat to parry (any mic spike in the window).
///
/// Drone movement/glow reuses Drone.cs (same arc/bob/sway/banking + EnergyGlove shader tint).
/// Host-authoritative plain MonoBehaviour. Self-bootstraps.
[DefaultExecutionOrder(10006)]
public class PCDroneSegment : MonoBehaviour
{
    public static PCDroneSegment Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject("~PCDroneSegment");
        DontDestroyOnLoad(go);
        go.AddComponent<PCDroneSegment>();
    }

    [Header("Prefabs (auto-pulled from DroneRushSegment if left empty)")]
    public GameObject dronePrefab;
    public GameObject portalPrefab;
    [Tooltip("Slash VFX prefab flung at the player for the shout-to-parry beats.")]
    public GameObject slashPrefab;

    [Header("Timing")]
    public float segmentDuration = 14f;
    [Tooltip("Seconds a drone/slash spends flying from the portal to the player.")]
    public float travelTime = 1.2f;
    [Tooltip("Minimum seconds between successive arrivals.")]
    public float minArrivalGap = 0.85f;
    [Tooltip("Window (s) around the arrival in which a scroll-dodge / shout-parry counts.")]
    public float reactWindow = 0.55f;

    [Header("Mix")]
    [Range(0f, 1f)] public float slashChance = 0.25f;
    [Tooltip("Mic volume (0..1) that counts as a SHOUT for parrying a slash.")]
    [Range(0.05f, 1f)] public float shoutParryVolume = 0.3f;

    [Header("Scoring")]
    public int dodgePoints    = 1500;
    public int parryPoints    = 1800;
    public int botHitPoints   = 1200;
    public int deflectBotDamage = 12;

    [Header("Camera dodge maneuver")]
    public float camSlide = 0.6f;
    public float camTilt  = 14f;
    public float camTime  = 0.34f;

    [Header("Portal placement")]
    public float portalBehindBot = 1.6f;
    public float portalHeight    = 1.6f;

    [Header("Drone size")]
    [Tooltip("Scale multiplier on the spawned drone (on top of Drone.droneScale).")]
    public float droneScale = 1.4f;

    [Header("Scroll sensitivity")]
    [Tooltip("Accumulated scroll units needed to register a dodge. Lower = more responsive.")]
    public float scrollThreshold = 0.5f;

    public bool SegmentActive { get; private set; }

    public static bool AnySegmentActive =>
        (Instance != null && Instance.SegmentActive) ||
        (DroneRushSegment.Instance != null && DroneRushSegment.Instance.SegmentActive);

    private bool _running;
    private GameObject _portal;
    private Transform _camRig;
    private Coroutine _camRoutine;

    // Accumulated scroll so a single quick tick isn't missed.
    private float _scrollAccum;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Update()
    {
        // Accumulate scroll every frame so RunDrone can drain it; decay slowly so stale input doesn't linger.
        float raw = ReadScrollRaw();
        _scrollAccum += raw;
        if (Mathf.Abs(raw) < 0.001f)
            _scrollAccum = Mathf.MoveTowards(_scrollAccum, 0f, Time.unscaledDeltaTime * 3f);
    }

    private void PullPrefabs()
    {
        var vr = DroneRushSegment.Instance;
        if (vr != null)
        {
            if (dronePrefab  == null) dronePrefab  = vr.dronePrefab;
            if (portalPrefab == null) portalPrefab = vr.portalPrefab;
        }
    }

    public void StartSegment(List<float> beatTrackTimes)
    {
        if (_running) return;
        var rmm = RhythmRoundManager.Instance;
        if (rmm == null || !rmm.isRoundActive) return;
        if (VRCameraDriver.VRActive) return;
        PullPrefabs();
        StartCoroutine(RunSegment(beatTrackTimes));
    }

    private IEnumerator RunSegment(List<float> beats)
    {
        _running = true;
        SegmentActive = true;

        var human     = LocalHuman();
        var bot       = human != null ? human.GetComponent<PlayerController>()?.GetOpponent() : null;
        var botCombat = bot   != null ? bot.GetComponent<PlayerCombat>()   : null;
        if (human == null || bot == null) { _running = false; SegmentActive = false; yield break; }

        _camRig = human.GetComponent<PlayerController>()?.CameraPosition;

        if (botCombat != null) botCombat.EnterHeldStagger();
        var botApproach = bot.GetComponent<BotBeatApproach>();
        if (botApproach != null) botApproach.CancelApproach();
        bot.GetComponent<PlayerController>()?.ClearApproachOverride();

        Vector3 toBot = (bot.transform.position - human.transform.position); toBot.y = 0f;
        toBot = toBot.sqrMagnitude > 0.001f ? toBot.normalized : Vector3.forward;
        Vector3 portalPos = bot.transform.position + toBot * portalBehindBot + Vector3.up * portalHeight;
        if (portalPrefab != null) _portal = Instantiate(portalPrefab, portalPos, Quaternion.LookRotation(-toBot));

        var rmm      = RhythmRoundManager.Instance;
        float endAt  = Time.time + segmentDuration;
        float lastArrival = -99f;
        int   beatIdx = 0;
        _scrollAccum  = 0f;

        while (Time.time < endAt && rmm != null && rmm.isRoundActive)
        {
            float now = rmm.GetCurrentTrackTime();
            float arriveTrack = NextBeatAfter(beats, ref beatIdx, now + travelTime, lastArrival + minArrivalGap);
            if (arriveTrack < 0f) { yield return null; continue; }
            lastArrival = arriveTrack;

            bool isSlash   = Random.value < slashChance;
            bool fromRight = Random.value < 0.5f;

            // Convert track-time arrival to a unscaled wall-clock time Drone.cs expects.
            float trackNow     = rmm.GetCurrentTrackTime();
            float secondsUntil = arriveTrack - trackNow;
            float arriveWall   = Time.unscaledTime + secondsUntil;

            // Camera/head transform to aim at.
            Transform aimAt = _camRig != null ? _camRig : human.transform;

            StartCoroutine(isSlash
                ? RunSlash(portalPos, human, botCombat, arriveTrack)
                : RunDrone(portalPos, aimAt, human, bot, botCombat, arriveTrack, arriveWall, fromRight));

            yield return new WaitForSeconds(minArrivalGap);
        }

        if (_portal != null) Destroy(_portal);
        if (botCombat != null) botCombat.ExitHeldStagger();
        SegmentActive = false;
        _running = false;
    }

    // ── DRONE ──────────────────────────────────────────────────────────────────────────────────────────
    private IEnumerator RunDrone(Vector3 portalPos, Transform aimAt, PlayerCombat human,
                                 PlayerController bot, PlayerCombat botCombat,
                                 float arriveTrack, float arriveWall, bool fromRight)
    {
        // Spawn via the same prefab + Drone.cs so we get arc/bob/sway/banking + EnergyGlove shader glow.
        GameObject droneGO = SpawnDrone(portalPos, droneScale);
        if (droneGO == null) yield break;

        var drone = droneGO.GetComponent<Drone>();

        // Arrive slightly to the side of the head so it's not dead-centre.
        Vector3 sideOffset = aimAt.right * (fromRight ? 0.15f : -0.15f);

        bool resolved = false;
        bool dodgedOk = false;

        if (drone != null)
        {
            drone.InitPC(aimAt, bot.transform, sideOffset, arriveWall, isRight: fromRight);
            // Wire outcome callbacks — correct scroll-dodge overrides the VR punch/dodge path.
            drone.onMissed  += _ => { if (!resolved) { resolved = true; botCombat?.AddScore(botHitPoints, "drone hit"); FlashHitFeedback(); } };
            drone.onDodged  += _ => { }; // head-dodge (VR path) — ignore on PC
            drone.onPunched += (_, __) => { }; // VR punch — ignore on PC
            drone.onHitBot  += _ => botCombat?.TakeDroneHit(deflectBotDamage);
        }

        var rmm = RhythmRoundManager.Instance;

        // wantDir: +1 = scroll UP (dodge LEFT) when drone from RIGHT; -1 = scroll DOWN when from LEFT.
        float wantDir = fromRight ? +1f : -1f;

        _scrollAccum = 0f; // clear stale scroll at drone spawn

        while (droneGO != null && !resolved)
        {
            float t = rmm != null ? rmm.GetCurrentTrackTime() : arriveTrack;

            if (Mathf.Abs(t - arriveTrack) <= reactWindow)
            {
                // Drain accumulated scroll.
                float dir = _scrollAccum > scrollThreshold ? +1f : _scrollAccum < -scrollThreshold ? -1f : 0f;
                if (dir != 0f)
                {
                    resolved = true;
                    _scrollAccum = 0f;
                    if (Mathf.Approximately(dir, wantDir))
                    {
                        dodgedOk = true;
                        DoCameraDodge(dir);
                        human.AddScore(dodgePoints, "drone dodge");
                        // Redirect the drone to bounce at the bot.
                        if (drone != null) StartCoroutine(RedirectDrone(droneGO, drone, bot, botCombat));
                        yield break;
                    }
                    else
                    {
                        // Wrong direction → treat as miss.
                        botCombat?.AddScore(botHitPoints, "drone hit");
                        FlashHitFeedback();
                        if (droneGO != null) Destroy(droneGO);
                        yield break;
                    }
                }
            }

            // Past the window with no correct dodge.
            if (t > arriveTrack + reactWindow && !resolved)
            {
                resolved = true;
                // Drone.cs onMissed callback will fire and handle scoring once it arrives.
                break;
            }

            yield return null;
        }
    }

    // Overrides the drone's trajectory to zip toward the bot (deflect).
    private IEnumerator RedirectDrone(GameObject droneGO, Drone drone, PlayerController bot, PlayerCombat botCombat)
    {
        if (droneGO == null) yield break;
        Vector3 from = droneGO.transform.position;
        Vector3 to   = bot.transform.position + Vector3.up * 1.2f;
        float   t    = 0f;
        float   dur  = 0.3f;
        while (t < dur && droneGO != null)
        {
            t += Time.unscaledDeltaTime;
            droneGO.transform.position = Vector3.Lerp(from, to, t / dur);
            droneGO.transform.LookAt(bot.transform);
            yield return null;
        }
        botCombat?.TakeDroneHit(deflectBotDamage);
        if (droneGO != null) Destroy(droneGO);
    }

    // ── SLASH ──────────────────────────────────────────────────────────────────────────────────────────
    private IEnumerator RunSlash(Vector3 portalPos, PlayerCombat human, PlayerCombat botCombat, float arriveTrack)
    {
        Transform target = _camRig != null ? _camRig : human.transform;
        GameObject slash = SpawnFlyer(slashPrefab, portalPos);

        var rmm = RhythmRoundManager.Instance;
        bool resolved = false;

        while (true)
        {
            float t = rmm != null ? rmm.GetCurrentTrackTime() : arriveTrack;

            if (slash != null)
            {
                float u = Mathf.InverseLerp(arriveTrack - travelTime, arriveTrack, t);
                slash.transform.position = Vector3.Lerp(portalPos, target.position, Mathf.Clamp01(u));
                Vector3 dir = (target.position - slash.transform.position);
                if (dir.sqrMagnitude > 0.001f)
                    slash.transform.rotation = Quaternion.LookRotation(dir.normalized);
            }

            if (!resolved && Mathf.Abs(t - arriveTrack) <= reactWindow)
            {
                float vol = human.vp != null ? human.vp.CurrentFestivalVolume : 0f;
                if (vol >= shoutParryVolume)
                {
                    resolved = true;
                    human.AddScore(parryPoints, "slash parry");
                    if (slash != null) Destroy(slash);
                    DoCameraDodge(0f);
                    yield break;
                }
            }

            if (t > arriveTrack + reactWindow)
            {
                if (!resolved) { botCombat?.AddScore(botHitPoints, "slash hit"); FlashHitFeedback(); }
                break;
            }
            yield return null;
        }
        if (slash != null) Destroy(slash);
    }

    // ── Camera maneuver ────────────────────────────────────────────────────────────────────────────────
    private void DoCameraDodge(float dir)
    {
        if (_camRig == null) return;
        if (_camRoutine != null) StopCoroutine(_camRoutine);
        _camRoutine = StartCoroutine(CameraDodgeRoutine(dir));
    }

    private IEnumerator CameraDodgeRoutine(float dir)
    {
        Transform cam = _camRig;
        Vector3   baseLocalPos = cam.localPosition;
        Quaternion baseLocalRot = cam.localRotation;

        float half  = camTime * 0.5f;
        float slide = (dir == 0f ? 0.25f : 1f) * camSlide;
        float tilt  = (dir == 0f ? 0.4f  : 1f) * camTilt;
        float sign  = dir >= 0f ? 1f : -1f;

        float e = 0f;
        while (e < half)
        {
            e += Time.unscaledDeltaTime;
            float p = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(e / half));
            cam.localPosition = baseLocalPos + Vector3.right * (sign * slide * p);
            cam.localRotation = baseLocalRot * Quaternion.Euler(0f, 0f, -sign * tilt * p);
            yield return null;
        }
        e = 0f;
        while (e < half)
        {
            e += Time.unscaledDeltaTime;
            float p = Mathf.SmoothStep(1f, 0f, Mathf.Clamp01(e / half));
            cam.localPosition = baseLocalPos + Vector3.right * (sign * slide * p);
            cam.localRotation = baseLocalRot * Quaternion.Euler(0f, 0f, -sign * tilt * p);
            yield return null;
        }
        cam.localPosition = baseLocalPos;
        cam.localRotation = baseLocalRot;
        _camRoutine = null;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────────────
    private GameObject SpawnDrone(Vector3 pos, float scale)
    {
        if (dronePrefab == null) return null;
        var go = Instantiate(dronePrefab, pos, Quaternion.identity);
        go.transform.localScale *= scale;
        return go;
    }

    private GameObject SpawnFlyer(GameObject prefab, Vector3 pos)
    {
        if (prefab == null) return null;
        return Instantiate(prefab, pos, Quaternion.identity);
    }

    private void FlashHitFeedback()
    {
        CameraShake.Instance?.Shake(0.18f, 0.35f);
        VRHaptics.GotHit(0.5f);
    }

    private float NextBeatAfter(List<float> beats, ref int idx, float floorA, float floorB)
    {
        float floor = Mathf.Max(floorA, floorB);
        while (idx < beats.Count && beats[idx] < floor) idx++;
        return idx < beats.Count ? beats[idx++] : -1f;
    }

    private static PlayerCombat LocalHuman()
    {
        foreach (var p in GameManager.players)
            if (p != null && p.isLocalPlayer && p.GetComponent<BotController>() == null)
                return p.GetComponent<PlayerCombat>();
        return null;
    }

    private static float ReadScrollRaw()
    {
#if ENABLE_INPUT_SYSTEM
        var m = Mouse.current;
        return m != null ? m.scroll.ReadValue().y : 0f;
#else
        return Input.mouseScrollDelta.y;
#endif
    }
}
