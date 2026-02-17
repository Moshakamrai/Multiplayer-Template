using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(menuName = "Audio/Sound Library")]
public class SoundLibrary : ScriptableObject
{
    public List<SoundData> sounds;

    private Dictionary<string, SoundData> soundMap;

    public void Init()
    {
        soundMap = new Dictionary<string, SoundData>();

        foreach (var sound in sounds)
        {
            if (!soundMap.ContainsKey(sound.soundID))
                soundMap.Add(sound.soundID, sound);
        }
    }

    public SoundData GetSound(string id)
    {
        if (soundMap == null) Init();
        return soundMap.ContainsKey(id) ? soundMap[id] : null;
    }
}
