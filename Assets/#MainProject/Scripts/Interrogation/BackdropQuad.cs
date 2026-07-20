using System.IO;
using UnityEngine;

// Loads a suspect's room backdrop from StreamingAssets AT RUNTIME and applies it to this
// quad's material. Must be runtime, not baked at scene-build time: a Texture2D created in
// the editor via LoadImage is a non-serialized in-memory object — it survives in the open
// scene right after building, but is GONE the moment the scene reloads (e.g. on Play),
// leaving the quad's material pointing at a null texture (the "backdrop vanishes on Play"
// bug). Loading here, in Start(), sidesteps that entirely.
[RequireComponent(typeof(Renderer))]
public class BackdropQuad : MonoBehaviour
{
    [Tooltip("Path relative to StreamingAssets, e.g. 'gnomes-intro/backdrops/pemberton.png'.")]
    public string streamingPath = "";

    void Start()
    {
        if (string.IsNullOrEmpty(streamingPath)) return;
        string full = Path.Combine(Application.streamingAssetsPath, streamingPath);
        if (!File.Exists(full)) { Debug.LogWarning($"[BackdropQuad] not found: {full}", this); return; }

        var tex = new Texture2D(2, 2);
        if (!tex.LoadImage(File.ReadAllBytes(full))) { Debug.LogWarning($"[BackdropQuad] failed to decode: {full}", this); return; }

        // Build the material fresh at runtime rather than trusting a build-time material to
        // have survived scene serialization (it often doesn't — same non-serialized-object
        // trap as the texture). Prefer URP Unlit, fall back to legacy Unlit/Texture.
        var rend = GetComponent<Renderer>();
        var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture");
        var mat = new Material(shader);
        mat.mainTexture = tex;
        rend.material = mat;

        // Match the quad's aspect to the image so the room isn't stretched — keep the
        // authored height, adjust width. (Scale set here rather than at build time so it
        // tracks whatever image actually loads.)
        float aspect = (float)tex.width / tex.height;
        var s = transform.localScale;
        transform.localScale = new Vector3(s.y * aspect, s.y, s.z);
    }
}
