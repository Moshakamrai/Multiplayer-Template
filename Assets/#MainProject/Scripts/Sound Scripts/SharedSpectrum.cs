using UnityEngine;

/// One FFT per frame, shared by every visualizer.
///
/// Each beat visualizer used to call AudioSource.GetSpectrumData(512, BlackmanHarris) in its OWN
/// Update — so two or three visualizers on screen meant two or three full FFTs every frame (the
/// BlackmanHarris window is the most expensive one). This provider performs the FFT a SINGLE time
/// per frame for a given AudioSource and hands the same buffer to every caller, so N visualizers
/// cost one FFT instead of N. No visual change — the data is identical to what each used to compute.
///
/// Usage: float[] spec = SharedSpectrum.Get(myAudioSource);  // call once per Update; cheap after the
/// first call in a frame. Returns null if the source is null.
[DefaultExecutionOrder(-100)] // resolve before visualizers read it
public class SharedSpectrum : MonoBehaviour
{
    public const int SIZE = 512;

    private static SharedSpectrum _instance;
    private readonly float[] _spectrum = new float[SIZE];
    private AudioSource _source;
    private int _frameComputed = -1; // Time.frameCount the buffer was last filled for _source

    private static SharedSpectrum Instance
    {
        get
        {
            if (_instance == null)
            {
                var go = new GameObject("~SharedSpectrum");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<SharedSpectrum>();
            }
            return _instance;
        }
    }

    /// Returns the spectrum for `source`, computing the FFT at most once per frame. Subsequent calls
    /// in the same frame (or for the same source) return the cached buffer with no extra FFT.
    public static float[] Get(AudioSource source)
    {
        if (source == null) return null;
        var inst = Instance;

        // Recompute only when the frame changed or the requesting source changed. If two different
        // sources ask in one frame we still serve one FFT each (source switch forces a refresh), but
        // in practice all visualizers point at the same music source, so it's one FFT total.
        if (inst._frameComputed != Time.frameCount || inst._source != source)
        {
            inst._source = source;
            inst._frameComputed = Time.frameCount;
            if (source.isPlaying)
                source.GetSpectrumData(inst._spectrum, 0, FFTWindow.BlackmanHarris);
            else
                System.Array.Clear(inst._spectrum, 0, SIZE);
        }
        return inst._spectrum;
    }
}
