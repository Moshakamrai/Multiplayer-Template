using UnityEngine;
using System.Collections;

public class CameraShake : MonoBehaviour
{
    public static CameraShake Instance { get; private set; }

    private Vector3 _originalLocalPos;
    private Coroutine _shakeCoroutine;

    void Awake()
    {
        Instance = this;
        _originalLocalPos = transform.localPosition;
        Debug.Log($"<color=cyan>[CameraShake] Initialized on '{gameObject.name}'. Instance set.</color>");
    }

    public void Shake(float duration, float magnitude)
    {
        Debug.Log($"<color=yellow>[CameraShake] Shake called — duration={duration}, magnitude={magnitude}, Instance null={Instance == null}</color>");
        if (_shakeCoroutine != null) StopCoroutine(_shakeCoroutine);
        _shakeCoroutine = StartCoroutine(ShakeRoutine(duration, magnitude));
    }

    IEnumerator ShakeRoutine(float duration, float magnitude)
    {
        Debug.Log($"<color=green>[CameraShake] Shake started on '{gameObject.name}'</color>");
        float elapsed = 0f;
        while (elapsed < duration)
        {
            float dampened = magnitude * (1f - elapsed / duration);
            transform.localPosition = _originalLocalPos + (Vector3)(Random.insideUnitCircle * dampened);
            elapsed += Time.deltaTime;
            yield return null;
        }
        transform.localPosition = _originalLocalPos;
        _shakeCoroutine = null;
        Debug.Log($"<color=green>[CameraShake] Shake finished.</color>");
    }
}
