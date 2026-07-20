using System;
using System.Collections;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

// Runtime NPC speech via Piper TTS (https://github.com/rhasspy/piper), shipped WITH the game.
//
// How it works: piper.exe lives in StreamingAssets/piper/ next to a voice model (.onnx +
// .onnx.json). Speak(text) runs piper as a subprocess (never linked into the game binary —
// keep it that way for licensing), writes a WAV to a persistent cache, then plays it.
// Each unique line is synthesized ONCE per machine; repeats play from cache instantly.
//
// Setup: Tools > Griz > Check Piper Setup tells you exactly what to download and where.
// If piper isn't installed, Available=false and callers fall back to gibberish (GrizVoice).
public class PiperVoice : MonoBehaviour
{
    [Tooltip("Part of a voice model filename to prefer when several .onnx files are in StreamingAssets/piper (e.g. \"alan\"). Empty = first one found.")]
    public string preferredModel = "";

    [Header("Per-language voices (filename substring, like WhisperTranscriber's model selection)")]
    [Tooltip("Voice to use in ENGLISH mode. IMPORTANT: with both voices installed, 'first .onnx " +
             "found' would pick bn_BD (sorts before en_GB alphabetically) — this default fixes that.")]
    public string englishModelContains = "en_";
    [Tooltip("Voice to use in BANGLA mode (the trained bn_BD voice).")]
    public string banglaModelContains = "bn_";

    /// <summary>Switch the voice by language code ("bn" → banglaModelContains, anything else →
    /// englishModelContains) and re-resolve the model file. Cached lines are keyed by model
    /// filename, so switching back and forth never plays the wrong voice's cache.</summary>
    public void UseLanguage(string langCode)
    {
        preferredModel = langCode == "bn" ? banglaModelContains : englishModelContains;
        _checked = false;
        _modelPath = null;
        Locate();
        Debug.Log($"[Piper] voice for lang={langCode}: {(string.IsNullOrEmpty(_modelPath) ? "NOT FOUND" : Path.GetFileName(_modelPath))}");
    }
    [Tooltip("Playback pitch — lower = deeper and grimmer, near 1 = lighter/wry. Comic dwarf: ~0.8. Sarcastic: ~0.95. Old man: ~0.88.")]
    [Range(0.5f, 1.5f)] public float pitch = 0.88f;
    [Tooltip("Piper speaking speed: <1 = faster, >1 = slower. Slower + lower reads OLDER and less machine-like.")]
    [Range(0.5f, 1.5f)] public float lengthScale = 1.05f;
    [Tooltip("Speaker index for MULTI-speaker models (vctk, semaine, aru…). -1 = single-speaker model.")]
    public int speakerId = -1;
    [Range(0f, 1f)] public float volume = 0.9f;
    [Tooltip("Tiny per-line random pitch/speed drift so every line doesn't sound identically robotic. 0 = off.")]
    [Range(0f, 0.15f)] public float naturalVariation = 0.05f;

    public bool IsSpeaking => _source != null && _source.isPlaying;

    /// <summary>The AudioSource actually playing his voice — lipsync reads live amplitude from this.</summary>
    public AudioSource Source => _source;

    AudioSource _source;
    Coroutine _speaking;
    string _exePath;
    string _modelPath;
    string _cacheDir;
    bool _checked;
    bool _available;

    public bool Available
    {
        get
        {
            if (!_checked) Locate();
            return _available;
        }
    }

    void Awake()
    {
        _source = gameObject.AddComponent<AudioSource>();
        _source.playOnAwake = false;
        _source.spatialBlend = 0f; // set 1 for a world-space NPC
        // Default to the English voice explicitly — "first .onnx found" would silently pick
        // the Bangla voice (bn_ sorts before en_) once both are installed.
        if (string.IsNullOrEmpty(preferredModel)) preferredModel = englishModelContains;
        Locate();
    }

    void Locate()
    {
        _checked = true;
        _available = false;
        // Path.GetFullPath normalizes mixed forward/backslash separators (streamingAssetsPath
        // uses "/", Path.Combine's later segments use "\" on Windows) — that mixed form
        // silently fails Process.Start in standalone builds (Editor Play mode tolerates it).
        string dir = Path.GetFullPath(Path.Combine(Application.streamingAssetsPath, "piper"));
        _exePath = Path.Combine(dir, "piper.exe");
        if (!File.Exists(_exePath)) return;
        if (!Directory.Exists(dir)) return;
        // Prefer a model whose filename contains preferredModel. If that's not installed,
        // fall back to a SAME-LANGUAGE-FAMILY voice rather than blindly grabbing the first
        // .onnx — otherwise an English character whose specific voice isn't installed would
        // get spoken by whatever sorts first alphabetically, which is the Bangla model
        // (bn_ < en_). A Bangla model reading English is the "shit TTS" bug this prevents.
        bool wantBangla = !string.IsNullOrEmpty(preferredModel) &&
                          preferredModel.ToLowerInvariant().Contains("bn");
        string sameFamilyFallback = null; // an en_/bn_ voice matching the requested language
        string anyFallback = null;        // truly-last resort: any voice at all
        foreach (var f in Directory.GetFiles(dir, "*.onnx"))
        {
            string name = Path.GetFileName(f).ToLowerInvariant();
            if (anyFallback == null) anyFallback = f;

            bool isBangla = name.Contains("bn_") || name.Contains("bangla");
            if (sameFamilyFallback == null && isBangla == wantBangla) sameFamilyFallback = f;

            if (!string.IsNullOrEmpty(preferredModel) && name.Contains(preferredModel.ToLowerInvariant()))
            {
                _modelPath = f;
                break;
            }
        }
        if (string.IsNullOrEmpty(_modelPath)) _modelPath = sameFamilyFallback ?? anyFallback;
        if (string.IsNullOrEmpty(_modelPath)) return;

        _cacheDir = Path.Combine(Application.persistentDataPath, "piper-cache");
        Directory.CreateDirectory(_cacheDir);
        _available = true;
    }

    public void Speak(string line)
    {
        if (!Available) return;
        Stop();
        _speaking = StartCoroutine(SpeakRoutine(line));
    }

    public void Stop()
    {
        if (_speaking != null) StopCoroutine(_speaking);
        _speaking = null;
        if (_source != null) _source.Stop();
    }

    IEnumerator SpeakRoutine(string rawLine)
    {
        string text = CleanForSpeech(rawLine);
        if (string.IsNullOrWhiteSpace(text)) yield break;

        string wavPath = Path.Combine(_cacheDir, Hash($"{text}|{Path.GetFileName(_modelPath)}|{lengthScale:0.00}|{speakerId}") + ".wav");

        if (!File.Exists(wavPath))
        {
            Task task = Task.Run(() => RunPiper(text, wavPath));
            while (!task.IsCompleted) yield return null;
            if (task.IsFaulted || !File.Exists(wavPath))
            {
                Debug.LogWarning($"[Piper] synthesis failed: {task.Exception?.GetBaseException().Message}");
                yield break;
            }
        }

        using (var req = UnityWebRequestMultimedia.GetAudioClip("file://" + wavPath, AudioType.WAV))
        {
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[Piper] couldn't load wav: {req.error}");
                yield break;
            }
            var clip = DownloadHandlerAudioClip.GetContent(req);
            float drift = naturalVariation > 0f ? UnityEngine.Random.Range(-naturalVariation, naturalVariation) : 0f;
            _source.pitch = pitch + drift;
            _source.volume = volume;
            _source.clip = clip;
            _source.Play();
        }
        _speaking = null;
    }

    void RunPiper(string text, string wavPath)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = _exePath,
            Arguments = $"--model \"{_modelPath}\" --output_file \"{wavPath}\" --length_scale {lengthScale.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}" +
                        (speakerId >= 0 ? $" --speaker {speakerId}" : ""),
            WorkingDirectory = Path.GetDirectoryName(_exePath),
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardInputEncoding = Encoding.UTF8,
        };
        using (var p = System.Diagnostics.Process.Start(psi))
        {
            p.StandardInput.WriteLine(text);
            p.StandardInput.Close();
            if (!p.WaitForExit(20000))
            {
                try { p.Kill(); } catch { }
                throw new TimeoutException("piper.exe timed out");
            }
        }
    }

    // Stage directions like "(does not move)" read terribly aloud — strip them.
    static string CleanForSpeech(string line)
    {
        string t = Regex.Replace(line ?? "", @"\([^)]*\)", " ");
        t = t.Replace("—", ", ").Replace("…", "...").Replace("|", ",");
        return Regex.Replace(t, @"\s+", " ").Trim();
    }

    static string Hash(string s)
    {
        using (var md5 = MD5.Create())
        {
            var bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(s));
            var sb = new StringBuilder();
            foreach (var b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
