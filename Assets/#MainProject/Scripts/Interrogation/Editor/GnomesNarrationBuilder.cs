using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

// Tools > Gnomes & Gaslight > Generate Intro Narration (Piper)
//
// Batch-synthesizes StreamingAssets/gnomes-intro/narration.txt (one line per photo) into a
// single narration.wav via piper.exe, the same subprocess pattern PiperVoice.cs uses live —
// this is the OFFLINE equivalent, run once at author-time rather than per-line at runtime,
// because IntroSequence wants ONE continuous audio clip to time the whole slideshow off of.
//
// If you'd rather record your own narration (better performance than TTS for a cold open —
// entirely reasonable), just drop narration.mp3/.wav/.ogg into gnomes-intro/ yourself and
// skip this tool entirely; IntroSequence picks up whichever file is there.
public static class GnomesNarrationBuilder
{
    static string IntroDir => Path.Combine(Application.streamingAssetsPath, "gnomes-intro");
    static string PiperDir => Path.Combine(Application.streamingAssetsPath, "piper");

    [Tooltip("Silence between lines, seconds — gives each photo a beat before the next line starts.")]
    const float GapSeconds = 0.6f;

    [MenuItem("Tools/Gnomes & Gaslight/Generate Intro Narration (Piper)")]
    static void Generate()
    {
        string narrationTxt = Path.Combine(IntroDir, "narration.txt");
        if (!File.Exists(narrationTxt))
        {
            Debug.LogWarning($"[Gnomes Narration] No narration.txt found at {narrationTxt}.");
            return;
        }
        string piperExe = Path.Combine(PiperDir, "piper.exe");
        if (!File.Exists(piperExe))
        {
            Debug.LogWarning("[Gnomes Narration] piper.exe not found — Tools > Griz > Check Piper Setup first.");
            return;
        }
        string model = null;
        foreach (var f in Directory.GetFiles(PiperDir, "*.onnx"))
        {
            // Prefer a voice NOT already claimed by a character (avoid the narrator
            // sounding identical to Griz/Pemberton) — falls back to whatever's installed.
            string name = Path.GetFileName(f).ToLowerInvariant();
            if (name.Contains("bn_")) continue; // never use the Bangla voice for English narration
            model = f;
            if (!name.Contains("griz") && !name.Contains("northern_english")) break; // prefer a non-character voice if one exists
        }
        if (model == null)
        {
            Debug.LogWarning("[Gnomes Narration] No usable .onnx voice found in StreamingAssets/piper.");
            return;
        }

        var lines = new List<string>();
        foreach (var raw in File.ReadAllLines(narrationTxt))
            if (!string.IsNullOrWhiteSpace(raw)) lines.Add(raw.Trim());
        if (lines.Count == 0)
        {
            Debug.LogWarning("[Gnomes Narration] narration.txt is empty.");
            return;
        }

        Debug.Log($"[Gnomes Narration] Synthesizing {lines.Count} lines with voice '{Path.GetFileName(model)}'...");

        string tempDir = Path.Combine(IntroDir, "_narration_lines_tmp");
        Directory.CreateDirectory(tempDir);
        var linePaths = new List<string>();
        try
        {
            for (int i = 0; i < lines.Count; i++)
            {
                string outPath = Path.Combine(tempDir, $"line_{i:000}.wav");
                if (!RunPiper(piperExe, model, lines[i], outPath))
                {
                    Debug.LogError($"[Gnomes Narration] Failed to synthesize line {i + 1}: \"{lines[i]}\"");
                    return;
                }
                linePaths.Add(outPath);
                EditorUtility.DisplayProgressBar("Generating Narration", $"Line {i + 1}/{lines.Count}", (float)(i + 1) / lines.Count);
            }

            string finalPath = Path.Combine(IntroDir, "narration.wav");
            var lineStarts = StitchWavs(linePaths, finalPath, GapSeconds);

            // One start-time (seconds) per line, same order as narration.txt — lets
            // IntroSequence advance each slide exactly when ITS line begins instead of
            // guessing an even split across the whole clip (which drifts out of sync the
            // moment lines have different lengths, which they always do).
            string timingsPath = Path.Combine(IntroDir, "narration_timings.txt");
            File.WriteAllLines(timingsPath, lineStarts.ConvertAll(t => t.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)));

            Debug.Log($"[Gnomes Narration] Done — {finalPath} (+ narration_timings.txt for exact per-slide sync). Press Play and run the intro to hear it.");
            EditorUtility.RevealInFinder(finalPath);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            try { Directory.Delete(tempDir, true); } catch { /* best-effort cleanup */ }
            AssetDatabase.Refresh();
        }
    }

    static bool RunPiper(string exe, string model, string text, string outPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = $"--model \"{model}\" --output_file \"{outPath}\"",
            WorkingDirectory = Path.GetDirectoryName(exe),
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardInputEncoding = Encoding.UTF8,
        };
        using (var p = Process.Start(psi))
        {
            p.StandardInput.WriteLine(text);
            p.StandardInput.Close();
            if (!p.WaitForExit(20000)) { try { p.Kill(); } catch { } return false; }
        }
        return File.Exists(outPath);
    }

    // Minimal PCM WAV reader/writer — concatenates each line's samples with a silence gap
    // between them. Piper always emits 16-bit mono PCM, so this stays deliberately simple
    // rather than pulling in a full audio library for one offline authoring tool.
    // Returns each line's START TIME in the stitched clip (seconds) so the caller can write
    // an exact per-slide timing file instead of assuming an even split.
    static List<float> StitchWavs(List<string> paths, string outPath, float gapSeconds)
    {
        int sampleRate = 0, channels = 0, bitsPerSample = 0;
        var allSamples = new List<short>();
        var lineStarts = new List<float>();

        for (int i = 0; i < paths.Count; i++)
        {
            ReadWav(paths[i], out int sr, out int ch, out int bits, out short[] samples);
            if (i == 0) { sampleRate = sr; channels = ch; bitsPerSample = bits; }
            lineStarts.Add((float)allSamples.Count / (sampleRate * channels));
            allSamples.AddRange(samples);
            if (i < paths.Count - 1)
            {
                int gapSamples = Mathf.RoundToInt(gapSeconds * sampleRate * channels);
                allSamples.AddRange(new short[gapSamples]); // silence
            }
        }
        WriteWav(outPath, sampleRate, channels, bitsPerSample, allSamples);
        return lineStarts;
    }

    static void ReadWav(string path, out int sampleRate, out int channels, out int bitsPerSample, out short[] samples)
    {
        using (var fs = File.OpenRead(path))
        using (var br = new BinaryReader(fs))
        {
            br.ReadBytes(4); // "RIFF"
            br.ReadInt32();  // chunk size
            br.ReadBytes(4); // "WAVE"

            sampleRate = 44100; channels = 1; bitsPerSample = 16;
            short[] data = Array.Empty<short>();

            while (fs.Position < fs.Length - 8)
            {
                string chunkId = new string(br.ReadChars(4));
                int chunkSize = br.ReadInt32();
                long chunkEnd = fs.Position + chunkSize;

                if (chunkId == "fmt ")
                {
                    br.ReadInt16();                 // audio format
                    channels = br.ReadInt16();
                    sampleRate = br.ReadInt32();
                    br.ReadInt32();                 // byte rate
                    br.ReadInt16();                 // block align
                    bitsPerSample = br.ReadInt16();
                }
                else if (chunkId == "data")
                {
                    int sampleCount = chunkSize / 2;
                    data = new short[sampleCount];
                    for (int i = 0; i < sampleCount; i++) data[i] = br.ReadInt16();
                }
                fs.Position = chunkEnd + (chunkEnd % 2); // chunks are word-aligned
            }
            samples = data;
        }
    }

    static void WriteWav(string path, int sampleRate, int channels, int bitsPerSample, List<short> samples)
    {
        using (var fs = File.Create(path))
        using (var bw = new BinaryWriter(fs))
        {
            int byteRate = sampleRate * channels * bitsPerSample / 8;
            int blockAlign = channels * bitsPerSample / 8;
            int dataSize = samples.Count * 2;

            bw.Write("RIFF".ToCharArray());
            bw.Write(36 + dataSize);
            bw.Write("WAVE".ToCharArray());

            bw.Write("fmt ".ToCharArray());
            bw.Write(16);
            bw.Write((short)1); // PCM
            bw.Write((short)channels);
            bw.Write(sampleRate);
            bw.Write(byteRate);
            bw.Write((short)blockAlign);
            bw.Write((short)bitsPerSample);

            bw.Write("data".ToCharArray());
            bw.Write(dataSize);
            foreach (var s in samples) bw.Write(s);
        }
    }
}
