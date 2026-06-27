using System.Collections.Generic;
using UnityEngine;

// Add this component to the player prefab.
// Assign BloodFX prefabs (Blood1–Blood15) and BloodAttach (AttachedBloodDecal) in the Inspector.
// No Mirror involvement — reads CurrentPercentage SyncVar which Mirror already syncs to all clients.
public class BloodOnHit : MonoBehaviour
{
    [Header("Blood FX Prefabs")]
    public GameObject[] BloodFX;
    public GameObject BloodAttach;
    public Light DirLight;

    [Header("Spawn Settings")]
    public float HeightOffset = 1.4f;
    public float EffectScale = 3.5f;

    [Header("Performance")]
    [Tooltip("Max blood bursts alive at once. Rapid hits (drone rush) recycle the oldest instead of " +
             "stacking dozens of volumetric effects (the main FPS drain).")]
    public int   maxLiveBursts = 2;
    [Tooltip("Minimum seconds between blood spawns — throttles a flurry of fast hits.")]
    public float minSpawnInterval = 0.2f;
    [Tooltip("How long each blood burst lives (was 20s — far too long when many spawn).")]
    public float burstLifetime = 3f;
    [Tooltip("How long each attached blood decal lives.")]
    public float decalLifetime = 4f;
    [Tooltip("Skip the attached decal entirely (the volumetric burst is the costly part; decals add up).")]
    public bool  spawnDecals = false;

    PlayerCombat _combat;
    float _lastPercentage;
    int _effectIdx;
    float _lastSpawnTime = -999f;
    readonly Queue<GameObject> _liveBursts = new Queue<GameObject>();

    void Start()
    {
        _combat = GetComponent<PlayerCombat>();
        _lastPercentage = _combat.CurrentPercentage;
    }

    void Update()
    {
        float current = _combat.CurrentPercentage;
        if (current > _lastPercentage && _lastPercentage >= 0f)
            SpawnBlood();
        _lastPercentage = current;
    }

    void SpawnBlood()
    {
        if (BloodFX == null || BloodFX.Length == 0) return;

        // Throttle: don't spawn a fresh burst if one just happened (a dense rush fires hits very fast).
        if (Time.unscaledTime - _lastSpawnTime < minSpawnInterval) return;
        _lastSpawnTime = Time.unscaledTime;

        // Cap concurrent bursts: recycle the oldest so they never pile up.
        while (_liveBursts.Count >= maxLiveBursts)
        {
            var old = _liveBursts.Dequeue();
            if (old != null) Destroy(old);
        }

        if (_effectIdx >= BloodFX.Length) _effectIdx = 0;
        Vector3 hitPos = transform.position + Vector3.up * HeightOffset;
        var burst = Instantiate(BloodFX[_effectIdx], hitPos, Quaternion.Euler(0, Random.Range(0f, 360f), 0));
        burst.transform.localScale = Vector3.one * EffectScale;
        _effectIdx++;

        var settings = burst.GetComponent<BFX_BloodSettings>();
        if (settings != null)
        {
            settings.DecalLiveTimeInfinite = false;
            if (DirLight != null) settings.LightIntensityMultiplier = DirLight.intensity;
        }

        // Slow down blood particles during hurt slow-mo
        var particles = burst.GetComponentsInChildren<ParticleSystem>();
        foreach (var ps in particles)
        {
            var main = ps.main;
            main.simulationSpeed = Time.timeScale;
        }

        _liveBursts.Enqueue(burst);
        Destroy(burst, burstLifetime);

        if (!spawnDecals || BloodAttach == null) return;

        var nearestBone = FindNearestBone(transform, hitPos);
        if (nearestBone == null) return;

        var decal = Instantiate(BloodAttach);
        decal.transform.position = hitPos;
        decal.transform.localRotation = Quaternion.identity;
        decal.transform.localScale = Vector3.one * (Random.Range(0.75f, 1.2f) * EffectScale);
        decal.transform.LookAt(hitPos + Vector3.up);
        decal.transform.Rotate(90, 0, 0);
        decal.transform.parent = nearestBone;
        Destroy(decal, decalLifetime);
    }

    // Cached bone list — built once, not re-walked on every hit (GetComponentsInChildren allocates).
    Transform[] _bones;
    Transform FindNearestBone(Transform root, Vector3 hitPos)
    {
        if (_bones == null) _bones = root.GetComponentsInChildren<Transform>();
        float closestDist = float.MaxValue;
        Transform closest = null;
        foreach (var child in _bones)
        {
            if (child == null) continue;
            float d = (child.position - hitPos).sqrMagnitude;
            if (d < closestDist) { closestDist = d; closest = child; }
        }
        return closest;
    }
}
