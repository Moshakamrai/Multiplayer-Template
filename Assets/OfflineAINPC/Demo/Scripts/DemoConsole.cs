using System.Collections.Generic;
using UnityEngine;

namespace OfflineAINPC.Demo
{
    /// <summary>
    /// Minimal IMGUI console for trying an NPC: type a line, read the reply, watch the
    /// classifier's decision.
    ///
    /// Uses IMGUI deliberately — it needs no Canvas, no prefabs, no TextMeshPro import, and no
    /// scene setup, so the demo works the instant the package lands in a project. Replace it
    /// with your own UI; the only integration points are NpcConversation.Say() and onNpcLine.
    ///
    /// The status line matters more than it looks: it tells you whether a reply came from the
    /// LLM or the keyword fallback. Without that, a working fallback is indistinguishable from
    /// a working model, and you can spend an hour wondering why your prompt edits do nothing.
    /// </summary>
    [AddComponentMenu("Offline AI NPC/Demo/Demo Console")]
    public class DemoConsole : MonoBehaviour
    {
        [Tooltip("The NPC to talk to.")]
        public NpcConversation npc;

        [Tooltip("Optional. Shows whether the local model is running, so you can tell LLM " +
                 "replies from keyword-fallback replies.")]
        public LocalLlmService llm;

        [Tooltip("Lines of conversation to keep on screen.")]
        public int visibleLines = 14;

        readonly List<string> _log = new List<string>();
        string _input = "";
        Vector2 _scroll;
        GUIStyle _wrap;
        bool _focusQueued = true;

        void OnEnable()
        {
            if (npc != null) npc.onNpcLine.AddListener(OnNpcLine);
        }

        void OnDisable()
        {
            if (npc != null) npc.onNpcLine.RemoveListener(OnNpcLine);
        }

        void OnNpcLine(string line)
        {
            string who = npc != null ? npc.npcName : "NPC";
            _log.Add($"<b>{who}:</b> {line}");
            Trim();
            _scroll.y = float.MaxValue;
        }

        void Trim()
        {
            while (_log.Count > Mathf.Max(4, visibleLines * 2)) _log.RemoveAt(0);
        }

        void Submit()
        {
            string text = _input.Trim();
            if (text.Length == 0) return;
            if (npc == null)
            {
                _log.Add("<i>No NPC assigned on DemoConsole.</i>");
                return;
            }
            if (npc.IsBusy) return;   // a turn is still resolving

            _log.Add($"<b>You:</b> {text}");
            Trim();
            _input = "";
            _scroll.y = float.MaxValue;
            npc.Say(text);
        }

        void OnGUI()
        {
            if (_wrap == null)
                _wrap = new GUIStyle(GUI.skin.label) { wordWrap = true, richText = true };

            const int pad = 12;
            float w = Mathf.Min(620, Screen.width - pad * 2);
            float h = Mathf.Min(420, Screen.height - pad * 2);

            GUILayout.BeginArea(new Rect(pad, pad, w, h), GUI.skin.box);

            // ── status ───────────────────────────────────────────────────────────────
            string status;
            if (llm == null) status = "No LocalLlmService assigned — keyword fallback.";
            else if (llm.IsReady) status = "Local LLM ready — replies use language understanding.";
            else status = "Keyword fallback: " + llm.StatusMessage;
            GUILayout.Label(status, _wrap);

            if (npc != null && npc.IsBusy) GUILayout.Label("<i>thinking...</i>", _wrap);
            else GUILayout.Label(" ", _wrap);   // reserve the row so layout does not jump

            GUILayout.Space(4);

            // ── transcript ───────────────────────────────────────────────────────────
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            for (int i = 0; i < _log.Count; i++) GUILayout.Label(_log[i], _wrap);
            GUILayout.EndScrollView();

            // ── input ────────────────────────────────────────────────────────────────
            // Enter must be handled before the TextField consumes the event, or submitting
            // with the keyboard silently does nothing.
            var e = Event.current;
            bool enter = e.type == EventType.KeyDown &&
                         (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter);

            GUILayout.BeginHorizontal();
            GUI.SetNextControlName("npcInput");
            _input = GUILayout.TextField(_input, 200);
            bool clicked = GUILayout.Button("Say", GUILayout.Width(60));
            GUILayout.EndHorizontal();

            if (enter) { Submit(); e.Use(); }
            else if (clicked) Submit();

            if (_focusQueued)
            {
                GUI.FocusControl("npcInput");
                _focusQueued = false;
            }

            GUILayout.EndArea();
        }
    }
}
