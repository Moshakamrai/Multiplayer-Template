using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// DRONE RUSH SEGMENT (PC) — a beat-timed PUNCH break.
///
/// Triggered on a dense beat run. The bot stands in IDLE. Drones spawn from BEHIND the bot, drift out to
/// its LEFT or RIGHT, then fly on a straight line toward the matching side of the player, arriving ON a
/// beat. You HIT the incoming drone with the arrow keys:
///   • LEFT arrow  → hits a LEFT-side drone with the PUNCH animation.
///   • RIGHT arrow → hits a RIGHT-side drone with the UPPERCUT animation.
/// A correct, on-time hit punches the drone back into the bot (damage + score). Miss / wrong key / wrong
/// side → the drone hits you (bot scores). No dodging, no shout, no overlays.
///
/// First segment of a session PAUSES and shows a short how-to overlay (click to continue).
/// Host-authoritative plain MonoBehaviour (game runs as host). Self-bootstraps.
[DefaultExecutionOrder(10006)]
public class DroneRushSegment : MonoBehaviour
{
    public static DroneRushSegment Instance { get; private set; }

    // NO self-bootstrap. This lives as a real object in the scene (with the Drone Prefab assigned in
    // the inspector). A runtime-spawned duplicate used to fight this scene object for Instance and win
    // with an EMPTY prefab slot — that was why the prefab looked "emptied on start".

    [Header("Prefab")]
    [Tooltip("Drone prefab — just the visual model. Any VR 'Drone' component on it is stripped at spawn.")]
    public GameObject dronePrefab;

    [Header("Timing")]
    public float segmentDuration = 16f;
    [Tooltip("Seconds a drone spends flying from behind the bot to the player.")]
    public float travelTime = 1.3f;
    [Tooltip("Minimum seconds between successive drone arrivals.")]
    public float minArrivalGap = 0.9f;
    [Tooltip("Window (s) before AND after the beat in which an arrow-key hit counts. Bigger = easier.")]
    public float hitWindow = 0.55f;

    [Header("Attack animation state names (from the card Animator)")]
    [Tooltip("Animator state played when you hit a LEFT drone with the LEFT arrow.")]
    public string punchState = "Cross";
    [Tooltip("Animator state played when you hit a RIGHT drone with the RIGHT arrow.")]
    public string uppercutState = "Uppercut";

    [Header("Scoring")]
    public int hitPoints        = 1600;   // clean punch of a drone
    public int botHitPoints     = 1200;   // bot scores when a drone hits you
    public int deflectBotDamage = 12;     // damage to the bot when you punch a drone into it

    [Header("Drone spawn / flight")]
    [Tooltip("How far BEHIND the bot the drones spawn (m).")]
    public float spawnBehind = 1.4f;
    [Tooltip("Spawn height above the bot base (m).")]
    public float spawnHeight = 1.6f;
    [Tooltip("How far out to the side the drone drifts as it leaves the bot (m).")]
    public float sideDrift = 1.0f;
    [Tooltip("How far to the player's side the drone aims (m from the player centre).")]
    public float playerSideOffset = 0.7f;
    [Tooltip("Vertical offset (m) applied to the drone's ARRIVAL point, relative to the camera. Negative " +
             "brings the drone DOWN toward glove height. -0.6 ≈ ~50% down toward the gloves.")]
    public float gloveHeightOffset = -0.6f;
    [Tooltip("Scale multiplier on each drone.")]
    public float droneScale = 1.4f;

    [Header("Colours / glow")]
    public Color leftColor  = new Color(0.3f, 0.6f, 1f);   // blue = LEFT (punch)
    public Color rightColor = new Color(1f, 0.45f, 0.15f); // orange = RIGHT (uppercut)
    [Tooltip("Emission multiplier (glow strength).")]
    public float glow = 3.5f;

    [Header("Punch feel")]
    [Tooltip("Animator playback speed multiplier for the punch/uppercut during THIS segment only " +
             "(bigger = snappier, more exaggerated). Restored to 1 when the segment ends.")]
    public float punchAnimSpeed = 1.8f;
    [Tooltip("Projectile prefab fired from the glove to the drone on a hit (this segment only). Optional " +
             "— if empty, a glowing energy ball is generated at runtime.")]
    public GameObject punchProjectilePrefab;
    [Tooltip("Seconds the projectile takes to travel from the glove to the drone.")]
    public float projectileTime = 0.12f;
    [Tooltip("Where the projectile launches from, relative to the camera (right = +x, up = +y, fwd = +z).")]
    public Vector3 gloveMuzzleOffset = new Vector3(0f, -0.35f, 0.5f);

    [Header("Camera hit kick")]
    public float camPunchKick = 0.18f;
    public float camKickTime  = 0.12f;

    public bool SegmentActive { get; private set; }

    /// True while the drone segment is running. (Kept as a static helper so game code can gate on it.)
    public static bool AnySegmentActive => Instance != null && Instance.SegmentActive;

    private bool _running;
    private Transform _camRig;
    private Coroutine _camRoutine;
    private Animator _humanAnimator;
    private float _prevAnimSpeed = 1f;

    // Tutorial (once per session).
    private static bool _tutorialShown;
    private bool _tutorialActive;

    // Latched arrow press: -1 = LEFT this frame, +1 = RIGHT this frame, 0 = none.
    private int _arrow;

    static readonly int ID_BaseColor = Shader.PropertyToID("_BaseColor");
    static readonly int ID_RimColor  = Shader.PropertyToID("_RimColor");
    static readonly int ID_FlowColor = Shader.PropertyToID("_FlowColor");
    static readonly int ID_Emission  = Shader.PropertyToID("_EmissionColor");

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Update()
    {
        int a = ReadArrow();
        if (a != 0) _arrow = a;
    }

    public void StartSegment(List<float> beatTrackTimes)
    {
        if (_running) { Debug.Log("<color=orange>[Drone]</color> ignored — already running."); return; }
        var rmm = RhythmRoundManager.Instance;
        if (rmm == null || !rmm.isRoundActive) { Debug.Log("<color=orange>[Drone]</color> ignored — no active round."); return; }
        // No prefab → do NOT run: otherwise you'd get hit by invisible drones. Bail so normal play continues.
        if (dronePrefab == null)
        {
            Debug.LogError("<color=red>[Drone]</color> No 'Drone Prefab' assigned on ~DroneRushSegment — segment SKIPPED so you don't get hit by invisible drones. Assign the Drone prefab in the inspector.");
            return;
        }
        Debug.Log($"<color=lime>[Drone]</color> Starting segment, {beatTrackTimes.Count} beats.");
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
        Transform aimAt = _camRig != null ? _camRig : human.transform;

        // Boost the player's punch animation speed for this segment (snappier/exaggerated).
        _humanAnimator = human.animator;
        if (_humanAnimator != null) { _prevAnimSpeed = _humanAnimator.speed; _humanAnimator.speed = punchAnimSpeed; }

        // Bot: stand IDLE (pinned) — no charging the player, no stagger pose.
        if (botCombat != null) botCombat.HoldIdle();
        var botApproach = bot.GetComponent<BotBeatApproach>();
        if (botApproach != null) botApproach.CancelApproach();
        bot.GetComponent<PlayerController>()?.ClearApproachOverride();

        // First segment of the session: how-to overlay (pauses game).
        if (!_tutorialShown)
        {
            _tutorialShown = true;
            yield return ShowTutorial();
        }

        var rmm      = RhythmRoundManager.Instance;
        float endAt  = Time.time + segmentDuration;
        float lastArrival = -99f;
        int   beatIdx = 0;

        while (Time.time < endAt && rmm != null && rmm.isRoundActive)
        {
            float now = rmm.GetCurrentTrackTime();
            float arriveTrack = NextBeatAfter(beats, ref beatIdx, now + travelTime, lastArrival + minArrivalGap);
            if (arriveTrack < 0f) { yield return null; continue; }
            lastArrival = arriveTrack;

            bool fromRight = Random.value < 0.5f;
            // Fire-and-forget: each drone lives its own life (no shared state).
            StartCoroutine(RunDrone(bot.transform, human.transform, aimAt, botCombat, arriveTrack, fromRight));

            yield return new WaitForSeconds(minArrivalGap);
        }

        if (_humanAnimator != null) _humanAnimator.speed = _prevAnimSpeed; // restore normal anim speed
        if (botCombat != null) botCombat.ReleaseIdle();
        SegmentActive = false;
        _running = false;
    }

    // ── ONE DRONE: spawn behind bot → drift to a side → fly straight at the player's matching side. ──
    private IEnumerator RunDrone(Transform bot, Transform human, Transform aimAt,
                                 PlayerCombat botCombat, float arriveTrack, bool fromRight)
    {
        var rmm = RhythmRoundManager.Instance;

        Vector3 toPlayer = human.position - bot.position; toPlayer.y = 0f;
        toPlayer = toPlayer.sqrMagnitude > 0.001f ? toPlayer.normalized : Vector3.forward;
        // The PLAYER looks toward the bot (-toPlayer). The player's RIGHT is Cross(up, -toPlayer). Using
        // the player's own right so "fromRight" drone = right side of the SCREEN = RIGHT arrow.
        Vector3 playerRight = Vector3.Cross(Vector3.up, -toPlayer);
        float   sideSign    = fromRight ? 1f : -1f;

        // Drones fly at glove/hand height, not up near the head — bring the arrival + drift down.
        Vector3 spawnPos  = bot.position - toPlayer * spawnBehind + Vector3.up * spawnHeight;
        Vector3 driftPos  = bot.position + playerRight * (sideSign * sideDrift) + Vector3.up * spawnHeight;
        Vector3 arrivePos = aimAt.position + playerRight * (sideSign * playerSideOffset)
                          + Vector3.up * gloveHeightOffset; // lowered toward the gloves

        var go = SpawnDrone(spawnPos);
        if (go == null) yield break;
        TintDrone(go, fromRight ? rightColor : leftColor);

        float driftFrac = 0.3f;
        // LEFT arrow hits a LEFT drone (punch); RIGHT arrow hits a RIGHT drone (uppercut).
        int   wantArrow = fromRight ? +1 : -1;

        bool resolved = false;
        while (go != null)
        {
            float t = rmm != null ? rmm.GetCurrentTrackTime() : arriveTrack;
            float u = Mathf.InverseLerp(arriveTrack - travelTime, arriveTrack, t); // 0 spawn → 1 beat

            Vector3 pos;
            if (u < driftFrac)
                pos = Vector3.Lerp(spawnPos, driftPos, u / driftFrac);
            else
                pos = Vector3.Lerp(driftPos, arrivePos, (u - driftFrac) / (1f - driftFrac));
            go.transform.position = pos;
            Vector3 face = (aimAt.position - go.transform.position);
            if (face.sqrMagnitude > 0.001f) go.transform.rotation = Quaternion.LookRotation(face.normalized);

            if (!resolved && Mathf.Abs(t - arriveTrack) <= hitWindow && _arrow != 0)
            {
                int a = _arrow; _arrow = 0;
                if (a == wantArrow)
                {
                    resolved = true;
                    var human2 = LocalHuman();
                    human2?.PlayLocalAttackAnim(fromRight ? uppercutState : punchState);
                    DoCameraKick(fromRight ? +1 : -1);
                    human2?.AddScore(hitPoints, "drone punch");
                    // Fire a projectile from the glove to the drone, THEN punch the drone into the bot.
                    yield return FireProjectileAtDrone(aimAt, go, fromRight);
                    yield return PunchIntoBot(go, bot, botCombat);
                    yield break;
                }
                // Wrong arrow → consumed; drone continues and may still land on you.
            }

            if (t > arriveTrack + hitWindow)
            {
                if (!resolved) { botCombat?.AddScore(botHitPoints, "drone hit"); FlashHitFeedback(); }
                break;
            }
            yield return null;
        }
        if (go != null) Destroy(go);
    }

    // Fire a projectile from the glove muzzle to the drone on a hit (segment-only flavour). Uses the
    // assigned prefab, or a runtime-built glowing energy ball if none is set.
    private IEnumerator FireProjectileAtDrone(Transform aimAt, GameObject drone, bool fromRight)
    {
        if (drone == null) yield break;

        // Muzzle position from the camera: right/up/forward offset, mirrored to the swinging side.
        Vector3 right = aimAt.right, up = aimAt.up, fwd = aimAt.forward;
        float sideSign = fromRight ? 1f : -1f;
        Vector3 muzzle = aimAt.position
                       + right * (gloveMuzzleOffset.x * sideSign)
                       + up    *  gloveMuzzleOffset.y
                       + fwd   *  gloveMuzzleOffset.z;

        GameObject proj = punchProjectilePrefab != null
            ? Instantiate(punchProjectilePrefab, muzzle, Quaternion.identity)
            : BuildEnergyBall(muzzle, fromRight ? rightColor : leftColor);

        float e = 0f;
        while (e < projectileTime && proj != null && drone != null)
        {
            e += Time.unscaledDeltaTime;
            proj.transform.position = Vector3.Lerp(muzzle, drone.transform.position, e / projectileTime);
            yield return null;
        }
        if (proj != null) Destroy(proj);
    }

    // A simple glowing sphere used when no projectile prefab is assigned.
    private GameObject BuildEnergyBall(Vector3 pos, Color c)
    {
        var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ball.transform.position = pos;
        ball.transform.localScale = Vector3.one * 0.18f;
        var col = ball.GetComponent<Collider>(); if (col != null) Destroy(col);
        var r = ball.GetComponent<Renderer>();
        var m = new Material(Shader.Find("Sprites/Default")); // unlit, always visible
        m.color = c;
        r.material = m;
        if (r.material.HasProperty(ID_Emission)) r.material.SetColor(ID_Emission, c * glow);
        return ball;
    }

    // Punch a hit drone back into the bot (damage + its blood/hit VFX play there).
    private IEnumerator PunchIntoBot(GameObject go, Transform bot, PlayerCombat botCombat)
    {
        if (go == null) yield break;
        Vector3 from = go.transform.position;
        Vector3 to   = bot.position + Vector3.up * 1.2f;
        float e = 0f, dur = 0.26f;
        while (e < dur && go != null)
        {
            e += Time.unscaledDeltaTime;
            go.transform.position = Vector3.Lerp(from, to, e / dur);
            go.transform.LookAt(bot);
            yield return null;
        }
        botCombat?.TakeDroneHit(deflectBotDamage);
        if (go != null) Destroy(go);
    }

    // ── Camera hit kick — a quick punchy jolt to the side you swung (no flip). ──
    private void DoCameraKick(int side)
    {
        if (_camRig == null) return;
        if (_camRoutine != null) StopCoroutine(_camRoutine);
        _camRoutine = StartCoroutine(CameraKickRoutine(side));
    }

    private IEnumerator CameraKickRoutine(int side)
    {
        Transform cam = _camRig;
        Vector3 basePos = cam.localPosition;
        float e = 0f;
        while (e < camKickTime)
        {
            e += Time.unscaledDeltaTime;
            float p = Mathf.Sin(Mathf.Clamp01(e / camKickTime) * Mathf.PI); // out and back
            cam.localPosition = basePos + Vector3.right * (side * camPunchKick * p) + Vector3.forward * (camPunchKick * 0.4f * p);
            yield return null;
        }
        cam.localPosition = basePos;
        _camRoutine = null;
    }

    // ── Drone spawn / tint ──────────────────────────────────────────────────────────────────────────────
    private GameObject SpawnDrone(Vector3 pos)
    {
        if (dronePrefab == null) return null;
        var go = Instantiate(dronePrefab, pos, Quaternion.identity);
        go.transform.localScale *= droneScale;
        var vrDrone = go.GetComponent<Drone>(); // strip VR flight; we drive position
        if (vrDrone != null) Destroy(vrDrone);
        return go;
    }

    private void TintDrone(GameObject go, Color c)
    {
        var rend = go != null ? go.GetComponentInChildren<Renderer>() : null;
        if (rend == null) return;
        var mat = rend.material; // instance
        if (mat.HasProperty(ID_BaseColor)) mat.SetColor(ID_BaseColor, c);
        if (mat.HasProperty(ID_RimColor))  mat.SetColor(ID_RimColor,  c);
        if (mat.HasProperty(ID_FlowColor)) mat.SetColor(ID_FlowColor, c);
        if (mat.HasProperty(ID_Emission))  mat.SetColor(ID_Emission,  c * glow);
    }

    // ── First-time tutorial ─────────────────────────────────────────────────────────────────────────────
    private IEnumerator ShowTutorial()
    {
        _tutorialActive = true;
        var rmm = RhythmRoundManager.Instance;
        rmm?.PauseTrackClock();
        float prevScale = Time.timeScale;
        Time.timeScale = 0f;

        float realElapsed = 0f;
        yield return null;
        bool clicked = false;
        while (!clicked)
        {
            realElapsed += Time.unscaledDeltaTime;
            if (MouseClickedThisFrame()) clicked = true;
            yield return null;
        }

        Time.timeScale = prevScale;
        rmm?.ResumeTrackClock(realElapsed);
        _tutorialActive = false;
        _arrow = 0;
    }

    private void OnGUI()
    {
        if (!_tutorialActive) return;
        int w = Screen.width, h = Screen.height;
        DrawRect(new Rect(0, 0, w, h), new Color(0f, 0f, 0f, 0.82f));

        var title = new GUIStyle(GUI.skin.label)
        { fontSize = Mathf.RoundToInt(h * 0.045f), fontStyle = FontStyle.Bold,
          alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.35f, 0.95f, 1f) } };
        var head = new GUIStyle(GUI.skin.label)
        { fontSize = Mathf.RoundToInt(h * 0.05f), fontStyle = FontStyle.Bold,
          alignment = TextAnchor.MiddleCenter, wordWrap = true };
        var body = new GUIStyle(GUI.skin.label)
        { fontSize = Mathf.RoundToInt(h * 0.028f), alignment = TextAnchor.UpperCenter, wordWrap = true,
          normal = { textColor = new Color(0.9f, 0.92f, 1f) } };
        var footer = new GUIStyle(GUI.skin.label)
        { fontSize = Mathf.RoundToInt(h * 0.03f), fontStyle = FontStyle.Bold,
          alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(1f, 0.9f, 0.3f) } };

        GUI.Label(new Rect(0, h * 0.10f, w, h * 0.08f), "DRONE RUSH — PUNCH THE DRONES", title);

        var leftPanel = new Rect(w * 0.06f, h * 0.28f, w * 0.40f, h * 0.42f);
        DrawRect(leftPanel, new Color(0.05f, 0.10f, 0.18f, 0.95f));
        DrawBorder(leftPanel, new Color(0.3f, 0.6f, 1f, 0.9f), 3);
        GUI.Label(new Rect(leftPanel.x, leftPanel.y + h * 0.03f, leftPanel.width, h * 0.07f), "◄ LEFT ARROW", head);
        GUI.Label(new Rect(leftPanel.x + 20, leftPanel.y + h * 0.14f, leftPanel.width - 40, leftPanel.height * 0.6f),
            "A drone flies at your LEFT side.\n\nPress the LEFT ARROW KEY on the beat\nto PUNCH it back into your opponent.", body);

        var rightPanel = new Rect(w * 0.54f, h * 0.28f, w * 0.40f, h * 0.42f);
        DrawRect(rightPanel, new Color(0.05f, 0.10f, 0.18f, 0.95f));
        DrawBorder(rightPanel, new Color(1f, 0.5f, 0.2f, 0.9f), 3);
        GUI.Label(new Rect(rightPanel.x, rightPanel.y + h * 0.03f, rightPanel.width, h * 0.07f), "RIGHT ARROW ►", head);
        GUI.Label(new Rect(rightPanel.x + 20, rightPanel.y + h * 0.14f, rightPanel.width - 40, rightPanel.height * 0.6f),
            "A drone flies at your RIGHT side.\n\nPress the RIGHT ARROW KEY on the beat\nto UPPERCUT it back into your opponent.", body);

        GUI.Label(new Rect(0, h * 0.78f, w, h * 0.08f), "CLICK ANYWHERE TO START", footer);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────────────
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

    private static int ReadArrow()
    {
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        if (k != null)
        {
            if (k.leftArrowKey.wasPressedThisFrame)  return -1;
            if (k.rightArrowKey.wasPressedThisFrame) return +1;
        }
        return 0;
#else
        if (Input.GetKeyDown(KeyCode.LeftArrow))  return -1;
        if (Input.GetKeyDown(KeyCode.RightArrow)) return +1;
        return 0;
#endif
    }

    private static bool MouseClickedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        var mo = Mouse.current;
        return mo != null && mo.leftButton.wasPressedThisFrame;
#else
        return Input.GetMouseButtonDown(0);
#endif
    }

    // ── OnGUI draw helpers ──────────────────────────────────────────────────────────────────────────────
    private static Texture2D _px;
    private static Texture2D Px
    {
        get { if (_px == null) { _px = new Texture2D(1, 1); _px.SetPixel(0, 0, Color.white); _px.Apply(); } return _px; }
    }

    private static void DrawRect(Rect r, Color c)
    {
        var prev = GUI.color; GUI.color = c; GUI.DrawTexture(r, Px); GUI.color = prev;
    }

    private static void DrawBorder(Rect r, Color c, int t)
    {
        DrawRect(new Rect(r.x, r.y, r.width, t), c);
        DrawRect(new Rect(r.x, r.yMax - t, r.width, t), c);
        DrawRect(new Rect(r.x, r.y, t, r.height), c);
        DrawRect(new Rect(r.xMax - t, r.y, t, r.height), c);
    }
}
