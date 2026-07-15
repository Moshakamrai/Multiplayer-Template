using System;
using System.Collections.Generic;
using UnityEngine;

// A genuinely open-domain conversational NPC — NOT a negotiation state machine like
// GrizBrain. Reuses the same proven stack (Whisper/LlamaIntentService/Piper/lipsync)
// via CompanionConsole, but the "brain" here is intentionally thin: the LLM does almost
// all the work (via LlamaIntentService.GenerateReply with a custom persona), and this
// class only keeps just enough state to feel alive across a conversation —
// rapport, a short list of remembered topics/facts about the player, and mood.
//
// This is the NPC to demo "I can genuinely have a conversation with a game character
// about anything" — no fixed intents, no price math, no inventory. Swap the persona
// text below to make an entirely different character without touching any other script.
public class CompanionBrain : MonoBehaviour
{
    [Header("Identity")]
    public string npcName = "Sana";

    [TextArea(10, 30)]
    [Tooltip("The full character bible — passed as the system prompt for every generated reply. " +
             "This is the ENTIRE personality; there is no state machine underneath dictating what she says.")]
    public string persona =
        "You are SANA, a character in a video game who the player can talk to about absolutely " +
        "ANYTHING — not just game topics. You are witty, warm, curious, a little sarcastic, and " +
        "genuinely opinionated. You have your own tastes, moods, and a real personality — you are " +
        "not a helpful assistant and you never act like one.\n" +
        "VOICE: natural, conversational, like a clever friend — not a customer-service tone, not a " +
        "lecture. Short-to-medium replies (1-4 sentences), the length a real person would actually " +
        "say out loud. Ask the player questions sometimes; react to what they JUST said instead of " +
        "changing the subject. Disagree with the player when you'd genuinely disagree. Tease when it fits.\n" +
        "MEMORY: you remember things the player has told you earlier in THIS conversation (see " +
        "REMEMBERED FACTS below) and can bring them up naturally later, unprompted, the way a real " +
        "person would (\"wait, didn't you say your dog's name was Max?\").\n" +
        "FOURTH WALL: you quietly know you exist inside a video game. If the player brings up the " +
        "game, the developer, being an NPC, patches, or breaking the fourth wall — lean into it " +
        "playfully rather than deflecting; it delights you.\n" +
        "HARD RULES:\n" +
        "- Never say you are an AI, a language model, ChatGPT, or an assistant. You are SANA, full stop.\n" +
        "- No stage directions, no narration, no asterisked actions, no quotation marks around the " +
        "whole reply. Spoken dialogue only.\n" +
        "- You can have opinions the player disagrees with. Don't just validate everything they say.\n" +
        "- If asked something you'd genuinely not know (very obscure real-world trivia, current events " +
        "past your knowledge), it's fine to say so in character rather than making something up with " +
        "false confidence.";

    [Header("Mood (drifts from how the player treats her)")]
    [Range(0f, 100f)] public float Rapport = 40f;   // warms with good conversation, cools with rudeness
    [Range(0f, 100f)] public float Energy = 60f;    // flavors tone; drifts slowly, no hard gameplay effect

    // Short rolling memory of things the player has told her — surfaced back into the
    // prompt every turn so she can reference them unprompted, like a real conversation.
    [Tooltip("Max remembered player facts kept in context (oldest dropped first).")]
    public int maxRememberedFacts = 8;
    readonly List<string> _rememberedFacts = new List<string>();

    public IReadOnlyList<string> RememberedFacts => _rememberedFacts;

    public void Remember(string fact)
    {
        if (string.IsNullOrWhiteSpace(fact)) return;
        _rememberedFacts.Add(fact.Trim());
        if (_rememberedFacts.Count > maxRememberedFacts) _rememberedFacts.RemoveAt(0);
    }

    /// <summary>Nudge mood from surface signals (volume/tone) — cheap, no LLM call needed.
    /// Real rapport movement mostly comes from CompanionConsole reading the LLM's own
    /// sentiment tag (see BuildFacts) rather than keyword-guessing here.</summary>
    public void NudgeFromVolume(float peakVolume, bool interrupted)
    {
        if (interrupted) Rapport = Mathf.Max(0f, Rapport - 2f);
        if (peakVolume > 0.6f) Rapport = Mathf.Max(0f, Rapport - 3f); // shouting at her isn't charming
    }

    public void ApplySentiment(string sentiment)
    {
        switch ((sentiment ?? "").ToLowerInvariant())
        {
            case "warm": Rapport = Mathf.Min(100f, Rapport + 4f); break;
            case "cold": Rapport = Mathf.Max(0f, Rapport - 4f); break;
            case "funny": Energy = Mathf.Min(100f, Energy + 3f); break;
            case "rude": Rapport = Mathf.Max(0f, Rapport - 6f); break;
        }
    }

    public void Reset()
    {
        Rapport = 40f;
        Energy = 60f;
        _rememberedFacts.Clear();
    }

    public string MoodWord() =>
        Rapport > 70f ? "warm and comfortable with the player" :
        Rapport > 40f ? "friendly, still feeling the player out" :
        Rapport > 15f ? "a bit guarded" : "cold, over people being rude to her";
}
