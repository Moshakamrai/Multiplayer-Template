using UnityEngine;

public class SoundManagerMain : MonoBehaviour
{
    public static SoundManagerMain Instance;
    public AudioSource audioSource;
    public AudioClip cardAcceptedClip;

    public AudioClip cardRejectedClip;

    [Header("Combat SFX")]
    public AudioClip attackClip, blockClip, parryClip, dashClip, hurtClip;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    public void PlayCardAccepted()
    {
        PlayWithPitch(cardAcceptedClip, Random.Range(0.95f, 1.05f));
    }

    private void PlayWithPitch(AudioClip clip, float pitch)
    {
        if (clip == null) return;
        GameObject go = new GameObject("SFX_OneShot");
        AudioSource src = go.AddComponent<AudioSource>();
        src.clip = clip;
        src.pitch = pitch;
        src.spatialBlend = 0f;
        src.volume = audioSource != null ? audioSource.volume : 1f;
        src.Play();
        Destroy(go, clip.length / Mathf.Abs(pitch) + 0.1f);
    }

    // Inside SoundManager.cs
   

    public void PlayCardRejected()
    {
        if (audioSource != null && cardRejectedClip != null)
        {
            audioSource.PlayOneShot(cardRejectedClip);
        }
    }

    public void PlaySuccessSFX(string type)
    {
        AudioClip clip = null;
        if (type == "Attack") clip = attackClip;
        else if (type == "Block") clip = blockClip;
        else if (type == "Parry") clip = parryClip;
        else if (type == "Dash") clip = dashClip;
        else if (type == "Hurt") clip = attackClip;

        PlayWithPitch(clip, Random.Range(0.92f, 1.08f));
    }
}