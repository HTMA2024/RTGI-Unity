using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;

namespace DDGI
{

    public class DDGIRaytracingManager : IDisposable
    {
        #region 常量定义

        private const int DEFAULT_RAYS_PER_PROBE = 128;
        private const int DEFAULT_FIXED_RAY_COUNT = 32;
        private const float DEFAULT_RAY_MIN_DISTANCE = 0.01f;
        private const float DEFAULT_RAY_MAX_DISTANCE = 100f;

        #endregion

        #region 私有字段

        private DDGIVolume _volume;
        private RayTracingAccelerationStructure _accelerationStructure;
        private RayTracingShader _rayGenShader;

        private RenderTexture _gBufferPositionDistance;
        private RenderTexture _gBufferNormalHitFlag;
        private RenderTexture _gBufferAlbedoRoughness;
        private RenderTexture _gBufferEmissionMetallic;

        private RenderTexture _directIrradianceBuffer;
        private RenderTexture _indirectIrradianceBuffer;
        private RenderTexture _radianceBuffer;

        private ComputeShader _deferredLightingShader;
        private ComputeShader _indirectLightingShader;
        private ComputeShader _radianceCompositeShader;

        private ComputeShader _lightingCombinedShader;

        private ComputeShader _monteCarloIntegrationShader;

        private ComputeShader _probeRelocationShader;
        private RenderTexture _probeDataTexture;
        private int _probeDataWidth;
        private int _probeDataHeight;

        private ComputeShader _probeClassificationShader;

        private RenderTexture _probeVariabilityTexture;
        private RenderTexture[] _probeVariabilityReductionLevels;
        private ComputeShader _variabilityReductionShader;
        private int _kernelVariabilityReduction;
        private int _kernelVariabilityExtraReduction;
        private float _currentGlobalVariability;
        private int _currentUpdateInterval = 1;
        private bool _variabilityReadbackPending;
        private AsyncGPUReadbackRequest _variabilityReadbackRequest;

        private int _kernelDeferredLighting;
        private int _kernelIndirectLighting;
        private int _kernelRadianceComposite;
        private int _kernelLightingCombined;
        private int _kernelMonteCarloIntegration;
        private int _kernelMonteCarloIrradiance;
        private int _kernelMonteCarloDistance;
        private int _kernelUpdateBorderRT;
        private int _kernelUpdateBorderIrradiance;
        private int _kernelUpdateBorderDistance;
        private int _kernelProbeRelocation;
        private int _kernelProbeRelocationReset;
        private int _kernelProbeClassification;
        private int _kernelProbeClassificationReset;

        private ComputeBuffer _probePositionsBuffer;
        private Vector4[] _probePositionsCache;
        private bool _probePositionsDirty = true;

        private Light _cachedMainLight;
        private bool _mainLightCached;

        private const int MAX_ADDITIONAL_LIGHTS = 8;
        private Vector4[] _additionalLightPositions;
        private Vector4[] _additionalLightColors;
        private Vector4[] _additionalLightDirections;
        private Light[] _cachedAdditionalLights;
        private int _additionalLightCount;

        private bool _useShadows = true;
        private float _shadowStrength = 1f;
        private float _shadowBias = 0.005f;
        private float _shadowNormalBias = 0.4f;

        private int _raysPerProbe = DEFAULT_RAYS_PER_PROBE;
        private int _fixedRayCount = DEFAULT_FIXED_RAY_COUNT;
        private float _rayMinDistance = DEFAULT_RAY_MIN_DISTANCE;
        private float _rayMaxDistance = DEFAULT_RAY_MAX_DISTANCE;
        private float _skyboxIntensity = 1f;
        private float _indirectIntensity = 1f;
        private float _normalBias = 0.1f;
        private float _chebyshevBias = 0.001f;

        private int _gBufferWidth;
        private int _gBufferHeight;

        private uint _frameIndex;

        private Matrix4x4 _currentRayRotationMatrix = Matrix4x4.identity;

        private bool _isInitialized;

        private bool _usingSharedRTAS;

        private static class ShaderIDs
        {
            public static readonly int _DDGIAccelStruct = Shader.PropertyToID("_DDGIAccelStruct");
            public static readonly int _ProbePositions = Shader.PropertyToID("_ProbePositions");
            public static readonly int _ProbeCount = Shader.PropertyToID("_ProbeCount");
            public static readonly int _RaysPerProbe = Shader.PropertyToID("_RaysPerProbe");
            public static readonly int _FixedRayCount = Shader.PropertyToID("_FixedRayCount");
            public static readonly int _FrameIndex = Shader.PropertyToID("_FrameIndex");
            public static readonly int _RayMinDistance = Shader.PropertyToID("_RayMinDistance");
            public static readonly int _RayMaxDistance = Shader.PropertyToID("_RayMaxDistance");
            public static readonly int _RayRotationAngle = Shader.PropertyToID("_RayRotationAngle");
            public static readonly int _RayRotationMatrix = Shader.PropertyToID("_RayRotationMatrix");
            public static readonly int _SkyboxTexture = Shader.PropertyToID("_SkyboxTexture");
            public static readonly int _SkyboxIntensity = Shader.PropertyToID("_SkyboxIntensity");
            public static readonly int _GBuffer_PositionDistance = Shader.PropertyToID("_GBuffer_PositionDistance");
            public static readonly int _GBuffer_NormalHitFlag = Shader.PropertyToID("_GBuffer_NormalHitFlag");
            public static readonly int _GBuffer_AlbedoRoughness = Shader.PropertyToID("_GBuffer_AlbedoRoughness");
            public static readonly int _GBuffer_EmissionMetallic = Shader.PropertyToID("_GBuffer_EmissionMetallic");
            public static readonly int _GBufferWidth = Shader.PropertyToID("_GBufferWidth");
            public static readonly int _GBufferHeight = Shader.PropertyToID("_GBufferHeight");

            public static readonly int _DirectIrradianceBuffer = Shader.PropertyToID("_DirectIrradianceBuffer");
            public static readonly int _IndirectIrradianceBuffer = Shader.PropertyToID("_IndirectIrradianceBuffer");
            public static readonly int _RadianceBuffer = Shader.PropertyToID("_RadianceBuffer");
            public static readonly int _MainLightDirection = Shader.PropertyToID("_MainLightDirection");
            public static readonly int _MainLightColor = Shader.PropertyToID("_MainLightColor");
            public static readonly int _AdditionalLightCount = Shader.PropertyToID("_AdditionalLightCount");
            public static readonly int _AdditionalLightPositions = Shader.PropertyToID("_AdditionalLightPositions");
            public static readonly int _AdditionalLightColors = Shader.PropertyToID("_AdditionalLightColors");
            public static readonly int _AdditionalLightDirections = Shader.PropertyToID("_AdditionalLightDirections");
            public static readonly int _UseShadows = Shader.PropertyToID("_UseShadows");
            public static readonly int _MainLightShadowMap = Shader.PropertyToID("_MainLightShadowMap");
            public static readonly int _MainLightShadowParams = Shader.PropertyToID("_MainLightShadowParams");

            public static readonly int _MainLightWorldToShadow0 = Shader.PropertyToID("_MainLightWorldToShadow0");
            public static readonly int _MainLightWorldToShadow1 = Shader.PropertyToID("_MainLightWorldToShadow1");
            public static readonly int _MainLightWorldToShadow2 = Shader.PropertyToID("_MainLightWorldToShadow2");
            public static readonly int _MainLightWorldToShadow3 = Shader.PropertyToID("_MainLightWorldToShadow3");

            public static readonly int _CascadeShadowSplitSpheres0 = Shader.PropertyToID("_CascadeShadowSplitSpheres0");
            public static readonly int _CascadeShadowSplitSpheres1 = Shader.PropertyToID("_CascadeShadowSplitSpheres1");
            public static readonly int _CascadeShadowSplitSpheres2 = Shader.PropertyToID("_CascadeShadowSplitSpheres2");
            public static readonly int _CascadeShadowSplitSpheres3 = Shader.PropertyToID("_CascadeShadowSplitSpheres3");
            public static readonly int _CascadeShadowSplitSphereRadii = Shader.PropertyToID("_CascadeShadowSplitSphereRadii");
            public static readonly int _ShadowCascadeCount = Shader.PropertyToID("_ShadowCascadeCount");
            public static readonly int _IndirectIntensity = Shader.PropertyToID("_IndirectIntensity");
            public static readonly int _MaxRayDistance = Shader.PropertyToID("_MaxRayDistance");

            public static readonly int _IrradianceAtlas_Prev = Shader.PropertyToID("_IrradianceAtlas_Prev");
            public static readonly int _DistanceAtlas_Prev = Shader.PropertyToID("_DistanceAtlas_Prev");
            public static readonly int _IrradianceAtlasParams = Shader.PropertyToID("_IrradianceAtlasParams");
            public static readonly int _DistanceAtlasParams = Shader.PropertyToID("_DistanceAtlasParams");
            public static readonly int _VolumeOrigin = Shader.PropertyToID("_VolumeOrigin");
            public static readonly int _VolumeSpacing = Shader.PropertyToID("_VolumeSpacing");
            public static readonly int _VolumeProbeCounts = Shader.PropertyToID("_VolumeProbeCounts");
            public static readonly int _NormalBias = Shader.PropertyToID("_NormalBias");
            public static readonly int _ViewBias = Shader.PropertyToID("_ViewBias");
            public static readonly int _ChebyshevBias = Shader.PropertyToID("_ChebyshevBias");

            public static readonly int _IrradianceAtlas = Shader.PropertyToID("_IrradianceAtlas");
            public static readonly int _DistanceAtlas = Shader.PropertyToID("_DistanceAtlas");
            public static readonly int _IrradianceAtlasSize = Shader.PropertyToID("_IrradianceAtlasSize");
            public static readonly int _DistanceAtlasSize = Shader.PropertyToID("_DistanceAtlasSize");
            public static readonly int _GutterSize = Shader.PropertyToID("_GutterSize");
            public static readonly int _Hysteresis = Shader.PropertyToID("_Hysteresis");
            public static readonly int _IrradianceGamma = Shader.PropertyToID("_IrradianceGamma");
            public static readonly int _IrradianceThreshold = Shader.PropertyToID("_IrradianceThreshold");
            public static readonly int _BrightnessThreshold = Shader.PropertyToID("_BrightnessThreshold");

            public static readonly int _ProbeVariability = Shader.PropertyToID("_ProbeVariability");
            public static readonly int _VariabilityEnabled = Shader.PropertyToID("_VariabilityEnabled");
            public static readonly int _ProbeVariabilityAverage = Shader.PropertyToID("_ProbeVariabilityAverage");
            public static readonly int _ProbeVariabilityReadOnly = Shader.PropertyToID("_ProbeVariabilityReadOnly");
            public static readonly int _InputSize = Shader.PropertyToID("_InputSize");
            public static readonly int _OutputSize = Shader.PropertyToID("_OutputSize");

            public static readonly int _ProbeData = Shader.PropertyToID("_ProbeData");
            public static readonly int _ProbeDataWidth = Shader.PropertyToID("_ProbeDataWidth");
            public static readonly int _ProbeSpacing = Shader.PropertyToID("_ProbeSpacing");
            public static readonly int _MinFrontfaceDistance = Shader.PropertyToID("_MinFrontfaceDistance");
            public static readonly int _BackfaceThreshold = Shader.PropertyToID("_BackfaceThreshold");
        }

        #endregion

        #region 公共属性

        public int RaysPerProbe
        {
            get => _raysPerProbe;
            set
            {
                if (_raysPerProbe != value)
                {

                    _raysPerProbe = value;
                    if (_isInitialized) RecreateGBuffers();
                }
            }
        }

        public int FixedRayCount
        {
            get => _fixedRayCount;
            set => _fixedRayCount = Mathf.Clamp(value, 8, _raysPerProbe);
        }

        public float RayMinDistance
        {
            get => _rayMinDistance;
            set => _rayMinDistance = Mathf.Max(0.001f, value);
        }

        public float RayMaxDistance
        {
            get => _rayMaxDistance;
            set => _rayMaxDistance = Mathf.Max(_rayMinDistance + 0.1f, value);
        }

        public float SkyboxIntensity
        {
            get => _skyboxIntensity;
            set => _skyboxIntensity = Mathf.Max(0f, value);
        }

        public float IndirectIntensity
        {
            get => _indirectIntensity;
            set => _indirectIntensity = Mathf.Max(0f, value);
        }

        public float NormalBias
        {
            get => _normalBias;
            set => _normalBias = Mathf.Max(0f, value);
        }

        public float ChebyshevBias
        {
            get => _chebyshevBias;
            set => _chebyshevBias = Mathf.Max(0.0001f, value);
        }

        public bool UseShadows
        {
            get => _useShadows;
            set => _useShadows = value;
        }

        public float ShadowStrength
        {
            get => _shadowStrength;
            set => _shadowStrength = Mathf.Clamp01(value);
        }

        public float ShadowBias
        {
            get => _shadowBias;
            set => _shadowBias = Mathf.Max(0f, value);
        }

        public float ShadowNormalBias
        {
            get => _shadowNormalBias;
            set => _shadowNormalBias = Mathf.Max(0f, value);
        }

        public RenderTexture GBufferPositionDistance => _gBufferPositionDistance;

        public RenderTexture GBufferNormalHitFlag => _gBufferNormalHitFlag;

        public RenderTexture GBufferAlbedoRoughness => _gBufferAlbedoRoughness;

        public RenderTexture GBufferEmissionMetallic => _gBufferEmissionMetallic;

        public RenderTexture DirectIrradianceBuffer => _directIrradianceBuffer;

        public RenderTexture IndirectIrradianceBuffer => _indirectIrradianceBuffer;

        public RenderTexture RadianceBuffer => _radianceBuffer;

        public RenderTexture ProbeDataTexture => _probeDataTexture;

        public RenderTexture ProbeVariabilityTexture => _probeVariabilityTexture;

        public float CurrentGlobalVariability => _currentGlobalVariability;

        public int CurrentUpdateInterval => _currentUpdateInterval;

        public bool IsInitialized => _isInitialized;

        #endregion

        #region 构造和初始化

        public DDGIRaytracingManager(DDGIVolume volume)
        {
            _volume = volume ?? throw new ArgumentNullException(nameof(volume));
        }

        public bool Initialize(RayTracingShader rayGenShader,
                               ComputeShader deferredLightingShader = null,
                               ComputeShader indirectLightingShader = null,
                               ComputeShader radianceCompositeShader = null,
                               ComputeShader monteCarloIntegrationShader = null,
                               ComputeShader probeRelocationShader = null,
                               ComputeShader probeClassificationShader = null,
                               ComputeShader lightingCombinedShader = null)
        {
            if (_isInitialized)
            {
                Debug.LogWarning("[DDGIRaytracingManager] Already initialized");
                return true;
            }

            if (!SystemInfo.supportsRayTracing)
            {
                Debug.LogError("[DDGIRaytracingManager] Ray tracing is not supported on this device");
                return false;
            }

            _rayGenShader = rayGenShader;
            if (_rayGenShader == null)
            {
                Debug.LogError("[DDGIRaytracingManager] RayGen shader is null");
                return false;
            }

            _deferredLightingShader = deferredLightingShader;
            _indirectLightingShader = indirectLightingShader;
            _radianceCompositeShader = radianceCompositeShader;
            _monteCarloIntegrationShader = monteCarloIntegrationShader;
            _probeRelocationShader = probeRelocationShader;
            _probeClassificationShader = probeClassificationShader;
            _lightingCombinedShader = lightingCombinedShader;

            InitializeComputeShaders();

            var sharedRTAS = URPSSGI.RTASManager.SharedInstance;
            if (sharedRTAS != null && sharedRTAS.IsAvailable)
            {
                _accelerationStructure = sharedRTAS.AccelerationStructure;
                _usingSharedRTAS = true;
            }
            else
            {
                if (!CreateAccelerationStructure())
                {
                    Debug.LogError("[DDGIRaytracingManager] Failed to create acceleration structure");
                    return false;
                }
            }

            if (!CreateGBuffers())
            {
                Debug.LogError("[DDGIRaytracingManager] Failed to create G-Buffers");
                Dispose();
                return false;
            }

            if (!CreateLightingBuffers())
            {
                Debug.LogError("[DDGIRaytracingManager] Failed to create lighting buffers");
                Dispose();
                return false;
            }

            CreateProbePositionsBuffer();

            _additionalLightPositions = new Vector4[MAX_ADDITIONAL_LIGHTS];
            _additionalLightColors = new Vector4[MAX_ADDITIONAL_LIGHTS];
            _additionalLightDirections = new Vector4[MAX_ADDITIONAL_LIGHTS];
            _cachedAdditionalLights = new Light[MAX_ADDITIONAL_LIGHTS];

            if (_probeRelocationShader != null || _probeClassificationShader != null)
            {
                CreateProbeDataTexture();
            }

            if (_volume.Descriptor.enableProbeVariability)
            {
                CreateProbeVariabilityTexture();
                CreateProbeVariabilityReductionTextures();
            }

            _isInitialized = true;
            Debug.Log("[DDGIRaytracingManager] Initialized successfully");

            return true;
        }

        private void InitializeComputeShaders()
        {
            if (_deferredLightingShader != null)
            {
                _kernelDeferredLighting = _deferredLightingShader.FindKernel("CSDeferredLighting");
            }

            if (_indirectLightingShader != null)
            {
                _kernelIndirectLighting = _indirectLightingShader.FindKernel("CSIndirectLighting");
            }

            if (_radianceCompositeShader != null)
            {
                _kernelRadianceComposite = _radianceCompositeShader.FindKernel("CSRadianceComposite");
            }

            if (_lightingCombinedShader != null)
            {
                _kernelLightingCombined = _lightingCombinedShader.FindKernel("CSLightingCombined");
            }

            if (_monteCarloIntegrationShader != null)
            {
                _kernelMonteCarloIntegration = _monteCarloIntegrationShader.FindKernel("CSMonteCarloIntegration");
                _kernelMonteCarloIrradiance = _monteCarloIntegrationShader.FindKernel("CSMonteCarloIrradiance");
                _kernelMonteCarloDistance = _monteCarloIntegrationShader.FindKernel("CSMonteCarloDistance");
                _kernelUpdateBorderRT = _monteCarloIntegrationShader.FindKernel("CSUpdateBorderRT");
                _kernelUpdateBorderIrradiance = _monteCarloIntegrationShader.FindKernel("CSUpdateBorderIrradiance");
                _kernelUpdateBorderDistance = _monteCarloIntegrationShader.FindKernel("CSUpdateBorderDistance");
            }

            if (_probeRelocationShader != null)
            {
                _kernelProbeRelocation = _probeRelocationShader.FindKernel("CSProbeRelocation");
                _kernelProbeRelocationReset = _probeRelocationShader.FindKernel("CSProbeRelocationReset");
            }

            if (_probeClassificationShader != null)
            {
                _kernelProbeClassification = _probeClassificationShader.FindKernel("CSProbeClassification");
                _kernelProbeClassificationReset = _probeClassificationShader.FindKernel("CSProbeClassificationReset");
            }
        }

        #endregion

        #region 加速结构管理

        private bool CreateAccelerationStructure()
        {
            try
            {
                var settings = new RayTracingAccelerationStructure.RASSettings
                {
                    rayTracingModeMask = RayTracingAccelerationStructure.RayTracingModeMask.Everything,
                    managementMode = RayTracingAccelerationStructure.ManagementMode.Automatic,
                    layerMask = ~0
                };

                _accelerationStructure = new RayTracingAccelerationStructure(settings);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[DDGIRaytracingManager] Failed to create acceleration structure: {e.Message}");
                return false;
            }
        }

        public bool UpdateAccelerationStructure()
        {
            if (_usingSharedRTAS)
            {

                var shared = URPSSGI.RTASManager.SharedInstance;
                if (shared != null && shared.IsAvailable)
                {
                    shared.BuildNow();

                    if (shared.BuildFailed)
                        return false;
                }
                return true;
            }

            if (_accelerationStructure == null) return false;

            _accelerationStructure.Build();
            return true;
        }

        #endregion

        #region G-Buffer管理

        private bool CreateGBuffers()
        {
            int probeCount = _volume.Descriptor.TotalProbeCount;
            int totalRays = probeCount * _raysPerProbe;

            _gBufferWidth = Mathf.CeilToInt(Mathf.Sqrt(totalRays));
            _gBufferHeight = Mathf.CeilToInt((float)totalRays / _gBufferWidth);

            _gBufferWidth = Mathf.Max(1, _gBufferWidth);
            _gBufferHeight = Mathf.Max(1, _gBufferHeight);

            try
            {

                var posDistFormat = _volume.Descriptor.useHighPrecisionGBuffer
                    ? GraphicsFormat.R32G32B32A32_SFloat
                    : GraphicsFormat.R16G16B16A16_SFloat;
                _gBufferPositionDistance = CreateGBufferTexture(
                    "_GBuffer_PositionDistance",
                    posDistFormat
                );

                _gBufferNormalHitFlag = CreateGBufferTexture(
                    "_GBuffer_NormalHitFlag",
                    GraphicsFormat.R16G16B16A16_SFloat
                );

                _gBufferAlbedoRoughness = CreateGBufferTexture(
                    "_GBuffer_AlbedoRoughness",
                    GraphicsFormat.R16G16B16A16_SFloat
                );

                _gBufferEmissionMetallic = CreateGBufferTexture(
                    "_GBuffer_EmissionMetallic",
                    GraphicsFormat.R16G16B16A16_SFloat
                );

                Debug.Log($"[DDGIRaytracingManager] Created G-Buffers: {_gBufferWidth}x{_gBufferHeight} " +
                          $"(Probes: {probeCount}, Rays/Probe: {_raysPerProbe})");

                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[DDGIRaytracingManager] Failed to create G-Buffers: {e.Message}");
                ReleaseGBuffers();
                return false;
            }
        }

        private RenderTexture CreateGBufferTexture(string name, GraphicsFormat format)
        {
            var rt = new RenderTexture(_gBufferWidth, _gBufferHeight, 0, format)
            {
                name = name,
                enableRandomWrite = true,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            rt.Create();
            return rt;
        }

        private void RecreateGBuffers()
        {
            ReleaseGBuffers();
            CreateGBuffers();
        }

        private void ReleaseGBuffers()
        {
            ReleaseRenderTexture(ref _gBufferPositionDistance);
            ReleaseRenderTexture(ref _gBufferNormalHitFlag);
            ReleaseRenderTexture(ref _gBufferAlbedoRoughness);
            ReleaseRenderTexture(ref _gBufferEmissionMetallic);
        }

        private void ReleaseRenderTexture(ref RenderTexture rt)
        {
            if (rt != null)
            {
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
                rt = null;
            }
        }

        #endregion

        #region 光照缓冲区管理

        private bool CreateLightingBuffers()
        {
            try
            {

                if (_lightingCombinedShader != null)
                {

                    _radianceBuffer = CreateGBufferTexture(
                        "_RadianceBuffer",
                        GraphicsFormat.R16G16B16A16_SFloat
                    );

                    Debug.Log("[DDGIRaytracingManager] Created lighting buffers (combined mode, no intermediate buffers)");
                    return true;
                }

                _directIrradianceBuffer = CreateGBufferTexture(
                    "_DirectIrradianceBuffer",
                    GraphicsFormat.R16G16B16A16_SFloat
                );

                _indirectIrradianceBuffer = CreateGBufferTexture(
                    "_IndirectIrradianceBuffer",
                    GraphicsFormat.R16G16B16A16_SFloat
                );

                _radianceBuffer = CreateGBufferTexture(
                    "_RadianceBuffer",
                    GraphicsFormat.R16G16B16A16_SFloat
                );

                Debug.Log("[DDGIRaytracingManager] Created lighting buffers (legacy mode)");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[DDGIRaytracingManager] Failed to create lighting buffers: {e.Message}");
                ReleaseLightingBuffers();
                return false;
            }
        }

        private void ReleaseLightingBuffers()
        {
            ReleaseRenderTexture(ref _directIrradianceBuffer);
            ReleaseRenderTexture(ref _indirectIrradianceBuffer);
            ReleaseRenderTexture(ref _radianceBuffer);
        }

        #endregion

        #region Probe位置缓冲区

        private void CreateProbePositionsBuffer()
        {
            int probeCount = _volume.Descriptor.TotalProbeCount;
            _probePositionsBuffer = new ComputeBuffer(probeCount, sizeof(float) * 4);
            _probePositionsCache = new Vector4[probeCount];
            _probePositionsDirty = true;
        }

        public void MarkProbePositionsDirty()
        {
            _probePositionsDirty = true;
        }

        public void UpdateProbePositions()
        {
            if (_probePositionsBuffer == null || !_probePositionsDirty) return;

            int probeCount = _volume.ProbeCount;

            if (_probePositionsCache == null || _probePositionsCache.Length != probeCount)
            {
                _probePositionsCache = new Vector4[probeCount];
            }

            Vector3Int probeGridSize = _volume.Descriptor.probeCounts;
            Vector3 probeSpacing = _volume.Descriptor.probeSpacing;
            Vector3 volumeOrigin = _volume.transform.position;

            int index = 0;
            for (int z = 0; z < probeGridSize.z; z++)
            {
                for (int y = 0; y < probeGridSize.y; y++)
                {
                    for (int x = 0; x < probeGridSize.x; x++)
                    {
                        Vector3 localPos = new Vector3(
                            x * probeSpacing.x,
                            y * probeSpacing.y,
                            z * probeSpacing.z
                        );
                        Vector3 worldPos = volumeOrigin + localPos;
                        _probePositionsCache[index] = new Vector4(worldPos.x, worldPos.y, worldPos.z, index);
                        index++;
                    }
                }
            }

            _probePositionsBuffer.SetData(_probePositionsCache);
            _probePositionsDirty = false;
        }

        #endregion

        #region Probe Relocation

        private void CreateProbeDataTexture()
        {
            int probeCount = _volume.Descriptor.TotalProbeCount;

            _probeDataWidth = Mathf.CeilToInt(Mathf.Sqrt(probeCount));
            _probeDataHeight = Mathf.CeilToInt((float)probeCount / _probeDataWidth);

            _probeDataTexture = new RenderTexture(_probeDataWidth, _probeDataHeight, 0,
                GraphicsFormat.R32G32B32A32_SFloat)
            {
                name = "_ProbeData",
                enableRandomWrite = true,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            _probeDataTexture.Create();

            ResetProbeRelocation();

            Debug.Log($"[DDGIRaytracingManager] Created ProbeData texture: {_probeDataWidth}x{_probeDataHeight}");
        }

        public void ResetProbeRelocation()
        {
            if (_probeRelocationShader == null || _probeDataTexture == null)
                return;

            int probeCount = _volume.Descriptor.TotalProbeCount;
            int threadGroups = Mathf.CeilToInt(probeCount / 32f);

            _probeRelocationShader.SetTexture(_kernelProbeRelocationReset,
                ShaderIDs._ProbeData, _probeDataTexture);
            _probeRelocationShader.SetInt(ShaderIDs._ProbeCount, probeCount);

            _probeRelocationShader.Dispatch(_kernelProbeRelocationReset, threadGroups, 1, 1);
        }

        public void DispatchProbeRelocation(CommandBuffer cmd)
        {
            if (_probeRelocationShader == null || _probeDataTexture == null)
                return;

            var desc = _volume.Descriptor;
            if (!desc.enableProbeRelocation)
                return;

            int probeCount = desc.TotalProbeCount;
            int threadGroups = Mathf.CeilToInt(probeCount / 32f);

            cmd.SetComputeTextureParam(_probeRelocationShader, _kernelProbeRelocation,
                ShaderIDs._GBuffer_PositionDistance, _gBufferPositionDistance);
            cmd.SetComputeTextureParam(_probeRelocationShader, _kernelProbeRelocation,
                ShaderIDs._GBuffer_NormalHitFlag, _gBufferNormalHitFlag);

            cmd.SetComputeTextureParam(_probeRelocationShader, _kernelProbeRelocation,
                ShaderIDs._ProbeData, _probeDataTexture);

            cmd.SetComputeIntParam(_probeRelocationShader, ShaderIDs._ProbeCount, probeCount);
            cmd.SetComputeIntParam(_probeRelocationShader, ShaderIDs._RaysPerProbe, _raysPerProbe);
            cmd.SetComputeIntParam(_probeRelocationShader, ShaderIDs._FixedRayCount, _fixedRayCount);
            cmd.SetComputeIntParam(_probeRelocationShader, ShaderIDs._GBufferWidth, _gBufferWidth);
            cmd.SetComputeIntParam(_probeRelocationShader, ShaderIDs._ProbeDataWidth, _probeDataWidth);
            cmd.SetComputeVectorParam(_probeRelocationShader, ShaderIDs._ProbeSpacing,
                new Vector4(desc.probeSpacing.x, desc.probeSpacing.y, desc.probeSpacing.z, 0));
            cmd.SetComputeFloatParam(_probeRelocationShader, ShaderIDs._MinFrontfaceDistance,
                desc.probeMinFrontfaceDistance);
            cmd.SetComputeFloatParam(_probeRelocationShader, ShaderIDs._BackfaceThreshold,
                desc.probeBackfaceThreshold);

            cmd.DispatchCompute(_probeRelocationShader, _kernelProbeRelocation, threadGroups, 1, 1);
        }

        public void DispatchProbeRelocationImmediate()
        {
            CommandBuffer cmd = new CommandBuffer { name = "DDGI Probe Relocation" };
            DispatchProbeRelocation(cmd);
            Graphics.ExecuteCommandBuffer(cmd);
            cmd.Dispose();
        }

        private void ReleaseProbeDataTexture()
        {
            ReleaseRenderTexture(ref _probeDataTexture);
        }

        #endregion

        #region Probe Classification

        public void DispatchProbeClassification(CommandBuffer cmd)
        {
            if (_probeClassificationShader == null || _probeDataTexture == null)
                return;

            var desc = _volume.Descriptor;
            if (!desc.enableProbeClassification)
                return;

            int probeCount = desc.TotalProbeCount;
            int threadGroups = Mathf.CeilToInt(probeCount / 32f);

            cmd.SetComputeTextureParam(_probeClassificationShader, _kernelProbeClassification,
                ShaderIDs._GBuffer_PositionDistance, _gBufferPositionDistance);
            cmd.SetComputeTextureParam(_probeClassificationShader, _kernelProbeClassification,
                ShaderIDs._GBuffer_NormalHitFlag, _gBufferNormalHitFlag);

            cmd.SetComputeTextureParam(_probeClassificationShader, _kernelProbeClassification,
                ShaderIDs._ProbeData, _probeDataTexture);

            cmd.SetComputeIntParam(_probeClassificationShader, ShaderIDs._ProbeCount, probeCount);
            cmd.SetComputeIntParam(_probeClassificationShader, ShaderIDs._RaysPerProbe, _raysPerProbe);
            cmd.SetComputeIntParam(_probeClassificationShader, ShaderIDs._FixedRayCount, _fixedRayCount);
            cmd.SetComputeIntParam(_probeClassificationShader, ShaderIDs._GBufferWidth, _gBufferWidth);
            cmd.SetComputeIntParam(_probeClassificationShader, ShaderIDs._ProbeDataWidth, _probeDataWidth);
            cmd.SetComputeVectorParam(_probeClassificationShader, ShaderIDs._ProbeSpacing,
                new Vector4(desc.probeSpacing.x, desc.probeSpacing.y, desc.probeSpacing.z, 0));
            cmd.SetComputeFloatParam(_probeClassificationShader, ShaderIDs._BackfaceThreshold,
                desc.probeBackfaceThreshold);

            Vector3 classOrigin = _volume.transform.position;
            cmd.SetComputeVectorParam(_probeClassificationShader, ShaderIDs._VolumeOrigin,
                new Vector4(classOrigin.x, classOrigin.y, classOrigin.z, 0));
            cmd.SetComputeVectorParam(_probeClassificationShader, ShaderIDs._VolumeProbeCounts,
                new Vector4(desc.probeCounts.x, desc.probeCounts.y, desc.probeCounts.z, desc.TotalProbeCount));

            cmd.DispatchCompute(_probeClassificationShader, _kernelProbeClassification, threadGroups, 1, 1);
        }

        public void DispatchProbeClassificationImmediate()
        {
            CommandBuffer cmd = new CommandBuffer { name = "DDGI Probe Classification" };
            DispatchProbeClassification(cmd);
            Graphics.ExecuteCommandBuffer(cmd);
            cmd.Dispose();
        }

        public void ResetProbeClassification()
        {
            if (_probeClassificationShader == null || _probeDataTexture == null)
                return;

            int probeCount = _volume.Descriptor.TotalProbeCount;
            int threadGroups = Mathf.CeilToInt(probeCount / 32f);

            _probeClassificationShader.SetTexture(_kernelProbeClassificationReset,
                ShaderIDs._ProbeData, _probeDataTexture);
            _probeClassificationShader.SetInt(ShaderIDs._ProbeCount, probeCount);

            _probeClassificationShader.Dispatch(_kernelProbeClassificationReset, threadGroups, 1, 1);
        }

        #endregion

        #region Probe Variability

        private void CreateProbeVariabilityTexture()
        {
            var atlasManager = _volume.AtlasManager;
            if (atlasManager == null || !atlasManager.IsInitialized)
            {
                Debug.LogWarning("[DDGIRaytracingManager] Cannot create Variability texture: AtlasManager not initialized");
                return;
            }

            var irradianceSize = atlasManager.IrradianceAtlasSize;

            _probeVariabilityTexture = new RenderTexture(irradianceSize.x, irradianceSize.y, 0,
                UnityEngine.Experimental.Rendering.GraphicsFormat.R16_SFloat)
            {
                name = "_ProbeVariability",
                enableRandomWrite = true,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            _probeVariabilityTexture.Create();

            Debug.Log($"[DDGIRaytracingManager] Created ProbeVariability texture: {irradianceSize.x}x{irradianceSize.y}");
        }

        private void ReleaseProbeVariabilityTexture()
        {
            ReleaseRenderTexture(ref _probeVariabilityTexture);

            if (_probeVariabilityReductionLevels != null)
            {
                for (int i = 0; i < _probeVariabilityReductionLevels.Length; i++)
                {
                    if (_probeVariabilityReductionLevels[i] != null)
                    {
                        _probeVariabilityReductionLevels[i].Release();
                        UnityEngine.Object.DestroyImmediate(_probeVariabilityReductionLevels[i]);
                    }
                }
                _probeVariabilityReductionLevels = null;
            }
        }

        public void EnsureVariabilityTexture()
        {
            if (_probeVariabilityTexture != null)
                return;

            if (_volume.Descriptor.enableProbeVariability)
            {
                CreateProbeVariabilityTexture();
                CreateProbeVariabilityReductionTextures();
            }
        }

        private void CreateProbeVariabilityReductionTextures()
        {
            if (_probeVariabilityTexture == null)
                return;

            const int reductionFactor = 16;
            int width = _probeVariabilityTexture.width;
            int height = _probeVariabilityTexture.height;

            System.Collections.Generic.List<Vector2Int> levels = new System.Collections.Generic.List<Vector2Int>();
            int currentWidth = width;
            int currentHeight = height;

            while (currentWidth > 1 || currentHeight > 1)
            {

                currentWidth = Mathf.Max(1, Mathf.CeilToInt(currentWidth / (float)reductionFactor));
                currentHeight = Mathf.Max(1, Mathf.CeilToInt(currentHeight / (float)reductionFactor));
                levels.Add(new Vector2Int(currentWidth, currentHeight));
            }

            _probeVariabilityReductionLevels = new RenderTexture[levels.Count];
            for (int i = 0; i < levels.Count; i++)
            {
                var size = levels[i];
                _probeVariabilityReductionLevels[i] = new RenderTexture(size.x, size.y, 0,
                    UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16_SFloat)
                {
                    name = $"_ProbeVariabilityReduction_L{i}",
                    enableRandomWrite = true,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
                _probeVariabilityReductionLevels[i].Create();
            }

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append($"[DDGIRaytracingManager] Variability reduction chain: {width}x{height}");
            for (int i = 0; i < levels.Count; i++)
            {
                sb.Append($" -> {levels[i].x}x{levels[i].y}");
            }
            Debug.Log(sb.ToString());
        }

        public void SetVariabilityReductionShader(ComputeShader shader)
        {
            _variabilityReductionShader = shader;
            if (shader != null)
            {
                _kernelVariabilityReduction = shader.FindKernel("CSVariabilityReduction");
                _kernelVariabilityExtraReduction = shader.FindKernel("CSVariabilityExtraReduction");
            }
        }

        public void DispatchVariabilityReduction(CommandBuffer cmd)
        {
            if (!_volume.Descriptor.enableProbeVariability)
                return;

            if (_variabilityReductionShader == null || _probeVariabilityTexture == null)
                return;

            if (_probeVariabilityReductionLevels == null || _probeVariabilityReductionLevels.Length == 0)
                return;

            var desc = _volume.Descriptor;
            var atlasManager = _volume.AtlasManager;
            var atlasConfig = _volume.AtlasConfig;

            const int reductionFactor = 16;

            {
                int inputWidth = _probeVariabilityTexture.width;
                int inputHeight = _probeVariabilityTexture.height;
                var outputTex = _probeVariabilityReductionLevels[0];

                int groupsX = Mathf.CeilToInt(inputWidth / (float)reductionFactor);
                int groupsY = Mathf.CeilToInt(inputHeight / (float)reductionFactor);

                cmd.SetComputeTextureParam(_variabilityReductionShader, _kernelVariabilityReduction,
                    ShaderIDs._ProbeVariability, _probeVariabilityTexture);

                cmd.SetComputeTextureParam(_variabilityReductionShader, _kernelVariabilityReduction,
                    ShaderIDs._ProbeVariabilityAverage, outputTex);

                cmd.SetComputeIntParams(_variabilityReductionShader, ShaderIDs._InputSize, inputWidth, inputHeight);

                cmd.SetComputeIntParams(_variabilityReductionShader, ShaderIDs._IrradianceAtlasSize,
                    atlasManager.IrradianceAtlasSize.x, atlasManager.IrradianceAtlasSize.y,
                    atlasConfig.irradianceProbeResolution, atlasManager.ProbesPerRow);
                cmd.SetComputeIntParam(_variabilityReductionShader, ShaderIDs._GutterSize, atlasConfig.gutterSize);
                cmd.SetComputeIntParam(_variabilityReductionShader, ShaderIDs._ProbeCount, desc.TotalProbeCount);

                if (_probeDataTexture != null)
                {
                    cmd.SetComputeTextureParam(_variabilityReductionShader, _kernelVariabilityReduction,
                        ShaderIDs._ProbeData, _probeDataTexture);
                    cmd.SetComputeIntParam(_variabilityReductionShader, ShaderIDs._ProbeDataWidth, _probeDataWidth);
                }
                else
                {
                    cmd.SetComputeIntParam(_variabilityReductionShader, ShaderIDs._ProbeDataWidth, 0);
                }

                cmd.DispatchCompute(_variabilityReductionShader, _kernelVariabilityReduction, groupsX, groupsY, 1);
            }

            for (int level = 1; level < _probeVariabilityReductionLevels.Length; level++)
            {
                var inputTex = _probeVariabilityReductionLevels[level - 1];
                var outputTex = _probeVariabilityReductionLevels[level];

                int inputWidth = inputTex.width;
                int inputHeight = inputTex.height;

                int groupsX = Mathf.Max(1, Mathf.CeilToInt(inputWidth / (float)reductionFactor));
                int groupsY = Mathf.Max(1, Mathf.CeilToInt(inputHeight / (float)reductionFactor));

                cmd.SetComputeTextureParam(_variabilityReductionShader, _kernelVariabilityExtraReduction,
                    ShaderIDs._ProbeVariabilityReadOnly, inputTex);

                cmd.SetComputeTextureParam(_variabilityReductionShader, _kernelVariabilityExtraReduction,
                    ShaderIDs._ProbeVariabilityAverage, outputTex);

                cmd.SetComputeIntParams(_variabilityReductionShader, ShaderIDs._InputSize, inputWidth, inputHeight);

                cmd.DispatchCompute(_variabilityReductionShader, _kernelVariabilityExtraReduction, groupsX, groupsY, 1);
            }

            RequestVariabilityReadbackViaCmd(cmd);
        }

        private void RequestVariabilityReadbackViaCmd(CommandBuffer cmd)
        {

            if (_variabilityReadbackPending)
                return;

            if (_probeVariabilityReductionLevels == null || _probeVariabilityReductionLevels.Length == 0)
                return;

            var finalLevel = _probeVariabilityReductionLevels[_probeVariabilityReductionLevels.Length - 1];
            if (finalLevel == null || !finalLevel.IsCreated())
                return;

            cmd.RequestAsyncReadback(finalLevel, 0, TextureFormat.RGHalf, request =>
            {
                if (!request.hasError && request.done)
                {
                    try
                    {

                        var rawData = request.GetData<ushort>();
                        if (rawData.Length >= 2)
                        {

                            float rValue = Mathf.HalfToFloat(rawData[0]);
                            float gValue = Mathf.HalfToFloat(rawData[1]);

                            _currentGlobalVariability = rValue;
                            UpdateAdaptiveInterval();

                        }
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogWarning($"[DDGIRaytracingManager] Variability readback failed: {e.Message}");
                    }
                }
                _variabilityReadbackPending = false;
            });
            _variabilityReadbackPending = true;
        }

        public void RequestVariabilityReadbackImmediate()
        {

            if (_variabilityReadbackPending)
                return;

            if (_probeVariabilityReductionLevels == null || _probeVariabilityReductionLevels.Length == 0)
                return;

            var finalLevel = _probeVariabilityReductionLevels[_probeVariabilityReductionLevels.Length - 1];
            if (finalLevel == null || !finalLevel.IsCreated())
                return;

            _variabilityReadbackRequest = AsyncGPUReadback.Request(finalLevel, 0, TextureFormat.RGHalf, request =>
            {
                if (!request.hasError && request.done)
                {
                    try
                    {
                        var rawData = request.GetData<ushort>();
                        if (rawData.Length >= 2)
                        {
                            float rValue = Mathf.HalfToFloat(rawData[0]);
                            _currentGlobalVariability = rValue;
                            UpdateAdaptiveInterval();
                        }
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogWarning($"[DDGIRaytracingManager] Variability readback failed: {e.Message}");
                    }
                }
                _variabilityReadbackPending = false;
            });
            _variabilityReadbackPending = true;
        }

        private void UpdateAdaptiveInterval()
        {
            var desc = _volume.Descriptor;
            if (!desc.enableAdaptiveUpdate)
            {
                _currentUpdateInterval = 1;
                return;
            }

            float lowThreshold = desc.lowVariabilityThreshold;
            float highThreshold = desc.highVariabilityThreshold;
            int minInterval = desc.minUpdateInterval;
            int maxInterval = desc.maxUpdateInterval;

            if (_currentGlobalVariability < lowThreshold)
            {

                _currentUpdateInterval = maxInterval;
            }
            else if (_currentGlobalVariability > highThreshold)
            {

                _currentUpdateInterval = minInterval;
            }
            else
            {

                float t = (_currentGlobalVariability - lowThreshold) / (highThreshold - lowThreshold);
                _currentUpdateInterval = Mathf.RoundToInt(Mathf.Lerp(maxInterval, minInterval, t));
            }
        }

        public bool ShouldUpdateThisFrame(int frameIndex)
        {
            if (!_volume.Descriptor.enableAdaptiveUpdate)
                return true;

            return frameIndex % _currentUpdateInterval == 0;
        }

        #endregion

        #region 光线追踪执行

        public void DispatchRays(CommandBuffer cmd)
        {
            if (!_isInitialized || _rayGenShader == null)
            {
                Debug.LogWarning("[DDGIRaytracingManager] Not initialized or shader is null");
                return;
            }

            _frameIndex++;

            float h2 = HaltonSequence(_frameIndex, 2);
            float h3 = HaltonSequence(_frameIndex, 3);
            float h5 = HaltonSequence(_frameIndex, 5);

            float cosTheta = 1f - 2f * h2;
            float sinTheta = Mathf.Sqrt(Mathf.Max(0f, 1f - cosTheta * cosTheta));
            float axisPhi = h3 * Mathf.PI * 2f;
            Vector3 rotAxis = new Vector3(
                Mathf.Cos(axisPhi) * sinTheta,
                cosTheta,
                Mathf.Sin(axisPhi) * sinTheta
            );

            float rotAngleDeg = h5 * 360f;

            _currentRayRotationMatrix = Matrix4x4.Rotate(
                Quaternion.AngleAxis(rotAngleDeg, rotAxis)
            );

            cmd.SetRayTracingShaderPass(_rayGenShader, "DDGIRayTracing");

            cmd.SetRayTracingAccelerationStructure(_rayGenShader, ShaderIDs._DDGIAccelStruct, _accelerationStructure);

            cmd.SetRayTracingBufferParam(_rayGenShader, ShaderIDs._ProbePositions, _probePositionsBuffer);
            cmd.SetRayTracingIntParam(_rayGenShader, ShaderIDs._ProbeCount, _volume.Descriptor.TotalProbeCount);
            cmd.SetRayTracingIntParam(_rayGenShader, ShaderIDs._RaysPerProbe, _raysPerProbe);
            cmd.SetRayTracingIntParam(_rayGenShader, ShaderIDs._FixedRayCount, _fixedRayCount);
            cmd.SetRayTracingIntParam(_rayGenShader, ShaderIDs._FrameIndex, (int)_frameIndex);

            cmd.SetRayTracingFloatParam(_rayGenShader, ShaderIDs._RayMinDistance, _rayMinDistance);
            cmd.SetRayTracingFloatParam(_rayGenShader, ShaderIDs._RayMaxDistance, _rayMaxDistance);
            cmd.SetRayTracingFloatParam(_rayGenShader, ShaderIDs._RayRotationAngle, rotAngleDeg * Mathf.Deg2Rad);
            cmd.SetRayTracingMatrixParam(_rayGenShader, ShaderIDs._RayRotationMatrix, _currentRayRotationMatrix);

            Cubemap skybox = RenderSettings.skybox?.GetTexture("_Tex") as Cubemap;
            if (skybox != null)
            {
                cmd.SetRayTracingTextureParam(_rayGenShader, ShaderIDs._SkyboxTexture, skybox);
            }
            cmd.SetRayTracingFloatParam(_rayGenShader, ShaderIDs._SkyboxIntensity, _skyboxIntensity);

            if (_probeDataTexture != null)
            {
                cmd.SetRayTracingTextureParam(_rayGenShader, ShaderIDs._ProbeData, _probeDataTexture);
                cmd.SetRayTracingIntParam(_rayGenShader, ShaderIDs._ProbeDataWidth, _probeDataWidth);
            }

            cmd.SetRayTracingTextureParam(_rayGenShader, ShaderIDs._GBuffer_PositionDistance, _gBufferPositionDistance);
            cmd.SetRayTracingTextureParam(_rayGenShader, ShaderIDs._GBuffer_NormalHitFlag, _gBufferNormalHitFlag);
            cmd.SetRayTracingTextureParam(_rayGenShader, ShaderIDs._GBuffer_AlbedoRoughness, _gBufferAlbedoRoughness);
            cmd.SetRayTracingTextureParam(_rayGenShader, ShaderIDs._GBuffer_EmissionMetallic, _gBufferEmissionMetallic);
            cmd.SetRayTracingIntParam(_rayGenShader, ShaderIDs._GBufferWidth, _gBufferWidth);
            cmd.SetRayTracingIntParam(_rayGenShader, ShaderIDs._GBufferHeight, _gBufferHeight);

            int totalRays = _volume.Descriptor.TotalProbeCount * _raysPerProbe;
            cmd.DispatchRays(_rayGenShader, "DDGIRayGenShader", (uint)totalRays, 1, 1);
        }

        public void DispatchRaysImmediate()
        {
            CommandBuffer cmd = new CommandBuffer { name = "DDGI RayTracing" };
            DispatchRays(cmd);
            Graphics.ExecuteCommandBuffer(cmd);
            cmd.Dispose();
        }

        #endregion

        #region Phase 2: 光照计算

        public void DispatchLightingCompute(CommandBuffer cmd, RenderTexture irradianceAtlasPrev, RenderTexture distanceAtlasPrev)
        {
            if (!_isInitialized)
                return;

            if (_lightingCombinedShader != null)
            {
                DispatchLightingCombined(cmd, irradianceAtlasPrev, distanceAtlasPrev);
                return;
            }

            DispatchDeferredLighting(cmd);

            DispatchIndirectLighting(cmd, irradianceAtlasPrev, distanceAtlasPrev);

            DispatchRadianceComposite(cmd);
        }

        public void DispatchDeferredLighting(CommandBuffer cmd)
        {
            if (_deferredLightingShader == null)
                return;

            int totalRays = _volume.Descriptor.TotalProbeCount * _raysPerProbe;
            int threadGroups = Mathf.CeilToInt(totalRays / 64f);

            cmd.SetComputeTextureParam(_deferredLightingShader, _kernelDeferredLighting,
                ShaderIDs._GBuffer_PositionDistance, _gBufferPositionDistance);
            cmd.SetComputeTextureParam(_deferredLightingShader, _kernelDeferredLighting,
                ShaderIDs._GBuffer_NormalHitFlag, _gBufferNormalHitFlag);

            cmd.SetComputeTextureParam(_deferredLightingShader, _kernelDeferredLighting,
                ShaderIDs._DirectIrradianceBuffer, _directIrradianceBuffer);

            cmd.SetComputeIntParam(_deferredLightingShader, ShaderIDs._GBufferWidth, _gBufferWidth);
            cmd.SetComputeIntParam(_deferredLightingShader, ShaderIDs._GBufferHeight, _gBufferHeight);
            cmd.SetComputeIntParam(_deferredLightingShader, ShaderIDs._RaysPerProbe, _raysPerProbe);
            cmd.SetComputeIntParam(_deferredLightingShader, ShaderIDs._ProbeCount, _volume.Descriptor.TotalProbeCount);

            Light mainLight = GetMainLight();

            if (mainLight != null)
            {
                Vector3 lightDir = -mainLight.transform.forward;
                Color lightColor = mainLight.color;
                float lightIntensity = mainLight.intensity;

                cmd.SetComputeVectorParam(_deferredLightingShader, ShaderIDs._MainLightDirection,
                    new Vector4(lightDir.x, lightDir.y, lightDir.z, 0));
                cmd.SetComputeVectorParam(_deferredLightingShader, ShaderIDs._MainLightColor,
                    new Vector4(lightColor.r, lightColor.g, lightColor.b, lightIntensity));
            }
            else
            {
                cmd.SetComputeVectorParam(_deferredLightingShader, ShaderIDs._MainLightDirection,
                    new Vector4(0, 1, 0, 0));
                cmd.SetComputeVectorParam(_deferredLightingShader, ShaderIDs._MainLightColor,
                    new Vector4(1, 1, 1, 0));
            }

            bool shadowEnabled = _useShadows && mainLight != null && mainLight.shadows != LightShadows.None;
            cmd.SetComputeIntParam(_deferredLightingShader, ShaderIDs._UseShadows, shadowEnabled ? 1 : 0);

            if (shadowEnabled)
            {

                RenderTexture shadowMap = GetMainLightShadowMap();

                if (shadowMap != null)
                {
                    cmd.SetComputeTextureParam(_deferredLightingShader, _kernelDeferredLighting,
                        ShaderIDs._MainLightShadowMap, shadowMap);

                    Matrix4x4[] cascadeMatrices = GetCascadeShadowMatrices();
                    cmd.SetComputeMatrixParam(_deferredLightingShader, ShaderIDs._MainLightWorldToShadow0, cascadeMatrices[0]);
                    cmd.SetComputeMatrixParam(_deferredLightingShader, ShaderIDs._MainLightWorldToShadow1, cascadeMatrices[1]);
                    cmd.SetComputeMatrixParam(_deferredLightingShader, ShaderIDs._MainLightWorldToShadow2, cascadeMatrices[2]);
                    cmd.SetComputeMatrixParam(_deferredLightingShader, ShaderIDs._MainLightWorldToShadow3, cascadeMatrices[3]);

                    Vector4 sphere0 = Shader.GetGlobalVector("_CascadeShadowSplitSpheres0");
                    Vector4 sphere1 = Shader.GetGlobalVector("_CascadeShadowSplitSpheres1");
                    Vector4 sphere2 = Shader.GetGlobalVector("_CascadeShadowSplitSpheres2");
                    Vector4 sphere3 = Shader.GetGlobalVector("_CascadeShadowSplitSpheres3");
                    Vector4 sphereRadii = Shader.GetGlobalVector("_CascadeShadowSplitSphereRadii");

                    cmd.SetComputeVectorParam(_deferredLightingShader, ShaderIDs._CascadeShadowSplitSpheres0, sphere0);
                    cmd.SetComputeVectorParam(_deferredLightingShader, ShaderIDs._CascadeShadowSplitSpheres1, sphere1);
                    cmd.SetComputeVectorParam(_deferredLightingShader, ShaderIDs._CascadeShadowSplitSpheres2, sphere2);
                    cmd.SetComputeVectorParam(_deferredLightingShader, ShaderIDs._CascadeShadowSplitSpheres3, sphere3);
                    cmd.SetComputeVectorParam(_deferredLightingShader, ShaderIDs._CascadeShadowSplitSphereRadii, sphereRadii);

                    int cascadeCount = GetCascadeCount();
                    cmd.SetComputeIntParam(_deferredLightingShader, ShaderIDs._ShadowCascadeCount, cascadeCount);

                    Vector4 urpShadowParams = Shader.GetGlobalVector("_MainLightShadowParams");
                    cmd.SetComputeVectorParam(_deferredLightingShader, ShaderIDs._MainLightShadowParams,
                        new Vector4(urpShadowParams.x, 0, _shadowNormalBias, 0));
                }
                else
                {

                    cmd.SetComputeIntParam(_deferredLightingShader, ShaderIDs._UseShadows, 0);
                    cmd.SetComputeTextureParam(_deferredLightingShader, _kernelDeferredLighting,
                        ShaderIDs._MainLightShadowMap, Texture2D.whiteTexture);
                }
            }
            else
            {

                cmd.SetComputeTextureParam(_deferredLightingShader, _kernelDeferredLighting,
                    ShaderIDs._MainLightShadowMap, Texture2D.whiteTexture);
            }

            CollectAdditionalLights();

            cmd.SetComputeIntParam(_deferredLightingShader, ShaderIDs._AdditionalLightCount, _additionalLightCount);

            if (_additionalLightCount > 0)
            {
                cmd.SetComputeVectorArrayParam(_deferredLightingShader, ShaderIDs._AdditionalLightPositions, _additionalLightPositions);
                cmd.SetComputeVectorArrayParam(_deferredLightingShader, ShaderIDs._AdditionalLightColors, _additionalLightColors);
                cmd.SetComputeVectorArrayParam(_deferredLightingShader, ShaderIDs._AdditionalLightDirections, _additionalLightDirections);
            }

            cmd.DispatchCompute(_deferredLightingShader, _kernelDeferredLighting, threadGroups, 1, 1);
        }

        public void DispatchIndirectLighting(CommandBuffer cmd, RenderTexture irradianceAtlasPrev, RenderTexture distanceAtlasPrev)
        {
            if (_indirectLightingShader == null || irradianceAtlasPrev == null || distanceAtlasPrev == null)
                return;

            int totalRays = _volume.Descriptor.TotalProbeCount * _raysPerProbe;
            int threadGroups = Mathf.CeilToInt(totalRays / 64f);

            cmd.SetComputeTextureParam(_indirectLightingShader, _kernelIndirectLighting,
                ShaderIDs._GBuffer_PositionDistance, _gBufferPositionDistance);
            cmd.SetComputeTextureParam(_indirectLightingShader, _kernelIndirectLighting,
                ShaderIDs._GBuffer_NormalHitFlag, _gBufferNormalHitFlag);

            cmd.SetComputeTextureParam(_indirectLightingShader, _kernelIndirectLighting,
                ShaderIDs._IrradianceAtlas_Prev, irradianceAtlasPrev);
            cmd.SetComputeTextureParam(_indirectLightingShader, _kernelIndirectLighting,
                ShaderIDs._DistanceAtlas_Prev, distanceAtlasPrev);

            cmd.SetComputeTextureParam(_indirectLightingShader, _kernelIndirectLighting,
                ShaderIDs._IndirectIrradianceBuffer, _indirectIrradianceBuffer);

            cmd.SetComputeIntParam(_indirectLightingShader, ShaderIDs._GBufferWidth, _gBufferWidth);
            cmd.SetComputeIntParam(_indirectLightingShader, ShaderIDs._GBufferHeight, _gBufferHeight);

            var desc = _volume.Descriptor;
            Vector3 volumeOrigin = _volume.transform.position;
            cmd.SetComputeVectorParam(_indirectLightingShader, ShaderIDs._VolumeOrigin,
                new Vector4(volumeOrigin.x, volumeOrigin.y, volumeOrigin.z, 0));
            cmd.SetComputeVectorParam(_indirectLightingShader, ShaderIDs._VolumeSpacing,
                new Vector4(desc.probeSpacing.x, desc.probeSpacing.y, desc.probeSpacing.z, 0));
            cmd.SetComputeVectorParam(_indirectLightingShader, ShaderIDs._VolumeProbeCounts,
                new Vector4(desc.probeCounts.x, desc.probeCounts.y, desc.probeCounts.z, desc.TotalProbeCount));

            if (_volume.AtlasManager != null)
            {
                cmd.SetComputeVectorParam(_indirectLightingShader, ShaderIDs._IrradianceAtlasParams,
                    _volume.AtlasManager.GetIrradianceAtlasParams());
                cmd.SetComputeVectorParam(_indirectLightingShader, ShaderIDs._DistanceAtlasParams,
                    _volume.AtlasManager.GetDistanceAtlasParams());
            }

            cmd.SetComputeFloatParam(_indirectLightingShader, ShaderIDs._NormalBias, _normalBias);
            cmd.SetComputeFloatParam(_indirectLightingShader, ShaderIDs._ViewBias, desc.viewBias);
            cmd.SetComputeFloatParam(_indirectLightingShader, ShaderIDs._ChebyshevBias, _chebyshevBias);
            cmd.SetComputeFloatParam(_indirectLightingShader, ShaderIDs._IrradianceGamma, _volume.Descriptor.irradianceGamma);

            if (_probeDataTexture != null && desc.enableProbeClassification)
            {
                cmd.SetComputeTextureParam(_indirectLightingShader, _kernelIndirectLighting,
                    ShaderIDs._ProbeData, _probeDataTexture);
                cmd.SetComputeIntParam(_indirectLightingShader, ShaderIDs._ProbeDataWidth, _probeDataWidth);
            }
            else
            {

                cmd.SetComputeIntParam(_indirectLightingShader, ShaderIDs._ProbeDataWidth, 0);
            }

            cmd.DispatchCompute(_indirectLightingShader, _kernelIndirectLighting, threadGroups, 1, 1);
        }

        public void DispatchRadianceComposite(CommandBuffer cmd)
        {
            if (_radianceCompositeShader == null)
                return;

            int totalRays = _volume.Descriptor.TotalProbeCount * _raysPerProbe;
            int threadGroups = Mathf.CeilToInt(totalRays / 64f);

            cmd.SetComputeTextureParam(_radianceCompositeShader, _kernelRadianceComposite,
                ShaderIDs._GBuffer_PositionDistance, _gBufferPositionDistance);
            cmd.SetComputeTextureParam(_radianceCompositeShader, _kernelRadianceComposite,
                ShaderIDs._GBuffer_NormalHitFlag, _gBufferNormalHitFlag);
            cmd.SetComputeTextureParam(_radianceCompositeShader, _kernelRadianceComposite,
                ShaderIDs._GBuffer_AlbedoRoughness, _gBufferAlbedoRoughness);
            cmd.SetComputeTextureParam(_radianceCompositeShader, _kernelRadianceComposite,
                ShaderIDs._GBuffer_EmissionMetallic, _gBufferEmissionMetallic);

            cmd.SetComputeTextureParam(_radianceCompositeShader, _kernelRadianceComposite,
                ShaderIDs._DirectIrradianceBuffer, _directIrradianceBuffer);
            cmd.SetComputeTextureParam(_radianceCompositeShader, _kernelRadianceComposite,
                ShaderIDs._IndirectIrradianceBuffer, _indirectIrradianceBuffer);

            cmd.SetComputeTextureParam(_radianceCompositeShader, _kernelRadianceComposite,
                ShaderIDs._RadianceBuffer, _radianceBuffer);

            cmd.SetComputeIntParam(_radianceCompositeShader, ShaderIDs._GBufferWidth, _gBufferWidth);
            cmd.SetComputeIntParam(_radianceCompositeShader, ShaderIDs._GBufferHeight, _gBufferHeight);
            cmd.SetComputeIntParam(_radianceCompositeShader, ShaderIDs._RaysPerProbe, _raysPerProbe);
            cmd.SetComputeIntParam(_radianceCompositeShader, ShaderIDs._ProbeCount, _volume.Descriptor.TotalProbeCount);
            cmd.SetComputeFloatParam(_radianceCompositeShader, ShaderIDs._IndirectIntensity, _indirectIntensity);
            cmd.SetComputeFloatParam(_radianceCompositeShader, ShaderIDs._MaxRayDistance, _rayMaxDistance);

            cmd.DispatchCompute(_radianceCompositeShader, _kernelRadianceComposite, threadGroups, 1, 1);
        }

        public void DispatchLightingCombined(CommandBuffer cmd, RenderTexture irradianceAtlasPrev, RenderTexture distanceAtlasPrev)
        {
            if (_lightingCombinedShader == null)
                return;

            int totalRays = _volume.Descriptor.TotalProbeCount * _raysPerProbe;
            int threadGroups = Mathf.CeilToInt(totalRays / 64f);

            cmd.SetComputeTextureParam(_lightingCombinedShader, _kernelLightingCombined,
                ShaderIDs._GBuffer_PositionDistance, _gBufferPositionDistance);
            cmd.SetComputeTextureParam(_lightingCombinedShader, _kernelLightingCombined,
                ShaderIDs._GBuffer_NormalHitFlag, _gBufferNormalHitFlag);
            cmd.SetComputeTextureParam(_lightingCombinedShader, _kernelLightingCombined,
                ShaderIDs._GBuffer_AlbedoRoughness, _gBufferAlbedoRoughness);
            cmd.SetComputeTextureParam(_lightingCombinedShader, _kernelLightingCombined,
                ShaderIDs._GBuffer_EmissionMetallic, _gBufferEmissionMetallic);

            cmd.SetComputeTextureParam(_lightingCombinedShader, _kernelLightingCombined,
                ShaderIDs._RadianceBuffer, _radianceBuffer);

            cmd.SetComputeIntParam(_lightingCombinedShader, ShaderIDs._GBufferWidth, _gBufferWidth);
            cmd.SetComputeIntParam(_lightingCombinedShader, ShaderIDs._GBufferHeight, _gBufferHeight);
            cmd.SetComputeIntParam(_lightingCombinedShader, ShaderIDs._RaysPerProbe, _raysPerProbe);
            cmd.SetComputeIntParam(_lightingCombinedShader, ShaderIDs._ProbeCount, _volume.Descriptor.TotalProbeCount);

            Light mainLight = GetMainLight();

            if (mainLight != null)
            {
                Vector3 lightDir = -mainLight.transform.forward;
                Color lightColor = mainLight.color;
                float lightIntensity = mainLight.intensity;

                cmd.SetComputeVectorParam(_lightingCombinedShader, ShaderIDs._MainLightDirection,
                    new Vector4(lightDir.x, lightDir.y, lightDir.z, 0));
                cmd.SetComputeVectorParam(_lightingCombinedShader, ShaderIDs._MainLightColor,
                    new Vector4(lightColor.r, lightColor.g, lightColor.b, lightIntensity));
            }
            else
            {
                cmd.SetComputeVectorParam(_lightingCombinedShader, ShaderIDs._MainLightDirection,
                    new Vector4(0, 1, 0, 0));
                cmd.SetComputeVectorParam(_lightingCombinedShader, ShaderIDs._MainLightColor,
                    new Vector4(1, 1, 1, 0));
            }

            bool shadowEnabled = _useShadows && mainLight != null && mainLight.shadows != LightShadows.None;
            cmd.SetComputeIntParam(_lightingCombinedShader, ShaderIDs._UseShadows, shadowEnabled ? 1 : 0);

            if (shadowEnabled)
            {
                RenderTexture shadowMap = GetMainLightShadowMap();

                if (shadowMap != null)
                {
                    cmd.SetComputeTextureParam(_lightingCombinedShader, _kernelLightingCombined,
                        ShaderIDs._MainLightShadowMap, shadowMap);

                    Matrix4x4[] cascadeMatrices = GetCascadeShadowMatrices();
                    cmd.SetComputeMatrixParam(_lightingCombinedShader, ShaderIDs._MainLightWorldToShadow0, cascadeMatrices[0]);
                    cmd.SetComputeMatrixParam(_lightingCombinedShader, ShaderIDs._MainLightWorldToShadow1, cascadeMatrices[1]);
                    cmd.SetComputeMatrixParam(_lightingCombinedShader, ShaderIDs._MainLightWorldToShadow2, cascadeMatrices[2]);
                    cmd.SetComputeMatrixParam(_lightingCombinedShader, ShaderIDs._MainLightWorldToShadow3, cascadeMatrices[3]);

                    Vector4 sphere0 = Shader.GetGlobalVector("_CascadeShadowSplitSpheres0");
                    Vector4 sphere1 = Shader.GetGlobalVector("_CascadeShadowSplitSpheres1");
                    Vector4 sphere2 = Shader.GetGlobalVector("_CascadeShadowSplitSpheres2");
                    Vector4 sphere3 = Shader.GetGlobalVector("_CascadeShadowSplitSpheres3");
                    Vector4 sphereRadii = Shader.GetGlobalVector("_CascadeShadowSplitSphereRadii");

                    cmd.SetComputeVectorParam(_lightingCombinedShader, ShaderIDs._CascadeShadowSplitSpheres0, sphere0);
                    cmd.SetComputeVectorParam(_lightingCombinedShader, ShaderIDs._CascadeShadowSplitSpheres1, sphere1);
                    cmd.SetComputeVectorParam(_lightingCombinedShader, ShaderIDs._CascadeShadowSplitSpheres2, sphere2);
                    cmd.SetComputeVectorParam(_lightingCombinedShader, ShaderIDs._CascadeShadowSplitSpheres3, sphere3);
                    cmd.SetComputeVectorParam(_lightingCombinedShader, ShaderIDs._CascadeShadowSplitSphereRadii, sphereRadii);

                    int cascadeCount = GetCascadeCount();
                    cmd.SetComputeIntParam(_lightingCombinedShader, ShaderIDs._ShadowCascadeCount, cascadeCount);

                    Vector4 urpShadowParams = Shader.GetGlobalVector("_MainLightShadowParams");
                    cmd.SetComputeVectorParam(_lightingCombinedShader, ShaderIDs._MainLightShadowParams,
                        new Vector4(urpShadowParams.x, 0, _shadowNormalBias, 0));
                }
                else
                {

                    cmd.SetComputeIntParam(_lightingCombinedShader, ShaderIDs._UseShadows, 0);
                    cmd.SetComputeTextureParam(_lightingCombinedShader, _kernelLightingCombined,
                        ShaderIDs._MainLightShadowMap, Texture2D.whiteTexture);
                }
            }
            else
            {

                cmd.SetComputeTextureParam(_lightingCombinedShader, _kernelLightingCombined,
                    ShaderIDs._MainLightShadowMap, Texture2D.whiteTexture);
            }

            CollectAdditionalLights();

            cmd.SetComputeIntParam(_lightingCombinedShader, ShaderIDs._AdditionalLightCount, _additionalLightCount);

            if (_additionalLightCount > 0)
            {
                cmd.SetComputeVectorArrayParam(_lightingCombinedShader, ShaderIDs._AdditionalLightPositions, _additionalLightPositions);
                cmd.SetComputeVectorArrayParam(_lightingCombinedShader, ShaderIDs._AdditionalLightColors, _additionalLightColors);
                cmd.SetComputeVectorArrayParam(_lightingCombinedShader, ShaderIDs._AdditionalLightDirections, _additionalLightDirections);
            }

            if (irradianceAtlasPrev != null && distanceAtlasPrev != null)
            {
                cmd.SetComputeTextureParam(_lightingCombinedShader, _kernelLightingCombined,
                    ShaderIDs._IrradianceAtlas_Prev, irradianceAtlasPrev);
                cmd.SetComputeTextureParam(_lightingCombinedShader, _kernelLightingCombined,
                    ShaderIDs._DistanceAtlas_Prev, distanceAtlasPrev);
            }

            var desc = _volume.Descriptor;
            Vector3 volumeOrigin = _volume.transform.position;
            cmd.SetComputeVectorParam(_lightingCombinedShader, ShaderIDs._VolumeOrigin,
                new Vector4(volumeOrigin.x, volumeOrigin.y, volumeOrigin.z, 0));
            cmd.SetComputeVectorParam(_lightingCombinedShader, ShaderIDs._VolumeSpacing,
                new Vector4(desc.probeSpacing.x, desc.probeSpacing.y, desc.probeSpacing.z, 0));
            cmd.SetComputeVectorParam(_lightingCombinedShader, ShaderIDs._VolumeProbeCounts,
                new Vector4(desc.probeCounts.x, desc.probeCounts.y, desc.probeCounts.z, desc.TotalProbeCount));

            if (_volume.AtlasManager != null)
            {
                cmd.SetComputeVectorParam(_lightingCombinedShader, ShaderIDs._IrradianceAtlasParams,
                    _volume.AtlasManager.GetIrradianceAtlasParams());
                cmd.SetComputeVectorParam(_lightingCombinedShader, ShaderIDs._DistanceAtlasParams,
                    _volume.AtlasManager.GetDistanceAtlasParams());
            }

            cmd.SetComputeFloatParam(_lightingCombinedShader, ShaderIDs._NormalBias, _normalBias);
            cmd.SetComputeFloatParam(_lightingCombinedShader, ShaderIDs._ViewBias, desc.viewBias);
            cmd.SetComputeFloatParam(_lightingCombinedShader, ShaderIDs._ChebyshevBias, _chebyshevBias);
            cmd.SetComputeFloatParam(_lightingCombinedShader, ShaderIDs._IrradianceGamma, desc.irradianceGamma);

            if (_probeDataTexture != null && desc.enableProbeClassification)
            {
                cmd.SetComputeTextureParam(_lightingCombinedShader, _kernelLightingCombined,
                    ShaderIDs._ProbeData, _probeDataTexture);
                cmd.SetComputeIntParam(_lightingCombinedShader, ShaderIDs._ProbeDataWidth, _probeDataWidth);
            }
            else
            {
                cmd.SetComputeIntParam(_lightingCombinedShader, ShaderIDs._ProbeDataWidth, 0);
            }

            cmd.SetComputeFloatParam(_lightingCombinedShader, ShaderIDs._IndirectIntensity, _indirectIntensity);
            cmd.SetComputeFloatParam(_lightingCombinedShader, ShaderIDs._MaxRayDistance, _rayMaxDistance);

            cmd.DispatchCompute(_lightingCombinedShader, _kernelLightingCombined, threadGroups, 1, 1);
        }

        public void DispatchLightingComputeImmediate(RenderTexture irradianceAtlasPrev, RenderTexture distanceAtlasPrev)
        {
            CommandBuffer cmd = new CommandBuffer { name = "DDGI Lighting Compute" };
            DispatchLightingCompute(cmd, irradianceAtlasPrev, distanceAtlasPrev);
            Graphics.ExecuteCommandBuffer(cmd);
            cmd.Dispose();
        }

        #endregion

        #region Phase 3-4: 蒙特卡洛积分和边框更新

        public Matrix4x4 GetCurrentRayRotationMatrix()
        {
            return _currentRayRotationMatrix;
        }

        public void DispatchMonteCarloIntegration(CommandBuffer cmd,
                                                   RenderTexture irradianceAtlas,
                                                   RenderTexture distanceAtlas,
                                                   RenderTexture irradianceAtlasPrev,
                                                   RenderTexture distanceAtlasPrev)
        {
            if (_monteCarloIntegrationShader == null)
            {
                Debug.LogWarning("[DDGIRaytracingManager] MonteCarloIntegration shader is null");
                return;
            }

            var atlasManager = _volume.AtlasManager;
            var atlasConfig = _volume.AtlasConfig;
            if (atlasManager == null)
                return;

            var desc = _volume.Descriptor;
            int probeCount = desc.TotalProbeCount;

            cmd.SetComputeIntParam(_monteCarloIntegrationShader, ShaderIDs._GBufferWidth, _gBufferWidth);
            cmd.SetComputeIntParam(_monteCarloIntegrationShader, ShaderIDs._GBufferHeight, _gBufferHeight);
            cmd.SetComputeIntParam(_monteCarloIntegrationShader, ShaderIDs._RaysPerProbe, _raysPerProbe);
            cmd.SetComputeIntParam(_monteCarloIntegrationShader, ShaderIDs._FixedRayCount, _fixedRayCount);
            cmd.SetComputeIntParam(_monteCarloIntegrationShader, ShaderIDs._ProbeCount, probeCount);

            if (_probeDataTexture != null)
            {
                cmd.SetComputeIntParam(_monteCarloIntegrationShader, ShaderIDs._ProbeDataWidth, _probeDataWidth);
            }
            else
            {
                cmd.SetComputeIntParam(_monteCarloIntegrationShader, ShaderIDs._ProbeDataWidth, 0);
            }

            var irradianceSize = atlasManager.IrradianceAtlasSize;
            var distanceSize = atlasManager.DistanceAtlasSize;

            cmd.SetComputeIntParams(_monteCarloIntegrationShader, ShaderIDs._IrradianceAtlasSize,
                irradianceSize.x, irradianceSize.y,
                atlasConfig.irradianceProbeResolution,
                atlasManager.ProbesPerRow);

            cmd.SetComputeIntParams(_monteCarloIntegrationShader, ShaderIDs._DistanceAtlasSize,
                distanceSize.x, distanceSize.y,
                atlasConfig.distanceProbeResolution,
                atlasManager.ProbesPerRow);

            cmd.SetComputeIntParam(_monteCarloIntegrationShader, ShaderIDs._GutterSize, atlasConfig.gutterSize);

            cmd.SetComputeFloatParam(_monteCarloIntegrationShader, ShaderIDs._Hysteresis, desc.hysteresis);
            cmd.SetComputeFloatParam(_monteCarloIntegrationShader, ShaderIDs._IrradianceGamma, desc.irradianceGamma);
            cmd.SetComputeFloatParam(_monteCarloIntegrationShader, ShaderIDs._MaxRayDistance, _rayMaxDistance);
            cmd.SetComputeFloatParam(_monteCarloIntegrationShader, ShaderIDs._IrradianceThreshold, desc.irradianceThreshold);
            cmd.SetComputeFloatParam(_monteCarloIntegrationShader, ShaderIDs._BrightnessThreshold, desc.brightnessThreshold);
            cmd.SetComputeFloatParam(_monteCarloIntegrationShader, ShaderIDs._BackfaceThreshold, desc.probeBackfaceThreshold);

            bool variabilityEnabled = desc.enableProbeVariability && _probeVariabilityTexture != null;
            cmd.SetComputeIntParam(_monteCarloIntegrationShader, ShaderIDs._VariabilityEnabled, variabilityEnabled ? 1 : 0);

            Matrix4x4 rotationMatrix = GetCurrentRayRotationMatrix();
            cmd.SetComputeMatrixParam(_monteCarloIntegrationShader, ShaderIDs._RayRotationMatrix, rotationMatrix);

            cmd.SetComputeTextureParam(_monteCarloIntegrationShader, _kernelMonteCarloIrradiance,
                ShaderIDs._RadianceBuffer, _radianceBuffer);
            cmd.SetComputeTextureParam(_monteCarloIntegrationShader, _kernelMonteCarloIrradiance,
                ShaderIDs._GBuffer_NormalHitFlag, _gBufferNormalHitFlag);
            cmd.SetComputeTextureParam(_monteCarloIntegrationShader, _kernelMonteCarloIrradiance,
                ShaderIDs._IrradianceAtlas_Prev, irradianceAtlasPrev);
            cmd.SetComputeTextureParam(_monteCarloIntegrationShader, _kernelMonteCarloIrradiance,
                ShaderIDs._IrradianceAtlas, irradianceAtlas);

            if (_probeDataTexture != null)
            {
                cmd.SetComputeTextureParam(_monteCarloIntegrationShader, _kernelMonteCarloIrradiance,
                    ShaderIDs._ProbeData, _probeDataTexture);
            }

            if (variabilityEnabled)
            {
                cmd.SetComputeTextureParam(_monteCarloIntegrationShader, _kernelMonteCarloIrradiance,
                    ShaderIDs._ProbeVariability, _probeVariabilityTexture);
            }

            int irradianceRes = atlasConfig.irradianceProbeResolution;
            cmd.DispatchCompute(_monteCarloIntegrationShader, _kernelMonteCarloIrradiance,
                Mathf.CeilToInt(irradianceRes / 8f),
                Mathf.CeilToInt(irradianceRes / 8f),
                probeCount);

            cmd.SetComputeTextureParam(_monteCarloIntegrationShader, _kernelMonteCarloDistance,
                ShaderIDs._RadianceBuffer, _radianceBuffer);
            cmd.SetComputeTextureParam(_monteCarloIntegrationShader, _kernelMonteCarloDistance,
                ShaderIDs._GBuffer_NormalHitFlag, _gBufferNormalHitFlag);
            cmd.SetComputeTextureParam(_monteCarloIntegrationShader, _kernelMonteCarloDistance,
                ShaderIDs._DistanceAtlas_Prev, distanceAtlasPrev);
            cmd.SetComputeTextureParam(_monteCarloIntegrationShader, _kernelMonteCarloDistance,
                ShaderIDs._DistanceAtlas, distanceAtlas);

            if (_probeDataTexture != null)
            {
                cmd.SetComputeTextureParam(_monteCarloIntegrationShader, _kernelMonteCarloDistance,
                    ShaderIDs._ProbeData, _probeDataTexture);
            }

            int distanceRes = atlasConfig.distanceProbeResolution;
            cmd.DispatchCompute(_monteCarloIntegrationShader, _kernelMonteCarloDistance,
                Mathf.CeilToInt(distanceRes / 8f),
                Mathf.CeilToInt(distanceRes / 8f),
                probeCount);
        }

        public void DispatchBorderUpdate(CommandBuffer cmd,
                                          RenderTexture irradianceAtlas,
                                          RenderTexture distanceAtlas)
        {
            if (_monteCarloIntegrationShader == null)
                return;

            var atlasManager = _volume.AtlasManager;
            var atlasConfig = _volume.AtlasConfig;
            if (atlasManager == null)
                return;

            int probeCount = _volume.Descriptor.TotalProbeCount;
            if (probeCount <= 0)
                return;

            var irradianceSize = atlasManager.IrradianceAtlasSize;
            var distanceSize = atlasManager.DistanceAtlasSize;

            cmd.SetComputeIntParams(_monteCarloIntegrationShader, ShaderIDs._IrradianceAtlasSize,
                irradianceSize.x, irradianceSize.y,
                atlasConfig.irradianceProbeResolution,
                atlasManager.ProbesPerRow);

            cmd.SetComputeIntParams(_monteCarloIntegrationShader, ShaderIDs._DistanceAtlasSize,
                distanceSize.x, distanceSize.y,
                atlasConfig.distanceProbeResolution,
                atlasManager.ProbesPerRow);

            cmd.SetComputeIntParam(_monteCarloIntegrationShader, ShaderIDs._GutterSize, atlasConfig.gutterSize);
            cmd.SetComputeIntParam(_monteCarloIntegrationShader, ShaderIDs._ProbeCount, probeCount);

            cmd.SetComputeTextureParam(_monteCarloIntegrationShader, _kernelUpdateBorderIrradiance,
                ShaderIDs._IrradianceAtlas, irradianceAtlas);

            cmd.DispatchCompute(_monteCarloIntegrationShader, _kernelUpdateBorderIrradiance,
                probeCount, 1, 1);

            cmd.SetComputeTextureParam(_monteCarloIntegrationShader, _kernelUpdateBorderDistance,
                ShaderIDs._DistanceAtlas, distanceAtlas);

            cmd.DispatchCompute(_monteCarloIntegrationShader, _kernelUpdateBorderDistance,
                probeCount, 1, 1);
        }

        public void DispatchAtlasUpdate(CommandBuffer cmd,
                                         RenderTexture irradianceAtlas,
                                         RenderTexture distanceAtlas,
                                         RenderTexture irradianceAtlasPrev,
                                         RenderTexture distanceAtlasPrev)
        {

            DispatchMonteCarloIntegration(cmd, irradianceAtlas, distanceAtlas, irradianceAtlasPrev, distanceAtlasPrev);
        }

        public void DispatchMonteCarloIntegrationImmediate(RenderTexture irradianceAtlas,
                                                  RenderTexture distanceAtlas,
                                                  RenderTexture irradianceAtlasPrev,
                                                  RenderTexture distanceAtlasPrev)
        {
            CommandBuffer cmd = new CommandBuffer { name = "DDGI MonteCarlo Integration" };
            DispatchAtlasUpdate(cmd, irradianceAtlas, distanceAtlas, irradianceAtlasPrev, distanceAtlasPrev);
            Graphics.ExecuteCommandBuffer(cmd);
            cmd.Dispose();
        }

        public void DispatchBorderUpdateImmediate(RenderTexture irradianceAtlas,
            RenderTexture distanceAtlas)
        {
            CommandBuffer cmd = new CommandBuffer { name = "DDGI Border Update" };
            DispatchBorderUpdate(cmd, irradianceAtlas, distanceAtlas);
            Graphics.ExecuteCommandBuffer(cmd);
            cmd.Dispose();
        }

        public void DispatchFullUpdate(
            CommandBuffer cmd,
            RenderTexture irradianceAtlas,
            RenderTexture distanceAtlas,
            RenderTexture irradianceAtlasPrev,
            RenderTexture distanceAtlasPrev,
            bool enableRelocation,
            bool enableClassification = false)
        {
            if (!_isInitialized)
                return;

            DispatchRays(cmd);

            if (enableRelocation)
            {
                DispatchProbeRelocation(cmd);
            }

            if (enableClassification)
            {
                DispatchProbeClassification(cmd);
            }

            DispatchLightingCompute(cmd, irradianceAtlasPrev, distanceAtlasPrev);

            DispatchMonteCarloIntegration(cmd, irradianceAtlas, distanceAtlas, irradianceAtlasPrev, distanceAtlasPrev);

            DispatchVariabilityReduction(cmd);

            DispatchBorderUpdate(cmd, irradianceAtlas, distanceAtlas);
        }

        public void DispatchFullUpdateImmediate(
            RenderTexture irradianceAtlas,
            RenderTexture distanceAtlas,
            RenderTexture irradianceAtlasPrev,
            RenderTexture distanceAtlasPrev,
            bool enableRelocation,
            bool enableClassification = false)
        {
            CommandBuffer cmd = new CommandBuffer { name = "DDGI Full Update" };
            DispatchFullUpdate(cmd, irradianceAtlas, distanceAtlas, irradianceAtlasPrev, distanceAtlasPrev, enableRelocation, enableClassification);
            Graphics.ExecuteCommandBuffer(cmd);
            cmd.Dispose();
        }

        #endregion

        #region 调试和可视化

        public DDGIHitResult GetHitResult(int probeIndex, int rayIndex)
        {
            DDGIHitResult result = new DDGIHitResult();

            if (!_isInitialized) return result;

            int linearIndex = probeIndex * _raysPerProbe + rayIndex;
            int x = linearIndex % _gBufferWidth;
            int y = linearIndex / _gBufferWidth;

            return result;
        }

        public Vector2Int GetGBufferSize()
        {
            return new Vector2Int(_gBufferWidth, _gBufferHeight);
        }

        public int GetTotalRayCount()
        {
            return _volume.Descriptor.TotalProbeCount * _raysPerProbe;
        }

        #endregion

        #region 辅助函数

        private static float HaltonSequence(uint index, int baseValue)
        {
            float result = 0f;
            float fraction = 1f;
            uint i = index;
            while (i > 0)
            {
                fraction *= 1f / baseValue;
                result += (i % (uint)baseValue) * fraction;
                i /= (uint)baseValue;
            }
            return result;
        }

        private Light GetMainLight()
        {

            if (RenderSettings.sun != null)
                return RenderSettings.sun;

            if (_mainLightCached && _cachedMainLight != null)
                return _cachedMainLight;

            _cachedMainLight = null;
            Light[] lights = UnityEngine.Object.FindObjectsOfType<Light>();
            foreach (var light in lights)
            {
                if (light.type == LightType.Directional)
                {
                    _cachedMainLight = light;
                    break;
                }
            }
            _mainLightCached = true;

            return _cachedMainLight;
        }

        public void InvalidateMainLightCache()
        {
            _mainLightCached = false;
            _cachedMainLight = null;
        }

        private void CollectAdditionalLights()
        {
            if (_additionalLightPositions == null)
                return;

            _additionalLightCount = 0;

            Light mainLight = GetMainLight();

            Light[] allLights = UnityEngine.Object.FindObjectsOfType<Light>();
            foreach (var light in allLights)
            {

                if (!light.enabled || !light.gameObject.activeInHierarchy)
                    continue;

                if (light == mainLight || light.type == LightType.Directional)
                    continue;

                if (light.type != LightType.Point && light.type != LightType.Spot)
                    continue;

                if (_additionalLightCount >= MAX_ADDITIONAL_LIGHTS)
                    break;

                int index = _additionalLightCount;

                Vector3 pos = light.transform.position;
                _additionalLightPositions[index] = new Vector4(pos.x, pos.y, pos.z, light.range);

                Color color = light.color;
                _additionalLightColors[index] = new Vector4(color.r, color.g, color.b, light.intensity);

                Vector3 dir = -light.transform.forward;
                float spotAngle = (light.type == LightType.Spot) ? light.spotAngle * Mathf.Deg2Rad : 0f;
                _additionalLightDirections[index] = new Vector4(dir.x, dir.y, dir.z, spotAngle);

                _cachedAdditionalLights[index] = light;
                _additionalLightCount++;
            }
        }

        private Matrix4x4[] GetCascadeShadowMatrices()
        {

            Matrix4x4[] urpMatrices = Shader.GetGlobalMatrixArray("_MainLightWorldToShadow");

            Matrix4x4[] matrices = new Matrix4x4[4];

            if (urpMatrices != null && urpMatrices.Length >= 4)
            {

                matrices[0] = urpMatrices[0];
                matrices[1] = urpMatrices[1];
                matrices[2] = urpMatrices[2];
                matrices[3] = urpMatrices[3];
            }
            else
            {

                matrices[0] = Matrix4x4.identity;
                matrices[1] = Matrix4x4.identity;
                matrices[2] = Matrix4x4.identity;
                matrices[3] = Matrix4x4.identity;
                Debug.LogWarning("[DDGIRaytracingManager] Failed to get cascade shadow matrices from URP");
            }

            return matrices;
        }

        private int GetCascadeCount()
        {
            Vector4 radii = Shader.GetGlobalVector("_CascadeShadowSplitSphereRadii");

            int count = 0;
            if (radii.x > 0) count = 1;
            if (radii.y > 0) count = 2;
            if (radii.z > 0) count = 3;
            if (radii.w > 0) count = 4;

            return Mathf.Max(1, count);
        }

        private RenderTexture GetMainLightShadowMap()
        {

            Texture shadowTex = Shader.GetGlobalTexture("_MainLightShadowmapTexture");
            return shadowTex as RenderTexture;
        }

        #endregion

        #region 资源释放

        public void Dispose()
        {
            ReleaseGBuffers();
            ReleaseLightingBuffers();
            ReleaseProbeDataTexture();
            ReleaseProbeVariabilityTexture();

            if (_probePositionsBuffer != null)
            {
                _probePositionsBuffer.Release();
                _probePositionsBuffer = null;
            }

            if (_usingSharedRTAS)
            {
                _accelerationStructure = null;
                _usingSharedRTAS = false;
            }
            else if (_accelerationStructure != null)
            {
                _accelerationStructure.Release();
                _accelerationStructure = null;
            }

            _isInitialized = false;
            Debug.Log("[DDGIRaytracingManager] Disposed");
        }

        #endregion
    }

    [Serializable]
    public struct DDGIHitResult
    {
        public Vector3 position;
        public float hitDistance;
        public Vector3 normal;
        public uint hitFlag;
        public Vector3 albedo;
        public float roughness;
        public Vector3 emission;
        public float metallic;

        public bool IsMiss => hitFlag == 0;
        public bool IsHit => hitFlag == 1;
        public bool IsBackface => hitFlag == 2;
    }
}
