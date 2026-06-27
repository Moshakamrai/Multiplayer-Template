using System.Collections;
using UnityEngine;

/// POWER SURGE — the PC replacement for the VR drone-rush "reward" segment.
///
/// On a dense beat run (handed in by RhythmRoundManager, same trigger the drones used), the screen goes
/// electric, the music swells, and for a few seconds EVERY point the player scores is DOUBLED. No new
/// input is demanded — you just keep playing the beats and the numbers explode. It's a pure "you earned
/// a hot streak" rush: rewarding like the drones, but a REST from the voice workload, not more of it.
///
/// Plain MonoBehaviour (not networked): this game runs as HOST (server+client in one process), and all
/// scoring is server-side, so the local player reads SurgeActive/Multiplier directly — no SyncVar/RPC
/// needed, and no risk of an un-spawned NetworkBehaviour. Self-bootstraps; nothing to place in a scene.
[DefaultExecutionOrder(10005)]
public class PowerSurge : MonoBehaviour
{
    public static PowerSurge Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject("~PowerSurge");
        DontDestroyOnLoad(go);
        go.AddComponent<PowerSurge>();
    }

    [Header("Tuning")]
    [Tooltip("Seconds a surge lasts.")]
    public float surgeDuration = 5f;
    [Tooltip("Score multiplier while the surge is active.")]
    public int multiplier = 2;
    [Tooltip("Optional electric/aura VFX prefab spawned at screen-front during the surge (null = just the tint).")]
    public GameObject surgeVfxPrefab;
    [Tooltip("Optional swell/charge sound played when the surge begins.")]
    public AudioClip surgeSound;

    // Read by PlayerCombat.AddScore.
    public bool SurgeActive { get; private set; }
    public int  Multiplier  { get; private set; } = 1;

    private bool _running;
    private float _bannerFade;
    private AudioSource _sfx;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    /// Start a surge (ignored if one is already running). Called by RhythmRoundManager on a dense beat
    /// run — the same hook the drone segment used.
    public void StartSurge()
    {
        if (_running) return;
        var rmm = RhythmRoundManager.Instance;
        if (rmm == null || !rmm.isRoundActive) return;
        StartCoroutine(RunSurge());
    }

    private IEnumerator RunSurge()
    {
        _running = true;
        SurgeActive = true;
        Multiplier  = Mathf.Max(2, multiplier);
        _bannerFade = 1f;
        PlayBeginFx();

        yield return new WaitForSeconds(surgeDuration);

        SurgeActive = false;
        Multiplier  = 1;
        _running = false;
    }

    private void PlayBeginFx()
    {
        if (surgeSound != null)
        {
            if (_sfx == null) { _sfx = gameObject.AddComponent<AudioSource>(); _sfx.spatialBlend = 0f; }
            _sfx.PlayOneShot(surgeSound, 0.9f);
        }
        if (surgeVfxPrefab != null)
        {
            Camera cam = CachedCamera.Main;
            Vector3 pos = cam != null ? cam.transform.position + cam.transform.forward * 3f : Vector3.zero;
            var fx = Instantiate(surgeVfxPrefab, pos, Quaternion.identity);
            Destroy(fx, surgeDuration + 0.5f);
        }
    }

    // ── On-screen feedback: an electric edge tint + a "POWER SURGE ×N — DOUBLE POINTS" banner ──
    private void OnGUI()
    {
        if (!SurgeActive && _bannerFade <= 0f) return;
        if (SurgeActive) _bannerFade = 1f; else _bannerFade -= Time.unscaledDeltaTime * 1.5f;
        float a = Mathf.Clamp01(_bannerFade);

        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 8f);
        GUI.color = new Color(0.2f, 0.85f, 1f, 0.12f * a * pulse);
        var tex = Texture2D.whiteTexture;
        GUI.DrawTexture(new Rect(0, 0, Screen.width, 16), tex);
        GUI.DrawTexture(new Rect(0, Screen.height - 16, Screen.width, 16), tex);
        GUI.DrawTexture(new Rect(0, 0, 16, Screen.height), tex);
        GUI.DrawTexture(new Rect(Screen.width - 16, 0, 16, Screen.height), tex);

        var style = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = Mathf.RoundToInt(Screen.height * 0.045f),
            fontStyle = FontStyle.Bold
        };
        style.normal.textColor = new Color(0.3f, 1f, 1f, a);
        GUI.color = Color.white;
        GUI.Label(new Rect(0, Screen.height * 0.12f, Screen.width, Screen.height * 0.1f),
                  $"POWER SURGE  x{Mathf.Max(2, multiplier)}  -  DOUBLE POINTS", style);
    }
}
