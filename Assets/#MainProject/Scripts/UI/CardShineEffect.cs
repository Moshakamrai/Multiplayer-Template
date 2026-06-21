using UnityEngine;
using UnityEngine.UI;
using System.Collections;

// Attach to each hand-card button's Image. On Awake it swaps in an instance of the
// "UI/CardActivate" shader and drives all the card visuals:
//   • constant subtle holographic shimmer
//   • shine-sweep + glow + scale punch on Activate() (voice acceptance)
//   • digital DISSOLVE-OUT when the card leaves the hand, DISSOLVE-IN when it's drawn
// CardManager calls SetShown(true/false) each frame; this owns the enable/disable so the
// dissolve can finish before the GameObject is turned off.
[RequireComponent(typeof(Image))]
public class CardShineEffect : MonoBehaviour
{
    [Header("Activation Shine")]
    public Color shineColor    = new Color(1f, 1f, 1f, 1f);
    public float shineWidth    = 0.18f;
    public float shineDuration = 0.45f;

    [Header("Activation Glow")]
    public Color glowColor = new Color(0.30f, 0.90f, 1f, 1f);
    public float glowPeak  = 1.6f;

    [Header("Idle Shimmer")]
    public Color shimmerColor = new Color(0.40f, 0.80f, 1f, 1f);
    [Range(0f, 1f)] public float idleShimmer = 0.06f;
    public float shimmerSpeed = 3.0f;

    [Header("Scale Punch (juice)")]
    public float punchScale    = 0.12f;   // 0 = disabled
    public float punchDuration = 0.25f;

    [Header("\"Called\" Pop (extra juice when a card is locked in)")]
    [Tooltip("ON: card does an elastic overshoot + tilt wobble + glow flash when activated, " +
             "instead of the plain shine. OFF: just the subtle shine/punch above.")]
    public bool useCalledPop = true;
    [Tooltip("How far the card overshoots its size at the peak of the pop (0.3 = +30%).")]
    public float popOvershoot = 0.32f;
    [Tooltip("Total length of the pop animation (overshoot + elastic settle).")]
    public float popDuration = 0.55f;
    [Tooltip("Max tilt (degrees) of the quick wobble during the pop.")]
    public float popTilt = 9f;
    [Tooltip("Glow brightness at the flash peak (stacks on the shine glow).")]
    public float popGlowPeak = 2.6f;

    [Header("Active-Card Glow (persists while this card is the chosen/playing move)")]
    [Tooltip("Base glow held on the active card.")]
    public float activeGlowBase = 1.2f;
    [Tooltip("Extra glow added at the peak of the on-beat pulse.")]
    public float activeGlowPulse = 1.8f;
    [Tooltip("Slight scale-up of the active card so it stands out.")]
    public float activeScaleBoost = 0.06f;
    [Tooltip("Continuous gentle wobble (tilt degrees) on the active card. Keep small.")]
    public float activeWobbleAngle = 3.5f;
    [Tooltip("Wobble speed (cycles/sec-ish).")]
    public float activeWobbleSpeed = 2.0f;

    [Header("Dissolve (exit / enter)")]
    public Color dissolveColor   = new Color(1f, 0.55f, 0.1f, 1f); // hot burning edge
    public float dissolveEdge    = 0.08f;
    public float dissolveScale   = 14f;
    public float exitDuration    = 0.40f;  // dissolve OUT when card leaves
    public float enterDuration   = 0.35f;  // dissolve IN when card is drawn

    private enum State { Hidden, Entering, Shown, Exiting }
    private State _state = State.Shown;

    private Image     _img;
    private Material  _mat;
    private RectTransform _rt;
    private Vector3   _baseScale = Vector3.one;
    private Quaternion _baseRot  = Quaternion.identity;
    private Coroutine _shine;
    private Coroutine _punch;
    private Coroutine _anim;   // enter/exit dissolve
    private Coroutine _pop;    // "called" elastic pop

    // Persistent "this is the active/chosen card" glow — driven each frame by CardManager.
    private bool  _activeGlow;
    private float _activeGlowAmt;   // 0..1 beat-charge for the pulse
    private Color _activeGlowColor = new Color(0.3f, 0.9f, 1f, 1f);

    /// CardManager calls this every frame: on = this card is the current move; amt = beat charge (0→1)
    /// so the glow pulses up to each beat. Turns the chosen card into a glowing, beat-pulsing border.
    public void SetActiveGlow(bool on, float amt, Color color)
    {
        _activeGlow = on;
        _activeGlowAmt = Mathf.Clamp01(amt);
        _activeGlowColor = color;
    }

    static readonly int ID_ShineColor      = Shader.PropertyToID("_ShineColor");
    static readonly int ID_Shine           = Shader.PropertyToID("_Shine");
    static readonly int ID_ShineWidth      = Shader.PropertyToID("_ShineWidth");
    static readonly int ID_Glow            = Shader.PropertyToID("_Glow");
    static readonly int ID_GlowColor       = Shader.PropertyToID("_GlowColor");
    static readonly int ID_ShimmerColor    = Shader.PropertyToID("_ShimmerColor");
    static readonly int ID_ShimmerStrength = Shader.PropertyToID("_ShimmerStrength");
    static readonly int ID_ShimmerSpeed    = Shader.PropertyToID("_ShimmerSpeed");
    static readonly int ID_Dissolve        = Shader.PropertyToID("_Dissolve");
    static readonly int ID_DissolveEdge    = Shader.PropertyToID("_DissolveEdge");
    static readonly int ID_DissolveScale   = Shader.PropertyToID("_DissolveScale");
    static readonly int ID_DissolveColor   = Shader.PropertyToID("_DissolveColor");

    void Awake()
    {
        _img = GetComponent<Image>();
        _rt  = GetComponent<RectTransform>();
        _baseScale = _rt.localScale;
        _baseRot   = _rt.localRotation;
        _state = gameObject.activeSelf ? State.Shown : State.Hidden;

        var shader = Shader.Find("UI/CardActivate");
        if (shader == null)
        {
            Debug.LogWarning("CardShineEffect: shader 'UI/CardActivate' not found. " +
                             "Add it to Project Settings > Graphics > Always Included Shaders for builds.");
            return;
        }

        _mat = new Material(shader);
        _img.material = _mat;

        _mat.SetColor(ID_ShineColor, shineColor);
        _mat.SetColor(ID_GlowColor, glowColor);
        _mat.SetColor(ID_ShimmerColor, shimmerColor);
        _mat.SetColor(ID_DissolveColor, dissolveColor);
        _mat.SetFloat(ID_ShineWidth, shineWidth);
        _mat.SetFloat(ID_ShimmerStrength, idleShimmer);
        _mat.SetFloat(ID_ShimmerSpeed, shimmerSpeed);
        _mat.SetFloat(ID_DissolveEdge, dissolveEdge);
        _mat.SetFloat(ID_DissolveScale, dissolveScale);
        _mat.SetFloat(ID_Shine, 0f);
        _mat.SetFloat(ID_Glow, 0f);
        _mat.SetFloat(ID_Dissolve, 0f);
    }

    // Persistent active-card glow + gentle scale, applied each frame. Skips while the one-shot pop /
    // shine coroutines are running (they own the glow then) so they don't fight.
    void Update()
    {
        if (_mat == null || _state != State.Shown) return;
        if (_pop != null || _shine != null) return; // let the "called" burst finish first

        if (_activeGlow)
        {
            // Glow held at a base level, pulsing UP toward each beat (amt = beat charge 0→1).
            float pulse = 0.7f + 0.3f * Mathf.Sin(Time.time * 6f * Mathf.PI);
            float g = activeGlowBase + activeGlowPulse * _activeGlowAmt * pulse;
            _mat.SetColor(ID_GlowColor, _activeGlowColor);
            _mat.SetFloat(ID_Glow, g);
            if (_rt != null)
            {
                _rt.localScale = _baseScale * (1f + activeScaleBoost * (0.5f + 0.5f * _activeGlowAmt));
                // Continuous gentle wobble — a soft tilt sway (two slightly-offset sines so it doesn't
                // look like a clean metronome) to read as "alive / chosen", without being distracting.
                float wob = Mathf.Sin(Time.time * activeWobbleSpeed * Mathf.PI)
                          + 0.4f * Mathf.Sin(Time.time * activeWobbleSpeed * 1.7f * Mathf.PI);
                _rt.localRotation = _baseRot * Quaternion.Euler(0f, 0f, wob * activeWobbleAngle * 0.7f);
            }
        }
        else
        {
            // Not the active card → make sure we're not leaving a stale glow/scale/rotation on it.
            _mat.SetFloat(ID_Glow, 0f);
            if (_rt != null && _rt.localScale != _baseScale) _rt.localScale = _baseScale;
            if (_rt != null && _rt.localRotation != _baseRot) _rt.localRotation = _baseRot;
        }
    }

    // ── Show / hide with dissolve, driven by CardManager ───────────────────
    public void SetShown(bool show)
    {
        // No material (shader missing) → fall back to a plain instant toggle.
        if (_mat == null)
        {
            if (gameObject.activeSelf != show) gameObject.SetActive(show);
            _state = show ? State.Shown : State.Hidden;
            return;
        }

        if (show)
        {
            if (_state == State.Shown || _state == State.Entering) return;
            StartEnter();
        }
        else
        {
            if (_state == State.Hidden || _state == State.Exiting) return;
            StartExit();
        }
    }

    private void StartEnter()
    {
        _state = State.Entering;
        if (!gameObject.activeSelf) gameObject.SetActive(true);
        _mat.SetFloat(ID_Dissolve, 1f);
        if (_anim != null) StopCoroutine(_anim);
        _anim = StartCoroutine(DissolveRoutine(1f, 0f, enterDuration, State.Shown, false));
    }

    private void StartExit()
    {
        if (!gameObject.activeSelf) { _state = State.Hidden; return; }
        _state = State.Exiting;
        float from = _mat.GetFloat(ID_Dissolve);
        if (_anim != null) StopCoroutine(_anim);
        _anim = StartCoroutine(DissolveRoutine(from, 1f, exitDuration, State.Hidden, true));
    }

    private IEnumerator DissolveRoutine(float from, float to, float dur, State endState, bool disableAtEnd)
    {
        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(t / dur);
            _mat.SetFloat(ID_Dissolve, Mathf.Lerp(from, to, p));
            yield return null;
        }
        _mat.SetFloat(ID_Dissolve, to);
        _state = endState;
        _anim = null;
        if (disableAtEnd) gameObject.SetActive(false);
    }

    // ── Activation burst (called when the card is accepted by voice) ───────
    public void Activate()
    {
        if (!isActiveAndEnabled || _mat == null) return;

        // Always sweep the shine across the card.
        if (_shine != null) StopCoroutine(_shine);
        _shine = StartCoroutine(ShineRoutine());

        if (useCalledPop)
        {
            // Juicy "card called" moment: elastic scale overshoot + tilt wobble + glow flash.
            if (_pop != null) StopCoroutine(_pop);
            _pop = StartCoroutine(CalledPopRoutine());
        }
        else if (punchScale > 0.001f)
        {
            if (_punch != null) StopCoroutine(_punch);
            _punch = StartCoroutine(PunchRoutine());
        }
    }

    // Snappy overshoot that springs past full size then settles back with a damped bounce, while the
    // card tilts and a glow flash fires — reads clearly as "this card just got locked in".
    private IEnumerator CalledPopRoutine()
    {
        float tiltDir = Random.value < 0.5f ? -1f : 1f; // wobble left or right at random for variety
        float t = 0f;
        while (t < popDuration)
        {
            t += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(t / popDuration);

            // Damped elastic curve: a big overshoot up front that rings down to 0.
            float decay   = Mathf.Exp(-6f * p);
            float spring  = decay * Mathf.Sin(p * Mathf.PI * 3.0f);
            float scaleAdd = popOvershoot * spring;
            _rt.localScale = _baseScale * (1f + scaleAdd);

            // Tilt follows the same spring, fading out as it settles.
            _rt.localRotation = _baseRot * Quaternion.Euler(0f, 0f, tiltDir * popTilt * spring);

            // Glow flashes hardest at the start of the pop, then fades.
            _mat.SetFloat(ID_Glow, Mathf.Max(_mat.GetFloat(ID_Glow), popGlowPeak * decay));
            yield return null;
        }
        _rt.localScale    = _baseScale;
        _rt.localRotation = _baseRot;
        _mat.SetFloat(ID_Glow, 0f);
        _pop = null;
    }

    private IEnumerator ShineRoutine()
    {
        float t = 0f;
        while (t < shineDuration)
        {
            t += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(t / shineDuration);
            _mat.SetFloat(ID_Shine, p);
            // Max so the shine glow and the "called" pop glow combine instead of overwriting each
            // other when both run in the same frame.
            _mat.SetFloat(ID_Glow, Mathf.Max(_mat.GetFloat(ID_Glow), Mathf.Sin(p * Mathf.PI) * glowPeak));
            yield return null;
        }
        _mat.SetFloat(ID_Shine, 0f);
        if (_pop == null) _mat.SetFloat(ID_Glow, 0f); // don't kill the pop's glow if it's still going
        _shine = null;
    }

    private IEnumerator PunchRoutine()
    {
        float t = 0f;
        while (t < punchDuration)
        {
            t += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(t / punchDuration);
            float s = Mathf.Sin(p * Mathf.PI) * punchScale;
            _rt.localScale = _baseScale * (1f + s);
            yield return null;
        }
        _rt.localScale = _baseScale;
        _punch = null;
    }

    void OnDisable()
    {
        // Reset visuals so a re-enabled card starts clean.
        if (_mat != null)
        {
            _mat.SetFloat(ID_Shine, 0f);
            _mat.SetFloat(ID_Glow, 0f);
            _mat.SetFloat(ID_Dissolve, 0f);
        }
        if (_rt != null) { _rt.localScale = _baseScale; _rt.localRotation = _baseRot; }
        _shine = _punch = _anim = _pop = null;
    }

    void OnDestroy()
    {
        if (_mat != null) Destroy(_mat);
    }
}
