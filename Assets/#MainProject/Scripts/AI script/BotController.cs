using UnityEngine;
using Mirror;

public class BotController : NetworkBehaviour
{
    private PlayerCombat _combat;
    private PlayerController _controller;

    // Replace your Start function
    void Start()
    {
        _combat = GetComponent<PlayerCombat>();
        _controller = GetComponent<PlayerController>();

        // Call the new public wrapper to avoid authority and protection errors
        if (isServer) _controller.SetReady(true);
    }

    [Server]
    public void ThinkNextMove()
    {
        if (_combat.IsHurting || _combat.IsDead) return;

        float decision = Random.value;
        string attack = "";
        Vector3 dash = Vector3.zero;

        // Simple Logic: 70% Attack, 20% Dodge, 10% Block
        if (decision < 0.7f)
        {
            float atkType = Random.value;
            if (atkType < 0.5f) attack = "Jab";
            else if (atkType < 0.8f) attack = "Cross";
            else attack = "Hook";
        }
        else if (decision < 0.9f)
        {
            dash = (Random.value > 0.5f) ? Vector3.left : Vector3.right;
        }
        else
        {
            attack = "Block";
        }

        // We inject directly into the combat buffer
        _combat.QueueRhythmMove(attack, dash);
    }
}