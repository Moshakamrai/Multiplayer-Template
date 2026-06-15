using System.Collections;
using UnityEngine;

/// <summary>
/// Put this on a slash VFX prefab. It flies forward (its local +Z) and self-destroys.
/// Set Speed to 0 for a stationary slash burst that just plays in place and fades.
/// PlayerCombat spawns it facing the opponent on each sword swing.
///
/// DissolveAt(point): trade-loser treatment — the projectile keeps flying until it reaches
/// the clash point, then fizzles out there (emission stops, it shrinks away) instead of
/// passing through the winner's VFX.
/// </summary>
public class SlashProjectile : MonoBehaviour
{
    [Tooltip("Forward travel speed (units/sec). 0 = stationary burst.")]
    public float speed = 7f;
    [Tooltip("Seconds before it auto-destroys.")]
    public float lifetime = 1.2f;
    [Tooltip("Seconds the dissolve shrink takes once the clash point is reached.")]
    public float dissolveTime = 0.22f;

    private bool _dissolving;     // DissolveAt already scheduled
    private bool _holdPosition;   // stops forward motion once the clash point is reached

    void Start() => Invoke(nameof(Die), lifetime); // cancellable (unlike delayed Destroy)

    private void Die() => Destroy(gameObject);

    void Update()
    {
        if (speed != 0f && !_holdPosition)
            transform.position += transform.forward * speed * Time.deltaTime;
    }

    /// This projectile LOST the trade: let it fly to `clashPoint`, then fizzle out there.
    public void DissolveAt(Vector3 clashPoint)
    {
        if (_dissolving) return;
        _dissolving = true;

        // Time until we reach the clash point along our flight path.
        float dist  = Vector3.Dot(clashPoint - transform.position, transform.forward);
        float delay = speed > 0.01f ? Mathf.Max(0.02f, dist / speed) : 0.02f;
        // Re-schedule death so the lifetime timer can't kill us before the dissolve plays out.
        CancelInvoke(nameof(Die));
        Invoke(nameof(Die), delay + dissolveTime + 0.5f);
        StartCoroutine(DissolveRoutine(delay));
    }

    private IEnumerator DissolveRoutine(float delay)
    {
        yield return new WaitForSeconds(delay);
        _holdPosition = true;

        // Stop producing new particles/trails; existing ones fade naturally while we shrink.
        foreach (var ps in GetComponentsInChildren<ParticleSystem>())
        {
            var em = ps.emission;
            em.enabled = false;
        }
        foreach (var tr in GetComponentsInChildren<TrailRenderer>())
            tr.emitting = false;

        Vector3 startScale = transform.localScale;
        float t = 0f;
        while (t < dissolveTime)
        {
            t += Time.deltaTime;
            transform.localScale = startScale * Mathf.Max(0.001f, 1f - t / dissolveTime);
            yield return null;
        }
        Destroy(gameObject);
    }
}
