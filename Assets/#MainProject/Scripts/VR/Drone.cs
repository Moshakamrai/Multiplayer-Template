using UnityEngine;

/// One drone in the DroneRushSegment. Spawned at the portal, it flies toward the player with its speed
/// tuned so it ARRIVES at punch range exactly on a beat. The player either:
///   • PUNCHES it (hand near + moving fast)  → it bounces back to the bot; on-beat landing = damage+points
///   • DODGES it with their head (head moved aside as it arrives) → small "save" points, flies past
///   • MISSES (neither) → screen sparks (no real damage), drone despawns
///
/// Runs LOCALLY on the owning client (that's where VR hand/head tracking lives). It never deals damage
/// itself — it reports the outcome to DroneRushSegment, which routes damage/score through the server.
/// No real physics collision: pure distance + timing checks (chosen for VR/multiplayer reliability).
public class Drone : MonoBehaviour
{
    public enum State { Incoming, Bouncing, Done }

    // Tunables (set by the segment when it spawns the drone).
    private Transform _player;       // the head/camera to fly at
    private Transform _bot;          // where a punched drone bounces to
    private float _arriveTime;       // unscaled time the drone should reach punch range (a beat)
    private DroneRushSegment _owner; // reports outcomes here
    private bool _isRight;           // red = right hand, blue = left hand
    private bool _isFinisher;        // the big climactic drone
    public bool IsFinisher => _isFinisher;

    [Header("Glow / colour (Custom/EnergyGlove shader)")]
    [Tooltip("The drone's Mesh Renderer (multi-material). Leave empty to auto-find in children. " +
             "The BODY material — element index below — gets tinted red (right) / blue (left).")]
    public Renderer bodyRenderer;
    [Tooltip("Which material slot is the BODY (the main one running Custom/EnergyGlove). On your drone " +
             "BODY is Element 0.")]
    public int bodyMaterialIndex = 0;
    [Tooltip("How bright the emissive/rim glow is. Keep modest (1–1.5) or HDR + bloom blows it out to white.")]
    public float emissionBoost = 1.2f;
    [Tooltip("Overall size multiplier for the drone (1 = prefab size). Lower = smaller drones.")]
    public float droneScale = 0.75f;

    [Header("Football mode (World Cup event)")]
    [Tooltip("Replace the drone body with a spinning football (procedural — no assets needed). The " +
             "left/right hand cue becomes a soft coloured glow on the ball instead of a full-body tint.")]
    public bool footballMode = true;
    [Tooltip("Ball diameter in metres (footballMode). The finisher ball is automatically bigger.")]
    public float ballDiameter = 0.38f;
    [Tooltip("Ball tumble spin while flying in (deg/sec) — sells the 'kicked ball' feel.")]
    public float ballTumbleSpeed = 420f;
    [Tooltip("Ball glow (hand colour) when far away. Ramps up to ballGlowNear as it closes in.")]
    public float ballGlowStrength = 0.6f;
    [Tooltip("Ball glow when it's about to arrive — the bright 'punch NOW' cue.")]
    public float ballGlowNear = 2.4f;
    // The struck ball ARCS to a destination chosen by HIT QUALITY (power + on-beat): a clean strike
    // goes for the GOAL, a decent one drills the BOT, a weak one falls short. Aim assist guides it there
    // as a real ballistic arc — physical and cool, never a dead-straight snap.
    [Tooltip("Combined hit quality (0–1) at/above which a struck ball goes for the GOAL. Below it, the " +
             "ball is aimed at the BOT instead. Quality = 0.6·power + 0.4·onBeat.")]
    [Range(0f, 1f)] public float goalQualityThreshold = 0.6f;
    [Tooltip("Below THIS quality the ball just falls short (weak, mistimed contact) — no bot damage.")]
    [Range(0f, 1f)] public float reachQualityThreshold = 0.3f;
    [Tooltip("How strongly the shot curves toward its chosen target (goal or bot). 1 = dead accurate, " +
             "lower = the swing direction still bends the shot for a more physical, less locked-on feel.")]
    [Range(0f, 1f)] public float aimAssist = 0.8f;
    [Tooltip("Flight time (s) of the arc to the target — lower = flatter/faster rocket, higher = loftier.")]
    public float shotFlightTime = 0.55f;
    [Tooltip("How far from the goal-mouth centre still counts as IN (m). Size to your goalpost.")]
    public float goalMouthRadius = 0.9f;
    [Tooltip("Gravity (m/s²) on the struck ball — gives every shot a real ballistic arc.")]
    public float ballGravity = 14f;

    [Header("Audio + haptics")]
    [Tooltip("Sound played when you successfully punch a drone.")]
    public AudioClip punchSound;
    [Tooltip("Punch sound volume.")]
    public float punchVolume = 0.7f;

    [Header("Hit VFX")]
    [Tooltip("VFX prefab spawned at the drone's position when it's punched. Drag your drone-hit effect here.")]
    public GameObject hitVfxPrefab;
    [Tooltip("Auto-destroy the spawned hit VFX after this many seconds.")]
    public float hitVfxLifetime = 2f;

    // EnergyGlove shader colour properties (verified against the shader).
    static readonly int ID_BaseColor = Shader.PropertyToID("_BaseColor");
    static readonly int ID_RimColor  = Shader.PropertyToID("_RimColor");
    static readonly int ID_FlowColor = Shader.PropertyToID("_FlowColor");
    static readonly int ID_Emission  = Shader.PropertyToID("_EmissionColor");

    [Header("Feel")]
    [Tooltip("How close (m) the hand must be to count as a punch.")]
    public float punchRadius = 0.95f;
    [Tooltip("Minimum hand speed (m/s) for a swing to count as a punch.")]
    public float punchMinSpeed = 0.8f;
    [Tooltip("How far (m) the head must move from its resting line to count as a dodge.")]
    public float dodgeDistance = 0.28f;
    [Tooltip("Punch-range distance from the player at which the drone is 'arrived'. Bigger = it stops " +
             "further out (less in-your-face).")]
    public float arriveRange = 0.55f;
    [Tooltip("Speed (m/s) of the bounce back toward the bot.")]
    public float bounceSpeed = 14f;

    [Header("Drone flight feel")]
    [Tooltip("Arc sideways amplitude (m). Drones spawn in the middle but arc toward middle-left/right.")]
    public float arcAmount = 0.7f;
    [Tooltip("Vertical bob height (m) as it flies in.")]
    public float bobAmount = 0.18f;
    [Tooltip("Bob speed.")]
    public float bobSpeed = 4f;
    [Tooltip("Sideways weave amplitude (m).")]
    public float swayAmount = 0.12f;
    [Tooltip("Sideways weave speed.")]
    public float swaySpeed = 2.2f;
    [Tooltip("How hard it banks/tilts into its weave (degrees).")]
    public float bankAngle = 25f;
    [Tooltip("Constant idle spin of the body (deg/sec) for a hovering-drone feel.")]
    public float idleSpin = 40f;

    private State _state = State.Incoming;
    private Vector3 _spawnPos;
    private Vector3 _arriveOffset; // lateral offset so it ends to the side of the head, not in the face
    private float   _spawnTime;
    private float   _phase;        // random per-drone so they don't all weave in sync
    private float   _arcSign;      // +1 = arcs to the right, -1 = arcs to the left
    private Vector3 _headRestPos;
    private bool    _headRestCaptured;
    private TrailRenderer _trail;
    private Material _mat;
    private Vector3  _fullScale;
    private float    _born; // for the spawn pop-in
    private Vector3  _tumbleAxis = Vector3.right; // football mode: world axis the ball rolls around
    private Color    _tint;          // hand colour, kept for the proximity glow ramp
    private Transform _goal;         // goal mouth (optional) — punched balls fly here instead of the bot
    private float    _punchOnBeat;   // on-beat quality of the punch (0–1)
    private float    _punchPower;    // swing power of the punch (0–1)
    private bool     _punchRight;    // which glove actually made contact (its velocity launches the ball)
    private Vector3  _vel;           // ballistic velocity while bouncing (football mode)
    private float    _groundY;       // floor height for ground bounces
    private int      _grounds;       // how many ground bounces this shot has done
    private float    _bounceT0;      // when the bounce started (safety timeout)
    private bool     _readyBuzzed;   // the one-shot "ball entered range" haptic tick has fired
    private bool     _goalShot;      // this shot's quality earned a goal attempt (vs a bot shot)

    // Read by DroneRushSegment when the ball's fate resolves (goal / bot / fell short) — scoring and
    // bot damage happen at the OUTCOME now, since with real physics the destination isn't known at punch.
    public float PunchPower  => _punchPower;
    public float PunchOnBeat => _punchOnBeat;

    public void Init(Transform player, Transform bot, Vector3 arriveOffset, float arriveTime,
                     DroneRushSegment owner, bool isRight, bool isFinisher, Transform goal = null)
    {
        _player = player; _bot = bot; _arriveOffset = arriveOffset; _arriveTime = arriveTime; _owner = owner;
        _isRight = isRight; _isFinisher = isFinisher; _goal = goal;
        _spawnPos = transform.position;
        _spawnTime = Time.unscaledTime;
        _born = Time.unscaledTime;
        _phase = Random.value * 10f;
        _arcSign = isRight ? 1f : -1f;

        Color tint = isRight ? new Color(1f, 0.28f, 0.30f) : new Color(0.3f, 0.6f, 1f); // red R / blue L
        _tint = tint;

        // Trail disabled per design — turn off any TrailRenderer on the drone.
        _trail = GetComponent<TrailRenderer>();
        if (_trail != null) _trail.enabled = false;

        // Tint the BODY material (Custom/EnergyGlove) so the whole drone glows the hand colour. The
        // drone is multi-material; we only instance + recolour the BODY slot (default element 0).
        if (bodyRenderer == null) bodyRenderer = GetComponentInChildren<Renderer>();
        if (footballMode)
        {
            BuildFootballVisual(tint, isFinisher);
        }
        else if (bodyRenderer != null)
        {
            var mats = bodyRenderer.materials; // instances — won't touch the shared assets
            int idx = Mathf.Clamp(bodyMaterialIndex, 0, mats.Length - 1);
            _mat = mats[idx];
            // Drive the EnergyGlove colour channels to the tint. Keep multipliers LOW so the actual
            // colour reads instead of HDR blowing out to white under bloom.
            if (_mat.HasProperty(ID_BaseColor)) _mat.SetColor(ID_BaseColor, tint);
            if (_mat.HasProperty(ID_RimColor))  _mat.SetColor(ID_RimColor,  tint);
            if (_mat.HasProperty(ID_FlowColor)) _mat.SetColor(ID_FlowColor, tint);
            if (_mat.HasProperty(ID_Emission))  _mat.SetColor(ID_Emission,  tint * emissionBoost);
            bodyRenderer.materials = mats; // reassign so the instanced array sticks
        }

        // Spawn-from-portal feel: start tiny and pop to full (reduced) size.
        _fullScale = transform.localScale * droneScale * (isFinisher ? 1.7f : 1f);
        transform.localScale = Vector3.zero;
    }

    // ── Football mode: swap the drone body for a procedural spinning football ──────────────────
    // Hides every renderer on the drone prefab and parents a white/black patched sphere in its place.
    // No assets needed: the ball texture is generated once and cached for the whole session.
    private void BuildFootballVisual(Color tint, bool isFinisher)
    {
        foreach (var r in GetComponentsInChildren<Renderer>())
            r.enabled = false;

        var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ball.name = "Football";
        Destroy(ball.GetComponent<Collider>()); // outcome checks are distance-based, no physics wanted
        ball.transform.SetParent(transform, false);
        ball.transform.localPosition = Vector3.zero;
        ball.transform.localRotation = Quaternion.Euler(Random.value * 360f, Random.value * 360f, 0f);

        // The root pops to prefabScale × droneScale (finisher ×1.7 rides on top and should keep the
        // ball bigger too) — size the child so the ball's full world diameter lands on ballDiameter.
        Vector3 baseFull = Vector3.Scale(transform.localScale, Vector3.one * droneScale);
        ball.transform.localScale = new Vector3(
            ballDiameter / Mathf.Max(0.0001f, baseFull.x),
            ballDiameter / Mathf.Max(0.0001f, baseFull.y),
            ballDiameter / Mathf.Max(0.0001f, baseFull.z));

        Shader sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) sh = Shader.Find("Standard");
        _mat = new Material(sh);
        BallTextures(_isRight, out Texture2D baseTex, out Texture2D emisTex);
        _mat.mainTexture = baseTex;
        _mat.color = Color.white;
        if (_mat.HasProperty("_Smoothness")) _mat.SetFloat("_Smoothness", 0.7f);       // glossy tech shell
        else if (_mat.HasProperty("_Glossiness")) _mat.SetFloat("_Glossiness", 0.7f);
        // Neon glow: the emission MAP masks the glow to the panel outlines + circuit grid, so the ball
        // reads as dark carbon with hand-coloured light lines (red=right, blue=left). Ramps up close-in.
        _mat.EnableKeyword("_EMISSION");
        if (_mat.HasProperty("_EmissionMap")) _mat.SetTexture("_EmissionMap", emisTex);
        if (_mat.HasProperty(ID_Emission)) _mat.SetColor(ID_Emission, tint * (ballGlowStrength * EMIS_LINE_BOOST));
        ball.GetComponent<Renderer>().material = _mat;
    }

    // The emission is masked to thin lines, so it needs a hotter multiplier than a whole-surface glow
    // for the same perceived brightness — keeps the existing inspector glow values sensible.
    private const float EMIS_LINE_BOOST = 2.5f;

    // Cyberpunk ball — near-black carbon shell, dark hand-colour panels at the 12 icosahedron vertex
    // directions (where the pentagons sit on a real ball), NEON panel outlines + a faint lat/long
    // circuit grid baked into a grayscale emission map (tinted per hand at runtime).
    private static Texture2D _ballTexRight, _ballTexLeft, _ballEmisRight, _ballEmisLeft;
    private static void BallTextures(bool isRight, out Texture2D baseTex, out Texture2D emisTex)
    {
        baseTex = isRight ? _ballTexRight : _ballTexLeft;
        emisTex = isRight ? _ballEmisRight : _ballEmisLeft;
        if (baseTex != null && emisTex != null) return;
        Color patchCol = isRight ? new Color(0.72f, 0.03f, 0.06f) : new Color(0.04f, 0.18f, 0.75f);
        Color shellCol = new Color(0.05f, 0.055f, 0.07f);    // carbon-dark shell
        Color panelCol = Color.Lerp(shellCol, patchCol, 0.30f); // panels barely tinted — the neon does the talking
        const int W = 256, H = 128;
        const float PHI = 1.6180339f;
        Vector3[] spots =
        {
            new Vector3(0,  1,  PHI), new Vector3(0,  1, -PHI), new Vector3(0, -1,  PHI), new Vector3(0, -1, -PHI),
            new Vector3( 1,  PHI, 0), new Vector3( 1, -PHI, 0), new Vector3(-1,  PHI, 0), new Vector3(-1, -PHI, 0),
            new Vector3( PHI, 0,  1), new Vector3(-PHI, 0,  1), new Vector3( PHI, 0, -1), new Vector3(-PHI, 0, -1),
        };
        for (int i = 0; i < spots.Length; i++) spots[i].Normalize();

        var bTex = new Texture2D(W, H, TextureFormat.RGBA32, true);
        var eTex = new Texture2D(W, H, TextureFormat.RGBA32, true);
        var bPx = new Color32[W * H];
        var ePx = new Color32[W * H];
        float spotCos = Mathf.Cos(20f * Mathf.Deg2Rad);   // panel angular radius
        float edgeCos = Mathf.Cos(23f * Mathf.Deg2Rad);   // soft edge falloff
        for (int y = 0; y < H; y++)
        {
            float lat = ((y + 0.5f) / H - 0.5f) * Mathf.PI;
            float cl = Mathf.Cos(lat), sl = Mathf.Sin(lat);
            for (int x = 0; x < W; x++)
            {
                float lon = ((x + 0.5f) / W - 0.5f) * 2f * Mathf.PI;
                Vector3 dir = new Vector3(cl * Mathf.Sin(lon), sl, cl * Mathf.Cos(lon));
                float best = -1f;
                for (int s = 0; s < spots.Length; s++)
                {
                    float d = Vector3.Dot(dir, spots[s]);
                    if (d > best) best = d;
                }
                float inPatch = Mathf.InverseLerp(edgeCos, spotCos, best); // 1 inside a panel, 0 outside

                // NEON: bright ring right on the panel boundary + a faint fill inside the panel.
                float ring = Mathf.Pow(1f - Mathf.Abs(inPatch * 2f - 1f), 2f);
                // Circuit grid: thin lat/long light lines over the dark shell (kept off the panels).
                float gridLon = Mathf.Pow(Mathf.Abs(Mathf.Sin(lon * 6f)), 48f);
                float gridLat = Mathf.Pow(Mathf.Abs(Mathf.Sin(lat * 4f)), 48f);
                float grid = Mathf.Max(gridLon, gridLat) * 0.35f * (1f - inPatch);
                float emis = Mathf.Max(ring, Mathf.Max(grid, inPatch * 0.12f));

                bPx[y * W + x] = (Color32)Color.Lerp(shellCol, panelCol, inPatch);
                byte e = (byte)Mathf.RoundToInt(Mathf.Clamp01(emis) * 255f);
                ePx[y * W + x] = new Color32(e, e, e, 255);
            }
        }
        bTex.SetPixels32(bPx); bTex.Apply(true);
        eTex.SetPixels32(ePx); eTex.Apply(true);
        if (isRight) { _ballTexRight = bTex; _ballEmisRight = eTex; }
        else         { _ballTexLeft  = bTex; _ballEmisLeft  = eTex; }
        baseTex = bTex; emisTex = eTex;
    }

    private void Update()
    {
        if (_player == null) { Destroy(gameObject); return; }

        // Pop-in from the portal over ~0.25s (elastic-ish overshoot).
        float age = Time.unscaledTime - _born;
        if (age < 0.3f)
        {
            float p = Mathf.Clamp01(age / 0.25f);
            float s = 1f + Mathf.Sin(p * Mathf.PI) * 0.25f; // slight overshoot
            transform.localScale = _fullScale * Mathf.Clamp01(p) * s;
        }
        else if (transform.localScale != _fullScale)
        {
            transform.localScale = _fullScale;
        }

        switch (_state)
        {
            case State.Incoming:  TickIncoming();  break;
            case State.Bouncing:  TickBouncing();  break;
        }
    }

    // Note: material instances created via bodyRenderer.materials are auto-destroyed with the GameObject.

    private void TickIncoming()
    {
        // Capture the head's resting position once, so we can detect a sideways dodge later.
        if (!_headRestCaptured && VRHands.Tracking) { _headRestPos = VRHands.HeadPos; _headRestCaptured = true; }

        // Base path: spawn in the middle, then arc toward middle-left or middle-right before reaching
        // punch range. The arc makes the segment feel dynamic while keeping every drone hittable.
        Vector3 aimPoint = _player.position + _arriveOffset;
        Vector3 toAim = (aimPoint - _spawnPos);
        Vector3 target = aimPoint - toAim.normalized * arriveRange;
        float now = Time.unscaledTime;
        float total = Mathf.Max(0.0001f, _arriveTime - _spawnTime);
        float t = Mathf.Clamp01((now - _spawnTime) / total);
        float eased = t * t * (3f - 2f * t);                 // smoothstep accel-in

        Vector3 flightDir = toAim.normalized;
        Vector3 sideAxis = Vector3.Cross(Vector3.up, flightDir);
        if (sideAxis.sqrMagnitude < 0.001f) sideAxis = Vector3.right;
        sideAxis.Normalize();

        Vector3 basePos;
        if (arcAmount > 0.001f)
        {
            Vector3 mid = Vector3.Lerp(_spawnPos, target, 0.5f);
            Vector3 control = mid + sideAxis * arcAmount * _arcSign;
            basePos = QuadraticBezier(_spawnPos, control, target, eased);
        }
        else
        {
            basePos = Vector3.Lerp(_spawnPos, target, eased);
        }

        // Drone wobble: bob (vertical) + sway (sideways) layered onto the path, FADING to zero as it
        // arrives so the punch target stays precise.
        float fade = 1f - eased;                              // full wobble far out, none on arrival
        float bob  = Mathf.Sin(now * bobSpeed  + _phase) * bobAmount  * fade;
        float sway = Mathf.Sin(now * swaySpeed + _phase) * swayAmount * fade;
        transform.position = basePos + Vector3.up * bob + sideAxis * sway;

        if (footballMode)
        {
            // Kicked-ball topspin: roll around the side axis (perpendicular to the flight path).
            _tumbleAxis = sideAxis;
            transform.Rotate(_tumbleAxis, ballTumbleSpeed * Time.unscaledDeltaTime, Space.World);
        }
        else
        {
            // Face the player, banking into the sway, plus a constant idle spin for a hovering-drone feel.
            float bank = Mathf.Cos(now * swaySpeed + _phase) * bankAngle * fade;
            Quaternion look = Quaternion.LookRotation(_player.position - transform.position);
            transform.rotation = look * Quaternion.Euler(0f, 0f, bank);
            transform.Rotate(Vector3.up, idleSpin * Time.unscaledDeltaTime, Space.Self);
        }

        float distToPlayer = Vector3.Distance(transform.position, aimPoint);

        // Football glow ramp: brighter hand-colour glow the closer it gets — the visual "punch NOW" cue.
        if (footballMode && _mat != null && _mat.HasProperty(ID_Emission))
        {
            float near = Mathf.Clamp01(1f - (distToPlayer - arriveRange) / 2.2f);
            _mat.SetColor(ID_Emission, _tint * (Mathf.Lerp(ballGlowStrength, ballGlowNear, near * near) * EMIS_LINE_BOOST));
        }

        // "READY" CUE: a SINGLE crisp tick the moment the ball crosses into strike range on the hand
        // that should hit it — fired ONCE per ball, not every frame. (The old per-frame rumble buzzed
        // constantly and told you nothing; the impact jolt below is what you actually feel.)
        if (!_readyBuzzed && distToPlayer < arriveRange + 0.45f)
        {
            _readyBuzzed = true;
            VRHaptics.BallReady(_isRight ? VRHaptics.Hand.Right : VRHaptics.Hand.Left);
        }

        // PUNCH: hand near the drone, moving fast. (Punch power = swing speed → harder bounce/damage.)
        if (VRHands.PunchedAt(transform.position, punchRadius, punchMinSpeed, out bool rightHand, out float speed))
        {
            // Reaching the drone roughly on the beat is what makes the bounce "count" — punch closeness
            // to _arriveTime sets the on-beat reward. The punch itself always responds (feels physical).
            float beatError = Mathf.Abs(now - _arriveTime);
            float onBeat = Mathf.Clamp01(1f - beatError / 0.25f); // 1 = dead-on the beat, 0 = >250ms off
            float power = Mathf.Clamp01(speed / 6f);               // swing speed → punch power

            // Feedback: hit sound (2D so it's always clearly audible) + a real IMPACT JOLT scaled by
            // how hard you swung (a crack that decays, not a flat buzz) + the ball's own hit VFX burst
            // at the contact point.
            PlayPunchSound();
            VRHaptics.BallImpact(rightHand ? VRHaptics.Hand.Right : VRHaptics.Hand.Left, power);
            if (hitVfxPrefab != null)
            {
                var fx = Instantiate(hitVfxPrefab, transform.position, Quaternion.identity);
                Destroy(fx, hitVfxLifetime);
            }

            _punchOnBeat = onBeat;
            _punchPower = power;
            _punchRight = rightHand;
            StartBounce();
            _owner?.OnDronePunched(this, power, onBeat, rightHand, _isFinisher);
            return;
        }

        // Arrived at the player and was NOT punched → check dodge, else it's a miss.
        if (distToPlayer <= arriveRange || now >= _arriveTime + 0.15f)
        {
            bool dodged = _headRestCaptured && VRHands.Tracking &&
                          Vector3.Distance(VRHands.HeadPos, _headRestPos) >= dodgeDistance;
            if (dodged) _owner?.OnDroneDodged(this);
            else        _owner?.OnDroneMissed(this);
            Finish();
        }
    }

    // ONE shared 2D AudioSource for all drone punch sounds — reused via PlayOneShot, so a dense rush
    // doesn't allocate a GameObject+AudioSource per hit (that was GC/perf churn during the lag spike).
    private static AudioSource _sfx;
    private void PlayPunchSound()
    {
        if (punchSound == null) return;
        if (_sfx == null)
        {
            var go = new GameObject("~DroneSFX");
            DontDestroyOnLoad(go);
            _sfx = go.AddComponent<AudioSource>();
            _sfx.playOnAwake = false;
            _sfx.spatialBlend = 0f; // 2D
        }
        _sfx.PlayOneShot(punchSound, punchVolume);
    }

    private void StartBounce()
    {
        _state = State.Bouncing;
        _bounceT0 = Time.unscaledTime;
        _grounds = 0;
        _groundY = _bot != null ? _bot.position.y : transform.position.y - 1.2f;

        if (!footballMode) return; // classic drones keep the straight MoveTowards zip-back

        // HIT QUALITY picks the destination: a clean, on-beat strike goes for the GOAL; a decent one
        // drills the BOT; a weak/mistimed one falls short. (power is the swing, onBeat is the timing.)
        float quality = _punchPower * 0.6f + _punchOnBeat * 0.4f;
        _goalShot = _goal != null && quality >= goalQualityThreshold;
        bool reaches = quality >= reachQualityThreshold;

        Vector3 targetPos;
        if (_goalShot)                     targetPos = _goal.position;
        else if (_bot != null && reaches)  targetPos = _bot.position + Vector3.up * 1.0f; // chest, not feet
        else
        {
            // Weak shot: aim a short, low point in front — it'll arc down and die in the turf.
            Vector3 fwd = ((_bot != null ? _bot.position : _player.position) - transform.position);
            fwd.y = 0f; fwd = fwd.sqrMagnitude > 0.001f ? fwd.normalized : Vector3.forward;
            targetPos = transform.position + fwd * Random.Range(1.2f, 2.2f);
            targetPos.y = _groundY;
        }

        // The RAW swing direction (for the physical feel), then bend it toward the chosen target by
        // aimAssist so a committed punch actually gets there while still curving off the real swing.
        Vector3 swing = _punchRight ? VRHands.RightVelocity : VRHands.LeftVelocity;
        Vector3 toTarget = targetPos - transform.position;
        float dist = toTarget.magnitude;

        // Solve the ballistic launch velocity that lands ON the target after shotFlightTime under gravity.
        Vector3 g = Vector3.down * ballGravity;
        float T = Mathf.Max(0.2f, shotFlightTime);
        Vector3 ballisticVel = toTarget / T - 0.5f * g * T;

        // Blend the pure-aim ballistic solution with the player's actual swing direction (kept at the
        // ballistic speed) — aimAssist 1 = perfectly on target, lower = more swing-flavoured curve.
        if (swing.sqrMagnitude > 0.25f)
        {
            Vector3 swingVel = swing.normalized * ballisticVel.magnitude;
            _vel = Vector3.Slerp(swingVel, ballisticVel, Mathf.Clamp01(aimAssist));
        }
        else _vel = ballisticVel; // no usable swing vector → just take the clean arc

        // COOL FACTOR: light up a streaking trail behind the struck ball — gold for a goal attempt,
        // the hand colour otherwise — and flare the ball's own glow so it reads as a charged rocket.
        Color streak = _goalShot ? new Color(1f, 0.82f, 0.15f) : _tint;
        if (_trail == null) _trail = GetComponent<TrailRenderer>();
        if (_trail == null) _trail = gameObject.AddComponent<TrailRenderer>();
        _trail.enabled = true;
        _trail.time = 0.22f;
        _trail.startWidth = ballDiameter * (_goalShot ? 0.9f : 0.6f);
        _trail.endWidth = 0f;
        _trail.numCapVertices = 4;
        var tmat = new Material(Shader.Find("Sprites/Default"));
        tmat.color = streak;
        _trail.material = tmat;
        _trail.startColor = new Color(streak.r, streak.g, streak.b, 0.9f);
        _trail.endColor   = new Color(streak.r, streak.g, streak.b, 0f);
        if (_mat != null && _mat.HasProperty(ID_Emission))
            _mat.SetColor(ID_Emission, streak * (ballGlowNear * EMIS_LINE_BOOST * 1.3f));
    }

    private Vector3 QuadraticBezier(Vector3 a, Vector3 b, Vector3 c, float t)
    {
        float u = 1f - t;
        return u * u * a + 2f * u * t * b + t * t * c;
    }

    private void TickBouncing()
    {
        float dt = Time.unscaledDeltaTime;

        if (footballMode)
        {
            // Ballistic flight: integrate gravity; tumble around the axis perpendicular to travel.
            _vel += Vector3.down * ballGravity * dt;
            transform.position += _vel * dt;
            Vector3 flat = _vel; flat.y = 0f;
            if (flat.sqrMagnitude > 0.01f) _tumbleAxis = Vector3.Cross(Vector3.up, flat.normalized);
            transform.Rotate(_tumbleAxis, ballTumbleSpeed * 2.5f * dt, Space.World);

            // OUTCOMES — detected, not chosen. Checked in priority order each frame:
            // 1) Ball entered the goal mouth → GOAL (a bounced-in trickler counts too — that's football).
            if (_goal != null && Vector3.Distance(transform.position, _goal.position) <= goalMouthRadius)
            {
                _owner?.OnDroneHitBot(this, true);
                Finish();
                return;
            }
            // 2) Ball struck the bot's body → slams into him.
            if (_bot != null && Vector3.Distance(transform.position, _bot.position + Vector3.up * 1f) <= 0.8f)
            {
                _owner?.OnDroneHitBot(this, false);
                Finish();
                return;
            }
            // 3) Ground contact: two damped bounces, then the shot is dead — it fell short/wide.
            float ballR = ballDiameter * 0.5f;
            if (transform.position.y <= _groundY + ballR && _vel.y < 0f)
            {
                if (_grounds < 2)
                {
                    _grounds++;
                    var p = transform.position; p.y = _groundY + ballR; transform.position = p;
                    _vel = new Vector3(_vel.x * 0.55f, -_vel.y * 0.4f, _vel.z * 0.55f);
                }
                else { _owner?.OnBallFellShort(this); Finish(); return; }
            }
            // 4) Safety: flew off somewhere forever → same as falling short.
            if (Time.unscaledTime - _bounceT0 > 3f) { _owner?.OnBallFellShort(this); Finish(); }
            return;
        }

        // Classic (non-football) drones: straight zip back into the bot, as always.
        if (_bot == null) { Finish(); return; }
        transform.position = Vector3.MoveTowards(transform.position, _bot.position, bounceSpeed * dt);
        transform.LookAt(_bot);
        transform.Rotate(Vector3.forward, idleSpin * 4f * dt, Space.Self); // fast spin on the zip back
        if (Vector3.Distance(transform.position, _bot.position) < 0.4f)
        {
            _owner?.OnDroneHitBot(this, false);
            Finish();
        }
    }

    private void Finish()
    {
        _state = State.Done;
        Destroy(gameObject, 0.05f);
    }
}
