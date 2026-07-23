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
//
// Cross-suspect finger-pointing (SuspectBrain.suspicionsOfOthers, added this session):
// Pemberton's three opinions (on Constance, Higgins, Finch) are authored below. When
// Phase 2 adds the other three suspects, give each their own suspicionsOfOthers list too —
// e.g. Constance suspects Higgins (caught him snooping, wrong lead), Higgins suspects the
// Vicar (jealous/petty, accidentally nudges toward the real affair), and Finch — the killer
// — should point at Higgins's obvious financial motive as a deliberate, calm misdirect.
// These are texture/misdirection ONLY: none of them are load-bearing for the real 3-fact
// accusation chain in GDD §2.4, by design.
//
// Backdrops: already generated and copied into StreamingAssets/gnomes-intro/backdrops/ as
// constance.png, higgins.png, finch.png (pemberton.png is wired below) — Phase 2 just needs
// suspect.backdropStreamingPath = "gnomes-intro/backdrops/<name>.png" for each.
public static class GnomesSceneBuilder
{
    const string ScenePath = "Assets/Scenes/GnomesAndGaslightSolo.unity";

    [MenuItem("Tools/Gnomes & Gaslight/Create Solo Test Scene")]
    static void CreateScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        // Frame the bust as a close portrait. Default camera at z=-10 makes a head look
        // tiny; pull it in and raise it to the bust's eye line, no skybox tint bleeding in.
        var cam = Camera.main;
        if (cam != null)
        {
            cam.transform.position = new Vector3(0f, 1.5f, -2.4f);
            cam.transform.rotation = Quaternion.identity;
            cam.fieldOfView = 45f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.05f, 0.05f, 0.07f);
        }

        var voskGO = new GameObject("Vosk (free vocabulary)");
        var vp = voskGO.AddComponent<VoiceProcessor>();
        var stt = voskGO.AddComponent<VoskSpeechToText>();
        stt.VoiceProcessor = vp;
        // NOT auto-start: the mic must stay closed through the intro narration and the
        // dossier card, or the game "hears" the player (and the narration itself) before
        // the interview has even begun. InterrogationConsole.BeginInterview() opens it.
        stt.AutoStart = false;
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
        pemberton.role = "The Neighbour";
        pemberton.bio =
            "The Ravenscrofts' next-door neighbour of many years — a chatty, nosy widow who prides " +
            "herself on knowing everyone's business and misses very little from her garden window. " +
            "Harmless enough on the surface, and she does love an audience.";
        pemberton.whySuspect =
            "She was seen on the manor grounds late on the night Reginald died — well after dark, with " +
            "no obvious reason to be there. She's been evasive about exactly why. Whatever she was up to, " +
            "she may also have seen who else was moving about that night.\n" +
            "Try: ask where she was and what she was doing — and get her gossiping about the household.";
        pemberton.persona =
            "You are MRS. PEMBERTON, the Ravenscroft family's neighbor. You are chatty, nosy, easily " +
            "flattered, and genuinely terrified — but of a much smaller crime than murder. You love to " +
            "gossip and will happily talk about OTHER people's business at length.\n" +
            "HOW YOU ANSWER: you ANSWER the question you're asked — you're a talker who loves an audience, " +
            "so you give real, concrete, colourful answers about the Ravenscroft household, the night of " +
            "the dinner, the neighbours, whatever. You might add a little gossipy aside, but you always " +
            "actually address what was asked. Only ONE thing stays guarded (your real secret, below) — " +
            "everything else you'll happily talk about honestly.\n" +
            "UNDER DIRECT/BLUNT PRESSURE about your REAL SECRET specifically: you get flustered and steer " +
            "away — but you still answer OTHER questions normally.\n" +
            "UNDER GOSSIP/FLATTERY: this is your weak point. Treated warmly or asked about OTHER people's " +
            "business, you relax and volunteer more — about the grounds, the dinner, eventually your own " +
            "guilt.\n" +
            "YOUR FALSE GIVE (offer this readily under enough pressure, NOT your real secret): you don't " +
            "much like Lady Constance and have spread a rumor or two about the state of her marriage. Real, " +
            "a little juicy, NOT murder.\n" +
            "HARD RULES: never say you are an AI or a language model. Don't invent people, places, or facts " +
            "that aren't in your context or the conversation. No stage directions in asterisks beyond a " +
            "short parenthetical. Keep replies to 1-3 sentences, spoken aloud, answering what was asked.";
        pemberton.openingLine =
            "Oh! An investigator, in my little shed — how thrilling. Terrible business of course, " +
            "poor Reginald, simply terrible. Tea? No? Well. Ask away, dear — I see everything from " +
            "this window, you know.";
        pemberton.backdropStreamingPath = "gnomes-intro/backdrops/pemberton.png";
        // Voice: en_GB-jenny_dioco-medium (warm higher female — fits a nosy neighbor).
        // Download both .onnx + .onnx.json from huggingface rhasspy/piper-voices into
        // StreamingAssets/piper. Until then PiperVoice falls back to an English MALE voice
        // (no longer the Bangla model — see the same-language-family fallback fix).
        // Phase 2 voices: Constance -> "alba" (softer female), Higgins ->
        // "northern_english_male" (already installed), Finch -> "alan" (already installed).
        pemberton.voiceModelContains = "jenny";
        pemberton.voicePitch = 1.06f;      // bright, chatty (jenny is already female, so less pitch-up needed)
        pemberton.voiceLengthScale = 0.98f; // slightly quick — she talks like a gossip
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
        pemberton.falseGiveCardText = "Admits spreading rumors that the Ravenscroft marriage was loveless.";
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
        pemberton.secretCardText = "Gnome thief confessed — AND saw Finch near the study after dinner ended.";
        pemberton.brokenStateDirection =
            "Relief and laughter — you're no longer performing, you're rambling, half-laughing at " +
            "yourself for how small your real secret turns out to be.";
        // Per-encounter task checklist: small, concrete, player-facing goals — ticks off the
        // SUSPECT'S OWN reply, not the player's question, so it only completes when she
        // actually answers. Keywords matched against her generated lines; tune if she
        // phrases things in ways that miss these (same iteration loop as the pressure classifier).
        // Loose, thematic goals — direction, NOT a script of exact questions. The player
        // decides HOW to get there (any wording, any angle); an objective ticks when her
        // OWN answer touches the topic. Kept broad so interrogation feels open, not like
        // filling in a form. Only the first 2-3 unfinished show at once (CaseBoardUI).
        pemberton.objectives = new List<SuspectBrain.Objective>
        {
            new SuspectBrain.Objective
            {
                label = "Pin down where she was that night",
                keywords = new List<string> { "home", "shed", "window", "garden", "grounds", "reading", "tea", "book", "resting", "watching" }
            },
            new SuspectBrain.Objective
            {
                label = "Get her talking about the household",
                keywords = new List<string> { "constance", "widow", "marriage", "higgins", "butler", "finch", "doctor", "reginald", "neighbor", "gnome" }
            },
            new SuspectBrain.Objective
            {
                label = "Find what she's hiding",
                source = SuspectBrain.Objective.Source.Broken
            },
        };
        // Cross-suspect finger-pointing (this session's addition): a genuine opinion, only
        // ever volunteered if the player directly asks her about someone else. She's
        // self-absorbed about her OWN secret, so she doesn't lead with a theory unprompted
        // — but if asked, she has one, and it's real gossip, not evidence.
        pemberton.suspicionsOfOthers = new List<SuspectBrain.Suspicion>
        {
            new SuspectBrain.Suspicion
            {
                aboutWhom = "Constance",
                belief = "She thinks Lady Constance's grief is 'a touch theatrical' and privately suspects " +
                          "the marriage was unhappy — she has no real evidence, just neighborly gossip and " +
                          "a good eye for performance.",
                beliefCard = "Thinks the widow's grief looks theatrical. (No evidence.)"
            },
            new SuspectBrain.Suspicion
            {
                aboutWhom = "Higgins",
                belief = "She finds the butler 'oddly twitchy lately' and jokes that 'the quiet ones always " +
                         "have a ledger of their own' — pure hunch, she has no idea about his gambling debt.",
                beliefCard = "Finds the butler 'oddly twitchy lately'. (Pure hunch.)"
            },
            new SuspectBrain.Suspicion
            {
                aboutWhom = "Finch",
                belief = "She genuinely likes Dr. Finch and initially waves off any suspicion of him as " +
                         "'the one decent man in that house' — until/unless her own secret breaks, at " +
                         "which point her actual sighting of him near the study becomes available instead.",
                beliefCard = "Defends Dr. Finch: 'the one decent man in that house'."
            },
        };

        var interviewGO = new GameObject("PembertonInterview");
        var console = interviewGO.AddComponent<InterrogationConsole>();
        console.Vosk = stt;
        console.Brain = pemberton;
        console.Board = board;

        // ── Bust: reuse the Gunan_animated blendshape model/prefab from the GrizNPC scenes.
        // The prefab already carries its OWN GrizAnimatorLink (with autoLipSync/
        // autoFacialExpressions on) — reuse that instance rather than adding a second one,
        // which would fight the first over the same blendshapes. No shop/sword logic here
        // (useAnimatorStates = false, same as Sana's CompanionConsole) — she just sits,
        // blinks, lipsyncs, and emotes through an interview.
        var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/#MainProject/Prefabs/Gunan_animated.prefab");
        if (modelAsset == null) // fall back to the raw FBX if the prefab ever moves/is deleted
            modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/#MainProject/Models/BlendshapeModels/source/Gunan_animated.fbx");
        if (modelAsset != null)
        {
            var model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
            model.name = "Pemberton (bust)";
            model.transform.SetParent(pembertonGO.transform, false);
            // Framed by the portrait camera set above (cam at (0,1.5,-2.4), 45° FOV).
            model.transform.position = new Vector3(0f, 0f, 0f);

            var animator = model.GetComponentInChildren<Animator>();
            if (animator == null) animator = model.AddComponent<Animator>();

            var animLink = model.GetComponentInChildren<GrizAnimatorLink>();
            if (animLink == null) animLink = model.AddComponent<GrizAnimatorLink>();
            animLink.animator = animator;
            animLink.useAnimatorStates = false; // interrogation suspect: no shop/sword states
            console.AnimLink = animLink; // console.Start() finishes the wiring once Voice exists

            // World-space backdrop quad, positioned BEHIND the bust so the camera's normal
            // depth sort puts the 3D character in front of it (an IMGUI OnGUI() overlay draws
            // over the ENTIRE screen including the 3D render, which hides the bust). The
            // TEXTURE is loaded at RUNTIME by BackdropQuad, NOT baked here: a Texture2D made
            // in the editor via LoadImage is non-serialized and vanishes when the scene
            // reloads on Play, which is what made the backdrop keep disappearing.
            if (!string.IsNullOrEmpty(pemberton.backdropStreamingPath))
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = "Pemberton Backdrop";
                Object.DestroyImmediate(quad.GetComponent<Collider>());
                quad.transform.position = new Vector3(0f, 1.5f, 6f); // centered on the camera's eye line
                // A Quad's front face normal is +Z by default — facing AWAY from a camera at
                // z=-10 looking toward +Z, so the camera sees only the back face (culled by
                // URP Unlit). Rotate 180° on Y so the textured front faces the camera.
                quad.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
                quad.transform.localScale = new Vector3(18f, 10f, 1f); // BackdropQuad fixes the aspect at runtime; oversized so no sky peeks past its edges
                // Material + texture are fully (re)built at runtime by BackdropQuad using
                // Sprites/Default — no build-time material to serialize/lose.
                var bq = quad.AddComponent<BackdropQuad>();
                bq.streamingPath = pemberton.backdropStreamingPath;
            }
        }
        else
        {
            Debug.LogWarning("[Gnomes & Gaslight] Gunan_animated prefab/FBX not found at the expected " +
                              "path — scene built without a bust. Assign one manually or fix the path in GnomesSceneBuilder.");
        }

        // ── Cold open: narrated photographs before the first interview (the highest-
        // leverage polish pass — pure authored pacing, zero LLM variance). Content lives in
        // StreamingAssets/gnomes-intro/ (photos + narration.mp3/.wav/.ogg + narration.txt) —
        // works today with just the placeholder narration.txt (silent captioned slideshow)
        // and upgrades to real photography/voiceover later with no code changes.
        var introGO = new GameObject("IntroSequence");
        var intro = introGO.AddComponent<IntroSequence>();

        var runnerGO = new GameObject("CaseRunner");
        var runner = runnerGO.AddComponent<CaseRunner>();
        runner.board = board;
        runner.suspects = new List<SuspectBrain> { pemberton };
        runner.totalRounds = 4;
        runner.autoStartSolo = true;
        runner.soloSuspect = pemberton;
        runner.soloConsole = console;
        runner.intro = intro;
        runner.caseMusicFileName = "case-music.mp3"; // starts once the intro finishes and the case begins
        console.Runner = runner;

        var uiGO = new GameObject("CaseBoardUI");
        var ui = uiGO.AddComponent<CaseBoardUI>();
        ui.Board = board;
        ui.Runner = runner;
        ui.ActiveConsole = console;

        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log($"[Gnomes & Gaslight] Solo test scene saved to {ScenePath}. Press Play — the narrated " +
                  "cold open runs first (click/space to skip), then the case auto-starts into Pemberton's " +
                  "interview. Push BLUNT questions first to feel the 'this isn't working' cost, then try " +
                  "warm/gossipy flattery — that's her real axis. Watch the side panel for her false give " +
                  "(gold) and eventual broken-state reveal (red). Drop real photos + narration.mp3 into " +
                  "StreamingAssets/gnomes-intro/ whenever ready — no code changes needed.");
    }
}
