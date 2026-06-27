using System.Collections.Generic;
using UnityEngine;

/// World-space 3D SCORE display floating above each fighter (you + the bot), replacing the old health
/// percentage. Built entirely in code from Unity's 3D TextMesh (a real mesh in the world, so it has
/// genuine perspective/depth — no TextMeshPro package needed), with a dark drop-shadow copy behind it
/// for extra pop. When a fighter's score changes, the number ANIMATES from old→new over ~1.5s with a
/// running count-up, a little pop-scale, and a low-key blip that ticks while it climbs.
///
/// Self-bootstraps (like the other HUDs) — nothing to place in a scene. PlayerCombat.OnScoreChanged
/// pushes updates here via OnScoreUpdated().
[DefaultExecutionOrder(10005)]
public class ScoreHud : MonoBehaviour
{
    public static ScoreHud Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        var go = new GameObject("~ScoreHud");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<ScoreHud>();
    }

    private const float COUNT_TIME    = 1.5f;   // seconds for the number to run up to its new value
    private const float HEIGHT        = 2.6f;   // metres above the bot's feet
    private const float CHAR_SIZE     = 0.17f;  // TextMesh char size (world scale) — readable but not huge
    private const int   FONT_SIZE     = 90;     // crisp glyphs, scaled down by CHAR_SIZE
    private const float POP_SCALE     = 1.30f;  // how much the label pops when a new chunk lands
    private const float TICK_PERIOD   = 0.06f;  // seconds between count-up blips while climbing

    // Both scores sit by the BOT (so you can actually see your own — it's not stuck on your head):
    //   • pushed BACK behind the bot, up at HEIGHT
    //   • YOUR score offset to one side, the BOT's to the other
    private const float BEHIND_BOT    = 5.5f;   // metres further from the player, behind the bot (pushed way back)
    private const float SIDE_OFFSET   = 3.4f;   // metres left/right of the bot for each score column (wide so big numbers never collide)

    private static readonly Color SelfColor = new Color(0.25f, 0.95f, 1f);   // cyan = you
    private static readonly Color OppColor  = new Color(1f, 0.4f, 0.4f);     // red  = the bot
    private static readonly Color Shadow    = new Color(0f, 0f, 0f, 0.85f);

    private class Tag
    {
        public PlayerCombat owner;
        public bool  isSelf;      // the local player's score (vs the bot's)
        public Transform root;
        public TextMesh face;     // bright front
        public TextMesh shadow;   // dark copy behind, offset for depth
        public int   displayed;   // the number currently shown
        public int   from, to;    // animation endpoints
        public float t;           // 0..1 animation progress
        public bool  animating;
        public float nextTick;    // next count-up blip time
        public Vector3 baseScale;
    }

    private readonly Dictionary<PlayerCombat, Tag> _tags = new Dictionary<PlayerCombat, Tag>();
    private Font _font;
    private AudioSource _audio;
    private AudioClip _blip;

    private void Awake()
    {
        _font  = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
              ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
        _audio = gameObject.AddComponent<AudioSource>();
        _audio.playOnAwake = false;
        _audio.spatialBlend = 0f; // 2D, it's a UI sound
        _audio.volume = 0.35f;    // low-key
        _blip = MakeBlip();
    }

    /// Called from PlayerCombat.OnScoreChanged on every client. Kicks off the count-up animation.
    public void OnScoreUpdated(PlayerCombat pc, int oldVal, int newVal)
    {
        if (pc == null) return;
        var tag = Get(pc);
        if (tag == null) return;

        // Animate from whatever is currently shown (so rapid-fire gains chain smoothly) up to newVal.
        tag.from = tag.displayed;
        tag.to   = newVal;
        tag.t    = 0f;
        tag.animating = tag.from != tag.to;
        tag.nextTick = 0f;
    }

    // LateUpdate (not Update): run AFTER the bot's pin and the camera have finalized their positions for
    // the frame, so the scoreboards read settled values and don't vibrate against them.
    private void LateUpdate()
    {
        // BOTH scoreboards live by the BOT (the local player's opponent), so you can actually read
        // your own — it's not stuck on your own head. They sit BACK behind the bot, up at HEIGHT,
        // YOUR score offset to one side and the BOT's to the other, all facing you.
        //
        // IMPORTANT: anchor on the bot's fixed SPAWN/home point, NOT its live transform. During a beat
        // run-in the bot's transform charges toward the player and back every beat; anchoring to it made
        // the scoreboards lurch/vibrate up-and-down with that motion. The home point is rock-steady.
        Vector3 anchorPos = BotAnchorPos(out bool haveAnchor);
        Camera cam = CachedCamera.Main;
        if (!haveAnchor) return;

        // Build the left/right + back basis from the player→bot direction (flattened).
        Vector3 fromPlayer = cam != null ? (anchorPos - cam.transform.position) : Vector3.forward;
        fromPlayer.y = 0f;
        if (fromPlayer.sqrMagnitude < 0.0001f) fromPlayer = Vector3.forward;
        fromPlayer.Normalize();
        Vector3 rightAxis = Vector3.Cross(Vector3.up, fromPlayer); // player's left/right across the bot

        Vector3 basePos = anchorPos + Vector3.up * HEIGHT + fromPlayer * BEHIND_BOT;

        foreach (var pc in FindFighters())
        {
            var tag = Get(pc);
            if (tag == null) continue;

            // YOU on the player's LEFT of the bot, BOT on the player's RIGHT (so they don't overlap).
            float side = tag.isSelf ? -SIDE_OFFSET : SIDE_OFFSET;
            tag.root.position = basePos + rightAxis * side;
            if (cam != null)
                tag.root.rotation = Quaternion.LookRotation(tag.root.position - cam.transform.position);

            AnimateTag(tag);
        }
    }

    // The bot = the local player's opponent. We cluster both scoreboards around the bot's FIXED spawn
    // (HomePosition), so they don't ride the bot's beat run-in motion. Falls back to the live transform
    // only if home hasn't been set yet.
    private Vector3 BotAnchorPos(out bool found)
    {
        found = false;
        foreach (var p in GameManager.players)
        {
            if (p == null || !p.isLocalPlayer) continue;
            var opp = p.GetOpponent();
            if (opp == null) return Vector3.zero;
            found = true;
            Vector3 home = opp.HomePosition;
            return home != Vector3.zero ? home : opp.transform.position;
        }
        return Vector3.zero;
    }

    private void AnimateTag(Tag tag)
    {
        if (tag.animating)
        {
            tag.t += Time.deltaTime / COUNT_TIME;
            float e = EaseOutCubic(Mathf.Clamp01(tag.t));
            tag.displayed = Mathf.RoundToInt(Mathf.Lerp(tag.from, tag.to, e));

            // Low-key blip ticking up while the number climbs.
            if (Time.time >= tag.nextTick)
            {
                tag.nextTick = Time.time + TICK_PERIOD;
                if (_audio != null && _blip != null) _audio.PlayOneShot(_blip, 0.35f);
            }

            // Pop scale that settles back to base as it finishes.
            float pop = 1f + (POP_SCALE - 1f) * (1f - e);
            tag.root.localScale = tag.baseScale * pop;

            if (tag.t >= 1f)
            {
                tag.displayed = tag.to;
                tag.animating = false;
                tag.root.localScale = tag.baseScale;
            }
            SetText(tag, tag.displayed);
        }
    }

    private void SetText(Tag tag, int value)
    {
        string s = value.ToString("N0");
        tag.face.text   = s;
        tag.shadow.text = s;
    }

    private Tag Get(PlayerCombat pc)
    {
        if (_tags.TryGetValue(pc, out var existing) && existing.root != null) return existing;

        bool isSelf = pc.isLocalPlayer;
        Color col = isSelf ? SelfColor : OppColor;

        var root = new GameObject($"Score_{pc.name}").transform;
        root.SetParent(transform, false);

        // Shadow copy slightly behind + down-right for a chunky 3D drop.
        var shadow = MakeText(root, Shadow, 1f);
        shadow.transform.localPosition = new Vector3(0.06f, -0.06f, 0.08f);
        // Bright face in front.
        var face = MakeText(root, col, 1f);
        face.transform.localPosition = Vector3.zero;

        var tag = new Tag
        {
            owner = pc, isSelf = isSelf, root = root, face = face, shadow = shadow,
            displayed = pc.Score, from = pc.Score, to = pc.Score,
            baseScale = Vector3.one
        };
        SetText(tag, pc.Score);
        _tags[pc] = tag;
        return tag;
    }

    private TextMesh MakeText(Transform parent, Color col, float sizeMult)
    {
        var go = new GameObject("Txt");
        go.transform.SetParent(parent, false);
        var tm = go.AddComponent<TextMesh>();
        tm.font          = _font;
        tm.fontSize      = FONT_SIZE;
        tm.characterSize = CHAR_SIZE * sizeMult;
        tm.fontStyle     = FontStyle.Bold;
        tm.anchor        = TextAnchor.MiddleCenter;
        tm.alignment     = TextAlignment.Center;
        tm.color         = col;
        // Use the font's material so the glyphs actually render.
        var mr = go.GetComponent<MeshRenderer>();
        if (mr != null && _font != null) mr.sharedMaterial = _font.material;
        return tm;
    }

    // Find the active fighters (local player + opponent/bot) without holding stale refs.
    private IEnumerable<PlayerCombat> FindFighters()
    {
        foreach (var p in GameManager.players)
        {
            if (p == null) continue;
            var pc = p.GetComponent<PlayerCombat>();
            if (pc != null) yield return pc;
        }
    }

    private static float EaseOutCubic(float x) => 1f - Mathf.Pow(1f - x, 3f);

    // A short, soft sine "blip" generated in code so there's no asset to wire up.
    private AudioClip MakeBlip()
    {
        int rate = 44100;
        int len  = rate / 20; // 50 ms
        var data = new float[len];
        float freq = 880f;
        for (int i = 0; i < len; i++)
        {
            float t = (float)i / rate;
            float env = Mathf.Exp(-18f * t);          // fast decay = a soft tick
            data[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * env * 0.5f;
        }
        var clip = AudioClip.Create("ScoreBlip", len, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
