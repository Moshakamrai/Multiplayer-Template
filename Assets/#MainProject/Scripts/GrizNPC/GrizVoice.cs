using System.Collections;
using UnityEngine;

// Procedural gibberish speech ("Banjo-Kazooie / Animal Crossing speak").
// No audio assets: syllable blips are synthesized at Awake from a per-NPC seed,
// then a line of text is "spoken" as a pitched blip sequence timed to its length.
// Every NPC gets a distinct voice by changing voiceSeed / basePitch.
[RequireComponent(typeof(AudioSource))]
public class GrizVoice : MonoBehaviour
{
    [Header("Voice identity (change per NPC)")]
    [Tooltip("Seed for this NPC's syllable bank — different seed = different voice.")]
    public int voiceSeed = 1337;
    [Tooltip("Base frequency in Hz. Griz = low and gravelly (~90). Higher = squeakier NPC.")]
    public float baseFrequency = 92f;
    [Range(0.5f, 2f)] public float basePitch = 1f;

    [Header("Delivery")]
    [Tooltip("Seconds per syllable blip.")]
    public float syllableLength = 0.085f;
    public float syllableGap = 0.045f;
    [Range(0f, 1f)] public float volume = 0.55f;

    public bool IsSpeaking { get; private set; }

    AudioSource _source;
    AudioClip[] _bank;
    Coroutine _speaking;
    System.Random _rng;

    void Awake()
    {
        _source = GetComponent<AudioSource>();
        _source.playOnAwake = false;
        _source.spatialBlend = 0f; // 2D for the console; set 1 for a world NPC
        _rng = new System.Random(voiceSeed);
        BuildSyllableBank();
    }

    // 8 slightly different blips = enough variety that lines don't sound looped
    void BuildSyllableBank()
    {
        _bank = new AudioClip[8];
        for (int i = 0; i < _bank.Length; i++)
        {
            float freq = baseFrequency * (0.85f + 0.35f * (float)_rng.NextDouble());
            float grit = 0.05f + 0.15f * (float)_rng.NextDouble();
            _bank[i] = MakeBlip($"blip{i}", freq, grit, syllableLength);
        }
    }

    AudioClip MakeBlip(string name, float freq, float grit, float dur)
    {
        const int sr = 22050;
        int n = Mathf.CeilToInt(dur * sr);
        var data = new float[n];
        float phase = 0f;
        var noise = new System.Random(voiceSeed * 7 + name.Length);
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)sr;
            // fast attack, exponential decay — reads as a spoken syllable
            float env = (1f - Mathf.Exp(-t * 600f)) * Mathf.Exp(-t * 28f);
            phase += freq / sr;
            float saw = 2f * (phase - Mathf.Floor(phase + 0.5f));          // buzzy vocal-fold-ish
            float harm = Mathf.Sin(phase * 2f * Mathf.PI * 2.7f) * 0.35f;   // formant-ish color
            float gritN = ((float)noise.NextDouble() * 2f - 1f) * grit;     // gravel
            data[i] = (saw * 0.55f + harm + gritN) * env * 0.5f;
        }
        var clip = AudioClip.Create(name, n, 1, sr, false);
        clip.SetData(data, 0);
        return clip;
    }

    /// <summary>Speak a line as gibberish. moodPitch: 1 = normal, >1 = agitated (low patience).</summary>
    public void Speak(string line, float moodPitch = 1f)
    {
        Stop();
        _speaking = StartCoroutine(SpeakRoutine(line ?? "", Mathf.Clamp(moodPitch, 0.6f, 1.8f)));
    }

    /// <summary>Cut him off mid-line (the interruption mechanic needs this to be audible).</summary>
    public void Stop()
    {
        if (_speaking != null) StopCoroutine(_speaking);
        _speaking = null;
        IsSpeaking = false;
    }

    IEnumerator SpeakRoutine(string line, float moodPitch)
    {
        IsSpeaking = true;
        bool question = line.TrimEnd().EndsWith("?");
        string[] words = line.Split(' ');
        var localRng = new System.Random(line.GetHashCode() ^ voiceSeed); // same line = same melody

        int spoken = 0;
        int totalSyllables = 0;
        foreach (var w in words) totalSyllables += SyllableCount(w);
        totalSyllables = Mathf.Clamp(totalSyllables, 1, 40); // cap long lines — gist, not audiobook

        foreach (var word in words)
        {
            if (spoken >= totalSyllables) break;
            bool shouted = word.Length > 2 && word == word.ToUpperInvariant() && word.ToLowerInvariant() != word;
            int syl = SyllableCount(word);
            for (int s = 0; s < syl && spoken < totalSyllables; s++, spoken++)
            {
                float progress = spoken / (float)totalSyllables;
                float drift = question
                    ? Mathf.Lerp(0.95f, 1.25f, progress)     // rising = question
                    : Mathf.Lerp(1.05f, 0.9f, progress);     // falling = statement
                _source.pitch = basePitch * moodPitch * drift * (0.92f + 0.16f * (float)localRng.NextDouble())
                                * (shouted ? 1.25f : 1f);
                _source.PlayOneShot(_bank[localRng.Next(_bank.Length)], shouted ? volume * 1.5f : volume);
                yield return new WaitForSeconds(syllableLength + syllableGap * (0.7f + 0.6f * (float)localRng.NextDouble()));
            }
            yield return new WaitForSeconds(syllableGap); // word gap
        }
        IsSpeaking = false;
        _speaking = null;
    }

    static int SyllableCount(string word)
    {
        int count = 0;
        bool prevVowel = false;
        foreach (char c in word.ToLowerInvariant())
        {
            bool vowel = "aeiouy".IndexOf(c) >= 0;
            if (vowel && !prevVowel) count++;
            prevVowel = vowel;
        }
        return Mathf.Max(1, count);
    }
}
