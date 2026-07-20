using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// GNOMES & GASLIGHT — the cold open: narrated photographs setting up the murder before
// the first interview. This is the single highest-leverage polish pass on the whole game —
// no LLM variance, no gameplay systems, just authored pacing and stakes, entirely under
// the designer's control. Content-driven so it works with placeholders TODAY and upgrades
// to real photography/narration later with zero code changes:
//
//   StreamingAssets/gnomes-intro/
//     001_manor.jpg, 002_table.jpg, 003_gnomes.jpg, 004_staircase.jpg, ...  (any count, sorted by filename)
//     narration.mp3 (or .wav/.ogg)      <- your own recorded voiceover, full sequence
//     narration.txt                     <- ONE caption line per photo, blank line = no caption change
//
// If narration.mp3 is missing, captions still display (silent slideshow) so the scene is
// always testable. If photos are missing, a single dark placeholder card is shown so the
// beats and timing can still be judged before real photography exists.
public class IntroSequence : MonoBehaviour
{
    [Tooltip("Called when the intro finishes or is skipped — hook this to CaseRunner.BeginCase()/BeginInterrogation.")]
    public event Action OnIntroComplete;

    [Header("Pacing")]
    [Tooltip("Seconds each photo holds if no narration audio is present to drive timing.")]
    public float secondsPerSlideNoAudio = 4.5f;
    public float crossfadeSeconds = 1.2f;
    [Tooltip("Fraction of narration audio duration allotted per slide if slide count doesn't evenly divide — even split is used unless narration.txt has per-line timing (not required for v1).")]
    public bool allowSkip = true;

    AudioSource _narrationSource;
    Texture2D[] _photos;
    string[] _captions;
    int _current = -1;
    float _alpha; // crossfade blend 0..1 toward _current
    Texture2D _prevPhoto;
    bool _finished;
    bool _started;

    const string BG = "#0a0a0d";

    void Awake()
    {
        _narrationSource = gameObject.AddComponent<AudioSource>();
        _narrationSource.playOnAwake = false;
        _narrationSource.spatialBlend = 0f;
    }

    public void Begin()
    {
        if (_started) return;
        _started = true;
        LoadAssets();
        StartCoroutine(RunSequence());
    }

    void LoadAssets()
    {
        string dir = Path.Combine(Application.streamingAssetsPath, "gnomes-intro");
        var photoList = new List<Texture2D>();
        if (Directory.Exists(dir))
        {
            var files = new List<string>(Directory.GetFiles(dir, "*.jpg"));
            files.AddRange(Directory.GetFiles(dir, "*.png"));
            files.Sort(StringComparer.OrdinalIgnoreCase); // 001_, 002_... controls order
            foreach (var f in files)
            {
                var tex = new Texture2D(2, 2);
                if (tex.LoadImage(File.ReadAllBytes(f))) photoList.Add(tex);
            }
        }
        _photos = photoList.ToArray();

        string captionPath = Path.Combine(dir, "narration.txt");
        _captions = File.Exists(captionPath)
            ? File.ReadAllLines(captionPath)
            : new[] { "The Ravenscroft estate. A death, and a house full of reasons." };

        // narration audio: try common extensions; missing = silent slideshow, still testable
        foreach (var ext in new[] { "mp3", "wav", "ogg" })
        {
            string audioPath = Path.Combine(dir, $"narration.{ext}");
            if (File.Exists(audioPath))
                StartCoroutine(LoadNarrationAudio(audioPath));
        }
    }

    IEnumerator LoadNarrationAudio(string path)
    {
        var type = path.EndsWith(".mp3") ? AudioType.MPEG : path.EndsWith(".ogg") ? AudioType.OGGVORBIS : AudioType.WAV;
        using (var req = UnityEngine.Networking.UnityWebRequestMultimedia.GetAudioClip("file://" + path, type))
        {
            yield return req.SendWebRequest();
            if (req.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                _narrationSource.clip = UnityEngine.Networking.DownloadHandlerAudioClip.GetContent(req);
                _narrationSource.Play();
            }
        }
    }

    IEnumerator RunSequence()
    {
        int slideCount = Mathf.Max(1, _photos.Length == 0 ? _captions.Length : _photos.Length);

        // Give narration audio a moment to start loading/playing before timing off its length.
        yield return new WaitForSeconds(0.15f);
        float totalSeconds = (_narrationSource.clip != null)
            ? _narrationSource.clip.length
            : slideCount * secondsPerSlideNoAudio;
        float perSlide = Mathf.Max(2f, totalSeconds / slideCount);

        for (int i = 0; i < slideCount; i++)
        {
            if (_finished) yield break;
            AdvanceTo(i);
            float t0 = Time.time;
            while (Time.time - t0 < perSlide)
            {
                if (_finished) yield break;
                yield return null;
            }
        }
        Finish();
    }

    void AdvanceTo(int index)
    {
        _prevPhoto = _current >= 0 && _current < _photos.Length ? _photos[_current] : null;
        _current = index;
        _alpha = 0f;
    }

    void Update()
    {
        if (!_started || _finished) return;
        _alpha = Mathf.MoveTowards(_alpha, 1f, Time.deltaTime / Mathf.Max(0.01f, crossfadeSeconds));

        if (allowSkip && (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) || Input.GetMouseButtonDown(0)))
            Finish();
    }

    void Finish()
    {
        if (_finished) return;
        _finished = true;
        if (_narrationSource.isPlaying) _narrationSource.Stop();
        OnIntroComplete?.Invoke();
    }

    void OnGUI()
    {
        if (!_started || _finished) return;
        var full = new Rect(0, 0, Screen.width, Screen.height);

        GUI.color = HexColor(BG);
        GUI.DrawTexture(full, Texture2D.whiteTexture);
        GUI.color = Color.white;

        Texture2D current = (_current >= 0 && _current < _photos.Length) ? _photos[_current] : null;
        DrawFitted(full, _prevPhoto, 1f - _alpha);
        DrawFitted(full, current, _alpha);

        // caption bar
        string caption = _captions.Length > 0 ? _captions[Mathf.Clamp(_current, 0, _captions.Length - 1)] : "";
        if (!string.IsNullOrWhiteSpace(caption))
        {
            float k = Mathf.Max(1f, Screen.height / 900f);
            var capRect = new Rect(40 * k, Screen.height - 120 * k, Screen.width - 80 * k, 80 * k);
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(22 * k),
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                richText = true
            };
            GUI.color = new Color(0, 0, 0, 0.55f);
            GUI.DrawTexture(capRect, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(capRect, $"<color=#e5e5e5>{caption}</color>", style);
        }

        if (allowSkip)
        {
            var skipStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.LowerRight };
            GUI.Label(new Rect(Screen.width - 220, Screen.height - 30, 200, 24),
                "<color=#666>click / space to skip</color>", new GUIStyle(skipStyle) { richText = true });
        }
    }

    static void DrawFitted(Rect area, Texture2D tex, float alpha)
    {
        if (tex == null || alpha <= 0f) return;
        float texAspect = (float)tex.width / tex.height;
        float areaAspect = area.width / area.height;
        Rect fit;
        if (texAspect > areaAspect)
        {
            float h = area.width / texAspect;
            fit = new Rect(area.x, area.y + (area.height - h) / 2f, area.width, h);
        }
        else
        {
            float w = area.height * texAspect;
            fit = new Rect(area.x + (area.width - w) / 2f, area.y, w, area.height);
        }
        var prevColor = GUI.color;
        GUI.color = new Color(1, 1, 1, alpha);
        GUI.DrawTexture(fit, tex, ScaleMode.ScaleToFit);
        GUI.color = prevColor;
    }

    static Color HexColor(string hex)
    {
        ColorUtility.TryParseHtmlString(hex, out var c);
        return c;
    }
}
