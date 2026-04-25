using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Mirror;

[System.Serializable]
public class CombatCard
{
    public string   cardName;
    public string   triggerName;
    public string   description;
    // Combo-only fields (ignored for normal cards)
    public bool     isCombo          = false;
    public int      comboChainLength = 0;
    public string[] comboAttacks     = new string[0];
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
    private GUIStyle  _titleStyle;
    private GUIStyle  _descStyle;
    private GUIStyle  _statusStyle;
    private GUIStyle  _badgeStyle;

    // Card dimensions
    const float cWidth  = 200f;
    const float cHeight = 230f;
    const float cSpace  = 18f;

    // Combo hand state (server only)
    private bool _comboHandDealt    = false;
    private int  _comboHandForChain = 0;

    /// <summary>True while a combo hand is active — callers should NOT try to refill the hand.</summary>
    public bool IsComboHandActive => _comboHandDealt;

    // ── Combo card definitions ─────────────────────────────────────────────
    // 4 cards × 4 chain lengths (2/3/4/5) = 16 entries injected at runtime.
    // Trigger names are always Combo1–Combo4; chain length distinguishes sets.
    private static CombatCard[] BuildComboCardDefs() => new[]
    {
        // 2-CHAIN
        new CombatCard { cardName="Combo 1", triggerName="Combo1", isCombo=true, comboChainLength=2,
            comboAttacks=new[]{"Jab","Cross"},
            description="Punch → Blast" },
        new CombatCard { cardName="Combo 2", triggerName="Combo2", isCombo=true, comboChainLength=2,
            comboAttacks=new[]{"Cross","Hook"},
            description="Blast → Hook" },
        new CombatCard { cardName="Combo 3", triggerName="Combo3", isCombo=true, comboChainLength=2,
            comboAttacks=new[]{"Hook","UnbreakablePunch"},
            description="Hook → Boom" },
        new CombatCard { cardName="Combo 4", triggerName="Combo4", isCombo=true, comboChainLength=2,
            comboAttacks=new[]{"Left","Cross"},
            description="Dodge L → Blast" },
        // 3-CHAIN
        new CombatCard { cardName="Combo 1", triggerName="Combo1", isCombo=true, comboChainLength=3,
            comboAttacks=new[]{"Jab","Jab","Cross"},
            description="Punch → Punch → Blast" },
        new CombatCard { cardName="Combo 2", triggerName="Combo2", isCombo=true, comboChainLength=3,
            comboAttacks=new[]{"Cross","Hook","UnbreakablePunch"},
            description="Blast → Hook → Boom" },
        new CombatCard { cardName="Combo 3", triggerName="Combo3", isCombo=true, comboChainLength=3,
            comboAttacks=new[]{"Left","Cross","Hook"},
            description="Dodge L → Blast → Hook" },
        new CombatCard { cardName="Combo 4", triggerName="Combo4", isCombo=true, comboChainLength=3,
            comboAttacks=new[]{"ParryIntent","Jab","Cross"},
            description="Cage → Punch → Blast" },
        // 4-CHAIN
        new CombatCard { cardName="Combo 1", triggerName="Combo1", isCombo=true, comboChainLength=4,
            comboAttacks=new[]{"Jab","Jab","Cross","Hook"},
            description="Punch→Punch→Blast→Hook" },
        new CombatCard { cardName="Combo 2", triggerName="Combo2", isCombo=true, comboChainLength=4,
            comboAttacks=new[]{"Left","Cross","Right","Cross"},
            description="DodgeL→Blast→DodgeR→Blast" },
        new CombatCard { cardName="Combo 3", triggerName="Combo3", isCombo=true, comboChainLength=4,
            comboAttacks=new[]{"Cross","Hook","Jab","UnbreakablePunch"},
            description="Blast→Hook→Punch→Boom" },
        new CombatCard { cardName="Combo 4", triggerName="Combo4", isCombo=true, comboChainLength=4,
            comboAttacks=new[]{"ParryIntent","Cross","Cross","Hook"},
            description="Cage→Blast→Blast→Hook" },
        // 5-CHAIN
        new CombatCard { cardName="Combo 1", triggerName="Combo1", isCombo=true, comboChainLength=5,
            comboAttacks=new[]{"Jab","Jab","Cross","Hook","UnbreakablePunch"},
            description="Punch→Punch→Blast→Hook→Boom" },
        new CombatCard { cardName="Combo 2", triggerName="Combo2", isCombo=true, comboChainLength=5,
            comboAttacks=new[]{"Left","Jab","Cross","Right","Cross"},
            description="DodgeL→Punch→Blast→DodgeR→Blast" },
        new CombatCard { cardName="Combo 3", triggerName="Combo3", isCombo=true, comboChainLength=5,
            comboAttacks=new[]{"Cross","Left","Cross","Right","Hook"},
            description="Blast→DodgeL→Blast→DodgeR→Hook" },
        new CombatCard { cardName="Combo 4", triggerName="Combo4", isCombo=true, comboChainLength=5,
            comboAttacks=new[]{"ParryIntent","Jab","Hook","Cross","UnbreakablePunch"},
            description="Cage→Punch→Hook→Blast→Boom" },
    };

    private void Awake()
    {
        // Inject combo cards into the library once per play session
        if (!cardLibrary.Exists(c => c.isCombo))
            foreach (var cc in BuildComboCardDefs()) cardLibrary.Add(cc);
    }

    public override void OnStartServer() { InitializeDeck(); }

    [Server]
    public void InitializeDeck()
    {
        _deck.Clear();
        if (cardLibrary.Count == 0) return;
        // Combo cards are never shuffled into the normal deck — they are dealt on demand
        for (int i = 0; i < cardLibrary.Count; i++)
            if (!cardLibrary[i].isCombo) { _deck.Add(cardLibrary[i]); _deck.Add(cardLibrary[i]); }
        _deck = _deck.OrderBy(x => Random.value).ToList();
    }

    [Server]
    public void DealInitialHand()
    {
        _comboHandDealt    = false;
        _comboHandForChain = 0;
        currentHandIndices.Clear();
        for (int i = 0; i < 4; i++) DrawCard();
    }

    [Server]
    public void DrawCard()
    {
        if (_comboHandDealt) { Debug.Log($"<color=orange>[CardManager]</color> DrawCard() blocked — combo hand is active for {gameObject.name}"); return; }
        if (currentHandIndices.Count >= 4) return;
        if (_deck.Count == 0) InitializeDeck();
        if (_deck.Count == 0) return;
        CombatCard drawn = _deck[0];
        _deck.RemoveAt(0);
        int libIndex = cardLibrary.FindIndex(c => c.triggerName == drawn.triggerName && !c.isCombo);
        currentHandIndices.Add(libIndex);
    }

    // Called every server frame to detect when an upcoming chain needs a combo hand swap
    private void Update()
    {
        if (!isServer) return;
        if (GetComponent<BotController>() != null) return;
        var rmm = RhythmRoundManager.Instance;
        if (rmm == null || !rmm.isRoundActive) return;

        int  nextCluster = rmm.GetNextClusterSize();
        bool isChain     = nextCluster >= 2;

        if (isChain && (!_comboHandDealt || _comboHandForChain != nextCluster))
        {
            Debug.Log($"<color=yellow>[CardManager]</color> Chain detected! nextCluster={nextCluster}, prev={_comboHandForChain} — swapping to combo hand for {gameObject.name}");
            _comboHandDealt    = true;
            _comboHandForChain = nextCluster;
            SwapToComboHand(nextCluster);
        }
        else if (!isChain && _comboHandDealt)
        {
            Debug.Log($"<color=cyan>[CardManager]</color> Chain ended (nextCluster={nextCluster}) — swapping back to normal hand for {gameObject.name}");
            _comboHandDealt    = false;
            _comboHandForChain = 0;
            SwapToNormalHand();
        }
    }

    [Server]
    private void SwapToComboHand(int chainLength)
    {
        currentHandIndices.Clear();
        for (int i = 0; i < cardLibrary.Count; i++)
        {
            var c = cardLibrary[i];
            if (c.isCombo && c.comboChainLength == chainLength)
                currentHandIndices.Add(i);
        }
        Debug.Log($"<color=yellow>[CardManager]</color> Combo hand dealt: {chainLength}× chain → {currentHandIndices.Count} cards for {gameObject.name}. Indices: [{string.Join(", ", currentHandIndices)}]");
        if (currentHandIndices.Count != 4)
            Debug.LogWarning($"[CardManager] Expected 4 combo cards for chainLength={chainLength} but got {currentHandIndices.Count}. Check Awake() combo card injection.");
    }

    [Server]
    private void SwapToNormalHand()
    {
        DealInitialHand();
        Debug.Log($"<color=cyan>[CardManager] Swapped back to normal hand for {gameObject.name}</color>");
    }

    public bool IsCardInHand(string trigger)
    {
        foreach (int index in currentHandIndices)
            if (index >= 0 && index < cardLibrary.Count && cardLibrary[index].triggerName == trigger) return true;
        return false;
    }

    // Returns the CombatCard for the first hand slot matching the trigger, or null
    public CombatCard GetCardInHand(string trigger)
    {
        foreach (int index in currentHandIndices)
            if (index >= 0 && index < cardLibrary.Count && cardLibrary[index].triggerName == trigger)
                return cardLibrary[index];
        return null;
    }

    [Server]
    public void DiscardCard(string trigger)
    {
        for (int i = 0; i < currentHandIndices.Count; i++)
        {
            if (cardLibrary[currentHandIndices[i]].triggerName != trigger) continue;

            // Combo cards never leave the hand — play the flash but keep the slot.
            // The hand stays until the chain length changes or the round ends.
            if (cardLibrary[currentHandIndices[i]].isCombo)
            {
                TargetRpcPlayDiscardAnim(connectionToClient, currentHandIndices[i], i);
                return;
            }

            TargetRpcPlayDiscardAnim(connectionToClient, currentHandIndices[i], i);
            currentHandIndices.RemoveAt(i);
            break;
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
        EnsureStyles();

        float totalW = (cWidth * 4) + (cSpace * 3);
        float startX = Screen.width / 2f - totalW / 2f;

        // --- Timing state ---
        var   rmm         = RhythmRoundManager.Instance;
        bool  isRhythm    = rmm != null && rmm.isRoundActive;
        float approachFrac = 0f;   // 0 = just after beat, 1 = shout now
        bool  isShout     = false;
        float pulse       = 0f;

        if (isRhythm)
        {
            float trackTime   = rmm.GetCurrentTrackTime();
            float nextBeat    = rmm.GetNextBeatTime();
            float timeToNext  = nextBeat > 0f ? nextBeat - trackTime : 999f;
            float windowTotal = (nextBeat - rmm.lastBeatFireTime) - 0.5f;
            float elapsed     = trackTime - rmm.lastBeatFireTime;
            float fillRatio   = windowTotal > 0.01f ? Mathf.Clamp01(1f - elapsed / windowTotal) : 0f;
            isShout       = timeToNext <= 0.5f && timeToNext >= -0.2f;
            approachFrac  = isShout ? 1f : (1f - fillRatio);
            pulse         = (Mathf.Sin(Time.time * 14f) + 1f) * 0.5f;
        }

        // --- Active hand ---
        float hoverAmp = isRhythm ? 2f : 5f;
        float hoverY   = Mathf.Sin(Time.time * 2.5f) * hoverAmp;
        float baseY    = Screen.height - cHeight - 60f + hoverY;

        for (int i = 0; i < currentHandIndices.Count; i++)
        {
            int index = currentHandIndices[i];
            if (index < 0 || index >= cardLibrary.Count) continue;
            Rect r = new Rect(startX + i * (cWidth + cSpace), baseY, cWidth, cHeight);
            DrawCard(r, cardLibrary[index], isRhythm, approachFrac, isShout, pulse, 1f);
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
            DrawCard(r, cardLibrary[_activeDiscardAnims[i].libIndex], false, 0f, false, 0f, 1f - t);
        }

        GUI.color = Color.white;
    }

    // approachFrac: 0 = just after beat fired, 1 = shout window active
    private void DrawCard(Rect r, CombatCard card,
                          bool isRhythm, float approachFrac, bool isShout, float pulse,
                          float alpha)
    {
        bool isCombo = card.isCombo;

        // ── 1. Background ─────────────────────────────────────────────────
        Color bgBase = isCombo
            ? new Color(0.09f, 0.05f, 0.01f, 0.93f * alpha)
            : new Color(0.04f, 0.04f, 0.09f, 0.90f * alpha);
        if (isShout)
            bgBase = Color.Lerp(bgBase,
                isCombo ? new Color(0.18f, 0.11f, 0f,    0.93f * alpha)
                        : new Color(0.03f, 0.12f, 0.15f, 0.90f * alpha),
                pulse * 0.7f);
        GUI.color = bgBase;
        GUI.DrawTexture(r, _whiteTex);

        // ── 2. Top charge strip ───────────────────────────────────────────
        // Thin horizontal bar at the very top that lights up as beat approaches
        if (isRhythm)
        {
            float chargeAlpha = Mathf.Lerp(0.15f, 0.95f, approachFrac) * alpha;
            Color chargeCol   = isShout
                ? Color.Lerp(
                    isCombo ? new Color(1f, 0.7f, 0f)  : new Color(0.1f, 1f, 0.45f),
                    Color.white, pulse * 0.45f)
                : isCombo ? new Color(1f, 0.6f, 0f)    : new Color(0f, 0.85f, 1f);
            GUI.color = new Color(chargeCol.r, chargeCol.g, chargeCol.b, chargeAlpha);
            GUI.DrawTexture(new Rect(r.x, r.y, r.width * approachFrac, 3f), _whiteTex);
        }

        // ── 3. Approach bar (bottom of card) ──────────────────────────────
        if (isRhythm)
        {
            const float barPad    = 10f;
            const float barH      = 18f;
            const float shoutFrac = 0.20f;   // rightmost 20% = shout zone
            float barW = r.width - barPad * 2f;
            float barX = r.x  + barPad;
            float barY = r.y  + r.height - barH - 8f;

            // Track bg
            GUI.color = new Color(0f, 0f, 0f, 0.55f * alpha);
            GUI.DrawTexture(new Rect(barX, barY, barW, barH), _whiteTex);

            // Shout zone tint
            GUI.color = isCombo
                ? new Color(1f, 0.65f, 0f,  0.28f * alpha)
                : new Color(0f, 1f,    0.3f, 0.28f * alpha);
            GUI.DrawTexture(new Rect(barX + barW * (1f - shoutFrac), barY, barW * shoutFrac, barH), _whiteTex);

            // Moving fill  cyan → yellow → neon-green/gold as it approaches
            Color fillCol;
            if (isShout)
                fillCol = Color.Lerp(
                    isCombo ? new Color(1f, 0.7f,  0f,   0.92f * alpha)
                            : new Color(0.1f, 1f, 0.45f, 0.92f * alpha),
                    Color.white, pulse * 0.35f);
            else if (approachFrac > 0.72f)
                fillCol = Color.Lerp(
                    new Color(1f, 0.8f, 0.0f, 0.80f * alpha),
                    isCombo ? new Color(1f, 0.65f, 0f,   0.90f * alpha)
                            : new Color(0.1f, 1f, 0.4f, 0.90f * alpha),
                    (approachFrac - 0.72f) / 0.28f);
            else
                fillCol = new Color(0f, 0.85f, 1f, 0.70f * alpha);

            GUI.color = fillCol;
            GUI.DrawTexture(new Rect(barX, barY, barW * approachFrac, barH), _whiteTex);

            // Bar border
            GUI.color = new Color(1f, 1f, 1f, 0.18f * alpha);
            GUI.DrawTexture(new Rect(barX,        barY,        barW,  1.5f), _whiteTex);
            GUI.DrawTexture(new Rect(barX,        barY + barH, barW,  1.5f), _whiteTex);
            GUI.DrawTexture(new Rect(barX,        barY,        1.5f,  barH), _whiteTex);
            GUI.DrawTexture(new Rect(barX + barW, barY,        1.5f,  barH), _whiteTex);
        }

        // ── 4. Corner bracket borders ─────────────────────────────────────
        float glowT  = isRhythm ? approachFrac : 0f;
        float bLen   = isShout ? Mathf.Lerp(24f, 30f, pulse) : 22f;
        float bThick = isShout ? Mathf.Lerp(2f,  3.5f, pulse) : 2f;

        Color bracketBase = isCombo ? new Color(1f, 0.75f, 0f, alpha)  : new Color(0.85f, 0f, 1f, alpha);
        Color bracketHot  = isCombo ? new Color(1f, 0.92f, 0.3f, alpha): new Color(0.6f, 0f, 1f, alpha);
        if (isShout) bracketBase = Color.Lerp(bracketBase, Color.white, pulse * 0.45f);
        Color bCol = Color.Lerp(bracketBase, bracketHot, glowT);
        GUI.color = new Color(bCol.r, bCol.g, bCol.b, bCol.a * (0.55f + glowT * 0.45f));

        // top-left
        GUI.DrawTexture(new Rect(r.x,              r.y,       bLen,   bThick), _whiteTex);
        GUI.DrawTexture(new Rect(r.x,              r.y,       bThick, bLen),   _whiteTex);
        // top-right
        GUI.DrawTexture(new Rect(r.x + r.width - bLen, r.y,   bLen,   bThick), _whiteTex);
        GUI.DrawTexture(new Rect(r.x + r.width - bThick, r.y, bThick, bLen),   _whiteTex);
        // bottom-left
        GUI.DrawTexture(new Rect(r.x,              r.y + r.height - bThick, bLen,   bThick), _whiteTex);
        GUI.DrawTexture(new Rect(r.x,              r.y + r.height - bLen,   bThick, bLen),   _whiteTex);
        // bottom-right
        GUI.DrawTexture(new Rect(r.x + r.width - bLen,   r.y + r.height - bThick, bLen,   bThick), _whiteTex);
        GUI.DrawTexture(new Rect(r.x + r.width - bThick, r.y + r.height - bLen,   bThick, bLen),   _whiteTex);

        // ── 5. Text ───────────────────────────────────────────────────────
        GUI.color = Color.white;

        // Card name
        _titleStyle.normal.textColor = isCombo
            ? new Color(1f, 0.88f, 0.22f, alpha)
            : new Color(1f,  1f,   1f,    alpha);
        GUI.Label(new Rect(r.x, r.y + 10f, r.width, 38f), card.cardName, _titleStyle);

        // Description (middle)
        _descStyle.normal.textColor = isCombo
            ? new Color(1f,   0.78f, 0.35f, 0.88f * alpha)
            : new Color(0.72f, 0.88f, 1f,   0.82f * alpha);
        GUI.Label(new Rect(r.x + 6f, r.y + 58f, r.width - 12f, 60f), card.description, _descStyle);

        // State overlay: WAIT / GET READY / !! SHOUT !!
        if (isRhythm)
        {
            string stateText;
            Color  stateCol;
            if (isShout)
            {
                stateText = "!! SHOUT !!";
                stateCol  = isCombo
                    ? new Color(1f, 0.85f, 0f,   (0.78f + pulse * 0.22f) * alpha)
                    : new Color(0.2f, 1f, 0.5f,  (0.78f + pulse * 0.22f) * alpha);
            }
            else if (approachFrac > 0.68f)
            {
                stateText = "GET READY";
                float ramp = (approachFrac - 0.68f) / 0.32f;
                stateCol  = new Color(1f, Mathf.Lerp(0.65f, 0.9f, ramp), 0.1f, Mathf.Lerp(0.5f, 0.9f, ramp) * alpha);
            }
            else
            {
                stateText = "WAIT";
                stateCol  = new Color(1f, 1f, 1f, 0.18f * alpha);
            }
            _statusStyle.normal.textColor = stateCol;
            GUI.Label(new Rect(r.x, r.y + r.height - 56f, r.width, 24f), stateText, _statusStyle);
        }

        // Chain length badge — top-right
        if (isCombo)
        {
            _badgeStyle.normal.textColor = new Color(1f, 0.75f, 0f, alpha);
            GUI.Label(new Rect(r.x, r.y + 5f, r.width - 6f, 22f), $"{card.comboChainLength}×", _badgeStyle);
        }
    }

    private void EnsureStyles()
    {
        if (_titleStyle != null) return;
        _titleStyle  = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter,  fontStyle = FontStyle.Bold, fontSize = 22 };
        _descStyle   = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter,  fontSize  = 13, wordWrap = true };
        _statusStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 13 };
        _badgeStyle  = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperRight,   fontStyle = FontStyle.Bold, fontSize = 14 };
    }
}
