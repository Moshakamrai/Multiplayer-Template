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
    
    // SyncList tells all clients which cards are in their hand
    public readonly SyncList<int> currentHandIndices = new SyncList<int>();

    public override void OnStartServer()
    {
        InitializeDeck();
    }

    [Server]
    public void InitializeDeck()
    {
        _deck.Clear();
        if (cardLibrary.Count == 0) return;

        // 16 cards total (2 of each move)
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
                currentHandIndices.RemoveAt(i);
                break;
            }
        }
    }

    // Inside CardManager.cs
    private void OnGUI()
    {
        // 1. Only draw for the local human player
        if (!isLocalPlayer || currentHandIndices.Count == 0) return;

        // 2. Safety check for the library
        if (cardLibrary == null || cardLibrary.Count == 0) return;

        // --- DIRECT GUI RENDERING ---
        float cWidth = 160f; float cHeight = 100f; float space = 15f;
        float totalW = (cWidth * 4) + (space * 3);
        float startX = (Screen.width / 2) - (totalW / 2);
        float startY = Screen.height - cHeight - 60f; // Moved up slightly

        // Style setup
        GUIStyle titleStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, fontStyle = FontStyle.Bold, fontSize = 20 };
        GUIStyle descStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.LowerCenter, fontSize = 11, wordWrap = true };
        titleStyle.normal.textColor = Color.yellow;
        descStyle.normal.textColor = Color.white;

        for (int i = 0; i < currentHandIndices.Count; i++)
        {
            // Safety check for indices
            int index = currentHandIndices[i];
            if (index < 0 || index >= cardLibrary.Count) continue;

            CombatCard card = cardLibrary[index];
            Rect r = new Rect(startX + (i * (cWidth + space)), startY, cWidth, cHeight);
            
            GUI.Box(r, ""); // Background box
            GUI.Label(new Rect(r.x, r.y + 5, cWidth, 30), card.cardName, titleStyle);
            GUI.Label(new Rect(r.x + 5, r.y + 35, cWidth - 10, 60), card.description, descStyle);
        }
    }
}