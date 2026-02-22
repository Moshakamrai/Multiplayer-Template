using UnityEngine;

public class HitboxProperties : MonoBehaviour
{
    // The damage this glove will deal on its next collision
    public int currentDamage = 10;
    
    // Reference to the owner so we don't punch ourselves
    public PlayerCombat owner;

    private void Start()
    {
        owner = GetComponentInParent<PlayerCombat>();
    }
}