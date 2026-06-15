using UnityEngine;
using System.Collections.Generic;

// Self-contained floating damage numbers. Draws via OnGUI in screen space, so it needs
// NO prefab and NO Canvas set up in the scene — just this component on a GameObject.
// Color convention (set by the caller): red = damage YOU took, green = damage YOU dealt.
public class FloatingDamageTextManager : MonoBehaviour
{
    public static FloatingDamageTextManager Instance { get; private set; }

    private struct Popup
    {
        public Vector3 worldPos;
        public string text;
        public Color color;
        public float startTime;
        public int damage;
    }

    private readonly List<Popup> _popups = new List<Popup>();
    private const float DURATION = 1.4f;
    private const float RISE_PIXELS = 90f;

    private GUIStyle _style;
    private Camera _cam;

    // Guarantee an instance exists even if nobody placed one in the scene.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance == null)
        {
            var go = new GameObject("FloatingDamageTextManager");
            go.AddComponent<FloatingDamageTextManager>();
            DontDestroyOnLoad(go);
        }
    }

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
    }

    // Camera.main needs a "MainCamera"-tagged camera; fall back to any camera otherwise.
    private Camera GetCamera()
    {
        if (_cam != null) return _cam;
        _cam = Camera.main;
        if (_cam == null) _cam = FindObjectOfType<Camera>();
        return _cam;
    }

    // Kept the same signature so existing callers (PlayerCombat) work unchanged.
    public void ShowDamage(int damage, Color color, Vector3 worldPosition)
    {
        // VR: IMGUI doesn't render in stereo, so spawn a real 3D text popup instead.
        if (VRCameraDriver.VRActive)
        {
            SpawnWorldPopup(damage, color, worldPosition);
            return;
        }

        _popups.Add(new Popup
        {
            worldPos = worldPosition,
            text = damage.ToString(),
            color = color,
            startTime = Time.time,
            damage = damage
        });
    }

    // --- VR world-space popup: a billboarded 3D TextMesh that rises and fades, then self-destroys ---
    private void SpawnWorldPopup(int damage, Color color, Vector3 worldPos)
    {
        var go = new GameObject("DmgPopup");
        go.transform.position = worldPos + Vector3.up * 0.2f;

        var tm = go.AddComponent<TextMesh>();
        tm.text = damage.ToString();
        tm.color = color;
        tm.fontSize = 90;
        tm.characterSize = 0.012f + Mathf.Clamp(damage, 0, 40) * 0.0004f; // bigger hits = bigger text
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.fontStyle = FontStyle.Bold;
        go.GetComponent<MeshRenderer>().sortingOrder = 5000; // draw over the world

        StartCoroutine(AnimateWorldPopup(go, tm, color));
    }

    private System.Collections.IEnumerator AnimateWorldPopup(GameObject go, TextMesh tm, Color color)
    {
        float t = 0f;
        Vector3 start = go.transform.position;
        while (t < DURATION && go != null)
        {
            t += Time.deltaTime;
            float n = t / DURATION;

            // Rise upward and fade out (slow at first).
            go.transform.position = start + Vector3.up * (n * 0.6f);
            float alpha = 1f - Mathf.Clamp01(n * n);
            tm.color = new Color(color.r, color.g, color.b, alpha);

            // Billboard toward the camera so it's readable from any angle.
            var cam = GetCamera();
            if (cam != null)
                go.transform.rotation = Quaternion.LookRotation(go.transform.position - cam.transform.position);

            yield return null;
        }
        if (go != null) Destroy(go);
    }

    private void OnGUI()
    {
        if (_popups.Count == 0) return;
        var cam = GetCamera();
        if (cam == null) return;

        if (_style == null)
            _style = new GUIStyle { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };

        for (int i = _popups.Count - 1; i >= 0; i--)
        {
            var p = _popups[i];
            float t = (Time.time - p.startTime) / DURATION;
            if (t >= 1f) { _popups.RemoveAt(i); continue; }

            Vector3 sp = cam.WorldToScreenPoint(p.worldPos);
            if (sp.z < 0f) continue; // behind the camera

            // Screen-space Y is bottom-up; GUI is top-down. Flip and rise upward over time.
            float guiX = sp.x;
            float guiY = (Screen.height - sp.y) - t * RISE_PIXELS;

            // Bigger hits = bigger text; everything scales down slightly as it fades.
            int fontSize = Mathf.RoundToInt(Mathf.Lerp(40f, 26f, t) + Mathf.Clamp(p.damage, 0, 40) * 0.5f);
            _style.fontSize = fontSize;

            float alpha = 1f - Mathf.Clamp01(t * t); // fade out, slow at first
            var rect = new Rect(guiX - 100f, guiY - 30f, 200f, 60f);

            // Black outline for readability over any background
            _style.normal.textColor = new Color(0f, 0f, 0f, alpha * 0.9f);
            GUI.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height), p.text, _style);
            GUI.Label(new Rect(rect.x - 2f, rect.y + 2f, rect.width, rect.height), p.text, _style);

            // Main colored number
            _style.normal.textColor = new Color(p.color.r, p.color.g, p.color.b, alpha);
            GUI.Label(rect, p.text, _style);
        }
    }
}
