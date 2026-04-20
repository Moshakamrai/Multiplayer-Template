using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Mirror;

[System.Serializable]
public class CombatCard
{
    public string cardName;
    public string triggerName;
    public string description;
}

public class CardManager : NetworkBehaviour
{
    public List<CombatCard> cardLibrary = new List<CombatCard>();
    private List<CombatCard> _deck = new List<CombatCard>();

    public readonly SyncList<int> currentHandIndices = new SyncList<int>();

    private struct DiscardAnim
    {
        public int libIndex;
        public float startX;
        public float startTime;
    }
    private List<DiscardAnim> _activeDiscardAnims = new List<DiscardAnim>();

    private struct CardFlash
    {
        public float startX;
        public float baseY;
        public float startTime;
    }
    private List<CardFlash> _activeCardFlashes = new List<CardFlash>();

    private Texture2D _whiteTex;

    // Card dimensions
    const float cWidth  = 160f;
    const float cHeight = 180f;
    const float cSpace  = 15f;

    public override void OnStartServer() { InitializeDeck(); }

    [Server]
    public void InitializeDeck()
    {
        _deck.Clear();
        if (cardLibrary.Count == 0) return;
        for (int i = 0; i < cardLibrary.Count; i++) { _deck.Add(cardLibrary[i]); _deck.Add(cardLibrary[i]); }
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
                TargetRpcPlayDiscardAnim(connectionToClient, currentHandIndices[i], i);
                currentHandIndices.RemoveAt(i);
                break;
            }
        }
    }

    [TargetRpc]
    private void TargetRpcPlayDiscardAnim(NetworkConnection target, int libIndex, int slotIndex)
    {
        float totalW = (cWidth * 4) + (cSpace * 3);
        float startX = Screen.width / 2f - totalW / 2f + slotIndex * (cWidth + cSpace);
        float baseY = Screen.height - cHeight - 60f;
        _activeCardFlashes.Add(new CardFlash { startX = startX, baseY = baseY, startTime = Time.time });
        _activeDiscardAnims.Add(new DiscardAnim { libIndex = libIndex, startX = startX, startTime = Time.time });
    }

    private void OnGUI()
    {
        if (!isLocalPlayer) return;
        if (cardLibrary == null || cardLibrary.Count == 0) return;

        if (_whiteTex == null) { _whiteTex = new Texture2D(1, 1); _whiteTex.SetPixel(0, 0, Color.white); _whiteTex.Apply(); }

        GUIStyle titleStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.UpperCenter,
            fontStyle = FontStyle.Bold,
            fontSize = 22
        };
        GUIStyle descStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.LowerCenter,
            fontSize = 11,
            wordWrap = true
        };

        float totalW = (cWidth * 4) + (cSpace * 3);
        float startX = Screen.width / 2f - totalW / 2f;

        // --- Timing state ---
        var rmm = RhythmRoundManager.Instance;
        bool isRhythm = rmm != null && rmm.isRoundActive;
        float fillRatio  = 1f;
        bool  isShout    = false;
        float pulse      = 0f;

        if (isRhythm)
        {
            float trackTime  = rmm.GetCurrentTrackTime();
            float nextBeat   = rmm.GetNextBeatTime();
            float timeToNext = nextBeat > 0f ? nextBeat - trackTime : 999f;
            float windowTotal = (nextBeat - rmm.lastBeatFireTime) - 0.5f;
            float elapsed     = trackTime - rmm.lastBeatFireTime;
            fillRatio = windowTotal > 0.01f ? Mathf.Clamp01(1f - elapsed / windowTotal) : 0f;
            isShout   = timeToNext <= 0.5f && timeToNext >= -0.2f;
            pulse     = (Mathf.Sin(Time.time * 14f) + 1f) * 0.5f;
        }

        // --- Active hand ---
        float hoverAmp  = isRhythm ? 3f : 6f;
        float hoverY    = Mathf.Sin(Time.time * 2.5f) * hoverAmp;
        float baseY     = Screen.height - cHeight - 60f + hoverY;

        for (int i = 0; i < currentHandIndices.Count; i++)
        {
            int index = currentHandIndices[i];
            if (index < 0 || index >= cardLibrary.Count) continue;
            Rect r = new Rect(startX + i * (cWidth + cSpace), baseY, cWidth, cHeight);
            DrawCard(r, cardLibrary[index], isRhythm, fillRatio, isShout, pulse, titleStyle, descStyle, 1f);
        }

        // --- Card use flash ---
        for (int i = _activeCardFlashes.Count - 1; i >= 0; i--)
        {
            float t = (Time.time - _activeCardFlashes[i].startTime) / 0.12f;
            if (t > 1f) { _activeCardFlashes.RemoveAt(i); continue; }
            Rect r = new Rect(_activeCardFlashes[i].startX, _activeCardFlashes[i].baseY, cWidth, cHeight);
            GUI.color = new Color(1f, 1f, 1f, (1f - t) * 0.85f);
            GUI.DrawTexture(r, _whiteTex);
        }

        // --- Discard ghosts ---
        for (int i = _activeDiscardAnims.Count - 1; i >= 0; i--)
        {
            float t = (Time.time - _activeDiscardAnims[i].startTime) / 0.5f;
            if (t > 1f) { _activeDiscardAnims.RemoveAt(i); continue; }
            Rect r = new Rect(_activeDiscardAnims[i].startX,
                              Screen.height - cHeight - 60f - t * 150f,
                              cWidth, cHeight);
            DrawCard(r, cardLibrary[_activeDiscardAnims[i].libIndex], false, 1f, false, 0f, titleStyle, descStyle, 1f - t);
        }

        GUI.color = Color.white;
    }

    private void DrawCard(Rect r, CombatCard card,
                          bool isRhythm, float fillRatio, bool isShout, float pulse,
                          GUIStyle titleStyle, GUIStyle descStyle, float alpha)
    {
        // 1. Dark base
        GUI.color = new Color(0.06f, 0.06f, 0.1f, 0.88f * alpha);
        GUI.DrawTexture(r, _whiteTex);

        if (isRhythm)
        {
            // 2. Timing fill — full card width, depletes from right to left
            Color fillColor;
            if (isShout)
                fillColor = Color.Lerp(new Color(1f, 0.15f, 0f, 0.82f), new Color(1f, 0.6f, 0.1f, 0.82f), pulse);
            else if (fillRatio < 0.25f)
                fillColor = new Color(1f, 0.8f, 0f, 0.72f);   // yellow
            else
                fillColor = new Color(0f, 0.85f, 1f, 0.62f);  // cyan

            float fillH = isShout ? r.height : r.height * fillRatio;
            GUI.color = fillColor;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, fillH), _whiteTex);

            if (isShout)
            {
                // 3b. Pulsing ring in center — signals SHOUT NOW
                float sz = 28f + pulse * 16f;
                float cx = r.x + r.width  * 0.5f - sz * 0.5f;
                float cy = r.y + r.height * 0.5f - sz * 0.5f;
                GUI.color = new Color(0f, 0f, 0f, 0.9f);
                GUI.DrawTexture(new Rect(cx, cy, sz, sz), _whiteTex);
                float inner = sz * 0.42f;
                GUI.color = new Color(1f, 1f, 1f, 0.65f + pulse * 0.35f);
                GUI.DrawTexture(new Rect(cx + (sz - inner) * 0.5f, cy + (sz - inner) * 0.5f, inner, inner), _whiteTex);
            }
        }

        // 4. Neon magenta border
        GUI.color = new Color(0.8f, 0f, 1f, 0.72f * alpha);
        GUI.DrawTexture(new Rect(r.x - 2f,       r.y,            2f,           r.height),      _whiteTex);
        GUI.DrawTexture(new Rect(r.x + r.width,  r.y,            2f,           r.height),      _whiteTex);
        GUI.DrawTexture(new Rect(r.x - 2f,       r.y - 2f,       r.width + 4f, 2f),            _whiteTex);
        GUI.DrawTexture(new Rect(r.x - 2f,       r.y + r.height, r.width + 4f, 2f),            _whiteTex);

        // 5. Text — always white so it reads over any fill colour
        titleStyle.normal.textColor = new Color(1f, 1f, 1f, alpha);
        descStyle.normal.textColor  = new Color(0.88f, 0.88f, 0.88f, alpha);
        GUI.color = Color.white;
        GUI.Label(new Rect(r.x,     r.y + 10,           r.width,      36),          card.cardName,    titleStyle);
        GUI.Label(new Rect(r.x + 5, r.y + r.height - 48, r.width - 10, 42),         card.description, descStyle);
    }
}
