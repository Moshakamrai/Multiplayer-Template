using UnityEngine;
using System.Collections.Generic;

public class FloatingDamageTextManager : MonoBehaviour
{
    public static FloatingDamageTextManager Instance { get; private set; }

    [SerializeField] private GameObject damageTextPrefab;
    private Queue<FloatingDamageText> pool = new Queue<FloatingDamageText>();
    private const int POOL_SIZE = 20;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        // Initialize pool if prefab is assigned
        if (damageTextPrefab != null)
        {
            for (int i = 0; i < POOL_SIZE; i++)
            {
                GameObject go = Instantiate(damageTextPrefab, transform);
                FloatingDamageText fdt = go.GetComponent<FloatingDamageText>();
                go.SetActive(false);
                pool.Enqueue(fdt);
            }
        }
    }

    public void ShowDamage(int damage, Color color, Vector3 worldPosition)
    {
        if (damageTextPrefab == null)
        {
            Debug.LogWarning("FloatingDamageTextManager: Damage text prefab not assigned!");
            return;
        }

        FloatingDamageText fdt;
        if (pool.Count > 0)
        {
            fdt = pool.Dequeue();
        }
        else
        {
            GameObject go = Instantiate(damageTextPrefab, transform);
            fdt = go.GetComponent<FloatingDamageText>();
        }

        fdt.gameObject.SetActive(true);
        fdt.Initialize(damage, color, worldPosition);

        // Return to pool after animation completes
        StartCoroutine(ReturnToPoolAfterDelay(fdt, 1.6f));
    }

    private System.Collections.IEnumerator ReturnToPoolAfterDelay(FloatingDamageText fdt, float delay)
    {
        yield return new WaitForSeconds(delay);
        pool.Enqueue(fdt);
    }
}
