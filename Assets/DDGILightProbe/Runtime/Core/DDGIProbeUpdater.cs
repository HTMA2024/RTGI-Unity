using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace DDGI
{

    public enum ProbeUpdateMode
    {

        ComputeShader = 0,

        Raytracing = 1
    }

    [RequireComponent(typeof(DDGIVolume))]
    [ExecuteInEditMode]
    public class DDGIProbeUpdater : MonoBehaviour
    {
        #region Serialized Fields

        [Header("更新设置")]
        [SerializeField]
        private bool m_EnableUpdate = true;

        [SerializeField]
        [Tooltip("选择探针更新模式：ComputeShader（传统）或 Raytracing（DXR）")]
        private ProbeUpdateMode m_UpdateMode = ProbeUpdateMode.ComputeShader;

        [SerializeField]
        [Tooltip("Raytracing模式所需的RayGen Shader")]
        private RayTracingShader m_RayGenShader;

        [SerializeField]
        [Range(1, 16)]
        private int m_UpdatesPerFrame = 1;

        [Header("Raytracing设置")]
        [SerializeField]
        [Tooltip("每个Probe发射的光线数量")]
        private int m_RaysPerProbe = 128;

        [SerializeField]
        [Range(0.001f, 1f)]
        private float m_RayMinDistance = 0.01f;

        [SerializeField]
        [Range(10f, 500f)]
        private float m_RayMaxDistance = 100f;

        [SerializeField]
        [Tooltip("延迟光照Compute Shader")]
        private ComputeShader m_DeferredLightingShader;

        [SerializeField]
        [Tooltip("间接光采样Compute Shader")]
        private ComputeShader m_IndirectLightingShader;

        [SerializeField]
        [Tooltip("Radiance合成Compute Shader")]
        private ComputeShader m_RadianceCompositeShader;

        [SerializeField]
        [Tooltip("合并光照Compute Shader（替代DeferredLighting+IndirectLighting+RadianceComposite三个独立Pass）")]
        private ComputeShader m_LightingCombinedShader;

        [SerializeField]
        [Tooltip("蒙特卡洛积分Compute Shader（将RadianceBuffer积分到Atlas）")]
        private ComputeShader m_MonteCarloIntegrationShader;

        [SerializeField]
        [Tooltip("Probe Relocation Compute Shader（自动移动陷入几何体的探针）")]
        private ComputeShader m_ProbeRelocationShader;

        [SerializeField]
        [Tooltip("Probe Classification Compute Shader（自动标记无效探针）")]
        private ComputeShader m_ProbeClassificationShader;

        [SerializeField]
        [Tooltip("Variability Reduction Compute Shader（计算全局变异度）")]
        private ComputeShader m_VariabilityReductionShader;

        [Header("光照设置")]
        [SerializeField]
        [Range(0f, 5f)]
        private float m_SkyboxIntensity = 1f;

        [SerializeField]
        [Range(0f, 5f)]
        [Tooltip("间接光强度")]
        private float m_IndirectIntensity = 1f;

        [SerializeField]
        [Range(0f, 1f)]
        [Tooltip("法线偏移（用于避免自遮挡）")]
        private float m_NormalBias = 0.1f;

        [SerializeField]
        [Range(0.0001f, 0.1f)]
        [Tooltip("切比雪夫偏移")]
        private float m_ChebyshevBias = 0.001f;

        [Header("阴影设置")]
        [SerializeField]
        [Range(0f, 1f)]
        [Tooltip("阴影强度")]
        private float m_ShadowStrength = 1f;

        [SerializeField]
        [Range(0f, 0.1f)]
        [Tooltip("阴影深度偏移")]
        private float m_ShadowBias = 0.005f;

        [SerializeField]
        [Range(0f, 2f)]
        [Tooltip("阴影法线偏移")]
        private float m_ShadowNormalBias = 0.4f;

        #endregion

        #region Private Fields

        private DDGIVolume m_Volume;
        private DDGIRaytracingManager m_RaytracingManager;

        private bool m_IsInitialized = false;
        private bool m_RaytracingSupported = false;
        private int m_FrameCount = 0;

        private Vector3 m_LastVolumePosition;
        private Quaternion m_LastVolumeRotation;
        private Vector3 m_LastVolumeScale;

        #endregion
        #region Properties

        public bool EnableUpdate
        {
            get => m_EnableUpdate;
            set => m_EnableUpdate = value;
        }

        public ProbeUpdateMode UpdateMode
        {
            get => m_UpdateMode;
            set
            {
                if (m_UpdateMode != value)
                {
                    m_UpdateMode = value;
                    OnUpdateModeChanged();
                }
            }
        }

        public bool IsRaytracingSupported => m_RaytracingSupported;

        public bool IsUsingRaytracing => m_UpdateMode == ProbeUpdateMode.Raytracing && m_RaytracingSupported && m_RaytracingManager != null;

        public DDGIRaytracingManager RaytracingManager => m_RaytracingManager;

        public bool IsInitialized => m_IsInitialized;

        #endregion

        #region Unity Lifecycle

        private void OnEnable()
        {
            m_Volume = GetComponent<DDGIVolume>();

            Debug.Log(SystemInfo.graphicsDeviceName + "_" + SystemInfo.supportsRayTracing);
            m_RaytracingSupported = SystemInfo.supportsRayTracing;
            if (!m_RaytracingSupported && m_UpdateMode == ProbeUpdateMode.Raytracing)
            {
                Debug.LogWarning("[DDGIProbeUpdater] Raytracing not supported, falling back to ComputeShader mode");
                m_UpdateMode = ProbeUpdateMode.ComputeShader;
            }

            Initialize();
        }

        private void OnDisable()
        {
            DDGIResourceProvider.Unregister();
            Cleanup();
        }

        private void Update()
        {
            if (!m_EnableUpdate || !m_IsInitialized)
                return;

            if (m_Volume == null || !m_Volume.IsInitialized || m_Volume.AtlasManager == null)
                return;

            for (int i = 0; i < m_UpdatesPerFrame; i++)
            {
                if (IsUsingRaytracing)
                {
                    UpdateProbesRaytracing();
                }
                else
                {

                }
            }

            RegisterResourceSnapshot();
        }

        private void OnValidate()
        {

            if (m_UpdateMode == ProbeUpdateMode.Raytracing && !SystemInfo.supportsRayTracing)
            {
                Debug.LogWarning("[DDGIProbeUpdater] Raytracing not supported on this device");
            }

            if (m_IsInitialized && m_Volume != null && m_Volume.IsInitialized)
            {

                if (m_RaytracingManager != null)
                {
                    m_RaytracingManager.RaysPerProbe = m_RaysPerProbe;
                    m_RaytracingManager.RayMinDistance = m_RayMinDistance;
                    m_RaytracingManager.RayMaxDistance = m_RayMaxDistance;
                    m_RaytracingManager.SkyboxIntensity = m_SkyboxIntensity;
                    m_RaytracingManager.IndirectIntensity = m_IndirectIntensity;
                    m_RaytracingManager.NormalBias = m_NormalBias;
                    m_RaytracingManager.ChebyshevBias = m_ChebyshevBias;
                    m_RaytracingManager.ShadowStrength = m_ShadowStrength;
                    m_RaytracingManager.ShadowBias = m_ShadowBias;
                    m_RaytracingManager.ShadowNormalBias = m_ShadowNormalBias;
                }
            }
        }

        #endregion

        #region Public Methods

        public void Initialize()
        {

            if (m_RaytracingSupported && m_RayGenShader != null)
            {
                InitializeRaytracing();
            }

            CacheVolumeTransform();

            m_IsInitialized = true;

            var visualizer = GetComponent<DDGIProbeVisualizer>();
            if (visualizer != null && m_RaytracingManager != null)
            {
                visualizer.SetRaytracingManager(m_RaytracingManager);
            }
        }

        public void Cleanup()
        {
            CleanupRaytracing();
            m_IsInitialized = false;
        }

        public void ForceUpdate()
        {
            if (!m_IsInitialized)
                Initialize();

            if (m_IsInitialized)
            {
                if (IsUsingRaytracing)
                {
                    UpdateProbesRaytracing();
                }
                else
                {

                }
            }
        }

        #endregion

        #region Raytracing Methods

        private void InitializeRaytracing()
        {
            if (m_RaytracingManager != null)
            {
                m_RaytracingManager.Dispose();
            }

            m_RaytracingManager = new DDGIRaytracingManager(m_Volume);
            m_RaytracingManager.RaysPerProbe = m_RaysPerProbe;
            m_RaytracingManager.RayMinDistance = m_RayMinDistance;
            m_RaytracingManager.RayMaxDistance = m_RayMaxDistance;
            m_RaytracingManager.SkyboxIntensity = m_SkyboxIntensity;
            m_RaytracingManager.IndirectIntensity = m_IndirectIntensity;
            m_RaytracingManager.NormalBias = m_NormalBias;
            m_RaytracingManager.ChebyshevBias = m_ChebyshevBias;
            m_RaytracingManager.ShadowStrength = m_ShadowStrength;
            m_RaytracingManager.ShadowBias = m_ShadowBias;
            m_RaytracingManager.ShadowNormalBias = m_ShadowNormalBias;

            if (!m_RaytracingManager.Initialize(
                m_RayGenShader,
                m_DeferredLightingShader,
                m_IndirectLightingShader,
                m_RadianceCompositeShader,
                m_MonteCarloIntegrationShader,
                m_ProbeRelocationShader,
                m_ProbeClassificationShader,
                m_LightingCombinedShader))
            {
                Debug.LogError("[DDGIProbeUpdater] Failed to initialize raytracing manager");
                m_RaytracingManager.Dispose();
                m_RaytracingManager = null;
            }
            else
            {

                if (m_VariabilityReductionShader != null)
                {
                    m_RaytracingManager.SetVariabilityReductionShader(m_VariabilityReductionShader);
                }

                Debug.Log("[DDGIProbeUpdater] Raytracing manager initialized successfully");
            }
        }

        private void CleanupRaytracing()
        {
            if (m_RaytracingManager != null)
            {
                m_RaytracingManager.Dispose();
                m_RaytracingManager = null;
            }
        }

        private void OnUpdateModeChanged()
        {
            if (m_UpdateMode == ProbeUpdateMode.Raytracing && m_RaytracingSupported)
            {
                if (m_RaytracingManager == null && m_RayGenShader != null)
                {
                    InitializeRaytracing();
                }
            }

            var visualizer = GetComponent<DDGIProbeVisualizer>();
            if (visualizer != null)
            {
                visualizer.SetRaytracingManager(m_RaytracingManager);
            }
        }

        private void UpdateProbesRaytracing()
        {
            if (m_RaytracingManager == null || !m_RaytracingManager.IsInitialized)
                return;

            var atlasManager = m_Volume.AtlasManager;
            if (atlasManager == null || !atlasManager.IsInitialized)
                return;

            m_FrameCount++;

            var desc = m_Volume.Descriptor;
            if (desc.enableAdaptiveUpdate && desc.enableProbeVariability)
            {
                if (!m_RaytracingManager.ShouldUpdateThisFrame(m_FrameCount))
                    return;
            }

            bool transformChanged = HasVolumeTransformChanged();
            if (transformChanged)
            {
                m_RaytracingManager.MarkProbePositionsDirty();
                CacheVolumeTransform();
            }

            if (!m_RaytracingManager.UpdateAccelerationStructure())
                return;

            m_RaytracingManager.UpdateProbePositions();

            bool enableRelocation = desc.enableProbeRelocation;
            if (enableRelocation)
            {
                int updateInterval = desc.relocationUpdateInterval;
                enableRelocation = updateInterval <= 0 || m_FrameCount % (updateInterval + 1) == 0;
            }

            bool enableClassification = desc.enableProbeClassification;
            if (enableClassification)
            {
                int updateInterval = desc.classificationUpdateInterval;
                enableClassification = updateInterval <= 0 || m_FrameCount % (updateInterval + 1) == 0;
            }

            if (desc.enableProbeVariability)
            {
                m_RaytracingManager.EnsureVariabilityTexture();
            }

            m_RaytracingManager.DispatchFullUpdateImmediate(
                atlasManager.CurrentIrradianceAtlas,
                atlasManager.CurrentDistanceAtlas,
                atlasManager.PrevIrradianceAtlas,
                atlasManager.PrevDistanceAtlas,
                enableRelocation,
                enableClassification);

            atlasManager.SwapBuffers();
        }

        private bool HasVolumeTransformChanged()
        {
            if (m_Volume == null) return false;

            Transform t = m_Volume.transform;
            return t.position != m_LastVolumePosition ||
                   t.rotation != m_LastVolumeRotation ||
                   t.lossyScale != m_LastVolumeScale;
        }

        private void CacheVolumeTransform()
        {
            if (m_Volume == null) return;

            Transform t = m_Volume.transform;
            m_LastVolumePosition = t.position;
            m_LastVolumeRotation = t.rotation;
            m_LastVolumeScale = t.lossyScale;
        }

        private void RegisterResourceSnapshot()
        {
            var atlasManager = m_Volume.AtlasManager;
            var desc = m_Volume.Descriptor;

            bool valid = atlasManager != null
                      && atlasManager.IsInitialized
                      && atlasManager.IrradianceAtlas != null
                      && atlasManager.DistanceAtlas != null;

            if (!valid)
            {
                DDGIResourceProvider.Unregister();
                return;
            }

            var irradianceSize = atlasManager.IrradianceAtlasSize;
            var distanceSize = atlasManager.DistanceAtlasSize;

            var snapshot = new DDGIResourceSnapshot(
                irradianceAtlas:    atlasManager.IrradianceAtlas,
                distanceAtlas:      atlasManager.DistanceAtlas,
                origin:             m_Volume.transform.position,
                probeSpacing:       desc.probeSpacing,
                probeCounts:        desc.probeCounts,
                normalBias:         m_RaytracingManager != null ? m_RaytracingManager.NormalBias : m_NormalBias,
                viewBias:           desc.viewBias,
                irradianceGamma:    desc.irradianceGamma,
                irradianceProbeRes: atlasManager.Config.irradianceProbeResolution,
                distanceProbeRes:   atlasManager.Config.distanceProbeResolution,
                probesPerRow:       atlasManager.ProbesPerRow,
                irradianceTexelSize: new Vector2(1f / irradianceSize.x, 1f / irradianceSize.y),
                distanceTexelSize:   new Vector2(1f / distanceSize.x, 1f / distanceSize.y),
                isValid:            true
            );

            DDGIResourceProvider.Register(snapshot);
        }

        #endregion
    }
}
