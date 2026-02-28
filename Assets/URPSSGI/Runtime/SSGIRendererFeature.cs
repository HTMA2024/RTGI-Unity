using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace URPSSGI
{

    public sealed class SSGIRendererFeature : ScriptableRendererFeature
    {

        private static bool s_RTFallbackWarningLogged;

        [SerializeField] private ComputeShader ssgiComputeShader;
        [SerializeField] private ComputeShader depthPyramidComputeShader;
        [SerializeField] private ComputeShader colorPyramidComputeShader;
        [SerializeField] private ComputeShader bilateralUpsampleComputeShader;
        [SerializeField] private Texture2D blueNoiseTexture;
        [SerializeField] private Texture2D owenScrambledTexture;
        [SerializeField] private Texture2D scramblingTile8SPP;
        [SerializeField] private Texture2D rankingTile8SPP;
        [SerializeField] private ComputeShader temporalFilterComputeShader;
        [SerializeField] private ComputeShader diffuseDenoiserComputeShader;
        [SerializeField] private Shader compositeShader;
        [SerializeField] private RayTracingShader rtgiRayTracingShader;
        [SerializeField] private ComputeShader deferredLightingComputeShader;

        private SSGIRenderPass m_SSGIPass;
        private SSGICompositePass m_CompositePass;
        private SSGICopyMotionVectorsPass m_CopyMVPass;
        private Material m_CompositeMaterial;
        private RTGIRenderPass m_RTGIPass;
        private RTASManager m_RTASManager;
        private bool m_Initialized;

        public override void Create()
        {

            m_SSGIPass?.Dispose();
            m_SSGIPass = null;
            m_RTGIPass?.Dispose();
            m_RTGIPass = null;
            m_RTASManager?.Dispose();
            m_RTASManager = null;
            CoreUtils.Destroy(m_CompositeMaterial);
            m_CompositeMaterial = null;
            m_CompositePass = null;

            if (ssgiComputeShader == null)
            {
                Debug.LogError("[SSGI] Compute Shader 未指定，SSGIRendererFeature 已禁用。");
                m_Initialized = false;
                return;
            }

            if (depthPyramidComputeShader == null)
            {
                Debug.LogError("[SSGI] Depth Pyramid Compute Shader 未指定，SSGIRendererFeature 已禁用。");
                m_Initialized = false;
                return;
            }

            if (colorPyramidComputeShader == null)
            {
                Debug.LogError("[SSGI] Color Pyramid Compute Shader 未指定，SSGIRendererFeature 已禁用。");
                m_Initialized = false;
                return;
            }

            if (bilateralUpsampleComputeShader == null)
            {
                Debug.LogError("[SSGI] Bilateral Upsample Compute Shader 未指定，SSGIRendererFeature 已禁用。");
                m_Initialized = false;
                return;
            }

            if (blueNoiseTexture == null)
            {
                Debug.LogError("[SSGI] Blue Noise Texture 未指定，SSGIRendererFeature 已禁用。");
                m_Initialized = false;
                return;
            }

            if (owenScrambledTexture == null || scramblingTile8SPP == null || rankingTile8SPP == null)
                Debug.LogWarning("[SSGI] BND 序列纹理（OwenScrambled/ScramblingTile/RankingTile）未完整指定，将回退到简单蓝噪声采样。");

            if (compositeShader == null)
            {
                Debug.LogError("[SSGI] Composite Shader 未指定，SSGIRendererFeature 已禁用。");
                m_Initialized = false;
                return;
            }

            if (temporalFilterComputeShader == null || diffuseDenoiserComputeShader == null)
                Debug.LogWarning("[SSGI] Temporal Filter 或 Diffuse Denoiser Compute Shader 未指定，去噪功能将不可用。");

            m_SSGIPass = new SSGIRenderPass(
                ssgiComputeShader,
                depthPyramidComputeShader,
                colorPyramidComputeShader,
                bilateralUpsampleComputeShader,
                blueNoiseTexture,
                owenScrambledTexture,
                scramblingTile8SPP,
                rankingTile8SPP,
                temporalFilterComputeShader,
                diffuseDenoiserComputeShader,
                deferredLightingComputeShader);

            m_CompositeMaterial = CoreUtils.CreateEngineMaterial(compositeShader);
            m_CompositePass = new SSGICompositePass(m_CompositeMaterial);
            m_CopyMVPass = new SSGICopyMotionVectorsPass(temporalFilterComputeShader);

            if (rtgiRayTracingShader != null && RTASManager.IsRayTracingSupported())
            {
                m_RTASManager = new RTASManager();
                RTASManager.SharedInstance = m_RTASManager;
                m_RTGIPass = new RTGIRenderPass(
                    rtgiRayTracingShader,
                    temporalFilterComputeShader,
                    diffuseDenoiserComputeShader,
                    bilateralUpsampleComputeShader,
                    owenScrambledTexture,
                    scramblingTile8SPP,
                    rankingTile8SPP,
                    m_RTASManager,
                    deferredLightingComputeShader);
            }
            else if (rtgiRayTracingShader != null)
            {
                Debug.LogWarning("[SSGI] 当前平台不支持光线追踪，RTGI 功能不可用。");
            }

            m_Initialized = true;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!m_Initialized)
                return;

            var camType = renderingData.cameraData.cameraType;
            if (camType != CameraType.Game && camType != CameraType.SceneView)
                return;

            UniversalRenderPipelineAsset urpAsset = UniversalRenderPipeline.asset;
            if (urpAsset != null)
            {
                if (!urpAsset.supportsCameraDepthTexture)
                    urpAsset.supportsCameraDepthTexture = true;
                if (!urpAsset.supportsCameraOpaqueTexture)
                    urpAsset.supportsCameraOpaqueTexture = true;
            }

            SSGIVolumeComponent volume = VolumeManager.instance.stack.GetComponent<SSGIVolumeComponent>();
            if (volume == null || !volume.IsActive())
                return;

            IndirectDiffuseMode mode = volume.giMode.value;

            if (mode != IndirectDiffuseMode.ScreenSpace
                && (m_RTGIPass == null || !RTASManager.IsRayTracingSupported()))
            {
                if (!s_RTFallbackWarningLogged)
                {
                    Debug.LogWarning("[SSGI] 当前平台不支持光线追踪，已回退到 ScreenSpace 模式。");
                    s_RTFallbackWarningLogged = true;
                }
                mode = IndirectDiffuseMode.ScreenSpace;
            }

            switch (mode)
            {
                case IndirectDiffuseMode.ScreenSpace:
                    m_SSGIPass.Setup(volume, false);
                    renderer.EnqueuePass(m_SSGIPass);
                    break;

                case IndirectDiffuseMode.RayTraced:
                    m_RTASManager.Prepare(~0);
                    m_RTGIPass.Setup(volume, mode);
                    renderer.EnqueuePass(m_RTGIPass);
                    break;

                case IndirectDiffuseMode.Mixed:

                    m_SSGIPass.Setup(volume, true);
                    renderer.EnqueuePass(m_SSGIPass);

                    m_RTASManager.Prepare(~0);
                    m_RTGIPass.Setup(volume, mode);
                    renderer.EnqueuePass(m_RTGIPass);
                    break;

                case IndirectDiffuseMode.MixedDDGI:

                    bool ddgiAvailable = DDGI.DDGIResourceProvider.Current.isValid;
                    IndirectDiffuseMode effectiveMode = ddgiAvailable
                        ? IndirectDiffuseMode.MixedDDGI
                        : IndirectDiffuseMode.Mixed;

                    m_SSGIPass.Setup(volume, true);
                    renderer.EnqueuePass(m_SSGIPass);
                    m_RTASManager.Prepare(~0);
                    m_RTGIPass.Setup(volume, effectiveMode);
                    renderer.EnqueuePass(m_RTGIPass);
                    break;
            }

            if (volume.compositeIntensity.value > 0.0f)
            {
                m_CompositePass.Setup(volume);
                renderer.EnqueuePass(m_CompositePass);
            }

            Camera cam = renderingData.cameraData.camera;
            SSGIHistoryManager hist = SSGIHistoryManager.GetOrCreate(cam);
            m_CopyMVPass.Setup(mode, hist);
            renderer.EnqueuePass(m_CopyMVPass);
        }

        protected override void Dispose(bool disposing)
        {
            m_SSGIPass?.Dispose();
            m_SSGIPass = null;

            m_RTGIPass?.Dispose();
            m_RTGIPass = null;

            RTASManager.SharedInstance = null;
            m_RTASManager?.Dispose();
            m_RTASManager = null;

            CoreUtils.Destroy(m_CompositeMaterial);
            m_CompositeMaterial = null;
            m_CompositePass = null;
            m_CopyMVPass = null;

            SSGIHistoryManager.ReleaseAll();
            SSGICameraContext.ReleaseAll();
            m_Initialized = false;
            s_RTFallbackWarningLogged = false;
        }
    }
}
