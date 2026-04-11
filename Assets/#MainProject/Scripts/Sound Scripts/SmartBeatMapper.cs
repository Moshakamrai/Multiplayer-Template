using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class SmartBeatMapper : MonoBehaviour
{
    public AudioSource audioSource;
    
    [Header("Quantize Settings (FL Studio Style)")]
    public float targetBPM = 100f; // The BPM of your track
    
    [Tooltip("1 = Full Beats (0.6s), 2 = Half Beats (0.3s), 4 = 16th Notes")]
    public int quantizeDivisor = 2; // We use 2 so it catches those fast 0.3s hits you liked!

    private List<float> _rawTaps = new List<float>();
    private List<float> _quantizedBeats = new List<float>();
    
    private bool _isRecording = false;

    void Update()
    {
        if (!_isRecording || audioSource == null || !audioSource.isPlaying) return;

        // 1. RECORD HUMAN INPUT
        if (Input.GetKeyDown(KeyCode.Space))
        {
            float currentTime = audioSource.time;
            _rawTaps.Add(currentTime);
            Debug.Log($"Raw Tap: {currentTime:F3}s");
        }
    }

    // 2. THE FL STUDIO MAGIC (Snap to Grid)
    // 2. RAW TAP BYPASS (Quantization Disabled)
    // 2. THE FL STUDIO MAGIC (Snap to Grid)
    private void QuantizeTaps()
    {
        _quantizedBeats.Clear();
        if (_rawTaps.Count == 0) return;

        // Calculate the exact time (in seconds) between grid lines
        // 100 BPM = 0.6s per beat. Divisor of 2 = 0.3s grid lines.
        float beatInterval = 60f / targetBPM; 
        float snapGrid = beatInterval / quantizeDivisor;

        foreach (float tap in _rawTaps)
        {
            // Mathematically round the human tap to the nearest perfect grid line
            float snappedTime = Mathf.Round(tap / snapGrid) * snapGrid;
            
            // Prevent two messy taps from snapping to the exact same beat
            if (!_quantizedBeats.Contains(snappedTime))
            {
                _quantizedBeats.Add(snappedTime);
            }
        }
        
        _quantizedBeats.Sort(); // Ensure they are in chronological order
        Debug.Log($"<color=cyan>QUANTIZED:</color> Snapped {_rawTaps.Count} raw taps to the perfect {targetBPM} BPM grid. Saved {_quantizedBeats.Count} beats.");
    }

    // 3. THE UI WORKFLOW
    private void OnGUI()
    {
        if (audioSource == null || audioSource.clip == null) return;

        GUILayout.BeginArea(new Rect(Screen.width / 2 - 200, 50, 400, 200));
        GUIStyle title = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 22, fontStyle = FontStyle.Bold };
        title.normal.textColor = Color.yellow;
        GUILayout.Label("SMART QUANTIZE MAPPER", title);
        
        GUILayout.Label($"Target BPM: {targetBPM} | Grid Snap: 1/{quantizeDivisor} Beat", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter });
        GUILayout.Space(10);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button(_isRecording ? "STOP RECORDING" : "START TAP RECORD", GUILayout.Height(50)))
        {
            _isRecording = !_isRecording;
            if (_isRecording)
            {
                _rawTaps.Clear();
                _quantizedBeats.Clear();
                audioSource.time = 0f;
                audioSource.Play();
            }
            else
            {
                audioSource.Stop();
                QuantizeTaps(); // Automatically fix the beats when you stop!
            }
        }
        GUILayout.EndHorizontal();

        if (!_isRecording && _quantizedBeats.Count > 0)
        {
            GUI.color = Color.green;
            if (GUILayout.Button($"SAVE {_quantizedBeats.Count} QUANTIZED BEATS", GUILayout.Height(50)))
            {
                SaveCustomTrack();
            }
            GUI.color = Color.white;
        }

        GUILayout.Label("Press SPACEBAR to the rhythm. The system will auto-correct your timing.", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter });
        GUILayout.EndArea();
    }

    private void SaveCustomTrack()
    {
        string data = string.Join("|", _quantizedBeats);
        string saveKey = "CustomMap_" + audioSource.clip.name;
        PlayerPrefs.SetString(saveKey, data);
        PlayerPrefs.Save();
        Debug.Log($"<color=green>SAVED PERFECT MAP:</color> {saveKey}");
    }
}