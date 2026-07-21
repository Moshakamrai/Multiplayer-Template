using System.Collections.Generic;
using System.IO;
using UnityEngine;

// CHARACTER LAB — an audition bench for every blendshape head in the project.
// Cycle models, fire each facial expression, hear every installed Piper voice, tweak
// pitch/speed live, swap the material shader, and watch lipsync drive off real TTS audio.
// Purely a dev tool: nothing here is part of the game loop.
//
// Built by Tools > Gnomes & Gaslight > Create Character Lab Scene.
public class CharacterLab : MonoBehaviour
{
    [System.Serializable]
    public class Subject
    {
        public string label;
        public GameObject instance;   // spawned model root (inactive unless selected)
        public FacialExpressions face;
        public MouthLipSync lipSync;
    }

    public List<Subject> subjects = new List<Subject>();
    public PiperVoice voice;

    [Tooltip("Line the voice speaks when you hit Speak — long enough to watch lipsync properly.")]
    [TextArea(2, 4)]
    public string testLine = "Oh, I do see everything from this window, you know. Every little coming and going.";

    static readonly string[] Moods = { "neutral", "happy", "sad", "terrified", "confused", "angry" };

    int _subject;
    int _voiceIndex;
    string[] _voiceFiles = new string[0];
    string[] _voiceNames = new string[0];
    string _status = "";
    Vector2 _scroll;

    // Shader options applied to every renderer on the selected model.
    static readonly string[] ShaderNames = { "Storybook/Cel", "Universal Render Pipeline/Lit", "Sprites/Default" };
    int _shaderIndex;

    void Start()
    {
        ScanVoices();
        SelectSubject(0);
    }

    void ScanVoices()
    {
        string dir = Path.Combine(Application.streamingAssetsPath, "piper");
        if (!Directory.Exists(dir)) { _status = "no StreamingAssets/piper folder"; return; }
        var files = Directory.GetFiles(dir, "*.onnx");
        _voiceFiles = files;
        _voiceNames = new string[files.Length];
        for (int i = 0; i < files.Length; i++) _voiceNames[i] = Path.GetFileNameWithoutExtension(files[i]);
    }

    void SelectSubject(int index)
    {
        if (subjects.Count == 0) return;
        _subject = ((index % subjects.Count) + subjects.Count) % subjects.Count;
        for (int i = 0; i < subjects.Count; i++)
            if (subjects[i].instance != null) subjects[i].instance.SetActive(i == _subject);

        // Point the shared voice at THIS model's lipsync so the mouth follows the audio.
        var s = subjects[_subject];
        if (s.lipSync != null) s.lipSync.piperVoice = voice;
        if (s.face != null) s.face.piperVoice = voice;
    }

    void ApplyMood(string mood)
    {
        var s = subjects.Count > 0 ? subjects[_subject] : null;
        if (s?.face != null) { s.face.SetMood(mood); _status = $"mood → {mood}"; }
        else _status = "this model has no FacialExpressions (no blendshapes?)";
    }

    void ApplyVoice()
    {
        if (voice == null || _voiceFiles.Length == 0) return;
        voice.preferredModel = _voiceNames[_voiceIndex];
        voice.UseLanguage(_voiceNames[_voiceIndex].StartsWith("bn_") ? "bn" : "en");
        _status = $"voice → {_voiceNames[_voiceIndex]}";
    }

    void ApplyShader()
    {
        var s = subjects.Count > 0 ? subjects[_subject] : null;
        if (s?.instance == null) return;
        var shader = Shader.Find(ShaderNames[_shaderIndex]);
        if (shader == null) { _status = $"shader not found: {ShaderNames[_shaderIndex]}"; return; }
        foreach (var r in s.instance.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.materials; // instances, so we don't mutate shared project assets
            for (int i = 0; i < mats.Length; i++)
            {
                var tex = mats[i].mainTexture;
                mats[i].shader = shader;
                if (tex != null) mats[i].mainTexture = tex; // keep the albedo through the swap
            }
            r.materials = mats;
        }
        _status = $"shader → {ShaderNames[_shaderIndex]}";
    }

    void OnGUI()
    {
        float k = Mathf.Max(1f, Screen.height / 900f);
        var box = new GUIStyle(GUI.skin.box) { fontSize = Mathf.RoundToInt(15 * k) };
        var btn = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(16 * k) };
        var lab = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(16 * k), richText = true, wordWrap = true };
        var head = new GUIStyle(lab) { fontStyle = FontStyle.Bold };

        float w = 380 * k;
        GUILayout.BeginArea(new Rect(12 * k, 12 * k, w, Screen.height - 24 * k), box);
        _scroll = GUILayout.BeginScrollView(_scroll);

        GUILayout.Label("<b>CHARACTER LAB</b>", head);
        GUILayout.Space(6 * k);

        // ── model ──
        string subjectName = subjects.Count > 0 ? subjects[_subject].label : "(none)";
        GUILayout.Label($"Model: <b>{subjectName}</b>  ({_subject + 1}/{Mathf.Max(1, subjects.Count)})", lab);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("◀ prev", btn)) SelectSubject(_subject - 1);
        if (GUILayout.Button("next ▶", btn)) SelectSubject(_subject + 1);
        GUILayout.EndHorizontal();

        var cur = subjects.Count > 0 ? subjects[_subject] : null;
        GUILayout.Label(cur?.face == null
            ? "<color=#ff8a8a>no FacialExpressions — expressions/blink unavailable</color>"
            : "<color=#8ff0a4>FacialExpressions ✓</color>", lab);
        GUILayout.Label(cur?.lipSync == null
            ? "<color=#ff8a8a>no MouthLipSync — mouth won't move</color>"
            : "<color=#8ff0a4>MouthLipSync ✓</color>", lab);

        GUILayout.Space(10 * k);
        GUILayout.Label("<b>EXPRESSIONS</b>", head);
        foreach (var m in Moods)
            if (GUILayout.Button(m, btn)) ApplyMood(m);

        GUILayout.Space(10 * k);
        GUILayout.Label("<b>SHADER</b>", head);
        GUILayout.Label(ShaderNames[_shaderIndex], lab);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("◀", btn)) { _shaderIndex = (_shaderIndex - 1 + ShaderNames.Length) % ShaderNames.Length; ApplyShader(); }
        if (GUILayout.Button("▶", btn)) { _shaderIndex = (_shaderIndex + 1) % ShaderNames.Length; ApplyShader(); }
        GUILayout.EndHorizontal();

        GUILayout.Space(10 * k);
        GUILayout.Label("<b>VOICE</b>", head);
        if (_voiceFiles.Length == 0) GUILayout.Label("<color=#ff8a8a>no .onnx voices in StreamingAssets/piper</color>", lab);
        else
        {
            GUILayout.Label(_voiceNames[_voiceIndex], lab);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("◀", btn)) { _voiceIndex = (_voiceIndex - 1 + _voiceFiles.Length) % _voiceFiles.Length; ApplyVoice(); }
            if (GUILayout.Button("▶", btn)) { _voiceIndex = (_voiceIndex + 1) % _voiceFiles.Length; ApplyVoice(); }
            GUILayout.EndHorizontal();
        }

        if (voice != null)
        {
            GUILayout.Label($"pitch {voice.pitch:0.00}", lab);
            voice.pitch = GUILayout.HorizontalSlider(voice.pitch, 0.5f, 1.5f);
            GUILayout.Label($"speed (lengthScale) {voice.lengthScale:0.00}", lab);
            voice.lengthScale = GUILayout.HorizontalSlider(voice.lengthScale, 0.5f, 1.5f);
            GUILayout.Label(voice.Available ? "<color=#8ff0a4>piper ✓</color>" : "<color=#ff8a8a>piper not installed</color>", lab);
        }

        GUILayout.Space(10 * k);
        if (GUILayout.Button("▶ SPEAK (test lipsync)", btn, GUILayout.Height(40 * k)))
        {
            if (voice != null && voice.Available) { voice.Speak(testLine); _status = "speaking…"; }
            else _status = "piper unavailable";
        }

        GUILayout.Space(8 * k);
        GUILayout.Label($"<color=#ffd968>{_status}</color>", lab);

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }
}
