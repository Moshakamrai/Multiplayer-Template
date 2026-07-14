using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

// Griz — backstage arms dealer. Prototype NPC #1 (DESIGN_COOP.md §7).
// Personality: slightly deaf (absorbs Vosk mishearings), keeps a grudge "ledger" (a napkin),
// respects loud confidence but only once, secretly cowardly, plays favorites.
// Pure local state machine: transcript + peak mic volume in → authored line out.
public class GrizBrain : MonoBehaviour
{
    [Header("The deal on the table")]
    public string itemName = "the Rust-Cutter";
    public int basePrice = 100;
    [Tooltip("Griz will never sell below this.")]
    public int hiddenFloor = 55;

    [Header("Volume")]
    [Tooltip("Peak mic volume above this counts as SHOUTING at Griz.")]
    public float shoutVolume = 0.35f;

    // ── Hidden negotiation state ──
    public int Price { get; private set; }
    public float Patience { get; private set; }   // 0..100, 0 = kicked out
    public float Respect { get; private set; }    // 0..100, lowers his accept threshold
    public float Fear { get; private set; }       // one good threat fills it, then it's spent
    public bool DealClosed { get; private set; }
    public bool KickedOut { get; private set; }

    public enum Intent { Greet, Haggle, Offer, Flatter, Threaten, Beg, Insult, AskInfo, Smalltalk, Buy, Accept, Barter, Backstory, Inventory, Unknown }

    Intent _lastIntent = Intent.Unknown;
    int _repeat;                 // same intent in a row
    int _unknownCount;
    bool _threatSpent;           // the coward card only works once
    bool _counterPending;        // Griz just named a price → a bare "okay" accepts it
    int _storyIndex;
    readonly System.Random _rng = new System.Random();

    // Set by the UI/hub layer when the player starts talking OVER him (his audio was
    // playing when new speech arrived). Consumed by the next Process call.
    public bool PendingInterruption;

    public struct Reply
    {
        public Intent intent;
        public string line;
        public bool dealClosed;
        public bool kickedOut;
        public string revealSecret; // content of a secret unlocked THIS turn (null otherwise)
    }

    // ── Secrets: earned through conversation. The LLM prompt only ever contains the
    // HINT until the state machine unlocks the CONTENT — un-leakable by design. ──
    [Serializable]
    public class Secret
    {
        public string id;
        [TextArea] public string hint;     // always in the prompt: what to guard/deflect
        [TextArea] public string content;  // enters the prompt only once unlocked
        public float minRespect;           // >0 → unlocks at this much respect
        public float minFear;              // >0 → unlocks at this much fear
        public string triggerWord = "";    // non-empty → mentioning this unlocks it
        [NonSerialized] public bool Unlocked;
    }

    [Header("Secrets — earned through conversation")]
    public List<Secret> secrets = new List<Secret>
    {
        new Secret
        {
            id = "the-ear", minRespect = 55f,
            hint = "He NEVER tells the true story of his missing ear — he deflects with a different ridiculous fake story every time he is asked.",
            content = "THE TRUTH about the ear: he bet it in a card game he was WINNING, until his partner Vessa swapped the deck. He let her keep the ear because he loved her. Still does. He only admits this to someone he genuinely respects — and he goes quiet and sincere when he does."
        },
        new Secret
        {
            id = "the-debt", minFear = 99f, triggerWord = "bookie",
            hint = "The top name on his napkin ledger is not someone who owes HIM — it is who HE owes. He gets twitchy if the arena Bookie comes up.",
            content = "THE TRUTH about the debt: Griz owes the arena Bookie 4,000 gold and the shop itself is collateral. If word spreads he is finished. When this comes out he panics, admits it, then immediately begs the player to keep it quiet."
        },
    };

    string EvaluateSecrets(string text)
    {
        foreach (var s in secrets)
        {
            if (s == null || s.Unlocked) continue;
            bool byRespect = s.minRespect > 0f && Respect >= s.minRespect;
            bool byFear = s.minFear > 0f && Fear >= s.minFear;
            bool byWord = !string.IsNullOrEmpty(s.triggerWord) && text.Contains(s.triggerWord.ToLowerInvariant());
            if (byRespect || byFear || byWord)
            {
                s.Unlocked = true;
                return s.content;
            }
        }
        return null;
    }

    void Awake() { ResetGriz(); }

    public void ResetGriz()
    {
        Price = basePrice;
        Patience = 60f;
        Respect = 20f;
        Fear = 0f;
        DealClosed = false;
        KickedOut = false;
        _lastIntent = Intent.Unknown;
        _repeat = 0;
        _unknownCount = 0;
        _threatSpent = false;
        _counterPending = false;
        _recentLines.Clear();
        foreach (var s in secrets) if (s != null) s.Unlocked = false;
    }

    // ─────────────────────────────────────────────────────────────
    public Reply Process(string transcript, float peakVolume)
    {
        string text = Normalize(transcript);
        int offer = ExtractNumber(text);
        bool shouting = peakVolume >= shoutVolume;
        return React(text, Classify(text, offer, shouting), offer, shouting);
    }

    // LLM-understood path (LlamaIntentService): the language model supplies intent + offer;
    // the state machine and authored lines stay fully in charge of the response.
    public Reply ProcessClassified(string transcript, float peakVolume, Intent intent, int llmOffer)
    {
        string text = Normalize(transcript);
        int offer = llmOffer > 0 ? llmOffer : ExtractNumber(text);
        bool shouting = peakVolume >= shoutVolume;
        if (intent == Intent.Accept && !_counterPending) intent = Intent.Buy; // no counter on the table
        if (intent == Intent.Offer && offer <= 0) intent = Intent.Haggle;     // "offer" without a number

        // Safety net: Buy must actually be ABOUT the item on sale. The LLM classifier can
        // still misfire on sentences like "I'm here to buy chickens" (contains "buy", but
        // isn't a purchase of itemName) - closing the deal on that is a real bug we hit in
        // testing. If the utterance doesn't mention the item at all, treat it as browsing
        // (Inventory) instead of letting it silently close the sale.
        if (intent == Intent.Buy && !MentionsItem(text))
            intent = Intent.Inventory;

        return React(text, intent, offer, shouting);
    }

    bool MentionsItem(string text)
    {
        if (string.IsNullOrEmpty(itemName)) return true;
        foreach (var word in itemName.ToLowerInvariant().Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries))
            if (word.Length > 3 && text.Contains(word)) return true;
        // generic purchase words ("it", "the sword", "that") count as referring to the
        // single item on sale - only reject when the player named something ELSE specific
        return Regex.IsMatch(text, @"\b(it|that|this|the sword|the item|the weapon)\b");
    }

    public bool CounterPending => _counterPending;

    public static bool TryParseIntent(string s, out Intent intent)
    {
        s = (s ?? "").Trim().ToLowerInvariant();
        // the model occasionally uses near-miss labels — normalize the common ones
        switch (s)
        {
            case "threat": case "threats": case "threatening": s = "threaten"; break;
            case "ask": case "question": case "info": s = "askinfo"; break;
            case "compliment": case "flattery": s = "flatter"; break;
            case "trade": s = "barter"; break;
            case "purchase": s = "buy"; break;
            case "hello": case "greeting": s = "greet"; break;
        }
        return Enum.TryParse(s, true, out intent);
    }

    static string Normalize(string t) => (t ?? "").ToLowerInvariant().Trim();

    Reply React(string text, Intent intent, int offer, bool shouting)
    {
        var r = new Reply();
        if (DealClosed) { r.line = $"We're DONE. {itemName} is yours. Go hit something with it."; return r; }
        if (KickedOut) { r.kickedOut = true; r.line = "OUT. The napkin remembers."; return r; }

        bool interrupted = PendingInterruption;
        PendingInterruption = false;
        if (string.IsNullOrWhiteSpace(text)) intent = Intent.Unknown;

        _repeat = (intent == _lastIntent) ? _repeat + 1 : 0;
        _lastIntent = intent;
        r.intent = intent;
        _counterPending = false; // re-set below by any branch that puts a price on the table

        // Interruption: he objects — but still ANSWERS the question (a snark prefix,
        // never swallowing what the player actually said)
        string interruptPrefix = "";
        if (interrupted && intent != Intent.Unknown)
        {
            Patience -= 5f;
            interruptPrefix = Pick(
                "RUDE. Anyway — ",
                "I was TALKING. But fine: ",
                "The audacity. The NERVE. ...So: ");
        }

        switch (intent)
        {
            case Intent.Greet:
                r.line = _repeat == 0
                    ? Pick(
                        $"Well well. Fresh meat with FUNCTIONING legs. I'm Griz. This beauty is {itemName}. {Price} gold, and that's me being generous.",
                        $"Welcome to the finest — okay, the ONLY — shop down here. {itemName}. {Price} gold. Don't touch anything else.",
                        $"Ahh, CUSTOMERS! Welcome to Griz's Fine Armaments. Mind the blood — most of it's decorative. {itemName}, {Price} gold, today only. Also every day.")
                    : Pick(
                        "You already said hello. Once is polite, twice is a scheme.",
                        "Yes, yes, hello, hi, wonderful. BUY something.");
                break;

            case Intent.Smalltalk:
                r.line = Pick(
                    "How am I DOING? I'm surrounded by weapons and debt, kid. Living the dream.",
                    "Business is bad, my knee is worse, and you're not buying anything. So — perfect, thanks.",
                    $"Small talk is free. Everything else is {Price} gold.",
                    "Oh we're CHATTING now? Sure. Lovely weather. Underground. Where there is no weather.");
                break;

            case Intent.AskInfo:
                r.line = Pick(
                    $"{itemName}? Took it off a champion. Well. Took it off what was LEFT of a champion. {Price} gold.",
                    "What do I know? Everything. What will I TELL you? Depends what you're buying.",
                    $"The story costs nothing. The sword costs {Price}. One of these is worth hearing.");
                break;

            case Intent.Haggle:
                if (_repeat == 0)
                {
                    Price = Mathf.Max(hiddenFloor, Price - Mathf.RoundToInt(basePrice * 0.10f));
                    _counterPending = true;
                    r.line = Pick(
                        $"Ohh, a negotiator. Fine. {Price}. Because you made me laugh inside. Deep inside.",
                        $"You wound me. WOUND me. ...{Price}. Final. Probably.");
                }
                else if (_repeat == 1)
                {
                    Price = Mathf.Max(hiddenFloor, Price - Mathf.RoundToInt(basePrice * 0.05f));
                    Patience -= 10f;
                    _counterPending = true;
                    r.line = Pick(
                        $"AGAIN with the haggling. {Price}. My children will starve. I don't have children. STILL.",
                        $"{Price}, and I want you to know I'm frowning. This is my frowning face.");
                }
                else
                {
                    Patience -= 15f;
                    r.line = Pick(
                        "Give me a NUMBER or give me SILENCE.",
                        "We're going in circles. I get dizzy, I raise prices. Careful.");
                }
                break;

            case Intent.Offer:
                r = HandleOffer(offer, r);
                break;

            case Intent.Accept:
                DealClosed = true;
                r.dealClosed = true;
                r.line = Pick(
                    $"DONE. {Price} gold. Shake on it — not too hard, the arm's original.",
                    $"{Price} it is. A pleasure doing business. Mostly my pleasure.");
                break;

            case Intent.Inventory:
                r.line = _repeat == 0
                    ? Pick(
                        $"Else? ELSE? You're LOOKING at the inventory. The rest is napkins and regret. {itemName}, {Price} gold.",
                        "Oh sure, let me check the back. (does not move) ...No. It's the sword or nothing.",
                        $"Today's stock: one legendary sword. Yesterday's stock: same sword. It's a slow-moving legend. {Price} gold.")
                    : Pick(
                        "Asking twice doesn't restock the shelf, kid.",
                        $"Still just the sword. It's getting self-conscious. {Price}, and apologize to it.");
                break;

            case Intent.Barter:
                r.line = Pick(
                    "TRADE? What've you got, pockets full of AMBITION? Gold, kid. The napkin only counts gold.",
                    "I did barter once. Now I own three goats and a grudge. GOLD only.",
                    "Unless it jingles, I don't want it.");
                break;

            case Intent.Backstory:
                r.line = NextStoryLine();
                break;

            case Intent.Buy:
                DealClosed = true;
                r.dealClosed = true;
                r.line = Pick(
                    $"SOLD! {Price} gold. Pleasure ruining you — DOING business with you.",
                    $"{Price} gold and {itemName} is yours. No refunds. The napkin says so.",
                    $"SOLD for {Price}! May it serve you well. It won't. But may it.");
                break;

            case Intent.Flatter:
                if (_repeat == 0)
                {
                    Respect += 10f;
                    r.line = Pick(
                        "Flattery! I LOVE flattery. Doesn't work. Love it though.",
                        "Go on... I mean — NO discounts. But go on.");
                }
                else if (_repeat == 1)
                {
                    Respect += 5f;
                    Price = Mathf.Max(hiddenFloor, Price - 3);
                    r.line = Pick(
                        $"Okay, that one was good. {Price}. Tell NO ONE I did that.",
                        $"You have taste. {Price}, and I hate myself.");
                }
                else
                {
                    Patience -= 8f;
                    r.line = Pick(
                        "You've complimented me three times. It's getting WEIRD.",
                        "I'm flattered-out. My ego is FULL. Numbers now.");
                }
                break;

            case Intent.Threaten:
                if (offer > 0)
                {
                    Patience -= 5f;
                    _counterPending = true;
                    r.line = Pick(
                        $"{offer} gold AND a death threat? That's not haggling, that's a mugging with extra steps. {Price}.",
                        $"You offer {offer} and threaten the family I don't HAVE. Bold. Insane, but bold. Price is still {Price}.");
                }
                else if (!shouting)
                {
                    Patience -= 5f;
                    r.line = Pick(
                        "Was that a THREAT? Delivered like a lullaby? Adorable.",
                        "Threaten me with your CHEST, kid. That was a whisper with ambitions.");
                }
                else if (!_threatSpent)
                {
                    _threatSpent = true;
                    Fear = 100f;
                    Price = Mathf.Max(hiddenFloor, Price - Mathf.RoundToInt(basePrice * 0.15f));
                    r.line = Pick(
                        $"OKAY. Okay. OKAY. {Price}. Not because I'm scared. Because I respect... volume.",
                        $"WHY ARE WE YELLING — {Price}! {Price}, alright?! Maniac.");
                }
                else
                {
                    KickedOut = true;
                    r.kickedOut = true;
                    r.line = "THAT'S TWICE. Out. OUT! You're going on the napkin. IN INK.";
                }
                break;

            case Intent.Beg:
                if (Respect >= 40f)
                {
                    Price = Mathf.Max(hiddenFloor, Price - 5);
                    r.line = $"Ugh. Don't make the face. {Price}. The face WORKED, are you happy?";
                }
                else
                {
                    r.line = Pick(
                        "Begging? In MY shop? The floor is already wet with tears, kid. None of them mine.",
                        "I ran a charity once. It's called this shop. Prices ARE the charity.");
                }
                break;

            case Intent.Insult:
                Patience -= 20f;
                Price = Price + 5;
                r.line = Pick(
                    $"Rude tax. Price is {Price} now. Keep talking, I'll retire on you.",
                    $"I've been called worse by better. {Price} now. Math is my revenge.");
                if (CheckPatience(ref r)) return r;
                break;

            default: // Unknown → the deaf-guy bit: HIS flaw, not the tech's
                _unknownCount++;
                if (_unknownCount >= 3) Patience -= 5f;
                r.line = Pick(
                    "Speak into my GOOD ear. No, the other— you know what, just point.",
                    "You want a hundred CABBAGES? ...Oh. Words. Use the big clear ones.",
                    "I heard maybe four words and one of them was insulting. Try again, slower.",
                    "My left ear died in '89. The right one's on strike. LOUDER and SIMPLER.");
                break;
        }

        if (interruptPrefix.Length > 0) r.line = interruptPrefix + r.line;
        r.revealSecret = EvaluateSecrets(text);
        if (CheckPatience(ref r)) return r;
        return r;
    }

    Reply HandleOffer(int offer, Reply r)
    {
        // His real accept threshold sinks with respect and fear
        int threshold = Mathf.Max(hiddenFloor,
            Price - Mathf.RoundToInt(Respect * 0.2f) - (Fear > 0f ? 5 : 0));

        if (offer >= Price)
        {
            DealClosed = true; r.dealClosed = true;
            r.line = offer > basePrice
                ? $"{offer}?! I mean — {offer}! Yes! SOLD! (Sucker.)"
                : $"{offer} gold. DONE. See how easy that was?";
        }
        else if (offer >= threshold)
        {
            DealClosed = true; r.dealClosed = true;
            Price = offer;
            r.line = Pick(
                $"...{offer}. FINE. You're bleeding me dry but FINE. Sold.",
                $"{offer}?! ...ugh. The napkin will remember this discount. SOLD.");
        }
        else if (offer < Mathf.RoundToInt(hiddenFloor * 0.6f))
        {
            Patience -= 15f;
            r.line = Pick(
                $"{offer}?! {offer} is what I pay people to LEAVE. Insulting.",
                $"For {offer} gold I'll sell you directions to the exit.");
        }
        else
        {
            Price = Mathf.Max(hiddenFloor, (Price + offer) / 2);
            _counterPending = true;
            r.line = Pick(
                $"{offer}? Cute. Meet me at {Price} and we'll both hate it equally.",
                $"Not {offer}. {Price}. That's the sound of me compromising. Savor it.");
        }
        return r;
    }

    bool CheckPatience(ref Reply r)
    {
        if (Patience > 0f || KickedOut || DealClosed) return false;
        KickedOut = true;
        r.kickedOut = true;
        r.line = "My patience died. You killed it. Funeral's private — OUT!";
        return true;
    }

    // ─────────────────────────────────────────────────────────────
    Intent Classify(string text, int offer, bool shouting)
    {
        if (string.IsNullOrWhiteSpace(text)) return Intent.Unknown;

        int greet = Score(text, "hello", "hi", "hey", "yo", "greetings", "sup", "good day");
        int buy = Score(text, "deal", "sold", "i'll take", "ill take", "i'll buy", "ill buy", "take it", "buy it", "we're done", "done deal", "you got a deal");
        int haggle = Score(text, "cheaper", "discount", "lower", "too much", "expensive", "less", "drop the price", "come down", "better price", "best price");
        int flatter = Score(text, "nice", "love", "best", "handsome", "great", "amazing", "beautiful", "friend", "legend", "genius", "wonderful", "charming");
        int threaten = Score(text, "or else", "kill", "hurt", "break your", "burn", "smash", "regret", "punch", "destroy", "make you", "last chance", "stab", "dead");
        int beg = Score(text, "please", "broke", "poor", "mercy", "help me out", "for free", "nothing left", "i beg");
        int insult = Score(text, "ugly", "stupid", "idiot", "scam", "thief", "trash", "garbage", "rip off", "ripoff", "old man", "crook", "hate you");
        int smalltalk = Score(text, "how are you", "how you doing", "how are you doing", "how's it", "hows it",
            "what's up", "whats up", "how is business", "how's business", "you good", "you okay", "nice place");
        int backstory = Score(text, "your story", "about you", "who are you", "your life", "how did you", "why are you here",
            "about yourself", "your name", "where are you from", "what happened to you",
            // Whisper commonly mishears "ear" as "a year"/"year"/"here" in this exact phrasing
            "lose the ear", "lose your ear", "lose a year", "lose your year", "your ear", "the ear", "missing ear");
        int barter = Score(text, "trade", "swap", "exchange", "barter", "instead of gold");
        int inventory = Score(text, "anything else", "what else", "something else", "what do you have", "what you got",
            "what have you got", "do you have", "you have any", "what do you sell", "what are you selling", "selling",
            "show me", "other weapon", "other stuff", "more weapons", "inventory", "stock", "what's in", "whats in",
            "i need a", "need a sword", "need a weapon", "looking for", "i want a", "want a sword", "want a weapon");
        int accept = Score(text, "okay", "ok", "fine", "sure", "alright", "yes", "yeah", "yep", "agreed");
        int askInfo = Score(text, "what", "who", "where", "why", "how", "tell me", "story", "about this", "about the");

        bool offerContext = offer > 0 && (Score(text, "gold", "coin", "give you", "offer", "how about", "pay", "i got", "i have", "take") > 0
                                          || CountWords(text) <= 3);

        // Threats outrank everything — "twenty gold or else" is a threat, not an offer
        if (threaten > 0) return Intent.Threaten;
        if (offerContext) return Intent.Offer;
        if (buy > 0) return Intent.Buy;
        // Bare agreement while Griz has a price on the table = accepting his counter
        if (_counterPending && accept > 0 && CountWords(text) <= 4) return Intent.Accept;
        if (inventory > 0) return Intent.Inventory;
        if (barter > 0) return Intent.Barter;
        if (insult > flatter && insult > 0) return Intent.Insult;
        if (haggle > 0) return Intent.Haggle;
        if (beg > 0) return Intent.Beg;
        if (flatter > 0) return Intent.Flatter;
        if (smalltalk > 0) return Intent.Smalltalk;
        if (backstory > 0) return Intent.Backstory;
        if (greet > 0) return Intent.Greet;
        if (askInfo > 0) return Intent.AskInfo;
        return Intent.Unknown;
    }

    // Single words match on WORD BOUNDARIES ("how" must not match inside "however");
    // multi-word phrases use plain substring matching.
    static int Score(string text, params string[] keys)
    {
        int s = 0;
        foreach (var raw in keys)
        {
            string k = raw.Trim();
            if (k.Contains(" "))
            {
                if (text.Contains(k)) s++;
            }
            else if (Regex.IsMatch(text, @"\b" + Regex.Escape(k) + @"\b"))
            {
                s++;
            }
        }
        return s;
    }

    static int CountWords(string text) =>
        text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;

    // "seventy five" / "a hundred" / "75" → 75 / 100
    static readonly Dictionary<string, int> NumberWords = new Dictionary<string, int>
    {
        {"one",1},{"two",2},{"three",3},{"four",4},{"five",5},{"six",6},{"seven",7},{"eight",8},
        {"nine",9},{"ten",10},{"eleven",11},{"twelve",12},{"thirteen",13},{"fourteen",14},{"fifteen",15},
        {"sixteen",16},{"seventeen",17},{"eighteen",18},{"nineteen",19},{"twenty",20},{"thirty",30},
        {"forty",40},{"fifty",50},{"sixty",60},{"seventy",70},{"eighty",80},{"ninety",90},{"hundred",100},
    };

    public static int ExtractNumber(string text)
    {
        var digits = Regex.Match(text, @"\d+");
        if (digits.Success && int.TryParse(digits.Value, out int d)) return d;

        var words = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        int total = 0, current = 0;
        bool found = false;
        foreach (var w in words)
        {
            if (NumberWords.TryGetValue(w, out int v))
            {
                found = true;
                if (v == 100) current = Mathf.Max(1, current) * 100;
                else current += v;
            }
            else if (found) { total += current; current = 0; }
        }
        total += current;
        return found ? total : 0;
    }

    // No-repeat picker: never serves a line that's still in recent memory. If a pool is
    // fully exhausted, the repeat is OWNED in character ("As I SAID —") — different text
    // also means Piper synthesizes a fresh delivery instead of replaying the cached wav.
    readonly List<string> _recentLines = new List<string>();

    static readonly string[] RepeatPrefixes =
    {
        "As I SAID — ",
        "Again, since ears are apparently optional: ",
        "One more time, for the cheap seats: ",
        "I've said this before and I'll say it angrier: ",
    };

    string Pick(params string[] options)
    {
        var fresh = new List<string>();
        foreach (var o in options)
            if (!_recentLines.Contains(o)) fresh.Add(o);

        bool forcedRepeat = fresh.Count == 0;
        string choice = forcedRepeat
            ? options[_rng.Next(options.Length)]
            : fresh[_rng.Next(fresh.Count)];

        _recentLines.Add(choice);
        if (_recentLines.Count > 30) _recentLines.RemoveAt(0);

        return forcedRepeat ? RepeatPrefixes[_rng.Next(RepeatPrefixes.Length)] + choice : choice;
    }

    // Gentle patience recovery while he's calm (called every frame by the console) —
    // he cools off over time instead of staying one bad joke from eviction forever.
    public void Tick(float dt)
    {
        if (KickedOut || DealClosed) return;
        if (Patience > 0f && Patience < 60f)
            Patience = Mathf.Min(60f, Patience + 0.4f * dt);
    }

    // ── Backstory: every NPC blabbers lore. Sequential so stories don't repeat. ──
    static readonly string[] StoryLines =
    {
        "Me? Twenty years I fought up THERE. The crowd chants your name until one day it chants someone else's. So now I sell the chanters their swords.",
        "Lost the ear in the semifinals of '09. Not the hearing — the EAR. Won the fight though. Kept the ear. It's in a box somewhere.",
        "This shop was my manager's. He owed me money, then he owed me the shop, then he ran. His name's top of the napkin. THE ORIGINAL napkin.",
        "The arena wasn't always music, you know. Used to be just screaming. Then some genius added DRUMS to the screaming and sold tickets. Civilization!",
        "Every weapon here has a story. Usually the story is: a fighter stopped needing it. Suddenly. Mid-sentence.",
        "I had a partner once. A caller. Best voice in the business. One night the crowd was too loud and the striker couldn't hear him. That was that. SPEAK UP — that's my point.",
        "Kid came in last month, bought the janky blade, saved thirty gold. Very proud of himself. The arena keeps his boots by the door now. Nice boots.",
        "Why 'Griz'? Short for something. Nobody alive remembers what. I enjoy the mystery. Also I forgot.",
    };

    public string NextStoryLine()
    {
        string line = StoryLines[_storyIndex % StoryLines.Length];
        _storyIndex++;
        return line;
    }

    // Unprompted rambling when the players go quiet — called by the hub/console on a timer.
    public string IdleMutter()
    {
        int roll = _rng.Next(3);
        if (roll == 0) return NextStoryLine();
        return Pick(
            "(muttering) ...and SHE said the axe was 'decorative'...",
            "(scratches a new name onto the napkin, glances up at you, scratches harder)",
            "You browse like someone who can't read price tags.",
            "(hums the arena anthem, badly, with feeling)",
            $"That's {itemName} you keep eyeballing. It can tell. It's needy like that.");
    }
}
