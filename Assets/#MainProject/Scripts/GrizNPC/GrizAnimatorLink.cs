using UnityEngine;

// Drives the shopkeeper model's animations from Griz's voice.
// Attach to the NPC model (or anywhere), assign the Animator.
// - talkingBool is set TRUE while he's speaking (Piper or gibberish) → use it to blend
//   a talk/jaw-flap state in the Animator.
// - gestureTrigger fires at random intervals WHILE talking → hook arm waves/shrugs.
// Leave either name empty to skip it. Works with any Animator setup.
public class GrizAnimatorLink : MonoBehaviour
{
    public Animator animator;
    public PiperVoice piper;
    public GrizVoice gibberish;

    [Tooltip("Animator bool set while Griz is speaking. Empty = unused.")]
    public string talkingBool = "Talking";
    [Tooltip("Animator trigger fired now and then while talking (gestures). Empty = unused.")]
    public string gestureTrigger = "Gesture";
    [Tooltip("Average seconds between gesture triggers while talking.")]
    public float gestureEvery = 4f;

    float _nextGesture;
    bool _wasTalking;

    void Start()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (piper == null) piper = FindObjectOfType<PiperVoice>();
        if (gibberish == null) gibberish = FindObjectOfType<GrizVoice>();
    }

    bool Speaking =>
        (piper != null && piper.IsSpeaking) || (gibberish != null && gibberish.IsSpeaking);

    void Update()
    {
        if (animator == null) return;
        bool talking = Speaking;

        if (!string.IsNullOrEmpty(talkingBool))
            animator.SetBool(talkingBool, talking);

        if (talking && !_wasTalking)
            _nextGesture = Time.time + 0.3f; // gesture soon after he starts a line

        if (talking && !string.IsNullOrEmpty(gestureTrigger) && Time.time >= _nextGesture)
        {
            animator.SetTrigger(gestureTrigger);
            _nextGesture = Time.time + gestureEvery * Random.Range(0.6f, 1.6f);
        }
        _wasTalking = talking;
    }
}
