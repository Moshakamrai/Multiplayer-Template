using System.Collections.Generic;
using UnityEngine;

// Makes an ARKit-blendshape face feel ALIVE while (and between) talking. The Gunan head
// has 53 expression blendshapes and until now only JawOpen was used (MouthLipSync).
// This drives the rest:
//   - BLINKING: randomized 2-6s interval, fast close / slower open. The single biggest
//     "it's alive" signal and it costs nothing.
//   - EYE MICRO-DARTS: small saccades between blinks so the gaze isn't glassy.
//   - TALKING BROWS: brow raise pulses that follow speech loudness (same live-amplitude
//     technique as MouthLipSync) — reads as emphasis.
//   - MOOD: SetMood("warm"/"cold"/"funny"/"rude"/"neutral") eases the face toward an
//     expression and slowly decays back to neutral. The consoles already extract exactly
//     these sentiment tags per reply, so this is driven by what's actually being said.
//
// Auto-added by GrizAnimatorLink (like MouthLipSync). Owns every blendshape EXCEPT
// JawOpen, which stays MouthLipSync's — no fighting over the same weights.
public class FacialExpressions : MonoBehaviour
{
    [Tooltip("Renderer with the ARKit-style blendshapes. Auto-found (prefers 'head'/'face' names) if empty.")]
    public SkinnedMeshRenderer face;
    [Tooltip("Voice to read live loudness from for brow emphasis. Auto-found if empty.")]
    public PiperVoice piperVoice;

    [Header("Blink")]
    public float blinkIntervalMin = 2f;
    public float blinkIntervalMax = 6f;
    public float blinkCloseSeconds = 0.06f;
    public float blinkOpenSeconds = 0.12f;

    [Header("Eye micro-darts")]
    public float dartIntervalMin = 3f;
    public float dartIntervalMax = 8f;
    [Range(0f, 100f)] public float dartWeight = 25f;

    [Header("Mood")]
    [Tooltip("How fast the face eases toward a new mood expression.")]
    public float moodEaseSpeed = 4f;
    [Tooltip("Seconds after which a mood starts relaxing back to neutral.")]
    public float moodHoldSeconds = 9f;

    [Header("Talking brow emphasis")]
    [Range(0.5f, 8f)] public float browGain = 3f;
    [Range(0f, 100f)] public float maxBrowWeight = 30f;

    // found blendshape indices (-1 = not present on this mesh)
    int _blinkL = -1, _blinkR = -1;
    int _smileL = -1, _smileR = -1, _cheekL = -1, _cheekR = -1;
    int _browsUC = -1, _browsDL = -1, _browsDR = -1, _browsSq = -1;
    int _frownL = -1, _frownR = -1, _sneerL = -1, _sneerR = -1;
    int _eyeInL = -1, _eyeInR = -1, _eyeOutL = -1, _eyeOutR = -1;

    readonly Dictionary<int, float> _moodTarget = new Dictionary<int, float>();
    readonly Dictionary<int, float> _current = new Dictionary<int, float>();

    float _nextBlink;
    float _blinkPhase = -1f; // <0 = not blinking; else seconds into the blink
    float _nextDart;
    float _dartEnd;
    int _dartShapeA = -1, _dartShapeB = -1;
    float _moodSetTime = -99f;
    float _browLevel;
    const int SampleWindow = 256;
    float[] _samples = new float[SampleWindow];

    void Start()
    {
        if (piperVoice == null) piperVoice = FindObjectOfType<PiperVoice>();
        if (face == null)
        {
            SkinnedMeshRenderer firstFound = null;
            foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (firstFound == null) firstFound = smr;
                string n = smr.gameObject.name.ToLowerInvariant();
                if (n.Contains("head") || n.Contains("face")) { face = smr; break; }
            }
            if (face == null) face = firstFound;
        }
        if (face == null || face.sharedMesh == null)
        {
            Debug.LogWarning("[FacialExpressions] no face mesh found — disabling.", this);
            enabled = false;
            return;
        }

        _blinkL = Find("eyeblink_l"); _blinkR = Find("eyeblink_r");
        _smileL = Find("mouthsmile_l"); _smileR = Find("mouthsmile_r");
        _cheekL = Find("cheeksquint_l"); _cheekR = Find("cheeksquint_r");
        _browsUC = Find("browsu_c"); _browsDL = Find("browsd_l"); _browsDR = Find("browsd_r");
        _browsSq = Find("browssqueeze");
        _frownL = Find("mouthfrown_l"); _frownR = Find("mouthfrown_r");
        _sneerL = Find("sneer_l"); _sneerR = Find("sneer_r");
        _eyeInL = Find("eyein_l"); _eyeInR = Find("eyein_r");
        _eyeOutL = Find("eyeout_l"); _eyeOutR = Find("eyeout_r");

        if (_blinkL < 0)
            Debug.LogWarning("[FacialExpressions] no EyeBlink blendshapes found — this mesh may use different names; check the MouthLipSync blendshape dump.", this);

        _nextBlink = Time.time + Random.Range(blinkIntervalMin, blinkIntervalMax);
        _nextDart = Time.time + Random.Range(dartIntervalMin, dartIntervalMax);
    }

    // matches by the leaf name after any "ExpressionBlendshapes." style prefix
    int Find(string leafLower)
    {
        var mesh = face.sharedMesh;
        for (int i = 0; i < mesh.blendShapeCount; i++)
        {
            string raw = mesh.GetBlendShapeName(i);
            string leaf = raw.Contains(".") ? raw.Substring(raw.LastIndexOf('.') + 1) : raw;
            if (leaf.ToLowerInvariant() == leafLower) return i;
        }
        return -1;
    }

    /// <summary>Ease the face toward a mood. Accepts the consoles' sentiment tags:
    /// warm, cold, funny, rude, neutral (anything else = neutral).</summary>
    public void SetMood(string mood)
    {
        _moodTarget.Clear();
        switch ((mood ?? "").ToLowerInvariant())
        {
            case "warm":
                Target(_smileL, 55f); Target(_smileR, 55f); Target(_browsUC, 20f);
                break;
            case "funny":
                Target(_smileL, 75f); Target(_smileR, 75f); Target(_cheekL, 35f); Target(_cheekR, 35f); Target(_browsUC, 30f);
                break;
            case "cold":
                Target(_browsDL, 45f); Target(_browsDR, 45f); Target(_frownL, 25f); Target(_frownR, 25f);
                break;
            case "rude": // the player was rude — she's offended
                Target(_browsSq, 50f); Target(_sneerL, 30f); Target(_sneerR, 30f); Target(_frownL, 30f); Target(_frownR, 30f);
                break;
            // neutral / unknown: empty target set → everything eases back to 0
        }
        _moodSetTime = Time.time;
    }

    void Target(int idx, float weight) { if (idx >= 0) _moodTarget[idx] = weight; }

    void Update()
    {
        if (face == null) return;

        // ── mood easing (+ slow decay back to neutral after the hold period) ──
        float decay = Time.time - _moodSetTime > moodHoldSeconds ? 0.35f : 1f;
        EaseMoodShape(_smileL); EaseMoodShape(_smileR); EaseMoodShape(_cheekL); EaseMoodShape(_cheekR);
        EaseMoodShape(_browsDL); EaseMoodShape(_browsDR); EaseMoodShape(_browsSq);
        EaseMoodShape(_frownL); EaseMoodShape(_frownR); EaseMoodShape(_sneerL); EaseMoodShape(_sneerR);
        void EaseMoodShape(int idx)
        {
            if (idx < 0) return;
            float target = (_moodTarget.TryGetValue(idx, out float t) ? t : 0f) * decay;
            float cur = _current.TryGetValue(idx, out float c) ? c : 0f;
            cur = Mathf.MoveTowards(cur, target, moodEaseSpeed * 60f * Time.deltaTime);
            _current[idx] = cur;
            face.SetBlendShapeWeight(idx, cur);
        }

        // ── talking brow emphasis (live loudness, same trick as MouthLipSync) ──
        float browTarget = 0f;
        var src = piperVoice != null ? piperVoice.Source : null;
        if (src != null && src.isPlaying)
        {
            src.GetOutputData(_samples, 0);
            float sum = 0f;
            for (int i = 0; i < _samples.Length; i++) sum += _samples[i] * _samples[i];
            float rms = Mathf.Sqrt(sum / _samples.Length);
            browTarget = Mathf.Clamp01(rms * browGain) * maxBrowWeight;
        }
        _browLevel = Mathf.MoveTowards(_browLevel, browTarget, 180f * Time.deltaTime);
        if (_browsUC >= 0)
        {
            // brow-up emphasis coexists with mood: take whichever is stronger
            float moodBrow = _moodTarget.TryGetValue(_browsUC, out float mb) ? mb * decay : 0f;
            face.SetBlendShapeWeight(_browsUC, Mathf.Max(_browLevel, moodBrow));
        }

        // ── blinking ──
        if (_blinkPhase < 0f && Time.time >= _nextBlink)
            _blinkPhase = 0f;
        if (_blinkPhase >= 0f)
        {
            _blinkPhase += Time.deltaTime;
            float w;
            if (_blinkPhase < blinkCloseSeconds)
                w = (_blinkPhase / blinkCloseSeconds) * 100f;
            else if (_blinkPhase < blinkCloseSeconds + blinkOpenSeconds)
                w = (1f - (_blinkPhase - blinkCloseSeconds) / blinkOpenSeconds) * 100f;
            else
            {
                w = 0f;
                _blinkPhase = -1f;
                _nextBlink = Time.time + Random.Range(blinkIntervalMin, blinkIntervalMax);
            }
            if (_blinkL >= 0) face.SetBlendShapeWeight(_blinkL, w);
            if (_blinkR >= 0) face.SetBlendShapeWeight(_blinkR, w);
        }

        // ── eye micro-darts (skipped mid-blink) ──
        if (Time.time >= _nextDart && _blinkPhase < 0f && _dartShapeA < 0)
        {
            bool lookIn = Random.value > 0.5f;
            _dartShapeA = lookIn ? _eyeInL : _eyeOutL;
            _dartShapeB = lookIn ? _eyeInR : _eyeOutR;
            _dartEnd = Time.time + Random.Range(0.3f, 0.8f);
        }
        if (_dartShapeA >= 0)
        {
            bool active = Time.time < _dartEnd;
            float w = active ? dartWeight : 0f;
            face.SetBlendShapeWeight(_dartShapeA, w);
            if (_dartShapeB >= 0) face.SetBlendShapeWeight(_dartShapeB, w);
            if (!active)
            {
                _dartShapeA = _dartShapeB = -1;
                _nextDart = Time.time + Random.Range(dartIntervalMin, dartIntervalMax);
            }
        }
    }
}
