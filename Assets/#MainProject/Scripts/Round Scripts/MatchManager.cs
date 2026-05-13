using UnityEngine;
using System.Collections;

public class MatchManager : MonoBehaviour
{
    public static MatchManager Instance;

    public int p1Wins = 0;
    public int p2Wins = 0;
    public int currentRound = 0;
    public int trackPickerIndex = -1;
    public bool isPreRoundShop = true;
    public bool isMatchActive = false;
    public bool isInShopPhase = false;
    public bool isInTrackPickPhase = false;
    public int lastRoundWinnerIndex = -1;

    public const int WinsToWin = 5;
    public const int MaxRounds = 9;

    private void Awake()
    {
        if (Instance == null) Instance = this;
    }

    public void StartMatch()
    {
        p1Wins = 0;
        p2Wins = 0;
        currentRound = 0;
        isPreRoundShop = true;
        isMatchActive = true;
        lastRoundWinnerIndex = -1;

        trackPickerIndex = Random.Range(0, 2);
        Debug.Log($"[MatchManager] Coin toss: Player {trackPickerIndex + 1} picks first track.");

        EconomyManager.Instance?.InitializeCredits();
        ShopUI.Instance?.OpenPreRoundShop(trackPickerIndex);
    }

    public void OnPreRoundShopClosed()
    {
        isPreRoundShop = false;
        isInTrackPickPhase = true;
        ShopUI.Instance?.ShowTrackPicker(trackPickerIndex);
    }

    public void SelectTrack(RoundType type, string customMapName = "")
    {
        isInTrackPickPhase = false;
        currentRound++;
        ShopUI.Instance?.CloseAll();

        switch (type)
        {
            case RoundType.SlowRhythm:
                RhythmRoundManager.Instance?.StartSlowRound();
                break;
            case RoundType.FastCombo:
                RhythmRoundManager.Instance?.StartFastRound();
                break;
            case RoundType.CustomTrack:
                StartCoroutine(StartCustomRoundByName(customMapName));
                break;
        }
    }

    private IEnumerator StartCustomRoundByName(string mapName)
    {
        yield return null;
        RhythmRoundManager.Instance?.StartCustomRound();
    }

    public void OnRoundEnded(int winnerPlayerIndex)
    {
        lastRoundWinnerIndex = winnerPlayerIndex;

        if (winnerPlayerIndex == 0)      p1Wins++;
        else if (winnerPlayerIndex == 1) p2Wins++;

        EconomyManager.Instance?.OnRoundEnd(winnerPlayerIndex);

        if (p1Wins >= WinsToWin || p2Wins >= WinsToWin || currentRound >= MaxRounds)
        {
            EndMatch();
            return;
        }

        if (winnerPlayerIndex == 0)      trackPickerIndex = 1;
        else if (winnerPlayerIndex == 1) trackPickerIndex = 0;

        isInShopPhase = true;
        ShopUI.Instance?.OpenBetweenRoundShop();
    }

    public void OnBetweenRoundShopClosed()
    {
        isInShopPhase = false;
        isInTrackPickPhase = true;
        ShopUI.Instance?.ShowTrackPicker(trackPickerIndex);
    }

    private void EndMatch()
    {
        isMatchActive = false;
        int matchWinner = (p1Wins > p2Wins) ? 0 : (p2Wins > p1Wins) ? 1 : -1;
        ShopUI.Instance?.ShowMatchResult(matchWinner, p1Wins, p2Wins);
    }

    public bool IsBotMatch() => GameManager.players.Count == 1;

    public string GetPlayerName(int index)
    {
        if (index < 0 || index >= GameManager.players.Count) return $"Player {index + 1}";
        int i = 0;
        foreach (var p in GameManager.players)
        {
            if (i == index) return p.PlayerName;
            i++;
        }
        return $"Player {index + 1}";
    }
}
