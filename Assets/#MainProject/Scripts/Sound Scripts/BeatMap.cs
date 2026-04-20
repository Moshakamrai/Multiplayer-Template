using System.Collections.Generic;

[System.Serializable]
public class Beat
{
    public float time;
    public float strength;
    public int   chainIndex;
}

[System.Serializable]
public class BeatChain
{
    public List<Beat> beats = new List<Beat>();

    // Voice input window — formula: 1.5 × chainCount × beatInterval
    // floored by: chainCount × minWindowPerCommand
    public float inputWindowStart;
    public float inputWindowEnd;
    public float inputWindowDuration;

    public int   chainLength   => beats.Count;
    public float FirstBeatTime => beats.Count > 0 ? beats[0].time                  : 0f;
    public float LastBeatTime  => beats.Count > 0 ? beats[beats.Count - 1].time    : 0f;

    public bool WindowContains(float songTime) =>
        songTime >= inputWindowStart && songTime <= inputWindowEnd;
}

public class BeatMap
{
    public float           bpm;
    public float           beatInterval;
    public string          songName;
    public float           songLength;
    public List<Beat>      allBeats = new List<Beat>();
    public List<BeatChain> chains   = new List<BeatChain>();

    public int TotalBeatCount  => allBeats.Count;
    public int TotalChainCount => chains.Count;

    public BeatChain ActiveChainAt(float songTime)
    {
        foreach (var c in chains)
            if (c.WindowContains(songTime)) return c;
        return null;
    }
}
