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

    // Kept for VoiceCommandManager compatibility — always empty in combo mode
    public readonly SyncList<int> currentHandIndices = new SyncList<int>();

    // ── Slot system ────────────────────────────────────────────────────────
    [SyncVar] public int attackSlotsTotal      = 3;
    [SyncVar] public int defenseSlotTotal      = 2;
    [SyncVar] public int attackSlotsRemaining  = 3;
    [SyncVar] public int defenseSlotsRemaining = 2;

    // ── Same-card cooldown (blocks reusing the last-used card next window) ─
    [SyncVar] public string justUsedTrigger = "";  // set when a card is consumed this window
    [SyncVar] public string blockedTrigger  = "";  // the trigger blocked during the NEXT window

    private int _cfgAttack  = 3;
    private int _cfgDefense = 2;

    // ── Classification ─────────────────────────────────────────────────────
    private static readonly HashSet<string> _atkSet = new HashSet<string>
        { "Jab", "Cross", "Hook", "UnbreakablePunch", "Grapple", "Feint", "Uppercut", "Sweep", "Overclock", "Reverse" };
    private static readonly HashSet<string> _defSet = new HashSet<string>
        { "Block", "ParryIntent", "Left", "Right", "Clutch", "Focus", "Taunt", "Trap", "Cage", "Mirror" };

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
    private Texture2D    _whiteTex;
    private GUIStyle     _titleStyle;
    private GUIStyle     _descStyle;
    private GUIStyle     _statusStyle;
    private GUIStyle     _badgeStyle;
    private CardManager  _oppCardsCache;
    private PlayerCombat _myPCombat;

    // Slot bonus popup
    private string _slotBonusText  = "";
    private Color  _slotBonusColor = Color.white;
    private float  _slotBonusFade  = 0f;

    // Single-mode card sizes
    const float nWidth  = 170f;
    const float nHeight = 200f;
    const float nSpace  = 14f;

    // Combo HUD dimensions
    private const float COMBO_W = 420f;
    private const float COMBO_H = 170f;

    // Combo HUD styles (separate from single-mode styles)
    private GUIStyle _comboMoveStyle;
    private GUIStyle _comboStatusStyle;
    private GUIStyle _comboLabelStyle;

    public bool IsComboHandActive => false;

    // ── Round-available cards (synced from PlayerCombat SyncVar) ───────────
    public List<string> availableCardsForRound = new List<string>();
    private string _lastCardsString = "";

    private void UpdateAvailableCardsFromCombat()
    {
        if (_myPCombat == null) _myPCombat = GetComponent<PlayerCombat>();
        if (_myPCombat == null) return;
        if (_myPCombat.availableCardsString == _lastCardsString) return;
        _lastCardsString = _myPCombat.availableCardsString;
        if (string.IsNullOrEmpty(_lastCardsString))
            availableCardsForRound.Clear();
        else
            availableCardsForRound = new List<string>(_lastCardsString.Split('|'));
    }

    // ── Card definitions ───────────────────────────────────────────────────
    // ALL possible cards that can appear in the shop and be played
    private static CombatCard[] BuildNormalCardDefs() => new[]
    {
        // Basic (8 cards)
        new CombatCard { cardName="Punch", triggerName="Jab",              description="Quick lead strike. Reliable base damage." },
        new CombatCard { cardName="Blast", triggerName="Cross",            description="Straight power hit. Counters enemies trying to dodge." },
        new CombatCard { cardName="Hook",  triggerName="Hook",             description="Heavy side-swing. High damage and grants bonus Energy." },
        new CombatCard { cardName="Block", triggerName="Block",            description="Standard defense. Wider timing window to negate damage." },
        new CombatCard { cardName="Left",  triggerName="Left",             description="Quick dodge to the left. Evades Jabs and Hooks." },
        new CombatCard { cardName="Right", triggerName="Right",            description="Quick dodge to the right. Evades Jabs and Hooks." },
        new CombatCard { cardName="Parry",  triggerName="ParryIntent",      description="Elite Parry. Reflects 120% damage back to the attacker." },
        new CombatCard { cardName="Boom",  triggerName="UnbreakablePunch", description="Unstoppable. Ignores blocks and deals massive dmg." },
        new CombatCard { cardName="Grapple", triggerName="Grapple",         description="Command grab. Bypasses blocks and dodges." },
        new CombatCard { cardName="Feint",  triggerName="Feint",           description="Cancels opponent defense. Mind game tool." },
        new CombatCard { cardName="Clutch", triggerName="Clutch",          description="HIGH RISK. Nullify heavy attack on perfect timing." },
        // Advanced (4 cards)
        new CombatCard { cardName="Uppercut", triggerName="Uppercut",       description="Anti-dodge attack. Punishes evasive play." },
        new CombatCard { cardName="Sweep",    triggerName="Sweep",          description="Low attack. Catches defensive players." },
        new CombatCard { cardName="Focus",    triggerName="Focus",          description="Charge up. Next attack deals +50% damage." },
        new CombatCard { cardName="Taunt",    triggerName="Taunt",          description="Force opponent to use only Attack Cards next turn." },
        // Legendary (5 cards)
        new CombatCard { cardName="Overclock", triggerName="Overclock",     description="High damage rush. Deals +35% but +10% self-damage." },
        new CombatCard { cardName="Reverse",   triggerName="Reverse",       description="Negates all damage and returns it to opponent." },
        new CombatCard { cardName="Trap",      triggerName="Trap",          description="Set trap. Opponent takes damage on move or block." },
        new CombatCard { cardName="Cage",      triggerName="Cage",          description="Trap opponent. Playing a card next turn damages them." },
        new CombatCard { cardName="Mirror",    triggerName="Mirror",        description="Returns next attack damage +15% bonus." },
    };

    private void Awake()
    {
        // Merge any missing cards from the full definition set
        var existingTriggers = new HashSet<string>();
        foreach (var c in cardLibrary)
            if (!string.IsNullOrEmpty(c.triggerName))
                existingTriggers.Add(c.triggerName);

        foreach (var nc in BuildNormalCardDefs())
            if (!existingTriggers.Contains(nc.triggerName))
                cardLibrary.Add(nc);
    }

    // ── Slot API ───────────────────────────────────────────────────────────

    public bool HasSlot(string trigger)
    {
        bool isRhythm = RhythmRoundManager.Instance != null && RhythmRoundManager.Instance.isRoundActive;
        if (!isRhythm) return IsAttackTrigger(trigger) || IsDefenseTrigger(trigger);

        // Block the last-used card for one full input window
        if (trigger == blockedTrigger) return false;

        if (trigger.StartsWith("Combo"))
        {
            CombatCard card = GetCardInHand(trigger);
            if (card == null) return false;
            if (IsComboAttack(card)) return IsOpponentStaggered() || attackSlotsRemaining > 0;
            return defenseSlotsRemaining > 0;
        }
        if (IsAttackTrigger(trigger))  return IsOpponentStaggered() || attackSlotsRemaining > 0;
        if (IsDefenseTrigger(trigger)) return defenseSlotsRemaining > 0;
        return false;
    }

    private bool IsOpponentStaggered()
    {
        foreach (var p in GameManager.players)
        {
            if (p == null) continue;
            var cm = p.GetComponent<CardManager>();
            if (cm != null && cm != this)
                return p.GetComponent<PlayerCombat>()?.IsStaggered ?? false;
        }
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

        if (isAttack)
        {
            if (IsOpponentStaggered()) return;
            attackSlotsRemaining = Mathf.Max(0, attackSlotsRemaining - 1);
        }
        else
        {
            defenseSlotsRemaining = Mathf.Max(0, defenseSlotsRemaining - 1);
        }

        // Record for next-window cooldown — only during active single-move rhythm rounds
        var rmm = RhythmRoundManager.Instance;
        if (rmm != null && rmm.isRoundActive && rmm.IsSingleMoveMode() && !trigger.StartsWith("Combo"))
            justUsedTrigger = trigger;
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
        blockedTrigger    = "";
        justUsedTrigger   = "";
        attackSlotsRemaining  = attackSlotsTotal;
        defenseSlotsRemaining = defenseSlotTotal;
    }

    // Called every beat by RhythmRoundManager to roll the cooldown forward one window
    [Server]
    public void AdvanceCooldown()
    {
        blockedTrigger  = justUsedTrigger;
        justUsedTrigger = "";
    }

    [Command]
    public void CmdSetSlots(int atk, int def)
    {
        int a = Mathf.Clamp(atk, 1, 4);
        int d = Mathf.Clamp(5 - a, 1, 4);
        attackSlotsTotal      = a;
        defenseSlotTotal      = d;
        attackSlotsRemaining  = a;
        defenseSlotsRemaining = d;
    }

    // ── Card availability API ──────────────────────────────────────────────

    public bool IsCardInHand(string trigger)
    {
        // In combo mode, moves are auto-assigned — voice commands cannot queue them
        var rmm = RhythmRoundManager.Instance;
        if (rmm != null && rmm.isRoundActive && !rmm.IsSingleMoveMode()) return false;

        // Shop filter: only allow cards picked in the pre-round shop OR owned from inventory
        var inv = GetComponent<PlayerInventory>();
        if (inv != null && inv.equippedCombatCards.Count > 0)
        {
            // Map trigger names to card IDs
            string cardId = trigger switch
            {
                "Jab" => "jab",
                "Cross" => "cross",
                "Hook" => "hook",
                "Block" => "block",
                "Left" => "dodge_left",
                "Right" => "dodge_right",
                "ParryIntent" => "reflect",
                "UnbreakablePunch" => "boom",
                "Grapple" => "grapple",
                "Feint" => "feint",
                "Clutch" => "clutch",
                "Uppercut" => "uppercut",
                "Sweep" => "sweep",
                "Focus" => "focus",
                "Taunt" => "taunt",
                "Overclock" => "overclock",
                "Reverse" => "reverse",
                "Trap" => "trap",
                "Cage" => "cage",
                "Mirror" => "mirror",
                _ => trigger.ToLower()
            };
            if (!inv.equippedCombatCards.Contains(cardId)) return false;
        }
        else if (availableCardsForRound.Count > 0 && !availableCardsForRound.Contains(trigger))
        {
            return false;
        }

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
        int libIndex = cardLibrary.FindIndex(c => c.triggerName == trigger && !c.isCombo);
        if (libIndex >= 0 && connectionToClient != null)
            TargetRpcPlayDiscardAnim(connectionToClient, libIndex);
        ConsumeSlot(trigger);
    }

    [TargetRpc]
    public void TargetShowSlotBonus(NetworkConnection target, bool isAttackSlot)
    {
        _slotBonusText  = isAttackSlot ? "+1 ATK SLOT" : "+1 DEF SLOT";
        _slotBonusColor = isAttackSlot ? new Color(1f, 0.30f, 0.30f) : new Color(0.25f, 0.75f, 1f);
        _slotBonusFade  = 1f;
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
        var allDefCards = new[] { "Block", "ParryIntent", "Left", "Right", "Clutch", "Focus", "Taunt", "Trap", "Cage", "Mirror" };
        var allAtkCards = new[] { "Jab", "Cross", "Hook", "UnbreakablePunch", "Grapple", "Feint", "Uppercut", "Sweep", "Overclock", "Reverse" };

        var defCards = (availableCardsForRound.Count > 0)
            ? allDefCards.Where(c => availableCardsForRound.Contains(c)).ToArray()
            : allDefCards;
        var atkCards = (availableCardsForRound.Count > 0)
            ? allAtkCards.Where(c => availableCardsForRound.Contains(c)).ToArray()
            : allAtkCards;

        float gap       = 40f;
        float defGroupW = nWidth * defCards.Length + nSpace * Mathf.Max(0, defCards.Length - 1);
        float atkGroupW = nWidth * atkCards.Length + nSpace * Mathf.Max(0, atkCards.Length - 1);
        float totalW    = defGroupW + gap + atkGroupW;
        float defStartX = Screen.width / 2f - totalW / 2f;
        float atkStartX = defStartX + defGroupW + gap;

        for (int i = 0; i < atkCards.Length; i++)
            if (atkCards[i] == trigger) return atkStartX + i * (nWidth + nSpace);
        for (int i = 0; i < defCards.Length; i++)
            if (defCards[i] == trigger) return defStartX + i * (nWidth + nSpace);
        return Screen.width / 2f;
    }

    // ── OnGUI ──────────────────────────────────────────────────────────────

    private void OnGUI()
    {
        if (!isLocalPlayer) return;
        if (cardLibrary == null || cardLibrary.Count == 0) return;
        if (RhythmRoundManager.Instance != null && RhythmRoundManager.Instance.isShopPhase) return;

        if (_whiteTex == null) { _whiteTex = new Texture2D(1, 1); _whiteTex.SetPixel(0, 0, Color.white); _whiteTex.Apply(); }
        EnsureStyles();

        UpdateAvailableCardsFromCombat();

        if (_myPCombat == null) _myPCombat = GetComponent<PlayerCombat>();

        var  rmm         = RhythmRoundManager.Instance;
        bool isRhythm    = rmm != null && rmm.isRoundActive;
        bool isComboMode = isRhythm && !rmm.IsSingleMoveMode();

        bool inputLocked = false;
        if (isRhythm && _myPCombat != null)
        {
            if (rmm.IsSingleMoveMode())
                inputLocked = !_myPCombat.HasOpenSlot(false) || !_myPCombat.HasOpenSlot(true);
            else
                inputLocked = _myPCombat._comboBuffer.Count > 0;
        }

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

        if (isComboMode)
        {
            // ── COMBO MODE: auto-assigned move HUD ─────────────────────────
            DrawComboModeHUD(rmm, approachFrac, isShout, pulse, hoverY);
        }
        else
        {
            // ── SINGLE MODE: original defense LEFT / attack RIGHT ───────────
            float baseY     = Screen.height - nHeight - 60f + hoverY;
            float gap       = 120f;

            var allDefCards = new[] { "Block", "ParryIntent", "Left", "Right", "Clutch", "Focus", "Taunt", "Trap", "Cage", "Mirror" };
            var allAtkCards = new[] { "Jab", "Cross", "Hook", "UnbreakablePunch", "Grapple", "Feint", "Uppercut", "Sweep", "Overclock", "Reverse" };

            var defCards = (availableCardsForRound.Count > 0)
                ? System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Where(allDefCards, c => availableCardsForRound.Contains(c)))
                : allDefCards;
            var atkCards = (availableCardsForRound.Count > 0)
                ? System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Where(allAtkCards, c => availableCardsForRound.Contains(c)))
                : allAtkCards;

            float defGroupW = nWidth * defCards.Length + nSpace * Mathf.Max(0, defCards.Length - 1);
            float atkGroupW = nWidth * atkCards.Length + nSpace * Mathf.Max(0, atkCards.Length - 1);
            float totalW    = defGroupW + gap + atkGroupW;
            float defStartX = Screen.width / 2f - totalW / 2f;
            float atkStartX = defStartX + defGroupW + gap;

            for (int i = 0; i < defCards.Length; i++)
            {
                int libIdx = cardLibrary.FindIndex(c => c.triggerName == defCards[i] && !c.isCombo);
                if (libIdx < 0) continue;
                bool isBlocked = (defCards[i] == blockedTrigger);
                float alpha = isBlocked ? 0.18f : ((defenseSlotsRemaining > 0 && !inputLocked) ? 1f : 0.30f);
                Rect r = new Rect(defStartX + i * (nWidth + nSpace), baseY, nWidth, nHeight);
                DrawCard(r, cardLibrary[libIdx], isRhythm, approachFrac, isShout, pulse, alpha, isBlocked);
            }

            for (int i = 0; i < atkCards.Length; i++)
            {
                int libIdx = cardLibrary.FindIndex(c => c.triggerName == atkCards[i] && !c.isCombo);
                if (libIdx < 0) continue;
                bool isBlocked = (atkCards[i] == blockedTrigger);
                float alpha = isBlocked ? 0.18f : ((attackSlotsRemaining > 0 && !inputLocked) ? 1f : 0.30f);
                Rect r = new Rect(atkStartX + i * (nWidth + nSpace), baseY, nWidth, nHeight);
                DrawCard(r, cardLibrary[libIdx], isRhythm, approachFrac, isShout, pulse, alpha, isBlocked);
            }

            float pipY = Screen.height - nHeight - 60f + hoverY - 36f;
            DrawSlotPips(defStartX, pipY, defenseSlotsRemaining, defenseSlotTotal, false);
            DrawSlotPips(atkStartX, pipY, attackSlotsRemaining,  attackSlotsTotal,  true);

            if (!isRhythm) DrawSlotConfigUI();

            // Opponent slot display (single mode only)
            if (isRhythm) DrawOpponentSlots();
        }

        // ── Slot bonus popup ──
        if (_slotBonusFade > 0f)
        {
            _slotBonusFade -= Time.deltaTime * 0.7f;
            float rise = Mathf.Lerp(55f, 0f, _slotBonusFade);
            Color bc   = _slotBonusColor;
            bc.a = Mathf.Clamp01(_slotBonusFade * 2f);

            GUIStyle bonusStyle = new GUIStyle(GUI.skin.label)
                { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 36 };
            GUIStyle shadow = new GUIStyle(bonusStyle);
            shadow.normal.textColor     = new Color(0f, 0f, 0f, bc.a * 0.75f);
            bonusStyle.normal.textColor = bc;

            float bx = Screen.width  / 2f - 210f;
            float by = Screen.height / 2f - 130f - rise;
            GUI.Label(new Rect(bx + 3f, by + 3f, 420f, 64f), _slotBonusText, shadow);
            GUI.Label(new Rect(bx - 3f, by - 3f, 420f, 64f), _slotBonusText, shadow);
            GUI.Label(new Rect(bx,      by,       420f, 64f), _slotBonusText, bonusStyle);
            GUI.color = Color.white;
        }

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
            Rect r = new Rect(_activeDiscardAnims[i].startX,
                              Screen.height - nHeight - 60f - t * 150f, nWidth, nHeight);
            DrawCard(r, cardLibrary[_activeDiscardAnims[i].libIndex], false, 0f, false, 0f, 1f - t);
        }

        GUI.color = Color.white;
    }

    // ── Combo Mode HUD ─────────────────────────────────────────────────────

    private void DrawComboModeHUD(RhythmRoundManager rmm, float approachFrac,
                                  bool inShout, float pulse, float hoverY)
    {
        EnsureComboStyles();

        int    chainSize = rmm.currentComboCount;
        int    chainPos  = rmm.currentChainPosition;
        string moveId    = GetAssignedMoveId();
        string moveName  = ComboDisplayName(moveId);

        float px = Screen.width  / 2f - COMBO_W / 2f;
        float py = Screen.height - COMBO_H - 28f + hoverY;

        // Background
        Color bg = inShout
            ? Color.Lerp(new Color(0.07f, 0.03f, 0.14f, 0.95f),
                         new Color(0.14f, 0.06f, 0.24f, 0.98f), pulse * 0.6f)
            : new Color(0.04f, 0.04f, 0.10f, 0.92f);
        GUI.color = bg;
        GUI.DrawTexture(new Rect(px, py, COMBO_W, COMBO_H), _whiteTex);

        // Top charge strip
        Color stripCol = inShout
            ? Color.Lerp(new Color(0.85f, 0.35f, 1f), Color.white, pulse * 0.40f)
            : new Color(0.60f, 0.15f, 1f);
        GUI.color = new Color(stripCol.r, stripCol.g, stripCol.b,
                              Mathf.Lerp(0.15f, 0.95f, approachFrac));
        GUI.DrawTexture(new Rect(px, py, COMBO_W * approachFrac, 5f), _whiteTex);

        // Corner brackets
        float bLen   = inShout ? Mathf.Lerp(20f, 30f,  pulse) : 18f;
        float bThick = inShout ? Mathf.Lerp(2f,  3.5f, pulse) : 2f;
        Color bCol   = Color.Lerp(new Color(0.70f, 0.20f, 1f), new Color(0.90f, 0.60f, 1f), approachFrac);
        if (inShout) bCol = Color.Lerp(bCol, Color.white, pulse * 0.45f);
        GUI.color = new Color(bCol.r, bCol.g, bCol.b, 0.50f + approachFrac * 0.50f);
        DrawBrackets(px, py, COMBO_W, COMBO_H, bLen, bThick);

        // Combo chain badge
        GUI.color = Color.white;
        _comboLabelStyle.normal.textColor = new Color(1f, 0.6f, 0.1f);
        GUI.Label(new Rect(px + 8f, py + 6f, 200f, 20f),
                  $"COMBO  {chainPos + 1} / {chainSize}×", _comboLabelStyle);

        // Move name (large)
        _comboMoveStyle.normal.textColor = inShout
            ? Color.Lerp(new Color(0.95f, 0.60f, 1f), Color.white, pulse * 0.45f)
            : Color.white;
        GUI.Label(new Rect(px, py + 22f, COMBO_W, 52f), moveName, _comboMoveStyle);

        // Status text
        string statusText;
        Color  statusCol;
        if (inShout)
        {
            statusText = "!! SHOUT NOW !!";
            statusCol  = Color.Lerp(new Color(0.85f, 0.45f, 1f), Color.white, pulse * 0.35f);
            statusCol.a = 0.80f + pulse * 0.20f;
        }
        else if (approachFrac > 0.68f)
        {
            float ramp = (approachFrac - 0.68f) / 0.32f;
            statusText = "GET READY";
            statusCol  = new Color(1f, Mathf.Lerp(0.65f, 0.90f, ramp), 0.15f,
                                   Mathf.Lerp(0.50f, 0.90f, ramp));
        }
        else
        {
            statusText = string.IsNullOrEmpty(moveId) ? "INCOMING..." : "SHOUT ON BEAT";
            statusCol  = new Color(1f, 1f, 1f, 0.32f);
        }
        _comboStatusStyle.normal.textColor = statusCol;
        GUI.Label(new Rect(px, py + 82f, COMBO_W, 24f), statusText, _comboStatusStyle);

        // Approach bar
        DrawComboBar(px + 8f, py + COMBO_H - 38f, COMBO_W - 16f, 22f, approachFrac, inShout, pulse);

        GUI.color = Color.white;
    }

    private void DrawComboBar(float bx, float by, float bw, float bh,
                              float fill, bool inShout, float pulse)
    {
        const float shoutZone = 0.20f;

        GUI.color = new Color(0f, 0f, 0f, 0.65f);
        GUI.DrawTexture(new Rect(bx, by, bw, bh), _whiteTex);

        GUI.color = inShout ? new Color(0.80f, 0.30f, 1f, 0.55f)
                            : new Color(0.55f, 0.10f, 1f, 0.25f);
        GUI.DrawTexture(new Rect(bx + bw * (1f - shoutZone), by, bw * shoutZone, bh), _whiteTex);

        GUI.color = new Color(0.75f, 0.40f, 1f, 0.55f);
        GUI.DrawTexture(new Rect(bx + bw * (1f - shoutZone) - 1f, by, 2f, bh), _whiteTex);

        Color fillCol;
        if (inShout)
            fillCol = Color.Lerp(new Color(0.85f, 0.30f, 1f, 0.95f), Color.white, pulse * 0.38f);
        else if (fill > 0.72f)
            fillCol = Color.Lerp(new Color(1f, 0.80f, 0f, 0.85f),
                                 new Color(0.80f, 0.25f, 1f, 0.92f),
                                 (fill - 0.72f) / 0.28f);
        else
            fillCol = new Color(0.30f, 0.50f, 1f, 0.75f);

        GUI.color = fillCol;
        GUI.DrawTexture(new Rect(bx, by, bw * fill, bh), _whiteTex);

        if (fill > 0.01f)
        {
            GUI.color = new Color(1f, 1f, 1f, 0.20f);
            GUI.DrawTexture(new Rect(bx, by, bw * fill, bh * 0.28f), _whiteTex);
        }

        GUI.color = new Color(1f, 1f, 1f, 0.18f);
        GUI.DrawTexture(new Rect(bx,      by,      bw,   1.5f), _whiteTex);
        GUI.DrawTexture(new Rect(bx,      by + bh, bw,   1.5f), _whiteTex);
        GUI.DrawTexture(new Rect(bx,      by,      1.5f, bh),   _whiteTex);
        GUI.DrawTexture(new Rect(bx + bw, by,      1.5f, bh),   _whiteTex);

        GUI.color = Color.white;
    }

    private void DrawBrackets(float x, float y, float w, float h, float len, float thick)
    {
        GUI.DrawTexture(new Rect(x,             y,             len,   thick), _whiteTex);
        GUI.DrawTexture(new Rect(x,             y,             thick, len),   _whiteTex);
        GUI.DrawTexture(new Rect(x + w - len,   y,             len,   thick), _whiteTex);
        GUI.DrawTexture(new Rect(x + w - thick, y,             thick, len),   _whiteTex);
        GUI.DrawTexture(new Rect(x,             y + h - thick, len,   thick), _whiteTex);
        GUI.DrawTexture(new Rect(x,             y + h - len,   thick, len),   _whiteTex);
        GUI.DrawTexture(new Rect(x + w - len,   y + h - thick, len,   thick), _whiteTex);
        GUI.DrawTexture(new Rect(x + w - thick, y + h - len,   thick, len),   _whiteTex);
    }

    private string GetAssignedMoveId()
    {
        if (_myPCombat == null) return "";
        var rmm = RhythmRoundManager.Instance;
        if (rmm == null) return "";
        return rmm.IsSingleMoveMode()
            ? _myPCombat.PendingAttackTrigger
            : (_myPCombat._comboBuffer.Count > 0 ? _myPCombat._comboBuffer[0].attack : "");
    }

    private static string ComboDisplayName(string trigger) => trigger switch
    {
        "Jab"               => "PUNCH",
        "Cross"             => "FLANK",
        "Hook"              => "HOOK",
        "UnbreakablePunch"  => "BOOM",
        "ParryIntent"       => "CAGE",
        "Grapple"           => "GRAPPLE",
        "Feint"             => "FEINT",
        "Clutch"            => "CLUTCH",
        "Uppercut"          => "UPPERCUT",
        "Sweep"             => "SWEEP",
        "Focus"             => "FOCUS",
        "Taunt"             => "TAUNT",
        "Overclock"         => "OVERCLOCK",
        "Reverse"           => "REVERSE",
        "Trap"              => "TRAP",
        "Cage"              => "CAGE",
        "Mirror"            => "MIRROR",
        _                   => string.IsNullOrEmpty(trigger) ? "..." : trigger.ToUpper()
    };

    // ── Single-mode drawing ────────────────────────────────────────────────

    private void DrawSlotPips(float groupX, float y, int remaining, int total, bool isAttack)
    {
        const int displaySlots = 4;
        float pip = 18f;
        float gap = 7f;
        float groupW     = nWidth * 4 + nSpace * 3;
        float totalPipW  = displaySlots * pip + (displaySlots - 1) * gap;
        float startX     = groupX + groupW / 2f - totalPipW / 2f;

        Color filled = isAttack ? new Color(1f, 0.28f, 0.28f, 0.95f)
                                : new Color(0.28f, 0.68f, 1f,   0.95f);
        Color empty  = new Color(0.15f, 0.15f, 0.15f, 0.65f);

        string label = isAttack ? "ATK" : "DEF";
        GUIStyle s = new GUIStyle(GUI.skin.label)
            { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        s.normal.textColor = isAttack ? new Color(1f, 0.5f, 0.5f) : new Color(0.5f, 0.8f, 1f);
        GUI.Label(new Rect(startX - 36f, y, 34f, pip), label, s);

        for (int i = 0; i < displaySlots; i++)
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

        val.normal.textColor = new Color(0.4f, 0.78f, 1f);
        GUI.Label(new Rect(col1, row1, 160f, 24f), $"DEFENSE  {_cfgDefense}", val);
        if (GUI.Button(new Rect(col1,       row2, 44f, 30f), "−")) { _cfgDefense = Mathf.Max(1, _cfgDefense - 1); _cfgAttack = Mathf.Min(4, 5 - _cfgDefense); }
        GUI.Label(new Rect(col1 + 48f,      row2, 30f, 30f), _cfgDefense.ToString(), val);
        if (GUI.Button(new Rect(col1 + 82f, row2, 44f, 30f), "+")) { _cfgDefense = Mathf.Min(4, _cfgDefense + 1); _cfgAttack = Mathf.Max(1, 5 - _cfgDefense); }

        val.normal.textColor = new Color(1f, 0.45f, 0.45f);
        GUI.Label(new Rect(col2, row1, 160f, 24f), $"ATTACK  {_cfgAttack}", val);
        if (GUI.Button(new Rect(col2,       row2, 44f, 30f), "−")) { _cfgAttack = Mathf.Max(1, _cfgAttack - 1); _cfgDefense = Mathf.Min(4, 5 - _cfgAttack); }
        GUI.Label(new Rect(col2 + 48f,      row2, 30f, 30f), _cfgAttack.ToString(), val);
        if (GUI.Button(new Rect(col2 + 82f, row2, 44f, 30f), "+")) { _cfgAttack = Mathf.Min(4, _cfgAttack + 1); _cfgDefense = Mathf.Max(1, 5 - _cfgAttack); }

        GUIStyle btn = new GUIStyle(GUI.skin.button) { fontStyle=FontStyle.Bold, fontSize=15 };
        GUI.color = new Color(0.25f, 0.9f, 0.35f);
        if (GUI.Button(new Rect(px + panelW / 2f - 70f, py + 126f, 140f, 36f), "CONFIRM", btn))
            CmdSetSlots(_cfgAttack, _cfgDefense);
        GUI.color = Color.white;
    }

    private void DrawCard(Rect r, CombatCard card, bool isRhythm,
                          float approachFrac, bool isShout, float pulse, float alpha, bool isBlocked = false)
    {
        // Background
        Color bgBase = new Color(0.04f, 0.04f, 0.09f, 0.90f * alpha);
        if (isShout)
            bgBase = Color.Lerp(bgBase, new Color(0.03f, 0.12f, 0.15f, 0.90f * alpha), pulse * 0.7f);
        GUI.color = bgBase;
        GUI.DrawTexture(r, _whiteTex);

        // Top charge strip
        if (isRhythm)
        {
            float chargeAlpha = Mathf.Lerp(0.15f, 0.95f, approachFrac) * alpha;
            Color chargeCol = isShout
                ? Color.Lerp(new Color(0.1f, 1f, 0.45f), Color.white, pulse * 0.45f)
                : new Color(0f, 0.85f, 1f);
            GUI.color = new Color(chargeCol.r, chargeCol.g, chargeCol.b, chargeAlpha);
            GUI.DrawTexture(new Rect(r.x, r.y, r.width * approachFrac, 5f), _whiteTex);
        }

        // Circular progress indicator (centered, above status text) - always bright
        if (isRhythm)
        {
            float circleX = r.x + r.width / 2f - 30f;
            float circleY = r.y + 105f;
            float circleW = 60f;
            const float borderThick = 4f;
            float indicatorAlpha = Mathf.Max(0.85f, alpha); // Always keep indicator bright, even on dim cards

            // Outer circle background (dark)
            GUI.color = new Color(0.15f, 0.15f, 0.15f, 0.9f * indicatorAlpha);
            GUI.DrawTexture(new Rect(circleX, circleY, circleW, circleW), _whiteTex);

            // Inner circle fill (scales with approachFrac from 0 to 1)
            float fillW = (circleW - borderThick * 2f) * approachFrac;
            Color fillCol;
            if (isShout)
                fillCol = Color.Lerp(new Color(0.1f, 1f, 0.45f, 0.98f * indicatorAlpha), Color.white, pulse * 0.40f);
            else if (approachFrac > 0.72f)
                fillCol = Color.Lerp(new Color(1f, 0.8f, 0f, 0.90f * indicatorAlpha),
                                     new Color(0.1f, 1f, 0.4f, 0.95f * indicatorAlpha),
                                     (approachFrac - 0.72f) / 0.28f);
            else
                fillCol = new Color(0f, 0.85f, 1f, 0.80f * indicatorAlpha);

            // Draw fill centered (inside the border)
            float fillX = circleX + borderThick + ((circleW - borderThick * 2f) - fillW) / 2f;
            float fillY = circleY + borderThick + ((circleW - borderThick * 2f) - fillW) / 2f;
            GUI.color = fillCol;
            GUI.DrawTexture(new Rect(fillX, fillY, fillW, fillW), _whiteTex);

            // Thick circle border (bright outline - always visible)
            GUI.color = new Color(0.2f, 0.9f, 1f, 0.98f * indicatorAlpha);
            GUI.DrawTexture(new Rect(circleX, circleY, circleW, borderThick), _whiteTex); // top
            GUI.DrawTexture(new Rect(circleX, circleY + circleW - borderThick, circleW, borderThick), _whiteTex); // bottom
            GUI.DrawTexture(new Rect(circleX, circleY, borderThick, circleW), _whiteTex); // left
            GUI.DrawTexture(new Rect(circleX + circleW - borderThick, circleY, borderThick, circleW), _whiteTex); // right
        }

        // Corner brackets
        float glowT  = isRhythm ? approachFrac : 0f;
        float bLen   = isShout ? Mathf.Lerp(20f, 26f, pulse) : 18f;
        float bThick = isShout ? Mathf.Lerp(2f,  3f,  pulse) : 2f;
        Color bracketBase = new Color(0.85f, 0f, 1f, alpha);
        Color bracketHot  = new Color(0.6f,  0f, 1f, alpha);
        if (isShout) bracketBase = Color.Lerp(bracketBase, Color.white, pulse * 0.45f);
        Color bCol2 = Color.Lerp(bracketBase, bracketHot, glowT);
        GUI.color = new Color(bCol2.r, bCol2.g, bCol2.b, bCol2.a * (0.55f + glowT * 0.45f));
        DrawBrackets(r.x, r.y, r.width, r.height, bLen, bThick);

        // Text
        GUI.color = Color.white;
        _titleStyle.normal.textColor  = new Color(1f,    1f,    1f,    alpha);
        _descStyle.normal.textColor   = new Color(0.72f, 0.88f, 1f,    0.82f * alpha);
        _statusStyle.normal.textColor = Color.white;

        GUI.Label(new Rect(r.x, r.y + 8f, r.width, 34f), card.cardName, _titleStyle);
        GUI.Label(new Rect(r.x + 5f, r.y + 46f, r.width - 10f, 54f), card.description, _descStyle);

        if (isRhythm && !isBlocked)
        {
            string stateText;
            Color  stateCol;
            if (isShout)
            {
                stateText = "!! SHOUT !!";
                stateCol  = new Color(0.2f, 1f, 0.5f, (0.78f + pulse * 0.22f) * alpha);
            }
            else if (approachFrac > 0.68f)
            {
                float ramp = (approachFrac - 0.68f) / 0.32f;
                stateText  = "GET READY";
                stateCol   = new Color(1f, Mathf.Lerp(0.65f, 0.9f, ramp), 0.1f,
                                       Mathf.Lerp(0.5f, 0.9f, ramp) * alpha);
            }
            else
            {
                stateText = "Command your\nattack now";
                stateCol  = new Color(1f, 1f, 1f, 0.25f * alpha);
            }
            _statusStyle.normal.textColor = stateCol;
            GUI.Label(new Rect(r.x, r.y + r.height - 30f, r.width, 24f), stateText, _statusStyle);
        }

        // Blocked overlay — drawn last so it sits on top of everything
        if (isBlocked)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.72f);
            GUI.DrawTexture(r, _whiteTex);

            // Diagonal cross lines
            float cx = r.x + r.width  / 2f;
            float cy = r.y + r.height / 2f;
            float lineLen = Mathf.Min(r.width, r.height) * 0.38f;
            float thick   = 3f;
            GUI.color = new Color(0.9f, 0.15f, 0.15f, 0.85f);
            GUI.DrawTexture(new Rect(cx - lineLen, cy - thick / 2f, lineLen * 2f, thick), _whiteTex);
            GUI.DrawTexture(new Rect(cx - thick / 2f, cy - lineLen, thick, lineLen * 2f), _whiteTex);

            GUIStyle coolStyle = new GUIStyle(GUI.skin.label)
                { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 13 };
            coolStyle.normal.textColor = new Color(1f, 0.25f, 0.25f, 0.95f);
            GUI.color = Color.white;
            GUI.Label(new Rect(r.x, r.y + r.height - 38f, r.width, 22f), "COOLDOWN", coolStyle);
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
            if (_oppCardsCache == null)
                foreach (var cm in FindObjectsOfType<CardManager>())
                    if (cm != this) { _oppCardsCache = cm; break; }
        }
        CardManager opp = _oppCardsCache;
        if (opp == null) return;

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

        const int displaySlots = 4;
        float defTotalW = displaySlots * pip + (displaySlots - 1) * pipGap;
        float defStartX = px + panelW * 0.25f - defTotalW * 0.5f;
        lbl.normal.textColor = new Color(0.4f, 0.78f, 1f);
        GUI.Label(new Rect(px, py + 24f, panelW * 0.5f, 16f), "DEF", lbl);
        Color defFilled = new Color(0.28f, 0.68f, 1f, 0.9f);
        Color empty     = new Color(0.15f, 0.15f, 0.15f, 0.65f);
        for (int i = 0; i < displaySlots; i++)
        {
            GUI.color = (i < opp.defenseSlotsRemaining) ? defFilled : empty;
            GUI.DrawTexture(new Rect(defStartX + i * (pip + pipGap), py + 44f, pip, pip), _whiteTex);
        }

        float atkTotalW = displaySlots * pip + (displaySlots - 1) * pipGap;
        float atkStartX = px + panelW * 0.75f - atkTotalW * 0.5f;
        lbl.normal.textColor = new Color(1f, 0.45f, 0.45f);
        GUI.Label(new Rect(px + panelW * 0.5f, py + 24f, panelW * 0.5f, 16f), "ATK", lbl);
        Color atkFilled = new Color(1f, 0.28f, 0.28f, 0.9f);
        for (int i = 0; i < displaySlots; i++)
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

    private void EnsureComboStyles()
    {
        if (_comboMoveStyle != null) return;
        _comboMoveStyle   = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter,  fontStyle = FontStyle.Bold, fontSize = 40 };
        _comboStatusStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 15 };
        _comboLabelStyle  = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft,   fontStyle = FontStyle.Bold, fontSize = 13 };
    }
}
