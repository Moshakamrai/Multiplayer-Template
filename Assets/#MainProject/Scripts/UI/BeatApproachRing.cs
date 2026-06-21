using UnityEngine;

/// New-player beat cue: a glowing ring that SHRINKS in toward the opponent and snaps to its tight
/// "hit now" size exactly on the beat (Osu / Beat Saber approach-circle style). Sits around the
/// opponent so the player's eyes are already where the action is.
///
/// Self-bootstraps (like VRWorldHud/VRMenus) — nothing to place in a scene. Works in VR and flat.
/// Draws two LineRenderer circles: a fixed inner "target" ring and a moving approach ring that
/// collapses onto it on the beat, then flashes. Pure code, no prefab/material setup needed.
[DefaultExecutionOrder(10003)]
public class BeatApproachRing : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        var go = new GameObject("~BeatApproachRing");
        DontDestroyOnLoad(go);
        go.AddComponent<BeatApproachRing>();
    }

    // Tunables
    private const int   SEGMENTS      = 48;
    private const float TARGET_RADIUS = 0.55f;  // the fixed inner ring (the "hit on the beat" size)
    private const float START_RADIUS  = 1.45f;  // approach ring starts this big and shrinks in
    // Center height above the opponent's feet. Must be >= START_RADIUS so the bottom of the widest
    // ring never dips below the floor (was 1.3 with a 2.6 radius → ~1.3m of ring buried in the ground).
    private const float HEIGHT        = 1.55f;
    private const float APPROACH_LEAD = 1.2f;   // seconds before the beat the ring starts closing in
    private const float LINE_WIDTH    = 0.045f;

    private static readonly Color ApproachColor = new Color(0.2f, 0.9f, 1f);   // cyan, closing in
    private static readonly Color TargetColor   = new Color(1f, 1f, 1f, 0.55f);// faint white target
    private static readonly Color FlashColor    = new Color(0.3f, 1f, 0.45f);  // green "hit!" flash

    private LineRenderer _approach, _target;
    private Material _mat;
    private float _lastBeat = -1f;
    private float _flash;     // 0..1 on-beat flash, decays
    private bool _built;

    private void Update()
    {
        var rmm = RhythmRoundManager.Instance;
        bool active = rmm != null && rmm.isRoundActive;

        // Only show during an active round, once we can find the opponent to wrap — AND only while the
        // beat coach says the player still needs help. Once they're hitting the beat consistently the
        // ring disappears; it returns if they start missing again (see BeatCoach).
        Transform opp = LocalOpponent();
        if (!active || opp == null || !BeatCoach.CoachingVisible)
        {
            if (_built) { _approach.enabled = false; _target.enabled = false; }
            return;
        }

        if (!_built) Build();
        _approach.enabled = true; _target.enabled = true;

        // Park both rings on the opponent at chest height.
        Vector3 center = opp.position + Vector3.up * HEIGHT;

        // Time to the next beat → how far the approach ring has closed in (1 = wide, 0 = on the beat).
        float toBeat = rmm.GetNextBeatTime() - rmm.GetCurrentTrackTime();
        float t = (toBeat >= 0f && toBeat <= APPROACH_LEAD && APPROACH_LEAD > 0.001f)
            ? toBeat / APPROACH_LEAD : 1f;
        float radius = Mathf.Lerp(TARGET_RADIUS, START_RADIUS, Mathf.Clamp01(t));

        // On-beat flash: when a new beat fires, pop the flash.
        float beat = rmm.lastBeatFireTime;
        if (beat > 0f && !Mathf.Approximately(beat, _lastBeat)) { _lastBeat = beat; _flash = 1f; }
        if (_flash > 0f) _flash = Mathf.Max(0f, _flash - Time.deltaTime * 4f);

        // The closer to the beat, the brighter the approach ring (urgency) + the on-beat green flash.
        float closeness = 1f - Mathf.Clamp01(t);
        Color appCol = Color.Lerp(ApproachColor, FlashColor, _flash) * (1f + closeness * 1.5f + _flash * 2f);

        // Vertical ring that faces the player from a FIXED orientation. Use the spawn-to-spawn axis
        // instead of the camera's right so it doesn't spin as the bot/camera move.
        Vector3 right = FixedRightAxis();
        Vector3 up = Vector3.up;

        DrawCircle(_approach, center, radius, appCol, right, up);
        DrawCircle(_target,   center, TARGET_RADIUS, Color.Lerp(TargetColor, FlashColor, _flash), right, up);
    }

    // Stable horizontal axis perpendicular to the bot-to-player spawn direction.
    private Vector3 FixedRightAxis()
    {
        var rmm = RhythmRoundManager.Instance;
        if (rmm != null && rmm.playerStartPosition != null && rmm.botStartPosition != null)
        {
            Vector3 toPlayer = rmm.playerStartPosition.position - rmm.botStartPosition.position;
            toPlayer.y = 0f;
            if (toPlayer.sqrMagnitude > 0.0001f)
            {
                Vector3 right = Vector3.Cross(Vector3.up, toPlayer.normalized);
                if (right.sqrMagnitude > 0.0001f) return right.normalized;
            }
        }
        return Vector3.right;
    }

    private void Build()
    {
        _mat = new Material(Shader.Find("Sprites/Default")); // unlit, vertex-colored, always visible
        _approach = MakeRing("ApproachRing");
        _target   = MakeRing("TargetRing");
        _built = true;
    }

    private LineRenderer MakeRing(string n)
    {
        var go = new GameObject(n);
        go.transform.SetParent(transform, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.loop = true;
        lr.positionCount = SEGMENTS;
        lr.widthMultiplier = LINE_WIDTH;
        lr.material = _mat;
        lr.numCapVertices = 2;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.alignment = LineAlignment.View; // face the camera so it reads as a flat ring from any angle
        return lr;
    }

    // Draw a circle of `radius` around `center` in the plane spanned by `right` × `up`, tinted `col`.
    // Passing (camera-right, world-up) makes a VERTICAL ring standing upright toward the player.
    private void DrawCircle(LineRenderer lr, Vector3 center, float radius, Color col, Vector3 right, Vector3 up)
    {
        for (int i = 0; i < SEGMENTS; i++)
        {
            float a = (float)i / SEGMENTS * Mathf.PI * 2f;
            lr.SetPosition(i, center + (right * Mathf.Cos(a) + up * Mathf.Sin(a)) * radius);
        }
        lr.startColor = lr.endColor = col;
    }

    // The opponent of the LOCAL player (the bot, in solo play) — that's who we wrap the ring around.
    private Transform LocalOpponent()
    {
        foreach (var p in GameManager.players)
        {
            if (p == null) continue;
            if (p.isLocalPlayer)
            {
                var o = p.GetOpponent();
                return o != null ? o.transform : null;
            }
        }
        return null;
    }

    private void OnDestroy()
    {
        if (_mat != null) Destroy(_mat);
    }
}
