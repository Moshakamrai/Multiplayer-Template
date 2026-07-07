using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Full-screen pixelation with a per-layer strength split:
//  - everything renders at 'pixelHeight' (coarse, chunky)
//  - renderers on 'characterLayers' are stamped into a mask and use
//    'characterPixelHeight' instead (finer = less pixelated)
// Runs before post-processing so bloom glows over the pixel blocks.
// IMGUI/OnGUI draws outside the pipeline and stays full-res.
public class PixelateFeature : ScriptableRendererFeature
{
    [Tooltip("Environment pixel resolution (vertical). 360 = chunky, 540 = subtle.")]
    [Range(90, 1080)] public int pixelHeight = 360;

    [Tooltip("Pixel resolution for renderers on Character Layers. Higher = less pixelated than the environment.")]
    [Range(90, 2160)] public int characterPixelHeight = 720;

    [Tooltip("Layers that get the finer characterPixelHeight (put your characters on a layer and pick it here). None = everything uses the coarse grid.")]
    public LayerMask characterLayers = 0;

    class PixelatePass : ScriptableRenderPass
    {
        public int coarseHeight;
        public int fineHeight;
        public LayerMask characterLayers;

        RTHandle _copy;
        RTHandle _mask;
        Material _blitMaterial;
        Material _maskMaterial;

        static readonly List<ShaderTagId> ShaderTags = new List<ShaderTagId>
        {
            new ShaderTagId("UniversalForward"),
            new ShaderTagId("UniversalForwardOnly"),
            new ShaderTagId("SRPDefaultUnlit"),
        };

        public PixelatePass()
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
        }

        bool EnsureMaterials()
        {
            if (_blitMaterial == null)
            {
                var s = Shader.Find("Hidden/CyberPixel/PixelateBlit");
                if (s != null) _blitMaterial = CoreUtils.CreateEngineMaterial(s);
            }
            if (_maskMaterial == null)
            {
                var s = Shader.Find("Hidden/CyberPixel/Mask");
                if (s != null) _maskMaterial = CoreUtils.CreateEngineMaterial(s);
            }
            return _blitMaterial != null && _maskMaterial != null;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (renderingData.cameraData.cameraType != CameraType.Game)
                return;
            if (!EnsureMaterials())
                return;

            var camDesc = renderingData.cameraData.cameraTargetDescriptor;

            // Full-res copy of the camera color (can't read + write the same target)
            var copyDesc = camDesc;
            copyDesc.depthBufferBits = 0;
            copyDesc.msaaSamples = 1;
            RenderingUtils.ReAllocateIfNeeded(ref _copy, copyDesc, FilterMode.Point, TextureWrapMode.Clamp, name: "_CyberPixelCopy");

            // Character mask (full-res, single channel)
            var maskDesc = copyDesc;
            maskDesc.graphicsFormat = GraphicsFormat.R8_UNorm;
            RenderingUtils.ReAllocateIfNeeded(ref _mask, maskDesc, FilterMode.Point, TextureWrapMode.Clamp, name: "_CyberPixelMaskRT");

            RTHandle source = renderingData.cameraData.renderer.cameraColorTargetHandle;

            var cmd = CommandBufferPool.Get("CyberPixel Pixelate");
            Blitter.BlitCameraTexture(cmd, source, _copy, 0f, false);
            CoreUtils.SetRenderTarget(cmd, _mask, ClearFlag.Color, Color.clear);
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            // Stamp character-layer renderers into the mask
            if (characterLayers.value != 0)
            {
                var sorting = new SortingSettings(renderingData.cameraData.camera);
                var drawing = new DrawingSettings(ShaderTags[0], sorting)
                {
                    overrideMaterial = _maskMaterial,
                    overrideMaterialPassIndex = 0,
                    perObjectData = PerObjectData.None,
                };
                for (int i = 1; i < ShaderTags.Count; i++)
                    drawing.SetShaderPassName(i, ShaderTags[i]);
                var filtering = new FilteringSettings(RenderQueueRange.all, characterLayers);
                context.DrawRenderers(renderingData.cullResults, ref drawing, ref filtering);
            }

            // Final composite back into the camera color
            int coarseH = Mathf.Clamp(coarseHeight, 90, camDesc.height);
            int coarseW = Mathf.Max(90, Mathf.RoundToInt(camDesc.width * (coarseH / (float)camDesc.height)));
            int fineH = Mathf.Clamp(fineHeight, coarseH, camDesc.height);
            int fineW = Mathf.Max(90, Mathf.RoundToInt(camDesc.width * (fineH / (float)camDesc.height)));
            _blitMaterial.SetVector("_CyberPixelGrids", new Vector4(coarseW, coarseH, fineW, fineH));

            cmd.SetGlobalTexture("_CyberPixelMask", _mask);
            Blitter.BlitCameraTexture(cmd, _copy, source, _blitMaterial, 0);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public void Dispose()
        {
            _copy?.Release();
            _copy = null;
            _mask?.Release();
            _mask = null;
            CoreUtils.Destroy(_blitMaterial);
            _blitMaterial = null;
            CoreUtils.Destroy(_maskMaterial);
            _maskMaterial = null;
        }
    }

    PixelatePass _pass;

    public override void Create()
    {
        _pass = new PixelatePass();
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (renderingData.cameraData.cameraType != CameraType.Game)
            return;
        _pass.coarseHeight = pixelHeight;
        _pass.fineHeight = characterPixelHeight;
        _pass.characterLayers = characterLayers;
        renderer.EnqueuePass(_pass);
    }

    protected override void Dispose(bool disposing)
    {
        _pass?.Dispose();
    }
}
