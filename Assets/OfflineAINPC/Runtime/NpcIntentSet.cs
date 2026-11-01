using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace OfflineAINPC
{
    /// <summary>
    /// Defines the intents one NPC can recognise, as data.
    ///
    /// This is the piece that keeps an LLM on a leash. Instead of letting the model say
    /// whatever it likes, you enumerate the finite set of things a player can MEAN here;
    /// the model's only job is to pick one. Your own game logic then decides what happens,
    /// so the NPC can never invent a price, an item, or a promise you did not author.
    ///
    /// The same data drives three things, which is why it is worth defining carefully:
    ///   1. The LLM classification prompt (generated — see BuildClassificationPrompt)
    ///   2. The offline keyword fallback used when no model is installed (MatchKeywords)
    ///   3. Validation, so a hallucinated intent name is rejected rather than passed on
    /// </summary>
    [CreateAssetMenu(menuName = "Offline AI NPC/Intent Set", fileName = "NewIntentSet")]
    public class NpcIntentSet : ScriptableObject
    {
        [Serializable]
        public class Intent
        {
            [Tooltip("Identifier your game code switches on. Lowercase, no spaces (e.g. \"haggle\").")]
            public string id;

            [Tooltip("Explain to the model when to choose this intent. Write it like you are " +
                     "briefing a new employee: concrete, with an example if the line is fuzzy. " +
                     "This text goes verbatim into the prompt and is the single biggest lever " +
                     "on classification quality.")]
            [TextArea(2, 4)]
            public string whenToUse;

            [Tooltip("Keywords/phrases for the no-model fallback. Single words match on word " +
                     "boundaries (\"how\" will not match inside \"however\"); multi-word entries " +
                     "match as substrings. Not used when an LLM is available.")]
            public string[] fallbackKeywords = Array.Empty<string>();

            [Tooltip("Checked before lower-priority intents in the fallback classifier. Use for " +
                     "intents that must win when ambiguous — e.g. a threat outranks an offer, so " +
                     "\"twenty gold or else\" is read as a threat.")]
            public int fallbackPriority;
        }

        [Header("Identity")]
        [Tooltip("What the player is talking to and about. One line of context for the classifier, " +
                 "e.g. \"a customer haggling with a fantasy weapon-shop merchant\".")]
        public string situation = "a player talking to an NPC";

        [Header("Intents")]
        [Tooltip("Every meaning this NPC can recognise. Keep the list tight — a smaller, " +
                 "well-separated set classifies far more reliably than a long fuzzy one.")]
        public List<Intent> intents = new List<Intent>();

        [Header("Numeric extraction (optional)")]
        [Tooltip("Also ask the model to pull a number out of the utterance (a gold offer, a " +
                 "quantity, a page number). Exposed as NpcIntentResult.amount.")]
        public bool extractAmount;

        [Tooltip("What the number means, for the prompt — e.g. \"the gold amount if the player " +
                 "proposes a price, else 0\".")]
        public string amountMeaning = "the number the player names, else 0";

        [Tooltip("Returned when nothing matches. Must also exist in the list above so your " +
                 "code can handle it like any other intent.")]
        public string fallbackIntentId = "unknown";

        /// <summary>Valid intent ids, for validating whatever the model returns.</summary>
        public bool IsValidIntent(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            for (int i = 0; i < intents.Count; i++)
                if (string.Equals(intents[i].id, id, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        /// <summary>
        /// Builds the system prompt for classification. Generated rather than hand-written so
        /// the prompt can never drift out of sync with the intent list — edit the asset, and
        /// the prompt and the fallback both follow.
        ///
        /// Constrained deliberately: JSON only, one line, fixed key set. Paired with
        /// temperature 0 in LocalLlmService, this makes the output near-deterministic.
        /// </summary>
        public string BuildClassificationPrompt()
        {
            var sb = new StringBuilder();
            sb.Append("You classify one utterance from ").Append(situation).Append(".\n");
            sb.Append("Reply with ONLY one line of JSON, nothing else: ");
            sb.Append(extractAmount ? "{\"intent\":\"...\",\"amount\":0}\n" : "{\"intent\":\"...\"}\n");

            sb.Append("intent must be one of: ");
            for (int i = 0; i < intents.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(intents[i].id);
            }
            sb.Append(".\n");

            if (extractAmount)
                sb.Append("amount = ").Append(amountMeaning).Append(".\n");

            bool anyGuidance = false;
            for (int i = 0; i < intents.Count; i++)
                if (!string.IsNullOrWhiteSpace(intents[i].whenToUse)) { anyGuidance = true; break; }

            if (anyGuidance)
            {
                sb.Append("Meanings: ");
                for (int i = 0; i < intents.Count; i++)
                {
                    var it = intents[i];
                    if (string.IsNullOrWhiteSpace(it.whenToUse)) continue;
                    sb.Append(it.id).Append(" = ").Append(it.whenToUse.Trim().TrimEnd('.')).Append(". ");
                }
                sb.Append('\n');
            }

            sb.Append("If nothing fits, use ").Append(fallbackIntentId).Append('.');
            return sb.ToString();
        }

        /// <summary>
        /// Offline keyword classifier, used when no LLM is installed. Deliberately crude: it
        /// exists so your game still runs (and ships) without a model, not to match LLM quality.
        /// Highest fallbackPriority wins, then highest keyword hit count.
        /// </summary>
        public string MatchKeywords(string utterance)
        {
            if (string.IsNullOrWhiteSpace(utterance)) return fallbackIntentId;
            string text = utterance.ToLowerInvariant();

            string bestId = fallbackIntentId;
            int bestPriority = int.MinValue;
            int bestScore = 0;

            for (int i = 0; i < intents.Count; i++)
            {
                var it = intents[i];
                if (it.fallbackKeywords == null || it.fallbackKeywords.Length == 0) continue;

                int score = 0;
                foreach (var raw in it.fallbackKeywords)
                {
                    if (string.IsNullOrWhiteSpace(raw)) continue;
                    string k = raw.Trim().ToLowerInvariant();

                    // Multi-word phrases match as substrings; single words must match on word
                    // boundaries, or "how" fires inside "however" and short keywords dominate.
                    if (k.Contains(" "))
                    {
                        if (text.Contains(k)) score++;
                    }
                    else if (Regex.IsMatch(text, @"\b" + Regex.Escape(k) + @"\b"))
                    {
                        score++;
                    }
                }

                if (score == 0) continue;
                if (it.fallbackPriority > bestPriority ||
                    (it.fallbackPriority == bestPriority && score > bestScore))
                {
                    bestPriority = it.fallbackPriority;
                    bestScore = score;
                    bestId = it.id;
                }
            }

            return bestId;
        }

        /// <summary>Pulls the first integer out of an utterance, for the fallback path.</summary>
        public static int ExtractFirstNumber(string utterance)
        {
            if (string.IsNullOrEmpty(utterance)) return 0;
            var m = Regex.Match(utterance, @"\d+");
            return m.Success && int.TryParse(m.Value, out int n) ? n : 0;
        }
    }
}
