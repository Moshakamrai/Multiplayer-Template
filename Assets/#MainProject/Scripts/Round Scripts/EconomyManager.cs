using UnityEngine;

public class EconomyManager : MonoBehaviour
{
    public static EconomyManager Instance;

    public int p1Credits = 0;
    public int p2Credits = 0;

    private int _p1WinStreak = 0;
    private int _p2WinStreak = 0;
    private int _p1LossStreak = 0;
    private int _p2LossStreak = 0;

    private int _p1ExcellentCount = 0;
    private int _p2ExcellentCount = 0;
    private bool _p1PerfectCounter = false;
    private bool _p2PerfectCounter = false;

    private void Awake()
    {
        if (Instance == null) Instance = this;
    }

    public void InitializeCredits()
    {
        p1Credits = 0;
        p2Credits = 0;
        _p1WinStreak = 0;
        _p2WinStreak = 0;
        _p1LossStreak = 0;
        _p2LossStreak = 0;
        ResetRoundTracking();
    }

    public void ResetRoundTracking()
    {
        _p1ExcellentCount = 0;
        _p2ExcellentCount = 0;
        _p1PerfectCounter = false;
        _p2PerfectCounter = false;
    }

    public void RecordExcellent(int playerIndex)
    {
        if (playerIndex == 0) _p1ExcellentCount++;
        else                  _p2ExcellentCount++;
    }

    public void RecordPerfectCounter(int playerIndex)
    {
        if (playerIndex == 0) _p1PerfectCounter = true;
        else                  _p2PerfectCounter = true;
    }

    public void OnRoundEnd(int winnerIndex)
    {
        if (winnerIndex == 0)
        {
            _p1WinStreak++;  _p1LossStreak = 0;
            _p2WinStreak = 0; _p2LossStreak++;

            AddCredits(0, _p1WinStreak >= 2 ? 3 : 2);
            AddCredits(1, _p2LossStreak >= 2 ? 2 : 1);
        }
        else if (winnerIndex == 1)
        {
            _p2WinStreak++;  _p2LossStreak = 0;
            _p1WinStreak = 0; _p1LossStreak++;

            AddCredits(1, _p2WinStreak >= 2 ? 3 : 2);
            AddCredits(0, _p1LossStreak >= 2 ? 2 : 1);
        }
        else
        {
            AddCredits(0, 1);
            AddCredits(1, 1);
            _p1WinStreak = 0; _p2WinStreak = 0;
            _p1LossStreak = 0; _p2LossStreak = 0;
        }

        if (_p1ExcellentCount > _p2ExcellentCount)       AddCredits(0, 1);
        else if (_p2ExcellentCount > _p1ExcellentCount)  AddCredits(1, 1);

        if (_p1PerfectCounter && !_p2PerfectCounter)      AddCredits(0, 1);
        else if (_p2PerfectCounter && !_p1PerfectCounter) AddCredits(1, 1);

        ResetRoundTracking();
        Debug.Log($"[EconomyManager] After round: P1={p1Credits} credits, P2={p2Credits} credits");
    }

    public void AddCredits(int playerIndex, int amount)
    {
        if (playerIndex == 0) p1Credits += amount;
        else                  p2Credits += amount;
    }

    public bool SpendCredits(int playerIndex, int amount)
    {
        if (playerIndex == 0)
        {
            if (p1Credits < amount) return false;
            p1Credits -= amount;
            return true;
        }
        else
        {
            if (p2Credits < amount) return false;
            p2Credits -= amount;
            return true;
        }
    }

    public int GetCredits(int playerIndex) => playerIndex == 0 ? p1Credits : p2Credits;

    public static bool IsPerfectCounter(string attackerMove, string defenderMove, string timingRating)
    {
        if (timingRating != "GOOD" && timingRating != "EXCELLENT") return false;

        switch (attackerMove)
        {
            case "Jab":              return defenderMove == "Dodge" || defenderMove == "Left" || defenderMove == "Right";
            case "Hook":             return defenderMove == "Block";
            case "Cage":             return defenderMove == "Hook";
            case "Cross":
            case "Blast":            return defenderMove == "Left" || defenderMove == "Right";
            case "Boom":
            case "UnbreakablePunch": return defenderMove != "Cage" && defenderMove != "ParryIntent";
        }
        return false;
    }
}
