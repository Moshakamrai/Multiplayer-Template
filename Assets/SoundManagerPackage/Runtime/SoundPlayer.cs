using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SoundPlayer : MonoBehaviour
{
    public Transform BG_SoundPos;

    Dictionary<string, AudioSource> activeSounds;

    // Start is called before the first frame update
    void Start()
    {
        //StartCoroutine(PlaySoundBackground(1f));
    }

    // Update is called once per frame
    void Update()
    {

    }

    void PlaySoundBackground(float delay)
    {
        
        SoundManager.Instance.Play("BG01", BG_SoundPos.position);

    }

    public void PlaySound( string soundID , Transform soundPos)
    {
       
        SoundManager.Instance.Play(soundID, soundPos.position);

    }

    public void Stop(string soundID)
{
    if (activeSounds.TryGetValue(soundID, out AudioSource source))
    {
        source.Stop();
        activeSounds.Remove(soundID);
    }
}



}
