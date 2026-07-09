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
    [Tooltip("Playback pitch — 0.85-0.95 adds gravel to a stock voice.")]
    [Range(0.5f, 1.5f)] public float pitch = 0.9f;
    [Range(0f, 1f)] public float volume = 0.9f;

    public bool IsSpeaking => _source != null && _source.isPlaying;

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
        Locate();
    }

    void Locate()
    {
        _checked = true;
        _available = false;
        string dir = Path.Combine(Application.streamingAssetsPath, "piper");
        _exePath = Path.Combine(dir, "piper.exe");
        if (!File.Exists(_exePath)) return;
        // first voice model found in the folder is the NPC's voice
        if (!Directory.Exists(dir)) return;
        foreach (var f in Directory.GetFiles(dir, "*.onnx"))
        {
            _modelPath = f;
            break;
        }
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

        string wavPath = Path.Combine(_cacheDir, Hash(text + Path.GetFileName(_modelPath)) + ".wav");

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
            _source.pitch = pitch;
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
            Arguments = $"--model \"{_modelPath}\" --output_file \"{wavPath}\"",
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
