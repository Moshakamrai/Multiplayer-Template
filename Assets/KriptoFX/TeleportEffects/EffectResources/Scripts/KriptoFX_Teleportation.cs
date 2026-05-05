//#define KWS_DEBUG_MODE

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;



namespace TeleportFX
{
    [DisallowMultipleComponent]
    public class KriptoFX_Teleportation : TeleportFX_IScriptInstance
    {
        public TeleportationStateEnum TeleportationState = TeleportationStateEnum.Appear;
        public GameObject AppearTeleportationEffect;
        public GameObject DisappearTeleportationEffect;
        public bool UseEffectLighting = true;
        public float AppearTimeScale = 1;
        public float DisappearTimeScale = 1;

        internal bool UseAutoScale = true;
        internal Transform OverrideEffectPosition;


        [Space]
        public bool UseAnimatorTriggers = false;
        public String AppearAnimationTriggerName;
        public String DisappearAnimationTriggerName;


#if KWS_DEBUG_MODE
        public float DebugDisable = 3;
#endif

        public enum TeleportationStateEnum
        {
            DefaultEnabled,
            DefaultDisabled,
            Appear,
            Disappear
        }

        [HideInInspector] public Action IsTeleportationFinished;

        private TeleportationStateEnum _lastState = TeleportationStateEnum.DefaultEnabled;

        private Dictionary<Material, Material> _materialInstances = new Dictionary<Material, Material>();
        private List<MeshRendererInfo> _meshRenderers = new List<MeshRendererInfo>();

        public GameObject _appearEffectInstance;
        private GameObject _disappearEffectInstance;

        private TeleportFX_Settings _appearSettings;
        private TeleportFX_Settings _disappearSettings;
        private float _leftTime;

        private static int _TeleportThresholdID = Shader.PropertyToID("_TeleportThreshold");
        private static int _DissolveCutoutID = Shader.PropertyToID("_DissolveCutout");
        private static int _DissolveCutoutHeightID = Shader.PropertyToID("_DissolveCutoutHeight");
        private static int _NoiseStrengthID = Shader.PropertyToID("_NoiseStrength");
        private static int _DissolveColor1ID = Shader.PropertyToID("_DissolveColor1");
        private static int _DissolveColor2ID = Shader.PropertyToID("_DissolveColor2");
        private static int _DissolveColor3ID = Shader.PropertyToID("_DissolveColor3");
        private static int _DissolveThresholdID = Shader.PropertyToID("_DissolveThreshold");
        private static int _UseVertexPositionAsUVID = Shader.PropertyToID("_UseVertexPositionAsUV");
        private static int _NoiseScaleID = Shader.PropertyToID("_NoiseScale");
        private static int _DissolveNoiseID = Shader.PropertyToID("_DissolveNoise");

        private static int _AlphaCutoffEnableID = Shader.PropertyToID("_AlphaCutoffEnable");

        private Animator _animator;

        private const float DefaultModelScaleMeters = 2.67f;

        class MeshRendererInfo
        {
            public Renderer MeshRenderer;
            public Material DefaultMaterial;
            public Material InstanceMaterial;

            public MeshRendererInfo(Renderer rend, Dictionary<Material, Material> materialInstances)
            {
                MeshRenderer = rend;
                DefaultMaterial = rend.sharedMaterial;

                if (!materialInstances.ContainsKey(DefaultMaterial))
                {
                    InstanceMaterial = new Material(DefaultMaterial) { hideFlags = HideFlags.HideAndDontSave };
                    materialInstances.Add(DefaultMaterial, InstanceMaterial);
                }
                else
                {
                    InstanceMaterial = materialInstances[DefaultMaterial];
                }
            }

            public void SwapToTeleportMaterial(float thresholdCutout, float dissolveFade, Shader shader)
            {
                InstanceMaterial.shader = shader;
                InstanceMaterial.SetFloat(_TeleportThresholdID, thresholdCutout);
                InstanceMaterial.SetFloat(_DissolveCutoutID, dissolveFade);
                InstanceMaterial.SetFloat(_AlphaCutoffEnableID, 1);
                MeshRenderer.sharedMaterial = InstanceMaterial;
            }

            public void SwapToDefaultMaterial()
            {
                MeshRenderer.sharedMaterial = DefaultMaterial;
            }


            public void Clear()
            {
                if (InstanceMaterial != null) Destroy(InstanceMaterial);
                if (MeshRenderer != null && DefaultMaterial != null) MeshRenderer.sharedMaterial = DefaultMaterial;
            }
        }


        internal override void OnEnableExtended()
        {
            _lastState = TeleportationStateEnum.DefaultEnabled;
            ManualUpdate();

#if KWS_DEBUG_MODE
            TeleportationState = TeleportationStateEnum.Appear;
            CancelInvoke("DebugInvoke");
            Invoke("DebugInvoke", DebugDisable);
#endif
        }

        internal override void OnDisableExtended()
        {
            StopAllCoroutines();
        }

#if KWS_DEBUG_MODE
        void DebugInvoke()
        {
            TeleportationState = TeleportationStateEnum.Disappear;
        }
#endif

        void Awake()
        {
            if (AppearTeleportationEffect == null || DisappearTeleportationEffect == null)
            {
                Debug.LogError("You have to set the 'Appear and Dissapear Teleportation Effect' before using!");
                return;
            }

            _appearEffectInstance = Instantiate(
                AppearTeleportationEffect,
                transform.position,
                transform.rotation,
                transform
            );
            _appearEffectInstance.SetActive(false);

            _disappearEffectInstance = Instantiate(DisappearTeleportationEffect);
            _disappearEffectInstance.SetActive(false);

            _appearSettings = _appearEffectInstance.GetComponentInChildren<TeleportFX_Settings>();
            _disappearSettings = _disappearEffectInstance.GetComponentInChildren<TeleportFX_Settings>();
            _animator = GetComponent<Animator>();

            var meshRenderers = this.GetComponentsInChildren<Renderer>(true);
            foreach (var rend in meshRenderers)
            {
                _meshRenderers.Add(new MeshRendererInfo(rend, _materialInstances));
            }

            if (OverrideEffectPosition)
            {
                _appearEffectInstance.transform.parent = OverrideEffectPosition;
                _disappearEffectInstance.transform.parent = OverrideEffectPosition;
            }
            else
            {
                _appearEffectInstance.transform.parent = transform;
                _disappearEffectInstance.transform.parent = transform;
            }

            if (!UseEffectLighting)
            {
                foreach (var lightGO in _appearSettings.LightObjects)
                {
                    if (lightGO != null) lightGO.SetActive(false);
                }
                foreach (var lightGO in _disappearSettings.LightObjects)
                {
                    if (lightGO != null) lightGO.SetActive(false);
                }
            }

            if (UseAutoScale)
            {
                var minHeight = 0f;
                var maxHeight = 0f;
                foreach (var rend in meshRenderers)
                {
                    minHeight = Mathf.Min(minHeight, rend.bounds.min.y);
                    maxHeight = Mathf.Max(maxHeight, rend.bounds.max.y);
                }
                var modelSize = maxHeight - minHeight;
                var effectScale = Vector3.one * ((modelSize / transform.transform.localScale.y) / DefaultModelScaleMeters);
                _appearEffectInstance.transform.localScale = effectScale;
                _disappearEffectInstance.transform.localScale = effectScale;

            }


            if (Math.Abs(AppearTimeScale - 1) > 0.0001f)
            {
                SetTimeScale(_appearSettings, _appearEffectInstance);
            }
            if (Math.Abs(DisappearTimeScale - 1) > 0.0001f)
            {
                SetTimeScale(_disappearSettings, _disappearEffectInstance);
            }
        }

        void SetTimeScale(TeleportFX_Settings settings, GameObject effectInstance)
        {
            settings.CutoutTime /= AppearTimeScale;
            settings.VertexTeleportationTime /= AppearTimeScale;
            settings.VertexTeleportationTime /= AppearTimeScale;

            var particleSystems = effectInstance.GetComponentsInChildren<ParticleSystem>(true);
            foreach (var ps in particleSystems)
            {
                var main = ps.main;
                main.simulationSpeed *= AppearTimeScale;
            }
        }


        void OnDestroy()
        {
            ClearMaterials();
            if (_appearEffectInstance != null) Destroy(_appearEffectInstance);
            if (_disappearEffectInstance != null) Destroy(_disappearEffectInstance);
        }

        internal override void ManualUpdate()
        {
            if (AppearTeleportationEffect == null) return;

            if (_lastState != TeleportationState)
            {
                _lastState = TeleportationState;
                TeleportationStateChanged(false);
            }
            
        }


        void TeleportationStateChanged(bool isInitialisationState)
        {
            StopAllCoroutines();

            _appearEffectInstance.SetActive(false);
            _disappearEffectInstance.SetActive(false);
            _appearEffectInstance.transform.position = OverrideEffectPosition ? OverrideEffectPosition.position : transform.position;
            _disappearEffectInstance.transform.position = _appearEffectInstance.transform.position;

            if (TeleportationState == TeleportationStateEnum.Appear)
            {
                _appearEffectInstance.SetActive(true);
                foreach (var rendInfo in _meshRenderers) { rendInfo.SwapToTeleportMaterial(0, 0, _appearSettings.Shader); }

                TriggerAnimator(AppearAnimationTriggerName);
                if (!isInitialisationState) StartCoroutine(TeleportationTick());
            }

            if (TeleportationState == TeleportationStateEnum.Disappear)
            {
                _disappearEffectInstance.SetActive(true);
                foreach (var rendInfo in _meshRenderers) { rendInfo.SwapToTeleportMaterial(0, 0, _disappearSettings.Shader); }

                TriggerAnimator(DisappearAnimationTriggerName);
                if (!isInitialisationState) StartCoroutine(TeleportationTick());
            }


            if (TeleportationState == TeleportationStateEnum.DefaultEnabled)
            {
                foreach (var rendInfo in _meshRenderers)
                {
                    rendInfo.SwapToDefaultMaterial();
                }
            }

            if (TeleportationState == TeleportationStateEnum.DefaultDisabled)
            {
                foreach (var rendInfo in _meshRenderers)
                {
                    rendInfo.SwapToTeleportMaterial(1, 1, _appearSettings.Shader);
                }
            }
        }

        void ClearMaterials()
        {
            foreach (var rendInfo in _meshRenderers)
            {
                rendInfo.Clear();
            }

            _meshRenderers.Clear();
        }

        IEnumerator TeleportationTick()
        {
            var settings = _appearSettings;
            if (TeleportationState == TeleportationStateEnum.Disappear) settings = _disappearSettings;

            var maxTime = 0f;
            if (settings.UseVertexTeleportation) maxTime = Mathf.Max(settings.VertexTeleportationTime, maxTime);
            if (settings.UseDissolveByTime) maxTime = Mathf.Max(settings.CutoutTime, maxTime);
            if (settings.UseDissolveByHeight) maxTime = Mathf.Max(settings.DissolveByHeightDuration, maxTime);

            if (settings.UseDissolveByTime || settings.UseDissolveByHeight)
            {
                var useVertexPositionAsUV = settings.UseVertexPositionAsUV ? 1 : 0;

                foreach (var instance in _materialInstances)
                {
                    var mat = instance.Value;
                    mat.SetFloat(_UseVertexPositionAsUVID, useVertexPositionAsUV);
                    if (settings.OverrideTexture) mat.SetTexture(_DissolveNoiseID, settings.NoiseTexture);
                    mat.SetVector(_NoiseStrengthID, settings.NoiseStrength);
                    mat.SetVector(_NoiseScaleID, settings.NoiseScale);
                    mat.SetColor(_DissolveColor1ID, settings.DissolveColor1);
                    mat.SetColor(_DissolveColor2ID, settings.DissolveColor2);
                    mat.SetColor(_DissolveColor3ID, settings.DissolveColor3);
                    mat.SetVector(_DissolveThresholdID, settings.DissolveThresold);
                }
            }

            _leftTime = 0;
            while (_leftTime <= maxTime)
            {
                _leftTime += Time.deltaTime;
                var teleportThreshold = 0f;
                var dissolveCutout = 1f;
                var dissolveCutoutHeight = 0f;

                if (settings.UseVertexTeleportation)
                {
                    var normalizedVertexTime = Mathf.Clamp01(_leftTime / settings.VertexTeleportationTime);
                    teleportThreshold = settings.VertexTeleporationCurve.Evaluate(normalizedVertexTime);
                }

                if (settings.UseDissolveByTime)
                {
                    var normalizedCutoutTime = Mathf.Clamp01(_leftTime / settings.CutoutTime);
                    dissolveCutout = settings.CutoutCurve.Evaluate(normalizedCutoutTime);
                    //Debug.Log("_leftTime: " + _leftTime + "     settings.CutoutTime:" + settings.CutoutTime + "    normalizedCutoutTime:" + normalizedCutoutTime + "  " + dissolveCutout);
                }
                if (settings.UseDissolveByHeight)
                {
                    dissolveCutoutHeight = settings.DissolveAnchor.transform.position.y;
                }


                foreach (var instance in _materialInstances)
                {
                    var mat = instance.Value;
                    mat.SetFloat(_TeleportThresholdID, teleportThreshold);
                    mat.SetFloat(_DissolveCutoutID, dissolveCutout);
                    mat.SetFloat(_DissolveCutoutHeightID, dissolveCutoutHeight);
                }

                yield return null;
            }

            if (TeleportationState == TeleportationStateEnum.Appear)
            {
                foreach (var rendInfo in _meshRenderers)
                {
                    rendInfo.SwapToDefaultMaterial();
                }
            }

            IsTeleportationFinished?.Invoke();
        }

        void TriggerAnimator(string triggerName)
        {
            if (!UseAnimatorTriggers || _animator == null || triggerName.Length == 0) return;

            _animator.SetTrigger(triggerName);
        }

        public void TriggerTeleportation(bool appear)
        {
            if (appear)
            {
                TeleportationState = TeleportationStateEnum.Appear;
            }
            else
            {
                TeleportationState = TeleportationStateEnum.Disappear;
            }

            // Force the effect to update immediately
            ManualUpdate();
        }


    }
    
    
}

