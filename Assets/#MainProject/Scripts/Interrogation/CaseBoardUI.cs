using System.Collections.Generic;
using UnityEngine;

// GNOMES & GASLIGHT — the persistent side panel: statement cards slide in live during
// interrogation, contradictions are auto-flagged in red (players still have to notice WHY
// and connect them — the board never explains the case, only that two things don't match),
// an evidence tray lets a player "hold" a discovered item to present it mid-interview, and
// the top HUD shows phase/round/patience so nothing needs to be remembered by hand.
public class CaseBoardUI : MonoBehaviour
{
    public CaseBoard Board;
    public CaseRunner Runner;
    public InterrogationConsole ActiveConsole;

    [Header("Layout")]
    [Range(0.2f, 0.5f)] public float panelWidthFraction = 0.3f;
    public bool collapsed = false;

    Vector2 _cardScroll, _evidenceScroll, _transcriptScroll;
    string _lastToast = "";
    float _toastUntil;
    string _typed = "";
    readonly List<(string who, string text)> _transcript = new List<(string, string)>();

    const string BG = "#12121a";
    const string PURPLE = "#a855f7";
    const string RED = "#f87171";
    const string GREEN = "#4ade80";
    const string GOLD = "#facc15";

    void OnEnable()
    {
        if (Board != null)
        {
            Board.OnEvidenceDiscovered += ev => Toast($"NEW EVIDENCE: {ev.label}");
            Board.OnContradictionFound += c => Toast("CONTRADICTION FOUND — check the board");
            Board.OnSuspicionLoopFound += l => Toast($"SUSPICION LOOP: {l.a.suspectName} ⇄ {l.b.suspectName} — check the board");
        }
        if (ActiveConsole != null)
        {
            ActiveConsole.OnLine += (who, text) => _transcript.Add((who, text));
            ActiveConsole.OnPressureRead += (matched, kind) =>
            {
                _pressureFlash = matched
                    ? $"🔓 that landed — she's opening up ({kind})"
                    : $"🔒 not working — try a different approach (that was read as {kind})";
                _pressureFlashUntil = Time.time + 3.5f;
            };
        }
    }

    string _pressureFlash = "";
    float _pressureFlashUntil;

    void Toast(string msg)
    {
        _lastToast = msg;
        _toastUntil = Time.time + 4f;
    }

    void OnGUI()
    {
        if (Runner == null || Board == null) return;
        float k = Mathf.Max(1f, Screen.height / 900f);
        int f = Mathf.RoundToInt(15 * k);
        var rich = new GUIStyle(GUI.skin.label) { fontSize = f, richText = true, wordWrap = true };

        DrawTopHud(k, f, rich);
        DrawMainArea(k, f, rich);

        if (collapsed)
        {
            if (GUI.Button(new Rect(Screen.width - 140 * k, 60 * k, 120 * k, 36 * k), "Show board ▶"))
                collapsed = false;
            return;
        }

        float w = Screen.width * panelWidthFraction;
        var panelRect = new Rect(Screen.width - w, 60 * k, w, Screen.height - 70 * k);
        GUI.color = new Color(0.07f, 0.07f, 0.10f, 0.92f);
        GUI.DrawTexture(panelRect, Texture2D.whiteTexture);
        GUI.color = Color.white;

        GUILayout.BeginArea(panelRect);
        GUILayout.BeginHorizontal();
        GUILayout.Label($"<color={PURPLE}><b>CASE BOARD</b></color>", rich);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("◀ hide", GUILayout.Width(70 * k))) collapsed = true;
        GUILayout.EndHorizontal();

        DrawEvidenceTray(k, f, rich);
        GUILayout.Space(8 * k);
        DrawContradictions(k, f, rich);
        GUILayout.Space(8 * k);
        DrawSuspicionLoops(k, f, rich);
        GUILayout.Space(8 * k);
        DrawCards(k, f, rich);
        GUILayout.EndArea();
    }

    void DrawMainArea(float k, int f, GUIStyle rich)
    {
        float panelW = collapsed ? 0f : Screen.width * panelWidthFraction;
        var area = new Rect(10 * k, 60 * k, Screen.width - panelW - 20 * k, Screen.height - 130 * k);
        GUILayout.BeginArea(area);

        _transcriptScroll = GUILayout.BeginScrollView(_transcriptScroll, GUILayout.ExpandHeight(true));
        foreach (var (who, text) in _transcript)
        {
            string color = who == "YOU" ? "#99ccff" : "#ffb366";
            GUILayout.Label($"<color={color}>[{who}]</color> {text}", rich);
            GUILayout.Space(4 * k);
        }
        GUILayout.EndScrollView();

        if (Runner.CurrentPhase == CaseRunner.Phase.Interrogation && ActiveConsole != null)
        {
            if (Time.time < _pressureFlashUntil)
            {
                string flashColor = _pressureFlash.StartsWith("🔓") ? GREEN : "#facc15";
                GUILayout.Label($"<color={flashColor}>{_pressureFlash}</color>", rich);
            }
            GUILayout.BeginHorizontal();
            GUI.SetNextControlName("interrogationInput");
            _typed = GUILayout.TextField(_typed, GUILayout.Height(36 * k));
            bool submit = GUILayout.Button("Ask / Say", GUILayout.Width(110 * k), GUILayout.Height(36 * k)) ||
                          (Event.current.type == EventType.KeyUp && Event.current.keyCode == KeyCode.Return &&
                           GUI.GetNameOfFocusedControl() == "interrogationInput");
            GUILayout.EndHorizontal();
            if (submit && !string.IsNullOrWhiteSpace(_typed))
            {
                ActiveConsole.SendTyped(_typed);
                _typed = "";
                GUI.FocusControl("interrogationInput");
            }
        }
        else if (Runner.CurrentPhase == CaseRunner.Phase.Huddle)
        {
            GUILayout.Label($"<color={PURPLE}>Compare notes, then continue when ready.</color>", rich);
            if (GUILayout.Button("End huddle → next round", GUILayout.Height(40 * k)))
                Runner.EndHuddle();
        }
        else if (Runner.CurrentPhase == CaseRunner.Phase.Ended)
        {
            GUILayout.Label($"<color={RED}>Case complete — build your accusation from the board.</color>", rich);
        }

        GUILayout.EndArea();
    }

    void DrawTopHud(float k, int f, GUIStyle rich)
    {
        var r = new Rect(10 * k, 10 * k, Screen.width - 20 * k, 44 * k);
        GUI.color = new Color(0, 0, 0, 0.55f);
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = Color.white;

        string phase = Runner.CurrentPhase switch
        {
            CaseRunner.Phase.Assignment => $"<color={GOLD}>ASSIGNMENT</color> — choose who to interrogate",
            CaseRunner.Phase.Interrogation => $"<color={GREEN}>INTERROGATION</color> — {Mathf.Max(0f, Runner.PhaseTimeRemaining):0}s left" +
                (Runner.ActiveSuspect != null ? $"  ·  {Runner.ActiveSuspect.suspectName}  (patience {Runner.ActiveSuspect.patience:0})" : ""),
            CaseRunner.Phase.Huddle => $"<color={PURPLE}>HUDDLE</color> — compare notes (suspects can't hear this)",
            CaseRunner.Phase.Ended => $"<color={RED}>CASE READY FOR ACCUSATION</color>",
            _ => ""
        };
        GUILayout.BeginArea(r);
        GUILayout.BeginHorizontal();
        GUILayout.Label($"round <b>{Mathf.Min(Runner.RoundIndex + 1, Runner.totalRounds)}/{Runner.totalRounds}</b>   {phase}", rich);
        GUILayout.FlexibleSpace();
        if (Time.time < _toastUntil)
            GUILayout.Label($"<color={GOLD}>{_lastToast}</color>", rich);
        GUILayout.EndHorizontal();
        GUILayout.EndArea();
    }

    void DrawEvidenceTray(float k, int f, GUIStyle rich)
    {
        GUILayout.Label($"<color={GOLD}>EVIDENCE</color>", rich);
        _evidenceScroll = GUILayout.BeginScrollView(_evidenceScroll, GUILayout.Height(90 * k));
        foreach (var e in Board.AllEvidence)
        {
            if (!e.discovered) continue;
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<color={GOLD}>◆</color> <b>{e.label}</b> — {e.description}", rich);
            if (ActiveConsole != null && Runner.CurrentPhase == CaseRunner.Phase.Interrogation &&
                GUILayout.Button("Present", GUILayout.Width(80 * k)))
                ActiveConsole.presentedEvidenceId = e.id;
            GUILayout.EndHorizontal();
        }
        GUILayout.EndScrollView();
    }

    void DrawContradictions(float k, int f, GUIStyle rich)
    {
        if (Board.Contradictions.Count == 0) return;
        GUILayout.Label($"<color={RED}>⚠ CONTRADICTIONS</color>", rich);
        foreach (var c in Board.Contradictions)
            GUILayout.Label($"<color={RED}>• {c.a.suspectName} vs {c.b.suspectName}:</color> {c.reason}", rich);
    }

    const string CYAN = "#06b6d4";

    void DrawSuspicionLoops(float k, int f, GUIStyle rich)
    {
        if (Board.SuspicionLoops.Count == 0) return;
        GUILayout.Label($"<color={CYAN}>⇄ SUSPICION LOOPS (opinions, may both be wrong)</color>", rich);
        foreach (var l in Board.SuspicionLoops)
            GUILayout.Label($"<color={CYAN}>• {l.a.suspectName} and {l.b.suspectName} accuse EACH OTHER</color>", rich);
    }

    void DrawCards(float k, int f, GUIStyle rich)
    {
        GUILayout.Label($"<color={PURPLE}>STATEMENTS</color>", rich);
        _cardScroll = GUILayout.BeginScrollView(_cardScroll, GUILayout.ExpandHeight(true));
        foreach (var c in Board.Cards)
        {
            string color = c.isBrokenReveal ? RED : c.isFalseGive ? GOLD :
                           c.tag == "accusation" ? CYAN : "#cccccc";
            string tag = c.isBrokenReveal ? " [BROKEN]" : c.isFalseGive ? " [false give]" :
                        c.tag == "accusation" ? " [opinion]" : "";
            GUILayout.Label($"<color={color}><b>{c.suspectName}</b> (r{c.round + 1}){tag}: {c.text}</color>", rich);
            GUILayout.Space(4 * k);
        }
        GUILayout.EndScrollView();
    }
}
