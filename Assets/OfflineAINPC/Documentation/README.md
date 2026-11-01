# Offline AI NPC Toolkit

Talking NPCs that run **entirely on the player's machine**. No API keys, no accounts, no
per-user cost, no internet connection. The language model ships inside your game.

---

## Why offline

Cloud LLM NPCs look great in a prototype and then meet reality:

| | Cloud API | This toolkit |
|---|---|---|
| Cost per player | Forever, per message | Zero |
| Needs internet | Yes | No |
| Latency | Network round trip | Local |
| Player data | Leaves the machine | Never leaves |
| Vendor changes pricing | Your margins change | Nothing changes |
| Works in 5 years | If the API still exists | Yes |

The trade-off is honest: you ship a model file (a few GB), and quality is below the largest
frontier models. For NPC dialogue — short, in-character, constrained — a good 7B model is
more than enough.

---

## The core idea: the LLM is not in charge

Most LLM-NPC implementations let the model generate freely. Players then discover they can
talk a merchant into selling a legendary sword for 1 gold, or get an NPC to explain that it
is an AI language model.

This toolkit inverts that. The model's job is to **understand**, not to decide:

```
Player speaks
   ↓
LLM picks ONE intent from a list you define   ← the only thing the model decides
   ↓
YOUR C# decides what happens and what is said  ← full authority, fully testable
   ↓
(optional) LLM rephrases your approved line in character
```

Because prices and outcomes are computed in your code, the player cannot argue with them.
The demo merchant has a `minPrice` floor that is simple arithmetic — no prompt engineering
is holding it in place, so no prompt injection can move it.

---

## Try it in 30 seconds

`Tools > Offline AI NPC > Create Demo Scene`, then press Play and type at the merchant.

**This works with no model installed** — it runs on the keyword fallback, which is exactly
what your players get on a machine where setup failed. Try:

| Say | What happens |
|---|---|
| `hello` | He quotes the asking price |
| `that's too much` | Concedes 10%, never past his floor |
| `I'll give you 70 gold` | Counters by meeting in the middle |
| `okay` | Closes at the counteroffer |
| `cheaper` ×10 | Stops dead at `minPrice` — the floor is arithmetic, not a prompt |
| `20 gold or else` | Read as a **threat**, not an offer. Price stands |
| `I'll kill you` | Second threat and he refuses to trade at all |
| `this is a rip off` | Insult — the price goes **up** |

The status line at the top tells you whether replies came from the LLM or the fallback. Once
a model is installed the same scene starts understanding phrasings nobody wrote keywords for,
while every number above stays exactly as your code computed it.

---

## Quick start

### 1. Install the model

`Tools > Offline AI NPC > Setup Wizard` walks you through it:

- Download a **llama.cpp** release build and put `llama-server.exe` in
  `Assets/StreamingAssets/llama/`
- Put one **`.gguf`** model in the same folder (a 7–8B at Q4_K_M is a good default)

The wizard verifies both and reports what's missing.

> **The package works without this.** With no model installed, NPCs fall back to keyword
> matching — your game still builds, runs, and ships. Install the model when you want real
> language understanding.

### 2. Define what your NPC can understand

`Create > Offline AI NPC > Intent Set`. For each intent, give an `id`, a plain-English
`whenToUse`, and a few `fallbackKeywords`.

The `whenToUse` text is the single biggest lever on quality — write it like you are briefing
a new employee, with an example wherever the line is fuzzy. It goes into the prompt verbatim.

Keep the list tight. Six well-separated intents classify far more reliably than twenty
overlapping ones.

### 3. Write your NPC

Derive from `NpcConversation` and implement `Respond`:

```csharp
using OfflineAINPC;

public class Guard : NpcConversation
{
    public bool playerHasPass;

    protected override string Respond(NpcIntentResult result, string utterance)
    {
        switch (result.intent)
        {
            case "greet":   return "State your business.";
            case "bribe":
                // Your rules. The model cannot argue with this.
                if (result.amount >= 50) { /* open gate */ return "...walk on. Quickly."; }
                return $"{result.amount} gold? Insulting.";
            case "demand":  return playerHasPass ? "Pass verified. Go." : "No pass, no entry.";
            default:        return "Move along.";
        }
    }
}
```

Add a `LocalLlmService` to the scene, assign it plus your intent set, hook `onNpcLine` to
your dialogue UI, and call `Say("...")` with typed text or a speech transcript.

---

## Giving the classifier context

Some utterances are impossible to classify in isolation. "Fine." means nothing unless the
model knows an offer is on the table. Override `BuildClassifierContext`:

```csharp
protected override string BuildClassifierContext() =>
    $"item_for_sale: {itemName}\n" +
    $"current_price: {_price}\n" +
    $"counteroffer_open: {(_counterOffer > 0 ? "true" : "false")}";
```

This one override is usually the difference between a classifier that feels sharp and one
that feels random.

---

## Optional: LLM phrasing

With `useLlmPhrasing` off, your `Respond` text is spoken verbatim: deterministic, instant,
fully testable. Recommended while building.

With it on, the model rephrases your line in character, with instructions to preserve every
fact and number. You get varied dialogue, but the line still originated from a decision your
code made. Generated text passes through guardrails that strip stage directions and self
prefixes, cap length at a sentence boundary, and reject anything that breaks character.

If generation fails, times out, or produces something suspect, **your authored line is used
instead** — the player never sees a failure.

---

## API

### `LocalLlmService`
| Member | Purpose |
|---|---|
| `IsReady` | True once the local server is answering. False = fallbacks in use. |
| `StatusMessage` | Human-readable state, for your own setup UI. |
| `Classify(...)` | Utterance → validated intent. Always calls back. |
| `GenerateReply(...)` | Persona + context → a line. `success = false` on any problem. |
| `CleanGenerated(...)` | Static. The text guardrails, usable on its own. |
| `Json(...)` | Static. Correct JSON string escaping, including `\r`. |

### `NpcIntentSet`
| Member | Purpose |
|---|---|
| `intents` | The list of recognisable meanings. |
| `extractAmount` | Also pull a number out of the utterance. |
| `BuildClassificationPrompt()` | The generated prompt — inspect it when tuning. |
| `MatchKeywords(...)` | The offline fallback classifier. |
| `IsValidIntent(id)` | Validation; rejects anything the model invents. |

### `NpcConversation`
| Member | Purpose |
|---|---|
| `Respond(...)` | **Implement this.** Your game logic. |
| `BuildClassifierContext()` | Optional per-turn state for the classifier. |
| `BuildFacts()` | Optional facts the character may state. |
| `Say(text)` | Feed in player input. |
| `onNpcLine` | Fires with the final line. |
| `IsBusy` | A turn is in flight; ignore new input. |
| `ResetConversation()` | Clear history between encounters. |

---

## Tuning

**Classification is wrong.** Enable `logRawResponses` and read what came back. It is almost
always the prompt: sharpen the `whenToUse` text for the intents being confused, and make sure
`BuildClassifierContext` supplies the state needed to tell them apart.

**Too slow.** Raise `gpuLayers` (20–35 suits most 7B models on a mid-range GPU; too high
fails to allocate). Turn off `useLlmPhrasing` — classification alone is much faster. Use a
smaller quantisation.

**Everything falls back silently.** Check `StatusMessage`. Usually the model or exe is
missing, or `contextSize` is too small — an overflowing context truncates requests with no
error of its own.

**Port already in use.** A previous server survived. The service kills its child on
`OnDestroy`/`OnApplicationQuit`, but a hard Editor crash can orphan one; end it in Task
Manager or change `port`.

---

## Shipping your game

`StreamingAssets` is copied into builds, so the server and model travel with the game.

1. **Budget the size.** A 7B Q4_K_M model is ~4.5 GB.
2. **Check the model's licence.** They vary and some forbid commercial use. This is your
   decision, which is exactly why no model is bundled.
3. **Attribute llama.cpp** (MIT) in your credits.
4. **Test on a low-end machine.** Set `gpuLayers = 0` to confirm the CPU path is acceptable,
   and verify the keyword fallback path too.

---

## Platforms

Desktop: **Windows** (shipped path), **macOS/Linux** (works where a llama.cpp server build
exists; the component looks for `llama-server.exe`, so adjust the filename).

Not supported: mobile, consoles, WebGL — they cannot launch a child process. On those
platforms the toolkit degrades to the keyword classifier, so your game still runs.

---

## Third-party

This package contains **only C# source** — no binaries, no model weights. You supply:

- **llama.cpp** — MIT — https://github.com/ggml-org/llama.cpp
- **A GGUF model** — licence varies by model; check before shipping
