using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;
using System.Collections.Generic;

/// World-space combat HUD for VR. Screen-space IMGUI/uGUI can't render in stereo, so this
/// rebuilds the essentials — both health bars and the EXCELLENT/GOOD/BAD timing feedback —
/// as a real 3D panel that gently follows your gaze. It also promotes the player's existing
/// CARD canvas to world-space so the hand is visible in-headset.
///
/// Self-bootstraps; only runs while an XR headset is active. Zero effect on flat builds.
[DefaultExecutionOrder(10002)] // after VRCameraDriver(10000)/VRHands(10001): camera pose is final
public class VRWorldHud : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        var go = new GameObject("~VRWorldHud");
        DontDestroyOnLoad(go);
        go.AddComponent<VRWorldHud>();
    }

    // Tunables
    private const float HUD_DISTANCE = 2.0f;   // metres in front of the eyes
    private const float HUD_HEIGHT   = -0.15f; // slight drop so it sits below eye-line
    private const float FOLLOW_LERP  = 6f;     // higher = snappier gaze follow

    private Transform _root;          // gaze-following anchor
    private Canvas _canvas;
    private Image _selfFill, _oppFill;
    private Text  _selfLabel, _oppLabel, _timingLabel;
    private Font  _font;

    private Canvas _cardCanvas;       // the player's hand, promoted to world-space
    private float _cardRetry;         // periodic re-search until the hand canvas exists
    private Canvas _powerCanvas;      // the power-meter cone, promoted to world-space
    private PowerMeterReactor _powerReactor;
    private bool _powerSharesCard;    // true if the cone lives on the same canvas as the hand
    private float _powerRetry;
    private bool _built;

    private void Update()
    {
        if (!XRSettings.isDeviceActive) return;
        Camera cam = Camera.main;
        if (cam == null) return;

        if (!_built) { Build(); _built = true; }

        FollowGaze(cam);

        // Keep looking for the hand canvas (it spawns with the player, after the HUD) until found.
        // It stays parented to the player (so it dies with the player); we just dock it each frame.
        if (_cardCanvas == null)
        {
            _cardRetry -= Time.unscaledDeltaTime;
            if (_cardRetry <= 0f) { _cardRetry = 1f; PromoteCardCanvas(cam); }
        }
        else
        {
            DockCardCanvas();
        }

        // Same treatment for the power-meter cone (an Overlay canvas otherwise hidden in VR).
        if (_powerCanvas == null)
        {
            _powerRetry -= Time.unscaledDeltaTime;
            if (_powerRetry <= 0f) { _powerRetry = 1f; PromotePowerCanvas(cam); }
        }
        else
        {
            DockPowerCanvas();
        }

        UpdateHealth();
        UpdateTiming();
    }

    // --- Gaze-following: ease the HUD toward a pose in front of the camera ---
    private void FollowGaze(Camera cam)
    {
        Vector3 fwd = cam.transform.forward;
        Vector3 targetPos = cam.transform.position + fwd * HUD_DISTANCE + Vector3.up * HUD_HEIGHT;
        // Canvas +Z points away from the camera so the text faces the player.
        Quaternion targetRot = Quaternion.LookRotation(targetPos - cam.transform.position);

        float k = 1f - Mathf.Exp(-FOLLOW_LERP * Time.unscaledDeltaTime);
        _root.position = Vector3.Lerp(_root.position, targetPos, k);
        _root.rotation = Quaternion.Slerp(_root.rotation, targetRot, k);
    }

    private void UpdateHealth()
    {
        GetCombatants(out PlayerCombat self, out PlayerCombat opp);
        SetBar(_selfFill, _selfLabel, "YOU", self);
        SetBar(_oppFill,  _oppLabel,  "BOT", opp);
    }

    private const float BAR_FULL_W = 512f;

    private void SetBar(Image fill, Text label, string who, PlayerCombat pc)
    {
        if (pc == null) { if (label) label.text = $"{who}  --"; return; }
        float pct = Mathf.Clamp01(pc.CurrentPercentage / 100f); // Smash-style damage %
        var rt = (RectTransform)fill.transform;
        rt.sizeDelta = new Vector2(BAR_FULL_W * pct, rt.sizeDelta.y);
        fill.color = SeverityColor(pc.CurrentPercentage);
        if (label) label.text = $"{who}  {pc.CurrentPercentage:F0}%";
    }

    private void UpdateTiming()
    {
        float age = Time.time - PlayerCombat.VRTimingTime;
        if (age > 1.2f) { _timingLabel.text = ""; return; }
        float alpha = 1f - Mathf.Clamp01(age / 1.2f);
        _timingLabel.text = PlayerCombat.VRTimingText;
        var c = PlayerCombat.VRTimingColor;
        _timingLabel.color = new Color(c.r, c.g, c.b, alpha);
    }

    // ── Build the world-space canvas + widgets in code ──────────────────────────────────
    private void Build()
    {
        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
             ?? Resources.GetBuiltinResource<Font>("Arial.ttf");

        _root = new GameObject("HudRoot").transform;
        _root.SetParent(transform, false);

        var canvasGo = new GameObject("HudCanvas");
        canvasGo.transform.SetParent(_root, false);
        _canvas = canvasGo.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.WorldSpace;
        canvasGo.AddComponent<GraphicRaycaster>();

        var crt = (RectTransform)canvasGo.transform;
        crt.sizeDelta = new Vector2(1200, 900);
        crt.localScale = Vector3.one * 0.001f; // 1200px -> 1.2m wide
        crt.localPosition = Vector3.zero;

        // Health bars: YOU (left), BOT (right), pushed high up for visibility.
        BuildBar(crt, new Vector2(-300, 570), out _selfFill, out _selfLabel);
        BuildBar(crt, new Vector2( 300, 570), out _oppFill,  out _oppLabel);

        // Timing feedback text, centred (a bit above eye-line).
        _timingLabel = MakeText(crt, "", 80, new Vector2(0, 120), new Vector2(900, 140));
        _timingLabel.fontStyle = FontStyle.Bold;
    }

    private void BuildBar(RectTransform parent, Vector2 pos, out Image fill, out Text label)
    {
        bool isSelf = pos.x < 0; // left bar = self, right bar = opponent
        Color borderCol = isSelf ? new Color(0.15f, 0.8f, 1f, 0.7f) : new Color(1f, 0.2f, 0.2f, 0.7f);

        // Background with colored border tint
        var bg = MakeImage(parent, new Color(0.04f, 0.04f, 0.06f, 0.9f), pos, new Vector2(520, 64));

        // Colored border lines
        var topBorder = MakeImage((RectTransform)bg.transform, borderCol, new Vector2(0, 30), new Vector2(520, 3));
        var botBorder = MakeImage((RectTransform)bg.transform, borderCol, new Vector2(0, -30), new Vector2(520, 3));
        var leftBorder = MakeImage((RectTransform)bg.transform, borderCol, new Vector2(-258, 0), new Vector2(3, 64));
        var rightBorder = MakeImage((RectTransform)bg.transform, borderCol, new Vector2(258, 0), new Vector2(3, 64));

        // Fill anchored to the LEFT edge so growing its width fills rightward.
        fill = MakeImage((RectTransform)bg.transform, Color.green, Vector2.zero, new Vector2(0, 52));
        var frt = (RectTransform)fill.transform;
        frt.anchorMin = new Vector2(0f, 0.5f);
        frt.anchorMax = new Vector2(0f, 0.5f);
        frt.pivot     = new Vector2(0f, 0.5f);
        frt.anchoredPosition = new Vector2(-BAR_FULL_W * 0.5f + 4f, 0f); // start at left inner edge

        label = MakeText((RectTransform)bg.transform, "", 28, Vector2.zero, new Vector2(500, 64));
        label.fontStyle = FontStyle.Bold;
        label.color = isSelf ? new Color(0.2f, 0.9f, 1f) : new Color(1f, 0.4f, 0.4f);
    }

    private Image MakeImage(RectTransform parent, Color color, Vector2 pos, Vector2 size)
    {
        var go = new GameObject("Img", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        var img = go.AddComponent<Image>();
        img.color = color;
        return img;
    }

    private Text MakeText(RectTransform parent, string content, int size, Vector2 pos, Vector2 dim)
    {
        var go = new GameObject("Txt", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchoredPosition = pos;
        rt.sizeDelta = dim;
        var t = go.AddComponent<Text>();
        t.font = _font;
        t.text = content;
        t.fontSize = size;
        t.alignment = TextAnchor.MiddleCenter;
        t.color = Color.white;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }

    // ── Card canvas: find the player's hand canvas and dock it on the HUD in world space ──
    private void PromoteCardCanvas(Camera cam)
    {
        if (_cardCanvas != null) return;

        foreach (var c in FindObjectsOfType<Canvas>(true))
        {
            if (c == _canvas || c.renderMode == RenderMode.WorldSpace) continue;
            string n = c.gameObject.name.ToLowerInvariant();
            if (n.Contains("card") || n.Contains("hand") || n.Contains("playercanvas"))
            {
                _cardCanvas = c;
                c.renderMode = RenderMode.WorldSpace; // suppressor only hides NON-world-space canvases
                c.worldCamera = cam;
                c.enabled = true;                     // re-enable if the suppressor already hid it

                // Sharpen: world-space canvases render mushy by default. Crank the dynamic
                // pixel density so text/graphics are rasterised at much higher resolution.
                var scaler = c.GetComponent<CanvasScaler>();
                if (scaler == null) scaler = c.gameObject.AddComponent<CanvasScaler>();
                scaler.dynamicPixelsPerUnit = 4f;     // 1 = default/blurry; 4 = crisp in VR
                scaler.referencePixelsPerUnit = 100f;
                c.referencePixelsPerUnit = 100f;

                Debug.Log($"[VRWorldHud] Promoted card canvas '{c.name}' to world-space.");
                return;
            }
        }
        Debug.Log("[VRWorldHud] No card canvas found (name needs 'card'/'hand'/'playercanvas').");
    }

    // Where the hand sits relative to the gaze HUD (HUD-local: +X = your right, +Y = up).
    // Tweak these two to taste — moved to the right and lowered into comfortable view.
    private static readonly Vector3 CARD_OFFSET = new Vector3(0.30f, 1.05f, 0f); // nudged further left
    private const float CARD_SCALE = 0.002418f; // 7% smaller than 0.0026

    // Dock the player's hand canvas to the lower-right of the HUD, facing the player, every frame.
    // Kept parented to the player so it's cleaned up with them; we drive world pose directly.
    private void DockCardCanvas()
    {
        var t = _cardCanvas.transform;
        t.position = _root.TransformPoint(CARD_OFFSET);
        t.rotation = _root.rotation;
        t.localScale = Vector3.one * CARD_SCALE;
    }

    // Where the power cone sits relative to the HUD (HUD-local). Off to your RIGHT.
    private static readonly Vector3 POWER_OFFSET = new Vector3(1.064f, 0f, 0f); // pulled 20% back left
    private const float POWER_SCALE = 0.00136f; // 15% smaller than 0.0016

    // Find the local power-meter cone and make sure it renders in VR (world-space).
    private void PromotePowerCanvas(Camera cam)
    {
        var reactor = FindObjectOfType<PowerMeterReactor>(true);
        if (reactor == null) return;
        _powerReactor = reactor;

        var c = reactor.GetComponentInParent<Canvas>();
        if (c == null) return;
        _powerCanvas = c;
        _powerSharesCard = (c == _cardCanvas);

        if (!_powerSharesCard) // its own canvas → promote it; shared canvas is already world-space
        {
            c.renderMode = RenderMode.WorldSpace;
            c.worldCamera = cam;
            c.enabled = true; // un-hide if the suppressor already disabled it
            var scaler = c.GetComponent<CanvasScaler>() ?? c.gameObject.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 4f;
            scaler.referencePixelsPerUnit = 100f;
        }
        Debug.Log($"[VRWorldHud] Power meter on '{c.name}' (sharesHandCanvas={_powerSharesCard}) — docking left.");
    }

    private void DockPowerCanvas()
    {
        if (_powerSharesCard)
        {
            // Shares the hand canvas: we can't move the whole canvas (that's the cards), so we
            // reposition just the cone's group in world space. Size inherits the hand canvas scale.
            var t = _powerReactor != null ? _powerReactor.MeterRoot : null;
            if (t != null)
            {
                t.position = _root.TransformPoint(POWER_OFFSET);
                t.rotation = _root.rotation;
            }
        }
        else
        {
            var t = _powerCanvas.transform;
            t.position = _root.TransformPoint(POWER_OFFSET);
            t.rotation = _root.rotation;
            t.localScale = Vector3.one * POWER_SCALE;
        }
    }

    private void GetCombatants(out PlayerCombat self, out PlayerCombat opp)
    {
        self = null; opp = null;
        foreach (var p in GameManager.players)
        {
            if (p == null) continue;
            if (p.isLocalPlayer)
            {
                self = p.GetComponent<PlayerCombat>();
                var o = p.GetOpponent();
                if (o != null) opp = o.GetComponent<PlayerCombat>();
                return;
            }
        }
    }

    private static Color SeverityColor(float pct)
    {
        // 0% = green (fresh), 50% = yellow, 100%+ = red (about to lose).
        if (pct < 50f) return Color.Lerp(Color.green, Color.yellow, pct / 50f);
        return Color.Lerp(Color.yellow, Color.red, Mathf.Clamp01((pct - 50f) / 50f));
    }
}
