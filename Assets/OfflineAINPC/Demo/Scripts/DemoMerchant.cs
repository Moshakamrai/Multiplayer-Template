using UnityEngine;

namespace OfflineAINPC.Demo
{
    /// <summary>
    /// A haggling merchant in ~100 lines of game logic, to show the shape of a real NPC.
    ///
    /// Note what is NOT here: no prompt engineering, no JSON parsing, no subprocess handling, no
    /// timeout logic. Those live in the package. This file is only the negotiation rules.
    ///
    /// Note also that the player cannot talk him below minPrice. Not because the prompt asks
    /// nicely, but because the price is arithmetic in C# and the model is never consulted about
    /// it. Change useLlmPhrasing and the wording varies; the numbers cannot.
    /// </summary>
    [AddComponentMenu("Offline AI NPC/Demo/Demo Merchant")]
    public class DemoMerchant : NpcConversation
    {
        [Header("Shop")]
        public string itemName = "iron sword";
        public int askingPrice = 100;

        [Tooltip("He will never go below this, whatever the player says.")]
        public int minPrice = 60;

        [Tooltip("Gold the player has, for the demo. Drive this from your own inventory in a real game.")]
        public int playerGold = 80;

        int _price;
        int _counterOffer;          // >0 while a counteroffer is on the table
        bool _greeted;
        bool _sold;
        int _threats;
        bool _refusing;             // threatened twice — will not trade at all

        void Awake() { _price = askingPrice; }

        /// <summary>
        /// Short replies like "fine" are impossible to classify without this. Telling the model
        /// an offer is open is what separates "accept" from generic agreement.
        /// </summary>
        protected override string BuildClassifierContext()
        {
            return $"item_for_sale: {itemName}\n" +
                   $"current_price: {_price}\n" +
                   $"counteroffer_open: {(_counterOffer > 0 ? "true" : "false")}";
        }

        /// <summary>Only facts he is allowed to say out loud.</summary>
        protected override string BuildFacts()
        {
            return $"You sell exactly one item: a {itemName}. Its price is {_price} gold.\n" +
                   (_counterOffer > 0 ? $"You have offered it for {_counterOffer} gold.\n" : "") +
                   (_sold ? "The deal is already done.\n" : "");
        }

        protected override string Respond(NpcIntentResult result, string utterance)
        {
            if (_sold && result.intent != "greet")
                return "Deal's done. Go on, enjoy it.";

            // Refusal outranks everything except a fresh greeting. Enforced here in code, so
            // no amount of persuasion reopens the shop — the same principle as the price floor.
            if (_refusing && result.intent != "greet")
                return "I said we're done.";

            switch (result.intent)
            {
                case "greet":
                    if (_refusing)
                    {
                        // A civil re-approach reopens trade, so the demo can't dead-end.
                        _refusing = false;
                        _threats = 0;
                        return $"...Fine. {_price} gold, and keep it civil this time.";
                    }
                    if (_greeted) return "You again. Still got that " + itemName + " if you want it.";
                    _greeted = true;
                    return $"Aye. {Cap(itemName)}, {_price} gold. Interested?";

                case "inventory":
                    return $"One {itemName}. That's it. {_price} gold.";

                case "askinfo":
                    return $"Good steel, honest edge. {_price} gold, and that's fair.";

                case "haggle":
                    // Concede 10%, never past the floor. Pure arithmetic — unarguable.
                    int next = Mathf.Max(minPrice, Mathf.RoundToInt(_price * 0.9f));
                    if (next >= _price) return $"No. {_price} is already the bone, not the meat.";
                    _price = next;
                    _counterOffer = next;
                    return $"...Fine. {next} gold. Not a copper less.";

                case "offer":
                    return HandleOffer(result.amount);

                case "accept":
                    if (_counterOffer <= 0) return "Accept what? I've made you no offer.";
                    return TryClose(_counterOffer);

                case "buy":
                    return TryClose(_price);

                case "insult":
                    // Deliberately punishing: shows game logic reacting to tone, not just words.
                    _price = Mathf.RoundToInt(_price * 1.1f);
                    _counterOffer = 0;
                    return $"Price just went up. {_price} gold. Mind your mouth.";

                case "threaten":
                    // Note this is reached even for "20 gold or else" — the intent set gives
                    // threaten a higher fallbackPriority than offer, so a threat wrapped around
                    // a number is still a threat. Worth keeping: it is the clearest example of
                    // why priority ordering exists.
                    _threats++;
                    if (_threats >= 2)
                    {
                        _refusing = true;
                        _counterOffer = 0;
                        return "No. We're done — take your threats elsewhere.";
                    }
                    return "Try it and you'll leave with fewer fingers. Price stands.";

                case "flatter":
                    return "Flattery's free. The sword isn't.";

                default:
                    return $"Hm. {_price} gold for the {itemName}. Yes or no?";
            }
        }

        string HandleOffer(int amount)
        {
            if (amount <= 0) return "Name a number, then.";
            if (amount >= _price) return TryClose(amount);

            if (amount < minPrice)
                return $"{amount}? No. {minPrice} is the lowest I'll hear.";

            // Within range: meet in the middle, floored.
            int meet = Mathf.Max(minPrice, (amount + _price) / 2);
            _price = meet;
            _counterOffer = meet;
            return $"{amount}'s an insult, but you're close. {meet} and it's yours.";
        }

        string TryClose(int price)
        {
            if (playerGold < price)
                return $"You haven't got {price} gold. Come back when you do.";

            playerGold -= price;
            _sold = true;
            _counterOffer = 0;
            return $"Done. {price} gold, and the {itemName}'s yours. Don't come crying if it chips.";
        }

        static string Cap(string s) =>
            string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
}
