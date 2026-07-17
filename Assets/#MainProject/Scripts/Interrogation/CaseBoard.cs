using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// GNOMES & GASLIGHT — the deterministic case: true facts, evidence, statement cards, and
// auto-flagged contradictions. This is CODE, never seen whole by any LLM (facts-cage
// principle, same as Griz's price/GrizNPC's secrets — the model performs, the game owns
// the truth). SuspectBrain decides what a suspect is ALLOWED to say; CaseBoard is where
// what they DID say gets turned into the players' persistent side-panel board.
public class CaseBoard : MonoBehaviour
{
    [Serializable]
    public class EvidenceItem
    {
        public string id;
        public string label;          // "Torn betting slip"
        [TextArea(2, 4)] public string description;
        public bool discovered;       // becomes true when the forensics drip reveals it
    }

    [Serializable]
    public class StatementCard
    {
        public string suspectName;
        public string text;           // the auto-generated card text (short, factual claim)
        public string tag;            // free-form: "timeline", "alibi", "motive", "detail"...
        public int round;
        public bool isFalseGive;
        public bool isBrokenReveal;
    }

    [Serializable]
    public class Contradiction
    {
        public StatementCard a;
        public StatementCard b;
        public string reason; // short auto-generated explanation shown in the UI
    }

    [Header("Forensics drip — fixed per round (GDD Phase D / build-order decision)")]
    [Tooltip("Evidence discovered automatically at the END of round N (index 0 = end of round 1). " +
             "Fixed-by-round: every group sees identical evidence at identical times — no group can stall.")]
    public List<EvidenceItem> evidenceDripOrder = new List<EvidenceItem>();

    public IReadOnlyList<EvidenceItem> AllEvidence => evidenceDripOrder;
    public IReadOnlyList<StatementCard> Cards => _cards;
    public IReadOnlyList<Contradiction> Contradictions => _contradictions;

    readonly List<StatementCard> _cards = new List<StatementCard>();
    readonly List<Contradiction> _contradictions = new List<Contradiction>();

    public event Action<EvidenceItem> OnEvidenceDiscovered;
    public event Action<StatementCard> OnCardAdded;
    public event Action<Contradiction> OnContradictionFound;

    public void ResetCase()
    {
        _cards.Clear();
        _contradictions.Clear();
        foreach (var e in evidenceDripOrder) e.discovered = false;
    }

    /// <summary>Called once per completed round — reveals that round's evidence to everyone,
    /// per the doc's request for progressive clues instead of pure conversation pressure.</summary>
    public void RevealEvidenceForRound(int roundIndexZeroBased)
    {
        if (roundIndexZeroBased < 0 || roundIndexZeroBased >= evidenceDripOrder.Count) return;
        var item = evidenceDripOrder[roundIndexZeroBased];
        if (item.discovered) return;
        item.discovered = true;
        OnEvidenceDiscovered?.Invoke(item);
    }

    public bool IsEvidenceDiscovered(string evidenceId) =>
        evidenceDripOrder.Any(e => e.id == evidenceId && e.discovered);

    /// <summary>Add a statement card and auto-check it against every existing card for a
    /// contradiction — the "auto-flag" hint style: the board tells players TWO cards
    /// conflict, but never explains the case for them. Simple heuristic v1: two cards
    /// tagged the same AND mentioning a time/number that differs are flagged; anything
    /// subtler is deliberately left for the players to reason about themselves.</summary>
    public StatementCard AddCard(string suspectName, string text, string tag, int round,
        bool isFalseGive = false, bool isBrokenReveal = false)
    {
        var card = new StatementCard
        {
            suspectName = suspectName, text = text, tag = tag, round = round,
            isFalseGive = isFalseGive, isBrokenReveal = isBrokenReveal
        };
        _cards.Add(card);
        OnCardAdded?.Invoke(card);

        foreach (var other in _cards)
        {
            if (other == card || other.suspectName == card.suspectName) continue;
            if (!string.Equals(other.tag, card.tag, StringComparison.OrdinalIgnoreCase)) continue;
            string reason = ConflictingTimeOrNumber(other.text, card.text);
            if (reason != null)
            {
                var c = new Contradiction { a = other, b = card, reason = reason };
                _contradictions.Add(c);
                OnContradictionFound?.Invoke(c);
            }
        }
        return card;
    }

    // Deliberately dumb v1 heuristic: same tag + different clock time or number mentioned
    // = "these don't match, go look why". Good enough to prove the auto-flag UX; a case
    // author can always hand-seed known contradictions via AddCard's tag matching instead
    // of relying on this alone.
    static string ConflictingTimeOrNumber(string a, string b)
    {
        var timeA = System.Text.RegularExpressions.Regex.Match(a, @"\b\d{1,2}(:\d{2})?\s?(am|pm)?\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var timeB = System.Text.RegularExpressions.Regex.Match(b, @"\b\d{1,2}(:\d{2})?\s?(am|pm)?\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (timeA.Success && timeB.Success && !string.Equals(timeA.Value, timeB.Value, StringComparison.OrdinalIgnoreCase))
            return $"Different times mentioned for the same kind of event: \"{timeA.Value}\" vs \"{timeB.Value}\".";
        return null;
    }
}
