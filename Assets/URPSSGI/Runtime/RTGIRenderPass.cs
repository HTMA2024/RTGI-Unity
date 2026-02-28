using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering;

namespace URPSSGI
{

    public sealed class RTGIRenderPass : ScriptableRenderPass
    {

        private static readonly ProfilingSampler s_ProfilingSampler =
            new ProfilingSampler("RTGI");
        private static readonly ProfilingSampler s_RayTraceSampler =
            new ProfilingSampler("RTGI.RayTrace");
        private static readonly ProfilingSampler s_TemporalSampler =
            new ProfilingSampler("RTGI.TemporalFilter");
        private static readonly ProfilingSampler s_SpatialSampler =
            new ProfilingSampler("RTGI.SpatialDenoise");

        private static readonly int s_CameraDepthTextureID = Shader.PropertyToID("_CameraDepthTexture");
        private static readonly int s_CameraNormalsTextureID = Shader.PropertyToID("_CameraNormalsTexture");
        private static readonly int s_CameraMotionVectorsTextureID = Shader.PropertyToID("_CameraMotionVectorsTexture");

        private static readonly GlobalKeyword s_MultiBounceKeyword =
            GlobalKeyword.Create("MULTI_BOUNCE_INDIRECT");
        private static readonly GlobalKeyword s_AccurateNormalsKeyword =
            GlobalKeyword.Create("_SSGI_ACCURATE_NORMALS");

        private readonly RayTracingShader m_RTGIShader;
        private readonly ComputeShader m_TemporalFilterCS;
        private readonly ComputeShader m_DiffuseDenoiserCS;
        private readonly ComputeShader m_BilateralUpsampleCS;
        private readonly Texture2D m_OwenScrambledTexture;
        private readonly Texture2D m_ScramblingTile8SPP;
        private readonly Texture2D m_RankingTile8SPP;
        private readonly RTASManager m_RTASManager;
        private readonly bool m_HasDenoiseShaders;

        private readonly ComputeShader m_DeferredLightingCS;
        private readonly int m_DeferredLitKernel;
        private readonly int m_MergeSSGIAndRTGIKernel;
        private readonly bool m_HasDeferredLightingCS;

        private readonly bool m_HasBNDTextures;

        private readonly int m_TemporalFilterKernel;
        private readonly int m_CopyHistoryKernel;
        private readonly int m_CopyNormalsKernel;
        private readonly int m_CopyDepthKernel;
        private readonly int m_CopyNormalsDualKernel;
        private readonly int m_CopyDepthDualKernel;
        private readonly int m_CopyMotionVectorsKernel;
        private readonly int m_GeneratePointDistKernel;
        private readonly int m_BilateralFilterColorKernel;
        private readonly int m_BilateralFilterAOKernel;

        private readonly int m_BilateralUpSampleColorKernel;

        private ComputeBuffer m_PointDistributionBuffer;
        private bool m_PointDistributionInitialized;

        private SSGICameraContext m_Ctx;

        private RenderTargetIdentifier m_CachedDepthTexture;
        private RenderTargetIdentifier m_CachedNormalsTexture;
        private RenderTargetIdentifier m_CachedMotionVectorsTexture;

        private SSGIVolumeComponent m_VolumeComponent;
        private IndirectDiffuseMode m_CurrentMode;

        private float m_RcpSampleCount;
        private int m_CachedSampleCount;
        private int m_CachedBounceCount;
        private float m_CachedRayLength;
        private float m_CachedClampValue;
        private int m_CachedTextureLodBias;
        private int m_CachedLastBounceFallback;
        private float m_CachedAmbientProbeDimmer;
        private bool m_CachedDenoise;
        private float m_CachedDenoiserRadius;
        private bool m_CachedSecondDenoiserPass;
        private bool m_CachedMultiBounce;
        private bool m_CachedShadowRay;
        private Texture m_CachedSkyTexture;
        private bool m_CachedFullRes;

        private float m_CachedRayBias;
        private float m_CachedDistantRayBias;

        private bool m_CachedEnableRTAO;
        private float m_CachedRTAORadius;

        public RTGIRenderPass(
            RayTracingShader rtgiShader,
            ComputeShader temporalFilterCS,
            ComputeShader diffuseDenoiserCS,
            ComputeShader bilateralUpsampleCS,
            Texture2D owenScrambledTex,
            Texture2D scramblingTile8SPP,
            Texture2D rankingTile8SPP,
            RTASManager rtasManager,
            ComputeShader deferredLightingCS)
        {
            m_RTGIShader = rtgiShader;
            m_TemporalFilterCS = temporalFilterCS;
            m_DiffuseDenoiserCS = diffuseDenoiserCS;
            m_BilateralUpsampleCS = bilateralUpsampleCS;
            m_OwenScrambledTexture = owenScrambledTex;
            m_ScramblingTile8SPP = scramblingTile8SPP;
            m_RankingTile8SPP = rankingTile8SPP;
            m_RTASManager = rtasManager;

            m_DeferredLightingCS = deferredLightingCS;
            m_HasDeferredLightingCS = deferredLightingCS != null;
            if (m_HasDeferredLightingCS)
            {
                m_DeferredLitKernel        = deferredLightingCS.FindKernel("SSGIDeferredLit");
                m_MergeSSGIAndRTGIKernel   = deferredLightingCS.FindKernel("MergeSSGIAndRTGI");
            }

            m_HasBNDTextures = owenScrambledTex != null
                && scramblingTile8SPP != null
                && rankingTile8SPP != null;

            if (temporalFilterCS != null && diffuseDenoiserCS != null)
            {
                m_TemporalFilterKernel       = temporalFilterCS.FindKernel("TemporalFilter");
                m_CopyHistoryKernel          = temporalFilterCS.FindKernel("CopyHistory");
                m_CopyNormalsKernel          = temporalFilterCS.FindKernel("CopyNormals");
                m_CopyDepthKernel            = temporalFilterCS.FindKernel("CopyDepth");
                m_CopyNormalsDualKernel      = temporalFilterCS.FindKernel("CopyNormalsDual");
                m_CopyDepthDualKernel        = temporalFilterCS.FindKernel("CopyDepthDual");
                m_CopyMotionVectorsKernel    = temporalFilterCS.FindKernel("CopyMotionVectors");
                m_GeneratePointDistKernel    = diffuseDenoiserCS.FindKernel("GeneratePointDistribution");
                m_BilateralFilterColorKernel = diffuseDenoiserCS.FindKernel("BilateralFilterColor");
                m_BilateralFilterAOKernel    = diffuseDenoiserCS.FindKernel("BilateralFilterAO");
                m_HasDenoiseShaders = true;

                m_PointDistributionBuffer = new ComputeBuffer(64, 2 * sizeof(float));
            }

            if (bilateralUpsampleCS != null)
                m_BilateralUpSampleColorKernel = bilateralUpsampleCS.FindKernel("BilateralUpSampleColor");

            renderPassEvent = RenderPassEvent.AfterRenderingOpaques;

            ConfigureInput(ScriptableRenderPassInput.Depth
                         | ScriptableRenderPassInput.Normal
                         | ScriptableRenderPassInput.Motion);
        }

        public void Setup(SSGIVolumeComponent volume, IndirectDiffuseMode mode)
        {
            m_VolumeComponent = volume;
            m_CurrentMode = mode;

            m_CachedSampleCount = volume.rtSampleCount.value;
            m_CachedBounceCount = volume.rtBounceCount.value;
            m_CachedRayLength = volume.rtRayLength.value;
            m_CachedClampValue = volume.rtClampValue.value;
            m_CachedTextureLodBias = volume.rtTextureLodBias.value;
            m_CachedLastBounceFallback = (int)volume.rtLastBounceFallbackHierarchy.value;
            m_CachedAmbientProbeDimmer = volume.rtAmbientProbeDimmer.value;
            m_CachedDenoise = volume.rtDenoise.value;
            m_CachedDenoiserRadius = volume.rtDenoiserRadius.value;
            m_CachedSecondDenoiserPass = volume.rtSecondDenoiserPass.value;
            m_CachedMultiBounce = m_CachedBounceCount > 1;
            m_CachedShadowRay = volume.rtShadowRay.value;

            m_CachedRayBias = volume.rtRayBias.value;
            m_CachedDistantRayBias = volume.rtDistantRayBias.value;

            m_CachedEnableRTAO = volume.enableRTAO.value;
            m_CachedRTAORadius = volume.rtaoRadius.value;

            m_CachedFullRes = volume.fullResolution.value;

            m_RcpSampleCount = 1.0f / m_CachedSampleCount;

            Cubemap skyCubemap = null;
            Material skyMat = RenderSettings.skybox;
            if (skyMat != null)
            {
                skyCubemap = skyMat.GetTexture("_Tex") as Cubemap;
                if (skyCubemap == null)
                    skyCubemap = skyMat.GetTexture("_MainTex") as Cubemap;
            }
            if (skyCubemap == null)
                skyCubemap = RenderSettings.customReflectionTexture as Cubemap;
            if (skyCubemap == null)
                skyCubemap = ReflectionProbe.defaultTexture as Cubemap;
            m_CachedSkyTexture = skyCubemap != null ? (Texture)skyCubemap : Texture2D.blackTexture;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            Camera camera = renderingData.cameraData.camera;
            SSGICameraContext ctx = SSGICameraContext.GetOrCreate(camera);
            m_Ctx = ctx;

            RenderTextureDescriptor cameraDesc = renderingData.cameraData.cameraTargetDescriptor;
            int fullWidth = cameraDesc.width;
            int fullHeight = cameraDesc.height;

            bool fullRes = m_CachedFullRes;
            int traceW = fullRes ? fullWidth : fullWidth >> 1;
            int traceH = fullRes ? fullHeight : fullHeight >> 1;

            if (ctx.RTGIOutputTexture == null
                || ctx.RTGIOutputTexture.width != traceW
                || ctx.RTGIOutputTexture.height != traceH)
            {
                DestroyRT(ref ctx.RTGIOutputTexture);
                ctx.RTGIOutputTexture = CreateRT(traceW, traceH,
                    GraphicsFormat.R16G16B16A16_SFloat, true, "_RTGIOutputTexture");
            }

            if (!fullRes)
            {
                if (ctx.RTGIUpsampleResult == null
                    || ctx.RTGIUpsampleResult.width != fullWidth
                    || ctx.RTGIUpsampleResult.height != fullHeight)
                {
                    DestroyRT(ref ctx.RTGIUpsampleResult);
                    ctx.RTGIUpsampleResult = CreateRT(fullWidth, fullHeight,
                        GraphicsFormat.R16G16B16A16_SFloat, true, "_RTGIUpsampleResult");
                }
            }

            ctx.AllocateRTGIBuffersIfNeeded(traceW, traceH);

            if (m_CachedDenoise && m_HasDenoiseShaders)
            {
                if (ctx.TemporalOutputTemp == null
                    || ctx.TemporalOutputTemp.width != traceW
                    || ctx.TemporalOutputTemp.height != traceH)
                {
                    DestroyRT(ref ctx.TemporalOutputTemp);
                    ctx.TemporalOutputTemp = CreateRT(traceW, traceH,
                        GraphicsFormat.R16G16B16A16_SFloat, true, "_RTGITemporalOutputTemp");
                }

                if (ctx.SpatialDenoiseTemp == null
                    || ctx.SpatialDenoiseTemp.width != traceW
                    || ctx.SpatialDenoiseTemp.height != traceH)
                {
                    DestroyRT(ref ctx.SpatialDenoiseTemp);
                    ctx.SpatialDenoiseTemp = CreateRT(traceW, traceH,
                        GraphicsFormat.R16G16B16A16_SFloat, true, "_RTGISpatialDenoiseTemp");
                }
            }

            {
                if (ctx.RTAOOutputTexture == null
                    || ctx.RTAOOutputTexture.width != traceW
                    || ctx.RTAOOutputTexture.height != traceH)
                {
                    DestroyRT(ref ctx.RTAOOutputTexture);
                    ctx.RTAOOutputTexture = CreateRT(traceW, traceH,
                        GraphicsFormat.R8_UNorm, true, "_RTAOOutputTexture");
                }
            }

            if (m_CachedEnableRTAO && m_CachedDenoise && m_HasDenoiseShaders)
            {
                if (ctx.RTAOTemporalTemp == null
                    || ctx.RTAOTemporalTemp.width != traceW
                    || ctx.RTAOTemporalTemp.height != traceH)
                {
                    DestroyRT(ref ctx.RTAOTemporalTemp);
                    ctx.RTAOTemporalTemp = CreateRT(traceW, traceH,
                        GraphicsFormat.R16G16B16A16_SFloat, true, "_RTAOTemporalTemp");
                }
                if (ctx.RTAOSpatialTemp == null
                    || ctx.RTAOSpatialTemp.width != traceW
                    || ctx.RTAOSpatialTemp.height != traceH)
                {
                    DestroyRT(ref ctx.RTAOSpatialTemp);
                    ctx.RTAOSpatialTemp = CreateRT(traceW, traceH,
                        GraphicsFormat.R16_SFloat, true, "_RTAOSpatialTemp");
                }
            }
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {

            var camType = renderingData.cameraData.cameraType;
            if (camType != CameraType.Game && camType != CameraType.SceneView)
                return;

            if (m_RTASManager == null || !m_RTASManager.IsAvailable)
                return;

            if (m_RTASManager.BuildFailed)
                return;

            if (m_RTGIShader == null)
                return;

            ScriptableRenderer currentRenderer = renderingData.cameraData.renderer;
            Texture depthTex = URPTextureResolver.ResolveDepthTexture(currentRenderer);
            if (depthTex == null)
                return;

            m_CachedDepthTexture = new RenderTargetIdentifier(depthTex);

            Texture normalsTex = URPTextureResolver.ResolveNormalsTexture(currentRenderer);
            Texture motionTex = URPTextureResolver.ResolveMotionVectorsTexture();
            m_CachedNormalsTexture = normalsTex != null
                ? new RenderTargetIdentifier(normalsTex)
                : new RenderTargetIdentifier(Texture2D.blackTexture);
            m_CachedMotionVectorsTexture = motionTex != null
                ? new RenderTargetIdentifier(motionTex)
                : new RenderTargetIdentifier(Texture2D.blackTexture);

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, s_ProfilingSampler))
            {
                Camera camera = renderingData.cameraData.camera;

                m_Ctx = SSGICameraContext.GetOrCreate(camera);

                RenderTextureDescriptor cameraDesc = renderingData.cameraData.cameraTargetDescriptor;
                int fullWidth = cameraDesc.width;
                int fullHeight = cameraDesc.height;

                bool fullRes = m_CachedFullRes;
                int traceW = fullRes ? fullWidth : fullWidth >> 1;
                int traceH = fullRes ? fullHeight : fullHeight >> 1;

                m_RTASManager.Update(camera);

                cmd.SetKeyword(s_MultiBounceKeyword, m_CachedMultiBounce);
                cmd.SetKeyword(s_AccurateNormalsKeyword, m_VolumeComponent.useAccurateNormals.value);

                if ((m_CurrentMode == IndirectDiffuseMode.Mixed || m_CurrentMode == IndirectDiffuseMode.MixedDDGI)
                    && m_Ctx.SSGIExecutedThisFrame)
                {

                    SSGIHistoryManager history = SSGIHistoryManager.GetOrCreate(camera);
                    history.AllocateBuffersIfNeeded(traceW, traceH);

                    DispatchRayTrace(cmd, traceW, traceH, fullWidth, fullHeight, ref renderingData);

                    DispatchMergeSSGIAndRTGI(cmd, traceW, traceH);

                    RenderTargetIdentifier mergedGI = (RenderTargetIdentifier)m_Ctx.TemporalOutputTemp;
                    if (m_CachedDenoise && m_HasDenoiseShaders)
                    {

                        Matrix4x4 translateToCamera = Matrix4x4.Translate(SSGIRenderPass.GetCameraWorldPosition(camera));
                        Matrix4x4 viewMatrix = camera.worldToCameraMatrix;
                        Matrix4x4 gpuProjMatrix = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true);
                        Matrix4x4 vpMatrix = gpuProjMatrix * (viewMatrix * translateToCamera);
                        Matrix4x4 invVPMatrix = vpMatrix.inverse;
                        float halfFovRad = camera.fieldOfView * 0.5f * Mathf.Deg2Rad;
                        float pixelSpreadAngleTangent = Mathf.Tan(halfFovRad) * 2.0f / Mathf.Max(fullWidth, fullHeight);

                        PerformMixedDenoise(cmd, history, mergedGI, traceW, traceH, fullWidth, fullHeight,
                            translateToCamera, vpMatrix, invVPMatrix, pixelSpreadAngleTangent);

                        if (m_CachedEnableRTAO)
                            PerformRTAODenoise(cmd, traceW, traceH, fullWidth, fullHeight,
                                translateToCamera, vpMatrix, invVPMatrix, pixelSpreadAngleTangent);
                    }

                    if (m_HasDenoiseShaders)
                    {
                        if (m_CachedEnableRTAO)
                            DispatchCopyNormalsDual(cmd, history, traceW, traceH, fullWidth, fullHeight);
                        else
                            DispatchMixedCopyNormals(cmd, history, traceW, traceH, fullWidth, fullHeight);
                    }

                    bool denoised = m_CachedDenoise && m_HasDenoiseShaders;
                    RenderTargetIdentifier finalResult = DetermineMixedFinalResult(cmd,
                        fullRes, denoised, mergedGI, fullWidth, fullHeight, traceW, traceH);

                    m_Ctx.FinalGIResult = finalResult;
                    cmd.SetGlobalTexture(SSGIShaderIDs._IndirectDiffuseTexture, finalResult);

                    m_Ctx.PrevIndirectDiffuseTexture = finalResult;
                    cmd.SetGlobalTexture(SSGIShaderIDs._PrevIndirectDiffuseTexture, finalResult);

                    if (m_CachedEnableRTAO && m_Ctx.RTAOOutputTexture != null)
                    {
                        RenderTargetIdentifier rtaoResult;
                        if (m_CachedDenoise && m_HasDenoiseShaders && m_Ctx.RTAOSpatialTemp != null)
                            rtaoResult = m_Ctx.RTAOSpatialTemp;
                        else
                            rtaoResult = m_Ctx.RTAOOutputTexture;
                        m_Ctx.FinalRTAOResult = rtaoResult;
                        cmd.SetGlobalTexture(SSGIShaderIDs._RTAOOutputTexture, rtaoResult);
                        cmd.SetGlobalFloat(SSGIShaderIDs._RTAOIntensity,
                            m_VolumeComponent.rtaoIntensity.value);
                    }

                    BindRTGIDebugBuffers(cmd);

                    history.SetCurrentViewAndProj(
                        camera.worldToCameraMatrix,
                        GL.GetGPUProjectionMatrix(camera.projectionMatrix, true));
                    history.SetCurrentExposure(GetCurrentExposureMultiplier());

                    m_Ctx.RTGIPrevViewMatrix = camera.worldToCameraMatrix;
                    m_Ctx.RTGIPrevGpuProjMatrix = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true);
                    m_Ctx.RTGIPrevExposure = GetCurrentExposureMultiplier();

                    history.SwapAndSetReferenceSize(traceW, traceH);

                    m_Ctx.RTGIHistoryValid = true;
                    if (m_CachedEnableRTAO)
                        m_Ctx.RTAOHistoryValid = true;

                    m_Ctx.FrameIndex = (m_Ctx.FrameIndex + 1) & 0xFFFF;

                    m_Ctx.SSGIExecutedThisFrame = true;

                    SSGIRenderPass.UpdateRTGIStats(
                        m_CurrentMode,
                        m_RTASManager != null && m_RTASManager.IsAvailable,
                        traceW * traceH * m_CachedSampleCount);
                }
                else
                {

                DispatchRayTrace(cmd, traceW, traceH, fullWidth, fullHeight, ref renderingData);

                if (m_CachedDenoise && m_HasDenoiseShaders)
                {

                    Matrix4x4 translateToCamera = Matrix4x4.Translate(SSGIRenderPass.GetCameraWorldPosition(camera));
                    Matrix4x4 viewMatrix = camera.worldToCameraMatrix;
                    Matrix4x4 gpuProjMatrix = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true);
                    Matrix4x4 vpMatrix = gpuProjMatrix * (viewMatrix * translateToCamera);
                    Matrix4x4 invVPMatrix = vpMatrix.inverse;
                    float halfFovRad = camera.fieldOfView * 0.5f * Mathf.Deg2Rad;
                    float pixelSpreadAngleTangent = Mathf.Tan(halfFovRad) * 2.0f / Mathf.Max(fullWidth, fullHeight);

                    PerformRTGIDenoise(cmd, traceW, traceH, fullWidth, fullHeight,
                        translateToCamera, vpMatrix, invVPMatrix, pixelSpreadAngleTangent);

                    if (m_CachedEnableRTAO)
                    {
                        PerformRTAODenoise(cmd, traceW, traceH, fullWidth, fullHeight,
                            translateToCamera, vpMatrix, invVPMatrix, pixelSpreadAngleTangent);
                    }
                }

                {
                    m_Ctx.RTGIPrevViewMatrix = camera.worldToCameraMatrix;
                    m_Ctx.RTGIPrevGpuProjMatrix = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true);
                    m_Ctx.RTGIPrevExposure = GetCurrentExposureMultiplier();
                }

                if (m_HasDenoiseShaders)
                {
                    DispatchCopyNormals(cmd, traceW, traceH, fullWidth, fullHeight);
                }

                RenderTargetIdentifier denoiseResult;
                if (m_CachedDenoise && m_HasDenoiseShaders && m_Ctx.SpatialDenoiseTemp != null)
                    denoiseResult = m_Ctx.SpatialDenoiseTemp;
                else
                    denoiseResult = m_Ctx.RTGIOutputTexture;

                RenderTargetIdentifier finalResult;
                if (!fullRes && m_BilateralUpsampleCS != null)
                {
                    DispatchRTGIBilateralUpsample(cmd, fullWidth, fullHeight, traceW, traceH, denoiseResult);
                    finalResult = m_Ctx.RTGIUpsampleResult;
                }
                else
                {
                    finalResult = denoiseResult;
                }

                m_Ctx.FinalGIResult = finalResult;
                cmd.SetGlobalTexture(SSGIShaderIDs._IndirectDiffuseTexture, finalResult);

                m_Ctx.PrevIndirectDiffuseTexture = finalResult;
                cmd.SetGlobalTexture(SSGIShaderIDs._PrevIndirectDiffuseTexture, finalResult);

                BindRTGIDebugBuffers(cmd);

                if (m_CachedEnableRTAO && m_Ctx.RTAOOutputTexture != null)
                {
                    RenderTargetIdentifier rtaoResult;
                    if (m_CachedDenoise && m_HasDenoiseShaders && m_Ctx.RTAOSpatialTemp != null)
                        rtaoResult = m_Ctx.RTAOSpatialTemp;
                    else
                        rtaoResult = m_Ctx.RTAOOutputTexture;
                    m_Ctx.FinalRTAOResult = rtaoResult;
                    cmd.SetGlobalTexture(SSGIShaderIDs._RTAOOutputTexture, rtaoResult);
                    cmd.SetGlobalFloat(SSGIShaderIDs._RTAOIntensity,
                        m_VolumeComponent.rtaoIntensity.value);
                }

                m_Ctx.RTGIHistoryValid = true;
                if (m_CachedEnableRTAO)
                    m_Ctx.RTAOHistoryValid = true;

                m_Ctx.FrameIndex = (m_Ctx.FrameIndex + 1) & 0xFFFF;

                m_Ctx.SSGIExecutedThisFrame = true;

                SSGIRenderPass.UpdateRTGIStats(
                    m_CurrentMode,
                    m_RTASManager != null && m_RTASManager.IsAvailable,
                    traceW * traceH * m_CachedSampleCount);
                }
            }
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        private void DispatchMergeSSGIAndRTGI(CommandBuffer cmd, int texW, int texH)
        {
            int kernel = m_MergeSSGIAndRTGIKernel;

            cmd.SetComputeTextureParam(m_DeferredLightingCS, kernel,
                SSGIShaderIDs._SSGILitResult, m_Ctx.SSGIResult);
            cmd.SetComputeTextureParam(m_DeferredLightingCS, kernel,
                SSGIShaderIDs._RTGILitResult, m_Ctx.RTGIOutputTexture);
            cmd.SetComputeTextureParam(m_DeferredLightingCS, kernel,
                SSGIShaderIDs._MergeMask, m_Ctx.RTGIHitValidityMask);
            cmd.SetComputeTextureParam(m_DeferredLightingCS, kernel,
                SSGIShaderIDs._MergedGIOutputRW, m_Ctx.TemporalOutputTemp);

            SSGIRenderPass.ComputeThreadGroups(texW, texH, out int groupsX, out int groupsY);
            cmd.DispatchCompute(m_DeferredLightingCS, kernel, groupsX, groupsY, 1);
        }

        private void DispatchRayTrace(CommandBuffer cmd, int traceW, int traceH, int fullW, int fullH, ref RenderingData renderingData)
        {
            using (new ProfilingScope(cmd, s_RayTraceSampler))
            {
                RayTracingShader shader = m_RTGIShader;

                cmd.SetRayTracingShaderPass(shader, "IndirectDiffuse");

                cmd.SetRayTracingAccelerationStructure(shader,
                    SSGIShaderIDs._RaytracingAccelerationStructureName,
                    m_RTASManager.AccelerationStructure);

                if (m_HasBNDTextures)
                {
                    cmd.SetRayTracingTextureParam(shader,
                        SSGIShaderIDs._OwenScrambledTexture, m_OwenScrambledTexture);
                    cmd.SetRayTracingTextureParam(shader,
                        SSGIShaderIDs._ScramblingTileXSPP, m_ScramblingTile8SPP);
                    cmd.SetRayTracingTextureParam(shader,
                        SSGIShaderIDs._RankingTileXSPP, m_RankingTile8SPP);
                }

                cmd.SetRayTracingTextureParam(shader,
                    s_CameraDepthTextureID, m_CachedDepthTexture);
                cmd.SetRayTracingTextureParam(shader,
                    s_CameraNormalsTextureID, m_CachedNormalsTexture);

                cmd.SetRayTracingFloatParam(shader,
                    SSGIShaderIDs._RaytracingRayMaxLength, m_CachedRayLength);
                cmd.SetRayTracingIntParam(shader,
                    SSGIShaderIDs._RaytracingNumSamples, m_CachedSampleCount);
                cmd.SetRayTracingIntParam(shader,
                    SSGIShaderIDs._RaytracingMaxRecursion, m_CachedBounceCount);
                cmd.SetRayTracingFloatParam(shader,
                    SSGIShaderIDs._RaytracingIntensityClamp, m_CachedClampValue);
                cmd.SetRayTracingIntParam(shader,
                    SSGIShaderIDs._RayTracingLodBias, m_CachedTextureLodBias);
                cmd.SetRayTracingIntParam(shader,
                    SSGIShaderIDs._RayTracingLastBounceFallbackHierarchy, m_CachedLastBounceFallback);
                cmd.SetRayTracingFloatParam(shader,
                    SSGIShaderIDs._RayTracingAmbientProbeDimmer, m_CachedAmbientProbeDimmer);

                cmd.SetRayTracingIntParam(shader,
                    SSGIShaderIDs._RaytracingFrameIndex, m_Ctx.FrameIndex);

                cmd.SetRayTracingIntParam(shader,
                    SSGIShaderIDs._RayTracingRayMissFallbackHierarchy,
                    (int)m_VolumeComponent.rayMissFallback.value);

                cmd.SetRayTracingFloatParam(shader,
                    SSGIShaderIDs._SSGIScreenSize, traceW);

                int rtScale = m_CachedFullRes ? 1 : 2;
                cmd.SetRayTracingIntParam(shader,
                    SSGIShaderIDs._RTGIRayTracingScale, rtScale);

                cmd.SetRayTracingVectorParam(shader,
                    SSGIShaderIDs._RTGIFullScreenSize,
                    new Vector4(fullW, fullH, 1.0f / fullW, 1.0f / fullH));

                cmd.SetRayTracingTextureParam(shader,
                    SSGIShaderIDs._SkyTexture, m_CachedSkyTexture);

                if ((m_CurrentMode == IndirectDiffuseMode.Mixed || m_CurrentMode == IndirectDiffuseMode.MixedDDGI)
                    && m_Ctx.RTGIHitValidityMask != null
                    && m_Ctx.SSGIExecutedThisFrame)
                {
                    cmd.SetRayTracingTextureParam(shader,
                        SSGIShaderIDs._RTGIHitValidityMask, m_Ctx.RTGIHitValidityMask);

                    cmd.SetRayTracingTextureParam(shader,
                        SSGIShaderIDs._IndirectDiffuseTexture, m_Ctx.FinalGIResult);
                    cmd.SetRayTracingIntParam(shader,
                        SSGIShaderIDs._RTGIMixedMode, 1);

                    cmd.SetRayTracingTextureParam(shader,
                        SSGIShaderIDs._SSGIGBuffer0RW, m_Ctx.SSGIGBuffer0);
                    cmd.SetRayTracingTextureParam(shader,
                        SSGIShaderIDs._SSGIGBuffer2RW, m_Ctx.SSGIGBuffer2);
                    cmd.SetRayTracingTextureParam(shader,
                        SSGIShaderIDs._SSGIHitPositionNDCRW, m_Ctx.SSGIHitPositionNDC);
                }
                else
                {

                    cmd.SetRayTracingTextureParam(shader,
                        SSGIShaderIDs._RTGIHitValidityMask, Texture2D.blackTexture);
                    cmd.SetRayTracingTextureParam(shader,
                        SSGIShaderIDs._IndirectDiffuseTexture, Texture2D.blackTexture);
                    cmd.SetRayTracingIntParam(shader,
                        SSGIShaderIDs._RTGIMixedMode, 0);

                    cmd.SetRayTracingTextureParam(shader,
                        SSGIShaderIDs._SSGIGBuffer0RW, m_Ctx.RTGIHitValidityMask);
                    cmd.SetRayTracingTextureParam(shader,
                        SSGIShaderIDs._SSGIGBuffer2RW, m_Ctx.RTGIHitValidityMask);
                    cmd.SetRayTracingTextureParam(shader,
                        SSGIShaderIDs._SSGIHitPositionNDCRW, m_Ctx.RTGIHitValidityMask);
                }

                BindMainLightParams(cmd, shader, ref renderingData);

                int debugNormalMode = (m_VolumeComponent.debugMode.value == SSGIDebugMode.RTGINormal) ? 1 : 0;
                cmd.SetRayTracingIntParam(shader,
                    SSGIShaderIDs._RTGIDebugNormalMode, debugNormalMode);
                cmd.SetGlobalInt(SSGIShaderIDs._RTGIDebugNormalMode, debugNormalMode);

                int debugShadowMode = (m_VolumeComponent.debugMode.value == SSGIDebugMode.RTGIShadowMap) ? 1 : 0;
                cmd.SetRayTracingIntParam(shader,
                    SSGIShaderIDs._RTGIDebugShadowMode, debugShadowMode);
                cmd.SetGlobalInt(SSGIShaderIDs._RTGIDebugShadowMode, debugShadowMode);

                cmd.SetGlobalInt(SSGIShaderIDs._RayTracingLastBounceFallbackHierarchy,
                    m_CachedLastBounceFallback);
                cmd.SetGlobalFloat(SSGIShaderIDs._RayTracingAmbientProbeDimmer,
                    m_CachedAmbientProbeDimmer);

                {
                    float lbw = (m_CachedLastBounceFallback & 0x03) != 0 ? 1.0f : 0.0f;
                    cmd.SetGlobalFloat(SSGIShaderIDs._RayTracingLastBounceWeight,
                        lbw * m_CachedAmbientProbeDimmer);
                }

                cmd.SetGlobalTexture(SSGIShaderIDs._SkyTexture, m_CachedSkyTexture);

                cmd.SetGlobalInt(SSGIShaderIDs._UseShadowRay, m_CachedShadowRay ? 1 : 0);

                if (m_CachedMultiBounce)
                {
                    cmd.SetGlobalFloat(SSGIShaderIDs._RaytracingRayMaxLength, m_CachedRayLength);
                    cmd.SetGlobalInt(SSGIShaderIDs._RaytracingMaxRecursion, m_CachedBounceCount);
                    cmd.SetGlobalFloat(SSGIShaderIDs._RaytracingIntensityClamp, m_CachedClampValue);
                    cmd.SetGlobalInt(SSGIShaderIDs._RayTracingRayMissFallbackHierarchy,
                        (int)m_VolumeComponent.rayMissFallback.value);
                }

                cmd.SetRayTracingTextureParam(shader,
                    SSGIShaderIDs._RTGIOutputTexture, m_Ctx.RTGIOutputTexture);

                cmd.SetRayTracingIntParam(shader,
                    SSGIShaderIDs._EnableRTAO, m_CachedEnableRTAO ? 1 : 0);
                cmd.SetRayTracingFloatParam(shader,
                    SSGIShaderIDs._RTAORadius, m_CachedEnableRTAO ? m_CachedRTAORadius : 1.0f);
                cmd.SetRayTracingTextureParam(shader,
                    SSGIShaderIDs._RTAOOutputTexture, m_Ctx.RTAOOutputTexture);

                cmd.SetRayTracingIntParam(shader,
                    SSGIShaderIDs._UseAccurateNormals,
                    m_VolumeComponent.useAccurateNormals.value ? 1 : 0);

                cmd.SetRayTracingFloatParam(shader,
                    SSGIShaderIDs._RayTracingRayBias, m_CachedRayBias);
                cmd.SetRayTracingFloatParam(shader,
                    SSGIShaderIDs._RayTracingDistantRayBias, m_CachedDistantRayBias);

                {
                    Matrix4x4 gpuProjNoJitter = renderingData.cameraData.GetGPUProjectionMatrixNoJitter();
                    Matrix4x4 viewMatrix = renderingData.cameraData.GetViewMatrix();
                    Matrix4x4 vpNoJitter = gpuProjNoJitter * viewMatrix;
                    cmd.SetRayTracingMatrixParam(shader,
                        SSGIShaderIDs._RTGIInvVPNoJitter, vpNoJitter.inverse);
                }

                if (m_CurrentMode == IndirectDiffuseMode.MixedDDGI)
                {
                    var ddgi = DDGI.DDGIResourceProvider.Current;
                    if (ddgi.isValid)
                    {
                        cmd.SetRayTracingIntParam(shader, SSGIShaderIDs._RTGIMixedDDGIMode, 1);
                        cmd.SetRayTracingTextureParam(shader, SSGIShaderIDs._DDGIIrradianceAtlas, ddgi.irradianceAtlas);
                        cmd.SetRayTracingTextureParam(shader, SSGIShaderIDs._DDGIDistanceAtlas, ddgi.distanceAtlas);
                        cmd.SetRayTracingVectorParam(shader, SSGIShaderIDs._DDGIVolumeOrigin,
                            new Vector4(ddgi.origin.x, ddgi.origin.y, ddgi.origin.z, 0));
                        cmd.SetRayTracingVectorParam(shader, SSGIShaderIDs._DDGIVolumeSpacing,
                            new Vector4(ddgi.probeSpacing.x, ddgi.probeSpacing.y, ddgi.probeSpacing.z, 0));
                        cmd.SetRayTracingVectorParam(shader, SSGIShaderIDs._DDGIVolumeProbeCounts,
                            new Vector4(ddgi.probeCounts.x, ddgi.probeCounts.y, ddgi.probeCounts.z, 0));
                        cmd.SetRayTracingFloatParam(shader, SSGIShaderIDs._DDGINormalBias, ddgi.normalBias);
                        cmd.SetRayTracingFloatParam(shader, SSGIShaderIDs._DDGIViewBias, ddgi.viewBias);
                        cmd.SetRayTracingFloatParam(shader, SSGIShaderIDs._DDGIIrradianceGamma, ddgi.irradianceGamma);
                        cmd.SetRayTracingIntParam(shader, SSGIShaderIDs._DDGIIrradianceProbeRes, ddgi.irradianceProbeRes);
                        cmd.SetRayTracingIntParam(shader, SSGIShaderIDs._DDGIDistanceProbeRes, ddgi.distanceProbeRes);
                        cmd.SetRayTracingIntParam(shader, SSGIShaderIDs._DDGIProbesPerRow, ddgi.probesPerRow);
                        cmd.SetRayTracingVectorParam(shader, SSGIShaderIDs._DDGIIrradianceTexelSize,
                            new Vector4(ddgi.irradianceTexelSize.x, ddgi.irradianceTexelSize.y, 0, 0));
                        cmd.SetRayTracingVectorParam(shader, SSGIShaderIDs._DDGIDistanceTexelSize,
                            new Vector4(ddgi.distanceTexelSize.x, ddgi.distanceTexelSize.y, 0, 0));
                    }
                    else
                    {

                        cmd.SetRayTracingIntParam(shader, SSGIShaderIDs._RTGIMixedDDGIMode, 0);
                        cmd.SetRayTracingTextureParam(shader, SSGIShaderIDs._DDGIIrradianceAtlas, Texture2D.blackTexture);
                        cmd.SetRayTracingTextureParam(shader, SSGIShaderIDs._DDGIDistanceAtlas, Texture2D.blackTexture);
                    }
                }
                else
                {

                    cmd.SetRayTracingIntParam(shader, SSGIShaderIDs._RTGIMixedDDGIMode, 0);
                    cmd.SetRayTracingTextureParam(shader, SSGIShaderIDs._DDGIIrradianceAtlas, Texture2D.blackTexture);
                    cmd.SetRayTracingTextureParam(shader, SSGIShaderIDs._DDGIDistanceAtlas, Texture2D.blackTexture);
                }

                cmd.DispatchRays(shader, "IndirectDiffuseRayGen", (uint)traceW, (uint)traceH, 1);
            }
        }

        private static void BindMainLightParams(CommandBuffer cmd, RayTracingShader shader, ref RenderingData renderingData)
        {
            int shadowLightIndex = renderingData.lightData.mainLightIndex;
            int cascadeCount = renderingData.shadowData.mainLightShadowCascadesCount;

            Texture shadowmap = Shader.GetGlobalTexture(SSGIShaderIDs._MainLightShadowmapTexture);
            if (shadowmap != null)
            {
                float w = shadowmap.width;
                float h = shadowmap.height;
                cmd.SetGlobalVector(SSGIShaderIDs._MainLightShadowmapSize,
                    new Vector4(1.0f / w, 1.0f / h, w, h));
            }

            cmd.SetGlobalFloat(SSGIShaderIDs._MainLightShadowCascadeCount, (float)cascadeCount);

            if (shadowLightIndex < 0 || !renderingData.shadowData.supportsMainLightShadows)
                return;

            VisibleLight shadowLight = renderingData.lightData.visibleLights[shadowLightIndex];
            Light light = shadowLight.light;
            if (light == null || light.shadows == LightShadows.None)
                return;

            int smWidth = renderingData.shadowData.mainLightShadowmapWidth;
            int smHeight = renderingData.shadowData.mainLightShadowmapHeight;
            int shadowResolution = ShadowUtils.GetMaxTileResolutionInAtlas(smWidth, smHeight, cascadeCount);
            int renderTargetHeight = (cascadeCount == 2) ? smHeight >> 1 : smHeight;

            var shadowMatrices = s_ShadowMatrices;
            var splitDistances = s_SplitDistances;

            bool allSuccess = true;
            for (int i = 0; i < cascadeCount; i++)
            {
                bool success = ShadowUtils.ExtractDirectionalLightMatrix(
                    ref renderingData.cullResults, ref renderingData.shadowData,
                    shadowLightIndex, i, smWidth, renderTargetHeight,
                    shadowResolution, light.shadowNearPlane,
                    out splitDistances[i], out s_SliceData);

                if (!success)
                {
                    allSuccess = false;
                    break;
                }
                shadowMatrices[i] = s_SliceData.shadowTransform;
            }

            if (!allSuccess)
                return;

            Matrix4x4 noOp = Matrix4x4.zero;
            noOp.m22 = SystemInfo.usesReversedZBuffer ? 1.0f : 0.0f;
            for (int i = cascadeCount; i <= 4; i++)
                shadowMatrices[i] = noOp;

            float maxShadowDistanceSq = renderingData.cameraData.maxShadowDistance
                                      * renderingData.cameraData.maxShadowDistance;
            float cascadeBorder = renderingData.shadowData.mainLightShadowCascadeBorder;
            float shadowFadeScale, shadowFadeBias;
            if (cascadeBorder < 0.0001f)
            {
                shadowFadeScale = 1000f;
                shadowFadeBias = -maxShadowDistanceSq * 1000f;
            }
            else
            {
                float border = 1f - cascadeBorder;
                border *= border;
                float distanceFadeNear = border * maxShadowDistanceSq;
                float range = maxShadowDistanceSq - distanceFadeNear;
                shadowFadeScale = 1f / range;
                shadowFadeBias = -distanceFadeNear / range;
            }

            bool softShadows = light.shadows == LightShadows.Soft
                            && renderingData.shadowData.supportsSoftShadows;
            float softShadowsProp = softShadows ? 1f : 0f;

            cmd.SetGlobalMatrixArray(SSGIShaderIDs._MainLightWorldToShadow, shadowMatrices);
            cmd.SetGlobalVector(SSGIShaderIDs._MainLightShadowParams,
                new Vector4(light.shadowStrength, softShadowsProp, shadowFadeScale, shadowFadeBias));

            if (cascadeCount > 1)
            {
                cmd.SetGlobalVector(SSGIShaderIDs._CascadeShadowSplitSpheres0, splitDistances[0]);
                cmd.SetGlobalVector(SSGIShaderIDs._CascadeShadowSplitSpheres1, splitDistances[1]);
                cmd.SetGlobalVector(SSGIShaderIDs._CascadeShadowSplitSpheres2, splitDistances[2]);
                cmd.SetGlobalVector(SSGIShaderIDs._CascadeShadowSplitSpheres3, splitDistances[3]);
                cmd.SetGlobalVector(SSGIShaderIDs._CascadeShadowSplitSphereRadii, new Vector4(
                    splitDistances[0].w * splitDistances[0].w,
                    splitDistances[1].w * splitDistances[1].w,
                    splitDistances[2].w * splitDistances[2].w,
                    splitDistances[3].w * splitDistances[3].w));
            }
        }

        private static readonly Matrix4x4[] s_ShadowMatrices = new Matrix4x4[5];
        private static readonly Vector4[] s_SplitDistances = new Vector4[4];
        private static ShadowSliceData s_SliceData;

        private void PerformRTGIDenoise(CommandBuffer cmd, int width, int height, int fullWidth, int fullHeight,
            Matrix4x4 translateToCamera, Matrix4x4 vpMatrix, Matrix4x4 invVPMatrix, float pixelSpreadAngleTangent)
        {

            RenderTargetIdentifier currentFrameGI = (RenderTargetIdentifier)m_Ctx.RTGIOutputTexture;

            DispatchRTGITemporalFilter(cmd, currentFrameGI,
                m_Ctx.RTGIHistoryHF, (RenderTargetIdentifier)m_Ctx.TemporalOutputTemp,
                width, height, fullWidth, fullHeight,
                translateToCamera, vpMatrix, invVPMatrix, pixelSpreadAngleTangent);

            int pass1Jitter = m_CachedSecondDenoiserPass ? (m_Ctx.FrameIndex & 3) : -1;
            if (m_Ctx.SpatialDenoiseTemp != null)
            {
                DispatchRTGISpatialDenoise(cmd,
                    m_Ctx.TemporalOutputTemp, m_Ctx.SpatialDenoiseTemp,
                    width, height, fullWidth, fullHeight,
                    vpMatrix, invVPMatrix, pixelSpreadAngleTangent,
                    pass1Jitter, m_CachedDenoiserRadius);
            }

            if (m_CachedSecondDenoiserPass)
            {

                RenderTargetIdentifier pass2Input = (m_Ctx.SpatialDenoiseTemp != null)
                    ? (RenderTargetIdentifier)m_Ctx.SpatialDenoiseTemp
                    : (RenderTargetIdentifier)m_Ctx.TemporalOutputTemp;

                DispatchRTGITemporalFilter(cmd, pass2Input,
                    m_Ctx.RTGIHistoryLF, (RenderTargetIdentifier)m_Ctx.TemporalOutputTemp,
                    width, height, fullWidth, fullHeight,
                    translateToCamera, vpMatrix, invVPMatrix, pixelSpreadAngleTangent);

                if (m_Ctx.SpatialDenoiseTemp != null)
                {
                    DispatchRTGISpatialDenoise(cmd,
                        m_Ctx.TemporalOutputTemp, m_Ctx.SpatialDenoiseTemp,
                        width, height, fullWidth, fullHeight,
                        vpMatrix, invVPMatrix, pixelSpreadAngleTangent,
                        -1, m_CachedDenoiserRadius * 0.5f);
                }
            }
        }

        private void PerformMixedDenoise(CommandBuffer cmd,
            SSGIHistoryManager history,
            RenderTargetIdentifier mergedGI,
            int width, int height, int fullWidth, int fullHeight,
            Matrix4x4 translateToCamera, Matrix4x4 vpMatrix, Matrix4x4 invVPMatrix,
            float pixelSpreadAngleTangent)
        {

            DispatchMixedTemporalFilter(cmd, history, mergedGI,
                SSGIHistoryManager.HistoryHF, (RenderTargetIdentifier)m_Ctx.TemporalOutputTemp,
                width, height, fullWidth, fullHeight,
                translateToCamera, vpMatrix, invVPMatrix, pixelSpreadAngleTangent);

            int pass1Jitter = m_CachedSecondDenoiserPass ? (m_Ctx.FrameIndex & 3) : -1;
            if (m_Ctx.SpatialDenoiseTemp != null)
            {
                DispatchRTGISpatialDenoise(cmd,
                    m_Ctx.TemporalOutputTemp, m_Ctx.SpatialDenoiseTemp,
                    width, height, fullWidth, fullHeight,
                    vpMatrix, invVPMatrix, pixelSpreadAngleTangent,
                    pass1Jitter, m_CachedDenoiserRadius);
            }

            if (m_CachedSecondDenoiserPass)
            {

                RenderTargetIdentifier pass2Input = (m_Ctx.SpatialDenoiseTemp != null)
                    ? (RenderTargetIdentifier)m_Ctx.SpatialDenoiseTemp
                    : (RenderTargetIdentifier)m_Ctx.TemporalOutputTemp;

                DispatchMixedTemporalFilter(cmd, history, pass2Input,
                    SSGIHistoryManager.HistoryLF, (RenderTargetIdentifier)m_Ctx.TemporalOutputTemp,
                    width, height, fullWidth, fullHeight,
                    translateToCamera, vpMatrix, invVPMatrix, pixelSpreadAngleTangent);

                if (m_Ctx.SpatialDenoiseTemp != null)
                {
                    DispatchRTGISpatialDenoise(cmd,
                        m_Ctx.TemporalOutputTemp, m_Ctx.SpatialDenoiseTemp,
                        width, height, fullWidth, fullHeight,
                        vpMatrix, invVPMatrix, pixelSpreadAngleTangent,
                        -1, m_CachedDenoiserRadius * 0.5f);
                }
            }
        }

        private void PerformRTAODenoise(CommandBuffer cmd, int width, int height, int fullWidth, int fullHeight,
            Matrix4x4 translateToCamera, Matrix4x4 vpMatrix, Matrix4x4 invVPMatrix, float pixelSpreadAngleTangent)
        {

            RenderTargetIdentifier currentFrameAO = (RenderTargetIdentifier)m_Ctx.RTAOOutputTexture;

            DispatchRTGITemporalFilter(cmd, currentFrameAO,
                m_Ctx.RTAOHistoryHF, (RenderTargetIdentifier)m_Ctx.RTAOTemporalTemp,
                width, height, fullWidth, fullHeight,
                translateToCamera, vpMatrix, invVPMatrix, pixelSpreadAngleTangent);

            DispatchRTAOSpatialDenoise(cmd,
                m_Ctx.RTAOTemporalTemp, m_Ctx.RTAOSpatialTemp,
                width, height, fullWidth, fullHeight);

            if (m_CachedSecondDenoiserPass)
            {
                DispatchRTGITemporalFilter(cmd,
                    (RenderTargetIdentifier)m_Ctx.RTAOSpatialTemp,
                    m_Ctx.RTAOHistoryLF, (RenderTargetIdentifier)m_Ctx.RTAOTemporalTemp,
                    width, height, fullWidth, fullHeight,
                    translateToCamera, vpMatrix, invVPMatrix, pixelSpreadAngleTangent);

                DispatchRTAOSpatialDenoise(cmd,
                    m_Ctx.RTAOTemporalTemp, m_Ctx.RTAOSpatialTemp,
                    width, height, fullWidth, fullHeight);
            }
        }

        private void DispatchRTGITemporalFilter(CommandBuffer cmd,
            RenderTargetIdentifier currentFrameGI,
            RenderTexture historyBuffer,
            RenderTargetIdentifier outputRT,
            int texW, int texH, int fullW, int fullH,
            Matrix4x4 translateToCamera, Matrix4x4 vpMatrix, Matrix4x4 invVPMatrix,
            float pixelSpreadAngleTangent)
        {

            bool historyValid = (historyBuffer == m_Ctx.RTAOHistoryHF || historyBuffer == m_Ctx.RTAOHistoryLF)
                ? m_Ctx.RTAOHistoryValid
                : m_Ctx.RTGIHistoryValid;
            using (new ProfilingScope(cmd, s_TemporalSampler))
            {
                int kernel = m_TemporalFilterKernel;

                cmd.SetComputeTextureParam(m_TemporalFilterCS, kernel,
                    SSGIShaderIDs._SSGICurrentFrameTexture, currentFrameGI);

                RenderTargetIdentifier historyInput = historyValid
                    ? (RenderTargetIdentifier)historyBuffer
                    : currentFrameGI;
                cmd.SetComputeTextureParam(m_TemporalFilterCS, kernel,
                    SSGIShaderIDs._SSGIHistoryTexture, historyInput);

                cmd.SetComputeTextureParam(m_TemporalFilterCS, kernel,
                    SSGIShaderIDs._DepthPyramidTexture, m_CachedDepthTexture);

                RenderTargetIdentifier historyDepthInput = (RenderTargetIdentifier)m_Ctx.RTGIHistoryDepth;
                cmd.SetComputeTextureParam(m_TemporalFilterCS, kernel,
                    SSGIShaderIDs._HistoryDepthTexture, historyDepthInput);

                cmd.SetComputeTextureParam(m_TemporalFilterCS, kernel,
                    s_CameraMotionVectorsTextureID, m_CachedMotionVectorsTexture);
                cmd.SetComputeTextureParam(m_TemporalFilterCS, kernel,
                    s_CameraNormalsTextureID, m_CachedNormalsTexture);

                RenderTargetIdentifier historyNormalInput = (RenderTargetIdentifier)m_Ctx.RTGIHistoryNormal;
                cmd.SetComputeTextureParam(m_TemporalFilterCS, kernel,
                    SSGIShaderIDs._HistoryNormalTexture, historyNormalInput);

                RenderTargetIdentifier historyMVInput = (RenderTargetIdentifier)m_Ctx.RTGIHistoryMotionVector;
                cmd.SetComputeTextureParam(m_TemporalFilterCS, kernel,
                    SSGIShaderIDs._HistoryObjectMotionTexture, historyMVInput);

                cmd.SetComputeTextureParam(m_TemporalFilterCS, kernel,
                    SSGIShaderIDs._TemporalFilterOutputRW, outputRT);

                cmd.SetComputeTextureParam(m_TemporalFilterCS, kernel,
                    SSGIShaderIDs._HistoryOutputRW, historyBuffer);

                float currentExposure = GetCurrentExposureMultiplier();

                cmd.SetComputeMatrixParam(m_TemporalFilterCS,
                    SSGIShaderIDs.unity_MatrixVP, vpMatrix);
                cmd.SetComputeMatrixParam(m_TemporalFilterCS,
                    SSGIShaderIDs.unity_MatrixInvVP, invVPMatrix);

                Matrix4x4 prevVPMatrix = m_Ctx.RTGIPrevGpuProjMatrix
                    * (m_Ctx.RTGIPrevViewMatrix * translateToCamera);
                Matrix4x4 prevInvVPMatrix = prevVPMatrix.inverse;
                cmd.SetComputeMatrixParam(m_TemporalFilterCS,
                    SSGIShaderIDs._PrevInvVPMatrix, prevInvVPMatrix);
                cmd.SetComputeMatrixParam(m_TemporalFilterCS,
                    SSGIShaderIDs._PrevVPMatrix, prevVPMatrix);
                cmd.SetComputeFloatParam(m_TemporalFilterCS,
                    SSGIShaderIDs._PixelSpreadAngleTangent, pixelSpreadAngleTangent);
                cmd.SetComputeFloatParam(m_TemporalFilterCS,
                    SSGIShaderIDs._ExposureMultiplier, currentExposure);
                cmd.SetComputeFloatParam(m_TemporalFilterCS,
                    SSGIShaderIDs._PrevExposureMultiplier, m_Ctx.RTGIPrevExposure);
                cmd.SetComputeVectorParam(m_TemporalFilterCS,
                    SSGIShaderIDs._SSGIScreenSize,
                    SSGIRenderPass.ComputeScreenSize(texW, texH));

                cmd.SetComputeVectorParam(m_TemporalFilterCS,
                    SSGIShaderIDs._FullScreenSize,
                    SSGIRenderPass.ComputeScreenSize(fullW, fullH));

                SSGIRenderPass.ComputeThreadGroups(texW, texH, out int groupsX, out int groupsY);
                cmd.DispatchCompute(m_TemporalFilterCS, kernel, groupsX, groupsY, 1);

            }
        }

        private void DispatchMixedTemporalFilter(CommandBuffer cmd,
            SSGIHistoryManager history,
            RenderTargetIdentifier currentFrameGI, int historyBufferId,
            RenderTargetIdentifier outputRT,
            int texW, int texH, int fullW, int fullH,
            Matrix4x4 translateToCamera, Matrix4x4 vpMatrix, Matrix4x4 invVPMatrix,
            float pixelSpreadAngleTangent)
        {
            using (new ProfilingScope(cmd, s_TemporalSampler))
            {
                int kernel = m_TemporalFilterKernel;

                cmd.SetComputeTextureParam(m_TemporalFilterCS, kernel,
                    SSGIShaderIDs._SSGICurrentFrameTexture, currentFrameGI);

                cmd.SetComputeTextureParam(m_TemporalFilterCS, kernel,
                    SSGIShaderIDs._SSGIHistoryTexture, history.GetPreviousFrame(historyBufferId));

                cmd.SetComputeTextureParam(m_TemporalFilterCS, kernel,
                    SSGIShaderIDs._DepthPyramidTexture, m_CachedDepthTexture);

                cmd.SetComputeTextureParam(m_TemporalFilterCS, kernel,
                    SSGIShaderIDs._HistoryDepthTexture,
                    history.GetPreviousFrame(SSGIHistoryManager.HistoryDepth));

                cmd.SetComputeTextureParam(m_TemporalFilterCS, kernel,
                    s_CameraMotionVectorsTextureID, m_CachedMotionVectorsTexture);
                cmd.SetComputeTextureParam(m_TemporalFilterCS, kernel,
                    s_CameraNormalsTextureID, m_CachedNormalsTexture);

                cmd.SetComputeTextureParam(m_TemporalFilterCS, kernel,
                    SSGIShaderIDs._HistoryNormalTexture,
                    history.GetPreviousFrame(SSGIHistoryManager.HistoryNormal));

                cmd.SetComputeTextureParam(m_TemporalFilterCS, kernel,
                    SSGIShaderIDs._HistoryObjectMotionTexture,
                    history.GetPreviousFrame(SSGIHistoryManager.HistoryMotionVector));

                cmd.SetComputeTextureParam(m_TemporalFilterCS, kernel,
                    SSGIShaderIDs._TemporalFilterOutputRW, outputRT);

                cmd.SetComputeTextureParam(m_TemporalFilterCS, kernel,
                    SSGIShaderIDs._HistoryOutputRW, history.GetCurrentFrame(historyBufferId));

                Matrix4x4 prevVPMatrix = history.PrevGpuProjMatrix
                    * (history.PrevViewMatrix * translateToCamera);
                Matrix4x4 prevInvVPMatrix = prevVPMatrix.inverse;

                float currentExposure = GetCurrentExposureMultiplier();

                cmd.SetComputeMatrixParam(m_TemporalFilterCS,
                    SSGIShaderIDs.unity_MatrixVP, vpMatrix);
                cmd.SetComputeMatrixParam(m_TemporalFilterCS,
                    SSGIShaderIDs.unity_MatrixInvVP, invVPMatrix);
                cmd.SetComputeMatrixParam(m_TemporalFilterCS,
                    SSGIShaderIDs._PrevVPMatrix, prevVPMatrix);
                cmd.SetComputeMatrixParam(m_TemporalFilterCS,
                    SSGIShaderIDs._PrevInvVPMatrix, prevInvVPMatrix);
                cmd.SetComputeFloatParam(m_TemporalFilterCS,
                    SSGIShaderIDs._PixelSpreadAngleTangent, pixelSpreadAngleTangent);
                cmd.SetComputeFloatParam(m_TemporalFilterCS,
                    SSGIShaderIDs._ExposureMultiplier, currentExposure);
                cmd.SetComputeFloatParam(m_TemporalFilterCS,
                    SSGIShaderIDs._PrevExposureMultiplier, history.PrevExposure);
                cmd.SetComputeVectorParam(m_TemporalFilterCS,
                    SSGIShaderIDs._SSGIScreenSize,
                    SSGIRenderPass.ComputeScreenSize(texW, texH));
                cmd.SetComputeVectorParam(m_TemporalFilterCS,
                    SSGIShaderIDs._FullScreenSize,
                    SSGIRenderPass.ComputeScreenSize(fullW, fullH));

                SSGIRenderPass.ComputeThreadGroups(texW, texH, out int groupsX, out int groupsY);
                cmd.DispatchCompute(m_TemporalFilterCS, kernel, groupsX, groupsY, 1);
            }
        }

        private void DispatchRTGISpatialDenoise(CommandBuffer cmd,
            RenderTargetIdentifier inputRT, RenderTargetIdentifier outputRT,
            int texW, int texH, int fullW, int fullH,
            Matrix4x4 vpMatrix, Matrix4x4 invVPMatrix, float pixelSpreadAngleTangent,
            int jitterFramePeriod, float filterRadius)
        {
            using (new ProfilingScope(cmd, s_SpatialSampler))
            {

                if (!m_PointDistributionInitialized && m_HasBNDTextures)
                {
                    cmd.SetComputeTextureParam(m_DiffuseDenoiserCS, m_GeneratePointDistKernel,
                        SSGIShaderIDs._OwenScrambledTexture, m_OwenScrambledTexture);
                    cmd.SetComputeBufferParam(m_DiffuseDenoiserCS, m_GeneratePointDistKernel,
                        SSGIShaderIDs._PointDistributionRW, m_PointDistributionBuffer);
                    cmd.DispatchCompute(m_DiffuseDenoiserCS, m_GeneratePointDistKernel, 1, 1, 1);
                    m_PointDistributionInitialized = true;
                }

                int kernel = m_BilateralFilterColorKernel;

                cmd.SetComputeMatrixParam(m_DiffuseDenoiserCS,
                    SSGIShaderIDs.unity_MatrixVP, vpMatrix);
                cmd.SetComputeMatrixParam(m_DiffuseDenoiserCS,
                    SSGIShaderIDs.unity_MatrixInvVP, invVPMatrix);
                cmd.SetComputeFloatParam(m_DiffuseDenoiserCS,
                    SSGIShaderIDs._DenoiserFilterRadius, filterRadius);
                cmd.SetComputeFloatParam(m_DiffuseDenoiserCS,
                    SSGIShaderIDs._PixelSpreadAngleTangent, pixelSpreadAngleTangent);
                cmd.SetComputeIntParam(m_DiffuseDenoiserCS,
                    SSGIShaderIDs._JitterFramePeriod, jitterFramePeriod);
                cmd.SetComputeVectorParam(m_DiffuseDenoiserCS,
                    SSGIShaderIDs._SSGIScreenSize,
                    SSGIRenderPass.ComputeScreenSize(texW, texH));

                cmd.SetComputeVectorParam(m_DiffuseDenoiserCS,
                    SSGIShaderIDs._FullScreenSize,
                    SSGIRenderPass.ComputeScreenSize(fullW, fullH));

                cmd.SetComputeTextureParam(m_DiffuseDenoiserCS, kernel,
                    SSGIShaderIDs._DenoiseInputTexture, inputRT);
                cmd.SetComputeTextureParam(m_DiffuseDenoiserCS, kernel,
                    SSGIShaderIDs._DenoiseOutputTextureRW, outputRT);
                cmd.SetComputeTextureParam(m_DiffuseDenoiserCS, kernel,
                    SSGIShaderIDs._DepthPyramidTexture, m_CachedDepthTexture);
                cmd.SetComputeTextureParam(m_DiffuseDenoiserCS, kernel,
                    s_CameraNormalsTextureID, m_CachedNormalsTexture);

                cmd.SetComputeBufferParam(m_DiffuseDenoiserCS, kernel,
                    SSGIShaderIDs._PointDistribution, m_PointDistributionBuffer);

                SSGIRenderPass.ComputeThreadGroups(texW, texH, out int groupsX, out int groupsY);
                cmd.DispatchCompute(m_DiffuseDenoiserCS, kernel, groupsX, groupsY, 1);
            }
        }

        private void DispatchRTAOSpatialDenoise(CommandBuffer cmd,
            RenderTargetIdentifier inputRT, RenderTargetIdentifier outputRT,
            int texW, int texH, int fullW, int fullH)
        {
            using (new ProfilingScope(cmd, s_SpatialSampler))
            {
                int kernel = m_BilateralFilterAOKernel;

                cmd.SetComputeVectorParam(m_DiffuseDenoiserCS,
                    SSGIShaderIDs._SSGIScreenSize,
                    SSGIRenderPass.ComputeScreenSize(texW, texH));
                cmd.SetComputeVectorParam(m_DiffuseDenoiserCS,
                    SSGIShaderIDs._FullScreenSize,
                    SSGIRenderPass.ComputeScreenSize(fullW, fullH));

                cmd.SetComputeTextureParam(m_DiffuseDenoiserCS, kernel,
                    SSGIShaderIDs._AODenoiseInputTexture, inputRT);
                cmd.SetComputeTextureParam(m_DiffuseDenoiserCS, kernel,
                    SSGIShaderIDs._AODenoiseOutputTextureRW, outputRT);
                cmd.SetComputeTextureParam(m_DiffuseDenoiserCS, kernel,
                    SSGIShaderIDs._DepthPyramidTexture, m_CachedDepthTexture);
                cmd.SetComputeTextureParam(m_DiffuseDenoiserCS, kernel,
                    s_CameraNormalsTextureID, m_CachedNormalsTexture);

                SSGIRenderPass.ComputeThreadGroups(texW, texH, out int groupsX, out int groupsY);
                cmd.DispatchCompute(m_DiffuseDenoiserCS, kernel, groupsX, groupsY, 1);
            }
        }

        private void DispatchRTGIBilateralUpsample(CommandBuffer cmd,
            int fullWidth, int fullHeight, int halfWidth, int halfHeight,
            RenderTargetIdentifier lowResInput)
        {
            int kernel = m_BilateralUpSampleColorKernel;

            cmd.SetComputeTextureParam(m_BilateralUpsampleCS, kernel,
                SSGIShaderIDs._LowResolutionTexture, lowResInput);

            cmd.SetComputeTextureParam(m_BilateralUpsampleCS, kernel,
                SSGIShaderIDs._OutputUpscaledTexture, m_Ctx.RTGIUpsampleResult);

            cmd.SetComputeTextureParam(m_BilateralUpsampleCS, kernel,
                SSGIShaderIDs._DepthPyramidTexture, m_CachedDepthTexture);

            cmd.SetComputeVectorParam(m_BilateralUpsampleCS, SSGIShaderIDs._HalfScreenSize,
                new Vector4(halfWidth, halfHeight, 1.0f / halfWidth, 1.0f / halfHeight));

            cmd.SetComputeVectorParam(m_BilateralUpsampleCS, SSGIShaderIDs._SSGIScreenSize,
                SSGIRenderPass.ComputeScreenSize(fullWidth, fullHeight));

            cmd.SetComputeFloatParams(m_BilateralUpsampleCS, SSGIShaderIDs._DistanceBasedWeights,
                SSGIBilateralUpsampleDef.DistanceBasedWeights_2x2);
            cmd.SetComputeFloatParams(m_BilateralUpsampleCS, SSGIShaderIDs._TapOffsets,
                SSGIBilateralUpsampleDef.TapOffsets_2x2);

            cmd.SetComputeFloatParam(m_BilateralUpsampleCS, SSGIShaderIDs._RayMarchingLowResPercentage, 0.5f);

            SSGIRenderPass.ComputeThreadGroups(fullWidth, fullHeight, out int groupsX, out int groupsY);
            cmd.DispatchCompute(m_BilateralUpsampleCS, kernel, groupsX, groupsY, 1);
        }

        private void DispatchCopyNormals(CommandBuffer cmd, int texW, int texH, int fullW, int fullH)
        {
            int copyNormalsKernel = m_CopyNormalsKernel;
            cmd.SetComputeTextureParam(m_TemporalFilterCS, copyNormalsKernel,
                s_CameraNormalsTextureID, m_CachedNormalsTexture);
            cmd.SetComputeTextureParam(m_TemporalFilterCS, copyNormalsKernel,
                SSGIShaderIDs._HistoryNormalOutputRW, m_Ctx.RTGIHistoryNormal);
            cmd.SetComputeVectorParam(m_TemporalFilterCS,
                SSGIShaderIDs._SSGIScreenSize,
                SSGIRenderPass.ComputeScreenSize(texW, texH));

            cmd.SetComputeVectorParam(m_TemporalFilterCS,
                SSGIShaderIDs._FullScreenSize,
                SSGIRenderPass.ComputeScreenSize(fullW, fullH));
            SSGIRenderPass.ComputeThreadGroups(texW, texH, out int groupsX, out int groupsY);
            cmd.DispatchCompute(m_TemporalFilterCS, copyNormalsKernel, groupsX, groupsY, 1);

            int copyDepthKernel = m_CopyDepthKernel;
            cmd.SetComputeTextureParam(m_TemporalFilterCS, copyDepthKernel,
                s_CameraDepthTextureID, m_CachedDepthTexture);
            cmd.SetComputeTextureParam(m_TemporalFilterCS, copyDepthKernel,
                SSGIShaderIDs._HistoryDepthOutputRW, m_Ctx.RTGIHistoryDepth);
            cmd.DispatchCompute(m_TemporalFilterCS, copyDepthKernel, groupsX, groupsY, 1);
        }

        private void DispatchCopyNormalsDual(CommandBuffer cmd, SSGIHistoryManager history,
            int texW, int texH, int fullW, int fullH)
        {
            SSGIRenderPass.ComputeThreadGroups(texW, texH, out int groupsX, out int groupsY);

            cmd.SetComputeVectorParam(m_TemporalFilterCS,
                SSGIShaderIDs._SSGIScreenSize,
                SSGIRenderPass.ComputeScreenSize(texW, texH));
            cmd.SetComputeVectorParam(m_TemporalFilterCS,
                SSGIShaderIDs._FullScreenSize,
                SSGIRenderPass.ComputeScreenSize(fullW, fullH));

            int normKernel = m_CopyNormalsDualKernel;
            cmd.SetComputeTextureParam(m_TemporalFilterCS, normKernel,
                s_CameraNormalsTextureID, m_CachedNormalsTexture);
            cmd.SetComputeTextureParam(m_TemporalFilterCS, normKernel,
                SSGIShaderIDs._HistoryNormalOutputRW, history.GetCurrentFrame(SSGIHistoryManager.HistoryNormal));
            cmd.SetComputeTextureParam(m_TemporalFilterCS, normKernel,
                SSGIShaderIDs._HistoryNormalOutputRW2, m_Ctx.RTGIHistoryNormal);
            cmd.DispatchCompute(m_TemporalFilterCS, normKernel, groupsX, groupsY, 1);

            int depthKernel = m_CopyDepthDualKernel;
            cmd.SetComputeTextureParam(m_TemporalFilterCS, depthKernel,
                s_CameraDepthTextureID, m_CachedDepthTexture);
            cmd.SetComputeTextureParam(m_TemporalFilterCS, depthKernel,
                SSGIShaderIDs._HistoryDepthOutputRW, history.GetCurrentFrame(SSGIHistoryManager.HistoryDepth));
            cmd.SetComputeTextureParam(m_TemporalFilterCS, depthKernel,
                SSGIShaderIDs._HistoryDepthOutputRW2, m_Ctx.RTGIHistoryDepth);
            cmd.DispatchCompute(m_TemporalFilterCS, depthKernel, groupsX, groupsY, 1);
        }

        private void DispatchMixedCopyNormals(CommandBuffer cmd, SSGIHistoryManager history,
            int texW, int texH, int fullW, int fullH)
        {

            int copyNormalsKernel = m_CopyNormalsKernel;
            cmd.SetComputeTextureParam(m_TemporalFilterCS, copyNormalsKernel,
                s_CameraNormalsTextureID, m_CachedNormalsTexture);
            cmd.SetComputeTextureParam(m_TemporalFilterCS, copyNormalsKernel,
                SSGIShaderIDs._HistoryNormalOutputRW, history.GetCurrentFrame(SSGIHistoryManager.HistoryNormal));
            cmd.SetComputeVectorParam(m_TemporalFilterCS,
                SSGIShaderIDs._SSGIScreenSize,
                SSGIRenderPass.ComputeScreenSize(texW, texH));
            cmd.SetComputeVectorParam(m_TemporalFilterCS,
                SSGIShaderIDs._FullScreenSize,
                SSGIRenderPass.ComputeScreenSize(fullW, fullH));
            SSGIRenderPass.ComputeThreadGroups(texW, texH, out int groupsX, out int groupsY);
            cmd.DispatchCompute(m_TemporalFilterCS, copyNormalsKernel, groupsX, groupsY, 1);

            int copyDepthKernel = m_CopyDepthKernel;
            cmd.SetComputeTextureParam(m_TemporalFilterCS, copyDepthKernel,
                s_CameraDepthTextureID, m_CachedDepthTexture);
            cmd.SetComputeTextureParam(m_TemporalFilterCS, copyDepthKernel,
                SSGIShaderIDs._HistoryDepthOutputRW, history.GetCurrentFrame(SSGIHistoryManager.HistoryDepth));
            cmd.DispatchCompute(m_TemporalFilterCS, copyDepthKernel, groupsX, groupsY, 1);
        }

        private RenderTargetIdentifier DetermineMixedFinalResult(CommandBuffer cmd,
            bool fullRes, bool denoised,
            RenderTargetIdentifier mergeOutput,
            int fullWidth, int fullHeight, int halfWidth, int halfHeight)
        {

            RenderTargetIdentifier denoiseResult = (denoised && m_Ctx.SpatialDenoiseTemp != null)
                ? (RenderTargetIdentifier)m_Ctx.SpatialDenoiseTemp
                : mergeOutput;

            if (!fullRes && m_BilateralUpsampleCS != null)
            {
                DispatchRTGIBilateralUpsample(cmd, fullWidth, fullHeight, halfWidth, halfHeight, denoiseResult);
                return (RenderTargetIdentifier)m_Ctx.RTGIUpsampleResult;
            }

            return denoiseResult;
        }

        private void BindRTGIDebugBuffers(CommandBuffer cmd)
        {
            SSGIDebugMode debugMode = m_VolumeComponent.debugMode.value;

            bool isRTGIDebug = debugMode == SSGIDebugMode.RTGIOnly
                            || debugMode == SSGIDebugMode.RTGIRayLength
                            || debugMode == SSGIDebugMode.MixedMask
                            || debugMode == SSGIDebugMode.RTGINormal
                            || debugMode == SSGIDebugMode.RTAO
                            || debugMode == SSGIDebugMode.RTGIWithAO
                            || debugMode == SSGIDebugMode.RTGIShadowMap;
            m_Ctx.HasRTGIDebugBindings = isRTGIDebug;
            if (!isRTGIDebug)
                return;

            if (debugMode == SSGIDebugMode.RTGIOnly || debugMode == SSGIDebugMode.RTGINormal
                || debugMode == SSGIDebugMode.RTGIShadowMap)
            {
                m_Ctx.DebugRTGITexture = (RenderTargetIdentifier)m_Ctx.RTGIOutputTexture;
                cmd.SetGlobalTexture(SSGIShaderIDs._SSGIDebugRTGITexture, m_Ctx.RTGIOutputTexture);
            }

            if (debugMode == SSGIDebugMode.RTGIRayLength)
            {
                m_Ctx.DebugRTGIRayLengthTexture = (RenderTargetIdentifier)m_Ctx.RTGIOutputTexture;
                cmd.SetGlobalTexture(SSGIShaderIDs._SSGIDebugRTGIRayLengthTexture, m_Ctx.RTGIOutputTexture);
            }

            if (debugMode == SSGIDebugMode.MixedMask && m_Ctx.RTGIHitValidityMask != null)
            {
                m_Ctx.DebugMixedMaskTexture = (RenderTargetIdentifier)m_Ctx.RTGIHitValidityMask;
                cmd.SetGlobalTexture(SSGIShaderIDs._SSGIDebugMixedMaskTexture, m_Ctx.RTGIHitValidityMask);
            }

            if (debugMode == SSGIDebugMode.RTAO || debugMode == SSGIDebugMode.RTGIWithAO)
            {
                cmd.SetGlobalTexture(SSGIShaderIDs._RTAOOutputTexture, m_Ctx.FinalRTAOResult);
            }
        }

        public override void OnCameraCleanup(CommandBuffer cmd)
        {

        }

        public void Dispose()
        {
            CoreUtils.SafeRelease(m_PointDistributionBuffer);
            m_PointDistributionBuffer = null;
            m_PointDistributionInitialized = false;
            m_VolumeComponent = null;
            m_Ctx = null;
        }

        internal static Vector3 SampleHemisphereCosine(float u1, float u2, Vector3 N)
        {

            float r = Mathf.Sqrt(u1);
            float phi = 2.0f * Mathf.PI * u2;
            float x = r * Mathf.Cos(phi);
            float y = r * Mathf.Sin(phi);
            float z = Mathf.Sqrt(1.0f - u1);

            Matrix4x4 tbn = SSGIRenderPass.GetLocalFrame(N);
            Vector3 tangent = new Vector3(tbn.m00, tbn.m01, tbn.m02);
            Vector3 bitangent = new Vector3(tbn.m10, tbn.m11, tbn.m12);

            Vector3 dir;
            dir.x = tangent.x * x + bitangent.x * y + N.x * z;
            dir.y = tangent.y * x + bitangent.y * y + N.y * z;
            dir.z = tangent.z * x + bitangent.z * y + N.z * z;
            return dir.normalized;
        }

        internal static Color ClampIrradianceHSV(Color c, float clampValue)
        {

            float v = Mathf.Max(c.r, Mathf.Max(c.g, c.b));

            if (v <= clampValue)
                return c;

            float scale = clampValue / v;
            return new Color(c.r * scale, c.g * scale, c.b * scale, c.a);
        }

        internal static Color MergeSSGIAndRTGI(Color ssgi, Color rtgi, float mask)
        {

            return Color.Lerp(rtgi, ssgi, mask);
        }

        internal static bool ShouldEnableMultiBounce(int bounceCount)
        {
            return bounceCount > 1;
        }

        internal static float ComputeSecondPassRadius(float denoiserRadius)
        {
            return denoiserRadius * 0.5f;
        }

        private static float GetCurrentExposureMultiplier()
        {
            var stack = VolumeManager.instance.stack;
            var colorAdj = stack.GetComponent<UnityEngine.Rendering.Universal.ColorAdjustments>();
            return (colorAdj != null && colorAdj.active)
                ? Mathf.Pow(2.0f, colorAdj.postExposure.value)
                : 1.0f;
        }

        private static RenderTexture CreateRT(int width, int height,
            GraphicsFormat format, bool enableRandomWrite, string name)
        {
            var desc = new RenderTextureDescriptor(width, height)
            {
                graphicsFormat = format,
                depthBufferBits = 0,
                useMipMap = false,
                autoGenerateMips = false,
                enableRandomWrite = enableRandomWrite,
                msaaSamples = 1,
                dimension = TextureDimension.Tex2D
            };
            var rt = new RenderTexture(desc);
            rt.name = name;
            rt.Create();
            return rt;
        }

        private static void DestroyRT(ref RenderTexture rt)
        {
            if (rt != null)
            {
                rt.Release();
                Object.DestroyImmediate(rt);
                rt = null;
            }
        }
    }
}
