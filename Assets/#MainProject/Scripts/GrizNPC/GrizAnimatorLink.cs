using UnityEngine;

// Drives the shopkeeper model's animations from Griz's mood + voice.
// NO Animator transitions needed: the script CrossFades directly into states by name —
// your controller just needs the states to exist (islands are fine).
//
// Mapping: kicked out → Yelling · player threatened/insulted him → Standing Arguing ·
// he didn't understand → Disappointed · shouty line (CAPS words) → Yelling ·
// otherwise a random Talking variant. Returns to Idle when the voice stops.
public class GrizAnimatorLink : MonoBehaviour
{
    public Animator animator;
    public PiperVoice piper;
    public GrizVoice gibberish;

    [Header("State names as they appear in the Animator Controller")]
    public string idleState = "Idle";
    public string[] talkingStates = { "Talking", "Talking 2", "Talking 3", "Talking 4" };
    public string yellingState = "Yelling";
    public string arguingState = "Standing Arguing";
    public string disappointedState = "Disappointed";
    public float crossFadeSeconds = 0.25f;

    [Header("Sword bit — unsheathes when he talks about the merchandise")]
    [Tooltip("The sword GameObject in his hand — disabled in the scene; turned on when he draws.")]
    public GameObject swordObject;
    [Tooltip("Plays once when a line mentions the sword; your transition takes it to sword idle.")]
    public string unsheatheState = "Withdrawing Sword";
    [Tooltip("Idle used INSTEAD of normal idle once the sword is out.")]
    public string swordIdleState = "Sword And Shield Idle";
    [Tooltip("A line containing any of these words triggers the draw.")]
    public string[] swordWords = { "rust-cutter", "sword" };

    bool _swordOut;
    bool _wasSpeaking;

    void Start()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (piper == null) piper = FindObjectOfType<PiperVoice>();
        if (gibberish == null) gibberish = FindObjectOfType<GrizVoice>();
    }

    bool Speaking =>
        (piper != null && piper.IsSpeaking) || (gibberish != null && gibberish.IsSpeaking);

    /// <summary>Called by the console/hub whenever Griz starts a line.</summary>
    public void PlayForLine(string line, GrizBrain.Intent intent, GrizBrain brain)
    {
        if (animator == null) return;

        // First mention of the merchandise → draw the sword (once), then sword idle takes over
        if (!_swordOut && MentionsSword(line))
        {
            _swordOut = true;
            if (swordObject != null) swordObject.SetActive(true);
            if (!string.IsNullOrEmpty(unsheatheState))
            {
                animator.CrossFadeInFixedTime(unsheatheState, crossFadeSeconds);
                return; // your Animator transition carries it into swordIdleState
            }
        }

        string state;
        if (brain != null && brain.KickedOut) state = yellingState;
        else if (intent == GrizBrain.Intent.Threaten || intent == GrizBrain.Intent.Insult) state = arguingState;
        else if (intent == GrizBrain.Intent.Unknown) state = disappointedState;
        else if (CapsWordCount(line) >= 2) state = yellingState;
        else state = talkingStates.Length > 0 ? talkingStates[Random.Range(0, talkingStates.Length)] : idleState;

        if (!string.IsNullOrEmpty(state))
            animator.CrossFadeInFixedTime(state, crossFadeSeconds);
    }

    void Update()
    {
        bool speaking = Speaking;
        // line finished (or was interrupted) → settle back to idle (sword idle once drawn)
        string rest = _swordOut && !string.IsNullOrEmpty(swordIdleState) ? swordIdleState : idleState;
        if (_wasSpeaking && !speaking && animator != null && !string.IsNullOrEmpty(rest))
            animator.CrossFadeInFixedTime(rest, crossFadeSeconds * 2f);
        _wasSpeaking = speaking;
    }

    bool MentionsSword(string line)
    {
        if (string.IsNullOrEmpty(line)) return false;
        string lower = line.ToLowerInvariant();
        foreach (var w in swordWords)
            if (!string.IsNullOrEmpty(w) && lower.Contains(w.ToLowerInvariant())) return true;
        return false;
    }

    static int CapsWordCount(string line)
    {
        if (string.IsNullOrEmpty(line)) return 0;
        int n = 0;
        foreach (var w in line.Split(' '))
        {
            if (w.Length >= 3 && w == w.ToUpperInvariant() && w != w.ToLowerInvariant()) n++;
        }
        return n;
    }
}
