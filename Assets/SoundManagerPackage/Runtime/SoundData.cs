using UnityEngine;

[CreateAssetMenu(menuName = "Audio/Sound Data")]
public class SoundData : ScriptableObject
{
    [Header("Identification")]
    public string soundID;

    [Header("Audio")]
    public AudioClip clip;

    [Range(0f, 1f)] public float volume = 1f;
    [Range(0.1f, 3f)] public float pitch = 1f;

    public bool loop;
    public bool playOneShot;

    [Header("3D Settings")]
    public bool is3D;
    public float minDistance = 1f;
    public float maxDistance = 15f;
}
