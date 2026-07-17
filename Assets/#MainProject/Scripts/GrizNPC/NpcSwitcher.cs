using System.Collections.Generic;
using UnityEngine;

// Lets ONE scene host multiple talking NPCs (Griz, Sana, ...) sharing the same heavy
// backend (one Whisper server, one llama-server, no duplicate processes) while only ONE
// NPC's console is "Active" (mic live, replying, drawing its UI) at a time.
//
// Design: each NPC entry wraps whichever console component it uses (GrizTestConsole or
// CompanionConsole — both now expose Active/Pause()/Resume()). Switching calls Pause() on
// the old one and Resume() on the new one — conversation state (history, rapport/price,
// everything) is PRESERVED while paused, so switching back mid-conversation picks up
// exactly where you left off. Nothing is destroyed or recreated.
//
// Setup: add this to any object in the scene, list your NPCs in the Inspector (or let
// Tools > Griz > Create Multi-NPC Test Scene build one for you), press Tab in Play mode
// to cycle, or click a name in the tab bar.
public class NpcSwitcher : MonoBehaviour
{
    [System.Serializable]
    public class NpcEntry
    {
        public string label = "NPC";
        [Tooltip("Assign ONE of these two — whichever console type this NPC uses.")]
        public GrizTestConsole grizConsole;
        public CompanionConsole companionConsole;

        public bool IsActive => grizConsole != null ? grizConsole.Active : companionConsole != null && companionConsole.Active;
        public void Pause() { grizConsole?.Pause(); companionConsole?.Pause(); }
        public void Resume() { grizConsole?.Resume(); companionConsole?.Resume(); }
        public bool Valid => grizConsole != null || companionConsole != null;

        public PiperVoice Piper => grizConsole != null ? grizConsole.Piper : companionConsole?.Piper;
        public GrizVoice Gibberish => grizConsole != null ? grizConsole.Voice : companionConsole?.Voice;

        // The console's own AnimLink is how it drives SetMood/facial expressions (e.g. the
        // /mood debug command, or the LLM's own sentiment tag) — in a shared-model scene
        // this MUST be pointed at sharedModel too, or those calls silently no-op forever
        // (the console's own AnimLink lookup is intentionally skipped when a switcher owns
        // the shared model — see CompanionConsole.Start).
        public void SetAnimLink(GrizAnimatorLink link)
        {
            if (grizConsole != null) grizConsole.AnimLink = link;
            if (companionConsole != null) companionConsole.AnimLink = link;
        }
    }

    public List<NpcEntry> npcs = new List<NpcEntry>();
    [Tooltip("Which entry starts active. All others are paused at Start.")]
    public int startIndex = 0;
    [Tooltip("Key that cycles to the next NPC.")]
    public KeyCode cycleKey = KeyCode.Tab;

    [Header("Shared model (optional)")]
    [Tooltip("If every NPC shares ONE physical character model (e.g. all wired through the " +
             "same Gunan_animated head), assign its GrizAnimatorLink here. On every switch, " +
             "the switcher re-points it at the NEWLY active NPC's Piper/gibberish voice — so " +
             "the one model always talks/lip-syncs for whoever is currently active, regardless " +
             "of where that model sits in the hierarchy relative to each NPC's Brain.")]
    public GrizAnimatorLink sharedModel;

    int _current = -1;

    void Start()
    {
        if (npcs.Count == 0) return;
        for (int i = 0; i < npcs.Count; i++)
            if (npcs[i].Valid) npcs[i].Pause(); // start everyone paused, then resume only one
        SwitchTo(Mathf.Clamp(startIndex, 0, npcs.Count - 1));
    }

    void Update()
    {
        if (npcs.Count < 2) return;
        if (Input.GetKeyDown(cycleKey))
            SwitchTo((_current + 1) % npcs.Count);
    }

    public void SwitchTo(int index)
    {
        if (index < 0 || index >= npcs.Count || !npcs[index].Valid || index == _current) return;
        if (_current >= 0 && _current < npcs.Count) npcs[_current].Pause();
        _current = index;
        npcs[_current].Resume();

        if (sharedModel != null)
        {
            // Re-point the ONE shared model at whichever NPC is now active — this is what
            // makes "always Gunan_animated" work regardless of hierarchy: we don't rely on
            // GrizAnimatorLink finding the right PiperVoice on its own, we just tell it.
            sharedModel.piper = npcs[_current].Piper;
            sharedModel.gibberish = npcs[_current].Gibberish;
            sharedModel.PushPiperToLipSync();
            // Also give the now-active console a reference to the shared model as ITS
            // AnimLink — otherwise SetMood() (sentiment tags, the /mood debug command)
            // has nothing to call and silently does nothing, forever, in every switcher scene.
            npcs[_current].SetAnimLink(sharedModel);
        }
    }

    void OnGUI()
    {
        if (npcs.Count < 2) return;

        float k = Screen.height / 1080f;
        int fSize = Mathf.RoundToInt(22 * k);
        var style = new GUIStyle(GUI.skin.button) { fontSize = fSize };
        float w = 220 * k, h = 44 * k;

        GUILayout.BeginArea(new Rect(Screen.width - w - 20 * k, 20 * k, w, (h + 6 * k) * npcs.Count + 30 * k));
        GUILayout.Label($"<b><size={fSize}>Talking to:</size></b>", new GUIStyle(GUI.skin.label) { richText = true, fontSize = fSize });
        for (int i = 0; i < npcs.Count; i++)
        {
            if (!npcs[i].Valid) continue;
            var prevBg = GUI.backgroundColor;
            if (i == _current) GUI.backgroundColor = new Color(0.5f, 1f, 0.5f);
            if (GUILayout.Button(npcs[i].label, style, GUILayout.Height(h)))
                SwitchTo(i);
            GUI.backgroundColor = prevBg;
        }
        GUILayout.Label($"<color=#888888><size={Mathf.RoundToInt(16 * k)}>({cycleKey} to cycle)</size></color>",
            new GUIStyle(GUI.skin.label) { richText = true });
        GUILayout.EndArea();
    }
}
