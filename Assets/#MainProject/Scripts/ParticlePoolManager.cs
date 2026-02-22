using System.Collections.Generic;
using UnityEngine;

public class ParticlePoolManager : MonoBehaviour
{
    // Singleton instance so any script can call it instantly
    public static ParticlePoolManager Instance;

    // CHANGED: From 'struct' to 'class' so the Unity Inspector always shows it
    [System.Serializable]
    public class ParticlePool
    {
        public string ParticleName;      // The name you will call from other scripts (e.g., "Blood", "Sparks")
        public ParticleSystem Prefab;    // The actual particle prefab
        public int PoolSize;             // How many to keep loaded in memory
    }

    [Header("Particle Pools Setup")]
    public List<ParticlePool> Pools;

    // Dictionary for ultra-fast lookups using the string name
    private Dictionary<string, Queue<ParticleSystem>> _poolDictionary;

    private void Awake()
    {
        // 1. Singleton & DontDestroyOnLoad Setup
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject); // Persist across scene loads!
        }
        else
        {
            Destroy(gameObject); // Prevent duplicates if you reload the Lobby
            return;
        }

        // 2. Initialize the Pools
        _poolDictionary = new Dictionary<string, Queue<ParticleSystem>>();

        foreach (ParticlePool pool in Pools)
        {
            Queue<ParticleSystem> objectPool = new Queue<ParticleSystem>();

            for (int i = 0; i < pool.PoolSize; i++)
            {
                // Instantiate as a child of this manager to keep the hierarchy clean
                ParticleSystem obj = Instantiate(pool.Prefab, transform);
                obj.gameObject.SetActive(false);
                objectPool.Enqueue(obj);
            }

            _poolDictionary.Add(pool.ParticleName, objectPool);
        }
    }

    /// <summary>
    /// Plays a particle from the pool at the given position.
    /// </summary>
    public void PlayParticle(string particleName, Vector3 position, Quaternion rotation = default)
    {
        if (!_poolDictionary.ContainsKey(particleName))
        {
            Debug.LogWarning($"[ParticlePool] Pool with name '{particleName}' doesn't exist!");
            return;
        }

        // Pull the oldest particle from the front of the queue
        ParticleSystem particleToSpawn = _poolDictionary[particleName].Dequeue();

        // Setup and Play
        particleToSpawn.gameObject.SetActive(true);
        particleToSpawn.transform.position = position;
        
        // Use provided rotation, or default identity
        particleToSpawn.transform.rotation = rotation == default ? Quaternion.identity : rotation;
        
        particleToSpawn.Play();

        // Immediately put it back at the end of the queue so it can be recycled later
        // (This acts as a seamless circular pool)
        _poolDictionary[particleName].Enqueue(particleToSpawn);
    }
}