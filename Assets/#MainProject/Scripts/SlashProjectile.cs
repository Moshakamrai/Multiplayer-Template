using UnityEngine;

/// <summary>
/// Put this on a slash VFX prefab. It flies forward (its local +Z) and self-destroys.
/// Set Speed to 0 for a stationary slash burst that just plays in place and fades.
/// PlayerCombat spawns it facing the opponent on each sword swing.
/// </summary>
public class SlashProjectile : MonoBehaviour
{
    [Tooltip("Forward travel speed (units/sec). 0 = stationary burst.")]
    public float speed = 7f;
    [Tooltip("Seconds before it auto-destroys.")]
    public float lifetime = 1.2f;

    void Start() => Destroy(gameObject, lifetime);

    void Update()
    {
        if (speed != 0f)
            transform.position += transform.forward * speed * Time.deltaTime;
    }
}
