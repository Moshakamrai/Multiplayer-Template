using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Tools > Griz > Create Voxxy Kiosk Demo Scene
//
// The Voxxy Studio pitch deck's flagship use case, as a runnable demo: a "Financial
// Inclusion Terminal" — a voice-driven bank kiosk assistant for rural Bangladesh, where
// semi-literate customers handle account queries entirely through spoken Bangla
// (Pitch deck: Strategic Market Entry, slide 5; "custom-branded conversational voice
// personas" is the slide-6 deliverable this demonstrates).
//
// Reuses the entire proven NPC stack (CompanionBrain/Console + Whisper bn + NLLB both
// ways + trained bn_BD Piper voice) with a kiosk persona and MOCK account data injected
// as authoritative domain facts — same facts-cage principle as Griz's shop prices: the
// LLM voices the numbers, the data owns them. It cannot invent a balance.
public static class VoxxyKioskSceneBuilder
{
    const string ScenePath = "Assets/Scenes/VoxxyKioskDemo.unity";

    [MenuItem("Tools/Griz/Create Voxxy Kiosk Demo Scene")]
    static void CreateScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        var voskGO = new GameObject("Vosk (free vocabulary)");
        var vp = voskGO.AddComponent<VoiceProcessor>();
        var stt = voskGO.AddComponent<VoskSpeechToText>();
        stt.VoiceProcessor = vp;
        stt.AutoStart = true;
        stt.KeyPhrases = new List<string>();
        stt.FreeDictation = true;
        stt.MaxAlternatives = 0;
        stt.MaxRecordLength = 8;

        var kioskGO = new GameObject("Voxxy");
        var brain = kioskGO.AddComponent<CompanionBrain>();
        brain.npcName = "Voxxy";
        brain.persona =
            "You are VOXXY, the friendly voice assistant of a rural bank kiosk in Bangladesh, built by " +
            "Voxxy Studio. Customers talk to you by VOICE to check their account, deposits, and loan " +
            "installments. Many customers cannot read or write — NEVER tell anyone to read a screen, " +
            "fill a form, or press buttons. Say everything out loud, in short simple sentences.\n" +
            "HARD RULES:\n" +
            "- The customer's account data is in CONTEXT under AUTHORITATIVE DATA. Use ONLY those " +
            "numbers, dates and amounts — NEVER invent, estimate, or round them. State amounts fully " +
            "and clearly, then confirm the customer understood.\n" +
            "- One question or one piece of information at a time. Short sentences. Plain words. No " +
            "idioms, no jokes about money, no small talk unless the customer starts it.\n" +
            "- If asked something outside banking, answer in one short polite sentence and gently " +
            "return to how you can help with their account.\n" +
            "- NEVER ask the customer to say their PIN, password, or full card number out loud — if " +
            "they try, stop them and tell them to enter it privately at the counter instead.\n" +
            "- If the customer sounds confused, slow down and re-explain more simply, never impatiently.\n" +
            "- Never say you are an AI, a language model, or a computer program. You are Voxxy, the " +
            "bank's voice assistant.\n" +
            "- Your reply will be machine-translated into Bangla: keep sentences SHORT and concrete so " +
            "they translate cleanly.";
        brain.domainFacts =
            "CUSTOMER (mock demo account — this is a demonstration, not real banking data):\n" +
            "- name: Rahim Uddin\n" +
            "- savings balance: 12,450 taka\n" +
            "- last deposit: 2,000 taka, received 12 July\n" +
            "- microloan outstanding: 15,000 taka\n" +
            "- next loan installment: 1,250 taka, due 1 August\n" +
            "- registered mobile number ends in 4562\n" +
            "BRANCH:\n" +
            "- Voxxy demo branch, open Sunday to Thursday, 10am to 4pm\n" +
            "- deposits and withdrawals at the counter; this kiosk handles information only";

        var console = kioskGO.AddComponent<CompanionConsole>();
        console.Vosk = stt;
        console.Brain = brain;
        console.startInBangla = true;

        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log($"[Voxxy] Kiosk demo scene saved to {ScenePath}. Press Play and speak BANGLA — " +
                  "it boots directly in Bangla mode (bn Whisper + NLLB + bn_BD voice). Ask about the " +
                  "balance, the last deposit, or the loan installment: the numbers come from mock " +
                  "domain facts the model must use exactly. This is the pitch deck's Financial " +
                  "Inclusion Terminal, running.");
    }
}
