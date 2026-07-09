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
        // line finished (or was interrupted) → settle back to idle
        if (_wasSpeaking && !speaking && animator != null && !string.IsNullOrEmpty(idleState))
            animator.CrossFadeInFixedTime(idleState, crossFadeSeconds * 2f);
        _wasSpeaking = speaking;
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
