using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Mirror;

[System.Serializable]
public class CombatCard
{
    public string cardName; // Large Title: "PUNCH", "CAGE"
    public string triggerName; // Logic: "Jab", "ParryIntent"
    public string description; // Small text: "Reflects 120% dmg"
}


public class CardManager : NetworkBehaviour
{
    public List<CombatCard> cardLibrary = new List<CombatCard>(); 
    private List<CombatCard> _deck = new List<CombatCard>();
    
    public readonly SyncList<int> currentHandIndices = new SyncList<int>();

    // --- NEW ANIMATION STATE ---
    private struct DiscardAnim 
    { 
        public int libIndex; 
        public float startX; 
        public float startTime; 
    }
    private List<DiscardAnim> _activeDiscardAnims = new List<DiscardAnim>();

    public override void OnStartServer()
    {
        InitializeDeck();
    }

    [Server]
    public void InitializeDeck()
    {
        _deck.Clear();
        if (cardLibrary.Count == 0) return;

        for (int i = 0; i < cardLibrary.Count; i++)
        {
            _deck.Add(cardLibrary[i]);
            _deck.Add(cardLibrary[i]);
        }
        _deck = _deck.OrderBy(x => Random.value).ToList();
    }

    [Server]
    public void DealInitialHand()
    {
        currentHandIndices.Clear();
        for (int i = 0; i < 4; i++) DrawCard();
    }

    [Server]
    public void DrawCard()
    {
        if (currentHandIndices.Count >= 4) return;
        if (_deck.Count == 0) InitializeDeck();
        if (_deck.Count == 0) return;

        CombatCard drawn = _deck[0];
        _deck.RemoveAt(0);

        int libIndex = cardLibrary.FindIndex(c => c.triggerName == drawn.triggerName);
        currentHandIndices.Add(libIndex);
    }

    public bool IsCardInHand(string trigger)
    {
        foreach (int index in currentHandIndices)
            if (cardLibrary[index].triggerName == trigger) return true;
        return false;
    }

    [Server]
    public void DiscardCard(string trigger)
    {
        for (int i = 0; i < currentHandIndices.Count; i++)
        {
            if (cardLibrary[currentHandIndices[i]].triggerName == trigger)
            {
                // Capture data for the animation before removing
                TargetRpcPlayDiscardAnim(connectionToClient, currentHandIndices[i], i);
                currentHandIndices.RemoveAt(i);
                break;
            }
        }
    }

    [TargetRpc]
    private void TargetRpcPlayDiscardAnim(NetworkConnection target, int libIndex, int slotIndex)
    {
        // Calculate the exact starting X based on the slot it occupied
        float cWidth = 160f; float space = 15f;
        float totalW = (cWidth * 4) + (space * 3);
        float startX = (Screen.width / 2) - (totalW / 2) + (slotIndex * (cWidth + space));

        _activeDiscardAnims.Add(new DiscardAnim { 
            libIndex = libIndex, 
            startX = startX, 
            startTime = Time.time 
        });
    }

    private void OnGUI()
    {
        if (!isLocalPlayer) return;
        if (cardLibrary == null || cardLibrary.Count == 0) return;

        // Styles
        GUIStyle titleStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, fontStyle = FontStyle.Bold, fontSize = 20 };
        GUIStyle descStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.LowerCenter, fontSize = 11, wordWrap = true };
        titleStyle.normal.textColor = Color.yellow;
        descStyle.normal.textColor = Color.white;

        float cWidth = 160f; float cHeight = 100f; float space = 15f;
        float totalW = (cWidth * 4) + (space * 3);
        float startX = (Screen.width / 2) - (totalW / 2);
        
        // --- 1. DRAW ACTIVE HAND (With Breathing Animation) ---
        float hoverOffset = Mathf.Sin(Time.time * 2.5f) * 6f; 
        float pulseScale = 1.0f + (Mathf.Sin(Time.time * 4f) * 0.03f);
        float baseCenterY = Screen.height - cHeight - 60f + hoverOffset;

        for (int i = 0; i < currentHandIndices.Count; i++)
        {
            int index = currentHandIndices[i];
            if (index < 0 || index >= cardLibrary.Count) continue;

            CombatCard card = cardLibrary[index];
            float animW = cWidth * pulseScale;
            float animH = cHeight * pulseScale;
            float xOff = (animW - cWidth) / 2f;
            float yOff = (animH - cHeight) / 2f;

            Rect r = new Rect(startX + (i * (cWidth + space)) - xOff, baseCenterY - yOff, animW, animH);
            GUI.Box(r, ""); 
            GUI.Label(new Rect(r.x, r.y + 5, animW, 30), card.cardName, titleStyle);
            GUI.Label(new Rect(r.x + 5, r.y + 35, animW - 10, animH - 40), card.description, descStyle);
        }

        // --- 2. DRAW DISCARDING GHOSTS (Moving Out Animation) ---
        for (int i = _activeDiscardAnims.Count - 1; i >= 0; i--)
        {
            float elapsed = Time.time - _activeDiscardAnims[i].startTime;
            float duration = 0.5f; // Animation length

            if (elapsed > duration)
            {
                _activeDiscardAnims.RemoveAt(i);
                continue;
            }

            float t = elapsed / duration;
            // Slide up 150 pixels and fade to 0 transparency
            float slideUp = t * 150f;
            GUI.color = new Color(1, 1, 1, 1.0f - t); 

            CombatCard card = cardLibrary[_activeDiscardAnims[i].libIndex];
            Rect r = new Rect(_activeDiscardAnims[i].startX, (Screen.height - cHeight - 60f) - slideUp, cWidth, cHeight);
            
            GUI.Box(r, "");
            GUI.Label(new Rect(r.x, r.y + 5, cWidth, 30), card.cardName, titleStyle);
            GUI.Label(new Rect(r.x + 5, r.y + 35, cWidth - 10, cHeight - 40), card.description, descStyle);
        }
        GUI.color = Color.white; // Reset color for other UI
    }
}