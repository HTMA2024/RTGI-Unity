using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace URPSSGI
{

    public sealed class SSGICompositePass : ScriptableRenderPass
    {

        private static readonly ProfilingSampler s_ProfilingSampler =
            new ProfilingSampler("SSGI Composite");
        private static readonly int s_TempRTID = Shader.PropertyToID("_SSGICompositeTempRT");
        private static readonly int s_MainTexID = Shader.PropertyToID("_MainTex");
        private static readonly int s_CopySourceID = Shader.PropertyToID("_SSGICopySource");

        private const string k_ReplaceAmbientKeyword = "_SSGI_REPLACE_AMBIENT";

        private readonly Material m_CompositeMaterial;

        private SSGIVolumeComponent m_VolumeComponent;

        public SSGICompositePass(Material compositeMaterial)
        {
            m_CompositeMaterial = compositeMaterial;
            renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
        }

        public void Setup(SSGIVolumeComponent volume)
        {
            m_VolumeComponent = volume;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {

            var camType = renderingData.cameraData.cameraType;
            if (camType != CameraType.Game && camType != CameraType.SceneView)
                return;

            Camera camera = renderingData.cameraData.camera;
            SSGICameraContext ctx = SSGICameraContext.GetOrCreate(camera);
            if (!ctx.SSGIExecutedThisFrame)
                return;

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, s_ProfilingSampler))
            {
                RenderTextureDescriptor cameraDesc = renderingData.cameraData.cameraTargetDescriptor;
                int w = cameraDesc.width;
                int h = cameraDesc.height;

                cmd.SetGlobalTexture(SSGIShaderIDs._IndirectDiffuseTexture, ctx.FinalGIResult);
                cmd.SetGlobalTexture(SSGIShaderIDs._DepthPyramidTexture, ctx.DepthPyramidAtlas);
                cmd.SetGlobalBuffer(SSGIShaderIDs._DepthPyramidMipLevelOffsets, ctx.DepthMipOffsetBuffer);

                if (ctx.HasDebugBindings)
                {
                    SSGIDebugMode debugMode = m_VolumeComponent.debugMode.value;
                    if (debugMode == SSGIDebugMode.HitPointUV)
                        cmd.SetGlobalTexture(SSGIShaderIDs._SSGIDebugHitPointTexture, ctx.DebugHitPointTexture);
                    if (debugMode == SSGIDebugMode.AccumulationCount)
                        cmd.SetGlobalTexture(SSGIShaderIDs._SSGIDebugAccumCountTexture, ctx.DebugAccumCountTexture);
                    if (debugMode == SSGIDebugMode.DenoiseComparison)
                        cmd.SetGlobalTexture(SSGIShaderIDs._SSGIDebugPreDenoiseTexture, ctx.DebugPreDenoiseTexture);
                    if (debugMode == SSGIDebugMode.RawGI)
                        cmd.SetGlobalTexture(SSGIShaderIDs._SSGIDebugRawGITexture, ctx.DebugRawGITexture);
                    if ((int)debugMode >= (int)SSGIDebugMode.RayDirection
                        && (int)debugMode <= (int)SSGIDebugMode.DenoisedGI)
                        cmd.SetGlobalTexture(SSGIShaderIDs._SSGIDebugOutputTexture, ctx.DebugOutputTexture);
                }

                if (ctx.HasRTGIDebugBindings)
                {
                    SSGIDebugMode debugMode = m_VolumeComponent.debugMode.value;
                    if (debugMode == SSGIDebugMode.RTGIOnly)
                        cmd.SetGlobalTexture(SSGIShaderIDs._SSGIDebugRTGITexture, ctx.DebugRTGITexture);
                    if (debugMode == SSGIDebugMode.RTGINormal)
                        cmd.SetGlobalTexture(SSGIShaderIDs._SSGIDebugRTGITexture, ctx.DebugRTGITexture);
                    if (debugMode == SSGIDebugMode.RTGIShadowMap)
                        cmd.SetGlobalTexture(SSGIShaderIDs._SSGIDebugRTGITexture, ctx.DebugRTGITexture);
                    if (debugMode == SSGIDebugMode.RTGIRayLength)
                        cmd.SetGlobalTexture(SSGIShaderIDs._SSGIDebugRTGIRayLengthTexture, ctx.DebugRTGIRayLengthTexture);
                    if (debugMode == SSGIDebugMode.MixedMask)
                        cmd.SetGlobalTexture(SSGIShaderIDs._SSGIDebugMixedMaskTexture, ctx.DebugMixedMaskTexture);
                    if (debugMode == SSGIDebugMode.RTAO)
                        cmd.SetGlobalTexture(SSGIShaderIDs._RTAOOutputTexture, ctx.FinalRTAOResult);
                    if (debugMode == SSGIDebugMode.RTGIWithAO)
                        cmd.SetGlobalTexture(SSGIShaderIDs._RTAOOutputTexture, ctx.FinalRTAOResult);
                }

                {
                    SSGIDebugMode debugMode = m_VolumeComponent.debugMode.value;
                    if (debugMode == SSGIDebugMode.MotionVector)
                    {
                        IndirectDiffuseMode giMode = m_VolumeComponent.giMode.value;
                        if (giMode == IndirectDiffuseMode.RayTraced)
                        {
                            if (ctx.RTGIHistoryMotionVector != null)
                                cmd.SetGlobalTexture(SSGIShaderIDs._HistoryObjectMotionTexture, ctx.RTGIHistoryMotionVector);
                        }
                        else
                        {
                            SSGIHistoryManager history = SSGIHistoryManager.GetOrCreate(camera);
                            RenderTexture historyMV = history.GetPreviousFrame(SSGIHistoryManager.HistoryMotionVector);
                            if (historyMV != null)
                                cmd.SetGlobalTexture(SSGIShaderIDs._HistoryObjectMotionTexture, historyMV);
                        }
                    }
                }

                {
                    bool rtaoEnabled = m_VolumeComponent.enableRTAO.value;
                    IndirectDiffuseMode giMode = m_VolumeComponent.giMode.value;
                    bool hasRTAO = rtaoEnabled
                        && (giMode == IndirectDiffuseMode.RayTraced || giMode == IndirectDiffuseMode.Mixed || giMode == IndirectDiffuseMode.MixedDDGI);
                    cmd.SetGlobalFloat(SSGIShaderIDs._RTAOIntensity,
                        hasRTAO ? m_VolumeComponent.rtaoIntensity.value : 0.0f);
                    if (hasRTAO)
                        cmd.SetGlobalTexture(SSGIShaderIDs._RTAOOutputTexture, ctx.FinalRTAOResult);
                }

                cmd.SetGlobalFloat(SSGIShaderIDs._GIIntensity, m_VolumeComponent.compositeIntensity.value);

                cmd.SetGlobalVector(SSGIShaderIDs._SSGICompositeScreenSize,
                    new Vector4(w, h, 1.0f / w, 1.0f / h));

                int debugModeInt = (int)m_VolumeComponent.debugMode.value;
                cmd.SetGlobalInt(SSGIShaderIDs._SSGIDebugMode, debugModeInt);
                cmd.SetGlobalInt(SSGIShaderIDs._SSGIDebugMipLevel, m_VolumeComponent.debugMipLevel.value);

                bool replaceAmbient = m_VolumeComponent.compositeMode.value == SSGICompositeMode.ReplaceAmbient;
                CoreUtils.SetKeyword(m_CompositeMaterial, k_ReplaceAmbientKeyword, replaceAmbient);
                CoreUtils.SetKeyword(m_CompositeMaterial, "_SSGI_ACCURATE_NORMALS", m_VolumeComponent.useAccurateNormals.value);

                cmd.GetTemporaryRT(s_TempRTID, w, h, 0,
                    FilterMode.Point, RenderTextureFormat.ARGBHalf);

                RenderTargetIdentifier cameraTarget = renderingData.cameraData.renderer.cameraColorTarget;

                cmd.SetGlobalTexture(s_MainTexID, cameraTarget);
                cmd.SetRenderTarget(s_TempRTID);
                cmd.SetViewport(new Rect(0, 0, w, h));
                cmd.DrawProcedural(Matrix4x4.identity, m_CompositeMaterial, 0, MeshTopology.Triangles, 3);

                cmd.SetGlobalTexture(s_MainTexID, s_TempRTID);
                cmd.SetRenderTarget(cameraTarget);
                cmd.SetViewport(new Rect(0, 0, w, h));
                cmd.DrawProcedural(Matrix4x4.identity, m_CompositeMaterial, 1, MeshTopology.Triangles, 3);

                cmd.ReleaseTemporaryRT(s_TempRTID);
            }
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        internal static Vector3 CompositeAdditive(Vector3 sceneColor, Vector3 giColor,
            float intensity, Vector3 albedo)
        {
            return new Vector3(
                sceneColor.x + albedo.x * giColor.x * intensity,
                sceneColor.y + albedo.y * giColor.y * intensity,
                sceneColor.z + albedo.z * giColor.z * intensity);
        }

        internal static Vector3 CompositeReplaceAmbient(Vector3 sceneColor, Vector3 giColor,
            float intensity, Vector3 ambientEstimate, Vector3 albedo)
        {
            return new Vector3(
                sceneColor.x - ambientEstimate.x + albedo.x * giColor.x * intensity,
                sceneColor.y - ambientEstimate.y + albedo.y * giColor.y * intensity,
                sceneColor.z - ambientEstimate.z + albedo.z * giColor.z * intensity);
        }
    }
}
