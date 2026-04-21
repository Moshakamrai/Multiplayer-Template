using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Offline spectral-flux beat detector for a voice-input rhythm game.
///
/// "Big beat" philosophy:
///   Only strong, prominent beats (kicks, main pulse) become triggers.
///   Weak hits (ghost notes, light hi-hats that bleed through) are discarded
///   via a strength-percentile filter after onset detection.
///
/// Chain rules:
///   - No attack triggers in the first noTriggerBeforeSec seconds (grace period)
///   - Voice INPUT WINDOW comes BEFORE the attack fires — player speaks, then attack fires
///   - Every chain (single or multi) gets a flat minInputWindowSec window before its first beat
///   - Beats within maxInChainGapSec of each other form a chain (up to 5 beats)
///   - Windows never overlap — next chain only qualifies if its window fits after the previous one
///</summary>
public class BeatAnalysisEngine : MonoBehaviour
{
    // ── FFT ───────────────────────────────────────────────────────────────
    private const int FFT_SIZE = 2048;
    private const int HOP_SIZE = 512;

    // ── Onset detection ───────────────────────────────────────────────────
    [Header("Onset Detection")]
    [Tooltip("Higher = stricter — only the very loudest hits pass. 1.8–3.0 typical.")]
    [Range(1.2f, 4.0f)] public float thresholdMultiplier = 2.0f;

    [Tooltip("Minimum seconds between any two raw onsets (removes duplicates).")]
    [Range(0.05f, 0.4f)] public float minOnsetGapSec = 0.12f;

    [Tooltip("Only keep onsets in the top X% by strength — throws away ghost notes and weak hits. 0.4 = top 60%.")]
    [Range(0.0f, 0.8f)] public float strengthPercentileCutoff = 0.45f;

    // ── Trigger restrictions ──────────────────────────────────────────────
    [Header("Trigger Restrictions")]
    [Tooltip("No attack triggers are placed in the first N seconds of the song (5–8s recommended).")]
    [Range(0f, 10f)] public float noTriggerBeforeSec = 5f;

    [Tooltip("Flat voice input window duration before ANY attack (single or chain). Player speaks during this window, then the attack fires.")]
    [Range(1.0f, 8.0f)] public float minInputWindowSec = 3.0f;

    // ── Chain formation ───────────────────────────────────────────────────
    [Header("Chain Formation")]
    [Tooltip("Raw onset beats closer than this (seconds) are grouped into one chain. " +
             "At 120 BPM quarter-notes are 0.5s apart (single), 8th-notes 0.25s (chain). " +
             "0.38s = anything faster than ~158 BPM pairs forms a chain.")]
    [Range(0.1f, 0.8f)] public float maxInChainGapSec = 0.38f;

    // ── Events ────────────────────────────────────────────────────────────
    public event System.Action<BeatMap> OnAnalysisComplete;
    public event System.Action<float>   OnAnalysisProgress;
    public event System.Action<string>  OnAnalysisError;

    public bool    IsAnalyzing    { get; private set; }
    public BeatMap CurrentBeatMap { get; private set; }

    // ─────────────────────────────────────────────────────────────────────
    public void Analyze(AudioClip clip)
    {
        if (IsAnalyzing || clip == null) return;
        StartCoroutine(Pipeline(clip));
    }

    private IEnumerator Pipeline(AudioClip clip)
    {
        IsAnalyzing    = true;
        CurrentBeatMap = null;

        // ── 1. Load PCM ────────────────────────────────────────────────
        int sr    = clip.frequency;
        int ch    = clip.channels;
        int total = clip.samples;

        float waited = 0f;
        while (clip.loadState != AudioDataLoadState.Loaded && waited < 10f)
        { waited += Time.unscaledDeltaTime; yield return null; }

        if (clip.loadState != AudioDataLoadState.Loaded)
        { Fail("Clip not loaded — try WAV."); yield break; }

        float[] raw = new float[total * ch];
        clip.GetData(raw, 0);
        yield return null;

        // Silence check
        float maxAbs = 0f;
        for (int i = 0; i < Mathf.Min(raw.Length, 8192); i++)
            if (Mathf.Abs(raw[i]) > maxAbs) maxAbs = Mathf.Abs(raw[i]);
        if (maxAbs < 1e-6f) { Fail("Audio data is silent — reload clip."); yield break; }

        // Mix to mono
        float[] mono = new float[total];
        for (int i = 0; i < total; i++)
        {
            float s = 0f;
            for (int c = 0; c < ch; c++) s += raw[i * ch + c];
            mono[i] = s / ch;
        }
        raw = null;
        yield return null;
        Emit(0.05f);

        // ── 2. Spectral flux — bass-primary ────────────────────────────
        float[] hann    = HannWindow(FFT_SIZE);
        float[] prevMag = new float[FFT_SIZE / 2];
        float[] re      = new float[FFT_SIZE];
        float[] im      = new float[FFT_SIZE];

        float hzPerBin  = (float)sr / FFT_SIZE;
        // Narrow kick-drum band (50–200 Hz) for the primary "big beat" signal
        int   kickEnd   = Mathf.RoundToInt(200f  / hzPerBin);
        // Snare / upper body (200–5000 Hz)
        int   snareEnd  = Mathf.RoundToInt(5000f / hzPerBin);

        int     frames  = (total - FFT_SIZE) / HOP_SIZE;
        float[] bassFluxArr = new float[frames];   // kick-only signal (for strength scoring)
        float[] fullFluxArr = new float[frames];   // weighted combined

        for (int f = 0; f < frames; f++)
        {
            int off = f * HOP_SIZE;
            for (int i = 0; i < FFT_SIZE; i++)
            {
                re[i] = (off + i < mono.Length) ? mono[off + i] * hann[i] : 0f;
                im[i] = 0f;
            }
            FFT(re, im);

            float kickF = 0f, snareF = 0f, highF = 0f;
            for (int k = 1; k < FFT_SIZE / 2; k++)
            {
                float mag  = Mathf.Sqrt(re[k] * re[k] + im[k] * im[k]);
                float diff = Mathf.Max(0f, mag - prevMag[k]);
                prevMag[k] = mag;

                if      (k <= kickEnd)  kickF  += diff;
                else if (k <= snareEnd) snareF += diff;
                else                    highF  += diff;
            }

            bassFluxArr[f] = kickF;
            // Primary signal: heavy kick bias, snare adds body, high barely matters
            fullFluxArr[f] = kickF * 0.72f + snareF * 0.22f + highF * 0.06f;

            if (f % 150 == 0) { Emit(0.05f + 0.50f * (float)f / frames); yield return null; }
        }
        Emit(0.57f);

        // ── 3. Adaptive threshold + peak pick ──────────────────────────
        float hopSec  = (float)HOP_SIZE / sr;
        int   halfWin = Mathf.Max(1, Mathf.RoundToInt(0.4f / hopSec));
        float[] thr   = AdaptiveThreshold(fullFluxArr, halfWin, thresholdMultiplier);

        int         minGapF = Mathf.Max(1, Mathf.RoundToInt(minOnsetGapSec / hopSec));
        List<float> onsetT  = new List<float>();
        List<float> onsetS  = new List<float>();  // bass-only strength (for big-beat filter)
        int         lastPk  = -minGapF;

        for (int f = 1; f < frames - 1; f++)
        {
            if (fullFluxArr[f] > fullFluxArr[f-1] && fullFluxArr[f] >= fullFluxArr[f+1]
                && fullFluxArr[f] > thr[f] && f - lastPk >= minGapF)
            {
                onsetT.Add(f * hopSec);
                onsetS.Add(bassFluxArr[f]);   // strength = kick energy at this frame
                lastPk = f;
            }
        }

        // Fallback: lower threshold if too few beats
        if (onsetT.Count < 6)
        {
            thr = AdaptiveThreshold(fullFluxArr, halfWin, thresholdMultiplier * 0.65f);
            onsetT.Clear(); onsetS.Clear(); lastPk = -minGapF;
            for (int f = 1; f < frames - 1; f++)
            {
                if (fullFluxArr[f] > fullFluxArr[f-1] && fullFluxArr[f] >= fullFluxArr[f+1]
                    && fullFluxArr[f] > thr[f] && f - lastPk >= minGapF)
                { onsetT.Add(f * hopSec); onsetS.Add(bassFluxArr[f]); lastPk = f; }
            }
        }

        if (onsetT.Count < 4) { Fail($"Only {onsetT.Count} onsets — audio too quiet?"); yield break; }

        // ── 4. "Big beat" filter — keep only top (1-cutoff)% by strength ─
        // This discards ghost notes, light hi-hats, weak percussion.
        // Only strong, prominent hits (kicks, main pulse) survive.
        if (strengthPercentileCutoff > 0f && onsetT.Count > 8)
        {
            var sorted = new List<float>(onsetS);
            sorted.Sort();
            float cutoff = sorted[Mathf.FloorToInt(sorted.Count * strengthPercentileCutoff)];
            for (int i = onsetT.Count - 1; i >= 0; i--)
                if (onsetS[i] < cutoff) { onsetT.RemoveAt(i); onsetS.RemoveAt(i); }
        }

        Debug.Log($"[BeatEngine] Raw onsets after big-beat filter: {onsetT.Count}");
        Emit(0.68f);

        // ── 5. BPM estimation (for voice window formula only) ─────────
        // NOTE: we do NOT grid-snap beats. Grid snapping forces every gap
        // to exactly beatInterval, which breaks chain detection (gaps always
        // equal beatInterval > maxInChainGapSec → nothing ever chains).
        // Raw onset times are actually more accurate: the kick happened at
        // t=5.123s, not at the nearest quarter-note boundary.
        float bpm          = EstimateBPM(onsetT);
        float beatInterval = 60f / bpm;

        // Convert raw onset lists directly into Beat objects
        var beats = new List<Beat>();
        for (int i = 0; i < onsetT.Count; i++)
            beats.Add(new Beat { time = onsetT[i], strength = onsetS[i] });

        Debug.Log($"[BeatEngine] Beats (raw, no snap): {beats.Count}  BPM: {bpm:F1}");
        Emit(0.82f);
        yield return null;

        // ── 6. Build chains with all restrictions ──────────────────────
        List<BeatChain> chains = BuildChains(beats, beatInterval);  // beatInterval still used for BPM display

        Debug.Log($"[BeatEngine] Final chains: {chains.Count}  " +
                  $"(singles: {chains.FindAll(c=>c.chainLength==1).Count}, " +
                  $"2×: {chains.FindAll(c=>c.chainLength==2).Count}, " +
                  $"3×: {chains.FindAll(c=>c.chainLength==3).Count}, " +
                  $"4×: {chains.FindAll(c=>c.chainLength==4).Count}, " +
                  $"5×: {chains.FindAll(c=>c.chainLength>=5).Count})");

        Emit(1.0f);

        CurrentBeatMap = new BeatMap
        {
            bpm          = bpm,
            beatInterval = beatInterval,
            songName     = clip.name,
            songLength   = (float)total / sr,
            allBeats     = beats,
            chains       = chains
        };

        IsAnalyzing = false;
        OnAnalysisComplete?.Invoke(CurrentBeatMap);
    }

    // ── Chain builder — greedy single pass ───────────────────────────────
    // Voice window is placed BEFORE the attack: player speaks → attack fires.
    // A beat qualifies only if there is room for a full minInputWindowSec window
    // between the previous window's end and this beat's trigger time.
    private List<BeatChain> BuildChains(List<Beat> beats, float beatInterval)
    {
        var   chains        = new List<BeatChain>();
        float lastWindowEnd = -999f;  // end of previous voice window (= previous chain's FirstBeatTime)
        int   i             = 0;

        while (i < beats.Count)
        {
            var beat = beats[i];

            // Skip intro grace period
            if (beat.time < noTriggerBeforeSec)
            { i++; continue; }

            // Beat needs minInputWindowSec of free space before it fires
            // Window would start at (beat.time - minInputWindowSec); must be >= lastWindowEnd
            if (beat.time - minInputWindowSec < lastWindowEnd)
            { i++; continue; }

            // Qualifies — greedily pull in consecutive close beats (chain, up to 4)
            var chain = new BeatChain();
            chain.beats.Add(beat);
            int j = i + 1;
            while (j < beats.Count && chain.chainLength < 5)
            {
                if (beats[j].time - beats[j - 1].time > maxInChainGapSec) break;
                chain.beats.Add(beats[j]);
                j++;
            }

            FinalizeWindow(chain, lastWindowEnd);
            chains.Add(chain);
            lastWindowEnd = chain.inputWindowEnd;  // = chain.FirstBeatTime (window closes when attack fires)
            i = j;
        }

        for (int c = 0; c < chains.Count; c++)
            foreach (var b in chains[c].beats) b.chainIndex = c;

        return chains;
    }

    // Window comes BEFORE the first beat: player speaks during the window, then the attack fires.
    // Duration is always minInputWindowSec (flat, regardless of chain length).
    private void FinalizeWindow(BeatChain chain, float lastWindowEnd)
    {
        chain.inputWindowEnd      = chain.FirstBeatTime;
        chain.inputWindowStart    = Mathf.Max(lastWindowEnd, chain.FirstBeatTime - minInputWindowSec);
        chain.inputWindowDuration = chain.inputWindowEnd - chain.inputWindowStart;
    }

    // ── BPM via IOI histogram ─────────────────────────────────────────────
    private float EstimateBPM(List<float> t)
    {
        const float MIN = 55f, MAX = 205f, STEP = 0.5f;
        int   bins = Mathf.RoundToInt((MAX - MIN) / STEP) + 1;
        float[] h  = new float[bins];

        for (int i = 0; i < t.Count - 1; i++)
        for (int j = i + 1; j <= Mathf.Min(i + 6, t.Count - 1); j++)
        {
            float ioi = t[j] - t[i];
            if (ioi < 0.2f || ioi > 2.5f) continue;
            float bpm = 60f / ioi;
            float w   = 1f / (j - i);
            void Vote(float b, float wt)
            {
                if (b < MIN || b > MAX) return;
                int bin = Mathf.RoundToInt((b - MIN) / STEP);
                if (bin >= 0 && bin < bins) h[bin] += wt;
            }
            Vote(bpm,      w);
            Vote(bpm * 2f, w * 0.35f);
            Vote(bpm / 2f, w * 0.35f);
        }

        float[] sm = new float[bins];
        for (int b = 2; b < bins - 2; b++)
            sm[b] = h[b-2]*0.10f + h[b-1]*0.25f + h[b]*0.30f + h[b+1]*0.25f + h[b+2]*0.10f;

        int best = 0;
        for (int b = 1; b < bins; b++) if (sm[b] > sm[best]) best = b;
        return MIN + best * STEP;
    }

    // ── DSP helpers ───────────────────────────────────────────────────────
    private static float[] AdaptiveThreshold(float[] s, int halfWin, float mult)
    {
        float[] t = new float[s.Length];
        for (int i = 0; i < s.Length; i++)
        {
            int lo = Mathf.Max(0, i - halfWin), hi = Mathf.Min(s.Length - 1, i + halfWin);
            float sum = 0f;
            for (int j = lo; j <= hi; j++) sum += s[j];
            t[i] = sum / (hi - lo + 1) * mult;
        }
        return t;
    }

    private static float[] HannWindow(int n)
    {
        float[] w = new float[n];
        for (int i = 0; i < n; i++)
            w[i] = 0.5f * (1f - Mathf.Cos(2f * Mathf.PI * i / (n - 1)));
        return w;
    }

    private static void FFT(float[] re, float[] im)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j)
            { float t = re[i]; re[i] = re[j]; re[j] = t; t = im[i]; im[i] = im[j]; im[j] = t; }
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            float ang = -2f * Mathf.PI / len, wRe = Mathf.Cos(ang), wIm = Mathf.Sin(ang);
            for (int i = 0; i < n; i += len)
            {
                float cRe = 1f, cIm = 0f;
                for (int j = 0; j < len / 2; j++)
                {
                    float uRe = re[i+j], uIm = im[i+j];
                    float vRe = re[i+j+len/2]*cRe - im[i+j+len/2]*cIm;
                    float vIm = re[i+j+len/2]*cIm + im[i+j+len/2]*cRe;
                    re[i+j] = uRe+vRe; im[i+j] = uIm+vIm;
                    re[i+j+len/2] = uRe-vRe; im[i+j+len/2] = uIm-vIm;
                    float nRe = cRe*wRe - cIm*wIm; cIm = cRe*wIm + cIm*wRe; cRe = nRe;
                }
            }
        }
    }

    private void Emit(float p) => OnAnalysisProgress?.Invoke(p);
    private void Fail(string msg) { IsAnalyzing = false; OnAnalysisError?.Invoke(msg); }
}
