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
    public SkinnedMeshRenderer face;
    [Tooltip("Index of the mouth-open blendshape on 'face'. -1 = search by name instead.")]
    public int blendShapeIndex = -1;
    [Tooltip("Blendshape name to search for if index is -1 (e.g. \"Jaw_Open\", \"mouthOpen\", \"Viseme_AA\").")]
    public string blendShapeNameContains = "mouth";

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
        if (face == null) face = GetComponentInChildren<SkinnedMeshRenderer>();

        if (blendShapeIndex < 0 && face != null && face.sharedMesh != null && !string.IsNullOrEmpty(blendShapeNameContains))
        {
            string want = blendShapeNameContains.ToLowerInvariant();
            for (int i = 0; i < face.sharedMesh.blendShapeCount; i++)
            {
                if (face.sharedMesh.GetBlendShapeName(i).ToLowerInvariant().Contains(want))
                {
                    blendShapeIndex = i;
                    break;
                }
            }
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

    AudioSource ActiveSource => explicitSource != null ? explicitSource : (piperVoice != null ? piperVoice.Source : null);

    void Update()
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
