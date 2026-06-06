using Mirror;
using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

public enum RoundType { SlowRhythm, FastCombo, CustomTrack }

public class RhythmRoundManager : NetworkBehaviour
{
    public static RhythmRoundManager Instance;

    [SyncVar] public RoundType currentType = RoundType.SlowRhythm;
    [SyncVar(hook = nameof(OnRoundStateChanged))] public bool isRoundActive = false;

    [SyncVar] public int currentComboCount = 1;
    [SyncVar] public bool customIsCombo = false;
    [SyncVar] public int currentChainPosition = 0;
    [SyncVar] public float lastBeatFireTime = 0f;
    private int _clusterBeatsLeftToFire = 0;

    [SyncVar] private double _startTime;

    [Header("Animation Sync")]
    public float windUpTime = 0.53f;

    [Header("Round Music")]
    public AudioClip slowRhythmMusic;

    [Header("Custom Tracks")]
    public AudioClip[] availableTracks; // Drag all your MP3s/WAVs here in the Inspector!

    public bool IsWindUpActive { get { return _isWindUpFired; } }
    private bool _isWindUpFired = false;

    // We now use this exact same list for ALL 3 game modes!
    private List<float> _upcomingImpacts = new List<float>();
    private List<int> _clusterSizes = new List<int>();

    private bool _heavyHitThisBeat  = false;
    private bool _tiebreakerPaused  = false;
    private Texture2D _whiteTex;



    private struct CombatLogEntry
    {
        public string p1Name;
        public string p1Move;
        public int p1State;
        public int p1Damage;
        public string p2Name;
        public string p2Move;
        public int p2State;
        public int p2Damage;
        public string reason;
        public float timeAdded;
    }
    private List<CombatLogEntry> combatLogs = new List<CombatLogEntry>();

    [Header("Single Player Settings")]
    public GameObject botPrefab;          // Rounds 1-2: the NEW (sword) bot
    public GameObject botPrefabSecondary; // Rounds 3+: the PREVIOUS bot (leave empty to always use botPrefab)
    [Tooltip("Rounds 1..N use botPrefab; rounds after this use botPrefabSecondary.")]
    public int botSwapAfterRound = 2;
    [Tooltip("How far the bot stands from the player (sword bot wants more reach).")]
    public float botStandDistance = 6.0f;
    [Tooltip("Spawn height offset for the bot. Raise this if the bot's legs sink into the floor on spawn.")]
    public float botSpawnY = 1.0f;
    private GameObject _activeBot;
    private GameObject _activeBotPrefab;  // which prefab the current bot was spawned from

    // Runtime-loaded clips from SmartBeatMapper file paths
    private Dictionary<string, AudioClip> _runtimeClips = new Dictionary<string, AudioClip>();
    private bool _isLoadingClip = false;
    private string _loadingClipName = "";

    [Header("Match State")]
    [SyncVar] public int p1RoundWins = 0;
    [SyncVar] public int p2RoundWins = 0;
    [SyncVar] public int currentRoundNumber = 1;
    [SyncVar] public int matchWinner = 0; // 0=ongoing, 1=p1, 2=p2, 3=draw
    [SyncVar] public bool isMatchOver = false;

    [Header("Economy")]
    [SyncVar] public int p1TotalCredits = 0;
    [SyncVar] public int p2TotalCredits = 0;

    // Server-only round & streak tracking
    private int _p1WinStreak = 0;
    private int _p1LossStreak = 0;
    private int _p2WinStreak = 0;
    private int _p2LossStreak = 0;
    private int _p1RoundDamageDealt = 0;
    private int _p2RoundDamageDealt = 0;
    private bool _isEndingRound = false;
    private bool _pendingCustomAudioPlay = false;

    private readonly Vector3 _spawnP1 = new Vector3(0f, 0f, -2.5f);
    private Vector3 _spawnP2 => new Vector3(0f, botSpawnY, 2.5f);

    [Header("Shop Phase")]
    [SyncVar] public bool isShopPhase = false;
    [SyncVar] public float shopTimeRemaining = 72f;

    [Header("Round Picker")]
    [SyncVar] public bool isRoundPickerActive = false;
    private float _roundPickerTimer = 0f;
    private const float ROUND_PICKER_AUTO_SELECT = 20f;
    private bool _prevPickerB = false; // edge-detect the VR B-button pick

    // True while the right controller's B button is held (VR picker shortcut).
    private static bool ReadRightB()
    {
        InputDevice rh = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        return rh.isValid && rh.TryGetFeatureValue(CommonUsages.secondaryButton, out bool v) && v;
    }

    private readonly string[] _shopCardPool = { "Jab", "Cross", "Hook", "Block", "Left", "Right" };
    private List<string> _p1ShopSelection = new List<string>();
    private List<string> _p2ShopSelection = new List<string>();
    private bool _p1ShopLocked = false;
    private bool _p2ShopLocked = false;

    // Track which round types have been used this match (one-use per match)
    private HashSet<RoundType> _usedRoundTypes = new HashSet<RoundType>();
    private HashSet<string> _usedCustomMaps = new HashSet<string>();

    private void Awake()
    {
        if (Instance == null) Instance = this;
        EnsureShopSystemsExist();
    }

    private void EnsureShopSystemsExist()
    {
        // Auto-spawn shop system GameObjects if not present
        if (ShopPhaseManager.Instance == null)
        {
            var spmGo = new GameObject("ShopPhaseManager");
            spmGo.AddComponent<ShopPhaseManager>();
        }
        if (ShopUI.Instance == null)
        {
            var suGo = new GameObject("ShopUI");
            suGo.AddComponent<ShopUI>();
        }
        if (CardDatabase.Instance == null)
        {
            var cdGo = new GameObject("CardDatabase");
            cdGo.AddComponent<CardDatabase>();
        }
        if (RoundCountdownUI.Instance == null)
        {
            var go = new GameObject("RoundCountdownUI");
            go.AddComponent<RoundCountdownUI>();
        }
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        StartCoroutine(DelayedRoundStart());
    }

    [Server]
    private IEnumerator DelayedRoundStart()
    {
        yield return new WaitForSeconds(2f);
        // Spawn bot if playing solo (only 1 human connected)
        EnsureBotExists();
        // Skip the old pre-round draft shop. Hand everyone the Tier 0 learning deck
        // (Strike / Block / Parry) so they learn the triangle, then buy more in post-round shops.
        GrantStarterDeckAndBegin();
    }

    // Tier 0 starter deck: Jab (Strike), Block (Block), Reflect (Parry).
    // Players learn the core triangle before the shop ever opens.
    private static readonly string[] StarterDeck = { "jab", "block", "reflect" };

    [Server]
    private void GrantStarterDeckAndBegin()
    {
        foreach (var player in GameManager.players)
        {
            if (player == null) continue;
            var inv = player.GetComponent<PlayerInventory>();
            if (inv == null) continue;

            foreach (var id in StarterDeck)
                if (!inv.ownedCombatCards.Contains(id))
                    inv.ownedCombatCards.Add(id);

            // Equip the full starter deck for the first round (SetupRound builds triggers from this).
            inv.EquipCombatCards(new List<string>(inv.ownedCombatCards));
            Debug.Log($"<color=green>STARTER DECK:</color> {player.PlayerName} starts with {string.Join(",", inv.ownedCombatCards)}");
        }

        // Go straight to the round picker — no first shop.
        ShowRoundPicker();
    }

    [Server]
    public void StartShopPhase()
    {
        isShopPhase = true;
        shopTimeRemaining = 72f;
        _p1ShopSelection.Clear();
        _p2ShopSelection.Clear();
        _p1ShopLocked = false;
        _p2ShopLocked = false;
        StartCoroutine(ShopTimerRoutine());
    }

    [Server]
    private IEnumerator ShopTimerRoutine()
    {
        while (shopTimeRemaining > 0f && isShopPhase)
        {
            yield return null;
            shopTimeRemaining -= Time.deltaTime;

            if (HasBotPlayer() && !_p2ShopLocked)
                AutoFillBotPicks();

            if (AllPlayersLockedIn())
            {
                FinalizeShopAndStartRound();
                yield break;
            }
        }

        if (isShopPhase)
        {
            AutoFillRemainingPicks();
            FinalizeShopAndStartRound();
        }
    }

    [Server]
    private bool AllPlayersLockedIn()
    {
        var players = new List<PlayerController>(GameManager.players);
        if (players.Count == 0) return false;
        bool human1Locked = _p1ShopLocked;
        // If there's a bot, check bot's lock status. If solo without bot, auto-pass.
        bool hasBot = HasBotPlayer();
        bool human2Locked = players.Count > 1 ? (hasBot ? _p2ShopLocked : true) : true;
        return human1Locked && human2Locked;
    }

    [Server]
    private bool HasBotPlayer()
    {
        foreach (var p in GameManager.players)
            if (p != null && p.GetComponent<BotController>() != null)
                return true;
        return false;
    }

    [Server]
    private void AutoFillBotPicks()
    {
        var remaining = new List<string>(_shopCardPool);
        foreach (var c in _p2ShopSelection) remaining.Remove(c);
        while (_p2ShopSelection.Count < 3 && remaining.Count > 0)
        {
            string pick = remaining[Random.Range(0, remaining.Count)];
            _p2ShopSelection.Add(pick);
            if (pick == "Left" && !_p2ShopSelection.Contains("Right")) _p2ShopSelection.Add("Right");
            if (pick == "Right" && !_p2ShopSelection.Contains("Left")) _p2ShopSelection.Add("Left");
            remaining.Remove(pick);
        }
        _p2ShopLocked = true;
    }

    [Server]
    private void AutoFillRemainingPicks()
    {
        var players = new List<PlayerController>(GameManager.players);
        for (int i = 0; i < players.Count; i++)
        {
            var selection = (i == 0) ? _p1ShopSelection : _p2ShopSelection;
            var remaining = new List<string>(_shopCardPool);
            foreach (var c in selection) remaining.Remove(c);
            while (selection.Count < 4 && remaining.Count > 0)
            {
                string pick = remaining[Random.Range(0, remaining.Count)];
                selection.Add(pick);
                if (pick == "Left" && !selection.Contains("Right")) selection.Add("Right");
                if (pick == "Right" && !selection.Contains("Left")) selection.Add("Left");
                remaining.Remove(pick);
            }
        }
        _p1ShopLocked = true;
        _p2ShopLocked = true;
    }

    [Server]
    public void ToggleShopCard(PlayerCombat pc, string cardName)
    {
        if (!isShopPhase) return;
        if (System.Array.IndexOf(_shopCardPool, cardName) < 0) return;

        int playerIdx = GetPlayerIndex(pc);
        if (playerIdx < 0) return;

        var selection = (playerIdx == 0) ? _p1ShopSelection : _p2ShopSelection;
        bool locked = (playerIdx == 0) ? _p1ShopLocked : _p2ShopLocked;
        if (locked) return;

        if (selection.Contains(cardName))
        {
            selection.Remove(cardName);
            if (cardName == "Left") selection.Remove("Right");
            if (cardName == "Right") selection.Remove("Left");
        }
        else
        {
            int pickCount = selection.Count;
            if (selection.Contains("Left") && selection.Contains("Right")) pickCount--;
            if (pickCount >= 4) return;

            selection.Add(cardName);
            if (cardName == "Left" && !selection.Contains("Right")) selection.Add("Right");
            if (cardName == "Right" && !selection.Contains("Left")) selection.Add("Left");
        }
    }

    [Server]
    public void LockInShop(PlayerCombat pc)
    {
        if (!isShopPhase) return;
        int playerIdx = GetPlayerIndex(pc);
        if (playerIdx < 0) return;

        var selection = (playerIdx == 0) ? _p1ShopSelection : _p2ShopSelection;
        bool locked = (playerIdx == 0) ? _p1ShopLocked : _p2ShopLocked;
        if (locked) return;

        var remaining = new List<string>(_shopCardPool);
        foreach (var c in selection) remaining.Remove(c);
        while (selection.Count < 4 && remaining.Count > 0)
        {
            string pick = remaining[Random.Range(0, remaining.Count)];
            selection.Add(pick);
            if (pick == "Left" && !selection.Contains("Right")) selection.Add("Right");
            if (pick == "Right" && !selection.Contains("Left")) selection.Add("Left");
            remaining.Remove(pick);
        }

        if (playerIdx == 0) _p1ShopLocked = true;
        else _p2ShopLocked = true;
    }

    [Server]
    private int GetPlayerIndex(PlayerCombat pc)
    {
        var players = new List<PlayerController>(GameManager.players);
        for (int i = 0; i < players.Count; i++)
            if (players[i] != null && players[i].GetComponent<PlayerCombat>() == pc) return i;
        return -1;
    }

    [Server]
    private void FinalizeShopAndStartRound()
    {
        isShopPhase = false;

        // Sync selected cards to all clients via PlayerCombat SyncVar
        // Also save starter cards to PlayerInventory as owned cards
        var players = new List<PlayerController>(GameManager.players);
        for (int i = 0; i < players.Count; i++)
        {
            if (players[i] == null) continue;
            var pc = players[i].GetComponent<PlayerCombat>();
            var selection = (i == 0) ? _p1ShopSelection : _p2ShopSelection;
            if (pc != null)
                pc.availableCardsString = string.Join("|", selection);

            // Save starter cards to inventory as permanently owned
            var inv = players[i].GetComponent<PlayerInventory>();
            if (inv != null)
            {
                foreach (var cardName in selection)
                {
                    string cardId = TriggerToCardId(cardName);
                    if (!string.IsNullOrEmpty(cardId) && !inv.ownedCombatCards.Contains(cardId))
                        inv.ownedCombatCards.Add(cardId);
                }
                // Equip all owned cards for the round
                var equipList = new System.Collections.Generic.List<string>(inv.ownedCombatCards);
                inv.EquipCombatCards(equipList);
                Debug.Log($"<color=green>STARTER CARDS:</color> {players[i].PlayerName} now owns: {string.Join(",", inv.ownedCombatCards)}");
            }
        }

        // Don't auto-start round — show round picker instead
        ShowRoundPicker();
    }

    [Server]
    public void ShowRoundPicker()
    {
        isRoundPickerActive = true;
        _roundPickerTimer = ROUND_PICKER_AUTO_SELECT;
    }

    [Server]
    public void SelectRoundType(RoundType type, string customMapName = "")
    {
        if (!isRoundPickerActive) return;
        isRoundPickerActive = false;

        _usedRoundTypes.Add(type);
        if (type == RoundType.CustomTrack && !string.IsNullOrEmpty(customMapName))
            _usedCustomMaps.Add(customMapName);

        switch (type)
        {
            case RoundType.SlowRhythm: StartSlowRound(); break;
            case RoundType.FastCombo: StartFastRound(); break;
            case RoundType.CustomTrack:
                if (!string.IsNullOrEmpty(customMapName))
                    LoadAndPlayMap(customMapName, null);
                else
                    StartSlowRound();
                break;
        }
    }

    private string TriggerToCardId(string trigger)
    {
        return trigger switch
        {
            "Jab" => "jab",
            "Cross" => "cross",
            "Hook" => "hook",
            "Block" => "block",
            "Left" => "dodge_left",
            "Right" => "dodge_right",
            "ParryIntent" => "reflect",
            "UnbreakablePunch" => "boom",
            _ => trigger.ToLower()
        };
    }

    public bool IsSingleMoveMode()
    {
        if (currentType == RoundType.SlowRhythm) return true;
        if (currentType == RoundType.CustomTrack && !customIsCombo) return true;
        return false;
    }

    [Server]
    public void StartSlowRound()
    {
        if (isRoundActive) return;
        currentType = RoundType.SlowRhythm;
        currentComboCount = 1;
        customIsCombo = false;

        _upcomingImpacts.Clear();
        _clusterSizes.Clear();
        _isWindUpFired = false;

        if (slowRhythmMusic != null && BeatAnalyzer.Instance != null)
            BeatAnalyzer.Instance.audioSource.clip = slowRhythmMusic;

        // 4-second intervals up to the 60s round cap (beat at 60s would land exactly at the limit,
        // so stop at 56s — the time-limit check in Update handles the cutoff cleanly).
        for (int i = 1; i * 4.0f <= 60f; i++)
            _upcomingImpacts.Add(i * 4.0f);

        SetupRound();
    }

    [Server]
    public void StartFastRound()
    {
        if (isRoundActive) return;
        currentType = RoundType.FastCombo;
        _upcomingImpacts.Clear();
        _clusterSizes.Clear();

        // 1. The Intro (Impacts at 8.0, 8.6, 9.2, 9.8) - Wraps up right before 10s
        AddComboWindow(0f, 8f, 4, 0.6f);

        // 2. The 13s Half-Beat Burst (Impacts at 13.0, 13.3, 13.6, 13.9)
        // 4 hits using 0.3f gap for that fast double-time feel
        AddComboWindow(10f, 3f, 4, 0.3f);

        // 3. Engagement Filler (15s to 38s)
        // Single strikes spaced 4 seconds apart to keep the player active
        AddComboWindow(14f, 4f, 1, 0.6f); // Hits at 18.0s
        AddComboWindow(18f, 4f, 1, 0.6f); // Hits at 22.0s
        AddComboWindow(22f, 4f, 1, 0.6f); // Hits at 26.0s
        AddComboWindow(26f, 4f, 1, 0.6f); // Hits at 30.0s
        AddComboWindow(30f, 4f, 1, 0.6f); // Hits at 34.0s

        // 4. The 38.4s BIG DROP
        // Starts exactly at 38.4s and runs 4 heavy hits
        AddComboWindow(34f, 4.4f, 4, 0.6f);

        // 5. Final Burst before the 52s Loop
        // Fast half-beats ending right at 48.9s
        AddComboWindow(41f, 7f, 4, 0.3f);

        currentComboCount = 4; // Ensures the buffer is open
        customIsCombo = true;

        SetupRound();
    }

    // Added 'beatGap' parameter to control how fast the chain executes
    private void AddComboWindow(float startTime, float inputWindow, int hits, float beatGap = 0.6f)
    {
        for (int j = 0; j < hits; j++)
        {
            // Hits fire sequentially based on the specific beat gap
            float t = startTime + inputWindow + (j * beatGap);
            _upcomingImpacts.Add(t);
        }
        for (int j = 0; j < hits; j++) _clusterSizes.Add(hits);
    }

    [Server]
    public void StartCustomRound()
    {
        if (isRoundActive) return;
        currentType = RoundType.CustomTrack;

        string saveKey = "CustomMap_" + BeatAnalyzer.Instance.audioSource.clip.name;
        if (!PlayerPrefs.HasKey(saveKey)) return;

        // Uses our new helper to build the timeline
        LoadCustomMapData();

        BeatAnalyzer.Instance.audioSource.Stop();
        BeatAnalyzer.Instance.audioSource.time = 0f;
        _pendingCustomAudioPlay = true;

        _isWindUpFired = false;
        SetupRound();
    }

    // Replace only this part of SetupRound in RhythmRoundManager.cs
    [Server]
    private void SetupRound()
    {
        EnsureBotExists();

        // Sync equipped cards from PlayerInventory to PlayerCombat for all players
        foreach (var player in GameManager.players)
        {
            if (player == null) continue;

            var pc = player.GetComponent<PlayerCombat>();
            var inv = player.GetComponent<PlayerInventory>();
            var cm = player.GetComponent<CardManager>();

            // Convert equipped card IDs to trigger names
            if (inv != null && pc != null)
            {
                var triggerList = new List<string>();
                foreach (var cardId in inv.equippedCombatCards)
                {
                    string trigger = CardIdToTrigger(cardId);
                    if (!string.IsNullOrEmpty(trigger))
                        triggerList.Add(trigger);
                }
                pc.availableCardsString = string.Join("|", triggerList);
                if (cm != null)
                    cm.availableCardsForRound = new List<string>(triggerList);
                Debug.Log($"<color=green>SETUP ROUND:</color> {player.PlayerName} equipped {inv.equippedCombatCards.Count} cards -> triggers: {pc.availableCardsString}");
            }

            if (cm != null)
            {
                cm.currentHandIndices.Clear();
                cm.ResetSlots(); // also draws the initial per-family hand (needed by humans AND the bot)
                Debug.Log($"<color=green>SERVER:</color> Reset slots + drew hand for {player.PlayerName}");
            }
        }

        lastBeatFireTime = 0f;
        currentChainPosition = 0;
        _clusterBeatsLeftToFire = (_clusterSizes.Count > 0) ? _clusterSizes[0] : 1;
        RpcClearLogs();
        RpcStartCountdown();
        StartCoroutine(DelayedRoundActivation(5f));
    }

    [Server]
    private IEnumerator DelayedRoundActivation(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (_pendingCustomAudioPlay)
        {
            _pendingCustomAudioPlay = false;
            BeatAnalyzer.Instance.audioSource.Play();
        }
        _startTime = NetworkTime.time + 1.0;
        isRoundActive = true;
    }

    [ClientRpc]
    private void RpcStartCountdown()
    {
        RoundCountdownUI.Instance?.StartCountdown(5);
    }

    private string CardIdToTrigger(string cardId)
    {
        return cardId.ToLower() switch
        {
            "jab" => "Jab",
            "cross" => "Cross",
            "hook" => "Hook",
            "block" => "Block",
            "dodge_left" => "Left",
            "dodge_right" => "Right",
            "reflect" => "ParryIntent",
            "boom" => "UnbreakablePunch",
            "grapple" => "Grapple",
            "fake" => "Fake",
            "clutch" => "Clutch",
            "uppercut" => "Uppercut",
            "sweep" => "Sweep",
            "focus" => "Focus",
            "taunt" => "Taunt",
            "overclock" => "Overclock",
            "reverse" => "Reverse",
            "trap" => "Trap",
            "cage" => "Cage",
            "mirror" => "Mirror",
            "striker" => "Striker",
            "tank" => "Tank",
            "speedster" => "Speedster",
            "grappler" => "Grappler",
            "trickster" => "Trickster",
            "vampire" => "Vampire",
            "glass" => "Glass",
            "momentum" => "Momentum",
            _ => cardId
        };
    }

    [Server]
    public void StopRound()
    {
        isRoundActive = false;
        _startTime = 0;
        _upcomingImpacts.Clear();
        _isWindUpFired = false;
        currentChainPosition = 0;
        lastBeatFireTime = 0f;
        _clusterBeatsLeftToFire = 0;
        _isEndingRound = false;

        // Clear player hands so the GUI hides
        foreach (var player in GameManager.players)
        {
            if (player != null)
            {
                CardManager cm = player.GetComponent<CardManager>();
                if (cm != null) cm.currentHandIndices.Clear();
            }
        }
    }

    [Server]
    private IEnumerator EndRoundRoutine()
    {
        _isEndingRound = true;
        yield return new WaitForSeconds(1.5f);

        var playerList = new List<PlayerController>(GameManager.players);

        // Bot is spawned via NetworkServer.Spawn but may not be in GameManager.players.
        // Always include it so solo-play rounds end and flow into the shop correctly.
        if (_activeBot != null)
        {
            var botCtrl = _activeBot.GetComponent<PlayerController>();
            if (botCtrl != null && !playerList.Contains(botCtrl))
                playerList.Add(botCtrl);
        }

        if (playerList.Count < 2)
        {
            _isEndingRound = false;
            yield break;
        }

        PlayerCombat p1 = playerList[0].GetComponent<PlayerCombat>();
        PlayerCombat p2 = playerList[1].GetComponent<PlayerCombat>();

        int roundWinner = 0; // 0=draw, 1=p1, 2=p2
        string winReason = "";

        if (p1.CurrentPercentage < p2.CurrentPercentage)
        {
            roundWinner = 1;
            winReason = "Lower %";
        }
        else if (p2.CurrentPercentage < p1.CurrentPercentage)
        {
            roundWinner = 2;
            winReason = "Lower %";
        }
        else
        {
            if (_p1RoundDamageDealt > _p2RoundDamageDealt)
            {
                roundWinner = 1;
                winReason = "More Damage";
            }
            else if (_p2RoundDamageDealt > _p1RoundDamageDealt)
            {
                roundWinner = 2;
                winReason = "More Damage";
            }
            else
            {
                roundWinner = 0;
                winReason = "Draw";
            }
        }

        int p1Credits = 0;
        int p2Credits = 0;

        // Base credits
        if (roundWinner == 1) { p1Credits += 2; p2Credits += 1; }
        else if (roundWinner == 2) { p1Credits += 1; p2Credits += 2; }
        else { p1Credits += 2; p2Credits += 2; }

        // Consecutive win/loss bonuses
        if (roundWinner == 1)
        {
            _p1WinStreak++; _p1LossStreak = 0;
            _p2LossStreak++; _p2WinStreak = 0;
            if (_p1WinStreak >= 2) p1Credits += 1; // +3 total
            if (_p2LossStreak >= 2) p2Credits += 1; // +2 total
        }
        else if (roundWinner == 2)
        {
            _p2WinStreak++; _p2LossStreak = 0;
            _p1LossStreak++; _p1WinStreak = 0;
            if (_p2WinStreak >= 2) p2Credits += 1;
            if (_p1LossStreak >= 2) p1Credits += 1;
        }
        else
        {
            _p1WinStreak = 0; _p1LossStreak = 0;
            _p2WinStreak = 0; _p2LossStreak = 0;
        }

        // Most Excellent bonus
        if (p1.roundExcellentCount > p2.roundExcellentCount) p1Credits += 1;
        else if (p2.roundExcellentCount > p1.roundExcellentCount) p2Credits += 1;
        else { p1Credits += 1; p2Credits += 1; }

        // Perfect Counter bonus
        if (p1.roundCounterCount > p2.roundCounterCount) p1Credits += 1;
        else if (p2.roundCounterCount > p1.roundCounterCount) p2Credits += 1;
        else { p1Credits += 1; p2Credits += 1; }

        p1TotalCredits += p1Credits;
        p2TotalCredits += p2Credits;

        // Transfer earned credits to PlayerInventory for shop spending.
        // Plus a flat +3 Trait Tokens to BOTH players every round (separate trait economy).
        var p1Inv = playerList[0].GetComponent<PlayerInventory>();
        var p2Inv = playerList[1].GetComponent<PlayerInventory>();
        if (p1Inv != null) { p1Inv.AddCredits(p1Credits); p1Inv.traitTokens += 3; }
        if (p2Inv != null) { p2Inv.AddCredits(p2Credits); p2Inv.traitTokens += 3; }

        if (roundWinner == 1) p1RoundWins++;
        else if (roundWinner == 2) p2RoundWins++;
        currentRoundNumber++;

        RpcShowRoundResult(roundWinner, winReason, p1Credits, p2Credits,
                           p1.CurrentPercentage, p2.CurrentPercentage,
                           p1.roundExcellentCount, p2.roundExcellentCount,
                           p1.roundCounterCount, p2.roundCounterCount);

        yield return new WaitForSeconds(3f);

        if (p1RoundWins >= 5 || p2RoundWins >= 5 || currentRoundNumber > 9)
        {
            if (p1RoundWins > p2RoundWins) matchWinner = 1;
            else if (p2RoundWins > p1RoundWins) matchWinner = 2;
            else matchWinner = 3;
            isMatchOver = true;

            RpcShowMatchResult(matchWinner, p1RoundWins, p2RoundWins, p1TotalCredits, p2TotalCredits);
            yield return new WaitForSeconds(5f);
            NetworkManager.singleton.ServerChangeScene(SceneManager.GetActiveScene().name);
            yield break;
        }

        ResetPlayersForNextRound();
        // Post-round TFT shop with credits
        ShopPhaseManager.Instance?.StartShopPhase(currentRoundNumber);
    }

    [Server]
    private void ResetPlayersForNextRound()
    {
        _p1RoundDamageDealt = 0;
        _p2RoundDamageDealt = 0;

        int idx = 0;
        foreach (var player in GameManager.players)
        {
            if (player == null) continue;

            PlayerCombat pc = player.GetComponent<PlayerCombat>();
            if (pc != null) pc.ResetRoundStats();

            // Equip up to 10 owned combat cards for next round
            var inv = player.GetComponent<PlayerInventory>();
            if (inv != null)
            {
                var equipList = new System.Collections.Generic.List<string>(inv.ownedCombatCards);
                inv.EquipCombatCards(equipList);
            }

            Vector3 spawnPos = (idx == 0) ? _spawnP1 : _spawnP2;
            player.transform.position = spawnPos;

            PlayerController opponent = player.GetComponent<PlayerController>().GetOpponent();
            if (opponent != null)
            {
                Vector3 lookDir = opponent.transform.position - spawnPos;
                lookDir.y = 0;
                if (lookDir != Vector3.zero)
                    player.transform.rotation = Quaternion.LookRotation(lookDir);
            }
            idx++;
        }

        // Bot is reused between rounds
        if (_activeBot != null)
        {
            PlayerCombat botCombat = _activeBot.GetComponent<PlayerCombat>();
            if (botCombat != null) botCombat.ResetRoundStats();
            _activeBot.transform.position = _spawnP2;

            PlayerController botController = _activeBot.GetComponent<PlayerController>();
            PlayerController human = null;
            foreach (var p in GameManager.players)
                if (p != null && p.GetComponent<BotController>() == null) { human = p; break; }
            if (human != null && botController != null)
            {
                Vector3 lookDir = human.transform.position - _spawnP2;
                lookDir.y = 0;
                if (lookDir != Vector3.zero)
                    _activeBot.transform.rotation = Quaternion.LookRotation(lookDir);
            }
        }
    }

    [ClientRpc]
    private void RpcShowRoundResult(int winner, string reason, int p1Credits, int p2Credits,
                                     float p1Pct, float p2Pct,
                                     int p1Exc, int p2Exc,
                                     int p1Ctr, int p2Ctr)
    {
        // Round result overlay drawn in OnGUI via _lastRoundResult fields
        // This is a lightweight RPC; actual display is handled in OnGUI
        Debug.Log($"<color=yellow>[ROUND END]</color> Winner: {winner} | Reason: {reason} | P1: +{p1Credits} credits | P2: +{p2Credits} credits");
    }

    [ClientRpc]
    private void RpcShowMatchResult(int winner, int p1Wins, int p2Wins, int p1Credits, int p2Credits)
    {
        Debug.Log($"<color=green>[MATCH END]</color> Winner: {winner} | Score {p1Wins}-{p2Wins} | P1 Credits: {p1Credits} | P2 Credits: {p2Credits}");
    }

    [ClientRpc] private void RpcClearLogs() { combatLogs.Clear(); }

    private void Update()
    {
        // --- ROUND PICKER AUTO-SELECT ---
        // Runs BEFORE the round-active early-return and independently of OnGUI, so it
        // still fires in VR where the IMGUI picker is suppressed.
        if (isServer && isRoundPickerActive)
        {
            _roundPickerTimer -= Time.deltaTime;

            // VR: press B (right controller) to pick the first rhythm option immediately.
            // Solo host reads its own controller here. Edge-triggered so one press = one select.
            bool bNow = VRCameraDriver.VRActive && ReadRightB();
            if (bNow && !_prevPickerB && !_usedRoundTypes.Contains(RoundType.SlowRhythm))
                SelectRoundType(RoundType.SlowRhythm);
            _prevPickerB = bNow;

            if (_roundPickerTimer <= 0f)
            {
                if (!_usedRoundTypes.Contains(RoundType.SlowRhythm))
                    SelectRoundType(RoundType.SlowRhythm);
                else if (!_usedRoundTypes.Contains(RoundType.FastCombo))
                    SelectRoundType(RoundType.FastCombo);
            }
        }

        if (!isRoundActive || _startTime == 0) return;
        if (_tiebreakerPaused) return;

        if (isServer)
        {
            // --- ROUND END CHECKS ---
            bool shouldEndRound = false;
            float currentTime = GetCurrentTrackTime();

            if (_upcomingImpacts.Count == 0) shouldEndRound = true;

            // Hard 60-second cap on WALL-CLOCK time (not audio time). The audio clock
            // (audioSource.time) plateaus when a custom clip ends, so beats scheduled past
            // the clip's length would otherwise never fire and the round would hang forever.
            double wallElapsed = NetworkTime.time - _startTime;
            if (wallElapsed >= 60.0) shouldEndRound = true;

            if (shouldEndRound && !_isEndingRound)
            {
                StopRound();
                StartCoroutine(EndRoundRoutine());
                return;
            }

            if (_upcomingImpacts.Count == 0) return;

            {
                float targetBeat = _upcomingImpacts[0];

                // Wind Up Logic
                if (!_isWindUpFired && currentTime >= targetBeat - windUpTime)
                {
                    _isWindUpFired = true;
                    foreach (var player in GameManager.players)
                    {
                        if (player != null)
                        {
                            BotController bot = player.GetComponent<BotController>();
                            PlayerCombat pc = player.GetComponent<PlayerCombat>();
                            if (bot != null)
                                bot.ThinkNextMove();
                            else if (!IsSingleMoveMode())
                                AssignRandomComboMove(pc);
                            pc.ExecuteRhythmWindUp();
                        }
                    }

                    // Tiebreaker minigame disabled — same-family offense clashes are now resolved
                    // by the better-timing logic in ResolveRhythmCombat (closer to the beat wins).
                }

                // Impact Logic
                if (currentTime >= targetBeat)
                {
                    _isWindUpFired = false;
                    ExecutePulseImpact();

                    _upcomingImpacts.RemoveAt(0);
                    if (_clusterSizes.Count > 0) _clusterSizes.RemoveAt(0);

                    // Update input timing bar state
                    lastBeatFireTime = targetBeat;
                    _clusterBeatsLeftToFire--;
                    if (_clusterBeatsLeftToFire <= 0)
                    {
                        currentChainPosition = 0;
                        _clusterBeatsLeftToFire = (_clusterSizes.Count > 0) ? _clusterSizes[0] : 0;
                    }
                    else
                    {
                        currentChainPosition++;
                    }

                    if (_clusterSizes.Count > 0)
                    {
                        currentComboCount = _clusterSizes[0];
                        if (currentType == RoundType.CustomTrack)
                            customIsCombo = (currentComboCount > 1);
                    }

                    // Skip hit-stop when tiebreaker just triggered — its HitStopRoutine
                    // would reset Time.timeScale to 1.0 and cancel the slow-mo.
                    if (!_tiebreakerPaused)
                        RpcTriggerHitStop(_heavyHitThisBeat);
                    _heavyHitThisBeat = false;
                }
            }
        }
    }

    [Server]
    private void LoadCustomMapData()
    {
        string saveKey = "CustomMap_" + BeatAnalyzer.Instance.audioSource.clip.name;
        if (!PlayerPrefs.HasKey(saveKey)) return;

        _upcomingImpacts.Clear();
        _clusterSizes.Clear();
        string rawData = PlayerPrefs.GetString(saveKey);
        string[] rawTimes = rawData.Split('|');

        List<float> loadedTaps = new List<float>();
        foreach (string tStr in rawTimes)
        {
            if (float.TryParse(tStr, out float t)) loadedTaps.Add(t);
        }
        loadedTaps.Sort(); // Ensure perfect chronological order

        int i = 0;
        while (i < loadedTaps.Count)
        {
            int chainCount = 1;

            // Look ahead: Gap must be < 1.4s AND the chain cannot exceed 4 hits
            while (i + chainCount < loadedTaps.Count &&
                  (loadedTaps[i + chainCount] - loadedTaps[i + chainCount - 1]) < 1.4f &&
                  chainCount < 5)
            {
                chainCount++;
            }

            for (int j = 0; j < chainCount; j++)
            {
                _upcomingImpacts.Add(loadedTaps[i + j]);
                _clusterSizes.Add(chainCount);
            }

            i += chainCount; 
        }

        if (_clusterSizes.Count > 0)
        {
            currentComboCount = _clusterSizes[0];
            customIsCombo = (currentComboCount > 1);
        }
    }




   [Server]
    private void ResolveRhythmCombat()
    {
        var playerList = new List<PlayerController>(GameManager.players);
        if (playerList.Count < 2) return;

        PlayerController pc1 = playerList[0];
        PlayerController pc2 = playerList[1];
        PlayerCombat p1 = pc1.GetComponent<PlayerCombat>();
        PlayerCombat p2 = pc2.GetComponent<PlayerCombat>();

        var m1 = p1.PeekNextMove();
        var m2 = p2.PeekNextMove();

        // --- NEW: GRADE TIMING BEFORE DAMAGE ---
        EvaluateAndSendFeedback(p1, m1);
        EvaluateAndSendFeedback(p2, m2);

        // --- COMBO MODE: timing-clash only, no RPS ---
        // Only the player with better timing (smaller offset from the beat) deals damage.
        if (!IsSingleMoveMode())
        {
            float beatTime = GetNextBeatTime();
            float p1Off = p1.lastVocalSpikeTime > 0f ? Mathf.Abs(beatTime - p1.lastVocalSpikeTime) : float.MaxValue;
            float p2Off = p2.lastVocalSpikeTime > 0f ? Mathf.Abs(beatTime - p2.lastVocalSpikeTime) : float.MaxValue;

            int dmgFrom1 = p1.lastVocalSpikeTime > 0f ? ComputeComboDamage(m1.attack, p1Off) : 0;
            int dmgFrom2 = p2.lastVocalSpikeTime > 0f ? ComputeComboDamage(m2.attack, p2Off) : 0;

            if (p1Off < p2Off && dmgFrom1 > 0)
            {
                // p1 wins the timing clash — only p2 takes damage
                if (p1.connectionToClient != null) p1.TargetPlaySuccessSound("Attack");
                if (p2.connectionToClient != null) p2.TargetPlaySuccessSound("Hurt");
                PlayHitParticle(p2.transform.position);
                p2.TakeDamage(dmgFrom1, (p2.transform.position - p1.transform.position).normalized);
                RpcLogCombatTrade(pc1.PlayerName, FormatMove(m1), 1, 0,
                                  pc2.PlayerName, FormatMove(m2), -1, dmgFrom1, "Better timing wins");
            }
            else if (p2Off < p1Off && dmgFrom2 > 0)
            {
                // p2 wins the timing clash — only p1 takes damage
                if (p2.connectionToClient != null) p2.TargetPlaySuccessSound("Attack");
                if (p1.connectionToClient != null) p1.TargetPlaySuccessSound("Hurt");
                PlayHitParticle(p1.transform.position);
                p1.TakeDamage(dmgFrom2, (p1.transform.position - p2.transform.position).normalized);
                RpcLogCombatTrade(pc1.PlayerName, FormatMove(m1), -1, dmgFrom2,
                                  pc2.PlayerName, FormatMove(m2), 1, 0, "Better timing wins");
            }
            else
            {
                // Tie or both missed — no damage
                RpcLogCombatTrade(pc1.PlayerName, FormatMove(m1), 0, 0,
                                  pc2.PlayerName, FormatMove(m2), 0, 0, "Timing tied");
            }
            return;
        }

        float targetBeat = GetNextBeatTime();

        // --- TAUNT: punish a player who was ordered to Strike last beat but didn't ---
        CheckMustStrike(p1, m1);
        CheckMustStrike(p2, m2);

        // --- TIMING CLASH: same-family offense (Strike v Strike, Throw v Throw) ---
        // The triangle can't separate two of the same attack type, so better beat timing wins;
        // the loser is treated as interrupted (whiffs). Different offense (Strike vs Throw) is
        // decided by the triangle in DoesInterrupt instead.
        bool p1WinsTie = false;
        bool p2WinsTie = false;
        CardFamily fam1 = GetFamily(m1.attack);
        CardFamily fam2 = GetFamily(m2.attack);
        if (IsOffense(fam1) && fam1 == fam2)
        {
            float p1Off = p1.lastVocalSpikeTime > 0f ? Mathf.Abs(targetBeat - p1.lastVocalSpikeTime) : float.MaxValue;
            float p2Off = p2.lastVocalSpikeTime > 0f ? Mathf.Abs(targetBeat - p2.lastVocalSpikeTime) : float.MaxValue;
            if (p1Off < p2Off) p1WinsTie = true;
            else if (p2Off < p1Off) p2WinsTie = true;
        }

        // --- BULLETPROOF PROTECTION ---
        bool p1Protected = IsProtected(m1.attack);
        bool p2Protected = IsProtected(m2.attack);

        // --- INTERRUPTION LOGIC (expanded RPS for all 20 cards) ---
        bool p1Interrupted = !p1Protected && (p2WinsTie || DoesInterrupt(m2.attack, m1.attack, out _));
        bool p2Interrupted = !p2Protected && (p1WinsTie || DoesInterrupt(m1.attack, m2.attack, out _));

        int p1DamageTaken = 0;
        int p2DamageTaken = 0;

        // Penalty only applies if NOT protected
        if (p1Interrupted) { p1.TakeDamage(5); p1DamageTaken += 5; }
        if (p2Interrupted) { p2.TakeDamage(5); p2DamageTaken += 5; }

        // PROCESS ACTUAL HITS
        int p1Result = ProcessDamage(p1, m1, p2, m2, p1Interrupted, out int dmgToP2, out string p1Reason);
        int p2Result = ProcessDamage(p2, m2, p1, m1, p2Interrupted, out int dmgToP1, out string p2Reason);

        p2DamageTaken += dmgToP2;
        p1DamageTaken += dmgToP1;

        // Track damage dealt for round economy
        if (dmgToP2 > 0) _p1RoundDamageDealt += dmgToP2;
        if (dmgToP1 > 0) _p2RoundDamageDealt += dmgToP1;

        int p1State = (p1Result == 1 || p1Result == -1) ? 1 : (p1DamageTaken > 0 ? -1 : 0);
        int p2State = (p2Result == 1 || p2Result == -1) ? 1 : (p2DamageTaken > 0 ? -1 : 0);

        string tradeReason = !string.IsNullOrEmpty(p1Reason) ? p1Reason : p2Reason;
        RpcLogCombatTrade(pc1.PlayerName, FormatMove(m1), p1State, p1DamageTaken, pc2.PlayerName, FormatMove(m2), p2State, p2DamageTaken, tradeReason);

        // --- COUNTER BONUS: winning a trade gives +1 slot of the opposite type ---
        CardManager cm1 = pc1.GetComponent<CardManager>();
        CardManager cm2 = pc2.GetComponent<CardManager>();
        bool p1UsedAttack = !string.IsNullOrEmpty(m1.attack) && CardManager.IsAttackTrigger(m1.attack);
        bool p2UsedAttack = !string.IsNullOrEmpty(m2.attack) && CardManager.IsAttackTrigger(m2.attack);
        bool p1ActiveDef  = (!string.IsNullOrEmpty(m1.attack) && CardManager.IsDefenseTrigger(m1.attack)) || m1.dash != Vector3.zero;
        bool p2ActiveDef  = (!string.IsNullOrEmpty(m2.attack) && CardManager.IsDefenseTrigger(m2.attack)) || m2.dash != Vector3.zero;

        // Attack landed → attacker gains DEF slot
        if (p1Result == 1 && dmgToP2 > 0)  GrantCounterBonus(cm1, p1, true);
        if (p2Result == 1 && dmgToP1 > 0)  GrantCounterBonus(cm2, p2, true);

        // Defense held against an attack → defender gains ATK slot
        if (p2UsedAttack && dmgToP1 == 0 && p2Result == 0 && p1ActiveDef && !p1UsedAttack) GrantCounterBonus(cm1, p1, false);
        if (p1UsedAttack && dmgToP2 == 0 && p1Result == 0 && p2ActiveDef && !p2UsedAttack) GrantCounterBonus(cm2, p2, false);

        // Parry success → parrier gains ATK slot (p1Result==-1 means p2's parry reflected p1's attack)
        if (p1Result == -1) GrantCounterBonus(cm2, p2, false);
        if (p2Result == -1) GrantCounterBonus(cm1, p1, false);

        // Timing tie win → winner gains opposite slot
        if (p1WinsTie) GrantCounterBonus(cm1, p1, p1UsedAttack);
        if (p2WinsTie) GrantCounterBonus(cm2, p2, p2UsedAttack);
    }

    [Server]
    private int ProcessDamage(PlayerCombat attacker, PlayerCombat.RhythmAction move, PlayerCombat defender, PlayerCombat.RhythmAction defMove, bool isInterrupted, out int damageDealt, out string tradeReason)
    {
        damageDealt = 0;
        tradeReason = "";
        if (string.IsNullOrEmpty(move.attack)) return 0;
        if (attacker.IsStaggered) return 0;

        bool defenderStaggered = defender.IsStaggered;
        string atk = move.attack;
        string def = defMove.attack;

        // THROW family breaks through all defense (Block, Dodge, Parry). Strike does not.
        bool throwBreaks = GetFamily(atk) == CardFamily.Throw;

        // --- HANDLE CARD EFFECTS THAT PERSIST FROM LAST TURN ---
        // Trap: trigger if opponent moves or blocks
        if (defender.HasPendingTrap && !defenderStaggered)
        {
            if (atk == "Left" || atk == "Right" || atk == "Block")
            {
                int trapDmg = 15;
                if (attacker.activeTraitId == "trickster")
                    trapDmg *= 2;
                trapDmg = ApplyTraitMultiplier(trapDmg);
                defender.TakeDamage(trapDmg, isOpponentDamage: true);
                damageDealt = trapDmg;
                tradeReason = "Trap sprung";
                if (defender.connectionToClient != null) defender.TargetPlaySuccessSound("Hurt");
                PlayHitParticle(defender.transform.position);
                defender.HasPendingTrap = false;
                return 1;
            }
            defender.HasPendingTrap = false;
        }

        // Cage: trigger if opponent plays a card
        if (defender.HasPendingCage && !defenderStaggered && !string.IsNullOrEmpty(atk))
        {
            int cageDmg = 10;
            if (attacker.activeTraitId == "trickster")
                cageDmg *= 2;
            cageDmg = ApplyTraitMultiplier(cageDmg);
            defender.TakeDamage(cageDmg, isOpponentDamage: true);
            damageDealt = cageDmg;
            tradeReason = "Cage punished";
            if (defender.connectionToClient != null) defender.TargetPlaySuccessSound("Hurt");
            PlayHitParticle(defender.transform.position);
            defender.HasPendingCage = false;
            return 1;
        }

        // Interrupted attacker whiffs entirely — unless the attack is unstoppable (Boom/Overclock/Reverse).
        // Covers Strike-beats-Throw and the loser of a same-family timing clash. (Traps/Cages above still punish.)
        if (isInterrupted && !IsProtected(atk))
        {
            tradeReason = $"{atk} was interrupted";
            return 0;
        }

        // --- 1. DEFENSE CHECKS (bypassed when staggered) ---
        // Fake: cancels opponent defense with at least good timing
        if (!defenderStaggered && atk == "Fake")
        {
            float fSpike = attacker.lastVocalSpikeTime;
            float offset = Mathf.Abs(GetNextBeatTime() - fSpike);
            if (fSpike > 0 && offset <= 0.3f)
            {
                if (attacker.connectionToClient != null) attacker.TargetPlaySuccessSound("Attack");
                int fakeDmg = GetBaseDamage("Fake", attacker);
                // FakeCounter Lv3: double damage if defender defended last beat
                var fakeCard = GetCardData("Fake");
                int fakeLvl = GetCardLevel(attacker, "Fake");
                if (fakeCard != null && fakeCard.PerkActiveAt(fakeLvl) && defender.DefendedLastBeat)
                {
                    fakeDmg *= 2;
                    tradeReason = "Fake punished the defender — DOUBLE DAMAGE";
                }
                else tradeReason = "Fake slipped through";
                if (attacker.activeTraitId == "trickster") fakeDmg *= 2;
                fakeDmg = ApplyTraitMultiplier(fakeDmg);
                if (defender.connectionToClient != null) defender.TargetPlaySuccessSound("Hurt");
                PlayHitParticle(defender.transform.position);
                Vector3 dir = (defender.transform.position - attacker.transform.position).normalized;
                defender.TakeDamage(fakeDmg, dir, isOpponentDamage: true);
                damageDealt = fakeDmg;
                return 1;
            }
            return 0;
        }

        // Mirror: returns damage (Lv1=+15%, Lv2=+20%, Lv3=+25% bonus). A Throw breaks through it.
        if (!defenderStaggered && !throwBreaks && def == "Mirror" && defender.HasMirrorBuff)
        {
            if (defender.connectionToClient != null) defender.TargetPlaySuccessSound("Parry");
            PlayHitParticle(defender.transform.position);
            int baseDmg = GetBaseDamage(atk, attacker);
            int mirLvl = GetCardLevel(defender, "Mirror");
            float mirBonus = mirLvl >= 3 ? 1.25f : mirLvl == 2 ? 1.20f : 1.15f;
            int reflectedDmg = Mathf.RoundToInt(ApplyTraitMultiplier(Mathf.RoundToInt(baseDmg * mirBonus)) * CardUpgradeMult(defender, def));
            Vector3 kbDir = (attacker.transform.position - defender.transform.position).normalized;
            attacker.TakeDamage(reflectedDmg, kbDir);
            defender.HasMirrorBuff = false;
            tradeReason = "Mirror returned the hit";
            return -1;
        }

        // ParryIntent: full reflect on good timing (≤0.42s), 50% block on loose timing (≤0.58s). A Throw breaks through it.
        if (!defenderStaggered && !throwBreaks && def == "ParryIntent")
        {
            float pSpike = defender.lastVocalSpikeTime;
            float offset = Mathf.Abs(GetNextBeatTime() - pSpike);
            if (pSpike > 0 && offset <= DefenseWindow(defender, "ParryIntent", 0.42f))
            {
                if (defender.connectionToClient != null) defender.TargetPlaySuccessSound("Parry");
                PlayHitParticle(defender.transform.position);
                int baseDmg = GetBaseDamage(atk, attacker);
                // Reward the read: reflect 150% of the attack's damage, with a satisfying floor.
                int reflectedDmg = Mathf.Max(12, Mathf.RoundToInt(ApplyTraitMultiplier(Mathf.RoundToInt(baseDmg * 1.5f)) * CardUpgradeMult(defender, def)));
                Vector3 parryDir = (attacker.transform.position - defender.transform.position).normalized;
                attacker.TakeDamage(reflectedDmg, parryDir, isOpponentDamage: true);
                tradeReason = "Reflect punished the attack";
                return -1;
            }
            else if (pSpike > 0 && offset <= DefenseWindow(defender, "ParryIntent", 0.58f))
            {
                // Good timing — block 50% damage, no reflect
                if (defender.connectionToClient != null) defender.TargetPlaySuccessSound("Block");
                PlayHitParticle(defender.transform.position);
                int baseDmg = GetBaseDamage(atk, attacker);
                int partialDmg = ApplyTraitMultiplier(Mathf.RoundToInt(baseDmg * 0.5f));
                Vector3 parryDir = (attacker.transform.position - defender.transform.position).normalized;
                defender.TakeDamage(partialDmg, parryDir, isOpponentDamage: true);
                damageDealt = partialDmg;
                tradeReason = "Parry deflected half";
                return 1;
            }
        }

        // Reverse: high risk/reward — negate damage on good+ timing, or take 50% more on bad. A Throw breaks through it.
        if (!defenderStaggered && !throwBreaks && def == "Reverse")
        {
            float rSpike = defender.lastVocalSpikeTime;
            float offset = Mathf.Abs(GetNextBeatTime() - rSpike);
            int baseDmg = GetBaseDamage(atk, attacker);
            Vector3 revDir = (attacker.transform.position - defender.transform.position).normalized;

            if (rSpike > 0 && offset <= DefenseWindow(defender, "Reverse", 0.42f))
            {
                if (defender.connectionToClient != null) defender.TargetPlaySuccessSound("Parry");
                PlayHitParticle(defender.transform.position);
                int returnDmg = Mathf.RoundToInt(ApplyTraitMultiplier(baseDmg) * CardUpgradeMult(defender, def));
                attacker.TakeDamage(returnDmg, revDir);
                tradeReason = "Reverse countered";
                // ReverseLeech Lv3: heal 5% of returned damage as HP
                var revCard = GetCardData("Reverse");
                int revLvl = GetCardLevel(defender, "Reverse");
                if (revCard != null && revCard.PerkActiveAt(revLvl))
                {
                    int healAmt = Mathf.Max(1, Mathf.RoundToInt(returnDmg * 0.05f));
                    defender.CurrentPercentage -= healAmt; // reduce % = heal
                    tradeReason += " + LEECH";
                }
                return -1;
            }
            else
            {
                int failedReverseDmg = ApplyTraitMultiplier(Mathf.RoundToInt(baseDmg * 1.25f));
                defender.TakeDamage(failedReverseDmg, revDir);
                damageDealt = failedReverseDmg;
                tradeReason = "Reverse mistimed — took extra damage";
                return 1;
            }
        }

        // Clutch: perfect (≤0.24s) = nullify + reflect; good (≤0.44s) = full block; miss = self-damage. A Throw breaks through it.
        if (!defenderStaggered && !throwBreaks && def == "Clutch")
        {
            float cSpike = defender.lastVocalSpikeTime;
            float offset = Mathf.Abs(GetNextBeatTime() - cSpike);
            bool isHeavy = (atk == "UnbreakablePunch" || atk == "Hook" || atk == "Overclock");
            if (cSpike > 0 && offset <= DefenseWindow(defender, "Clutch", 0.24f) && isHeavy)
            {
                // Perfect clutch — nullify and reflect DOUBLE the heavy hit's damage.
                if (defender.connectionToClient != null) defender.TargetPlaySuccessSound("Parry");
                PlayHitParticle(defender.transform.position);
                int baseDmg = GetBaseDamage(atk, attacker);
                int clutchReflect = Mathf.RoundToInt(ApplyTraitMultiplier(Mathf.RoundToInt(baseDmg * 2f)) * CardUpgradeMult(defender, def));
                Vector3 clutchDir = (attacker.transform.position - defender.transform.position).normalized;
                attacker.TakeDamage(clutchReflect, clutchDir, isOpponentDamage: true);
                tradeReason = "CLUTCH! Perfect counter";

                // Clutch Lv3 perk: a perfect clutch also heals you 8%.
                if (GetCardLevel(defender, "Clutch") >= 3)
                {
                    defender.CurrentPercentage = Mathf.Max(0f, defender.CurrentPercentage - 8f);
                    tradeReason += " + HEAL";
                }
                return -1;
            }
            else if (cSpike > 0 && offset <= DefenseWindow(defender, "Clutch", 0.44f) && isHeavy)
            {
                // Good clutch — full block, no reflect, no self-damage
                if (defender.connectionToClient != null) defender.TargetPlaySuccessSound("Block");
                PlayHitParticle(defender.transform.position);
                tradeReason = "Clutch held the line";
                return 0;
            }
            else
            {
                // Failed clutch — self-damage (Lv3: reduced from 20% to 10%)
                var clutchCard = GetCardData("Clutch");
                int clutchLvl = GetCardLevel(defender, "Clutch");
                float missRatio = (clutchCard != null && clutchCard.PerkActiveAt(clutchLvl)) ? 0.10f : 0.20f;
                int clutchBaseDmg = GetBaseDamage(atk, attacker);
                int selfDmg = ApplyTraitMultiplier(Mathf.RoundToInt(clutchBaseDmg * missRatio));
                defender.TakeDamage(selfDmg);
                damageDealt = selfDmg;
                tradeReason = "Clutch mistimed — self-damage";
                return 0;
            }
        }

        // Movement dodge check — Dodge (Block family) RELIABLY evades Strikes whenever you dash;
        // a Throw catches it. No tight timing gate: playing the dodge is enough.
        bool moveSuccessful = false;
        if (!defenderStaggered && defMove.dash != Vector3.zero)
        {
            if (throwBreaks)
            {
                moveSuccessful = false; // a Throw catches the dodge
            }
            else
            {
                moveSuccessful = true;
                if (defender.connectionToClient != null) defender.TargetPlaySuccessSound("Dash");
                PlayHitParticle(defender.transform.position);

                // Dodge Lv3 perk: slipping a Strike sets up a whiff-punish — your next attack is charged.
                var dodgeCard = GetCardData(def);
                if (dodgeCard != null && dodgeCard.perk == CardPerk.DodgeEvade && dodgeCard.PerkActiveAt(GetCardLevel(defender, def)))
                    defender.BlockChargeReady = true;
            }
        }

        // Block check — Block beats Strike by the family rule, so it RELIABLY stops Strikes
        // whenever you play it. Throws break through. Timing only adds a bonus; it never makes
        // the block FAIL (that "block but still got hit" feel was a tight-timing gate).
        float blockMitigation = 0f;
        if (!defenderStaggered && def == "Block")
        {
            if (throwBreaks)
            {
                blockMitigation = 0f; // Throws bypass Block
            }
            else
            {
                blockMitigation = 1.0f; // full stop vs Strikes

                // Piercing trait: chips through 70% of the block
                if (attacker.activeTraitId == "piercing")
                    blockMitigation = 0.30f;

                // On-beat bonus only (block itself never fails): trait + perk rewards.
                float bSpike = defender.lastVocalSpikeTime;
                bool onBeat = bSpike > 0 && Mathf.Abs(GetNextBeatTime() - bSpike) <= 0.4f;
                if (onBeat)
                {
                    if (defender.activeTraitId == "stalwart") defender.HasStalwartBuff = true;
                    var blkCard = GetCardData("Block");
                    int blkLvl = GetCardLevel(defender, "Block");
                    if (blkCard != null && blkCard.PerkActiveAt(blkLvl) && blkCard.perk == CardPerk.BlockCounter)
                        defender.BlockChargeReady = true;
                }

                if (defender.connectionToClient != null) defender.TargetPlaySuccessSound("Block");
                PlayHitParticle(defender.transform.position);
            }
        }

        // --- 2. CALCULATE ATTACK DAMAGE ---
        int finalDmg = GetBaseDamage(atk, attacker); // level-aware from CardDatabase

        // Timing multiplier: EXCELLENT +25%, GOOD base, BAD −50%
        finalDmg = Mathf.RoundToInt(finalDmg * GetTimingMultiplier(attacker));

        // Volume bonus: louder shout = up to +25%
        float atkSpike = attacker.lastVocalSpikeTime;
        if (atkSpike > 0f && attacker.lastVocalSpikeVolume > 0f)
        {
            const float volThreshold = 0.4f;
            float vol = attacker.lastVocalSpikeVolume;
            if (vol > volThreshold)
            {
                float t = Mathf.Clamp01((vol - volThreshold) / (1f - volThreshold));
                finalDmg = Mathf.RoundToInt(finalDmg * Mathf.Lerp(1f, 1.25f, t));
            }
        }

        // Chain trait: +5% per consecutive hit
        if (!string.IsNullOrEmpty(attacker.activeTraitId) && attacker.activeTraitId == "chain")
        {
            int chainBonus = Mathf.RoundToInt(finalDmg * 0.05f * attacker.ConsecutiveHitsChain);
            finalDmg += chainBonus;
        }

        // Momentum trait: +10% per consecutive hit (max +40%)
        if (attacker.activeTraitId == "momentum")
        {
            int momentumBonus = Mathf.RoundToInt(finalDmg * Mathf.Min(0.10f * attacker.ConsecutiveHitsChain, 0.40f));
            finalDmg += momentumBonus;
        }

        // Grappler trait: +30% damage on Grapple for the whole round
        if (atk == "Grapple" && attacker.activeTraitId == "grappler")
        {
            finalDmg = Mathf.RoundToInt(finalDmg * 1.30f);
        }

        // ── PER-CARD PERKS: pre-hit modifiers ──────────────────────────────
        var atkCard = GetCardData(atk);
        int atkLvl  = GetCardLevel(attacker, atk);
        bool perkOn = atkCard != null && atkCard.PerkActiveAt(atkLvl);

        // Boom self-cost: pay 5% HP to throw (Lv3 perk: reduced to 2%).
        if (atk == "UnbreakablePunch")
        {
            float boomRatio = (atkLvl >= 3) ? 0.02f : 0.05f;
            attacker.TakeDamage(Mathf.Max(1, Mathf.RoundToInt(attacker.CurrentPercentage * boomRatio)));
        }

        // CounterBonus (Cross Lv3 / Uppercut Lv3): +20% if opponent attacked last beat
        if (perkOn && atkCard.perk == CardPerk.CounterBonus && defender.AttackedLastBeat)
            finalDmg = Mathf.RoundToInt(finalDmg * 1.20f);

        // BlockChargeReady (Block Lv3): attacker had a block-charge from last beat
        if (attacker.BlockChargeReady)
        {
            finalDmg = Mathf.RoundToInt(finalDmg * 1.15f);
            attacker.BlockChargeReady = false;
        }

        // Focus buff: Lv3 makes it uninterruptible (already tracked via HasFocusBuff flag)
        if (attacker.HasFocusBuff)
        {
            finalDmg = Mathf.RoundToInt(finalDmg * 1.5f);
            attacker.HasFocusBuff = false;
        }

        // Stalwart buff
        if (attacker.HasStalwartBuff)
        {
            finalDmg = Mathf.RoundToInt(finalDmg * 1.10f);
            attacker.HasStalwartBuff = false;
        }

        // Apply trait multiplier to final damage
        finalDmg = ApplyTraitMultiplier(finalDmg, attacker);

        // Card upgrade level: +15% damage per level above 1 (Lv2 = +15%, Lv3 = +30%).
        finalDmg = Mathf.RoundToInt(finalDmg * CardUpgradeMult(attacker, atk));

        // --- 3. HIT DETECTION (rock-paper-scissors) ---
        bool hits = false;
        if (defenderStaggered)
        {
            hits = true;
            tradeReason = $"{atk} hit staggered opponent";
        }
        else if (isInterrupted && !IsProtected(atk))
        {
            hits = false;
            tradeReason = $"{atk} was interrupted";
        }
        else
        {
            hits = EvaluateHit(atk, def, moveSuccessful);
            if (!hits)
            {
                if (moveSuccessful) tradeReason = "Dodge evaded the strike";
                else if (blockMitigation > 0f) tradeReason = "Block absorbed the hit";
                else tradeReason = $"{atk} whiffed";
            }
        }

        // --- SUPPORT / SETUP MOVES — resolve when NOT interrupted (they don't "hit"). ---
        // Stuffed by any offense (isInterrupted covers that); they land when the opponent plays safe.
        if (!isInterrupted && !defenderStaggered)
        {
            var setupCard = GetCardData(atk);
            int setupLvl  = GetCardLevel(attacker, atk);
            bool setupLv3 = setupCard != null && setupCard.PerkActiveAt(setupLvl);
            bool tricky   = attacker.activeTraitId == "trickster";

            switch (atk)
            {
                case "Focus":
                    attacker.HasFocusBuff = true;
                    tradeReason = "Focus — next Strike powered up";
                    break;
                case "Mirror":
                    attacker.HasMirrorBuff = true;
                    tradeReason = "Mirror — primed to reflect";
                    break;
                case "Trap":
                    attacker.HasPendingTrap  = true;
                    attacker.PendingTrapCount = (setupLv3 || tricky) ? 2 : 1; // Lv3 / Trickster: punish 2 moves
                    tradeReason = "Trap armed";
                    break;
                case "Cage":
                    attacker.HasPendingCage = true;
                    defender.CageBeatsRemaining = setupLv3 ? 2 : 1;            // Lv3: locked out 2 beats
                    tradeReason = "Cage — opponent can't defend next beat";
                    break;
                case "Taunt":
                    // Force the opponent to throw a STRIKE next beat — or take damage.
                    defender.MustStrikeBeats = (setupLv3 || tricky) ? 2 : 1;
                    tradeReason = "Taunt — opponent MUST Strike next beat";
                    if (defender.connectionToClient != null) defender.TargetMustStrikeWarn(defender.connectionToClient);
                    break;
            }
        }

        // --- 4. APPLY DAMAGE ---
        if (hits)
        {
            float mitigMult = defenderStaggered ? 1f : (1f - blockMitigation);
            damageDealt = Mathf.RoundToInt(finalDmg * mitigMult);
            if (blockMitigation > 0f) tradeReason = $"{atk} chipped through Block";
            else tradeReason = $"{atk} connected";

            // Glass trait: +20% MORE damage taken (high risk for the +40% attack boost)
            if (defender.activeTraitId == "glass")
            {
                damageDealt = Mathf.RoundToInt(damageDealt * 1.20f);
            }

            // Fortress trait: reduce damage taken by 18%
            if (!string.IsNullOrEmpty(defender.activeTraitId) && defender.activeTraitId == "fortress")
            {
                damageDealt = Mathf.RoundToInt(damageDealt * 0.82f);
            }

            // Defensive trait: reduce consecutive hits taken damage by 8%, max 40%
            if (!string.IsNullOrEmpty(defender.activeTraitId) && defender.activeTraitId == "defensive")
            {
                float defensiveReduction = Mathf.Min(0.40f, defender.ConsecutiveHitsChain * 0.08f);
                damageDealt = Mathf.RoundToInt(damageDealt * (1f - defensiveReduction));
            }

            if (damageDealt > 0)
            {
                if (atk == "UnbreakablePunch" || atk == "Overclock") _heavyHitThisBeat = true;
                if (attacker.connectionToClient != null) attacker.TargetPlaySuccessSound("Attack");
                if (defender.connectionToClient != null) defender.TargetPlaySuccessSound("Hurt");
                attacker.GloveStrikeFlash(); // gloves flare white-hot on a landed hit
                attacker.SpawnSwordImpact(defender.transform.position + Vector3.up); // sword slash burst (no-op for glove fighters)

                // ── POST-HIT PERK EFFECTS ──────────────────────────────────
                if (perkOn)
                {
                    switch (atkCard.perk)
                    {
                        case CardPerk.Stagger:
                            // Hook Lv3: opponent can't act next beat
                            defender.StaggerNextBeat = true;
                            tradeReason += " — STUNNED";
                            break;

                        case CardPerk.GrappleBleed:
                            // Grapple Lv3: 3% HP bleed for 2 beats
                            defender.BleedTurnsRemaining = 2;
                            defender.BleedDamagePerBeat  = Mathf.Max(1, Mathf.RoundToInt(defender.CurrentPercentage * 0.03f));
                            tradeReason += " — BLEED";
                            break;

                        case CardPerk.SweepKnockback:
                            // Sweep Lv3: clear opponent's combo buffer
                            defender._comboBuffer?.Clear();
                            tradeReason += " — COMBO BROKEN";
                            break;

                        case CardPerk.FastRedraw:
                            // Jab Lv3: no family lockout — redraw Strike immediately
                            var jabCm = attacker.GetComponent<CardManager>();
                            if (jabCm != null) jabCm.RedrawFamily(CardFamily.Strike);
                            break;
                    }
                }

                // FakeCounter (Fake Lv3): already applied pre-hit — nothing extra needed
                // BlockCounter (Block Lv3): set charge on successful block — handled in block section below
                PlayHitParticle(defender.transform.position);
                PlayCardActivationEffect(attacker, atk);
                Vector3 kbDir = (defender.transform.position - attacker.transform.position).normalized;
                defender.TakeDamage(damageDealt, kbDir, isOpponentDamage: true);

                // Draining trait: heal 3% self on hit
                if (!string.IsNullOrEmpty(attacker.activeTraitId) && attacker.activeTraitId == "draining")
                {
                    int healAmount = Mathf.Max(1, Mathf.RoundToInt(damageDealt * 0.03f));
                    attacker.CurrentPercentage -= healAmount; // Reduce percentage (healing)
                }

                // Vampire trait: heal 5% on every successful hit
                if (attacker.activeTraitId == "vampire")
                {
                    int vampireHeal = Mathf.Max(1, Mathf.RoundToInt(damageDealt * 0.05f));
                    attacker.CurrentPercentage -= vampireHeal; // Reduce percentage (healing)
                }

                // Track consecutive hits for Chain/Momentum/Defensive
                attacker.ConsecutiveHitsChain++;
                defender.ConsecutiveHitsChain = 0; // Reset defender's chain
            }

            return 1;
        }
        else
        {
            // Miss/no hit: reset attacker's consecutive hits chain
            attacker.ConsecutiveHitsChain = 0;

            // Volatile trait: +5% self-damage on miss/bad timing
            if (!string.IsNullOrEmpty(attacker.activeTraitId) && attacker.activeTraitId == "volatile")
            {
                int volatileDmg = Mathf.Max(1, Mathf.RoundToInt(finalDmg * 0.05f));
                attacker.TakeDamage(volatileDmg);
            }
        }
        return 0;
    }

    // Base damage from CardDatabase — level-aware (Lv1/2/3 damage pulled directly from card definition).
    [Server]
    private int GetBaseDamage(string attack, PlayerCombat attacker = null)
    {
        var card = CardDatabase.GetCombatCard(TriggerToCardId(attack));
        if (card == null) return 5;
        int lvl = 1;
        if (attacker != null)
        {
            var inv = attacker.GetComponent<PlayerInventory>();
            if (inv != null) lvl = inv.GetLevel(TriggerToCardId(attack));
        }
        return card.DamageForLevel(lvl);
    }

    // Level for a trigger on a given player.
    private int GetCardLevel(PlayerCombat player, string trigger)
    {
        if (player == null) return 1;
        var inv = player.GetComponent<PlayerInventory>();
        return inv != null ? inv.GetLevel(TriggerToCardId(trigger)) : 1;
    }

    // Perk data for a trigger, or null.
    private CombatCardData GetCardData(string trigger) => CardDatabase.GetCombatCard(TriggerToCardId(trigger));

    // Effective timing window for a defender's parry/defense card.
    // Base value is widened by the card's per-level timing bonus so upgrades feel real.
    private float DefenseWindow(PlayerCombat defender, string trigger, float baseWindow)
    {
        var card = GetCardData(trigger);
        if (card == null) return baseWindow;
        int lvl = GetCardLevel(defender, trigger);
        float bonus = lvl >= 3 ? card.lv3TimingBonus : lvl == 2 ? card.lv2TimingBonus : 0f;
        return baseWindow + bonus;
    }

    // Card upgrade level multiplier — kept for the existing reflect damage calls that already use it.
    [Server]
    private float CardUpgradeMult(PlayerCombat player, string trigger)
    {
        if (player == null || string.IsNullOrEmpty(trigger)) return 1f;
        var inv = player.GetComponent<PlayerInventory>();
        if (inv == null) return 1f;
        int lvl = inv.GetLevel(TriggerToCardId(trigger));
        return 1f + 0.15f * (lvl - 1);
    }

    // Apply trait and vex card damage multipliers
    [Server]
    private int ApplyTraitMultiplier(int baseDmg, PlayerCombat player = null)
    {
        if (baseDmg <= 0) return baseDmg;
        if (player == null) return baseDmg;

        float multiplier = 1f;

        // Trait effects with state-based bonuses
        if (!string.IsNullOrEmpty(player.activeTraitId))
        {
            multiplier *= GetTraitDamageMultiplier(player.activeTraitId);

            // State-based trait bonuses
            string trait = player.activeTraitId;

            // Bloodlust: +6% per consecutive hit
            if (trait == "bloodlust")
                multiplier *= (1f + (player.ConsecutiveHitsChain * 0.06f));

            // Momentum: +10% per consecutive hit (max +40%)
            if (trait == "momentum")
                multiplier *= (1f + Mathf.Min(player.ConsecutiveHitsChain * 0.10f, 0.40f));

            // Executioner: +35% when opponent > 65% health
            if (trait == "executioner")
            {
                PlayerCombat opponent = player.GetComponent<PlayerController>()?.GetOpponent()?.GetComponent<PlayerCombat>();
                if (opponent != null && opponent.CurrentPercentage > 65f)
                    multiplier *= 1.35f;
            }
        }

        return Mathf.RoundToInt(baseDmg * multiplier);
    }

    private float GetTraitDamageMultiplier(string traitId)
    {
        return traitId switch
        {
            // Offense traits
            "fury"         => 1.15f, // +15% all attack damage
            "glass"        => 1.40f, // +40% attack damage (but takes +20% incoming — handled in ApplyDamage)
            "bloodlust"    => 1.0f,  // Handled via ConsecutiveHitsChain bonus per hit
            "executioner"  => 1.0f,  // Handled in ApplyDamage based on opponent health
            "momentum"     => 1.0f,  // Handled in ApplyDamage based on consecutive hits
            "piercing"     => 1.0f,  // Ignores block defense, not raw damage
            "grappler"     => 1.0f,  // Per-card bonus handled in ApplyDamage
            "trickster"    => 1.0f,  // Effect multipliers handled in ApplyDamage

            // Defense traits
            "fortress"     => 1.0f,  // Damage reduction on incoming, handled in ApplyDamage
            "anchored"     => 1.0f,  // Stagger reduction handled in stagger calculation
            "stalwart"     => 1.0f,  // Block bonus handled separately

            // Utility traits
            "quicktrigger" => 1.0f,  // Timing window handled in VoiceProcessor
            "regenerate"   => 1.0f,  // Per-beat healing handled in beat loop
            "echo"         => 1.0f,  // Card refresh handled in card consumption logic
            "vampire"      => 1.0f,  // Lifesteal handled in ApplyDamage on hit

            // Legacy
            "heavy"        => 1.10f,
            "volatile"     => 1.10f,
            "draining"     => 1.0f,
            "chain"        => 1.0f,
            "defensive"    => 1.0f,
            "stunning"     => 1.0f,
            _ => 1.0f
        };
    }

    // Family-based hit evaluation. Block mitigation and Parry reflection are resolved
    // earlier in ProcessDamage; by the time we get here we only decide if a clean hit lands.
    [Server]
    private bool EvaluateHit(string attack, string defense, bool dodgeSuccessful)
    {
        CardFamily af = GetFamily(attack);

        // THROW — grabs through any guard. (Dodge that "caught" a throw already set dodgeSuccessful=false.)
        if (af == CardFamily.Throw) return true;

        // STRIKE — lands unless evaded by a dodge. (Block is applied as mitigation, not a miss.)
        if (af == CardFamily.Strike) return !dodgeSuccessful;

        // Block / Parry / Support never deal damage through this path.
        return false;
    }

    // Map a move's trigger name to its combat family (the simplified triangle).
    // Strike beats Throw -> Throw beats Block/Parry -> Block/Parry beats Strike.
    // Support cards sit outside the triangle. Overclock deals direct unstoppable damage, so it reads as a Strike.
    private CardFamily GetFamily(string trigger)
    {
        switch (trigger)
        {
            case "Jab": case "Cross": case "Hook": case "UnbreakablePunch": case "Uppercut": case "Overclock":
                return CardFamily.Strike;
            case "Grapple": case "Fake": case "Sweep":
                return CardFamily.Throw;
            case "Block": case "Left": case "Right":
                return CardFamily.Block;
            case "ParryIntent": case "Clutch": case "Reverse": case "Mirror":
                return CardFamily.Parry;
            default:
                return CardFamily.Support; // Focus, Taunt, Trap, Cage, empty/unknown — outside the triangle
        }
    }

    private bool IsOffense(CardFamily f) => f == CardFamily.Strike || f == CardFamily.Throw;

    // Taunt enforcement: if a player was ordered to Strike this beat and didn't, punish them.
    [Server]
    private void CheckMustStrike(PlayerCombat p, PlayerCombat.RhythmAction move)
    {
        if (p == null || p.MustStrikeBeats <= 0) return;
        p.MustStrikeBeats--;
        if (GetFamily(move.attack) != CardFamily.Strike)
            p.TakeDamage(12); // disobeyed the Taunt
    }

    // Cards that cannot be interrupted (unstoppable attacks)
    private bool IsProtected(string attack)
    {
        if (string.IsNullOrEmpty(attack)) return false;
        return attack == "UnbreakablePunch" || attack == "Overclock" || attack == "Reverse";
    }

    // Family-based interruption (the simplified triangle).
    // Strike beats Throw. Offense (Strike/Throw) stuffs passive Support setups.
    // (Throw beats Block/Parry is handled inside ProcessDamage via the "throw bypasses defense" path.)
    private bool DoesInterrupt(string attackerMove, string defenderMove, out string reason)
    {
        reason = "";
        if (string.IsNullOrEmpty(attackerMove) || string.IsNullOrEmpty(defenderMove)) return false;

        CardFamily af = GetFamily(attackerMove);
        CardFamily df = GetFamily(defenderMove);

        // Strike beats Throw — you hit them before the grab connects.
        if (af == CardFamily.Strike && df == CardFamily.Throw)
            { reason = "Strike stuffed the Throw"; return true; }

        // Offense catches a passive Support setup (Focus, Taunt, Trap, Cage) mid-cast.
        if (IsOffense(af) && df == CardFamily.Support)
            { reason = $"Caught mid-{defenderMove}"; return true; }

        return false;
    }

    private bool IsDefenseMove(string move)
    {
        if (string.IsNullOrEmpty(move)) return false;
        return move == "Block" || move == "ParryIntent" || move == "Left" || move == "Right"
            || move == "Clutch" || move == "Focus" || move == "Taunt" || move == "Trap"
            || move == "Cage" || move == "Mirror" || move == "Reverse";
    }


    // [Server]
    // private int ProcessDamage(PlayerCombat attacker, PlayerCombat.RhythmAction move, PlayerCombat defender, PlayerCombat.RhythmAction defMove, bool isInterrupted, out int damageDealt)
    // {
    //     damageDealt = 0;
    //     if (string.IsNullOrEmpty(move.attack) || move.attack == "Block") return 0;

    //     // --- 1. PARRY REFLECTION ---
    //     if (defender.IsParryActive)
    //     {
    //         defender.TargetPlaySuccessSound("Parry"); // Calls the Rpc on PlayerCombat

    //         bool isUnbreakable = (move.attack == "UnbreakablePunch");
    //         int baseRef = isUnbreakable ? 15 : ((move.attack == "Hook") ? 25 : 10);
    //         attacker.TakeDamage(Mathf.CeilToInt(baseRef * 1.2f));

    //         damageDealt = 0;
    //         return -1;
    //     }

    //     // --- 2. MOVEMENT & MITIGATION ---
    //     bool moveSuccessful = false;
    //     float blockMitigation = 0f;

    //     if (defMove.dash != Vector3.zero)
    //     {
    //         float dSpike = defender.lastVocalSpikeTime;
    //         if (dSpike > 0 && (GetNextBeatTime() - dSpike) <= 0.3f)
    //         {
    //             moveSuccessful = true;
    //             if (defender.connectionToClient != null)
    //             {
    //                 defender.TargetPlaySuccessSound("Dash"); // Or "Block", "Parry", etc.
    //             }
    //         }
    //     }

    //     if (defMove.attack == "Block")
    //     {
    //         float bSpike = defender.lastVocalSpikeTime;
    //         float targetBeat = GetNextBeatTime();
    //         float offset = Mathf.Abs(targetBeat - bSpike);

    //         blockMitigation = 0.7f; // Always 70% min

    //         if (bSpike > 0 && offset <= 0.4f)
    //         {
    //             blockMitigation = 0.7f + Mathf.Lerp(0.3f, 0f, offset / 0.4f);
    //             defender.TargetPlaySuccessSound("Block"); // Successful timed block sound
    //         }
    //     }

    //     // --- 3. ATTACK DAMAGE ---
    //     float targetBeatTime = GetNextBeatTime();
    //     float atkSpike = attacker.lastVocalSpikeTime;
    //     bool moveIsUnbreakable = (move.attack == "UnbreakablePunch");
    //     int finalDmg = moveIsUnbreakable ? 15 : 5;
    //     float window = moveIsUnbreakable ? 0.2f : 0.3f;

    //     if (atkSpike > 0 && (targetBeatTime - atkSpike) <= window)
    //     {
    //         float bonus = Mathf.Lerp(10, 0, Mathf.Max(0, targetBeatTime - atkSpike) / window);
    //         finalDmg += Mathf.RoundToInt(bonus);
    //     }

    //     // --- 4. HIT DETECTION ---
    //     bool hits = false;
    //     if (isInterrupted && !moveIsUnbreakable) hits = false;
    //     else
    //     {
    //         if (moveIsUnbreakable) hits = !moveSuccessful;
    //         else if (move.attack == "Jab") hits = !moveSuccessful;
    //         else if (move.attack == "Cross") hits = true;
    //         else if (move.attack == "Hook") hits = !moveSuccessful;
    //     }

    //     if (hits)
    //     {
    //         float multiplier = 1f - blockMitigation;
    //         damageDealt = Mathf.RoundToInt(finalDmg * multiplier);

    //         if (damageDealt > 0)
    //         {
    //             // 1. Attacker gets the "Hit" sound
    //             if (attacker.connectionToClient != null) attacker.TargetPlaySuccessSound("Attack");

    //             // 2. Defender gets the "Hurt" sound
    //             if (defender.connectionToClient != null) defender.TargetPlaySuccessSound("Hurt");

    //             defender.TakeDamage(damageDealt);
    //             return 1;
    //         }
    //     }
    //     return 0;
    // }
    private string FormatMove(PlayerCombat.RhythmAction move)
    {
        if (!string.IsNullOrEmpty(move.attack))
        {
            return move.attack switch
            {
                "ParryIntent"      => "CAGE",
                "UnbreakablePunch" => "BOOM",
                "Jab"              => "PUNCH",
                "Cross"            => "FLANK",
                "Hook"             => "HOOK",
                "Block"            => "BLOCK",
                "Grapple"          => "GRAPPLE",
                "Fake"            => "FEINT",
                "Clutch"           => "CLUTCH",
                "Uppercut"         => "UPPERCUT",
                "Sweep"            => "SWEEP",
                "Focus"            => "FOCUS",
                "Taunt"            => "TAUNT",
                "Overclock"        => "OVERCLOCK",
                "Reverse"          => "REVERSE",
                "Trap"             => "TRAP",
                "Cage"             => "CAGE",
                "Mirror"           => "MIRROR",
                _                  => move.attack.ToUpper()
            };
        }
        return (move.dash != Vector3.zero) ? "DODGE" : "IDLE";
    }

    [ClientRpc]
    private void RpcLogCombatTrade(string p1Name, string p1Move, int p1State, int p1Dmg, string p2Name, string p2Move, int p2State, int p2Dmg, string reason)
    {
        CommentaryManager.Instance?.OnTradeResolved(p1Move, p1Dmg, p2Move, p2Dmg, customIsCombo);

        combatLogs.Add(new CombatLogEntry
        {
            p1Name = string.IsNullOrEmpty(p1Name) ? "Player 1" : p1Name,
            p1Move = p1Move,
            p1State = p1State,
            p1Damage = p1Dmg,
            p2Name = string.IsNullOrEmpty(p2Name) ? "Player 2" : p2Name,
            p2Move = p2Move,
            p2State = p2State,
            p2Damage = p2Dmg,
            reason = reason,
            timeAdded = Time.time
        });
        if (combatLogs.Count > 5) combatLogs.RemoveAt(0);
    }

    private void ExecutePulseImpact()
    {
        // 1. Resolve combat logic for players
        if (GameManager.players.Count >= 2) ResolveRhythmCombat();

        foreach (var player in GameManager.players)
        {
            if (player != null)
            {
                PlayerCombat pc = player.GetComponent<PlayerCombat>();

                // Slot idle recharge disabled (slot system removed)
                // if (isServer)
                // {
                //     CardManager cm = player.GetComponent<CardManager>();
                //     if (cm != null)
                //     {
                //         var move = pc.PeekNextMove();
                //         if (string.IsNullOrEmpty(move.attack) && move.dash == Vector3.zero)
                //         {
                //             cm.attackSlotsRemaining  = Mathf.Min(cm.attackSlotsTotal,  cm.attackSlotsRemaining  + 1);
                //             cm.defenseSlotsRemaining = Mathf.Min(cm.defenseSlotTotal,  cm.defenseSlotsRemaining + 1);
                //         }
                //     }
                // }

                pc.ConsumeNextMove();

                // 3. HARD RESET: Clear all flags so lag cannot roll into the next beat
                // In chain mode, preserve the spike so hits 2-4 still grade correctly
                if (!customIsCombo)
                {
                    pc.lastVocalSpikeTime   = -1f;
                    pc.lastVocalSpikeVolume = 0f;
                }
                pc.IsParryActive = false;

                // 3. Stagger management + per-beat cooldown roll
                if (isServer)
                {
                    CardManager cm = player.GetComponent<CardManager>();
                    if (pc != null && cm != null)
                    {
                        // Roll the same-card cooldown forward: justUsed → blocked, clear justUsed
                        if (IsSingleMoveMode()) cm.AdvanceCooldown();

                        if (pc.IsStaggered)
                        {
                            // Count down dedicated stagger beat counter; clear when it expires
                            pc.StaggerBeatsRemaining--;
                            if (pc.StaggerBeatsRemaining <= 0)
                            {
                                pc.ClearStagger();
                                cm.ResetSlots();
                            }
                        }
                        // Slot-exhaustion stagger disabled (slot system removed)
                        // else if (cm.attackSlotsRemaining == 0 && cm.defenseSlotsRemaining == 0)
                        // {
                        //     pc.TriggerStagger(3);
                        // }

                        // Decay persistent card effects each beat
                        if (pc.HasFocusBuff) pc.FocusBuffBeatsRemaining--;
                        if (pc.HasMirrorBuff) pc.MirrorBuffBeatsRemaining--;
                        // Trap and Cage persist for 1 beat (triggered next turn), so they naturally clear

                        // Decay Taunt effect (lasts 1-2 turns based on Trickster)
                        if (pc.TauntTurnsRemaining > 0)
                        {
                            pc.TauntTurnsRemaining--;
                            if (pc.TauntTurnsRemaining <= 0)
                                pc.IsTauntedNextTurn = false;
                        }
                        else
                        {
                            pc.IsTauntedNextTurn = false;
                        }

                        // Regenerate trait: heal 4% health every beat
                        if (!string.IsNullOrEmpty(pc.activeTraitId) && pc.activeTraitId == "regenerate")
                        {
                            int healAmount = Mathf.Max(1, Mathf.RoundToInt(pc.CurrentPercentage * 0.04f));
                            pc.CurrentPercentage -= healAmount;
                        }

                        // ── Per-card perk state ticks ──────────────────────
                        // GrappleBleed: tick damage each beat
                        if (pc.BleedTurnsRemaining > 0)
                        {
                            pc.TakeDamage(pc.BleedDamagePerBeat);
                            pc.BleedTurnsRemaining--;
                            if (pc.BleedTurnsRemaining <= 0) pc.BleedDamagePerBeat = 0;
                        }

                        // Stagger perk (Hook Lv3): apply stagger on the beat after the hit
                        if (pc.StaggerNextBeat)
                        {
                            pc.TriggerStagger(1); // 1-beat stagger
                            pc.StaggerNextBeat = false;
                        }

                        // CageBreak Lv3: decay extra cage beats
                        if (pc.CageBeatsRemaining > 0) pc.CageBeatsRemaining--;

                        // Track last-beat action for FakeCounter / CounterBonus
                        bool didAttack  = !string.IsNullOrEmpty(pc.PendingAttackTrigger) &&
                                          CardManager.IsAttackTrigger(pc.PendingAttackTrigger);
                        bool didDefend  = !string.IsNullOrEmpty(pc.PendingAttackTrigger) &&
                                          CardManager.IsDefenseTrigger(pc.PendingAttackTrigger);
                        pc.AttackedLastBeat  = didAttack;
                        pc.DefendedLastBeat  = didDefend;
                    }
                }
            }
        }
    }

    // Returns a damage multiplier based on how close the attacker's vocal spike was to the beat.
    // EXCELLENT (≤0.10 s): +25%   GOOD (≤window): base   BAD (no spike / late): −50%
    [Server]
    private float GetTimingMultiplier(PlayerCombat attacker)
    {
        float spike = attacker.lastVocalSpikeTime;
        if (spike <= 0f) return 0.50f;
        float offset = Mathf.Abs(GetNextBeatTime() - spike);
        if (offset <= 0.10f) return 1.25f;
        if (offset <= 0.30f) return 1.00f;
        return 0.50f;
    }

    // Grants +1 slot of the OPPOSITE type to the winner of a trade.
    // usedAttack=true → winner attacked → gains DEF slot
    // usedAttack=false → winner defended → gains ATK slot
    [Server]
    private void GrantCounterBonus(CardManager cm, PlayerCombat pc, bool _usedAttack)
    {
        if (cm == null || pc == null) return;
        pc.roundCounterCount++;
        // Slot bonus grants disabled (slot system removed)
        // if (usedAttack)
        // {
        //     if (cm.defenseSlotsRemaining >= cm.defenseSlotTotal) return;
        //     cm.defenseSlotsRemaining = Mathf.Min(cm.defenseSlotTotal, cm.defenseSlotsRemaining + 1);
        //     if (pc.connectionToClient != null) cm.TargetShowSlotBonus(pc.connectionToClient, false);
        // }
        // else
        // {
        //     if (cm.attackSlotsRemaining >= cm.attackSlotsTotal) return;
        //     cm.attackSlotsRemaining = Mathf.Min(cm.attackSlotsTotal, cm.attackSlotsRemaining + 1);
        //     if (pc.connectionToClient != null) cm.TargetShowSlotBonus(pc.connectionToClient, true);
        // }
    }

    [Server]
    private void PlayHitParticle(Vector3 worldPosition)
    {
        RpcPlayHitParticle(worldPosition);
    }

    [ClientRpc]
    private void RpcPlayHitParticle(Vector3 worldPosition)
    {
        if (ParticlePoolManager.Instance != null)
            ParticlePoolManager.Instance.PlayParticle("Hit", worldPosition);
    }

    [Server]
    private void PlayCardActivationEffect(PlayerCombat player, string cardName)
    {
        RpcPlayCardActivationEffect(player, cardName);
    }

    [ClientRpc]
    private void RpcPlayCardActivationEffect(PlayerCombat player, string cardName)
    {
        if (player == null) return;
        // Brief glow effect on the player
        StartCoroutine(CardActivationEffectRoutine(player.transform));
    }

    private System.Collections.IEnumerator CardActivationEffectRoutine(Transform target)
    {
        Renderer renderer = target.GetComponentInChildren<Renderer>();
        if (renderer == null) yield break;

        Material mat = renderer.material;
        Color originalColor = mat.color;
        float duration = 0.3f;
        float elapsed = 0f;

        // Brief white flash
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            mat.color = Color.Lerp(Color.white, originalColor, t);
            yield return null;
        }
        mat.color = originalColor;
    }

    [Server]
    private void AssignRandomComboMove(PlayerCombat pc)
    {
        // Full pool of all 20 combat cards for combo mode
        string[] pool =
        {
            "Jab", "Cross", "Hook", "Block", "Left", "Right",
            "UnbreakablePunch", "ParryIntent",
            "Grapple", "Fake", "Clutch",
            "Uppercut", "Sweep", "Focus", "Taunt",
            "Overclock", "Reverse", "Trap", "Cage", "Mirror"
        };
        while (pc._comboBuffer.Count < currentComboCount)
        {
            string move = pool[Random.Range(0, pool.Length)];
            pc._comboBuffer.Add(new PlayerCombat.RhythmAction { attack = move, dash = Vector3.zero });
        }
    }

    private int ComputeComboDamage(string move, float timingOffset)
    {
        int baseDmg = move switch
        {
            "Jab"              => 8,
            "Cross"            => 12,
            "Hook"             => 16,
            "UnbreakablePunch" => 20,
            "ParryIntent"      => 10,
            "Grapple"          => 14,
            "Fake"            => 5,
            "Clutch"           => 0,
            "Uppercut"         => 16,
            "Sweep"            => 13,
            "Focus"            => 0,
            "Taunt"            => 3,
            "Overclock"        => 25,
            "Reverse"          => 0,
            "Trap"             => 8,
            "Cage"             => 8,
            "Mirror"           => 10,
            _                  => 8
        };
        float mult = timingOffset <= 0.10f ? 1.25f : timingOffset <= 0.30f ? 1.0f : 0.5f;
        return Mathf.Max(1, Mathf.RoundToInt(baseDmg * mult));
    }

    [Server]
    private void EvaluateAndSendFeedback(PlayerCombat pc, PlayerCombat.RhythmAction move)
    {
        // Don't grade them if they didn't do anything
        if (string.IsNullOrEmpty(move.attack) && move.dash == Vector3.zero) return;

        // Bots don't need UI feedback, so we skip if there is no client connection
        if (pc.connectionToClient == null) return;

        float targetBeat = GetNextBeatTime();

        // --- CHAIN MODE: one shout covers the whole cluster ---
        // Hits 2-4 will have a growing offset from the beat, but the spike is still valid.
        // Give GOOD for any existing spike; BAD only if there is no spike at all.
        if (customIsCombo)
        {
            if (pc.lastVocalSpikeTime <= 0)
            {
                Debug.Log($"<color=orange>[TIMING]</color> {pc.name} - CHAIN | No Vocal Spike -> BAD");
                pc.TargetShowTimingFeedback("BAD");
                return;
            }
            float chainOffset = Mathf.Abs(targetBeat - pc.lastVocalSpikeTime);
            string chainRating = (chainOffset <= 0.1f) ? "EXCELLENT" : "GOOD";
            if (chainRating == "EXCELLENT") pc.roundExcellentCount++;
            Debug.Log($"<color=cyan>[TIMING EVAL]</color> {pc.name} | CHAIN Beat: {targetBeat:F3}s | Spike: {pc.lastVocalSpikeTime:F3}s | Offset: {chainOffset:F3}s => <color=yellow>{chainRating}</color>");
            pc.TargetShowTimingFeedback(chainRating);
            return;
        }

        // --- SINGLE MODE: original narrow-window logic ---
        if (pc.lastVocalSpikeTime <= 0)
        {
            Debug.Log($"<color=orange>[TIMING]</color> {pc.name} - Move: {(string.IsNullOrEmpty(move.attack) ? "DODGE" : move.attack)} | No Vocal Spike Detected -> BAD");
            pc.TargetShowTimingFeedback("BAD");
            return;
        }

        float offset = Mathf.Abs(targetBeat - pc.lastVocalSpikeTime);

        float window = move.attack switch
        {
            "Block"            => 0.4f,
            "Clutch"           => 0.15f,
            "Reverse"          => 0.2f,
            "Mirror"           => 0.2f,
            "Trap"             => 0.35f,
            "Cage"             => 0.35f,
            "UnbreakablePunch" => 0.2f,
            "Overclock"        => 0.2f,
            _                  => 0.3f
        };

        string rating = "BAD";
        if (offset <= 0.1f) rating = "EXCELLENT";
        else if (offset <= window) rating = "GOOD";

        if (rating == "EXCELLENT") pc.roundExcellentCount++;

        Debug.Log($"<color=cyan>[TIMING EVAL]</color> {pc.name} | Beat: {targetBeat:F3}s | Voice Spike: {pc.lastVocalSpikeTime:F3}s | Offset: {offset:F3}s | Window: {window}s => <color=yellow>{rating}</color>");
        pc.TargetShowTimingFeedback(rating);
    }

    [ClientRpc] private void RpcTriggerHitStop(bool heavy)
    {
        float dur   = heavy ? 0.12f : 0.06f;
        float scale = heavy ? 0.02f : 0.05f;
        StartCoroutine(HitStopRoutine(dur, scale));
        if (CameraShake.Instance != null) CameraShake.Instance.Shake(heavy ? 0.18f : 0.10f, heavy ? 0.12f : 0.07f);
    }
    private IEnumerator HitStopRoutine(float dur, float scale) { Time.timeScale = scale; yield return new WaitForSecondsRealtime(dur); Time.timeScale = 1.0f; }

    void OnRoundStateChanged(bool oldVal, bool newVal) { if (BeatAnalyzer.Instance != null && BeatAnalyzer.Instance.audioSource != null && currentType != RoundType.CustomTrack) { if (newVal) BeatAnalyzer.Instance.audioSource.Play(); else BeatAnalyzer.Instance.audioSource.Stop(); } }

    private void DrawRoundPicker()
    {
        if (_whiteTex == null) { _whiteTex = new Texture2D(1, 1); _whiteTex.SetPixel(0, 0, Color.white); _whiteTex.Apply(); }

        // (Auto-select countdown is ticked in Update() so it works even when this
        // OnGUI picker is hidden — e.g. in VR.)

        // Scale for mobile so cards are finger-sized (no-op on desktop)
        var guiPrev = MobileGUI.Begin(out float vw, out float vh);

        // Dark overlay
        GUI.color = new Color(0.02f, 0.02f, 0.04f, 0.98f);
        GUI.DrawTexture(new Rect(0, 0, vw, vh), _whiteTex);
        GUI.color = Color.white;

        // Title
        GUIStyle titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 38, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        titleStyle.normal.textColor = new Color(1f, 0.85f, 0.2f);
        GUI.Label(new Rect(0, 20, vw, 50), "PICK THE ROUND", titleStyle);

        // Countdown label
        if (isServer && _roundPickerTimer > 0f)
        {
            GUIStyle timerStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            timerStyle.normal.textColor = _roundPickerTimer <= 3f ? new Color(1f, 0.3f, 0.3f) : new Color(0.6f, 0.6f, 0.6f);
            GUI.Label(new Rect(0, 62, vw, 30), $"Auto-selects in {Mathf.CeilToInt(_roundPickerTimer)}s", timerStyle);
        }

        float cardW = 280f, cardH = 200f;
        float gap = 32f;
        float startY = 110f;

        GUIStyle cardTitleStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        GUIStyle descStyle  = new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.MiddleCenter, wordWrap = true };

        // Collect custom maps
        var allMapNames = new List<string>();
        string registry = PlayerPrefs.GetString("CustomMapRegistry", "");
        if (!string.IsNullOrEmpty(registry))
            foreach (string n in registry.Split('|'))
                if (!string.IsNullOrEmpty(n) && PlayerPrefs.HasKey("CustomMap_" + n) && !allMapNames.Contains(n))
                    allMapNames.Add(n);
        if (availableTracks != null)
            foreach (AudioClip t in availableTracks)
                if (t != null && PlayerPrefs.HasKey("CustomMap_" + t.name) && !allMapNames.Contains(t.name))
                    allMapNames.Add(t.name);

        int totalCards = 2 + allMapNames.Count;
        float totalW   = totalCards * cardW + (totalCards - 1) * gap;
        float startX   = vw / 2f - totalW / 2f;

        // SLOW ROUND CARD (dimmed if already used)
        bool slowUsed = _usedRoundTypes.Contains(RoundType.SlowRhythm);
        DrawModeCard(startX, startY, cardW, cardH, "SLOW RHYTHM", "Single beat\nrhythm combat", slowUsed ? Color.gray : Color.cyan,
            () => { if (!slowUsed && isServer) SelectRoundType(RoundType.SlowRhythm); }, cardTitleStyle, descStyle);

        // FAST ROUND CARD (dimmed if already used)
        bool fastUsed = _usedRoundTypes.Contains(RoundType.FastCombo);
        DrawModeCard(startX + cardW + gap, startY, cardW, cardH, "FAST COMBO", "Cluster attack\nsequences", fastUsed ? Color.gray : Color.magenta,
            () => { if (!fastUsed && isServer) SelectRoundType(RoundType.FastCombo); }, cardTitleStyle, descStyle);

        // CUSTOM MAPS
        for (int idx = 0; idx < allMapNames.Count; idx++)
        {
            string mapName = allMapNames[idx];
            float customX  = startX + (idx + 2) * (cardW + gap);

            AudioClip clip = null;
            if (availableTracks != null)
                foreach (AudioClip t in availableTracks)
                    if (t != null && t.name == mapName) { clip = t; break; }
            if (clip == null && _runtimeClips.ContainsKey(mapName))
                clip = _runtimeClips[mapName];

            bool hasPath = PlayerPrefs.HasKey("CustomMapPath_" + mapName);
            bool loading = _isLoadingClip && _loadingClipName == mapName;

            bool mapUsed = _usedCustomMaps.Contains(mapName);
            Color mapColor = mapUsed ? Color.gray : Color.green;

            if (loading)
                DrawModeCard(customX, startY, cardW, cardH, "LOADING...", mapName, Color.yellow, () => { }, cardTitleStyle, descStyle);
            else if (clip != null || hasPath)
                DrawModeCard(customX, startY, cardW, cardH, mapName.ToUpper(), "Custom map", mapColor,
                    () => { if (!mapUsed && isServer) SelectRoundType(RoundType.CustomTrack, mapName); }, cardTitleStyle, descStyle);
        }

        MobileGUI.End(guiPrev);
    }

    private void DrawModeCard(float x, float y, float w, float h, string title, string desc, Color color, System.Action onClick, GUIStyle titleStyle, GUIStyle descStyle)
    {
        Rect cardRect = new Rect(x, y, w, h);
        const float borderThick = 6f;
        const float cornerSize = 12f;
        const float padding = 14f;

        // Background with subtle gradient appearance
        GUI.color = new Color(0.08f, 0.08f, 0.12f, 0.98f);
        GUI.DrawTexture(cardRect, _whiteTex);

        // Thick colored border
        GUI.color = color;
        GUI.DrawTexture(new Rect(x, y, w, borderThick), _whiteTex); // top
        GUI.DrawTexture(new Rect(x, y + h - borderThick, w, borderThick), _whiteTex); // bottom
        GUI.DrawTexture(new Rect(x, y, borderThick, h), _whiteTex); // left
        GUI.DrawTexture(new Rect(x + w - borderThick, y, borderThick, h), _whiteTex); // right

        // Corner brackets for visual flair
        Color cornerColor = new Color(color.r, color.g, color.b, 0.7f);
        GUI.color = cornerColor;
        // Top-left corner
        GUI.DrawTexture(new Rect(x + 4f, y + 4f, cornerSize, 2f), _whiteTex);
        GUI.DrawTexture(new Rect(x + 4f, y + 4f, 2f, cornerSize), _whiteTex);
        // Top-right corner
        GUI.DrawTexture(new Rect(x + w - cornerSize - 4f, y + 4f, cornerSize, 2f), _whiteTex);
        GUI.DrawTexture(new Rect(x + w - 6f, y + 4f, 2f, cornerSize), _whiteTex);
        // Bottom-left corner
        GUI.DrawTexture(new Rect(x + 4f, y + h - 6f, cornerSize, 2f), _whiteTex);
        GUI.DrawTexture(new Rect(x + 4f, y + h - cornerSize - 4f, 2f, cornerSize), _whiteTex);
        // Bottom-right corner
        GUI.DrawTexture(new Rect(x + w - cornerSize - 4f, y + h - 6f, cornerSize, 2f), _whiteTex);
        GUI.DrawTexture(new Rect(x + w - 6f, y + h - cornerSize - 4f, 2f, cornerSize), _whiteTex);

        // Title with glow effect (duplicate offset slightly for glow) - plenty of space to avoid clipping
        float titleY = y + padding;
        float titleHeight = h * 0.35f;
        GUI.color = new Color(color.r * 0.5f, color.g * 0.5f, color.b * 0.5f, 0.4f);
        GUI.Label(new Rect(x + 2f, titleY + 1f, w - 4f, titleHeight), title, titleStyle);
        GUI.color = color;
        GUI.Label(new Rect(x + 1f, titleY, w - 2f, titleHeight), title, titleStyle);

        // Description - give it plenty of vertical space
        float descY = y + (h * 0.40f);
        float descHeight = h - descY + y - padding;
        descStyle.normal.textColor = new Color(0.85f, 0.85f, 0.9f, 0.95f);
        GUI.Label(new Rect(x + padding, descY, w - (padding * 2f), descHeight), desc, descStyle);

        // Click detection
        if (GUI.Button(cardRect, "", GUI.skin.box)) onClick?.Invoke();

        GUI.color = Color.white;
    }

    private void LoadAndPlayMap(string mapName, AudioClip clip)
    {
        if (BeatAnalyzer.Instance != null && BeatAnalyzer.Instance.audioSource != null)
        {
            if (clip != null)
            {
                BeatAnalyzer.Instance.audioSource.clip = clip;
                StartCustomRound();
            }
            else
            {
                StartCoroutine(LoadClipThenStartRound(mapName, PlayerPrefs.GetString("CustomMapPath_" + mapName)));
            }
        }
    }

   private void OnGUI()
    {
        // VR: screen-space IMGUI splits across eyes under multi-pass. Hidden until
        // the world-space VR HUD is built (Step 3).
        if (VRCameraDriver.VRActive) return;

        if (TiebreakerManager.Instance != null && TiebreakerManager.Instance.IsTiebreakerActive) return;

        // --- SHOP PHASE OVERLAY ---
        if (isShopPhase)
        {
            DrawShop();
            return;
        }

        // --- ROUND PICKER OVERLAY ---
        if (isRoundPickerActive)
        {
            DrawRoundPicker();
            return;
        }

        // --- 1. SERVER CONTROLS (Top Left) ---
        if (NetworkServer.active && isServer)
        {
            // Increased the height to 500 to fit multiple track buttons
            GUILayout.BeginArea(new Rect(10, 10, 220, 500));
            if (!isRoundActive)
            {
                GUI.color = Color.white;
                GUILayout.EndArea();
                // Round picker is now shown as full-screen overlay instead
                GUILayout.BeginArea(new Rect(10, 10, 220, 500));
            }
            else
            {
                if (GUILayout.Button("STOP ROUND", GUILayout.Height(40))) { StopRound(); StartCoroutine(EndRoundRoutine()); }
                if (GUILayout.Button("SKIP TO SHOP", GUILayout.Height(40))) { StopRound(); StartCoroutine(EndRoundRoutine()); }
                GUILayout.Label($"ACTIVE: {currentType}", GUI.skin.box);
            }
            GUILayout.EndArea();
        }

        // --- 2. MATCH STATUS (Right side, just above MIC LEVEL box) ---
        // MIC LEVEL sits at x=Screen.width-420, y=Screen.height-120, w=400, h=100.
        // Stack the match panel directly above it with the same right alignment.
        {
            float panelW = 200f;
            float panelX = Screen.width - panelW - 20f; // flush with right edge
            float micTop = Screen.height - 120f;         // top of the MIC LEVEL box
            float rowH   = 26f;
            int   rows   = 4 + GameManager.players.Count; // round+score+p1cr+p2cr + one per player
            float panelH = rows * rowH + 8f;
            float panelY = micTop - panelH - 8f;          // 8px gap above MIC box

            GUILayout.BeginArea(new Rect(panelX, panelY, panelW, panelH));
            GUIStyle matchStyle = new GUIStyle(GUI.skin.box) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            matchStyle.normal.textColor = Color.cyan;
            GUILayout.Label($"ROUND {currentRoundNumber} / 9", matchStyle, GUILayout.Height(rowH));
            matchStyle.normal.textColor = Color.yellow;
            GUILayout.Label($"SCORE: {p1RoundWins} - {p2RoundWins}", matchStyle, GUILayout.Height(rowH));
            matchStyle.normal.textColor = Color.green;
            GUILayout.Label($"P1 Credits: {p1TotalCredits}", matchStyle, GUILayout.Height(rowH));
            GUILayout.Label($"P2 Credits: {p2TotalCredits}", matchStyle, GUILayout.Height(rowH));

            foreach (var p in GameManager.players)
            {
                if (p != null)
                {
                    PlayerCombat pc = p.GetComponent<PlayerCombat>();
                    matchStyle.normal.textColor = pc.CurrentPercentage >= 75f ? Color.red : new Color(0f, 1f, 0.5f);
                    GUILayout.Label($"{p.PlayerName}: {pc.CurrentPercentage:F0}%", matchStyle, GUILayout.Height(rowH));
                }
            }
            GUILayout.EndArea();
        }

        // --- 3. THE MASTERPIECE TIMER (Top Center) ---
        if (isRoundActive && _startTime != 0)
        {
            float elapsed = (float)(NetworkTime.time - _startTime);
            GUIStyle style = new GUIStyle(GUI.skin.label) { fontSize = 24, alignment = TextAnchor.MiddleCenter };

            if (elapsed < 0)
            {
                style.normal.textColor = Color.yellow;
                GUI.Label(new Rect(Screen.width / 2 - 125, 50, 250, 70), "READY?", style);
            }
            else if (currentType == RoundType.CustomTrack || currentType == RoundType.FastCombo)
            {
                if (_upcomingImpacts.Count > 0)
                {
                    float trackTime = GetCurrentTrackTime();
                    float timeToNextImpact = _upcomingImpacts[0] - trackTime;
                    bool isAttacking = timeToNextImpact < (currentComboCount * 0.8f);

                    if (isAttacking)
                    {
                        style.normal.textColor = Color.red;
                        GUI.Label(new Rect(Screen.width / 2 - 150, 50, 300, 120), $"DANGER\nATTACK CHAIN ACTIVE\n{currentComboCount}x HITS", style);
                    }
                    else
                    {
                        style.normal.textColor = Color.cyan;
                        string modeText = (currentComboCount > 1) ? $"CHAIN ({currentComboCount}x)" : "SINGLE";
                        GUI.Label(new Rect(Screen.width / 2 - 150, 50, 300, 120), $"{modeText}\nTHINK TIME\n{timeToNextImpact:F1}s", style);
                    }
                }
                else { GUI.Label(new Rect(Screen.width / 2 - 125, 50, 250, 70), "FINISHING...", style); }
            }
            else // Standard Slow Rhythm
            {
                float interval = 4.0f;
                float timer = elapsed % interval;
                style.normal.textColor = (timer > (interval - 1.5f)) ? Color.red : Color.white;
                GUI.Label(new Rect(Screen.width / 2 - 125, 50, 250, 100), $"WINDOW\n{timer:F1}s / {interval}s", style);
            }
        }

        // --- 4. COMBAT LOG (Right Side) — vertical feed, newest on top ---
        if (combatLogs.Count > 0)
        {
            if (_whiteTex == null) { _whiteTex = new Texture2D(1, 1); _whiteTex.SetPixel(0, 0, Color.white); _whiteTex.Apply(); }

            float panelW = 360f;
            float panelX = Screen.width - panelW - 16f;
            float panelY = 170f;
            int show = Mathf.Min(combatLogs.Count, 4);
            float entryH = 62f;
            float panelH = 34f + show * entryH;

            // Background + top accent
            GUI.color = new Color(0.03f, 0.04f, 0.10f, 0.85f);
            GUI.DrawTexture(new Rect(panelX, panelY, panelW, panelH), _whiteTex);
            GUI.color = new Color(0f, 0.9f, 0.9f, 0.75f);
            GUI.DrawTexture(new Rect(panelX, panelY, panelW, 2f), _whiteTex);
            GUI.color = Color.white;

            GUIStyle hdrStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            hdrStyle.normal.textColor = new Color(0f, 1f, 0.9f);
            GUI.Label(new Rect(panelX, panelY + 5f, panelW, 22f), "⚔  COMBAT LOG", hdrStyle);

            GUIStyle lineStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            GUIStyle reasonStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Italic, alignment = TextAnchor.MiddleLeft, wordWrap = false };
            reasonStyle.normal.textColor = new Color(1f, 0.85f, 0.3f, 0.9f);

            float ey = panelY + 30f;
            for (int k = 0; k < show; k++)
            {
                var log = combatLogs[combatLogs.Count - 1 - k]; // newest first
                string p1d = log.p1Damage > 0 ? $"  −{log.p1Damage}" : "";
                string p2d = log.p2Damage > 0 ? $"  −{log.p2Damage}" : "";

                lineStyle.normal.textColor = GetStateColor(log.p1State);
                GUI.Label(new Rect(panelX + 12f, ey, panelW - 24f, 18f), $"{log.p1Name}: {log.p1Move}{p1d}", lineStyle);
                lineStyle.normal.textColor = GetStateColor(log.p2State);
                GUI.Label(new Rect(panelX + 12f, ey + 18f, panelW - 24f, 18f), $"{log.p2Name}: {log.p2Move}{p2d}", lineStyle);

                if (!string.IsNullOrEmpty(log.reason))
                    GUI.Label(new Rect(panelX + 12f, ey + 36f, panelW - 24f, 16f), log.reason, reasonStyle);

                GUI.color = new Color(1f, 1f, 1f, 0.08f);
                GUI.DrawTexture(new Rect(panelX + 8f, ey + entryH - 5f, panelW - 16f, 1f), _whiteTex);
                GUI.color = Color.white;
                ey += entryH;
            }
        }
        // --- MATCH OVER OVERLAY ---
        if (isMatchOver)
        {
            if (_whiteTex == null) { _whiteTex = new Texture2D(1, 1); _whiteTex.SetPixel(0, 0, Color.white); _whiteTex.Apply(); }
            GUIStyle overlayStyle = new GUIStyle(GUI.skin.label) { fontSize = 42, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            overlayStyle.normal.textColor = Color.magenta;
            string matchResult = matchWinner == 1 ? "PLAYER 1 WINS MATCH!" : matchWinner == 2 ? "PLAYER 2 WINS MATCH!" : "MATCH DRAW!";
            GUI.color = new Color(0f, 0f, 0f, 0.75f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _whiteTex);
            GUI.color = Color.white;
            GUI.Label(new Rect(Screen.width / 2 - 300, Screen.height / 2 - 120, 600, 80), matchResult, overlayStyle);
            overlayStyle.fontSize = 24;
            overlayStyle.normal.textColor = Color.cyan;
            GUI.Label(new Rect(Screen.width / 2 - 300, Screen.height / 2 - 20, 600, 40), $"Final Score: {p1RoundWins} - {p2RoundWins}", overlayStyle);
            GUI.Label(new Rect(Screen.width / 2 - 300, Screen.height / 2 + 30, 600, 40), $"P1 Credits: {p1TotalCredits}  |  P2 Credits: {p2TotalCredits}", overlayStyle);
        }

        // --- CYBERPUNK SCANLINES OVERLAY ---
        if (isRoundActive && !isShopPhase && !isRoundPickerActive)
        {
            CyberpunkGUIUtils.DrawScanlines(speed: 3f, alpha: 0.12f);
        }

        DrawBackButton();
    }

    private void DrawBackButton()
    {
        // Cursor hint — always visible in-game
        GUIStyle hintStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            fontStyle = FontStyle.Italic,
            alignment = TextAnchor.MiddleLeft
        };
        hintStyle.normal.textColor = new Color(0.75f, 0.75f, 0.75f, 1f);
        GUI.Label(new Rect(20f, Screen.height - 80f, 260f, 22f), "Hold [Tab] to free cursor", hintStyle);

        if (isRoundActive || isShopPhase) return;

        if (GUI.Button(new Rect(20f, Screen.height - 55f, 160f, 40f), "← BACK TO MENU"))
        {
            if (NetworkServer.active)
                NetworkManager.singleton.StopHost();
            else
                NetworkManager.singleton.StopClient();
        }
    }

    // ── SHOP UI ────────────────────────────────────────────────────────────

    private void DrawShop()
    {
        if (_whiteTex == null) { _whiteTex = new Texture2D(1, 1); _whiteTex.SetPixel(0, 0, Color.white); _whiteTex.Apply(); }

        // Dark overlay
        GUI.color = new Color(0.04f, 0.04f, 0.08f, 0.96f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _whiteTex);
        GUI.color = Color.white;

        // Title
        GUIStyle titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 36, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        titleStyle.normal.textColor = new Color(1f, 0f, 0.5f);
        GUI.Label(new Rect(Screen.width / 2 - 350, 30, 700, 55), "PRE-ROUND SHOP", titleStyle);

        // Timer
        GUIStyle timerStyle = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        timerStyle.normal.textColor = shopTimeRemaining <= 10f ? Color.red : Color.cyan;
        GUI.Label(new Rect(Screen.width / 2 - 200, 90, 400, 45), $"TIME REMAINING: {Mathf.Max(0f, shopTimeRemaining):F1}s", timerStyle);

        // Instructions
        GUIStyle instrStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleCenter };
        instrStyle.normal.textColor = new Color(0.8f, 0.8f, 0.85f);
        GUI.Label(new Rect(Screen.width / 2 - 350, 135, 700, 28), "Pick 4 cards. Left + Right Dodge count as 1 pick.", instrStyle);

        // Cards grid
        var localPc = GameManager.localPlayer?.GetComponent<PlayerCombat>();
        bool isLocked = localPc != null && localPc.localShopLocked;

        float cardW = 220f, cardH = 280f;
        float gapX = 24f, gapY = 24f;
        int cols = 3, rows = 2;
        float totalW = cols * cardW + (cols - 1) * gapX;
        float totalH = rows * cardH + (rows - 1) * gapY;
        float startX = Screen.width / 2f - totalW / 2f;
        float startY = Screen.height / 2f - totalH / 2f + 10f;

        string[] shopCards = { "Jab", "Cross", "Hook", "Block", "Left", "Right" };
        string[] displayNames = { "PUNCH", "FLANK", "HOOK", "BLOCK", "LEFT", "RIGHT" };
        Color[] cardColors = {
            new Color(1f, 0.3f, 0.3f), new Color(1f, 0.3f, 0.3f), new Color(1f, 0.3f, 0.3f),
            new Color(0.25f, 0.7f, 1f), new Color(0.25f, 0.7f, 1f), new Color(0.25f, 0.7f, 1f)
        };

        for (int i = 0; i < shopCards.Length; i++)
        {
            int col = i % cols;
            int row = i / cols;
            float x = startX + col * (cardW + gapX);
            float y = startY + row * (cardH + gapY);
            bool isSelected = localPc != null && localPc.localShopSelection.Contains(shopCards[i]);
            bool isBundlePair = (shopCards[i] == "Left" && localPc != null && localPc.localShopSelection.Contains("Right")) ||
                                (shopCards[i] == "Right" && localPc != null && localPc.localShopSelection.Contains("Left"));
            DrawShopCard(new Rect(x, y, cardW, cardH), displayNames[i], shopCards[i], cardColors[i], isSelected, isBundlePair, isLocked);
        }

        // Pick count
        int pickCount = localPc != null ? GetPickCount(localPc.localShopSelection) : 0;
        GUIStyle countStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        countStyle.normal.textColor = pickCount >= 4 ? new Color(0.2f, 1f, 0.4f) : new Color(1f, 0.85f, 0.2f);
        GUI.Label(new Rect(Screen.width / 2 - 250, startY + totalH + 24, 500, 40), $"PICKS USED: {pickCount} / 4", countStyle);

        // Lock In button
        if (!isLocked)
        {
            GUIStyle btnStyle = new GUIStyle(GUI.skin.button) { fontSize = 24, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            GUI.color = pickCount >= 4 ? new Color(0.15f, 0.9f, 0.35f) : new Color(0.35f, 0.35f, 0.4f);
            if (GUI.Button(new Rect(Screen.width / 2 - 140, startY + totalH + 75, 280, 55), "LOCK IN", btnStyle) && pickCount >= 4)
            {
                localPc.CmdLockInShop();
                localPc.localShopLocked = true;
            }
            GUI.color = Color.white;
        }
        else
        {
            GUIStyle lockedStyle = new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            lockedStyle.normal.textColor = new Color(0.2f, 1f, 0.4f);
            GUI.Label(new Rect(Screen.width / 2 - 250, startY + totalH + 75, 500, 55), "LOCKED IN — WAITING...", lockedStyle);
        }
    }

    private void DrawShopCard(Rect r, string displayName, string cardName, Color baseColor, bool isSelected, bool isBundlePair, bool isLocked)
    {
        // Background
        Color bg = isSelected ? Color.Lerp(baseColor, Color.white, 0.25f) : new Color(0.06f, 0.06f, 0.1f, 0.92f);
        bg.a = isSelected ? 0.95f : 0.88f;
        GUI.color = bg;
        GUI.DrawTexture(r, _whiteTex);

        // Border
        Color borderCol = isSelected ? baseColor : new Color(0.3f, 0.3f, 0.4f, 0.7f);
        if (isBundlePair) borderCol = new Color(0.85f, 0.35f, 1f, 0.95f);
        GUI.color = borderCol;
        float bt = 4f;
        GUI.DrawTexture(new Rect(r.x, r.y, r.width, bt), _whiteTex);
        GUI.DrawTexture(new Rect(r.x, r.y + r.height - bt, r.width, bt), _whiteTex);
        GUI.DrawTexture(new Rect(r.x, r.y, bt, r.height), _whiteTex);
        GUI.DrawTexture(new Rect(r.x + r.width - bt, r.y, bt, r.height), _whiteTex);

        // Card name
        GUI.color = Color.white;
        GUIStyle nameStyle = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        GUI.Label(new Rect(r.x, r.y + 30, r.width, 45), displayName, nameStyle);

        // Selection indicator
        if (isSelected)
        {
            GUIStyle selStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            selStyle.normal.textColor = new Color(0.2f, 1f, 0.5f);
            GUI.Label(new Rect(r.x, r.y + r.height - 60, r.width, 30), "SELECTED", selStyle);
        }

        // Bundle indicator
        if (isBundlePair)
        {
            GUIStyle bundleStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Italic, alignment = TextAnchor.MiddleCenter };
            bundleStyle.normal.textColor = new Color(0.85f, 0.45f, 1f);
            GUI.Label(new Rect(r.x, r.y + r.height - 85, r.width, 25), "DODGE BUNDLE", bundleStyle);
        }

        // Click handling
        if (!isLocked && GUI.Button(r, "", GUI.skin.box))
        {
            var localPc = GameManager.localPlayer?.GetComponent<PlayerCombat>();
            if (localPc != null)
            {
                localPc.CmdToggleShopCard(cardName);
                // Local prediction
                if (localPc.localShopSelection.Contains(cardName))
                {
                    localPc.localShopSelection.Remove(cardName);
                    if (cardName == "Left") localPc.localShopSelection.Remove("Right");
                    if (cardName == "Right") localPc.localShopSelection.Remove("Left");
                }
                else
                {
                    int picks = GetPickCount(localPc.localShopSelection);
                    if (picks < 4)
                    {
                        localPc.localShopSelection.Add(cardName);
                        if (cardName == "Left" && !localPc.localShopSelection.Contains("Right")) localPc.localShopSelection.Add("Right");
                        if (cardName == "Right" && !localPc.localShopSelection.Contains("Left")) localPc.localShopSelection.Add("Left");
                    }
                }
            }
        }

        GUI.color = Color.white;
    }

    private int GetPickCount(List<string> selection)
    {
        int count = selection.Count;
        if (selection.Contains("Left") && selection.Contains("Right")) count--;
        return count;
    }

    private Color GetStateColor(int state) { if (state == 1) return Color.green; if (state == -1) return Color.red; return Color.white; }
    // Which bot prefab should be fighting this round.
    private GameObject DesiredBotPrefab()
    {
        if (botPrefabSecondary != null && currentRoundNumber > botSwapAfterRound)
            return botPrefabSecondary;
        return botPrefab;
    }

    // Count humans currently in the match (non-bot players).
    private int CountHumans()
    {
        int n = 0;
        foreach (var p in GameManager.players)
            if (p != null && p.GetComponent<BotController>() == null) n++;
        return n;
    }

    [Server]
    private void EnsureBotExists()
    {
        // Solo only: exactly one human and no second human.
        if (CountHumans() != 1) return;

        GameObject desired = DesiredBotPrefab();
        if (desired == null) return;

        // If the wrong bot is in for this round, retire it so the correct one spawns.
        if (_activeBot != null && _activeBotPrefab != desired)
        {
            var oldCtrl = _activeBot.GetComponent<PlayerController>();
            if (oldCtrl != null) GameManager.players.Remove(oldCtrl); // remove now to avoid a transient 3-player list
            NetworkServer.Destroy(_activeBot);
            _activeBot = null;
            _activeBotPrefab = null;
        }

        if (_activeBot != null) return; // correct bot already present

        Vector3 spawnPos = new Vector3(0, botSpawnY, 5);
        _activeBot = Instantiate(desired, spawnPos, Quaternion.identity);

        if (_activeBot.GetComponent<PlayerInventory>() == null)
            _activeBot.AddComponent<PlayerInventory>();

        NetworkServer.Spawn(_activeBot);
        var botPc = _activeBot.GetComponent<PlayerController>();
        botPc.SetReady(true);
        // Force at least 6m so the bot never crowds the player (overrides any stale Inspector value).
        botPc.DesiredDistance = Mathf.Max(botStandDistance, 6f);
        _activeBotPrefab = desired;

        // Give a freshly-spawned bot the starter deck so it can fight (e.g. after a mid-match swap).
        var inv = _activeBot.GetComponent<PlayerInventory>();
        if (inv != null && inv.ownedCombatCards.Count == 0)
        {
            foreach (var id in StarterDeck)
                if (!inv.ownedCombatCards.Contains(id)) inv.ownedCombatCards.Add(id);
            inv.EquipCombatCards(new List<string>(inv.ownedCombatCards));
        }
    }

    // Add these to RhythmRoundManager.cs
    public float GetNextBeatTime()
    {
        return (_upcomingImpacts.Count > 0) ? _upcomingImpacts[0] : 0f;
    }

    public float GetCurrentTrackTime()
    {
        return (currentType == RoundType.CustomTrack) ? BeatAnalyzer.Instance.audioSource.time : (float)(NetworkTime.time - _startTime);
    }

    public int GetNextClusterSize() => _clusterSizes.Count > 0 ? _clusterSizes[0] : 1;

    // Called by TiebreakerManager when the segment ends.
    // Consumes the tied beat, shifts the timeline so beats stay in sync with the
    // music (which was paused during the tiebreaker), then opens a fresh input window.
    [Server]
    public void ResumeAfterTiebreaker(float realSecondsElapsed)
    {
        // Consume the beat that triggered the tiebreaker
        if (_upcomingImpacts.Count > 0) _upcomingImpacts.RemoveAt(0);
        if (_clusterSizes.Count  > 0)  _clusterSizes.RemoveAt(0);

        // Shift _startTime forward by the real time spent in the tiebreaker so
        // NetworkTime.time - _startTime stays aligned with the audio source position.
        _startTime += realSecondsElapsed;

        lastBeatFireTime       = 0f;
        _isWindUpFired         = false;
        currentChainPosition   = 0;
        _clusterBeatsLeftToFire = _clusterSizes.Count > 0 ? _clusterSizes[0] : 1;

        // Reset all player states cleanly — same as the non-damage path in ExecutePulseImpact
        foreach (var player in GameManager.players)
        {
            if (player == null) continue;
            PlayerCombat pc = player.GetComponent<PlayerCombat>();
            pc.ConsumeNextMove();
            pc.lastVocalSpikeTime   = -1f;
            pc.lastVocalSpikeVolume = 0f;
            pc.IsParryActive        = false;
        }

        _tiebreakerPaused = false;
    }

    [TargetRpc]
    public void TargetPlaySuccessSound(string type)
    {
        if (SoundManagerMain.Instance != null)
            SoundManagerMain.Instance.PlaySuccessSFX(type);
    }

    private IEnumerator LoadClipThenStartRound(string clipName, string filePath)
    {
        _isLoadingClip = true;
        _loadingClipName = clipName;

        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        AudioType audioType = ext switch
        {
            ".mp3"            => AudioType.MPEG,
            ".wav"            => AudioType.WAV,
            ".ogg"            => AudioType.OGGVORBIS,
            ".aiff" or ".aif" => AudioType.AIFF,
            _                 => AudioType.UNKNOWN
        };

        if (audioType == AudioType.UNKNOWN)
        {
            Debug.LogWarning($"[RhythmRoundManager] Unsupported audio format for: {filePath}");
            _isLoadingClip = false;
            _loadingClipName = "";
            yield break;
        }

        string url = new System.Uri(filePath).AbsoluteUri;
        using UnityWebRequest req = UnityWebRequestMultimedia.GetAudioClip(url, audioType);
        yield return req.SendWebRequest();

        if (req.result == UnityWebRequest.Result.Success)
        {
            AudioClip clip = DownloadHandlerAudioClip.GetContent(req);
            clip.name = clipName;
            _runtimeClips[clipName] = clip;

            if (BeatAnalyzer.Instance != null && BeatAnalyzer.Instance.audioSource != null)
            {
                BeatAnalyzer.Instance.audioSource.clip = clip;
                StartCustomRound();
            }
        }
        else
        {
            Debug.LogWarning($"[RhythmRoundManager] Failed to load clip '{clipName}': {req.error}");
        }

        _isLoadingClip = false;
        _loadingClipName = "";
    }
}