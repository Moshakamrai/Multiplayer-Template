using System.Collections.Generic;
using UnityEngine;
using System.Globalization;

[RequireComponent(typeof(AudioSource))]
public class BeatAnalyzer : MonoBehaviour
{
    public static BeatAnalyzer Instance;
    public AudioSource audioSource;

    [Header("FFT Settings")]
    private const int SAMPLE_SIZE = 1024;
    private float[] _spectrum = new float[SAMPLE_SIZE];
    private float _sampleRate;

    [Header("Band Isolation (Hz)")]
    public float bassMin = 20f;
    public float bassMax = 150f;
    public float snareMin = 2000f;
    public float snareMax = 5000f;

    [Header("Strict Filtering")]
    public float thresholdMultiplier = 3.0f; 
    public float minBeatStrength = 0.35f; 
    public float beatCooldown = 0.85f;

    private int _thresholdWindowSize = 50; 
    private Queue<float> _fluxHistory = new Queue<float>();
    private float _previousEnergy = 0f;
    
    public bool isAnalyzed = false;
    public bool isAnalyzing = false;

    public struct BeatData { public float time; public float strength; }
    private List<BeatData> _rawScrapedBeats = new List<BeatData>();

    void Awake() { if (Instance == null) Instance = this; }

    void Start()
    {
        audioSource = GetComponent<AudioSource>();
        _sampleRate = AudioSettings.outputSampleRate; 
        for (int i = 0; i < _thresholdWindowSize; i++) _fluxHistory.Enqueue(0f);

        // INSTANT LOAD: Check if we've analyzed this track before!
        LoadAnalysis();
    }

    public void StartAnalysis()
    {
        _rawScrapedBeats.Clear();
        isAnalyzing = true;
        isAnalyzed = false;
        
        audioSource.Stop();
        audioSource.time = 0f;
        audioSource.Play();
    }

    void Update()
    {
        if (isAnalyzing)
        {
            if (!audioSource.isPlaying)
            {
                isAnalyzing = false;
                isAnalyzed = true;
                Debug.Log($"<color=cyan>ANALYSIS COMPLETE:</color> Found {_rawScrapedBeats.Count} beats.");
                
                // SAVE THE DATA PERMANENTLY
                SaveAnalysis();
                return;
            }

            AnalyzeSpectrum();
        }
    }

    private void AnalyzeSpectrum()
    {
        audioSource.GetSpectrumData(_spectrum, 0, FFTWindow.BlackmanHarris);

        float currentEnergy = CalculateBandEnergy(bassMin, bassMax) + CalculateBandEnergy(snareMin, snareMax);
        float flux = Mathf.Max(0f, currentEnergy - _previousEnergy);
        _previousEnergy = currentEnergy;

        _fluxHistory.Enqueue(flux);
        _fluxHistory.Dequeue(); 

        float averageFlux = 0f;
        foreach (float f in _fluxHistory) averageFlux += f;
        averageFlux /= _thresholdWindowSize;

        float dynamicThreshold = averageFlux * thresholdMultiplier;

        if (flux > dynamicThreshold && flux > minBeatStrength) 
        {
            float timestamp = audioSource.time; 
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
        if (time < 4.0f) return;

        if (_rawScrapedBeats.Count > 0 && time - _rawScrapedBeats[_rawScrapedBeats.Count - 1].time < beatCooldown) 
            return;

        _rawScrapedBeats.Add(new BeatData { time = time, strength = strength });
    }

    public List<float> GetAllActionTriggers()
    {
        List<float> finalizedActionTimestamps = new List<float>();
        foreach (var b in _rawScrapedBeats) finalizedActionTimestamps.Add(b.time);
        return finalizedActionTimestamps;
    }

    // --- NEW: SAVE AND LOAD ARCHITECTURE ---

    private string GetSaveKey() 
    {
        // Creates a unique save file name based on the specific audio clip
        if (audioSource != null && audioSource.clip != null)
            return "BeatData_" + audioSource.clip.name;
        return "BeatData_Default";
    }

    private void SaveAnalysis()
    {
        string data = "";
        foreach (var b in _rawScrapedBeats)
        {
            // Format: time,strength|time,strength|time,strength
            data += b.time.ToString(CultureInfo.InvariantCulture) + "," + b.strength.ToString(CultureInfo.InvariantCulture) + "|";
        }
        
        PlayerPrefs.SetString(GetSaveKey(), data);
        PlayerPrefs.Save();
        Debug.Log($"<color=yellow>BEATMAP SAVED!</color> Track: {audioSource.clip.name}");
    }

    private bool LoadAnalysis()
    {
        string key = GetSaveKey();
        if (PlayerPrefs.HasKey(key))
        {
            string data = PlayerPrefs.GetString(key);
            if (!string.IsNullOrEmpty(data))
            {
                _rawScrapedBeats.Clear();
                
                // Unpack the string back into the BeatData struct
                string[] entries = data.Split(new char[] { '|' }, System.StringSplitOptions.RemoveEmptyEntries);
                foreach (string entry in entries)
                {
                    string[] parts = entry.Split(',');
                    if (parts.Length == 2)
                    {
                        float t = float.Parse(parts[0], CultureInfo.InvariantCulture);
                        float s = float.Parse(parts[1], CultureInfo.InvariantCulture);
                        _rawScrapedBeats.Add(new BeatData { time = t, strength = s });
                    }
                }
                
                isAnalyzed = true;
                Debug.Log($"<color=green>BEATMAP LOADED FROM DISK!</color> Track: {audioSource.clip.name} | Beats: {_rawScrapedBeats.Count}");
                return true;
            }
        }
        return false;
    }
}