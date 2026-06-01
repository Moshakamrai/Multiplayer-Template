using UnityEngine;

/// <summary>
/// Beat-timing floor. The floor is one CLOCK — color/brightness mean STATE, not tile identity.
///
/// Instead of one sudden brighten, the beat is announced as a STAGED COUNTDOWN (like F1 start
/// lights): over a long lead-in the 6 bands light up one-by-one toward the player, each with a
/// sharp "tick" pop. When the last (nearest) band lights, the beat is imminent — then the whole
/// floor flashes GREEN. Lots of pointers, fully predictable.
///
///   WAIT   — dim, unified blue.
///   FILL   — bands light far→near, one tick at a time, ramping to amber + a faster pulse.
///   SHOUT  — whole floor flashes green→white on the beat, then eases back to blue.
///
/// Drag the 6 floor materials far→near (mat1 = farthest band). If the fill rolls the wrong way,
/// tick "Reverse Sweep". If the mats aren't spatially ordered, the fill still reads as a clean
/// staged countdown — it just won't be spatially directional.
/// </summary>
public class FloorBeatColorizer : MonoBehaviour
{
    [Header("Floor Materials  (far → near)")]
    public Material mat1;
    public Material mat2;
    public Material mat3;
    public Material mat4;
    public Material mat5;
    public Material mat6;

    [Header("State Colors (HDR)")]
    [ColorUsage(false, true)] public Color waitColor  = new Color(0.10f, 0.30f, 1.00f); // blue — wait
    [ColorUsage(false, true)] public Color readyColor = new Color(1.00f, 0.55f, 0.00f); // amber — filling
    [ColorUsage(false, true)] public Color popColor   = new Color(1.00f, 0.85f, 0.30f); // bright tick pop
    [ColorUsage(false, true)] public Color shoutColor = new Color(0.15f, 1.00f, 0.25f); // green — SHOUT
    [ColorUsage(false, true)] public Color shoutPeak  = new Color(1.00f, 1.00f, 1.00f); // white-hot peak

    [Header("Brightness per state")]
    public float waitIntensity  = 0.35f;  // dim while waiting
    public float readyIntensity = 2.6f;   // a lit (filled) band
    public float popIntensity   = 6.0f;   // the spike right when a band ticks on
    public float shoutIntensity = 11f;    // the green beat flash

    [Header("Countdown Timing")]
    [Tooltip("How many seconds BEFORE the beat the countdown starts. Bigger = more lead-up pointers.")]
    public float anticipationLead = 1.8f;
    [Tooltip("How sharply each band's tick-pop decays (higher = snappier tick).")]
    public float popSharpness = 9f;
    [Tooltip("How fast the green flash decays back to blue.")]
    public float shoutDecayRate = 5.0f;

    [Header("Feel")]
    [Tooltip("Vary the fill MOTION each beat too (far→near / near→far / center-out). Off = always the same far→near sweep (easier to read). Color still changes every beat either way.")]
    public bool  randomizePattern = false;
    [Tooltip("Roll the fill across bands toward the player. Off = all bands fill together.")]
    public bool  useSweep = true;
    [Tooltip("Flip the fill direction.")]
    public bool  reverseSweep = false;
    [Tooltip("Heartbeat pulse on lit bands; speeds up as the beat nears.")]
    public float pulseSpeedMax = 9f;
    public float idleBreatheSpeed = 0.7f;

    [Header("Optional Shader Hooks")]
    [Tooltip("Also push _BeatProgress / _BeatFlash to the materials for the Custom/BeatFloorTile shader. Harmless on Standard materials.")]
    public bool driveShaderProps = true;

    // Vibrant HDR palette for the per-beat fill color (greens left out so they don't clash
    // with the green "GO" flash — that one stays constant for learnability).
    private static readonly Color[] _palette =
    {
        new Color(1.00f, 0.45f, 0.00f), // orange
        new Color(1.00f, 0.10f, 0.45f), // hot pink
        new Color(0.00f, 0.85f, 1.00f), // cyan
        new Color(0.75f, 0.00f, 1.00f), // purple
        new Color(1.00f, 0.20f, 0.10f), // red
        new Color(0.20f, 0.40f, 1.00f), // electric blue
        new Color(1.00f, 0.80f, 0.00f), // gold
        new Color(1.00f, 0.00f, 0.85f), // magenta
    };

    // ── private ──────────────────────────────────────────────────────────────
    private Material[] _mats;
    private float _shoutTimer;
    private float _lastBeatTime = -1f;
    private Color _fillColor;     // re-rolled each beat
    private int   _patternMode;   // 0 far→near, 1 near→far, 2 all-together, 3 center-out

    private void RollNextPattern()
    {
        _fillColor = _palette[Random.Range(0, _palette.Length)];
        // Keep the MOTION consistent (far→near sweep) so players learn one cue; only the
        // color changes each beat. Tick "Randomize Pattern" if you want the motion to vary too.
        _patternMode = randomizePattern ? Random.Range(0, 4) : 0;
    }

    static readonly int ID_Emission = Shader.PropertyToID("_EmissionColor");
    static readonly int ID_Progress = Shader.PropertyToID("_BeatProgress");
    static readonly int ID_Flash    = Shader.PropertyToID("_BeatFlash");

    void Start()
    {
        _mats = new[] { mat1, mat2, mat3, mat4, mat5, mat6 };
        foreach (var m in _mats)
            if (m != null) m.EnableKeyword("_EMISSION");
        RollNextPattern();
    }

    void Update()
    {
        var  mgr         = RhythmRoundManager.Instance;
        bool roundActive = mgr != null && mgr.isRoundActive;
        float t = Time.time;

        // ── Detect the beat → start the green flash ────────────────────────
        if (roundActive)
        {
            float beat = mgr.lastBeatFireTime;
            if (beat > 0f && !Mathf.Approximately(beat, _lastBeatTime))
            {
                _lastBeatTime = beat;
                _shoutTimer   = 1f;
                RollNextPattern(); // fresh color + pattern for the next countdown
            }
        }
        if (_shoutTimer > 0f)
            _shoutTimer = Mathf.Max(0f, _shoutTimer - Time.deltaTime * shoutDecayRate);

        // ── Seconds until the next beat ────────────────────────────────────
        float toBeat = 999f;
        if (roundActive)
        {
            float now  = mgr.GetCurrentTrackTime();
            float next = mgr.GetNextBeatTime();
            if (next > 0f) toBeat = next - now;
        }
        float lead = Mathf.Max(0.2f, anticipationLead);
        float progress = (toBeat >= 0f && toBeat <= lead) ? 1f - (toBeat / lead) : 0f; // 0 far → 1 at beat

        // ── Drive every band ───────────────────────────────────────────────
        for (int i = 0; i < 6; i++)
        {
            if (_mats[i] == null) continue;

            Color col;
            float intensity;

            if (_shoutTimer > 0f)
            {
                // SHOUT — unified green→white flash.
                float f = _shoutTimer;
                col       = Color.Lerp(shoutColor, shoutPeak, f * 0.6f);
                intensity = Mathf.Lerp(waitIntensity, shoutIntensity, f);
            }
            else if (progress > 0f && roundActive)
            {
                // FILL — staged countdown. Pattern + color are re-rolled each beat.
                int si = reverseSweep ? (5 - i) : i;
                float fillFrac;
                switch (_patternMode)
                {
                    case 1:  fillFrac = useSweep ? (5 - si) / 6f : 0f; break;                 // near → far
                    case 2:  fillFrac = 0f; break;                                            // all together
                    case 3:  fillFrac = useSweep ? (Mathf.Abs(si - 2.5f) - 0.5f) / 3f : 0f; break; // center → out
                    default: fillFrac = useSweep ? si / 6f : 0f; break;                       // far → near
                }
                float fillTime  = lead * (1f - fillFrac);   // seconds-before-beat this band lights
                float secsSince = fillTime - toBeat;        // >=0 once lit

                if (secsSince < 0f)
                {
                    // Not lit yet — dim blue, gentle breath.
                    float breath = 0.5f + 0.5f * Mathf.Sin(t * idleBreatheSpeed * Mathf.PI * 2f + i * 0.6f);
                    col       = waitColor;
                    intensity = Mathf.Lerp(waitIntensity * 0.6f, waitIntensity, breath);
                }
                else
                {
                    // Lit — this beat's fill color, brightening with progress, + a sharp tick pop,
                    // + an accelerating heartbeat pulse.
                    float pop   = Mathf.Exp(-secsSince * popSharpness);
                    float pulse = 0.75f + 0.25f * Mathf.Sin(t * Mathf.Lerp(3f, pulseSpeedMax, progress) * Mathf.PI);
                    col       = Color.Lerp(_fillColor, popColor, pop);
                    intensity = Mathf.Lerp(readyIntensity * 0.8f, readyIntensity, progress) * pulse
                              + popIntensity * pop;
                }
            }
            else
            {
                // WAIT — calm dim blue.
                float breath = 0.5f + 0.5f * Mathf.Sin(t * idleBreatheSpeed * Mathf.PI * 2f + i * 0.6f);
                col       = waitColor;
                intensity = Mathf.Lerp(waitIntensity * 0.6f, waitIntensity, breath);
            }

            _mats[i].SetColor(ID_Emission, col * intensity);

            // Optional: feed the custom shader (ignored by Standard materials).
            if (driveShaderProps)
            {
                _mats[i].SetFloat(ID_Progress, progress);
                _mats[i].SetFloat(ID_Flash, _shoutTimer);
            }
        }
    }
}
