using UnityEngine;

// Amplitude-driven mouth movement — no plugins, works with any runtime-generated voice
// clip (Piper TTS here). Reads the ACTUAL PLAYING AUDIO's live output level each frame
// and drives either a blendshape (if the mesh has an ARKit/viseme-style "Jaw Open" or
// "Mouth Open" shape) or a bone rotation (if you'd rather rock a jaw bone). Pick ONE
// drive mode per character; both are wired for convenience.
//
// This is NOT full viseme lipsync (it won't shape different vowels/consonants) — it's
// the classic "mouth flaps proportional to loudness" technique. At NPC conversation
// distance it reads convincingly and costs nothing extra: no baked visemes, no extra
// synthesis step, works the instant a WAV starts playing.
//
// Upgrade path (documented, not built): for real per-phoneme mouth shapes, feed this same
// AudioSource into uLipSync (free, Unity Asset Store / GitHub) or OVRLipSync — both
// support analyzing a clip that's already playing, same hook point as this component.
public class MouthLipSync : MonoBehaviour
{
    [Header("Audio source to analyze")]
    [Tooltip("Usually the PiperVoice component on this NPC. Auto-found if left empty.")]
    public PiperVoice piperVoice;
    [Tooltip("Explicit AudioSource override — set this if not using PiperVoice.")]
    public AudioSource explicitSource;

    [Header("Blendshape drive (skip if using the jaw bone instead)")]
    [Tooltip("The renderer with the mouth blendshape. If empty, auto-search prefers a renderer whose " +
             "GameObject name contains 'head' or 'face' (e.g. Head_Mesh) over others (Eyes, EyeL_Mesh...).")]
    public SkinnedMeshRenderer face;
    [Tooltip("Index of the mouth-open blendshape on 'face'. -1 = search by name instead.")]
    public int blendShapeIndex = -1;
    [Tooltip("Blendshape name to search for if index is -1 (e.g. \"JawOpen\", \"mouthOpen\", \"Viseme_AA\"). " +
             "ARKit-style rigs usually name it JawOpen, not Mouth___ — check the logged blendshape list " +
             "at Start if the mouth doesn't move.")]
    public string blendShapeNameContains = "jawopen";
    [Tooltip("Log every blendshape name found on 'face' at Start — turn on once to see what your model actually has.")]
    public bool logBlendShapeNames = true;

    [Header("Jaw bone drive (skip if using the blendshape instead)")]
    public Transform jawBone;
    [Tooltip("Local rotation (degrees) applied at FULL mouth-open, relative to jawBone's rest pose.")]
    public Vector3 jawOpenEuler = new Vector3(-18f, 0f, 0f);

    [Header("Feel")]
    [Tooltip("Raw amplitude below this is treated as silence (avoids mouth twitching on noise floor).")]
    [Range(0f, 0.1f)] public float noiseFloor = 0.015f;
    [Tooltip("How hard to push quiet audio up toward fully-open — lower = more sensitive.")]
    [Range(0.5f, 8f)] public float gain = 3.5f;
    [Tooltip("How fast the mouth opens (higher = snappier).")]
    public float openSpeed = 22f;
    [Tooltip("How fast the mouth closes (higher = snappier; usually faster than opening).")]
    public float closeSpeed = 14f;

    const int SampleWindow = 256;
    float[] _samples = new float[SampleWindow];
    float _level;      // smoothed 0..1 mouth-open amount
    Quaternion _jawRestLocal;
    bool _haveJawRest;

    void Start()
    {
        if (piperVoice == null) piperVoice = GetComponent<PiperVoice>();
        if (piperVoice == null) piperVoice = GetComponentInParent<PiperVoice>();

        if (face == null)
        {
            // Prefer a renderer whose object name suggests it's the face/head, since a
            // model can have several SkinnedMeshRenderers (Eyes, EyeL_Mesh, Head_Mesh...)
            // and GetComponentInChildren just grabs whichever comes first in hierarchy order.
            SkinnedMeshRenderer firstFound = null;
            foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (firstFound == null) firstFound = smr;
                string n = smr.gameObject.name.ToLowerInvariant();
                if (n.Contains("head") || n.Contains("face"))
                {
                    face = smr;
                    break;
                }
            }
            if (face == null) face = firstFound;
        }

        if (face != null && face.sharedMesh != null && logBlendShapeNames)
        {
            int count = face.sharedMesh.blendShapeCount;
            if (count == 0)
            {
                Debug.Log($"[MouthLipSync] '{face.gameObject.name}' has NO blendshapes at all.", this);
            }
            else
            {
                var names = new string[count];
                for (int i = 0; i < count; i++) names[i] = $"[{i}] {face.sharedMesh.GetBlendShapeName(i)}";
                Debug.Log($"[MouthLipSync] '{face.gameObject.name}' blendshapes ({count}):\n" + string.Join("\n", names), this);
            }
        }

        if (blendShapeIndex < 0 && face != null && face.sharedMesh != null && !string.IsNullOrEmpty(blendShapeNameContains))
        {
            // strip any prefix like "ExpressionBlendshapes." before matching, and search
            // jaw-open first (best mouth-open proxy), falling back to the raw search term.
            string want = blendShapeNameContains.ToLowerInvariant();
            int fallback = -1;
            for (int i = 0; i < face.sharedMesh.blendShapeCount; i++)
            {
                string raw = face.sharedMesh.GetBlendShapeName(i);
                string leaf = raw.Contains(".") ? raw.Substring(raw.LastIndexOf('.') + 1) : raw;
                string lower = leaf.ToLowerInvariant();
                if (lower == "jawopen" || lower.Replace("_", "") == "jawopen") { blendShapeIndex = i; break; }
                if (fallback < 0 && lower.Contains(want)) fallback = i;
            }
            if (blendShapeIndex < 0) blendShapeIndex = fallback;
        }

        if (jawBone != null)
        {
            _jawRestLocal = jawBone.localRotation;
            _haveJawRest = true;
        }

        if ((face == null || blendShapeIndex < 0) && jawBone == null)
        {
            Debug.LogWarning("[MouthLipSync] No mouth blendshape found and no jawBone assigned — " +
                "nothing will visibly move. Most Mixamo characters have no mouth blendshapes at all: " +
                "either (a) assign a jaw/head bone to 'Jaw Bone' for a subtle nod-on-talk substitute, " +
                "or (b) use a face rig that has ARKit-style blendshapes and set 'Blend Shape Name Contains'.", this);
        }
    }

    PiperVoice[] _allVoices;
    float _nextVoiceScan;

    AudioSource ActiveSource
    {
        get
        {
            if (explicitSource != null) return explicitSource;
            // PiperVoice is often created at RUNTIME on a different GameObject — and on a
            // SHARED model (NpcSwitcher), the assigned voice can belong to a PAUSED NPC
            // while the audible line plays through another NPC's PiperVoice. So: trust the
            // assigned voice while it's actually playing, otherwise fall back to whichever
            // PiperVoice in the scene IS playing right now. Self-healing, no wiring order
            // assumptions.
            if (piperVoice != null && piperVoice.Source != null && piperVoice.Source.isPlaying)
                return piperVoice.Source;
            if (_allVoices == null || Time.unscaledTime >= _nextVoiceScan)
            {
                _allVoices = FindObjectsOfType<PiperVoice>();
                _nextVoiceScan = Time.unscaledTime + 2f;
            }
            foreach (var pv in _allVoices)
                if (pv != null && pv.Source != null && pv.Source.isPlaying) return pv.Source;
            return piperVoice != null ? piperVoice.Source : null;
        }
    }

    // LateUpdate, NOT Update: the Animator evaluates between the two, and any animation
    // clip that binds face properties would stomp weights written in Update every frame —
    // the classic "mouth stopped moving when the Animator got real states" regression.
    void LateUpdate()
    {
        var src = ActiveSource;
        float target = 0f;

        if (src != null && src.isPlaying)
        {
            src.GetOutputData(_samples, 0); // live playback samples, channel 0
            float sum = 0f;
            for (int i = 0; i < _samples.Length; i++) sum += _samples[i] * _samples[i];
            float rms = Mathf.Sqrt(sum / _samples.Length);

            if (rms > noiseFloor)
                target = Mathf.Clamp01((rms - noiseFloor) * gain);
        }

        float speed = target > _level ? openSpeed : closeSpeed;
        _level = Mathf.MoveTowards(_level, target, speed * Time.deltaTime);

        if (face != null && blendShapeIndex >= 0)
            face.SetBlendShapeWeight(blendShapeIndex, _level * 100f);

        if (jawBone != null && _haveJawRest)
            jawBone.localRotation = _jawRestLocal * Quaternion.Euler(jawOpenEuler * _level);
    }
}
