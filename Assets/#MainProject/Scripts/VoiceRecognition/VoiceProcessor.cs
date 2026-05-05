using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class VoiceProcessor : MonoBehaviour
{
    public bool IsRecording
    {
        get { return _audioClip != null && Microphone.IsRecording(CurrentDeviceName); }
    }

    [SerializeField] private int MicrophoneIndex;
    public int SampleRate { get; private set; }       // target rate Vosk expects (always 16000)
    public int ActualSampleRate { get; private set; } // rate the mic physically records at
    public int MinDeviceCaps { get; private set; }
    public int MaxDeviceCaps { get; private set; }
    public bool IsResampling => ActualSampleRate != 0 && ActualSampleRate != SampleRate;

    public Text debugText;
    public int FrameLength { get; private set; }      // output frame size in target-rate samples

    public event Action<short[]> OnFrameCaptured;
    public event Action OnRecordingStop;
    public event Action OnRecordingStart;

    public List<string> Devices { get; private set; }
    public int CurrentDeviceIndex { get; private set; }
    public float CurrentRawVolume { get; private set; }

    public string CurrentDeviceName
    {
        get
        {
            if (CurrentDeviceIndex < 0 || CurrentDeviceIndex >= Microphone.devices.Length)
                return string.Empty;
            return Devices[CurrentDeviceIndex];
        }
    }

    [Header("Voice Detection Settings")]
    [SerializeField, Range(0.0f, 1.0f)] private float _minimumSpeakingSampleValue = 0.05f;
    [Tooltip("How long silence must persist before audio transmission stops. Keep low for rhythm games (0.2–0.3s).")]
    [SerializeField] private float _silenceTimer = 0.25f;
    [SerializeField] private bool _autoDetect;

    private const int PEEK_SIZE = 128;
    private readonly float[] _peekBuffer = new float[PEEK_SIZE];

    // Capture frame length may be larger than FrameLength when mic rate > target rate.
    // e.g. mic at 48000, target 16000, FrameLength 256 → _captureFrameLength = 768
    private int _captureFrameLength;
    private float[] _resampledBuffer;  // scratch buffer; only allocated when resampling

    private float _timeAtSilenceBegan;
    private bool _audioDetected;
    private bool _didDetect;
    private bool _transmit;

    AudioClip _audioClip;
    private event Action RestartRecording;

    void Awake() => UpdateDevices();

#if UNITY_EDITOR
    void Update()
    {
        if (CurrentDeviceIndex != MicrophoneIndex) ChangeDevice(MicrophoneIndex);
    }
#endif

    public void UpdateDevices()
    {
        Devices = new List<string>();
        foreach (var device in Microphone.devices) Devices.Add(device);
        if (Devices.Count == 0) { CurrentDeviceIndex = -1; return; }
        CurrentDeviceIndex = MicrophoneIndex;
    }

    public void ChangeDevice(int deviceIndex)
    {
        if (deviceIndex < 0 || deviceIndex >= Devices.Count) return;
        if (IsRecording)
        {
            RestartRecording += () => { CurrentDeviceIndex = deviceIndex; StartRecording(SampleRate, FrameLength); RestartRecording = null; };
            StopRecording();
        }
        else CurrentDeviceIndex = deviceIndex;
    }

    public void StartRecording(int sampleRate = 16000, int frameSize = 512, bool? autoDetect = null)
    {
        if (autoDetect != null) _autoDetect = (bool)autoDetect;
        if (IsRecording)
        {
            if (sampleRate != SampleRate || frameSize != FrameLength)
            {
                RestartRecording += () => { StartRecording(SampleRate, FrameLength, autoDetect); RestartRecording = null; };
                StopRecording();
            }
            return;
        }

        SampleRate  = sampleRate;
        FrameLength = frameSize;

        // --- SAMPLE RATE DETECTION ---
        // GetDeviceCaps returns (0, 0) when the device accepts any rate — trust the request.
        // Otherwise, clamp to the device's supported range.
        Microphone.GetDeviceCaps(CurrentDeviceName, out int minFreq, out int maxFreq);
        MinDeviceCaps = minFreq;
        MaxDeviceCaps = maxFreq;

        if (minFreq == 0 && maxFreq == 0)
            ActualSampleRate = sampleRate;          // device accepts any rate
        else if (sampleRate >= minFreq && sampleRate <= maxFreq)
            ActualSampleRate = sampleRate;          // device natively supports the target rate
        else if (sampleRate < minFreq)
            ActualSampleRate = minFreq;             // must record higher; will downsample
        else
            ActualSampleRate = maxFreq;             // must record lower (unusual)

        // Number of mic samples that correspond to one output frame at the target rate.
        // e.g. FrameLength=256 at 16000 Hz = 16 ms; at 48000 Hz that same 16 ms = 768 samples.
        _captureFrameLength = (ActualSampleRate == SampleRate)
            ? FrameLength
            : Mathf.RoundToInt((float)FrameLength * ActualSampleRate / SampleRate);

        if (IsResampling)
            _resampledBuffer = new float[FrameLength];

        Debug.Log($"[VoiceProcessor] Device='{CurrentDeviceName}' caps=({minFreq}-{maxFreq}) Hz  " +
                  $"Requested={sampleRate} Hz  Actual={ActualSampleRate} Hz  " +
                  $"Resample={IsResampling}  captureFrame={_captureFrameLength}");

        _audioClip = Microphone.Start(CurrentDeviceName, true, 1, ActualSampleRate);
        StartCoroutine(RecordData());
    }

    public void StopRecording()
    {
        if (!IsRecording) return;
        Microphone.End(CurrentDeviceName);
        Destroy(_audioClip); _audioClip = null; _didDetect = false;
        StopCoroutine(RecordData());
    }

    IEnumerator RecordData()
    {
        float[] sampleBuffer = new float[_captureFrameLength];
        int startReadPos = 0;
        if (OnRecordingStart != null) OnRecordingStart.Invoke();

        while (IsRecording)
        {
            int curClipPos = Microphone.GetPosition(CurrentDeviceName);
            if (curClipPos < startReadPos) curClipPos += _audioClip.samples;

            int samplesAvailable = curClipPos - startReadPos;

            // Fast volume peek: read a tiny slice every frame so CurrentRawVolume
            // is never stale by more than ~16ms, regardless of Vosk frame size.
            if (samplesAvailable > 0)
            {
                int peekLen = Mathf.Min(PEEK_SIZE, samplesAvailable);
                _audioClip.GetData(_peekBuffer, startReadPos % _audioClip.samples);
                float peekMax = 0f;
                for (int i = 0; i < peekLen; i++)
                {
                    float a = Mathf.Abs(_peekBuffer[i]);
                    if (a > peekMax) peekMax = a;
                }
                CurrentRawVolume = peekMax;
            }

            // Wait until we have a full capture frame
            if (samplesAvailable < _captureFrameLength) { yield return null; continue; }

            int endReadPos = startReadPos + _captureFrameLength;
            if (endReadPos > _audioClip.samples)
            {
                int numSamplesClipEnd   = _audioClip.samples - startReadPos;
                int numSamplesClipStart = endReadPos - _audioClip.samples;
                float[] endClipSamples   = new float[numSamplesClipEnd];
                float[] startClipSamples = new float[numSamplesClipStart];
                _audioClip.GetData(endClipSamples, startReadPos);
                _audioClip.GetData(startClipSamples, 0);
                Buffer.BlockCopy(endClipSamples,   0, sampleBuffer, 0,                      numSamplesClipEnd * 4);
                Buffer.BlockCopy(startClipSamples, 0, sampleBuffer, numSamplesClipEnd * 4,  numSamplesClipStart * 4);
            }
            else _audioClip.GetData(sampleBuffer, startReadPos);

            startReadPos = endReadPos % _audioClip.samples;

            // Full-frame peak for VAD decision
            float maxVolume = 0.0f;
            for (int i = 0; i < sampleBuffer.Length; i++)
            {
                float absVal = Mathf.Abs(sampleBuffer[i]);
                if (absVal > maxVolume) maxVolume = absVal;
            }
            if (maxVolume > CurrentRawVolume) CurrentRawVolume = maxVolume;

            if (_autoDetect == false)
            {
                _transmit = _audioDetected = true;
            }
            else
            {
                if (maxVolume >= _minimumSpeakingSampleValue)
                {
                    _audioDetected = true;
                    _timeAtSilenceBegan = Time.time;
                }
                else
                {
                    if (_audioDetected && Time.time - _timeAtSilenceBegan > _silenceTimer)
                        _audioDetected = false;
                }
                _transmit = _audioDetected;
            }

            if (_audioDetected)
            {
                _didDetect = true;

                // --- RESAMPLE if mic rate differs from Vosk target rate ---
                float[] outputBuffer = sampleBuffer;
                int outputLen = _captureFrameLength;
                if (IsResampling)
                {
                    ResampleLinear(sampleBuffer, _captureFrameLength, _resampledBuffer, FrameLength);
                    outputBuffer = _resampledBuffer;
                    outputLen = FrameLength;
                }

                short[] pcmBuffer = new short[outputLen];
                for (int i = 0; i < outputLen; i++)
                    pcmBuffer[i] = (short)Math.Floor(outputBuffer[i] * short.MaxValue);

                if (OnFrameCaptured != null && _transmit) OnFrameCaptured.Invoke(pcmBuffer);
            }
            else if (_didDetect) { if (OnRecordingStop != null) OnRecordingStop.Invoke(); _didDetect = false; }
        }

        if (OnRecordingStop != null) OnRecordingStop.Invoke();
        if (RestartRecording != null) RestartRecording.Invoke();
    }

    // Linear interpolation resampler — works for any integer or fractional ratio.
    private static void ResampleLinear(float[] input, int inputLen, float[] output, int outputLen)
    {
        if (inputLen == outputLen) { Array.Copy(input, output, inputLen); return; }
        float step = (float)(inputLen - 1) / Mathf.Max(outputLen - 1, 1);
        for (int i = 0; i < outputLen; i++)
        {
            float pos = i * step;
            int lo = (int)pos;
            int hi = Mathf.Min(lo + 1, inputLen - 1);
            float t = pos - lo;
            output[i] = input[lo] * (1f - t) + input[hi] * t;
        }
    }
}
