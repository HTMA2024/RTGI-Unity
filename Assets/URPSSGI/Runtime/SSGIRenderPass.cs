using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering;

namespace URPSSGI
{

    public sealed class SSGIRenderPass : ScriptableRenderPass
    {

        private static readonly ProfilingSampler s_ProfilingSampler =
            new ProfilingSampler("SSGI");

        private static readonly ProfilingSampler s_DepthPyramidSampler =
            new ProfilingSampler("SSGI.DepthPyramid");
        private static readonly ProfilingSampler s_ColorPyramidSampler =
            new ProfilingSampler("SSGI.ColorPyramid");
        private static readonly ProfilingSampler s_TraceSampler =
            new ProfilingSampler("SSGI.Trace");
        private static readonly ProfilingSampler s_ReprojectSampler =
            new ProfilingSampler("SSGI.Reproject");
        private static readonly ProfilingSampler s_UpsampleSampler =
            new ProfilingSampler("SSGI.BilateralUpsample");
        private static readonly ProfilingSampler s_TemporalSampler =
            new ProfilingSampler("SSGI.TemporalFilter");
        private static readonly ProfilingSampler s_SpatialSampler =
            new ProfilingSampler("SSGI.SpatialDenoise");
        private static readonly ProfilingSampler s_DeferredLightingSampler =
            new ProfilingSampler("SSGI.DeferredLighting");

        private static readonly int s_CameraDepthTextureID = Shader.PropertyToID("_CameraDepthTexture");
        private static readonly int s_CameraOpaqueTextureID = Shader.PropertyToID("_CameraOpaqueTexture");
        private static readonly int s_CameraNormalsTextureID = Shader.PropertyToID("_CameraNormalsTexture");
        private static readonly int s_CameraMotionVectorsTextureID = Shader.PropertyToID("_CameraMotionVectorsTexture");

        private static readonly int s_GBuffer0ID = Shader.PropertyToID("_GBuffer0");
        private static readonly int s_GBuffer1ID = Shader.PropertyToID("_GBuffer1");
        private static readonly int s_GBuffer2ID = Shader.PropertyToID("_GBuffer2");

        private static readonly GlobalKeyword s_AccurateNormalsKeyword =
            GlobalKeyword.Create("_SSGI_ACCURATE_NORMALS");

        private readonly ComputeShader m_SSGIComputeShader;
        private readonly ComputeShader m_BilateralUpsampleCS;
        private readonly Texture2D m_BlueNoiseTexture;

        private readonly Texture2D m_OwenScrambledTexture;
        private readonly Texture2D m_ScramblingTileXSPP;
        private readonly Texture2D m_RankingTileXSPP;
        private readonly bool m_HasBNDTextures;

        private readonly ComputeShader m_DeferredLightingCS;
        private readonly int m_DeferredLitKernel;
        private readonly bool m_HasDeferredLightingCS;

        private readonly int m_ReprojectDeferredKernel;
        private readonly int m_ReprojectDeferredHalfKernel;

        private readonly ComputeShader m_TemporalFilterCS;
        private readonly ComputeShader m_DiffuseDenoiserCS;

        private readonly int m_TraceKernel;
        private readonly int m_TraceHalfKernel;
        private readonly int m_ReprojectKernel;
        private readonly int m_ReprojectHalfKernel;

        private readonly int m_TemporalFilterKernel;
        private readonly int m_CopyHistoryKernel;
        private readonly int m_CopyNormalsKernel;
        private readonly int m_CopyMotionVectorsKernel;
        private readonly int m_GeneratePointDistKernel;
        private readonly int m_BilateralFilterColorKernel;
        private readonly bool m_HasDenoiseShaders;

        private ComputeBuffer m_PointDistributionBuffer;
        private bool m_PointDistributionInitialized;

        private readonly int m_BilateralUpSampleColorKernel;

        private readonly DepthPyramidGenerator m_DepthPyramidGen;
        private readonly ColorPyramidGenerator m_ColorPyramidGen;

        private SSGICameraContext m_Ctx;

        private RenderTargetIdentifier m_CachedDepthTexture;
        private RenderTargetIdentifier m_CachedOpaqueTexture;
        private RenderTargetIdentifier m_CachedNormalsTexture;
        private RenderTargetIdentifier m_CachedMotionVectorsTexture;

        private RenderTargetIdentifier m_CachedGBuffer0;
        private RenderTargetIdentifier m_CachedGBuffer1;
        private RenderTargetIdentifier m_CachedGBuffer2;

        private static SSGIRuntimeStats s_LastStats;
        public static ref readonly SSGIRuntimeStats LastStats => ref s_LastStats;

        internal static void UpdateRTGIStats(IndirectDiffuseMode giMode, bool rtasAvail, int rayCount)
        {
            s_LastStats = new SSGIRuntimeStats(
                s_LastStats.workingWidth, s_LastStats.workingHeight,
                s_LastStats.isFullResolution, s_LastStats.activeRTCount,
                s_LastStats.denoiseEnabled, s_LastStats.secondPassEnabled,
                s_LastStats.currentDebugMode,
                giMode, rtasAvail, rayCount);
        }

        private SSGIVolumeComponent m_VolumeComponent;

        private bool m_IsMixedMode;
        private bool m_UseDeferredLighting;
        private int m_MixedRaySteps;

        public SSGIRenderPass(
            ComputeShader ssgiCS,
            ComputeShader depthPyramidCS,
            ComputeShader colorPyramidCS,
            ComputeShader bilateralUpsampleCS,
            Texture2D blueNoiseTex,
            Texture2D owenScrambledTex,
            Texture2D scramblingTileTex,
            Texture2D rankingTileTex,
            ComputeShader temporalFilterCS,
            ComputeShader diffuseDenoiserCS,
            ComputeShader deferredLightingCS = null)
        {
            m_SSGIComputeShader = ssgiCS;
            m_BilateralUpsampleCS = bilateralUpsampleCS;
            m_BlueNoiseTexture = blueNoiseTex;

            m_OwenScrambledTexture = owenScrambledTex;
            m_ScramblingTileXSPP = scramblingTileTex;
            m_RankingTileXSPP = rankingTileTex;
            m_HasBNDTextures = owenScrambledTex != null && scramblingTileTex != null && rankingTileTex != null;

            m_TraceKernel            = ssgiCS.FindKernel("TraceGlobalIllumination");
            m_TraceHalfKernel        = ssgiCS.FindKernel("TraceGlobalIlluminationHalf");
            m_ReprojectKernel        = ssgiCS.FindKernel("ReprojectGlobalIllumination");
            m_ReprojectHalfKernel    = ssgiCS.FindKernel("ReprojectGlobalIlluminationHalf");

            m_ReprojectDeferredKernel     = ssgiCS.FindKernel("ReprojectGlobalIlluminationDeferred");
            m_ReprojectDeferredHalfKernel = ssgiCS.FindKernel("ReprojectGlobalIlluminationDeferredHalf");

            m_DeferredLightingCS = deferredLightingCS;
            m_HasDeferredLightingCS = deferredLightingCS != null;
            if (m_HasDeferredLightingCS)
                m_DeferredLitKernel = deferredLightingCS.FindKernel("SSGIDeferredLit");

            m_BilateralUpSampleColorKernel = bilateralUpsampleCS.FindKernel("BilateralUpSampleColor");

            m_TemporalFilterCS = temporalFilterCS;
            m_DiffuseDenoiserCS = diffuseDenoiserCS;
            if (temporalFilterCS != null && diffuseDenoiserCS != null)
            {
                m_TemporalFilterKernel       = temporalFilterCS.FindKernel("TemporalFilter");
                m_CopyHistoryKernel          = temporalFilterCS.FindKernel("CopyHistory");
                m_CopyNormalsKernel          = temporalFilterCS.FindKernel("CopyNormals");
                m_CopyMotionVectorsKernel    = temporalFilterCS.FindKernel("CopyMotionVectors");
                m_GeneratePointDistKernel    = diffuseDenoiserCS.FindKernel("GeneratePointDistribution");
                m_BilateralFilterColorKernel = diffuseDenoiserCS.FindKernel("BilateralFilterColor");
                m_HasDenoiseShaders = true;

                m_PointDistributionBuffer = new ComputeBuffer(64, 2 * sizeof(float));
            }

            m_DepthPyramidGen = new DepthPyramidGenerator(depthPyramidCS);
            m_ColorPyramidGen = new ColorPyramidGenerator(colorPyramidCS);

            renderPassEvent = RenderPassEvent.AfterRenderingOpaques;

            ConfigureInput(ScriptableRenderPassInput.Depth
                         | ScriptableRenderPassInput.Color
                         | ScriptableRenderPassInput.Normal
                         | ScriptableRenderPassInput.Motion);
        }

        public void Setup(SSGIVolumeComponent volume, bool isMixedMode = false)
        {
            m_VolumeComponent = volume;
            m_IsMixedMode = isMixedMode;

            m_UseDeferredLighting = m_HasDeferredLightingCS
                && (isMixedMode || volume.deferredLighting.value);
            m_MixedRaySteps = isMixedMode ? volume.rtMixedRaySteps.value : 0;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            Camera camera = renderingData.cameraData.camera;
            SSGICameraContext ctx = SSGICameraContext.GetOrCreate(camera);
            m_Ctx = ctx;

            ctx.SSGIExecutedThisFrame = false;

            RenderTextureDescriptor cameraDesc = renderingData.cameraData.cameraTargetDescriptor;
            bool fullRes = m_VolumeComponent.fullResolution.value;
            bool denoise = m_VolumeComponent.denoise.value;
            SSGIDebugMode debugMode = m_VolumeComponent.debugMode.value;

            int fullWidth  = cameraDesc.width;
            int fullHeight = cameraDesc.height;

            if (fullWidth == ctx.AllocatedFullWidth && fullHeight == ctx.AllocatedFullHeight
                && fullRes == ctx.AllocatedFullRes && denoise == ctx.AllocatedDenoise
                && debugMode == ctx.AllocatedDebugMode
                && ctx.SSGIResult != null)
            {

                AllocateHitValidityMaskIfNeeded(ctx, fullWidth, fullHeight);
                AllocateDeferredLightingBuffersIfNeeded(ctx, fullWidth, fullHeight, fullRes);
                return;
            }

            ctx.ReleaseRenderTargets();

            int texWidth   = fullRes ? fullWidth : fullWidth >> 1;
            int texHeight  = fullRes ? fullHeight : fullHeight >> 1;

            ctx.SSGIResult = CreateRT(texWidth, texHeight,
                GraphicsFormat.R16G16B16A16_SFloat, true, "_SSGIResultTexture");

            ctx.HitPointBuffer = CreateRT(texWidth, texHeight,
                GraphicsFormat.R16G16_SFloat, true, "_SSGIHitPointBuffer");

            Vector2Int viewportSize = new Vector2Int(fullWidth, fullHeight);

            if (ctx.DepthMipInfo.mipLevelOffsets == null)
                ctx.DepthMipInfo.Allocate();
            ctx.DepthMipInfo.ComputePackedMipChainInfo(viewportSize);

            if (ctx.ColorMipInfo.mipLevelOffsets == null)
                ctx.ColorMipInfo.Allocate();
            ctx.ColorMipInfo.ComputePackedMipChainInfo(viewportSize);

            Vector2Int depthAtlasSize = ctx.DepthMipInfo.textureSize;
            var depthAtlasDesc = new RenderTextureDescriptor(depthAtlasSize.x, depthAtlasSize.y, RenderTextureFormat.RFloat, 0);
            depthAtlasDesc.useMipMap = false;
            depthAtlasDesc.autoGenerateMips = false;
            depthAtlasDesc.enableRandomWrite = true;
            depthAtlasDesc.sRGB = false;
            depthAtlasDesc.msaaSamples = 1;
            ctx.DepthPyramidAtlas = new RenderTexture(depthAtlasDesc);
            ctx.DepthPyramidAtlas.name = "_DepthPyramidAtlas";
            ctx.DepthPyramidAtlas.Create();

            Vector2Int colorAtlasSize = ctx.ColorMipInfo.textureSize;
            var colorAtlasDesc = new RenderTextureDescriptor(colorAtlasSize.x, colorAtlasSize.y, RenderTextureFormat.ARGBHalf, 0);
            colorAtlasDesc.useMipMap = false;
            colorAtlasDesc.autoGenerateMips = false;
            colorAtlasDesc.enableRandomWrite = true;
            colorAtlasDesc.sRGB = false;
            colorAtlasDesc.msaaSamples = 1;
            ctx.ColorPyramidAtlas = new RenderTexture(colorAtlasDesc);
            ctx.ColorPyramidAtlas.name = "_ColorPyramidAtlas";
            ctx.ColorPyramidAtlas.Create();

            ctx.ColorPyramidAtlasPrev = new RenderTexture(colorAtlasDesc);
            ctx.ColorPyramidAtlasPrev.name = "_ColorPyramidAtlasPrev";
            ctx.ColorPyramidAtlasPrev.Create();

            ctx.DepthMipOffsetBuffer = new ComputeBuffer(ctx.DepthMipInfo.mipLevelCount, sizeof(int) * 2);
            ctx.DepthMipOffsetBuffer.SetData(ctx.DepthMipInfo.mipLevelOffsets, 0, 0, ctx.DepthMipInfo.mipLevelCount);

            if (!fullRes)
            {
                ctx.UpsampleResult = CreateRT(fullWidth, fullHeight,
                    GraphicsFormat.R16G16B16A16_SFloat, true, "_SSGIUpsampleResult");
            }

            ctx.HistoryDepth = CreateRT(fullWidth, fullHeight,
                GraphicsFormat.R32_SFloat, false, "_SSGIHistoryDepth");

            if (denoise && m_HasDenoiseShaders)
            {
                ctx.SpatialDenoiseTemp = CreateRT(texWidth, texHeight,
                    GraphicsFormat.R16G16B16A16_SFloat, true, "_SSGISpatialDenoiseTemp");

                ctx.TemporalOutputTemp = CreateRT(texWidth, texHeight,
                    GraphicsFormat.R16G16B16A16_SFloat, true, "_SSGITemporalOutputTemp");
            }

            if (debugMode == SSGIDebugMode.DenoiseComparison && denoise && m_HasDenoiseShaders)
            {
                ctx.PreDenoiseGI = CreateRT(texWidth, texHeight,
                    GraphicsFormat.R16G16B16A16_SFloat, false, "_SSGIPreDenoiseGI");
            }

            if (ctx.DebugOutputBuffer == null)
            {
                ctx.DebugOutputBuffer = CreateRT(texWidth, texHeight,
                    GraphicsFormat.R16G16B16A16_SFloat, true, "_SSGIDebugOutputBuffer");
            }

            AllocateHitValidityMaskIfNeeded(ctx, fullWidth, fullHeight);

            AllocateDeferredLightingBuffersIfNeeded(ctx, fullWidth, fullHeight, fullRes);

            ctx.AllocatedFullWidth  = fullWidth;
            ctx.AllocatedFullHeight = fullHeight;
            ctx.AllocatedFullRes    = fullRes;
            ctx.AllocatedDenoise    = denoise;
            ctx.AllocatedDebugMode  = debugMode;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {

            var camType = renderingData.cameraData.cameraType;
            if (camType != CameraType.Game && camType != CameraType.SceneView)
                return;

            ScriptableRenderer currentRenderer = renderingData.cameraData.renderer;
            Texture depthTex  = URPTextureResolver.ResolveDepthTexture(currentRenderer);
            Texture opaqueTex = URPTextureResolver.ResolveOpaqueTexture(currentRenderer);

            if (depthTex == null || opaqueTex == null)
                return;

            m_CachedDepthTexture   = new RenderTargetIdentifier(depthTex);
            m_CachedOpaqueTexture  = new RenderTargetIdentifier(opaqueTex);

            Texture normalsTex = URPTextureResolver.ResolveNormalsTexture(currentRenderer);
            Texture motionTex  = URPTextureResolver.ResolveMotionVectorsTexture();
            m_CachedNormalsTexture       = normalsTex != null
                ? new RenderTargetIdentifier(normalsTex)
                : new RenderTargetIdentifier(Texture2D.blackTexture);
            m_CachedMotionVectorsTexture = motionTex != null
                ? new RenderTargetIdentifier(motionTex)
                : new RenderTargetIdentifier(Texture2D.blackTexture);

            if (m_UseDeferredLighting)
            {
                if (URPTextureResolver.ResolveGBuffers(currentRenderer,
                    out Texture gb0, out Texture gb1, out Texture gb2))
                {
                    m_CachedGBuffer0 = new RenderTargetIdentifier(gb0);
                    m_CachedGBuffer1 = new RenderTargetIdentifier(gb1);
                    m_CachedGBuffer2 = new RenderTargetIdentifier(gb2);
                }
                else
                {

                    m_UseDeferredLighting = false;
                }
            }

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, s_ProfilingSampler))
            {
                Camera camera = renderingData.cameraData.camera;

                m_Ctx = SSGICameraContext.GetOrCreate(camera);

                RenderTextureDescriptor cameraDesc = renderingData.cameraData.cameraTargetDescriptor;
                bool fullRes = m_VolumeComponent.fullResolution.value;
                bool denoise = m_VolumeComponent.denoise.value;
                bool secondPass = m_VolumeComponent.secondDenoiserPass.value;

                int fullWidth  = cameraDesc.width;
                int fullHeight = cameraDesc.height;
                int texW = fullRes ? fullWidth : fullWidth >> 1;
                int texH = fullRes ? fullHeight : fullHeight >> 1;

                RenderTargetIdentifier depthTexture = m_CachedDepthTexture;
                RenderTargetIdentifier colorTexture = m_CachedOpaqueTexture;

                SSGIHistoryManager history = SSGIHistoryManager.GetOrCreate(camera);
                history.AllocateBuffersIfNeeded(texW, texH);

                m_DepthPyramidGen.RenderDepthPyramid(cmd, depthTexture, m_Ctx.DepthPyramidAtlas,
                    ref m_Ctx.DepthMipInfo);

                if (!m_UseDeferredLighting)
                {
                    m_ColorPyramidGen.RenderColorPyramid(cmd, colorTexture, m_Ctx.ColorPyramidAtlas,
                        ref m_Ctx.ColorMipInfo);
                }

                cmd.SetGlobalTexture(SSGIShaderIDs._DepthPyramidTexture, m_Ctx.DepthPyramidAtlas);

                if (!m_UseDeferredLighting)
                    cmd.SetGlobalTexture(SSGIShaderIDs._ColorPyramidTexture, m_Ctx.ColorPyramidAtlas);

                cmd.SetGlobalBuffer(SSGIShaderIDs._DepthPyramidMipLevelOffsets, m_Ctx.DepthMipOffsetBuffer);

                cmd.SetKeyword(s_AccurateNormalsKeyword, m_VolumeComponent.useAccurateNormals.value);

                BindSSGICBufferParams(cmd, texW, texH, fullRes, camera);

                DispatchTrace(cmd, texW, texH, fullRes);

                DispatchReproject(cmd, texW, texH, fullRes);

                if (m_UseDeferredLighting)
                {
                    using (new ProfilingScope(cmd, s_DeferredLightingSampler))
                    {
                        DispatchDeferredLighting(cmd, texW, texH, renderingData);
                    }
                }

                if (!m_IsMixedMode && denoise)
                    PerformFullDenoise(cmd, history, texW, texH, fullWidth, fullHeight, fullRes, camera);

                if (!m_IsMixedMode && !fullRes)
                {
                    RenderTargetIdentifier upsampleInput = (denoise && m_HasDenoiseShaders && m_Ctx.SpatialDenoiseTemp != null)
                        ? (RenderTargetIdentifier)m_Ctx.SpatialDenoiseTemp
                        : (RenderTargetIdentifier)m_Ctx.SSGIResult;
                    DispatchBilateralUpsample(cmd, fullWidth, fullHeight, texW, texH, upsampleInput);
                }

                cmd.CopyTexture(m_Ctx.DepthPyramidAtlas, 0, 0, 0, 0, fullWidth, fullHeight,
                    m_Ctx.HistoryDepth, 0, 0, 0, 0);

                RenderTexture historyDepthCurrent = history.GetCurrentFrame(SSGIHistoryManager.HistoryDepth);
                if (fullRes)
                    cmd.CopyTexture(m_Ctx.HistoryDepth, 0, 0, historyDepthCurrent, 0, 0);
                else
                    cmd.Blit(m_Ctx.HistoryDepth, historyDepthCurrent);

                RenderTexture historyNormalCurrent = history.GetCurrentFrame(SSGIHistoryManager.HistoryNormal);
                if (m_HasDenoiseShaders)
                {
                    int copyNormalsKernel = m_CopyNormalsKernel;
                    cmd.SetComputeTextureParam(m_TemporalFilterCS, copyNormalsKernel,
                        s_CameraNormalsTextureID, m_CachedNormalsTexture);
                    cmd.SetComputeTextureParam(m_TemporalFilterCS, copyNormalsKernel,
                        SSGIShaderIDs._HistoryNormalOutputRW, historyNormalCurrent);
                    cmd.SetComputeVectorParam(m_TemporalFilterCS, SSGIShaderIDs._SSGIScreenSize,
                        ComputeScreenSize(texW, texH));
                    cmd.SetComputeVectorParam(m_TemporalFilterCS, SSGIShaderIDs._FullScreenSize,
                        ComputeScreenSize(fullWidth, fullHeight));
                    ComputeThreadGroups(texW, texH, out int normGroupsX, out int normGroupsY);
                    cmd.DispatchCompute(m_TemporalFilterCS, copyNormalsKernel, normGroupsX, normGroupsY, 1);
                }
                else
                {
                    cmd.Blit(m_CachedNormalsTexture, historyNormalCurrent);
                }

                if (m_IsMixedMode)
                {

                    cmd.SetGlobalTexture(SSGIShaderIDs._IndirectDiffuseTexture, m_Ctx.SSGIResult);
                }
                else
                {

                    RenderTargetIdentifier finalResult;
                    if (!fullRes)
                    {

                        finalResult = m_Ctx.UpsampleResult;
                    }
                    else if (denoise && m_HasDenoiseShaders && m_Ctx.SpatialDenoiseTemp != null)
                    {

                        finalResult = m_Ctx.SpatialDenoiseTemp;
                    }
                    else
                    {

                        finalResult = m_Ctx.SSGIResult;
                    }

                    m_Ctx.FinalGIResult = finalResult;
                    cmd.SetGlobalTexture(SSGIShaderIDs._IndirectDiffuseTexture, finalResult);
                }

                if (m_IsMixedMode && m_Ctx.RTGIHitValidityMask != null)
                {
                    cmd.SetGlobalTexture(SSGIShaderIDs._RTGIHitValidityMask, m_Ctx.RTGIHitValidityMask);
                }

                if (m_IsMixedMode)
                {
                    cmd.SetGlobalTexture(SSGIShaderIDs._SSGIGBuffer0, m_Ctx.SSGIGBuffer0);
                    cmd.SetGlobalTexture(SSGIShaderIDs._SSGIGBuffer2, m_Ctx.SSGIGBuffer2);
                    cmd.SetGlobalTexture(SSGIShaderIDs._SSGIHitPositionNDC, m_Ctx.SSGIHitPositionNDC);
                }

                if (!m_IsMixedMode)
                {
                    RenderTargetIdentifier finalResult = m_Ctx.FinalGIResult;
                    m_Ctx.PrevIndirectDiffuseTexture = finalResult;
                    cmd.SetGlobalTexture(SSGIShaderIDs._PrevIndirectDiffuseTexture, finalResult);
                }

                if (!m_IsMixedMode)
                    m_Ctx.PrevIndirectDiffuseTexture = m_Ctx.FinalGIResult;

                SSGIDebugMode debugMode = m_VolumeComponent.debugMode.value;
                BindDebugBuffers(cmd, debugMode, history, denoise);

                if (!m_IsMixedMode)
                    history.SwapAndSetReferenceSize(texW, texH);

                if (!m_IsMixedMode)
                {
                    history.SetCurrentViewAndProj(
                        camera.worldToCameraMatrix,
                        GL.GetGPUProjectionMatrix(camera.projectionMatrix, true));
                    history.SetCurrentExposure(GetCurrentExposureMultiplier());
                }

                if (!m_UseDeferredLighting)
                    m_Ctx.SwapColorPyramid();

                int activeRTCount = 5;
                if (!fullRes) activeRTCount++;
                if (denoise && m_HasDenoiseShaders) activeRTCount++;
                if (debugMode == SSGIDebugMode.DenoiseComparison && denoise && m_HasDenoiseShaders)
                    activeRTCount++;
                activeRTCount++;
                activeRTCount += 3;

                s_LastStats = new SSGIRuntimeStats(
                    texW, texH, fullRes, activeRTCount,
                    denoise && m_HasDenoiseShaders, secondPass, debugMode,
                    m_IsMixedMode ? IndirectDiffuseMode.Mixed : IndirectDiffuseMode.ScreenSpace);

                m_Ctx.FrameIndex = (m_Ctx.FrameIndex + 1) & 0xFFFF;

                m_Ctx.SSGIExecutedThisFrame = true;
            }
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void OnCameraCleanup(CommandBuffer cmd)
        {

        }

        private void ReleaseRenderTargets()
        {

            m_Ctx = null;
        }

        public void Dispose()
        {
            SSGICameraContext.ReleaseAll();
            CoreUtils.SafeRelease(m_PointDistributionBuffer);
            m_PointDistributionBuffer = null;
            m_PointDistributionInitialized = false;
            m_VolumeComponent = null;
            m_Ctx = null;
        }

        internal static Vector4 ComputeScreenSize(int width, int height)
        {
            float rcpW = 1.0f / width;
            float rcpH = 1.0f / height;
            return new Vector4(width, height, rcpW, rcpH);
        }

        internal static Vector3 GetCameraWorldPosition(Camera camera)
        {
            Matrix4x4 c2w = camera.cameraToWorldMatrix;
            return new Vector3(c2w.m03, c2w.m13, c2w.m23);
        }

        private static float GetCurrentExposureMultiplier()
        {
            var stack = VolumeManager.instance.stack;
            var colorAdj = stack.GetComponent<ColorAdjustments>();

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

        private void AllocateHitValidityMaskIfNeeded(SSGICameraContext ctx, int fullWidth, int fullHeight)
        {
            if (!m_IsMixedMode)
                return;

            if (ctx.RTGIHitValidityMask != null
                && ctx.RTGIHitValidityMask.width == fullWidth
                && ctx.RTGIHitValidityMask.height == fullHeight)
                return;

            if (ctx.RTGIHitValidityMask != null)
            {
                ctx.RTGIHitValidityMask.Release();
                Object.DestroyImmediate(ctx.RTGIHitValidityMask);
            }
            ctx.RTGIHitValidityMask = CreateRT(fullWidth, fullHeight,
                GraphicsFormat.R8_UNorm, true, "_RTGIHitValidityMask");
        }

        private void AllocateDeferredLightingBuffersIfNeeded(SSGICameraContext ctx, int fullWidth, int fullHeight, bool fullRes)
        {
            if (!m_UseDeferredLighting)
                return;

            int texW = fullRes ? fullWidth : fullWidth >> 1;
            int texH = fullRes ? fullHeight : fullHeight >> 1;
            ctx.AllocateDeferredLightingBuffersIfNeeded(texW, texH);
        }

        internal static void ComputeThicknessParams(float near, float far, float thickness,
            out float thicknessScale, out float thicknessBias)
        {

            thicknessScale = 1.0f / (1.0f + thickness);

            thicknessBias = -near / (far - near) * (thickness * thicknessScale);
        }

        internal static void ComputeThreadGroups(int width, int height, out int groupsX, out int groupsY)
        {
            groupsX = (width + 7) >> 3;
            groupsY = (height + 7) >> 3;
        }

        private void BindDebugBuffers(CommandBuffer cmd, SSGIDebugMode mode,
            SSGIHistoryManager history, bool denoise)
        {

            m_Ctx.HasDebugBindings = mode != SSGIDebugMode.None;

            if (mode == SSGIDebugMode.None)
                return;

            if (mode == SSGIDebugMode.HitPointUV)
            {
                m_Ctx.DebugHitPointTexture = (RenderTargetIdentifier)m_Ctx.HitPointBuffer;
                cmd.SetGlobalTexture(SSGIShaderIDs._SSGIDebugHitPointTexture, m_Ctx.HitPointBuffer);
            }

            if (mode == SSGIDebugMode.AccumulationCount && denoise && m_HasDenoiseShaders)
            {
                RenderTargetIdentifier accumTex = (RenderTargetIdentifier)history.GetCurrentFrame(SSGIHistoryManager.HistoryHF);
                m_Ctx.DebugAccumCountTexture = accumTex;
                cmd.SetGlobalTexture(SSGIShaderIDs._SSGIDebugAccumCountTexture, accumTex);
            }

            if (mode == SSGIDebugMode.DenoiseComparison && denoise && m_HasDenoiseShaders)
            {
                m_Ctx.DebugPreDenoiseTexture = (RenderTargetIdentifier)m_Ctx.PreDenoiseGI;
                cmd.SetGlobalTexture(SSGIShaderIDs._SSGIDebugPreDenoiseTexture, m_Ctx.PreDenoiseGI);
            }

            if (mode == SSGIDebugMode.RawGI)
            {
                m_Ctx.DebugRawGITexture = (RenderTargetIdentifier)m_Ctx.SSGIResult;
                cmd.SetGlobalTexture(SSGIShaderIDs._SSGIDebugRawGITexture, m_Ctx.SSGIResult);
            }

            if ((int)mode >= (int)SSGIDebugMode.RayDirection && (int)mode <= (int)SSGIDebugMode.DenoisedGI
                && m_Ctx.DebugOutputBuffer != null)
            {
                m_Ctx.DebugOutputTexture = (RenderTargetIdentifier)m_Ctx.DebugOutputBuffer;
                cmd.SetGlobalTexture(SSGIShaderIDs._SSGIDebugOutputTexture, m_Ctx.DebugOutputBuffer);
            }

            if (mode == SSGIDebugMode.MixedMask && m_IsMixedMode && m_Ctx.RTGIHitValidityMask != null)
            {
                m_Ctx.DebugMixedMaskTexture = (RenderTargetIdentifier)m_Ctx.RTGIHitValidityMask;
                m_Ctx.HasRTGIDebugBindings = true;
                cmd.SetGlobalTexture(SSGIShaderIDs._SSGIDebugMixedMaskTexture, m_Ctx.RTGIHitValidityMask);
            }
        }

        internal const float DEPTH_WEIGHT = 1.0f;

        internal static float ComputeBilateralWeight(
            Vector3 centerPos, float centerZ01, Vector3 centerNormal,
            Vector3 tapPos, float tapZ01, Vector3 tapNormal,
            float tapLuminance)
        {

            float depthWeight = Mathf.Max(0f, 1.0f - Mathf.Abs(tapZ01 - centerZ01) * DEPTH_WEIGHT);

            float nd = Mathf.Clamp01(Vector3.Dot(tapNormal, centerNormal));
            float nd2 = nd * nd;
            float normalWeight = nd2 * nd2;

            Vector3 dq = centerPos - tapPos;
            float distance2 = Vector3.Dot(dq, dq);
            float planeError = Mathf.Max(
                Mathf.Abs(Vector3.Dot(dq, tapNormal)),
                Mathf.Abs(Vector3.Dot(dq, centerNormal)));
            float planeW;
            if (distance2 < 0.0001f)
                planeW = 1.0f;
            else
                planeW = Mathf.Clamp01(1.0f - 2.0f * planeError / Mathf.Sqrt(distance2));
            float planeWeight = planeW * planeW;

            float luminanceWeight = 1.0f / (1.0f + tapLuminance);

            return depthWeight * normalWeight * planeWeight * luminanceWeight;
        }

        internal static Matrix4x4 GetLocalFrame(Vector3 localZ)
        {
            float x = localZ.x;
            float y = localZ.y;
            float z = localZ.z;
            float sz = z >= 0.0f ? 1.0f : -1.0f;
            float a = 1.0f / (sz + z);
            float ya = y * a;
            float b = x * ya;
            float c = x * x * a;
            Vector3 localX = new Vector3(c * sz - 1.0f, sz * b, x);
            Vector3 localY = new Vector3(b, y * ya - sz, y);

            Matrix4x4 m = Matrix4x4.identity;
            m.SetRow(0, new Vector4(localX.x, localX.y, localX.z, 0));
            m.SetRow(1, new Vector4(localY.x, localY.y, localY.z, 0));
            m.SetRow(2, new Vector4(localZ.x, localZ.y, localZ.z, 0));
            return m;
        }

        internal static Vector2 SampleDiskCubic(float u1, float u2)
        {
            float r = Mathf.Pow(u1, 1.0f / 3.0f);
            float angle = u2 * 2.0f * Mathf.PI;
            return new Vector2(r * Mathf.Cos(angle), r * Mathf.Sin(angle));
        }

        internal static float ComputeMaxDenoisingRadius(Vector3 posWS, float filterRadius)
        {
            float dist = posWS.magnitude;
            return dist * filterRadius / Mathf.Lerp(5.0f, 50.0f, Mathf.Clamp01(dist * 0.002f));
        }

        internal static float ComputeMaxReprojectionWorldRadius(
            Vector3 posWS, Vector3 normalWS,
            float pixelSpreadAngleTangent, float maxDistance, float pixelTolerance)
        {
            Vector3 viewWS = (-posWS).normalized;
            float parallelPixelFootPrint = pixelSpreadAngleTangent * posWS.magnitude;
            float cosAngle = Mathf.Abs(Vector3.Dot(normalWS, viewWS));
            float realPixelFootPrint = parallelPixelFootPrint / (cosAngle + 0.000001f);
            return Mathf.Max(maxDistance, realPixelFootPrint * pixelTolerance);
        }

        private void DispatchTrace(CommandBuffer cmd, int texWidth, int texHeight, bool fullRes)
        {
            int kernel = fullRes ? m_TraceKernel : m_TraceHalfKernel;

            cmd.SetComputeTextureParam(m_SSGIComputeShader, kernel,
                SSGIShaderIDs._IndirectDiffuseHitPointTextureRW, m_Ctx.HitPointBuffer);

            cmd.SetComputeTextureParam(m_SSGIComputeShader, kernel,
                SSGIShaderIDs._BlueNoiseTexture, m_BlueNoiseTexture);

            if (m_HasBNDTextures)
            {
                cmd.SetComputeTextureParam(m_SSGIComputeShader, kernel,
                    SSGIShaderIDs._OwenScrambledTexture, m_OwenScrambledTexture);
                cmd.SetComputeTextureParam(m_SSGIComputeShader, kernel,
                    SSGIShaderIDs._ScramblingTileXSPP, m_ScramblingTileXSPP);
                cmd.SetComputeTextureParam(m_SSGIComputeShader, kernel,
                    SSGIShaderIDs._RankingTileXSPP, m_RankingTileXSPP);
            }

            cmd.SetComputeTextureParam(m_SSGIComputeShader, kernel,
                SSGIShaderIDs._DepthPyramidTexture, m_Ctx.DepthPyramidAtlas);

            cmd.SetComputeBufferParam(m_SSGIComputeShader, kernel,
                SSGIShaderIDs._DepthPyramidMipLevelOffsets, m_Ctx.DepthMipOffsetBuffer);

            cmd.SetComputeTextureParam(m_SSGIComputeShader, kernel,
                s_CameraNormalsTextureID, m_CachedNormalsTexture);

            if (m_Ctx.DebugOutputBuffer != null)
                cmd.SetComputeTextureParam(m_SSGIComputeShader, kernel,
                    SSGIShaderIDs._SSGIDebugOutputRW, m_Ctx.DebugOutputBuffer);

            ComputeThreadGroups(texWidth, texHeight, out int groupsX, out int groupsY);
            cmd.DispatchCompute(m_SSGIComputeShader, kernel, groupsX, groupsY, 1);
        }

        private void DispatchReproject(CommandBuffer cmd, int texWidth, int texHeight, bool fullRes)
        {

            bool useDeferred = m_UseDeferredLighting;
            int kernel;
            if (useDeferred)
                kernel = fullRes ? m_ReprojectDeferredKernel : m_ReprojectDeferredHalfKernel;
            else
                kernel = fullRes ? m_ReprojectKernel : m_ReprojectHalfKernel;

            cmd.SetComputeTextureParam(m_SSGIComputeShader, kernel,
                SSGIShaderIDs._IndirectDiffuseHitPointTexture, m_Ctx.HitPointBuffer);

            cmd.SetComputeTextureParam(m_SSGIComputeShader, kernel,
                SSGIShaderIDs._IndirectDiffuseTextureRW, m_Ctx.SSGIResult);

            cmd.SetComputeTextureParam(m_SSGIComputeShader, kernel,
                SSGIShaderIDs._ColorPyramidTexture, m_Ctx.ColorPyramidAtlasPrev);

            Vector2Int mip1Off  = m_Ctx.ColorMipInfo.mipLevelOffsets[1];
            Vector2Int mip1Size = m_Ctx.ColorMipInfo.mipLevelSizes[1];
            cmd.SetComputeVectorParam(m_SSGIComputeShader, SSGIShaderIDs._ColorPyramidMip1Params,
                new Vector4(mip1Off.x, mip1Off.y, mip1Size.x, mip1Size.y));

            cmd.SetComputeTextureParam(m_SSGIComputeShader, kernel,
                SSGIShaderIDs._HistoryDepthTexture, m_Ctx.HistoryDepth);

            cmd.SetComputeTextureParam(m_SSGIComputeShader, kernel,
                SSGIShaderIDs._DepthPyramidTexture, m_Ctx.DepthPyramidAtlas);

            cmd.SetComputeTextureParam(m_SSGIComputeShader, kernel,
                s_CameraMotionVectorsTextureID, m_CachedMotionVectorsTexture);

            cmd.SetComputeTextureParam(m_SSGIComputeShader, kernel,
                s_CameraNormalsTextureID, m_CachedNormalsTexture);

            if (m_HasBNDTextures)
            {
                cmd.SetComputeTextureParam(m_SSGIComputeShader, kernel,
                    SSGIShaderIDs._OwenScrambledTexture, m_OwenScrambledTexture);
                cmd.SetComputeTextureParam(m_SSGIComputeShader, kernel,
                    SSGIShaderIDs._ScramblingTileXSPP, m_ScramblingTileXSPP);
                cmd.SetComputeTextureParam(m_SSGIComputeShader, kernel,
                    SSGIShaderIDs._RankingTileXSPP, m_RankingTileXSPP);
            }

            bool multiBounce = m_VolumeComponent.multiBounce.value;
            cmd.SetComputeIntParam(m_SSGIComputeShader, SSGIShaderIDs._SSGIMultiBounce,
                multiBounce ? 1 : 0);
            if (multiBounce)
            {

                cmd.SetComputeTextureParam(m_SSGIComputeShader, kernel,
                    SSGIShaderIDs._GBuffer0, m_CachedGBuffer0);

                cmd.SetComputeTextureParam(m_SSGIComputeShader, kernel,
                    SSGIShaderIDs._PrevIndirectDiffuseTexture, m_Ctx.PrevIndirectDiffuseTexture);
            }

            if (m_Ctx.DebugOutputBuffer != null)
                cmd.SetComputeTextureParam(m_SSGIComputeShader, kernel,
                    SSGIShaderIDs._SSGIDebugOutputRW, m_Ctx.DebugOutputBuffer);

            if (useDeferred || (m_IsMixedMode && m_Ctx.RTGIHitValidityMask != null))
            {

                if (m_Ctx.RTGIHitValidityMask == null)
                {
                    int fullW = fullRes ? texWidth : texWidth << 1;
                    int fullH = fullRes ? texHeight : texHeight << 1;
                    m_Ctx.RTGIHitValidityMask = CreateRT(fullW, fullH,
                        GraphicsFormat.R8_UNorm, true, "_RTGIHitValidityMask");
                }
                cmd.SetComputeTextureParam(m_SSGIComputeShader, kernel,
                    SSGIShaderIDs._RTGIHitValidityMask, m_Ctx.RTGIHitValidityMask);
            }

            if (useDeferred)
            {

                cmd.SetComputeTextureParam(m_SSGIComputeShader, kernel,
                    SSGIShaderIDs._GBuffer0, m_CachedGBuffer0);
                cmd.SetComputeTextureParam(m_SSGIComputeShader, kernel,
                    SSGIShaderIDs._GBuffer1, m_CachedGBuffer1);
                cmd.SetComputeTextureParam(m_SSGIComputeShader, kernel,
                    SSGIShaderIDs._GBuffer2, m_CachedGBuffer2);

                cmd.SetComputeTextureParam(m_SSGIComputeShader, kernel,
                    SSGIShaderIDs._SSGIGBuffer0RW, m_Ctx.SSGIGBuffer0);
                cmd.SetComputeTextureParam(m_SSGIComputeShader, kernel,
                    SSGIShaderIDs._SSGIGBuffer1RW, m_Ctx.SSGIGBuffer1);
                cmd.SetComputeTextureParam(m_SSGIComputeShader, kernel,
                    SSGIShaderIDs._SSGIGBuffer2RW, m_Ctx.SSGIGBuffer2);
                cmd.SetComputeTextureParam(m_SSGIComputeShader, kernel,
                    SSGIShaderIDs._SSGIHitPositionNDCRW, m_Ctx.SSGIHitPositionNDC);
            }

            ComputeThreadGroups(texWidth, texHeight, out int groupsX, out int groupsY);
            cmd.DispatchCompute(m_SSGIComputeShader, kernel, groupsX, groupsY, 1);
        }

        private void DispatchDeferredLighting(CommandBuffer cmd, int texWidth, int texHeight,
            RenderingData renderingData)
        {
            int kernel = m_DeferredLitKernel;

            cmd.SetComputeTextureParam(m_DeferredLightingCS, kernel,
                SSGIShaderIDs._SSGIGBuffer0, m_Ctx.SSGIGBuffer0);
            cmd.SetComputeTextureParam(m_DeferredLightingCS, kernel,
                SSGIShaderIDs._SSGIGBuffer1, m_Ctx.SSGIGBuffer1);
            cmd.SetComputeTextureParam(m_DeferredLightingCS, kernel,
                SSGIShaderIDs._SSGIGBuffer2, m_Ctx.SSGIGBuffer2);
            cmd.SetComputeTextureParam(m_DeferredLightingCS, kernel,
                SSGIShaderIDs._SSGIHitPositionNDC, m_Ctx.SSGIHitPositionNDC);

            cmd.SetComputeTextureParam(m_DeferredLightingCS, kernel,
                SSGIShaderIDs._IndirectDiffuseTextureRW, m_Ctx.SSGIResult);

            Camera camera = renderingData.cameraData.camera;
            Matrix4x4 gpuProj = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true);
            Matrix4x4 vp = gpuProj * camera.worldToCameraMatrix;
            cmd.SetComputeMatrixParam(m_DeferredLightingCS, SSGIShaderIDs.unity_MatrixInvVP, vp.inverse);
            cmd.SetComputeMatrixParam(m_DeferredLightingCS, SSGIShaderIDs.unity_MatrixVP, vp);

            cmd.SetComputeVectorParam(m_DeferredLightingCS, SSGIShaderIDs._SSGIScreenSize,
                ComputeScreenSize(texWidth, texHeight));

            {
                Texture shadowmap = Shader.GetGlobalTexture(SSGIShaderIDs._MainLightShadowmapTexture);
                if (shadowmap != null)
                {
                    float sw = shadowmap.width;
                    float sh = shadowmap.height;
                    cmd.SetGlobalVector(SSGIShaderIDs._MainLightShadowmapSize,
                        new Vector4(1.0f / sw, 1.0f / sh, sw, sh));
                }
            }

            bool multiBounce = m_VolumeComponent.multiBounce.value;
            cmd.SetComputeIntParam(m_DeferredLightingCS, SSGIShaderIDs._SSGIMultiBounce, multiBounce ? 1 : 0);
            if (multiBounce)
            {

                cmd.SetComputeTextureParam(m_DeferredLightingCS, kernel,
                    SSGIShaderIDs._GBuffer0, m_CachedGBuffer0);
                cmd.SetComputeTextureParam(m_DeferredLightingCS, kernel,
                    SSGIShaderIDs._PrevIndirectDiffuseTexture, m_Ctx.PrevIndirectDiffuseTexture);
            }

            ComputeThreadGroups(texWidth, texHeight, out int groupsX, out int groupsY);
            cmd.DispatchCompute(m_DeferredLightingCS, kernel, groupsX, groupsY, 1);
        }

        private void DispatchBilateralUpsample(CommandBuffer cmd, int fullWidth, int fullHeight,
            int halfWidth, int halfHeight, RenderTargetIdentifier lowResInput)
        {
            int kernel = m_BilateralUpSampleColorKernel;

            cmd.SetComputeTextureParam(m_BilateralUpsampleCS, kernel,
                SSGIShaderIDs._LowResolutionTexture, lowResInput);

            cmd.SetComputeTextureParam(m_BilateralUpsampleCS, kernel,
                SSGIShaderIDs._OutputUpscaledTexture, m_Ctx.UpsampleResult);

            cmd.SetComputeTextureParam(m_BilateralUpsampleCS, kernel,
                SSGIShaderIDs._DepthPyramidTexture, m_Ctx.DepthPyramidAtlas);

            Vector4 halfScreenSize = new Vector4(halfWidth, halfHeight, 1.0f / halfWidth, 1.0f / halfHeight);
            cmd.SetComputeVectorParam(m_BilateralUpsampleCS, SSGIShaderIDs._HalfScreenSize, halfScreenSize);

            Vector4 screenSize = ComputeScreenSize(fullWidth, fullHeight);
            cmd.SetComputeVectorParam(m_BilateralUpsampleCS, SSGIShaderIDs._SSGIScreenSize, screenSize);

            cmd.SetComputeFloatParams(m_BilateralUpsampleCS, SSGIShaderIDs._DistanceBasedWeights,
                SSGIBilateralUpsampleDef.DistanceBasedWeights_2x2);
            cmd.SetComputeFloatParams(m_BilateralUpsampleCS, SSGIShaderIDs._TapOffsets,
                SSGIBilateralUpsampleDef.TapOffsets_2x2);

            cmd.SetComputeFloatParam(m_BilateralUpsampleCS, SSGIShaderIDs._RayMarchingLowResPercentage, 0.5f);

            ComputeThreadGroups(fullWidth, fullHeight, out int groupsX, out int groupsY);
            cmd.DispatchCompute(m_BilateralUpsampleCS, kernel, groupsX, groupsY, 1);
        }

        private void DispatchTemporalFilter(CommandBuffer cmd, SSGIHistoryManager history,
            RenderTargetIdentifier currentFrameGI, int historyBufferId,
            RenderTargetIdentifier outputRT, int texW, int texH, int fullW, int fullH,
            Camera camera)
        {
            int kernel = m_TemporalFilterKernel;

            cmd.SetComputeTextureParam(m_TemporalFilterCS, kernel,
                SSGIShaderIDs._SSGICurrentFrameTexture, currentFrameGI);

            cmd.SetComputeTextureParam(m_TemporalFilterCS, kernel,
                SSGIShaderIDs._SSGIHistoryTexture, history.GetPreviousFrame(historyBufferId));

            cmd.SetComputeTextureParam(m_TemporalFilterCS, kernel,
                SSGIShaderIDs._DepthPyramidTexture, m_Ctx.DepthPyramidAtlas);
            cmd.SetComputeTextureParam(m_TemporalFilterCS, kernel,
                SSGIShaderIDs._HistoryDepthTexture, history.GetPreviousFrame(SSGIHistoryManager.HistoryDepth));

            cmd.SetComputeTextureParam(m_TemporalFilterCS, kernel,
                s_CameraMotionVectorsTextureID, m_CachedMotionVectorsTexture);
            cmd.SetComputeTextureParam(m_TemporalFilterCS, kernel,
                s_CameraNormalsTextureID, m_CachedNormalsTexture);
            cmd.SetComputeTextureParam(m_TemporalFilterCS, kernel,
                SSGIShaderIDs._HistoryNormalTexture, history.GetPreviousFrame(SSGIHistoryManager.HistoryNormal));

            cmd.SetComputeTextureParam(m_TemporalFilterCS, kernel,
                SSGIShaderIDs._HistoryObjectMotionTexture, history.GetPreviousFrame(SSGIHistoryManager.HistoryMotionVector));

            cmd.SetComputeTextureParam(m_TemporalFilterCS, kernel,
                SSGIShaderIDs._TemporalFilterOutputRW, outputRT);

            cmd.SetComputeTextureParam(m_TemporalFilterCS, kernel,
                SSGIShaderIDs._HistoryOutputRW, history.GetCurrentFrame(historyBufferId));

            Matrix4x4 viewMatrix = camera.worldToCameraMatrix;
            Matrix4x4 gpuProjMatrix = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true);

            Vector3 cameraPos = GetCameraWorldPosition(camera);
            Matrix4x4 translateToCamera = Matrix4x4.Translate(cameraPos);
            Matrix4x4 vpMatrix = gpuProjMatrix * (viewMatrix * translateToCamera);
            Matrix4x4 invVPMatrix = vpMatrix.inverse;

            float halfFovRad = camera.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float pixelSpreadAngleTangent = Mathf.Tan(halfFovRad) * 2.0f / Mathf.Min(fullW, fullH);

            Matrix4x4 prevVPMatrix = history.PrevGpuProjMatrix * (history.PrevViewMatrix * translateToCamera);
            Matrix4x4 prevInvVPMatrix = prevVPMatrix.inverse;

            float currentExposure = GetCurrentExposureMultiplier();
            float prevExposure = history.PrevExposure;

            cmd.SetComputeMatrixParam(m_TemporalFilterCS, SSGIShaderIDs.unity_MatrixVP, vpMatrix);
            cmd.SetComputeMatrixParam(m_TemporalFilterCS, SSGIShaderIDs.unity_MatrixInvVP, invVPMatrix);
            cmd.SetComputeMatrixParam(m_TemporalFilterCS, SSGIShaderIDs._PrevVPMatrix, prevVPMatrix);
            cmd.SetComputeMatrixParam(m_TemporalFilterCS, SSGIShaderIDs._PrevInvVPMatrix, prevInvVPMatrix);
            cmd.SetComputeFloatParam(m_TemporalFilterCS, SSGIShaderIDs._PixelSpreadAngleTangent,
                pixelSpreadAngleTangent);
            cmd.SetComputeFloatParam(m_TemporalFilterCS, SSGIShaderIDs._ExposureMultiplier,
                currentExposure);
            cmd.SetComputeFloatParam(m_TemporalFilterCS, SSGIShaderIDs._PrevExposureMultiplier,
                prevExposure);
            cmd.SetComputeVectorParam(m_TemporalFilterCS, SSGIShaderIDs._SSGIScreenSize,
                ComputeScreenSize(texW, texH));
            cmd.SetComputeVectorParam(m_TemporalFilterCS, SSGIShaderIDs._FullScreenSize,
                ComputeScreenSize(fullW, fullH));

            ComputeThreadGroups(texW, texH, out int groupsX, out int groupsY);
            cmd.DispatchCompute(m_TemporalFilterCS, kernel, groupsX, groupsY, 1);
        }

        private void DispatchSpatialDenoise(CommandBuffer cmd,
            RenderTargetIdentifier inputRT, RenderTargetIdentifier outputRT,
            int texW, int texH, int fullW, int fullH, Camera camera,
            int jitterFramePeriod, float filterRadius)
        {

            if (!m_PointDistributionInitialized)
            {
                cmd.SetComputeTextureParam(m_DiffuseDenoiserCS, m_GeneratePointDistKernel,
                    SSGIShaderIDs._OwenScrambledTexture, m_OwenScrambledTexture);
                cmd.SetComputeBufferParam(m_DiffuseDenoiserCS, m_GeneratePointDistKernel,
                    SSGIShaderIDs._PointDistributionRW, m_PointDistributionBuffer);
                cmd.DispatchCompute(m_DiffuseDenoiserCS, m_GeneratePointDistKernel, 1, 1, 1);
                m_PointDistributionInitialized = true;
            }

            Matrix4x4 viewMatrix = camera.worldToCameraMatrix;
            Matrix4x4 gpuProjMatrix = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true);

            Vector3 cameraPos = GetCameraWorldPosition(camera);
            Matrix4x4 translateToCamera = Matrix4x4.Translate(cameraPos);
            Matrix4x4 vpMatrix = gpuProjMatrix * (viewMatrix * translateToCamera);
            Matrix4x4 invVPMatrix = vpMatrix.inverse;

            float halfFovRad = camera.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float pixelSpreadAngleTangent = Mathf.Tan(halfFovRad) * 2.0f / Mathf.Min(texW, texH);

            int kernel = m_BilateralFilterColorKernel;

            cmd.SetComputeMatrixParam(m_DiffuseDenoiserCS, SSGIShaderIDs.unity_MatrixVP, vpMatrix);
            cmd.SetComputeMatrixParam(m_DiffuseDenoiserCS, SSGIShaderIDs.unity_MatrixInvVP, invVPMatrix);
            cmd.SetComputeFloatParam(m_DiffuseDenoiserCS, SSGIShaderIDs._DenoiserFilterRadius,
                filterRadius);
            cmd.SetComputeFloatParam(m_DiffuseDenoiserCS, SSGIShaderIDs._PixelSpreadAngleTangent,
                pixelSpreadAngleTangent);
            cmd.SetComputeIntParam(m_DiffuseDenoiserCS, SSGIShaderIDs._JitterFramePeriod,
                jitterFramePeriod);
            cmd.SetComputeVectorParam(m_DiffuseDenoiserCS, SSGIShaderIDs._SSGIScreenSize,
                ComputeScreenSize(texW, texH));
            cmd.SetComputeVectorParam(m_DiffuseDenoiserCS, SSGIShaderIDs._FullScreenSize,
                ComputeScreenSize(fullW, fullH));

            cmd.SetComputeTextureParam(m_DiffuseDenoiserCS, kernel,
                SSGIShaderIDs._DenoiseInputTexture, inputRT);
            cmd.SetComputeTextureParam(m_DiffuseDenoiserCS, kernel,
                SSGIShaderIDs._DenoiseOutputTextureRW, outputRT);
            cmd.SetComputeTextureParam(m_DiffuseDenoiserCS, kernel,
                SSGIShaderIDs._DepthPyramidTexture, m_Ctx.DepthPyramidAtlas);
            cmd.SetComputeTextureParam(m_DiffuseDenoiserCS, kernel,
                s_CameraNormalsTextureID, m_CachedNormalsTexture);

            cmd.SetComputeBufferParam(m_DiffuseDenoiserCS, kernel,
                SSGIShaderIDs._PointDistribution, m_PointDistributionBuffer);

            ComputeThreadGroups(texW, texH, out int groupsX, out int groupsY);
            cmd.DispatchCompute(m_DiffuseDenoiserCS, kernel, groupsX, groupsY, 1);
        }

        private void PerformFullDenoise(CommandBuffer cmd, SSGIHistoryManager history,
            int texW, int texH, int fullW, int fullH, bool fullRes, Camera camera)
        {

            if (!m_HasDenoiseShaders)
                return;

            float denoiserRadius = m_VolumeComponent.denoiserRadius.value;
            bool secondPass = m_VolumeComponent.secondDenoiserPass.value;

            RenderTargetIdentifier currentFrameGI = (RenderTargetIdentifier)m_Ctx.SSGIResult;

            SSGIDebugMode debugMode = m_VolumeComponent.debugMode.value;
            if (debugMode == SSGIDebugMode.DenoiseComparison && m_Ctx.PreDenoiseGI != null)
            {
                cmd.CopyTexture(m_Ctx.SSGIResult, 0, 0, m_Ctx.PreDenoiseGI, 0, 0);
            }

            DispatchTemporalFilter(cmd, history, currentFrameGI,
                SSGIHistoryManager.HistoryHF, (RenderTargetIdentifier)m_Ctx.TemporalOutputTemp,
                texW, texH, fullW, fullH, camera);

            int pass1Jitter = secondPass ? (m_Ctx.FrameIndex & 3) : -1;
            if (m_Ctx.SpatialDenoiseTemp != null)
            {
                DispatchSpatialDenoise(cmd,
                    m_Ctx.TemporalOutputTemp, m_Ctx.SpatialDenoiseTemp,
                    texW, texH, fullW, fullH, camera, pass1Jitter, denoiserRadius);
            }

            if (secondPass)
            {

                RenderTargetIdentifier pass2Input = (m_Ctx.SpatialDenoiseTemp != null)
                    ? (RenderTargetIdentifier)m_Ctx.SpatialDenoiseTemp : (RenderTargetIdentifier)m_Ctx.TemporalOutputTemp;

                DispatchTemporalFilter(cmd, history, pass2Input,
                    SSGIHistoryManager.HistoryLF, (RenderTargetIdentifier)m_Ctx.TemporalOutputTemp,
                    texW, texH, fullW, fullH, camera);

                if (m_Ctx.SpatialDenoiseTemp != null)
                {
                    DispatchSpatialDenoise(cmd,
                        m_Ctx.TemporalOutputTemp, m_Ctx.SpatialDenoiseTemp,
                        texW, texH, fullW, fullH, camera, -1, denoiserRadius * 0.5f);
                }
            }
        }

        private void BindSSGICBufferParams(CommandBuffer cmd, int texWidth, int texHeight,
            bool fullRes, Camera camera)
        {
            float near = camera.nearClipPlane;
            float far  = camera.farClipPlane;
            float thickness = m_VolumeComponent.depthBufferThickness.value;

            ComputeThicknessParams(near, far, thickness, out float thicknessScale, out float thicknessBias);

            int raySteps = m_IsMixedMode ? m_MixedRaySteps : m_VolumeComponent.maxRaySteps.value;
            cmd.SetComputeIntParam(m_SSGIComputeShader, SSGIShaderIDs._RayMarchingSteps,
                raySteps);
            cmd.SetComputeFloatParam(m_SSGIComputeShader, SSGIShaderIDs._RayMarchingThicknessScale,
                thicknessScale);
            cmd.SetComputeFloatParam(m_SSGIComputeShader, SSGIShaderIDs._RayMarchingThicknessBias,
                thicknessBias);
            cmd.SetComputeIntParam(m_SSGIComputeShader, SSGIShaderIDs._RayMarchingReflectsSky,
                1);
            cmd.SetComputeIntParam(m_SSGIComputeShader, SSGIShaderIDs._RayMarchingFallbackHierarchy,
                (int)m_VolumeComponent.rayMissFallback.value);

            int fullWidth  = fullRes ? texWidth : texWidth << 1;
            int fullHeight = fullRes ? texHeight : texHeight << 1;
            Vector4 screenSize = ComputeScreenSize(fullWidth, fullHeight);
            cmd.SetComputeVectorParam(m_SSGIComputeShader, SSGIShaderIDs._SSGIScreenSize, screenSize);

            cmd.SetComputeFloatParam(m_SSGIComputeShader, SSGIShaderIDs._RayMarchingLowResPercentageInv,
                fullRes ? 1.0f : 2.0f);

            cmd.SetComputeIntParam(m_SSGIComputeShader, SSGIShaderIDs._IndirectDiffuseFrameIndex,
                m_Ctx.FrameIndex);

            Matrix4x4 viewMatrix = camera.worldToCameraMatrix;

            Matrix4x4 gpuProjMatrix = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true);

            Vector3 cameraPos = GetCameraWorldPosition(camera);
            Matrix4x4 translateToCamera = Matrix4x4.Translate(cameraPos);
            Matrix4x4 cameraRelativeView = viewMatrix * translateToCamera;
            Matrix4x4 vpMatrix = gpuProjMatrix * cameraRelativeView;
            Matrix4x4 invVPMatrix = vpMatrix.inverse;
            cmd.SetComputeMatrixParam(m_SSGIComputeShader, SSGIShaderIDs.unity_MatrixVP, vpMatrix);
            cmd.SetComputeMatrixParam(m_SSGIComputeShader, SSGIShaderIDs.unity_MatrixInvVP, invVPMatrix);

            cmd.SetComputeVectorParam(m_SSGIComputeShader, SSGIShaderIDs._WorldSpaceCameraPos,
                cameraPos);

            SphericalHarmonicsL2 sh = RenderSettings.ambientProbe;
            cmd.SetComputeVectorParam(m_SSGIComputeShader, SSGIShaderIDs._SSGISHAr,
                new Vector4(sh[0, 3], sh[0, 1], sh[0, 2], sh[0, 0]));
            cmd.SetComputeVectorParam(m_SSGIComputeShader, SSGIShaderIDs._SSGISHAg,
                new Vector4(sh[1, 3], sh[1, 1], sh[1, 2], sh[1, 0]));
            cmd.SetComputeVectorParam(m_SSGIComputeShader, SSGIShaderIDs._SSGISHAb,
                new Vector4(sh[2, 3], sh[2, 1], sh[2, 2], sh[2, 0]));

            cmd.SetComputeVectorParam(m_SSGIComputeShader, SSGIShaderIDs._SSGISHBr,
                new Vector4(sh[0, 4], sh[0, 5], sh[0, 6], sh[0, 7]));
            cmd.SetComputeVectorParam(m_SSGIComputeShader, SSGIShaderIDs._SSGISHBg,
                new Vector4(sh[1, 4], sh[1, 5], sh[1, 6], sh[1, 7]));
            cmd.SetComputeVectorParam(m_SSGIComputeShader, SSGIShaderIDs._SSGISHBb,
                new Vector4(sh[2, 4], sh[2, 5], sh[2, 6], sh[2, 7]));

            cmd.SetComputeVectorParam(m_SSGIComputeShader, SSGIShaderIDs._SSGISHC,
                new Vector4(sh[0, 8], sh[1, 8], sh[2, 8], 0.0f));

            cmd.SetComputeIntParam(m_SSGIComputeShader, SSGIShaderIDs._SSGIDebugMode,
                (int)m_VolumeComponent.debugMode.value);
        }
    }
}
