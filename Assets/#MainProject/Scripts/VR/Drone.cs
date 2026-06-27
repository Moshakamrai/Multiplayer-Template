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

    // PC variant — no DroneRushSegment owner; outcomes are handled via the callbacks.
    public System.Action<Drone, float> onPunched;   // (drone, power)
    public System.Action<Drone>        onDodged;
    public System.Action<Drone>        onMissed;
    public System.Action<Drone>        onHitBot;

    public void InitPC(Transform player, Transform bot, Vector3 arriveOffset, float arriveTime,
                       bool isRight, bool isFinisher = false)
    {
        Init(player, bot, arriveOffset, arriveTime, null, isRight, isFinisher);
    }

    public void Init(Transform player, Transform bot, Vector3 arriveOffset, float arriveTime,
                     DroneRushSegment owner, bool isRight, bool isFinisher)
    {
        _player = player; _bot = bot; _arriveOffset = arriveOffset; _arriveTime = arriveTime; _owner = owner;
        _isRight = isRight; _isFinisher = isFinisher;
        _spawnPos = transform.position;
        _spawnTime = Time.unscaledTime;
        _born = Time.unscaledTime;
        _phase = Random.value * 10f;
        _arcSign = isRight ? 1f : -1f;

        Color tint = isRight ? new Color(1f, 0.28f, 0.30f) : new Color(0.3f, 0.6f, 1f); // red R / blue L

        // Trail disabled per design — turn off any TrailRenderer on the drone.
        _trail = GetComponent<TrailRenderer>();
        if (_trail != null) _trail.enabled = false;

        // Tint the BODY material (Custom/EnergyGlove) so the whole drone glows the hand colour. The
        // drone is multi-material; we only instance + recolour the BODY slot (default element 0).
        if (bodyRenderer == null) bodyRenderer = GetComponentInChildren<Renderer>();
        if (bodyRenderer != null)
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

        // Face the player, banking into the sway, plus a constant idle spin for a hovering-drone feel.
        float bank = Mathf.Cos(now * swaySpeed + _phase) * bankAngle * fade;
        Quaternion look = Quaternion.LookRotation(_player.position - transform.position);
        transform.rotation = look * Quaternion.Euler(0f, 0f, bank);
        transform.Rotate(Vector3.up, idleSpin * Time.unscaledDeltaTime, Space.Self);

        float distToPlayer = Vector3.Distance(transform.position, aimPoint);

        // APPROACH HAPTIC: as the drone closes in, buzz the hand that should hit it (red→right, blue→left)
        // so the player FEELS "punch this now", growing stronger the nearer it gets.
        if (distToPlayer < 1.6f)
        {
            float urgency = Mathf.Clamp01(1f - (distToPlayer - arriveRange) / 1.6f);
            VRHaptics.Rumble(_isRight ? VRHaptics.Hand.Right : VRHaptics.Hand.Left, 0.05f + 0.35f * urgency);
        }

        // PUNCH: hand near the drone, moving fast. (Punch power = swing speed → harder bounce/damage.)
        if (VRHands.PunchedAt(transform.position, punchRadius, punchMinSpeed, out bool rightHand, out float speed))
        {
            // Reaching the drone roughly on the beat is what makes the bounce "count" — punch closeness
            // to _arriveTime sets the on-beat reward. The punch itself always responds (feels physical).
            float beatError = Mathf.Abs(now - _arriveTime);
            float onBeat = Mathf.Clamp01(1f - beatError / 0.25f); // 1 = dead-on the beat, 0 = >250ms off
            float power = Mathf.Clamp01(speed / 6f);               // swing speed → punch power

            // Feedback: hit sound (2D so it's always clearly audible) + a solid thump on the hand +
            // a hit VFX spawned right where the drone was punched.
            PlayPunchSound();
            VRHaptics.Rumble(rightHand ? VRHaptics.Hand.Right : VRHaptics.Hand.Left, 0.8f);
            if (hitVfxPrefab != null)
            {
                var fx = Instantiate(hitVfxPrefab, transform.position, Quaternion.identity);
                Destroy(fx, hitVfxLifetime);
            }

            _owner?.OnDronePunched(this, power, onBeat, rightHand, _isFinisher);
            onPunched?.Invoke(this, power);
            StartBounce();
            return;
        }

        // Arrived at the player and was NOT punched → check dodge, else it's a miss.
        if (distToPlayer <= arriveRange || now >= _arriveTime + 0.15f)
        {
            bool dodged = _headRestCaptured && VRHands.Tracking &&
                          Vector3.Distance(VRHands.HeadPos, _headRestPos) >= dodgeDistance;
            if (dodged) { _owner?.OnDroneDodged(this); onDodged?.Invoke(this); }
            else        { _owner?.OnDroneMissed(this); onMissed?.Invoke(this); }
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
    }

    private Vector3 QuadraticBezier(Vector3 a, Vector3 b, Vector3 c, float t)
    {
        float u = 1f - t;
        return u * u * a + 2f * u * t * b + t * t * c;
    }

    private void TickBouncing()
    {
        if (_bot == null) { Finish(); return; }
        transform.position = Vector3.MoveTowards(transform.position, _bot.position, bounceSpeed * Time.unscaledDeltaTime);
        transform.LookAt(_bot);
        transform.Rotate(Vector3.forward, idleSpin * 4f * Time.unscaledDeltaTime, Space.Self); // fast spin on the zip back
        if (Vector3.Distance(transform.position, _bot.position) < 0.4f)
        {
            _owner?.OnDroneHitBot(this);
            onHitBot?.Invoke(this);
            Finish();
        }
    }

    private void Finish()
    {
        _state = State.Done;
        Destroy(gameObject, 0.05f);
    }
}
