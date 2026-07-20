using System.Collections.Generic;
using UnityEngine;

// GNOMES & GASLIGHT — the interrogation HUD, laid out as three floating panels over the
// 3D bust + room backdrop rather than one big side slab:
//   • bottom-LEFT  : the suspect's speech, in a rounded dialogue bubble (the main event)
//   • top-RIGHT    : what YOU just said + the live "listening…" mic transcript
//   • bottom-RIGHT : the case board (objectives / evidence / statements — reference only)
// A slim top bar keeps round/phase/patience. Objectives show at most 3 UNFINISHED at a
// time and refill as they tick, so the list always reads as "here's what to do next".
public class CaseBoardUI : MonoBehaviour
{
    public CaseBoard Board;
    public CaseRunner Runner;
    public InterrogationConsole ActiveConsole;

    [Header("Optional custom font (a .ttf/.otf dropped in Assets and assigned here). " +
            "Leave null to use Unity's built-in font at the styled sizes below.")]
    public Font uiFont;

    [Header("Panel look")]
    [Range(0f, 1f)] public float panelOpacity = 0.72f;
    [Range(0.22f, 0.42f)] public float boardWidthFraction = 0.3f;

    Vector2 _cardScroll;
    string _lastToast = "";
    float _toastUntil;
    readonly List<(string who, string text)> _transcript = new List<(string, string)>();
    string _lastSuspectLine = "";
    string _pressureFlash = "";
    float _pressureFlashUntil;

    const string PURPLE = "#c9a2ff";
    const string RED = "#ff8a8a";
    const string GREEN = "#8ff0a4";
    const string GOLD = "#ffd968";
    const string CYAN = "#5fd7e8";
    const string SUSPECT = "#ffcf9e";
    const string PLAYER = "#a9d4ff";
    const string DIM = "#d0d0d8";

    Texture2D _panelTex;
    GUIStyle _body, _bodyDim, _header, _bubble, _hudStyle;

    void OnEnable()
    {
        if (Board != null)
        {
            Board.OnEvidenceDiscovered += ev => Toast($"NEW EVIDENCE: {ev.label}");
            Board.OnContradictionFound += c => Toast("CONTRADICTION FOUND — check the board");
            Board.OnSuspicionLoopFound += l => Toast($"SUSPICION LOOP: {l.a.suspectName} ⇄ {l.b.suspectName}");
        }
        if (ActiveConsole != null)
        {
            ActiveConsole.OnLine += OnLine;
            ActiveConsole.OnPressureRead += (matched, kind) =>
            {
                _pressureFlash = matched
                    ? "✦ that landed — she's opening up"
                    : "✕ not working — try a different approach";
                _pressureFlashUntil = Time.time + 3.5f;
            };
            if (ActiveConsole.Brain != null)
                ActiveConsole.Brain.OnObjectiveComplete += o => Toast($"✓ {o.label}");
        }
    }

    void OnLine(string who, string text)
    {
        _transcript.Add((who, text));
        if (who != "YOU") _lastSuspectLine = text;
    }

    void Toast(string msg) { _lastToast = msg; _toastUntil = Time.time + 4f; }

    // Solid rounded-ish panel background (a flat tinted texture; GUI has no real rounded
    // rects, so a subtle dark fill + the styled text does the heavy lifting).
    Texture2D PanelTex()
    {
        if (_panelTex != null) return _panelTex;
        _panelTex = new Texture2D(1, 1);
        _panelTex.SetPixel(0, 0, new Color(0.055f, 0.05f, 0.075f, 1f));
        _panelTex.Apply();
        return _panelTex;
    }

    void BuildStyles(float k)
    {
        if (_body != null && (uiFont == null || _body.font == uiFont)) return;
        GUIStyle Base(int size, FontStyle fs) => new GUIStyle
        {
            font = uiFont,
            fontSize = Mathf.RoundToInt(size * k),
            fontStyle = fs,
            richText = true,
            wordWrap = true,
            normal = { textColor = Color.white },
            padding = new RectOffset(2, 2, 2, 2)
        };
        _body = Base(20, FontStyle.Normal);
        _bodyDim = Base(20, FontStyle.Normal);
        _header = Base(17, FontStyle.Bold);
        _bubble = Base(24, FontStyle.Normal);
        _bubble.padding = new RectOffset((int)(18 * k), (int)(18 * k), (int)(14 * k), (int)(14 * k));
        _hudStyle = Base(19, FontStyle.Bold);
    }

    void Panel(Rect r)
    {
        var prev = GUI.color;
        GUI.color = new Color(1, 1, 1, panelOpacity);
        GUI.DrawTexture(r, PanelTex());
        GUI.color = prev;
    }

    void OnGUI()
    {
        if (Runner == null || Board == null || !Runner.IsCaseStarted) return;
        float k = Mathf.Max(1f, Screen.height / 900f);
        BuildStyles(k);

        DrawTopBar(k);
        DrawSuspectBubble(k);   // bottom-left
        DrawPlayerPanel(k);     // top-right
        DrawBoardPanel(k);      // bottom-right
    }

    // ── top bar: round / phase / patience / toast ──
    void DrawTopBar(float k)
    {
        var r = new Rect(0, 0, Screen.width, 40 * k);
        Panel(r);
        string phase = Runner.CurrentPhase switch
        {
            CaseRunner.Phase.Assignment => $"<color={GOLD}>ASSIGNMENT</color>",
            CaseRunner.Phase.Interrogation => $"<color={GREEN}>INTERROGATION</color>  ·  {Mathf.Max(0f, Runner.PhaseTimeRemaining):0}s" +
                (Runner.ActiveSuspect != null ? $"  ·  <b>{Runner.ActiveSuspect.suspectName}</b>  ·  patience {Runner.ActiveSuspect.patience:0}" : ""),
            CaseRunner.Phase.Huddle => $"<color={PURPLE}>HUDDLE</color>",
            CaseRunner.Phase.Ended => $"<color={RED}>CASE READY FOR ACCUSATION</color>",
            _ => ""
        };
        GUI.Label(new Rect(16 * k, 0, Screen.width * 0.6f, 40 * k),
            $"<color={DIM}>Round {Mathf.Min(Runner.RoundIndex + 1, Runner.totalRounds)}/{Runner.totalRounds}</color>   {phase}", _hudStyle);
        if (Time.time < _toastUntil)
        {
            var ts = new GUIStyle(_hudStyle) { alignment = TextAnchor.MiddleRight };
            GUI.Label(new Rect(Screen.width * 0.4f - 16 * k, 0, Screen.width * 0.6f, 40 * k), $"<color={GOLD}>{_lastToast}</color>", ts);
        }
    }

    // ── bottom-left: the suspect's current line, in a dialogue bubble ──
    void DrawSuspectBubble(float k)
    {
        if (string.IsNullOrEmpty(_lastSuspectLine)) return;
        float w = Mathf.Min(Screen.width * 0.44f, 640 * k);
        float h = Mathf.Min(Screen.height * 0.32f, 260 * k);
        var r = new Rect(24 * k, Screen.height - h - 24 * k, w, h);
        Panel(r);
        string name = Runner.ActiveSuspect != null ? Runner.ActiveSuspect.suspectName : "Suspect";
        GUI.Label(new Rect(r.x + 18 * k, r.y + 10 * k, r.width - 36 * k, 28 * k), $"<color={SUSPECT}><b>{name}</b></color>", _header);
        GUI.Label(new Rect(r.x, r.y + 34 * k, r.width, r.height - 40 * k), $"<color=#ffffff>“{_lastSuspectLine}”</color>", _bubble);
    }

    // ── top-right: what you last said + the live mic transcript ──
    void DrawPlayerPanel(float k)
    {
        float w = Mathf.Min(Screen.width * 0.32f, 460 * k);
        var r = new Rect(Screen.width - w - 20 * k, 52 * k, w, 128 * k);
        Panel(r);
        GUILayout.BeginArea(new Rect(r.x + 14 * k, r.y + 10 * k, r.width - 28 * k, r.height - 20 * k));

        string lastYou = "";
        for (int i = _transcript.Count - 1; i >= 0; i--)
            if (_transcript[i].who == "YOU") { lastYou = _transcript[i].text; break; }
        GUILayout.Label(string.IsNullOrEmpty(lastYou)
            ? $"<color={DIM}>You haven't spoken yet.</color>"
            : $"<color={PLAYER}><b>You:</b></color> {lastYou}", _body);

        GUILayout.FlexibleSpace();
        if (Runner.CurrentPhase == CaseRunner.Phase.Interrogation && ActiveConsole != null)
        {
            if (Time.time < _pressureFlashUntil)
                GUILayout.Label($"<color={(_pressureFlash.StartsWith("✦") ? GREEN : GOLD)}>{_pressureFlash}</color>", _body);
            string hearing = ActiveConsole.LiveHearingText;
            GUILayout.Label(string.IsNullOrEmpty(hearing)
                ? $"<color={GREEN}>🎤 listening…</color>"
                : $"<color={GREEN}>🎤</color> <i>{hearing}</i>", _body);
        }
        else if (Runner.CurrentPhase == CaseRunner.Phase.Huddle)
        {
            if (GUILayout.Button("End huddle → next round", GUILayout.Height(38 * k))) Runner.EndHuddle();
        }
        GUILayout.EndArea();
    }

    // ── bottom-right: objectives (max 3 shown, refilling) + evidence + statements ──
    void DrawBoardPanel(float k)
    {
        float w = Screen.width * boardWidthFraction;
        float h = Screen.height * 0.6f;
        var r = new Rect(Screen.width - w - 20 * k, Screen.height - h - 24 * k, w, h);
        Panel(r);
        GUILayout.BeginArea(new Rect(r.x + 16 * k, r.y + 12 * k, r.width - 32 * k, r.height - 24 * k));

        DrawObjectives(k);
        GUILayout.Space(10 * k);
        DrawEvidence(k);
        GUILayout.Space(10 * k);
        DrawStatements(k);

        GUILayout.EndArea();
    }

    void DrawObjectives(float k)
    {
        var brain = ActiveConsole != null ? ActiveConsole.Brain : null;
        if (brain == null || brain.objectives.Count == 0) return;
        GUILayout.Label($"<color={GREEN}>YOUR NEXT MOVES</color>", _header);

        // Show at most 3 UNFINISHED objectives; as each ticks off, the next slides in.
        // A couple recently-completed ones stay visible (struck through) for a beat of
        // satisfaction before scrolling off.
        int shown = 0, doneShown = 0;
        foreach (var o in brain.objectives)
        {
            if (o.Done)
            {
                if (doneShown >= 1) continue; // keep just the most recent tick visible
                doneShown++;
                GUILayout.Label($"<color={GREEN}>✓ <s>{o.label}</s></color>", _body);
            }
            else
            {
                if (shown >= 3) break;
                shown++;
                GUILayout.Label($"<color={DIM}>○ {o.label}</color>", _body);
            }
        }
    }

    void DrawEvidence(float k)
    {
        bool any = false;
        foreach (var e in Board.AllEvidence) if (e.discovered) { any = true; break; }
        if (!any) return;
        GUILayout.Label($"<color={GOLD}>EVIDENCE</color>", _header);
        foreach (var e in Board.AllEvidence)
        {
            if (!e.discovered) continue;
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<color={GOLD}>◆</color> {e.label}", _body);
            if (ActiveConsole != null && Runner.CurrentPhase == CaseRunner.Phase.Interrogation &&
                GUILayout.Button("Present", GUILayout.Width(84 * k)))
                ActiveConsole.presentedEvidenceId = e.id;
            GUILayout.EndHorizontal();
        }
    }

    void DrawStatements(float k)
    {
        if (Board.Cards.Count == 0 && Board.Contradictions.Count == 0 && Board.SuspicionLoops.Count == 0) return;
        GUILayout.Label($"<color={PURPLE}>NOTES</color>", _header);
        _cardScroll = GUILayout.BeginScrollView(_cardScroll, GUILayout.ExpandHeight(true));

        foreach (var c in Board.Contradictions)
            GUILayout.Label($"<color={RED}>⚠ {c.a.suspectName} vs {c.b.suspectName}:</color> {c.reason}", _body);
        foreach (var l in Board.SuspicionLoops)
            GUILayout.Label($"<color={CYAN}>⇄ {l.a.suspectName} & {l.b.suspectName} accuse each other</color>", _body);

        foreach (var c in Board.Cards)
        {
            string color = c.isBrokenReveal ? RED : c.isFalseGive ? GOLD : c.tag == "accusation" ? CYAN : DIM;
            string tag = c.isBrokenReveal ? " [secret]" : c.isFalseGive ? " [petty]" : c.tag == "accusation" ? " [opinion]" : "";
            GUILayout.Label($"<color={color}><b>{c.suspectName}</b>{tag}: {c.text}</color>", _body);
            GUILayout.Space(4 * k);
        }
        GUILayout.EndScrollView();
    }
}
