using UnityEngine;

// Makes Griz look toward the camera (or any target) while he's talking, easing back when idle.
// Auto-added by GrizAnimatorLink onto the Animator's own GameObject (IK callbacks only fire there).
// REQUIRES: Humanoid rig + "IK Pass" ticked on the Animator controller's Base Layer.
public class GrizHeadLook : MonoBehaviour
{
    public GrizAnimatorLink link;
    public Transform target;
    [Range(0f, 1f)] public float talkingWeight = 0.75f;
    [Range(0f, 1f)] public float idleWeight = 0.2f;
    public float easeSpeed = 3f;

    Animator _anim;
    float _weight;

    void Awake() { _anim = GetComponent<Animator>(); }

    void OnAnimatorIK(int layerIndex)
    {
        if (_anim == null || target == null) return;
        float goal = (link != null && link.IsTalking) ? talkingWeight : idleWeight;
        _weight = Mathf.Lerp(_weight, goal, Time.deltaTime * easeSpeed);
        // head-dominant look: body stays planted, head + eyes track
        _anim.SetLookAtWeight(_weight, 0.1f, 0.9f, 1f, 0.55f);
        _anim.SetLookAtPosition(target.position);
    }
}
