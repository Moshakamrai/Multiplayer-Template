using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class FloatingDamageText : MonoBehaviour
{
    private Text damageText;
    private RectTransform rectTransform;
    private float duration = 1.5f;
    private float elapsedTime = 0f;

    private void Start()
    {
        damageText = GetComponent<Text>();
        rectTransform = GetComponent<RectTransform>();
    }

    public void Initialize(int damage, Color color, Vector3 worldPosition)
    {
        damageText.text = damage.ToString();
        damageText.color = color;
        elapsedTime = 0f;

        // Convert world position to screen position
        Vector3 screenPos = Camera.main.WorldToScreenPoint(worldPosition);
        rectTransform.position = screenPos;

        StartCoroutine(AnimateDamage());
    }

    private IEnumerator AnimateDamage()
    {
        Vector3 startPos = rectTransform.position;
        Vector3 endPos = startPos + Vector3.up * 80f;
        Vector3 startScale = Vector3.one;

        while (elapsedTime < duration)
        {
            elapsedTime += Time.deltaTime;
            float progress = elapsedTime / duration;

            // Move upward
            rectTransform.position = Vector3.Lerp(startPos, endPos, progress);

            // Scale down
            float scale = Mathf.Lerp(1.2f, 0.5f, progress);
            rectTransform.localScale = Vector3.one * scale;

            // Fade out
            Color color = damageText.color;
            color.a = Mathf.Lerp(1f, 0f, progress);
            damageText.color = color;

            yield return null;
        }

        gameObject.SetActive(false);
    }
}
