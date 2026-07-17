using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Tools > Gnomes & Gaslight > Create Solo Test Scene (Pemberton)
//
// Phase 1 vertical slice per GNOMES_AND_GASLIGHT_GDD.md §6: ONE suspect (Mrs. Pemberton —
// "the gnome secret is the lowest-stakes test case"), the persona/secret/false-give/broken-
// state authored straight from GDD §3.3, wired to a placeholder-model-safe pipeline (no
// bust/backdrop required yet — those arrive later per the doc's own note that art can lag
// code, same as Griz ran on a placeholder for a long time).
//
// This proves the full loop end to end: enter interview, push the wrong axis (costs time,
// not fun), find her real weakness (gossip/flattery), get the false give, then the broken
// state, watch it land on the board with auto-flagged contradictions against other cards.
public static class GnomesSceneBuilder
{
    const string ScenePath = "Assets/Scenes/GnomesAndGaslightSolo.unity";

    [MenuItem("Tools/Gnomes & Gaslight/Create Solo Test Scene")]
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

        // ── Case board: deterministic truth + fixed forensics drip (§2, build-order's
        // "fixed by round" decision) ──
        var boardGO = new GameObject("CaseBoard");
        var board = boardGO.AddComponent<CaseBoard>();
        board.evidenceDripOrder = new List<CaseBoard.EvidenceItem>
        {
            new CaseBoard.EvidenceItem
            {
                id = "betting-slip", label = "Torn betting slip",
                description = "Found wedged behind a desk drawer — a local bookmaker's chit, half-burned at one corner."
            },
            new CaseBoard.EvidenceItem
            {
                id = "will-draft-fee", label = "Disturbed will draft",
                description = "The study's will draft, refiled in the wrong drawer — it names a 'medical consultant fee arrangement' being cut."
            },
            new CaseBoard.EvidenceItem
            {
                id = "tonic-bottle", label = "Reginald's tonic bottle",
                description = "The nightly heart tonic bottle from the study — residue suggests a stronger dose than usual was prepared."
            },
            new CaseBoard.EvidenceItem
            {
                id = "repainted-gnome", label = "A garishly repainted garden gnome",
                description = "One of Reginald's prize gnomes, freshly repainted in lurid colors and left facing the east wing path."
            },
        };

        // ── Mrs. Pemberton — authored directly from GDD §3.3 ──
        var pembertonGO = new GameObject("Pemberton");
        var pemberton = pembertonGO.AddComponent<SuspectBrain>();
        pemberton.suspectName = "Mrs. Pemberton";
        pemberton.persona =
            "You are MRS. PEMBERTON, the Ravenscroft family's neighbor. You are chatty, nosy, easily " +
            "flattered, and genuinely terrified — but of a much smaller crime than murder. You love to " +
            "gossip and will happily talk about OTHER people's business at length.\n" +
            "UNDER DIRECT/BLUNT PRESSURE: you get MORE evasive and talk in circles — you're used to " +
            "gossiping your way out of scrutiny, so a blunt accusation just makes you perform innocence " +
            "louder and change the subject.\n" +
            "UNDER GOSSIP/FLATTERY: this is your real weak point. If treated like gossip between " +
            "neighbors — asked about OTHER people's business first, complimented, or spoken to warmly " +
            "— you relax and volunteer things about the grounds, the night of the dinner, and " +
            "eventually your own guilt, without ever feeling interrogated.\n" +
            "YOUR FALSE GIVE (offer this readily under generic/blunt pressure, NOT your real secret): " +
            "you don't much like Lady Constance and have spread a rumor or two about the state of her " +
            "marriage. This is real, a little juicy, and NOT murder — say it like you're getting away " +
            "with something petty.\n" +
            "HARD RULES: never say you are an AI or a language model. No stage directions in asterisks " +
            "beyond a short parenthetical if truly needed. Keep replies to 1-3 sentences, spoken aloud, " +
            "in character, reacting to exactly what was just said.";
        pemberton.weakness = SuspectBrain.PressureAxis.Flattery;
        pemberton.wrongAxisCost = 4f;
        pemberton.bluntCost = 7f; // blunt pressure actively backfires on her per the GDD
        pemberton.rightAxisGain = 20f;
        pemberton.falseGiveAtPatience = 65f;
        pemberton.falseGiveHint =
            "She dislikes Lady Constance and has spread rumors about the marriage — a real but petty, " +
            "non-murder confession she'll offer under enough generic pressure.";
        pemberton.falseGiveContent =
            "Mrs. Pemberton admits she's spread rumors that Lady Constance's marriage to Reginald was " +
            "loveless and that she 'wouldn't be surprised if the widow found comfort elsewhere.'";
        pemberton.breaksAtPatience = 20f;
        pemberton.secretHint =
            "She has been sneaking onto the Ravenscroft grounds at night for weeks, stealing one of " +
            "Reginald's prize garden gnomes at a time and repainting it grotesquely before returning it " +
            "— a slow prank war over his gnome collection. She is mortified anyone might find out and " +
            "will deflect hard if directly asked about the grounds, the gnomes, or being out at night.";
        pemberton.secretContent =
            "Mrs. Pemberton confesses to the gnome prank war — AND, almost as an aside now that she's " +
            "relieved to have that off her chest, mentions she saw Dr. Finch near the study, after the " +
            "dinner had already ended, earlier than he claims in his own account of that night.";
        pemberton.brokenStateDirection =
            "Relief and laughter — you're no longer performing, you're rambling, half-laughing at " +
            "yourself for how small your real secret turns out to be.";

        var interviewGO = new GameObject("PembertonInterview");
        var console = interviewGO.AddComponent<InterrogationConsole>();
        console.Vosk = stt;
        console.Brain = pemberton;
        console.Board = board;

        var runnerGO = new GameObject("CaseRunner");
        var runner = runnerGO.AddComponent<CaseRunner>();
        runner.board = board;
        runner.suspects = new List<SuspectBrain> { pemberton };
        runner.totalRounds = 4;
        runner.autoStartSolo = true;
        runner.soloSuspect = pemberton;
        runner.soloConsole = console;
        console.Runner = runner;

        var uiGO = new GameObject("CaseBoardUI");
        var ui = uiGO.AddComponent<CaseBoardUI>();
        ui.Board = board;
        ui.Runner = runner;
        ui.ActiveConsole = console;

        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log($"[Gnomes & Gaslight] Solo test scene saved to {ScenePath}. Press Play — the case " +
                  "auto-starts (call runner.BeginCase() + runner.BeginInterrogation(pemberton) from a " +
                  "quick debug hook, or wire a start button). Push BLUNT questions first to feel the " +
                  "'this isn't working' cost, then try warm/gossipy flattery — that's her real axis. " +
                  "Watch the side panel for her false give (gold) and eventual broken-state reveal (red).");
    }
}
