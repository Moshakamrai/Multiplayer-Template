using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace OfflineAINPC
{
    /// <summary>
    /// Base class for a talking NPC. Handles the conversation plumbing — classify, route to your
    /// handler, keep history, optionally phrase the reply with the LLM — so your subclass only
    /// writes game logic.
    ///
    /// THE PATTERN THIS ENFORCES
    ///
    /// Derived classes implement Respond(), which receives a validated intent and returns the
    /// line the NPC says. Your code decides every outcome: prices, whether a sale happens, what
    /// is revealed. The LLM may only (a) pick an intent from your list and (b) optionally
    /// rephrase a line you already approved.
    ///
    /// This is why a merchant built on this cannot be talked into selling a sword for 1 gold, no
    /// matter how clever the player is. The model never holds the authority. Letting an LLM
    /// generate freely is the common approach and the reason most LLM-NPC demos fall apart on
    /// contact with players.
    /// </summary>
    public abstract class NpcConversation : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("Shared LLM service. One per scene is enough — several NPCs can use the same one.")]
        public LocalLlmService llm;

        [Tooltip("Intents this NPC understands.")]
        public NpcIntentSet intentSet;

        [Header("Character")]
        [Tooltip("Shown to the player and used to strip the model's \"NAME:\" self-prefix.")]
        public string npcName = "NPC";

        [Header("Optional LLM phrasing")]
        [Tooltip("Let the model rephrase your authored line in character. OFF = your text is " +
                 "used verbatim (fully deterministic, zero latency). ON = more varied dialogue, " +
                 "but every line is still one YOUR code chose — only the wording changes.")]
        public bool useLlmPhrasing;

        [Tooltip("Character brief used when phrasing is on: who they are, how they talk, and " +
                 "their hard rules. State explicitly what they must never do — invent prices, " +
                 "mention being an AI, narrate actions.")]
        [TextArea(4, 12)]
        public string persona = "You are a terse NPC. Reply in 1-2 short sentences of spoken " +
                               "dialogue only. Never mention being an AI. Never invent facts, " +
                               "prices, or items: only react to what you are told.";

        [Tooltip("Turns of history sent as context. Higher is more coherent but uses more of the " +
                 "context window and is slower.")]
        public int historyTurns = 6;

        [Header("Events")]
        [Tooltip("Fires with the NPC's final line, whatever path produced it. Hook your dialogue " +
                 "UI and/or a TTS component here.")]
        public NpcLineEvent onNpcLine;

        [Serializable] public class NpcLineEvent : UnityEngine.Events.UnityEvent<string> { }

        /// <summary>True while a turn is in flight. Ignore new input until it clears.</summary>
        public bool IsBusy { get; private set; }

        readonly List<string> _history = new List<string>();

        /// <summary>
        /// Handle one classified utterance and return what the NPC says. All game logic lives
        /// here: this is where you check gold, decide a counteroffer, unlock a secret.
        /// </summary>
        /// <param name="result">Validated intent — guaranteed to be in your intent set.</param>
        /// <param name="utterance">Raw player text, for logging or keyword checks.</param>
        /// <returns>The line to say. Return null to stay silent.</returns>
        protected abstract string Respond(NpcIntentResult result, string utterance);

        /// <summary>
        /// Optional per-turn state for the classifier, as "key: value" lines. Supply whatever is
        /// needed to disambiguate — without it, a bare "fine" or "okay" cannot be classified.
        /// </summary>
        protected virtual string BuildClassifierContext() => null;

        /// <summary>
        /// Optional facts the character may state when phrasing is on. Only include what they are
        /// allowed to say: anything here can reach the player.
        /// </summary>
        protected virtual string BuildFacts() => null;

        /// <summary>Feed the NPC a line from the player (speech transcript or typed text).</summary>
        public void Say(string utterance)
        {
            if (IsBusy || string.IsNullOrWhiteSpace(utterance)) return;
            StartCoroutine(Turn(utterance));
        }

        /// <summary>Clears conversation history. Call between encounters.</summary>
        public void ResetConversation() { _history.Clear(); }

        IEnumerator Turn(string utterance)
        {
            IsBusy = true;

            NpcIntentResult result = default;
            bool got = false;

            if (llm != null && intentSet != null)
            {
                yield return llm.Classify(utterance, intentSet, r => { result = r; got = true; },
                                          BuildClassifierContext());
            }
            else if (intentSet != null)
            {
                // No service wired at all — still classify, so the NPC works in a bare scene.
                result = new NpcIntentResult
                {
                    intent = intentSet.MatchKeywords(utterance),
                    amount = intentSet.extractAmount ? NpcIntentSet.ExtractFirstNumber(utterance) : 0,
                    fromLlm = false,
                };
                got = true;
            }

            if (!got)
            {
                Debug.LogWarning($"[OfflineAINPC] {npcName}: no intent set assigned, turn ignored.");
                IsBusy = false;
                yield break;
            }

            // Game logic decides the line. This call is the authority.
            string authored = Respond(result, utterance);

            if (!string.IsNullOrWhiteSpace(authored))
            {
                string final = authored;

                if (useLlmPhrasing && llm != null && llm.IsReady)
                {
                    string context = BuildPhrasingContext(authored);
                    bool done = false;
                    yield return llm.GenerateReply(persona, context,
                        (text, ok) => { if (ok && !string.IsNullOrWhiteSpace(text)) final = text; done = true; },
                        npcName);
                    if (!done) final = authored; // watchdog tripped; authored line stands
                }

                _history.Add("Player: " + utterance.Trim());
                _history.Add(npcName + ": " + final);
                TrimHistory();

                onNpcLine?.Invoke(final);
            }

            IsBusy = false;
        }

        /// <summary>
        /// Builds the phrasing request. The authored line is passed as the intended MEANING, with
        /// an instruction to rephrase rather than invent — this is what keeps generated wording
        /// tethered to a decision your code already made.
        /// </summary>
        string BuildPhrasingContext(string authoredLine)
        {
            var sb = new StringBuilder();

            if (_history.Count > 0)
            {
                sb.Append("CONVERSATION SO FAR:\n");
                for (int i = 0; i < _history.Count; i++) sb.Append(_history[i]).Append('\n');
                sb.Append('\n');
            }

            string facts = BuildFacts();
            if (!string.IsNullOrWhiteSpace(facts)) sb.Append("FACTS YOU MAY STATE:\n").Append(facts).Append("\n\n");

            sb.Append("Say this, in your own voice, keeping every fact and number exactly as given:\n");
            sb.Append(authoredLine);
            return sb.ToString();
        }

        void TrimHistory()
        {
            int max = Mathf.Max(2, historyTurns * 2); // a "turn" is player + NPC
            while (_history.Count > max) _history.RemoveAt(0);
        }

        /// <summary>Read-only conversation log, for debug UI or transcripts.</summary>
        public IReadOnlyList<string> History => _history;
    }
}
