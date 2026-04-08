using UnityEngine;

public class SoundManagerMain : MonoBehaviour
{
    public static SoundManagerMain Instance;
    public AudioSource audioSource;
    public AudioClip cardAcceptedClip;

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
    public AudioClip cardRejectedClip;

    public void PlayCardRejected()
    {
        if (audioSource != null && cardRejectedClip != null)
        {
            audioSource.PlayOneShot(cardRejectedClip);
        }
    }
}