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
    public bool     isCombo          = false;
    public int      comboChainLength = 0;
    public string[] comboAttacks     = new string[0];
}

public class CardManager : NetworkBehaviour
{
    public List<CombatCard> cardLibrary = new List<CombatCard>();

    // Used only for combo hand — normal cards are always available
    public readonly SyncList<int> currentHandIndices = new SyncList<int>();

    // ── Slot system ────────────────────────────────────────────────────────
    [SyncVar] public int attackSlotsTotal      = 4;
    [SyncVar] public int defenseSlotTotal      = 2;
    [SyncVar] public int attackSlotsRemaining  = 4;
    [SyncVar] public int defenseSlotsRemaining = 2;

    // Client-side config UI (not synced)
    private int _cfgAttack  = 4;
    private int _cfgDefense = 2;

    // ── Classification ─────────────────────────────────────────────────────
    private static readonly HashSet<string> _atkSet = new HashSet<string>
        { "Jab", "Cross", "Hook", "UnbreakablePunch" };
    private static readonly HashSet<string> _defSet = new HashSet<string>
        { "Block", "ParryIntent", "Left", "Right" };

    public static bool IsAttackTrigger(string t)  => _atkSet.Contains(t);
    public static bool IsDefenseTrigger(string t) => _defSet.Contains(t);
    private static bool IsComboAttack(CombatCard c) =>
        c.comboAttacks.Length > 0 && _atkSet.Contains(c.comboAttacks[0]);

    // ── Animation state ────────────────────────────────────────────────────
    private struct DiscardAnim { public int libIndex; public float startX; public float startTime; }
    private struct CardFlash   { public float startX; public float baseY;  public float startTime; }
    private List<DiscardAnim> _activeDiscardAnims = new List<DiscardAnim>();
    private List<CardFlash>   _activeCardFlashes  = new List<CardFlash>();

    // ── GUI resources ──────────────────────────────────────────────────────
    private Texture2D  _whiteTex;
    private GUIStyle   _titleStyle;
    private GUIStyle   _descStyle;
    private GUIStyle   _statusStyle;
    private GUIStyle   _badgeStyle;
    private CardManager _oppCardsCache;

    // Combo-hand cards use full size; normal cards use compact size
    const float cWidth  = 240f;
    const float cHeight = 275f;
    const float cSpace  = 18f;
    const float nWidth  = 170f;  // normal mode card
    const float nHeight = 200f;
    const float nSpace  = 14f;

    // ── Combo-hand state (server only) ─────────────────────────────────────
    private bool _comboHandDealt    = false;
    private int  _comboHandForChain = 0;

    public bool IsComboHandActive => _comboHandDealt;

    // ── Combo card definitions — ALL combos are pure attack OR pure defense ─
    private static CombatCard[] BuildComboCardDefs() => new[]
    {
        // 2-CHAIN — 2 attack, 2 defense
        new CombatCard { cardName="Combo 1", triggerName="Combo1", isCombo=true, comboChainLength=2,
            comboAttacks=new[]{"Jab","Cross"},          description="Punch → Blast" },
        new CombatCard { cardName="Combo 2", triggerName="Combo2", isCombo=true, comboChainLength=2,
            comboAttacks=new[]{"Cross","Hook"},          description="Blast → Hook" },
        new CombatCard { cardName="Combo 3", triggerName="Combo3", isCombo=true, comboChainLength=2,
            comboAttacks=new[]{"Block","ParryIntent"},   description="Guard → Cage" },
        new CombatCard { cardName="Combo 4", triggerName="Combo4", isCombo=true, comboChainLength=2,
            comboAttacks=new[]{"Left","Right"},          description="Dodge L → Dodge R" },
        // 3-CHAIN — 2 attack, 2 defense
        new CombatCard { cardName="Combo 1", triggerName="Combo1", isCombo=true, comboChainLength=3,
            comboAttacks=new[]{"Jab","Cross","Hook"},                description="Punch → Blast → Hook" },
        new CombatCard { cardName="Combo 2", triggerName="Combo2", isCombo=true, comboChainLength=3,
            comboAttacks=new[]{"Cross","Hook","UnbreakablePunch"},   description="Blast → Hook → Boom" },
        new CombatCard { cardName="Combo 3", triggerName="Combo3", isCombo=true, comboChainLength=3,
            comboAttacks=new[]{"ParryIntent","Block","Left"},        description="Cage → Guard → Dodge L" },
        new CombatCard { cardName="Combo 4", triggerName="Combo4", isCombo=true, comboChainLength=3,
            comboAttacks=new[]{"Left","Right","Block"},              description="Dodge L → Dodge R → Guard" },
        // 4-CHAIN — 2 attack, 2 defense
        new CombatCard { cardName="Combo 1", triggerName="Combo1", isCombo=true, comboChainLength=4,
            comboAttacks=new[]{"Jab","Jab","Cross","Hook"},               description="Punch→Punch→Blast→Hook" },
        new CombatCard { cardName="Combo 2", triggerName="Combo2", isCombo=true, comboChainLength=4,
            comboAttacks=new[]{"Cross","Hook","Jab","UnbreakablePunch"},  description="Blast→Hook→Punch→Boom" },
        new CombatCard { cardName="Combo 3", triggerName="Combo3", isCombo=true, comboChainLength=4,
            comboAttacks=new[]{"Left","Block","Right","ParryIntent"},     description="DodgeL→Guard→DodgeR→Cage" },
        new CombatCard { cardName="Combo 4", triggerName="Combo4", isCombo=true, comboChainLength=4,
            comboAttacks=new[]{"ParryIntent","Left","Block","Right"},     description="Cage→DodgeL→Guard→DodgeR" },
        // 5-CHAIN — 2 attack, 2 defense
        new CombatCard { cardName="Combo 1", triggerName="Combo1", isCombo=true, comboChainLength=5,
            comboAttacks=new[]{"Jab","Jab","Cross","Hook","UnbreakablePunch"},   description="Punch→Punch→Blast→Hook→Boom" },
        new CombatCard { cardName="Combo 2", triggerName="Combo2", isCombo=true, comboChainLength=5,
            comboAttacks=new[]{"Jab","Cross","Cross","Hook","UnbreakablePunch"}, description="Punch→Blast→Blast→Hook→Boom" },
        new CombatCard { cardName="Combo 3", triggerName="Combo3", isCombo=true, comboChainLength=5,
            comboAttacks=new[]{"Left","Right","Block","ParryIntent","Left"},     description="DodgeL→DodgeR→Guard→Cage→DodgeL" },
        new CombatCard { cardName="Combo 4", triggerName="Combo4", isCombo=true, comboChainLength=5,
            comboAttacks=new[]{"ParryIntent","Block","Left","Right","Block"},    description="Cage→Guard→DodgeL→DodgeR→Guard" },
    };

    private static CombatCard[] BuildNormalCardDefs() => new[]
    {
        new CombatCard { cardName="Block", triggerName="Block",            description="Standard defense. Wider timing window to negate damage." },
        new CombatCard { cardName="Cage",  triggerName="ParryIntent",      description="Elite Parry. Reflects 120% damage back to the attacker." },
        new CombatCard { cardName="Left",  triggerName="Left",             description="Quick dodge to the left. Evades Jabs and Hooks." },
        new CombatCard { cardName="Right", triggerName="Right",            description="Quick dodge to the right. Evades Jabs and Hooks." },
        new CombatCard { cardName="Punch", triggerName="Jab",              description="Quick lead strike. Reliable base damage." },
        new CombatCard { cardName="Blast", triggerName="Cross",            description="Straight power hit. Counters enemies trying to dodge." },
        new CombatCard { cardName="Hook",  triggerName="Hook",             description="Heavy side-swing. High damage and grants bonus Energy." },
        new CombatCard { cardName="Boom",  triggerName="UnbreakablePunch", description="Unstoppable. Ignores blocks and deals massive 15-25 dmg." },
    };

    private void Awake()
    {
        if (!cardLibrary.Exists(c => !c.isCombo))
            foreach (var nc in BuildNormalCardDefs()) cardLibrary.Add(nc);
        if (!cardLibrary.Exists(c => c.isCombo))
            foreach (var cc in BuildComboCardDefs()) cardLibrary.Add(cc);
    }

    // ── Slot API ───────────────────────────────────────────────────────────

    public bool HasSlot(string trigger)
    {
        if (trigger.StartsWith("Combo"))
        {
            CombatCard card = GetCardInHand(trigger);
            if (card == null) return false;
            return IsComboAttack(card) ? attackSlotsRemaining > 0 : defenseSlotsRemaining > 0;
        }
        if (IsAttackTrigger(trigger))  return attackSlotsRemaining > 0;
        if (IsDefenseTrigger(trigger)) return defenseSlotsRemaining > 0;
        return false;
    }

    [Server]
    public void ConsumeSlot(string trigger)
    {
        bool isAttack;
        if (trigger.StartsWith("Combo"))
        {
            CombatCard card = GetCardInHand(trigger);
            isAttack = card != null && IsComboAttack(card);
        }
        else isAttack = IsAttackTrigger(trigger);

        if (isAttack) attackSlotsRemaining  = Mathf.Max(0, attackSlotsRemaining  - 1);
        else          defenseSlotsRemaining = Mathf.Max(0, defenseSlotsRemaining - 1);
    }

    [Server]
    public void TryRefillSlots()
    {
        if (attackSlotsRemaining == 0 && defenseSlotsRemaining == 0)
        {
            attackSlotsRemaining  = attackSlotsTotal;
            defenseSlotsRemaining = defenseSlotTotal;
        }
    }

    [Server]
    public void ResetSlots()
    {
        attackSlotsRemaining  = attackSlotsTotal;
        defenseSlotsRemaining = defenseSlotTotal;
    }

    [Command]
    public void CmdSetSlots(int atk, int def)
    {
        attackSlotsTotal      = Mathf.Clamp(atk, 1, 5);
        defenseSlotTotal      = Mathf.Clamp(def, 1, 5);
        attackSlotsRemaining  = attackSlotsTotal;
        defenseSlotsRemaining = defenseSlotTotal;
    }

    // ── Card availability API ──────────────────────────────────────────────

    public bool IsCardInHand(string trigger)
    {
        if (trigger.StartsWith("Combo"))
        {
            foreach (int index in currentHandIndices)
                if (index >= 0 && index < cardLibrary.Count && cardLibrary[index].triggerName == trigger)
                    return true;
            return false;
        }
        return HasSlot(trigger);
    }

    public CombatCard GetCardInHand(string trigger)
    {
        if (trigger.StartsWith("Combo"))
        {
            foreach (int index in currentHandIndices)
                if (index >= 0 && index < cardLibrary.Count && cardLibrary[index].triggerName == trigger)
                    return cardLibrary[index];
            return null;
        }
        return cardLibrary.Find(c => c.triggerName == trigger && !c.isCombo);
    }

    [Server]
    public void DiscardCard(string trigger)
    {
        int libIndex;
        if (trigger.StartsWith("Combo"))
        {
            libIndex = -1;
            foreach (int idx in currentHandIndices)
                if (cardLibrary[idx].triggerName == trigger) { libIndex = idx; break; }
        }
        else
        {
            libIndex = cardLibrary.FindIndex(c => c.triggerName == trigger && !c.isCombo);
        }

        if (libIndex >= 0 && connectionToClient != null)
            TargetRpcPlayDiscardAnim(connectionToClient, libIndex);

        ConsumeSlot(trigger);
    }

    [TargetRpc]
    private void TargetRpcPlayDiscardAnim(NetworkConnection target, int libIndex)
    {
        if (libIndex < 0 || libIndex >= cardLibrary.Count) return;
        float startX = GetCardScreenX(cardLibrary[libIndex].triggerName);
        float baseY  = Screen.height - nHeight - 60f;
        _activeCardFlashes.Add(new CardFlash { startX = startX, baseY = baseY, startTime = Time.time });
        _activeDiscardAnims.Add(new DiscardAnim { libIndex = libIndex, startX = startX, startTime = Time.time });
    }

    private float GetCardScreenX(string trigger)
    {
        var defCards = new[] { "Block", "ParryIntent", "Left", "Right" };
        var atkCards = new[] { "Jab", "Cross", "Hook", "UnbreakablePunch" };
        float gap       = 40f;
        float groupW    = nWidth * 4 + nSpace * 3;
        float defStartX = Screen.width / 2f - gap / 2f - groupW;
        float atkStartX = Screen.width / 2f + gap / 2f;

        for (int i = 0; i < atkCards.Length; i++)
            if (atkCards[i] == trigger) return atkStartX + i * (nWidth + nSpace);
        for (int i = 0; i < defCards.Length; i++)
            if (defCards[i] == trigger) return defStartX + i * (nWidth + nSpace);

        // Combo cards — centered
        float comboGroupW = cWidth * 4 + cSpace * 3;
        float comboStart  = Screen.width / 2f - comboGroupW / 2f;
        for (int i = 0; i < currentHandIndices.Count; i++)
            if (currentHandIndices[i] >= 0 && cardLibrary[currentHandIndices[i]].triggerName == trigger)
                return comboStart + i * (cWidth + cSpace);

        return Screen.width / 2f;
    }

    // ── Combo-hand management ──────────────────────────────────────────────

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
            _comboHandDealt    = true;
            _comboHandForChain = nextCluster;
            SwapToComboHand(nextCluster);
        }
        else if (!isChain && _comboHandDealt)
        {
            _comboHandDealt    = false;
            _comboHandForChain = 0;
            currentHandIndices.Clear();
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
        Debug.Log($"<color=yellow>[CardManager]</color> Combo hand ({chainLength}×) → {currentHandIndices.Count} cards for {gameObject.name}");
        if (currentHandIndices.Count != 4)
            Debug.LogWarning($"[CardManager] Expected 4 combo cards for chainLength={chainLength}, got {currentHandIndices.Count}.");
    }

    // ── OnGUI ──────────────────────────────────────────────────────────────

    private void OnGUI()
    {
        if (!isLocalPlayer) return;
        if (cardLibrary == null || cardLibrary.Count == 0) return;

        if (_whiteTex == null) { _whiteTex = new Texture2D(1, 1); _whiteTex.SetPixel(0, 0, Color.white); _whiteTex.Apply(); }
        EnsureStyles();

        var   rmm        = RhythmRoundManager.Instance;
        bool  isRhythm   = rmm != null && rmm.isRoundActive;

        float approachFrac = 0f;
        bool  isShout      = false;
        float pulse        = 0f;

        if (isRhythm)
        {
            float trackTime   = rmm.GetCurrentTrackTime();
            float nextBeat    = rmm.GetNextBeatTime();
            float timeToNext  = nextBeat > 0f ? nextBeat - trackTime : 999f;
            float windowTotal = (nextBeat - rmm.lastBeatFireTime) - 0.5f;
            float elapsed     = trackTime - rmm.lastBeatFireTime;
            float fillRatio   = windowTotal > 0.01f ? Mathf.Clamp01(1f - elapsed / windowTotal) : 0f;
            isShout      = timeToNext <= 0.5f && timeToNext >= -0.2f;
            approachFrac = isShout ? 1f : (1f - fillRatio);
            pulse        = (Mathf.Sin(Time.time * 14f) + 1f) * 0.5f;
        }

        float hoverAmp = isRhythm ? 2f : 5f;
        float hoverY   = Mathf.Sin(Time.time * 2.5f) * hoverAmp;

        if (_comboHandDealt)
        {
            // ── Combo mode: 4 combo cards centered ──
            float baseY  = Screen.height - cHeight - 60f + hoverY;
            float totalW = cWidth * 4 + cSpace * 3;
            float startX = Screen.width / 2f - totalW / 2f;

            for (int i = 0; i < currentHandIndices.Count; i++)
            {
                int idx = currentHandIndices[i];
                if (idx < 0 || idx >= cardLibrary.Count) continue;
                bool slotAvail = HasSlot(cardLibrary[idx].triggerName);
                Rect r = new Rect(startX + i * (cWidth + cSpace), baseY, cWidth, cHeight);
                DrawCard(r, cardLibrary[idx], isRhythm, approachFrac, isShout, pulse, slotAvail ? 1f : 0.30f);
            }
        }
        else
        {
            // ── Normal mode: defense LEFT, attack RIGHT ──
            float baseY     = Screen.height - nHeight - 60f + hoverY;
            float gap       = 40f;
            float groupW    = nWidth * 4 + nSpace * 3;
            float defStartX = Screen.width / 2f - gap / 2f - groupW;
            float atkStartX = Screen.width / 2f + gap / 2f;

            var defCards = new[] { "Block", "ParryIntent", "Left", "Right" };
            var atkCards = new[] { "Jab", "Cross", "Hook", "UnbreakablePunch" };

            for (int i = 0; i < defCards.Length; i++)
            {
                int libIdx = cardLibrary.FindIndex(c => c.triggerName == defCards[i] && !c.isCombo);
                if (libIdx < 0) continue;
                float alpha = defenseSlotsRemaining > 0 ? 1f : 0.30f;
                Rect r = new Rect(defStartX + i * (nWidth + nSpace), baseY, nWidth, nHeight);
                DrawCard(r, cardLibrary[libIdx], isRhythm, approachFrac, isShout, pulse, alpha);
            }

            for (int i = 0; i < atkCards.Length; i++)
            {
                int libIdx = cardLibrary.FindIndex(c => c.triggerName == atkCards[i] && !c.isCombo);
                if (libIdx < 0) continue;
                float alpha = attackSlotsRemaining > 0 ? 1f : 0.30f;
                Rect r = new Rect(atkStartX + i * (nWidth + nSpace), baseY, nWidth, nHeight);
                DrawCard(r, cardLibrary[libIdx], isRhythm, approachFrac, isShout, pulse, alpha);
            }

            // Slot pip indicators above each group
            float pipY = Screen.height - nHeight - 60f + hoverY - 36f;
            DrawSlotPips(defStartX, pipY, defenseSlotsRemaining, defenseSlotTotal, false);
            DrawSlotPips(atkStartX, pipY, attackSlotsRemaining,  attackSlotsTotal,  true);

            // Config UI when round is not active
            if (!isRhythm) DrawSlotConfigUI();
        }

        // Opponent slot display
        if (isRhythm) DrawOpponentSlots();

        // ── Card-use flash ──
        for (int i = _activeCardFlashes.Count - 1; i >= 0; i--)
        {
            float t = (Time.time - _activeCardFlashes[i].startTime) / 0.12f;
            if (t > 1f) { _activeCardFlashes.RemoveAt(i); continue; }
            Rect r = new Rect(_activeCardFlashes[i].startX, _activeCardFlashes[i].baseY, nWidth, nHeight);
            GUI.color = new Color(1f, 1f, 1f, (1f - t) * 0.85f);
            GUI.DrawTexture(r, _whiteTex);
        }

        // ── Discard ghosts ──
        for (int i = _activeDiscardAnims.Count - 1; i >= 0; i--)
        {
            float t = (Time.time - _activeDiscardAnims[i].startTime) / 0.5f;
            if (t > 1f) { _activeDiscardAnims.RemoveAt(i); continue; }
            bool isComboGhost = cardLibrary[_activeDiscardAnims[i].libIndex].isCombo;
            float gw = isComboGhost ? cWidth : nWidth;
            float gh = isComboGhost ? cHeight : nHeight;
            Rect r = new Rect(_activeDiscardAnims[i].startX,
                              Screen.height - gh - 60f - t * 150f, gw, gh);
            DrawCard(r, cardLibrary[_activeDiscardAnims[i].libIndex], false, 0f, false, 0f, 1f - t);
        }

        GUI.color = Color.white;
    }

    private void DrawSlotPips(float groupX, float y, int remaining, int total, bool isAttack)
    {
        float pip = 18f;
        float gap = 7f;
        float groupW = nWidth * 4 + nSpace * 3;
        float totalPipW = total * pip + (total - 1) * gap;
        float startX = groupX + groupW / 2f - totalPipW / 2f;

        Color filled = isAttack ? new Color(1f, 0.28f, 0.28f, 0.95f)
                                : new Color(0.28f, 0.68f, 1f,   0.95f);
        Color empty  = new Color(0.15f, 0.15f, 0.15f, 0.65f);

        string label = isAttack ? "ATK" : "DEF";
        GUIStyle s = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold,
                                                    alignment = TextAnchor.MiddleCenter };
        s.normal.textColor = isAttack ? new Color(1f, 0.5f, 0.5f) : new Color(0.5f, 0.8f, 1f);
        GUI.Label(new Rect(startX - 36f, y, 34f, pip), label, s);

        for (int i = 0; i < total; i++)
        {
            GUI.color = (i < remaining) ? filled : empty;
            GUI.DrawTexture(new Rect(startX + i * (pip + gap), y, pip, pip), _whiteTex);
        }
        GUI.color = Color.white;
    }

    private void DrawSlotConfigUI()
    {
        float panelW = 420f;
        float panelH = 180f;
        float px = Screen.width / 2f - panelW / 2f;
        float py = Screen.height - nHeight - 60f - panelH - 50f;

        GUI.color = new Color(0f, 0f, 0f, 0.82f);
        GUI.DrawTexture(new Rect(px, py, panelW, panelH), _whiteTex);
        GUI.color = new Color(0.4f, 0.4f, 0.4f, 1f);
        GUI.DrawTexture(new Rect(px,           py,           panelW, 2f), _whiteTex);
        GUI.DrawTexture(new Rect(px,           py+panelH-2f, panelW, 2f), _whiteTex);
        GUI.DrawTexture(new Rect(px,           py,           2f, panelH), _whiteTex);
        GUI.DrawTexture(new Rect(px+panelW-2f, py,           2f, panelH), _whiteTex);
        GUI.color = Color.white;

        GUIStyle hdr = new GUIStyle(GUI.skin.label)
            { alignment=TextAnchor.MiddleCenter, fontStyle=FontStyle.Bold, fontSize=16 };
        hdr.normal.textColor = new Color(0.9f, 0.9f, 0.9f);
        GUI.Label(new Rect(px, py + 10f, panelW, 24f), "SLOT CONFIGURATION", hdr);

        GUIStyle val = new GUIStyle(GUI.skin.label)
            { alignment=TextAnchor.MiddleCenter, fontStyle=FontStyle.Bold, fontSize=15 };

        float col1 = px + 30f;
        float col2 = px + panelW / 2f + 30f;
        float row1 = py + 44f;
        float row2 = py + 76f;

        // Defense column
        val.normal.textColor = new Color(0.4f, 0.78f, 1f);
        GUI.Label(new Rect(col1, row1, 160f, 24f), $"DEFENSE  {_cfgDefense}", val);
        if (GUI.Button(new Rect(col1,        row2, 44f, 30f), "−")) _cfgDefense = Mathf.Max(1, _cfgDefense - 1);
        GUI.Label(new Rect(col1 + 48f,       row2, 30f, 30f), _cfgDefense.ToString(), val);
        if (GUI.Button(new Rect(col1 + 82f,  row2, 44f, 30f), "+")) _cfgDefense = Mathf.Min(5, _cfgDefense + 1);

        // Attack column
        val.normal.textColor = new Color(1f, 0.45f, 0.45f);
        GUI.Label(new Rect(col2, row1, 160f, 24f), $"ATTACK  {_cfgAttack}", val);
        if (GUI.Button(new Rect(col2,        row2, 44f, 30f), "−")) _cfgAttack = Mathf.Max(1, _cfgAttack - 1);
        GUI.Label(new Rect(col2 + 48f,       row2, 30f, 30f), _cfgAttack.ToString(), val);
        if (GUI.Button(new Rect(col2 + 82f,  row2, 44f, 30f), "+")) _cfgAttack = Mathf.Min(5, _cfgAttack + 1);

        GUIStyle btn = new GUIStyle(GUI.skin.button) { fontStyle=FontStyle.Bold, fontSize=15 };
        GUI.color = new Color(0.25f, 0.9f, 0.35f);
        if (GUI.Button(new Rect(px + panelW / 2f - 70f, py + 126f, 140f, 36f), "CONFIRM", btn))
            CmdSetSlots(_cfgAttack, _cfgDefense);
        GUI.color = Color.white;
    }

    // approachFrac: 0=just fired, 1=shout window
    private void DrawCard(Rect r, CombatCard card, bool isRhythm,
                          float approachFrac, bool isShout, float pulse, float alpha)
    {
        bool isCombo = card.isCombo;

        // 1. Background
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

        // 2. Top charge strip
        if (isRhythm)
        {
            float chargeAlpha = Mathf.Lerp(0.15f, 0.95f, approachFrac) * alpha;
            Color chargeCol = isShout
                ? Color.Lerp(isCombo ? new Color(1f, 0.7f, 0f) : new Color(0.1f, 1f, 0.45f),
                             Color.white, pulse * 0.45f)
                : isCombo ? new Color(1f, 0.6f, 0f) : new Color(0f, 0.85f, 1f);
            GUI.color = new Color(chargeCol.r, chargeCol.g, chargeCol.b, chargeAlpha);
            GUI.DrawTexture(new Rect(r.x, r.y, r.width * approachFrac, 5f), _whiteTex);
        }

        // 3. Approach bar (bottom)
        if (isRhythm)
        {
            const float barPad    = 6f;
            const float barH      = 22f;
            const float shoutFrac = 0.20f;
            float barW = r.width - barPad * 2f;
            float barX = r.x + barPad;
            float barY = r.y + r.height - barH - 8f;

            // Track background
            GUI.color = new Color(0f, 0f, 0f, 0.65f * alpha);
            GUI.DrawTexture(new Rect(barX, barY, barW, barH), _whiteTex);

            // Shout zone
            GUI.color = isCombo ? new Color(1f, 0.65f, 0f, 0.30f * alpha)
                                : new Color(0f, 1f, 0.3f,  0.30f * alpha);
            GUI.DrawTexture(new Rect(barX + barW * (1f - shoutFrac), barY, barW * shoutFrac, barH), _whiteTex);

            // Shout zone divider line
            GUI.color = isCombo ? new Color(1f, 0.80f, 0.1f, 0.55f * alpha)
                                : new Color(0.1f, 1f, 0.5f,   0.55f * alpha);
            GUI.DrawTexture(new Rect(barX + barW * (1f - shoutFrac) - 1f, barY, 2f, barH), _whiteTex);

            Color fillCol;
            if (isShout)
                fillCol = Color.Lerp(isCombo ? new Color(1f, 0.7f, 0f, 0.95f * alpha)
                                             : new Color(0.1f, 1f, 0.45f, 0.95f * alpha),
                                     Color.white, pulse * 0.40f);
            else if (approachFrac > 0.72f)
                fillCol = Color.Lerp(new Color(1f, 0.8f, 0f, 0.85f * alpha),
                                     isCombo ? new Color(1f, 0.65f, 0f, 0.92f * alpha)
                                             : new Color(0.1f, 1f, 0.4f, 0.92f * alpha),
                                     (approachFrac - 0.72f) / 0.28f);
            else
                fillCol = new Color(0f, 0.85f, 1f, 0.75f * alpha);

            // Main fill
            GUI.color = fillCol;
            GUI.DrawTexture(new Rect(barX, barY, barW * approachFrac, barH), _whiteTex);

            // Gloss highlight (top 30% of fill)
            if (approachFrac > 0.01f)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.22f * alpha);
                GUI.DrawTexture(new Rect(barX, barY, barW * approachFrac, barH * 0.30f), _whiteTex);
            }

            // Border
            GUI.color = new Color(1f, 1f, 1f, 0.20f * alpha);
            GUI.DrawTexture(new Rect(barX,        barY,         barW,  1.5f), _whiteTex);
            GUI.DrawTexture(new Rect(barX,        barY + barH,  barW,  1.5f), _whiteTex);
            GUI.DrawTexture(new Rect(barX,        barY,         1.5f,  barH), _whiteTex);
            GUI.DrawTexture(new Rect(barX + barW, barY,         1.5f,  barH), _whiteTex);
        }

        // 4. Corner bracket borders
        float glowT  = isRhythm ? approachFrac : 0f;
        float bLen   = isShout ? Mathf.Lerp(20f, 26f, pulse) : 18f;
        float bThick = isShout ? Mathf.Lerp(2f,  3f,  pulse) : 2f;

        Color bracketBase = isCombo ? new Color(1f, 0.75f, 0f, alpha) : new Color(0.85f, 0f, 1f, alpha);
        Color bracketHot  = isCombo ? new Color(1f, 0.92f, 0.3f, alpha) : new Color(0.6f, 0f, 1f, alpha);
        if (isShout) bracketBase = Color.Lerp(bracketBase, Color.white, pulse * 0.45f);
        Color bCol = Color.Lerp(bracketBase, bracketHot, glowT);
        GUI.color = new Color(bCol.r, bCol.g, bCol.b, bCol.a * (0.55f + glowT * 0.45f));

        GUI.DrawTexture(new Rect(r.x,                  r.y,                   bLen,   bThick), _whiteTex);
        GUI.DrawTexture(new Rect(r.x,                  r.y,                   bThick, bLen),   _whiteTex);
        GUI.DrawTexture(new Rect(r.x + r.width - bLen, r.y,                   bLen,   bThick), _whiteTex);
        GUI.DrawTexture(new Rect(r.x + r.width - bThick, r.y,                 bThick, bLen),   _whiteTex);
        GUI.DrawTexture(new Rect(r.x,                  r.y + r.height - bThick, bLen,   bThick), _whiteTex);
        GUI.DrawTexture(new Rect(r.x,                  r.y + r.height - bLen,   bThick, bLen),   _whiteTex);
        GUI.DrawTexture(new Rect(r.x + r.width - bLen,   r.y + r.height - bThick, bLen,   bThick), _whiteTex);
        GUI.DrawTexture(new Rect(r.x + r.width - bThick, r.y + r.height - bLen,   bThick, bLen),   _whiteTex);

        // 5. Text
        GUI.color = Color.white;
        _titleStyle.normal.textColor = isCombo
            ? new Color(1f,  0.88f, 0.22f, alpha)
            : new Color(1f,  1f,    1f,    alpha);
        GUI.Label(new Rect(r.x, r.y + 8f, r.width, 34f), card.cardName, _titleStyle);

        _descStyle.normal.textColor = isCombo
            ? new Color(1f,   0.78f, 0.35f, 0.88f * alpha)
            : new Color(0.72f, 0.88f, 1f,   0.82f * alpha);
        GUI.Label(new Rect(r.x + 5f, r.y + 46f, r.width - 10f, 54f), card.description, _descStyle);

        if (isRhythm)
        {
            string stateText;
            Color  stateCol;
            if (isShout)
            {
                stateText = "!! SHOUT !!";
                stateCol  = isCombo
                    ? new Color(1f,   0.85f, 0f,   (0.78f + pulse * 0.22f) * alpha)
                    : new Color(0.2f, 1f,    0.5f, (0.78f + pulse * 0.22f) * alpha);
            }
            else if (approachFrac > 0.68f)
            {
                stateText = "GET READY";
                float ramp = (approachFrac - 0.68f) / 0.32f;
                stateCol  = new Color(1f, Mathf.Lerp(0.65f, 0.9f, ramp), 0.1f,
                                      Mathf.Lerp(0.5f, 0.9f, ramp) * alpha);
            }
            else
            {
                stateText = "WAIT";
                stateCol  = new Color(1f, 1f, 1f, 0.18f * alpha);
            }
            _statusStyle.normal.textColor = stateCol;
            GUI.Label(new Rect(r.x, r.y + r.height - 58f, r.width, 24f), stateText, _statusStyle);
        }

        if (isCombo)
        {
            _badgeStyle.normal.textColor = new Color(1f, 0.75f, 0f, alpha);
            GUI.Label(new Rect(r.x, r.y + 4f, r.width - 5f, 20f), $"{card.comboChainLength}×", _badgeStyle);
        }
    }

    private void DrawOpponentSlots()
    {
        if (_oppCardsCache == null)
        {
            foreach (var p in GameManager.players)
            {
                if (p == null) continue;
                var cm = p.GetComponent<CardManager>();
                if (cm != null && cm != this) { _oppCardsCache = cm; break; }
            }
            // Fallback: picks up bots that aren't in GameManager.players
            if (_oppCardsCache == null)
            {
                foreach (var cm in FindObjectsOfType<CardManager>())
                    if (cm != this) { _oppCardsCache = cm; break; }
            }
        }
        CardManager opp = _oppCardsCache;
        if (opp == null) return;

        // Anchored below the opponent health bar (posX = Screen.width-420, posY=20, barHeight=60)
        float pip    = 18f;
        float pipGap = 7f;
        float panelW = 400f;
        float panelH = 76f;
        float px = Screen.width - panelW - 20f;
        float py = 88f;

        GUI.color = new Color(0f, 0f, 0f, 0.72f);
        GUI.DrawTexture(new Rect(px, py, panelW, panelH), _whiteTex);
        GUI.color = Color.white;

        GUIStyle hdr = new GUIStyle(GUI.skin.label)
            { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 13 };
        hdr.normal.textColor = new Color(0.9f, 0.9f, 0.9f);
        GUI.Label(new Rect(px, py + 4f, panelW, 18f), "ENEMY SLOTS", hdr);

        GUIStyle lbl = new GUIStyle(GUI.skin.label)
            { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 11 };

        // Defense pips (left half)
        float defTotalW = opp.defenseSlotTotal * pip + (opp.defenseSlotTotal - 1) * pipGap;
        float defStartX = px + panelW * 0.25f - defTotalW * 0.5f;
        lbl.normal.textColor = new Color(0.4f, 0.78f, 1f);
        GUI.Label(new Rect(px, py + 24f, panelW * 0.5f, 16f), "DEF", lbl);
        Color defFilled = new Color(0.28f, 0.68f, 1f,  0.9f);
        Color empty     = new Color(0.15f, 0.15f, 0.15f, 0.65f);
        for (int i = 0; i < opp.defenseSlotTotal; i++)
        {
            GUI.color = (i < opp.defenseSlotsRemaining) ? defFilled : empty;
            GUI.DrawTexture(new Rect(defStartX + i * (pip + pipGap), py + 44f, pip, pip), _whiteTex);
        }

        // Attack pips (right half)
        float atkTotalW = opp.attackSlotsTotal * pip + (opp.attackSlotsTotal - 1) * pipGap;
        float atkStartX = px + panelW * 0.75f - atkTotalW * 0.5f;
        lbl.normal.textColor = new Color(1f, 0.45f, 0.45f);
        GUI.Label(new Rect(px + panelW * 0.5f, py + 24f, panelW * 0.5f, 16f), "ATK", lbl);
        Color atkFilled = new Color(1f, 0.28f, 0.28f, 0.9f);
        for (int i = 0; i < opp.attackSlotsTotal; i++)
        {
            GUI.color = (i < opp.attackSlotsRemaining) ? atkFilled : empty;
            GUI.DrawTexture(new Rect(atkStartX + i * (pip + pipGap), py + 44f, pip, pip), _whiteTex);
        }

        GUI.color = Color.white;
    }

    private void EnsureStyles()
    {
        if (_titleStyle != null) return;
        _titleStyle  = new GUIStyle(GUI.skin.label) { alignment=TextAnchor.UpperCenter,  fontStyle=FontStyle.Bold, fontSize=20 };
        _descStyle   = new GUIStyle(GUI.skin.label) { alignment=TextAnchor.UpperCenter,  fontSize=12, wordWrap=true };
        _statusStyle = new GUIStyle(GUI.skin.label) { alignment=TextAnchor.MiddleCenter, fontStyle=FontStyle.Bold, fontSize=13 };
        _badgeStyle  = new GUIStyle(GUI.skin.label) { alignment=TextAnchor.UpperRight,   fontStyle=FontStyle.Bold, fontSize=14 };
    }
}
