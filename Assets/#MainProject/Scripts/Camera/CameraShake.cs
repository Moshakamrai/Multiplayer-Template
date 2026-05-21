using UnityEngine;
using System.Collections;

public class CameraShake : MonoBehaviour
{
    public static CameraShake Instance { get; private set; }

    private Vector3 _originalLocalPos;
    private Coroutine _shakeCoroutine;
    private float _headBobPhase = 0f;
    public float headBobIntensity = 0.08f;
    public float headBobSpeed = 4f;

    void Awake()
    {
        Instance = this;
        _originalLocalPos = transform.localPosition;
        Debug.Log($"<color=cyan>[CameraShake] Initialized on '{gameObject.name}'. Instance set.</color>");
    }

    void Update()
    {
        // Apply subtle head bob during active rounds for fighter perspective immersion
        var rmm = RhythmRoundManager.Instance;
        if (rmm != null && rmm.isRoundActive && _shakeCoroutine == null)
        {
            _headBobPhase += Time.deltaTime * headBobSpeed;
            float bobX = Mathf.Sin(_headBobPhase * 0.7f) * headBobIntensity;
            float bobY = Mathf.Sin(_headBobPhase * 0.5f) * headBobIntensity * 0.6f;
            transform.localPosition = _originalLocalPos + new Vector3(bobX, bobY, 0f);
        }
        else if (_shakeCoroutine == null)
        {
            transform.localPosition = _originalLocalPos;
        }
    }

    public void Shake(float duration, float magnitude)
    {
        if (Instance == null)
        {
            Debug.LogWarning("<color=red>[CameraShake] Instance is null! Trying to find CameraShake in scene...</color>");
            Instance = FindObjectOfType<CameraShake>();
            if (Instance == null)
            {
                Debug.LogError("<color=red>[CameraShake] Cannot find CameraShake in scene!</color>");
                return;
            }
        }
        Debug.Log($"<color=yellow>[CameraShake] Shake called on '{gameObject.name}' — duration={duration}, magnitude={magnitude}</color>");
        if (_shakeCoroutine != null) StopCoroutine(_shakeCoroutine);
        _shakeCoroutine = StartCoroutine(ShakeRoutine(duration, magnitude));
    }

    IEnumerator ShakeRoutine(float duration, float magnitude)
    {
        Debug.Log($"<color=green>[CameraShake] Shake started on '{gameObject.name}' — magnitude={magnitude}</color>");
        float elapsed = 0f;
        while (elapsed < duration)
        {
            float dampened = magnitude * (1f - elapsed / duration);
            // Use more aggressive random offset - ensures visible shake
            float randomX = Random.Range(-1f, 1f) * dampened;
            float randomY = Random.Range(-1f, 1f) * dampened;
            Vector3 randomOffset = new Vector3(randomX, randomY, 0f);
            transform.localPosition = _originalLocalPos + randomOffset;
            Debug.Log($"<color=yellow>[CameraShake] Offset applied: {randomOffset}, dampened: {dampened}</color>");
            elapsed += Time.deltaTime;
            yield return null;
        }
        transform.localPosition = _originalLocalPos;
        _shakeCoroutine = null;
        Debug.Log($"<color=green>[CameraShake] Shake finished.</color>");
    }
}
