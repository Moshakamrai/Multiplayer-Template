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
    public float EffectScale = 2.0f;

    PlayerCombat _combat;
    float _lastPercentage;
    int _effectIdx;

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

        Destroy(burst, 20f);

        if (BloodAttach == null) return;

        var nearestBone = FindNearestBone(transform, hitPos);
        if (nearestBone == null) return;

        var decal = Instantiate(BloodAttach);
        decal.transform.position = hitPos;
        decal.transform.localRotation = Quaternion.identity;
        decal.transform.localScale = Vector3.one * (Random.Range(0.75f, 1.2f) * EffectScale);
        decal.transform.LookAt(hitPos + Vector3.up);
        decal.transform.Rotate(90, 0, 0);
        decal.transform.parent = nearestBone;
        Destroy(decal, 20f);
    }

    Transform FindNearestBone(Transform root, Vector3 hitPos)
    {
        float closestDist = float.MaxValue;
        Transform closest = null;
        foreach (var child in root.GetComponentsInChildren<Transform>())
        {
            float d = Vector3.Distance(child.position, hitPos);
            if (d < closestDist) { closestDist = d; closest = child; }
        }
        return closest;
    }
}
