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

    [Header("Head look")]
    [Tooltip("Look toward the camera while talking (needs Humanoid rig + IK Pass on Base Layer).")]
    public bool lookAtCamera = true;

    bool _swordOut;
    bool _wasSpeaking;
    // Animation is QUEUED here and only fires when the voice audio actually starts
    // (Piper synthesis takes ~0.5s — without this the anim leads the voice).
    string _pendingState;
    bool _pendingSword;
    float _pendingSince;

    void Start()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (piper == null) piper = FindObjectOfType<PiperVoice>();
        if (gibberish == null) gibberish = FindObjectOfType<GrizVoice>();

        if (lookAtCamera && animator != null)
        {
            var look = animator.gameObject.GetComponent<GrizHeadLook>();
            if (look == null) look = animator.gameObject.AddComponent<GrizHeadLook>();
            look.link = this;
            if (look.target == null && Camera.main != null) look.target = Camera.main.transform;
        }
    }

    bool Speaking =>
        (piper != null && piper.IsSpeaking) || (gibberish != null && gibberish.IsSpeaking);

    /// <summary>True while his voice is playing — used by GrizHeadLook.</summary>
    public bool IsTalking => Speaking;

    /// <summary>Called by the console/hub whenever Griz starts a line.</summary>
    public void PlayForLine(string line, GrizBrain.Intent intent, GrizBrain brain)
    {
        if (animator == null) return;

        // Draw the sword ONLY when actually asked about the merchandise (inventory /
        // item-lore questions) — not just any line that name-drops it (e.g. the greeting).
        bool askedAboutWares = intent == GrizBrain.Intent.Inventory || intent == GrizBrain.Intent.AskInfo;
        if (!_swordOut && askedAboutWares && MentionsSword(line))
        {
            _swordOut = true;
            _pendingSword = true;
            if (!string.IsNullOrEmpty(unsheatheState))
            {
                QueueState(unsheatheState);
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
            QueueState(state);
    }

    void QueueState(string state)
    {
        _pendingState = state;
        _pendingSince = Time.time;
        if (Speaking) ApplyPending(); // audio already rolling (e.g. cached wav) — fire now
    }

    void ApplyPending()
    {
        if (_pendingSword)
        {
            if (swordObject != null) swordObject.SetActive(true);
            _pendingSword = false;
        }
        if (!string.IsNullOrEmpty(_pendingState) && animator != null)
            animator.CrossFadeInFixedTime(_pendingState, crossFadeSeconds);
        _pendingState = null;
    }

    void Update()
    {
        bool speaking = Speaking;

        // fire the queued state the moment the audio starts (3s fallback if voice never comes)
        if (_pendingState != null && (speaking || Time.time - _pendingSince > 3f))
            ApplyPending();

        // line finished (or was interrupted) → settle back to idle (sword idle once drawn)
        string rest = _swordOut && !string.IsNullOrEmpty(swordIdleState) ? swordIdleState : idleState;
        if (_wasSpeaking && !speaking && _pendingState == null && animator != null && !string.IsNullOrEmpty(rest))
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
