using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Swept blade-arc trail. Samples two points along the blade (base + tip) every frame while
/// swinging and stitches consecutive frames into a filled, fading ribbon — so the WHOLE shape of
/// the blade smears through space, auto-following whatever the animation does (zero per-clip setup).
///
/// Put this on the sword fighter's root. Place two empty children on the blade (hilt + tip) and
/// drag them into Blade Base / Blade Tip. Call StartSwing() when an attack fires (PlayerCombat
/// does this automatically). Uses the Custom/SwordArc additive shader if no material is assigned.
/// </summary>
public class SwordArcTrail : MonoBehaviour
{
    [Header("Blade points (empties along the blade)")]
    public Transform bladeBase;   // hilt / guard
    public Transform bladeTip;    // tip

    [Header("Look")]
    public Material arcMaterial;                              // optional; auto-created from Custom/SwordArc if null
    [Tooltip("HDR — push intensity up (values >1) so it glows brightly over the neon arena.")]
    [ColorUsage(false, true)] public Color arcColor = new Color(1.4f, 2.6f, 3.4f, 1f); // bright HDR cyan
    [Tooltip("How long the arc lingers behind the blade (seconds).")]
    public float trailDuration = 0.30f;
    [Tooltip("Default emit window per swing (seconds).")]
    public float swingDuration = 0.45f;
    [Range(4, 80)] public int maxSamples = 48;

    private struct Sample { public Vector3 b, t; public float time; }
    private readonly List<Sample> _samples = new List<Sample>();
    private bool  _emitting;
    private float _emitUntil;

    private GameObject _arcGO;
    private Mesh _mesh;

    void Awake()
    {
        // World-space mesh holder (unparented so the player's transform doesn't smear the arc).
        _arcGO = new GameObject(name + "_SwordArc");
        _arcGO.transform.SetParent(null);
        _arcGO.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        var mf = _arcGO.AddComponent<MeshFilter>();
        var mr = _arcGO.AddComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

        if (arcMaterial == null)
        {
            var sh = Shader.Find("Custom/SwordArc");
            if (sh != null) arcMaterial = new Material(sh);
        }
        if (arcMaterial != null)
        {
            if (arcMaterial.HasProperty("_TintColor")) arcMaterial.SetColor("_TintColor", arcColor);
            mr.material = arcMaterial;
        }

        _mesh = new Mesh { name = "SwordArcMesh" };
        _mesh.MarkDynamic();
        mf.mesh = _mesh;
    }

    public void StartSwing() => StartSwing(swingDuration);

    public void StartSwing(float seconds)
    {
        if (bladeBase == null || bladeTip == null) return;
        _emitting  = true;
        _emitUntil = Time.time + seconds;
    }

    void LateUpdate()
    {
        float now = Time.time;

        if (_emitting)
        {
            if (now > _emitUntil) _emitting = false;
            if (bladeBase != null && bladeTip != null)
                _samples.Add(new Sample { b = bladeBase.position, t = bladeTip.position, time = now });
        }

        // Drop samples older than the trail duration, and cap the count.
        _samples.RemoveAll(s => now - s.time > trailDuration);
        while (_samples.Count > maxSamples) _samples.RemoveAt(0);

        RebuildMesh(now);
    }

    void RebuildMesh(float now)
    {
        if (_samples.Count < 2) { _mesh.Clear(); return; }

        int n = _samples.Count;
        var verts = new Vector3[n * 2];
        var cols  = new Color[n * 2];
        var uvs   = new Vector2[n * 2];

        for (int i = 0; i < n; i++)
        {
            verts[i * 2]     = _samples[i].b;
            verts[i * 2 + 1] = _samples[i].t;

            float age = Mathf.Clamp01((now - _samples[i].time) / Mathf.Max(0.0001f, trailDuration));
            float fade = 1f - age;                 // newest = brightest
            Color c = arcColor; c.a *= fade;
            cols[i * 2] = c; cols[i * 2 + 1] = c;

            float v = i / (float)(n - 1);
            uvs[i * 2]     = new Vector2(0f, v);
            uvs[i * 2 + 1] = new Vector2(1f, v);
        }

        int quads = n - 1;
        var tris = new int[quads * 12]; // 2 tris front + 2 tris back (double-sided)
        int k = 0;
        for (int i = 0; i < quads; i++)
        {
            int b0 = i * 2, t0 = i * 2 + 1, b1 = (i + 1) * 2, t1 = (i + 1) * 2 + 1;
            // front
            tris[k++] = b0; tris[k++] = t0; tris[k++] = b1;
            tris[k++] = t0; tris[k++] = t1; tris[k++] = b1;
            // back
            tris[k++] = b1; tris[k++] = t0; tris[k++] = b0;
            tris[k++] = b1; tris[k++] = t1; tris[k++] = t0;
        }

        _mesh.Clear();
        _mesh.vertices  = verts;
        _mesh.colors    = cols;
        _mesh.uv        = uvs;
        _mesh.triangles = tris;
        _mesh.RecalculateBounds();
    }

    void OnDestroy()
    {
        if (_arcGO != null) Destroy(_arcGO);
        if (_mesh != null)  Destroy(_mesh);
    }
}
