using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Beat-approach floor (Beat Saber style). A bright head travels from the FURTHEST tile toward
/// the player and arrives at the CLOSEST tile exactly ON the beat — a clear visual countdown of
/// when to shout. EACH beat the wave (and the glowing wake it leaves) is a NEW colour, so the
/// tiles it passes recolour every time. The closest tile flashes on the beat.
///
/// The travel time = the real gap between beats, so the wave naturally slows down when the music
/// is slow and speeds up when it's fast — always tied to an actual beat (never a free-running loop).
///
/// Drag the floor TILE objects (Renderers) in order CLOSEST → FURTHEST:
///   element 0  = tile 1  (closest — player stands just past its edge)
///   element 12 = tile 13 (furthest)
/// Each tile's material is INSTANCED at runtime, so tiles that share a colour still light
/// independently (and the shared material assets are never edited).
/// </summary>
public class FloorBeatColorizer : MonoBehaviour
{
    [Header("Floor tiles  (element 0 = CLOSEST / tile 1  →  last = FURTHEST / tile 13)")]
    public Renderer[] floorTiles;

    [Header("Base Colors (HDR)")]
    [ColorUsage(false, true)] public Color idleColor = new Color(0f, 0f, 0f); // BLACK at rest (no glow)
    [ColorUsage(false, true)] public Color hitColor  = new Color(0.15f, 1.00f, 0.25f); // green "SHOUT" flash
    [ColorUsage(false, true)] public Color hitPeak   = new Color(1.00f, 1.00f, 1.00f); // white-hot peak

    [Header("Wave palette — a NEW colour is picked each beat for the head + its trail")]
    [ColorUsage(false, true)] public Color[] waveColors = new[]
    {
        new Color(0.00f, 0.85f, 1.00f), // cyan
        new Color(1.00f, 0.10f, 0.55f), // hot pink
        new Color(1.00f, 0.55f, 0.00f), // orange
        new Color(0.65f, 0.00f, 1.00f), // purple
        new Color(0.20f, 0.45f, 1.00f), // electric blue
        new Color(1.00f, 0.80f, 0.00f), // gold
        new Color(1.00f, 0.00f, 0.85f), // magenta
        new Color(0.10f, 1.00f, 0.70f), // teal
        new Color(1.00f, 0.20f, 0.10f), // red
    };

    [Header("Brightness")]
    public float idleIntensity   = 0f;
    public float bandIntensity   = 5.0f;
    [Tooltip("Glow of the tiles the wave has already passed (they stay lit until the beat).")]
    public float filledIntensity = 3.5f;
    public float hitIntensity    = 12f;

    [Tooltip("How much to dim the floor during the drone-rush segment (1 = normal, 0.4 = much darker). " +
             "Only applies while the drone segment is active; the floor is full brightness otherwise.")]
    public float droneSegmentDim = 0.4f;

    [Header("Timing / Feel")]
    [Tooltip("The wave spans the real gap between beats, up to this many seconds. Slow songs → " +
             "the wave starts at most this early (so it never crawls forever on very slow beats).")]
    public float maxApproachTime = 2.5f;
    [Tooltip("How many tiles wide the bright head is. Smaller = a sharper, more defined 'note'.")]
    public float bandWidth = 1.8f;
    [Tooltip("The head brightens as it nears the player. 1 = no ramp.")]
    public float approachUrgency = 1.4f;
    public float hitDecayRate = 5f;
    public float idleBreatheSpeed = 0.6f;

    private Material[] _mats;
    private float _hitTimer;
    private float _lastBeatTime = -1f;
    private float _beatInterval;   // learned gap between beats (track time)
    private float _prevNextBeat;   // previous GetNextBeatTime, to detect a beat passing
    private Color _waveColor = Color.cyan; // this wave's colour (re-rolled each beat)
    private int   _waveIdx;

    static readonly int ID_Emission = Shader.PropertyToID("_EmissionColor");

    void Start()
    {
        _beatInterval = maxApproachTime;
        if (waveColors != null && waveColors.Length > 0) _waveColor = waveColors[0];

        var list = new List<Material>();
        if (floorTiles != null)
            foreach (var r in floorTiles)
            {
                if (r == null) { list.Add(null); continue; }
                r.material.EnableKeyword("_EMISSION"); // r.material auto-instances (per-tile control)
                list.Add(r.material);
            }
        _mats = list.ToArray();

        Debug.Log($"[FloorBeat] Wired {_mats.Length} tiles. " +
                  $"RhythmRoundManager={(RhythmRoundManager.Instance != null ? "found" : "NULL")}. " +
                  (_mats.Length == 0 ? "<<< NO TILES ASSIGNED — drag your floor tiles into 'Floor Tiles'." : ""));
    }

    private void RollWaveColor()
    {
        if (waveColors == null || waveColors.Length == 0) return;
        int next = Random.Range(0, waveColors.Length);
        if (waveColors.Length > 1 && next == _waveIdx) next = (next + 1) % waveColors.Length; // avoid repeat
        _waveIdx   = next;
        _waveColor = waveColors[next];
    }

    void Update()
    {
        if (_mats == null || _mats.Length == 0) return;
        int n = _mats.Length;

        var  mgr         = RhythmRoundManager.Instance;
        bool roundActive = mgr != null && mgr.isRoundActive;
        float t = Time.time;

        // ── Beat landed → flash the closest tile + pick the NEXT wave's colour ──
        if (roundActive)
        {
            float beat = mgr.lastBeatFireTime;
            if (beat > 0f && !Mathf.Approximately(beat, _lastBeatTime))
            {
                _lastBeatTime = beat;
                _hitTimer     = 1f;
                RollWaveColor(); // the wave now starting its approach gets a fresh colour
            }
        }
        if (_hitTimer > 0f)
            _hitTimer = Mathf.Max(0f, _hitTimer - Time.deltaTime * hitDecayRate);

        // ── Time to next beat + learn the beat interval (so the wave matches tempo) ──
        float toBeat = 999f;
        float lead   = maxApproachTime;
        if (roundActive)
        {
            float now  = mgr.GetCurrentTrackTime();
            float next = mgr.GetNextBeatTime();
            if (next > 0f)
            {
                if (_prevNextBeat > 0f && next > _prevNextBeat + 0.02f)
                {
                    float iv = next - _prevNextBeat;
                    if (iv > 0.1f && iv < 8f) _beatInterval = iv;
                }
                _prevNextBeat = next;
                toBeat = next - now;
            }
            lead = Mathf.Clamp(_beatInterval, 0.25f, maxApproachTime);
        }

        // head tile (float): FURTHEST (n-1) a full 'lead' away → CLOSEST (0) as the beat lands.
        bool  bandActive = roundActive && toBeat >= 0f && toBeat <= lead;
        float head     = bandActive ? (toBeat / lead) * (n - 1) : -999f;
        float progress = bandActive ? 1f - (toBeat / lead) : 0f; // 0 far → 1 at beat

        // Head gets a hot white core as it nears the player; trail is the plain wave colour.
        Color headCol = Color.Lerp(_waveColor, hitPeak, progress * 0.30f);

        // Dim the floor during the drone-rush segment so its glow doesn't overpower the drones; full
        // brightness any other time.
        float dim = PCDroneSegment.AnySegmentActive ? droneSegmentDim : 1f;

        // ── Light every tile ───────────────────────────────────────────────
        for (int i = 0; i < n; i++)
        {
            if (_mats[i] == null) continue;

            // Idle: calm dim breathing blue.
            float breath    = 0.5f + 0.5f * Mathf.Sin(t * idleBreatheSpeed * Mathf.PI * 2f + i * 0.5f);
            Color col       = idleColor;
            float intensity = Mathf.Lerp(idleIntensity * 0.6f, idleIntensity, breath);

            if (bandActive)
            {
                // Every tile the head has passed stays FULLY lit in the wave colour, building up
                // behind the wave. It all clears on the beat (the next wave restarts from the far
                // end, so nothing counts as "passed" anymore). Brightens slightly as the beat nears.
                if (i > head)
                {
                    float buildup = Mathf.Lerp(0.85f, 1.25f, progress);
                    col       = _waveColor;
                    intensity = Mathf.Max(intensity, filledIntensity * buildup);
                }

                // The bright head itself.
                float d    = Mathf.Abs(i - head);
                float band = Mathf.Clamp01(1f - d / Mathf.Max(0.01f, bandWidth));
                band *= band; // sharpen into a comet-like head
                if (band > 0f)
                {
                    float urgency = Mathf.Lerp(1f, approachUrgency, progress);
                    col       = Color.Lerp(col, headCol, band);
                    intensity = Mathf.Max(intensity, Mathf.Lerp(idleIntensity, bandIntensity * urgency, band));
                }
            }

            // Beat hit: the closest tile flashes green→white, with a small ripple onto tile 2.
            if (_hitTimer > 0f && i <= 1)
            {
                float f = _hitTimer * (i == 0 ? 1f : 0.45f);
                col       = Color.Lerp(col, Color.Lerp(hitColor, hitPeak, f * 0.6f), f);
                intensity = Mathf.Max(intensity, Mathf.Lerp(idleIntensity, hitIntensity, f));
            }

            _mats[i].SetColor(ID_Emission, col * (intensity * dim));
        }
    }

    void OnDestroy()
    {
        if (_mats != null)
            foreach (var m in _mats)
                if (m != null) Destroy(m);
    }
}
