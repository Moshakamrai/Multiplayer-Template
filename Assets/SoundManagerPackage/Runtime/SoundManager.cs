using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class SoundManager : MonoBehaviour
{
    public static SoundManager Instance;

    [Header("Library")]
    public SoundLibrary soundLibrary;
    public SoundLibrary thunderLibrary;

    [Header("Global Volume")]
    [Range(0f, 1f)] public float masterVolume = 1f;
    [Range(0f, 1f)] public float musicVolume = 1f;
    [Range(0f, 1f)] public float sfxVolume = 1f;

    [Header("Thunder")]
    public float thunderIntervalSeconds = 10f;   // every ~10 seconds
    public Transform thunderWorldPosition ;

    public Light[] thunderLights; // Assign in inspector

    private List<AudioSource> audioPool = new List<AudioSource>();
    private bool thunderRoutineRunning = false;

    void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        soundLibrary.Init();
        if (thunderLibrary != null)
            thunderLibrary.Init();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.T))
        {
            PlayThunderInstant();
            Debug.Log("Played instant thunder!");
        }
    }

    // =======================
    // AUTO START THUNDER
    // =======================
    void Start()
    {
        StartRandomThunder(thunderWorldPosition.position);
    }

    AudioSource GetAudioSource()
    {
        foreach (var src in audioPool)
        {
            if (!src.isPlaying)
                return src;
        }

        AudioSource newSource = gameObject.AddComponent<AudioSource>();
        audioPool.Add(newSource);
        return newSource;
    }

    // =======================
    // PLAY SOUND (NORMAL)
    // =======================
    public void Play(string soundID, Vector3 position)
    {
        SoundData sound = soundLibrary.GetSound(soundID);
        if (sound == null)
        {
            Debug.LogWarning("Sound not found: " + soundID);
            return;
        }

        PlaySoundData(sound, position);
    }

    public void Stop(string soundID)
    {
        foreach (var src in audioPool)
        {
            if (src.clip != null && src.clip.name == soundID)
                src.Stop();
        }
    }

    // =======================
    // THUNDER PUBLIC API
    // =======================

    // Auto / manual loop
    public void StartRandomThunder(Vector3 position)
    {
        if (thunderLibrary == null || thunderRoutineRunning)
            return;

        thunderRoutineRunning = true;
        StartCoroutine(RandomThunderLoop());
    }

    public void StopRandomThunder()
    {
        thunderRoutineRunning = false;
    }

    // Instant thunder
    
    public void PlayThunderInstant()
    {
        SoundData thunder = thunderLibrary.GetSound("Thunder 7");
        StartCoroutine(ThunderWithDelay(thunder));
    }

private IEnumerator ThunderWithDelay(SoundData thunder)
{
    Debug.Log("Coroutine STARTED");
    PlayThunderLight(thunderLights);
    yield return new WaitForSeconds(0.35f);
    Debug.Log("Playing sound now");
    PlaySoundData(thunder, thunderWorldPosition.position);
}



    public void PlayThunder(Vector3 position)
    {
        if (thunderLibrary == null) return;

        SoundData thunder = GetRandomSoundFromLibrary(thunderLibrary);
        if (thunder == null) return;

        PlaySoundData(thunder, thunderWorldPosition.position);
    }

    // =======================
    // INTERNAL
    // =======================

    IEnumerator RandomThunderLoop()
    {
        while (thunderRoutineRunning)
        {
            yield return new WaitForSeconds(thunderIntervalSeconds);
            PlayThunder(thunderWorldPosition.position);
        }
    }

    private SoundData GetRandomSoundFromLibrary(SoundLibrary library)
    {
        var type = library.GetType();
        var field = type.GetField("sounds");

        if (field != null)
        {
            var list = field.GetValue(library) as IList<SoundData>;
            if (list != null && list.Count > 0)
                return list[Random.Range(0, list.Count)];
        }

        return null;
    }

    private void PlaySoundData(SoundData sound, Vector3 position)
    {
        AudioSource source = GetAudioSource();
        source.clip = sound.clip;
        source.volume = sound.volume * masterVolume * sfxVolume;
        source.pitch = sound.pitch;
        source.loop = sound.loop;

        if (sound.is3D)
        {
            source.spatialBlend = 1f;
            source.transform.position = position;
            source.minDistance = sound.minDistance;
            source.maxDistance = sound.maxDistance;
        }
        else
        {
            source.spatialBlend = 0f;
        }

        if (sound.playOneShot)
            source.PlayOneShot(sound.clip);
        else
            source.Play();
    }

    public void PlayThunderLight(Light[] thunderLights)
    {
        Debug.Log("PlayThunderLight HIT");
        StartCoroutine(ThunderLightRoutine(thunderLights));
    }

    private IEnumerator ThunderLightRoutine(Light[] lights)
    {
        Debug.Log("ThunderLightRoutine STARTED");
        float startIntensity = 500f;
        float duration = 1.5f;

        // Turn on + snap intensity
        foreach (var light in lights)
        {
            light.gameObject.SetActive(true);
            light.intensity = startIntensity;
        }

        float t = 0f;
        while (t < duration)
        {
            Debug.Log("ThunderLightRoutine running, t = " + t);
            t += Time.deltaTime;
            float value = Mathf.Lerp(startIntensity, 0f, t / duration);
            foreach (var light in lights)
                light.intensity = value;

            yield return null;
        }

        // Ensure fully off
        foreach (var light in lights)
            light.intensity = 0f;
    }


}
