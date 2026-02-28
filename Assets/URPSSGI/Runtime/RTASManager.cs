using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace URPSSGI
{

    public sealed class RTASManager : IDisposable
    {

        public static RTASManager SharedInstance { get; set; }

        private RayTracingAccelerationStructure m_AccelerationStructure;
        private bool m_IsAvailable;
        private bool m_BuildFailed;
        private LayerMask m_CachedLayerMask;

        private RayTracingInstanceCullingConfig m_CullingConfig;
        private bool m_CullingConfigInitialized;

        public bool IsAvailable
        {
            get { return m_IsAvailable; }
        }

        public RayTracingAccelerationStructure AccelerationStructure
        {
            get { return m_AccelerationStructure; }
        }

        public static bool IsRayTracingSupported()
        {

            if (SystemInfo.supportsRayTracing)
                return true;

            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D12)
                return true;

            return false;
        }

        public void Prepare(LayerMask ssgiLayerMask, LayerMask ddgiLayerMask = default)
        {

            m_CachedLayerMask = ssgiLayerMask | ddgiLayerMask;

            if (!IsRayTracingSupported())
            {
                m_IsAvailable = false;
                return;
            }

            if (m_AccelerationStructure == null)
            {
                var settings = new RayTracingAccelerationStructure.RASSettings(
                    RayTracingAccelerationStructure.ManagementMode.Manual,
                    RayTracingAccelerationStructure.RayTracingModeMask.Everything,
                    m_CachedLayerMask);
                m_AccelerationStructure = new RayTracingAccelerationStructure(settings);
            }

            if (!m_CullingConfigInitialized)
            {
                InitCullingConfig();
                m_CullingConfigInitialized = true;
            }

            UpdateCullingLayerMask();

            m_IsAvailable = true;
        }

        private void InitCullingConfig()
        {
            m_CullingConfig = new RayTracingInstanceCullingConfig();

            m_CullingConfig.flags = RayTracingInstanceCullingFlags.EnableLODCulling;

            m_CullingConfig.lodParameters.orthoSize = 0;
            m_CullingConfig.lodParameters.isOrthographic = false;

            m_CullingConfig.subMeshFlagsConfig.opaqueMaterials =
                RayTracingSubMeshFlags.Enabled | RayTracingSubMeshFlags.ClosestHitOnly;
            m_CullingConfig.subMeshFlagsConfig.transparentMaterials =
                RayTracingSubMeshFlags.Enabled | RayTracingSubMeshFlags.UniqueAnyHitCalls;
            m_CullingConfig.subMeshFlagsConfig.alphaTestedMaterials =
                RayTracingSubMeshFlags.Enabled;

            m_CullingConfig.triangleCullingConfig.checkDoubleSidedGIMaterial = true;
            m_CullingConfig.triangleCullingConfig.frontTriangleCounterClockwise = false;
            m_CullingConfig.triangleCullingConfig.optionalDoubleSidedShaderKeywords = new string[1];
            m_CullingConfig.triangleCullingConfig.optionalDoubleSidedShaderKeywords[0] = "_DOUBLESIDED_ON";

            m_CullingConfig.materialTest.deniedShaderPasses = null;

            var giTest = new RayTracingInstanceCullingTest();
            giTest.allowOpaqueMaterials = true;
            giTest.allowAlphaTestedMaterials = true;
            giTest.allowTransparentMaterials = false;
            giTest.layerMask = m_CachedLayerMask;
            giTest.shadowCastingModeMask =
                (1 << (int)ShadowCastingMode.Off)
                | (1 << (int)ShadowCastingMode.On)
                | (1 << (int)ShadowCastingMode.TwoSided);
            giTest.instanceMask = 0xFF;

            m_CullingConfig.instanceTests = new RayTracingInstanceCullingTest[1];
            m_CullingConfig.instanceTests[0] = giTest;
        }

        private void UpdateCullingLayerMask()
        {
            if (m_CullingConfig.instanceTests != null && m_CullingConfig.instanceTests.Length > 0)
            {
                m_CullingConfig.instanceTests[0].layerMask = m_CachedLayerMask;
            }
        }

        public void BuildNow()
        {

            if (m_AccelerationStructure == null)
                return;

            try
            {

                m_AccelerationStructure.ClearInstances();
                m_AccelerationStructure.CullInstances(ref m_CullingConfig);

                m_AccelerationStructure.Build();
                m_BuildFailed = false;
            }
            catch (Exception e)
            {

                if (!m_BuildFailed)
                {
                    Debug.LogWarning($"[RTASManager] RTAS 构建失败（场景变化导致），下一帧将重试: {e.Message}");
                    m_BuildFailed = true;
                }
            }
        }

        public bool BuildFailed
        {
            get { return m_BuildFailed; }
        }

        public void Update(Camera camera = null)
        {

            if (camera != null)
            {
                m_CullingConfig.lodParameters.fieldOfView = camera.fieldOfView;
                m_CullingConfig.lodParameters.cameraPosition = camera.transform.position;
                m_CullingConfig.lodParameters.cameraPixelHeight = camera.pixelHeight;
            }

            BuildNow();
        }

        public void Dispose()
        {
            if (m_AccelerationStructure != null)
            {
                m_AccelerationStructure.Dispose();
                m_AccelerationStructure = null;
            }
            m_IsAvailable = false;
            m_CullingConfigInitialized = false;
        }
    }
}
