using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

// Accurate FINAL transcription for the hub — whisper.cpp server as a subprocess
// (same ship-a-separate-exe pattern as Piper and Llama).
//
// Division of labour: Vosk keeps doing the LIVE "hearing…" feedback and all combat
// words; Whisper re-transcribes the recorded utterance audio after the silence timer
// fires, and ITS text is what actually goes to the NPC brain. Vosk mishears become
// cosmetic.
//
// Bangla gateway: set language="bn" (+ translateToEnglish=true) and players can speak
// Bangla — Whisper outputs English text and the whole downstream pipeline (LLM intents,
// authored lines) works unchanged.
public class WhisperTranscriber : MonoBehaviour
{
    public int port = 8732;
    [Tooltip("Spoken language code: en, bn, hi, ... Applied at server launch.")]
    public string language = "en";
    [Tooltip("Whisper's built-in translate-to-English (the Bangla → English NPC trick).")]
    public bool translateToEnglish = false;

    [Header("Per-language model choice (filename substring, case-insensitive)")]
    [Tooltip("Model file to prefer in ENGLISH mode (e.g. \"small\"). Empty = first .bin found. " +
             "English is easy for Whisper — the small model is fine and fast.")]
    public string englishModelContains = "small";
    [Tooltip("Model file to prefer in BANGLA mode (e.g. \"large\" or \"medium\"). Bangla is a " +
             "low-resource language for Whisper — the small model's bn recognition/translation is " +
             "genuinely poor; use large-v3-turbo or medium for usable results. If no matching file " +
             "exists, falls back to whatever's there (and logs which model it actually loaded).")]
    public string banglaModelContains = "large";
    [Tooltip("Seconds of audio kept from before the detected utterance start (catches clipped first words).")]
    public float preRollSeconds = 0.5f;
    public float requestTimeout = 10f;

    public bool IsReady { get; private set; }
    /// <summary>Filename of the model actually loaded (shown in the console status bar).</summary>
    public string LoadedModelName { get; private set; } = "";

    const int SampleRate = 16000;

    System.Diagnostics.Process _proc;
    string _url;
    VoiceProcessor _vp;

    // rolling mic capture (same frames Vosk consumes), ~45s window
    readonly List<short> _ring = new List<short>();
    int _ringStartSample;
    int _totalSamples;
    int _utteranceStart = -1;

    void Start()
    {
        Launch();
        StartCoroutine(HookMicrophone());
    }

    /// <summary>Kill and relaunch the server with a new language — drives the on-screen
    /// EN/BN toggle. translate=true makes Whisper output ENGLISH text for foreign speech,
    /// so Bangla mode needs zero changes anywhere else in the pipeline.</summary>
    public void Restart(string newLanguage, bool translate)
    {
        language = newLanguage;
        translateToEnglish = translate;
        IsReady = false;
        Kill();
        StartCoroutine(RelaunchAfterPortFrees());
    }

    // Process.Kill() is async on Windows — the old server can still hold the port for a
    // moment. A short delay avoids a bind failure on rapid EN/BN toggling.
    IEnumerator RelaunchAfterPortFrees()
    {
        yield return new WaitForSeconds(0.5f);
        Launch();
    }

    void Launch()
    {
        string dir = Path.Combine(Application.streamingAssetsPath, "whisper");
        string exe = Path.Combine(dir, "whisper-server.exe");
        if (!File.Exists(exe)) exe = Path.Combine(dir, "server.exe"); // older release name
        // Pick the model by language: small is fine for English, but Bangla needs a bigger
        // tier to be usable. Prefer the per-language filename substring; fall back to the
        // first .bin found so a single-model install still works.
        string prefer = (language == "bn" ? banglaModelContains : englishModelContains) ?? "";
        string model = null, fallback = null;
        if (Directory.Exists(dir))
        {
            foreach (var f in Directory.GetFiles(dir, "*.bin"))
            {
                if (fallback == null) fallback = f;
                if (prefer.Length > 0 && Path.GetFileName(f).ToLowerInvariant().Contains(prefer.ToLowerInvariant()))
                {
                    model = f;
                    break;
                }
            }
        }
        if (model == null) model = fallback;

        if (!File.Exists(exe) || model == null)
        {
            Debug.Log("[Whisper] not installed — Vosk transcripts used as-is. Tools > Griz > Check Whisper Setup.");
            return;
        }
        LoadedModelName = Path.GetFileName(model).Replace("ggml-", "").Replace(".bin", "");
        Debug.Log($"[Whisper] loading '{Path.GetFileName(model)}' for lang={language}" +
                  (prefer.Length > 0 && !Path.GetFileName(model).ToLowerInvariant().Contains(prefer.ToLowerInvariant())
                      ? $" (WARNING: no file matching '{prefer}' found — using fallback; Bangla quality will suffer if this is the small model)"
                      : ""));

        _url = $"http://127.0.0.1:{port}/";
        string args = $"-m \"{model}\" --port {port} -l {language}" + (translateToEnglish ? " --translate" : "");
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            WorkingDirectory = dir,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        try { _proc = System.Diagnostics.Process.Start(psi); }
        catch (Exception e)
        {
            Debug.LogWarning($"[Whisper] failed to start whisper-server: {e.Message}");
            return;
        }
        StartCoroutine(WaitUntilReady());
    }

    IEnumerator WaitUntilReady()
    {
        float deadline = Time.realtimeSinceStartup + 90f;
        while (Time.realtimeSinceStartup < deadline)
        {
            using (var req = UnityWebRequest.Get(_url))
            {
                req.timeout = 2;
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success)
                {
                    IsReady = true;
                    Debug.Log($"[Whisper] ✅ ready — accurate finals active (lang={language}{(translateToEnglish ? ", translate" : "")}).");
                    yield break;
                }
            }
            yield return new WaitForSeconds(1f);
        }
        Debug.LogWarning("[Whisper] server never became ready — Vosk transcripts used as-is.");
    }

    IEnumerator HookMicrophone()
    {
        while (_vp == null)
        {
            var vosk = FindObjectOfType<VoskSpeechToText>();
            _vp = vosk != null ? vosk.VoiceProcessor : FindObjectOfType<VoiceProcessor>();
            if (_vp == null) yield return new WaitForSeconds(0.5f);
        }
        _vp.OnFrameCaptured += OnFrame;
    }

    void OnFrame(short[] samples)
    {
        _ring.AddRange(samples);
        _totalSamples += samples.Length;
        int cap = SampleRate * 45;
        if (_ring.Count > cap)
        {
            int drop = _ring.Count - cap;
            _ring.RemoveRange(0, drop);
            _ringStartSample += drop;
        }
    }

    /// <summary>Call when the player starts a new utterance (first live partial).</summary>
    public void MarkUtteranceStart()
    {
        _utteranceStart = Mathf.Max(_ringStartSample, _totalSamples - Mathf.RoundToInt(preRollSeconds * SampleRate));
    }

    /// <summary>Transcribe everything since MarkUtteranceStart. done(text) — null on any failure.</summary>
    public IEnumerator EndUtteranceAndTranscribe(Action<string> done)
    {
        if (!IsReady || _utteranceStart < 0) { done(null); yield break; }

        int start = Mathf.Max(_utteranceStart, _ringStartSample);
        int count = _totalSamples - start;
        _utteranceStart = -1;
        // Under 0.5s of audio is never a real sentence — and short/near-empty clips are
        // exactly what makes large Whisper models hallucinate ("it", "the", multilingual
        // token soup). Raised from 0.25s after seeing that in the wild.
        if (count < SampleRate / 2) { done(null); yield break; }

        var samples = new short[count];
        _ring.CopyTo(start - _ringStartSample, samples, 0, count);

        var form = new List<IMultipartFormSection>
        {
            new MultipartFormFileSection("file", ToWav(samples), "utterance.wav", "audio/wav"),
            new MultipartFormDataSection("response_format", "json"),
            new MultipartFormDataSection("temperature", "0.0"),
        };

        using (var req = UnityWebRequest.Post(_url + "inference", form))
        {
            req.timeout = Mathf.Max(2, Mathf.CeilToInt(requestTimeout));
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success) { done(null); yield break; }

            var m = Regex.Match(req.downloadHandler.text, "\"text\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            if (!m.Success) { done(null); yield break; }
            done(Regex.Unescape(m.Groups[1].Value).Trim());
        }
    }

    static byte[] ToWav(short[] samples)
    {
        int dataLen = samples.Length * 2;
        var bytes = new byte[44 + dataLen];
        using (var ms = new MemoryStream(bytes))
        using (var w = new BinaryWriter(ms))
        {
            w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            w.Write(36 + dataLen);
            w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
            w.Write(16);
            w.Write((short)1);            // PCM
            w.Write((short)1);            // mono
            w.Write(SampleRate);
            w.Write(SampleRate * 2);      // byte rate
            w.Write((short)2);            // block align
            w.Write((short)16);           // bits
            w.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            w.Write(dataLen);
            foreach (var s in samples) w.Write(s);
        }
        return bytes;
    }

    void OnDestroy()
    {
        if (_vp != null) _vp.OnFrameCaptured -= OnFrame;
        Kill();
    }
    void OnApplicationQuit() { Kill(); }

    void Kill()
    {
        try { if (_proc != null && !_proc.HasExited) _proc.Kill(); } catch { }
        _proc = null;
        IsReady = false;
    }
}
