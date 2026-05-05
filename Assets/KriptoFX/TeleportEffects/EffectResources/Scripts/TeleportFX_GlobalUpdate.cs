using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

#if USING_URP
using UnityEngine.Rendering.Universal;
#endif


namespace TeleportFX
{
    [AddComponentMenu("")]
    public class TeleportFX_GlobalUpdate : MonoBehaviour
    {
        internal static   GameObject                               Instance;
        internal static List<TeleportFX_IScriptInstance>         ScriptInstances     = new List<TeleportFX_IScriptInstance>();
        internal static List<TeleportFX_CommandBufferDistortion> DistortionInstances = new List<TeleportFX_CommandBufferDistortion>();


        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void RunOnStart()
        {
            Destroy(Instance);
            Instance = null;

            ScriptInstances.Clear();
        }

        public static void CreateInstanceIfRequired()
        {
            if (Instance != null) return;

            Instance = new GameObject("TeleportFX_GlobalUpdate") { hideFlags = HideFlags.HideAndDontSave };
            Instance.AddComponent<TeleportFX_GlobalUpdate>();
        }


        void Update()
        {
            for (var i = 0; i < ScriptInstances.Count; i++)
            {
                ScriptInstances[i].ManualUpdate();
            }
        }


        //builtin distortion rendering command buffer

        private CommandBuffer _cmd;
        private CameraEvent   _cameraEvent                   = CameraEvent.BeforeForwardAlpha;
        int                   _screenCopyID                  = Shader.PropertyToID("_CameraOpaqueTextureRT");
        private int           _globalBuiltintOpaqueTextureID = Shader.PropertyToID("_CameraOpaqueTexture");


        void OnEnable()
        {
            if (GraphicsSettings.currentRenderPipeline == null)
            {
                Camera.onPreCull += OnBeforeCameraRendering;
                Camera.onPostRender += OnAfterCameraRendering;
            }
            else
            {
                RenderPipelineManager.beginCameraRendering += OnBeforeCameraRendering;
                RenderPipelineManager.endCameraRendering += OnAfterCameraRendering;

            }
        }

        void OnDisable()
        {
            if (GraphicsSettings.currentRenderPipeline == null)
            {
                Camera.onPreCull -= OnBeforeCameraRendering;
                Camera.onPostRender -= OnAfterCameraRendering;
            }
            else
            {
                RenderPipelineManager.beginCameraRendering -= OnBeforeCameraRendering;
                RenderPipelineManager.endCameraRendering -= OnAfterCameraRendering;
            }
        }

        private void OnBeforeCameraRendering(Camera cam)
        {
            RenderDistortion(cam, isLegacyPipeline: true);
            EnableLegacyDepthIfRequired(cam);
        }


        private void OnAfterCameraRendering(Camera cam)
        {
            ClearDistortion(cam);
        }


        private void OnBeforeCameraRendering(ScriptableRenderContext context, Camera cam)
        {
            RenderDistortion(cam, isLegacyPipeline: false, context);
        }

        private void OnAfterCameraRendering(ScriptableRenderContext context, Camera cam)
        {
           
        }

        public static GraphicsFormat GetGraphicsFormatHDR()
        {
            if (SystemInfo.IsFormatSupported(GraphicsFormat.B10G11R11_UFloatPack32, FormatUsage.Render)) return GraphicsFormat.B10G11R11_UFloatPack32;
            else return GraphicsFormat.R16G16B16A16_SFloat;
        }


        void RenderDistortion(Camera cam, bool isLegacyPipeline, ScriptableRenderContext context = default)
        {
            if (DistortionInstances.Count == 0) return;

            if (isLegacyPipeline)
            {
                if (_cmd == null)
                {
                    _cmd = new CommandBuffer() { name = "TeleportFX_CameraDistortionRendering" };
                }

                _cmd.Clear();


                _cmd.GetTemporaryRT(_screenCopyID, Screen.width, Screen.height, 0, FilterMode.Bilinear, GetGraphicsFormatHDR());
                _cmd.Blit(BuiltinRenderTextureType.CurrentActive, _screenCopyID);
                _cmd.SetGlobalTexture(_globalBuiltintOpaqueTextureID, _screenCopyID);
                cam.AddCommandBuffer(_cameraEvent, _cmd);
            }
            else
            {
#if USING_URP
                if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset)
                {
                    var data = cam.GetUniversalAdditionalCameraData();
                    if (data != null)
                    {
                        data.requiresDepthOption = UnityEngine.Rendering.Universal.CameraOverrideOption.On;
                        data.requiresColorOption = UnityEngine.Rendering.Universal.CameraOverrideOption.On;

                        return;
                    }
                }
#endif
            }
        }

        void ClearDistortion(Camera cam)
        {
            if (DistortionInstances.Count == 0) return;
            if (_cmd != null)
            {
                cam.RemoveCommandBuffer(_cameraEvent, _cmd);
            }
        }

        void EnableLegacyDepthIfRequired(Camera cam, ScriptableRenderContext context = default)
        {
            if(cam.renderingPath == RenderingPath.Forward) cam.depthTextureMode |= DepthTextureMode.Depth;
        }
    }
}