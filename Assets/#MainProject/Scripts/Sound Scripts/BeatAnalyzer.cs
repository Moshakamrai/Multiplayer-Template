using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class BeatAnalyzer : MonoBehaviour
{
    public static BeatAnalyzer Instance;
    private AudioSource _audioSource;

    [Header("FFT Settings")]
    private const int SAMPLE_SIZE = 1024;
    private float[] _spectrum = new float[SAMPLE_SIZE];
    private float _sampleRate;

    [Header("Band Isolation (Hz)")]
    public float bassMin = 20f;
    public float bassMax = 150f;
    public float snareMin = 2000f;
    public float snareMax = 5000f;

    [Header("Strict Filtering (TUNE THESE)")]
    [Tooltip("How much higher than the average noise a peak must be (Try 2.0 to 3.0)")]
    public float thresholdMultiplier = 2.5f; 
    
    [Tooltip("The absolute minimum strength to even be considered a beat (Kills hi-hats)")]
    public float minBeatStrength = 0.20f; 
    
    [Tooltip("Minimum time between beats. 0.45s = approx 130 BPM max speed.")]
    public float beatCooldown = 0.45f;

    private int _thresholdWindowSize = 50; 
    private Queue<float> _fluxHistory = new Queue<float>();
    private float _previousEnergy = 0f;
    private float _clipStartTime = 0f;

    public struct BeatData 
    {
        public float time;
        public float strength;
    }
    private List<BeatData> _rawScrapedBeats = new List<BeatData>();

    void Awake() { if (Instance == null) Instance = this; }

    void Start()
    {
        _audioSource = GetComponent<AudioSource>();
        _sampleRate = AudioSettings.outputSampleRate; 
        for (int i = 0; i < _thresholdWindowSize; i++) _fluxHistory.Enqueue(0f);
    }

    void Update()
    {
        if (!_audioSource.isPlaying) return;
        if (_clipStartTime == 0f) _clipStartTime = Time.time;

        AnalyzeSpectrum();
    }

    private void AnalyzeSpectrum()
    {
        _audioSource.GetSpectrumData(_spectrum, 0, FFTWindow.BlackmanHarris);

        float currentEnergy = CalculateBandEnergy(bassMin, bassMax) + CalculateBandEnergy(snareMin, snareMax);
        float flux = Mathf.Max(0f, currentEnergy - _previousEnergy);
        _previousEnergy = currentEnergy;

        _fluxHistory.Enqueue(flux);
        _fluxHistory.Dequeue(); 

        float averageFlux = 0f;
        foreach (float f in _fluxHistory) averageFlux += f;
        averageFlux /= _thresholdWindowSize;

        float dynamicThreshold = averageFlux * thresholdMultiplier;

        // NEW: Enforcing the strict absolute noise floor (minBeatStrength)
        if (flux > dynamicThreshold && flux > minBeatStrength) 
        {
            float timestamp = Time.time - _clipStartTime;
            RegisterRawBeat(timestamp, flux);
        }
    }

    private float CalculateBandEnergy(float minHz, float maxHz)
    {
        int minIndex = Mathf.Clamp(Mathf.FloorToInt(minHz / (_sampleRate / 2) * SAMPLE_SIZE), 0, SAMPLE_SIZE / 2);
        int maxIndex = Mathf.Clamp(Mathf.FloorToInt(maxHz / (_sampleRate / 2) * SAMPLE_SIZE), 0, SAMPLE_SIZE / 2);

        float energy = 0f;
        for (int i = minIndex; i <= maxIndex; i++) energy += _spectrum[i];
        return energy;
    }

    private void RegisterRawBeat(float time, float strength)
    {
        // 4-Second Silence Rule (Intro Phase)
        if (time < 4.0f) return;

        // NEW: Enforcing the vocal-friendly cooldown spacing
        if (_rawScrapedBeats.Count > 0 && time - _rawScrapedBeats[_rawScrapedBeats.Count - 1].time < beatCooldown) 
        {
            return;
        }

        _rawScrapedBeats.Add(new BeatData { time = time, strength = strength });
        
        Debug.Log($"<color=green>CHONKY BEAT LOCKED!</color> Time: {time.ToString("F2")}s | Strength: {strength.ToString("F3")}");
    }

    public List<float> GenerateActionTriggers(float listeningWindowStart, float windowDuration)
    {
        float windowEnd = listeningWindowStart + windowDuration;
        int maxActions = Mathf.FloorToInt(windowDuration / 2.0f); 

        List<BeatData> validBeats = new List<BeatData>();
        foreach (var b in _rawScrapedBeats)
        {
            if (b.time >= listeningWindowStart && b.time < windowEnd) validBeats.Add(b);
        }

        validBeats.Sort((a, b) => b.strength.CompareTo(a.strength));

        if (validBeats.Count > maxActions)
        {
            validBeats.RemoveRange(maxActions, validBeats.Count - maxActions);
        }

        validBeats.Sort((a, b) => a.time.CompareTo(b.time));

        List<float> finalizedActionTimestamps = new List<float>();
        foreach (var b in validBeats)
        {
            float futureHitTime = b.time + windowDuration;
            finalizedActionTimestamps.Add(futureHitTime);
        }

        return finalizedActionTimestamps;
    }
}