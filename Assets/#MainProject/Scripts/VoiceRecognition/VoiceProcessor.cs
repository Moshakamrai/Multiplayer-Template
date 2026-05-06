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
    public int SampleRate { get; private set; }
    public Text debugText; 
    public int FrameLength { get; private set; }

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

    // Fast volume peek — reads a tiny window every Unity frame so CurrentRawVolume
    // is never more than one frame stale (~16ms), instead of one Vosk frame (32ms).
    private const int PEEK_SIZE = 128;
    private readonly float[] _peekBuffer = new float[PEEK_SIZE];

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
        SampleRate = sampleRate; FrameLength = frameSize;
        _audioClip = Microphone.Start(CurrentDeviceName, true, 1, sampleRate);
        StartCoroutine(RecordData());
    }

    public void StopRecording()
    {
        if (!IsRecording) return;
        Microphone.End(CurrentDeviceName);
        Destroy(_audioClip); _audioClip = null; _didDetect = false;
        StopCoroutine(RecordData());
        // If there's a pending device restart, fire it now since the coroutine won't exit naturally
        if (RestartRecording != null)
        {
            var callback = RestartRecording;
            RestartRecording = null;
            callback.Invoke();
        }
    }

    IEnumerator RecordData()
    {
        float[] sampleBuffer = new float[FrameLength];
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

            if (samplesAvailable < FrameLength) { yield return null; continue; }

            int endReadPos = startReadPos + FrameLength;
            if (endReadPos > _audioClip.samples)
            {
                int numSamplesClipEnd = _audioClip.samples - startReadPos;
                float[] endClipSamples = new float[numSamplesClipEnd];
                _audioClip.GetData(endClipSamples, startReadPos);

                int numSamplesClipStart = endReadPos - _audioClip.samples;
                float[] startClipSamples = new float[numSamplesClipStart];
                _audioClip.GetData(startClipSamples, 0);

                Buffer.BlockCopy(endClipSamples, 0, sampleBuffer, 0, numSamplesClipEnd * 4);
                Buffer.BlockCopy(startClipSamples, 0, sampleBuffer, numSamplesClipEnd * 4, numSamplesClipStart * 4);
            }
            else _audioClip.GetData(sampleBuffer, startReadPos);

            startReadPos = endReadPos % _audioClip.samples;
            
            // Full-frame peak for VAD decision (CurrentRawVolume already updated by fast peek above)
            float maxVolume = 0.0f;
            for (int i = 0; i < sampleBuffer.Length; i++)
            {
                float absVal = Mathf.Abs(sampleBuffer[i]);
                if (absVal > maxVolume) maxVolume = absVal;
            }
            // Keep CurrentRawVolume as the freshest value (peek may be more recent than full frame)
            if (maxVolume > CurrentRawVolume) CurrentRawVolume = maxVolume;

            // --- THE VAD FIX ---
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
                    // If we drop below the volume threshold, check if the silence timer has expired
                    if (_audioDetected && Time.time - _timeAtSilenceBegan > _silenceTimer) 
                    {
                        _audioDetected = false; 
                    }
                }
                
                // Keep transmitting as long as audio is considered "detected" (including the silence tail)
                _transmit = _audioDetected; 
            }

            if (_audioDetected)
            {
                _didDetect = true;
                short[] pcmBuffer = new short[sampleBuffer.Length];
                for (int i = 0; i < FrameLength; i++) pcmBuffer[i] = (short)Math.Floor(sampleBuffer[i] * short.MaxValue);
                if (OnFrameCaptured != null && _transmit) OnFrameCaptured.Invoke(pcmBuffer);
            }
            else if (_didDetect) { if (OnRecordingStop != null) OnRecordingStop.Invoke(); _didDetect = false; }
        }

        if (OnRecordingStop != null) OnRecordingStop.Invoke();
        if (RestartRecording != null) RestartRecording.Invoke();
    }
}