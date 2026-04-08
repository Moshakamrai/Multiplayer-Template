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
        if (audioSource != null && cardAcceptedClip != null)
        {
            audioSource.PlayOneShot(cardAcceptedClip);
        }
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
        else if (type == "Hurt") clip = hurtClip; // New case for taking damage

        if (audioSource != null && clip != null)
            audioSource.PlayOneShot(clip);
    }
}