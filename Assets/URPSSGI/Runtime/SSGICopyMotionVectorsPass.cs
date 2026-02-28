using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace URPSSGI
{

    public sealed class SSGICopyMotionVectorsPass : ScriptableRenderPass
    {
        private static readonly ProfilingSampler s_ProfilingSampler =
            new ProfilingSampler("SSGI.CopyMotionVectors");

        private static readonly int s_CameraMotionVectorsTextureID =
            Shader.PropertyToID("_CameraMotionVectorsTexture");

        private static readonly int s_CameraDepthTextureID =
            Shader.PropertyToID("_CameraDepthTexture");

        private static readonly int s_InvNonJitteredViewProjMatrixID =
            Shader.PropertyToID("_InvNonJitteredViewProjMatrix");

        private static readonly int s_PrevViewProjMatrixID =
            Shader.PropertyToID("_PrevViewProjMatrix");

        private readonly ComputeShader m_TemporalFilterCS;
        private readonly int m_CopyMotionVectorsKernel;

        private IndirectDiffuseMode m_Mode;
        private bool m_HasDenoiseShaders;

        private Matrix4x4 m_CachedPrevViewMatrix;
        private Matrix4x4 m_CachedPrevGpuProjMatrix;

        public SSGICopyMotionVectorsPass(ComputeShader temporalFilterCS)
        {
            m_TemporalFilterCS = temporalFilterCS;
            m_HasDenoiseShaders = temporalFilterCS != null;
            if (m_HasDenoiseShaders)
                m_CopyMotionVectorsKernel = temporalFilterCS.FindKernel("CopyMotionVectors");

            renderPassEvent = (RenderPassEvent)402;

            ConfigureInput(ScriptableRenderPassInput.Motion);
        }

        public void Setup(IndirectDiffuseMode mode, SSGIHistoryManager history)
        {
            m_Mode = mode;

            if (history != null)
            {
                m_CachedPrevViewMatrix = history.PrevViewMatrix;
                m_CachedPrevGpuProjMatrix = history.PrevGpuProjMatrix;
            }
            else
            {
                m_CachedPrevViewMatrix = Matrix4x4.identity;
                m_CachedPrevGpuProjMatrix = Matrix4x4.identity;
            }
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (!m_HasDenoiseShaders)
                return;

            var camType = renderingData.cameraData.cameraType;
            if (camType != CameraType.Game && camType != CameraType.SceneView)
                return;

            Camera camera = renderingData.cameraData.camera;
            SSGICameraContext ctx = SSGICameraContext.GetOrCreate(camera);

            if (!ctx.SSGIExecutedThisFrame)
                return;

            ScriptableRenderer currentRenderer = renderingData.cameraData.renderer;
            Texture motionTex = URPTextureResolver.ResolveMotionVectorsTexture(currentRenderer);
            if (motionTex == null)
                return;

            RenderTargetIdentifier motionVectorsRT = new RenderTargetIdentifier(motionTex);

            RenderTextureDescriptor cameraDesc = renderingData.cameraData.cameraTargetDescriptor;
            int fullWidth = cameraDesc.width;
            int fullHeight = cameraDesc.height;

            SSGIVolumeComponent volume = VolumeManager.instance.stack.GetComponent<SSGIVolumeComponent>();
            bool fullRes = volume != null && volume.fullResolution.value;
            int texW = fullRes ? fullWidth : fullWidth >> 1;
            int texH = fullRes ? fullHeight : fullHeight >> 1;

            Matrix4x4 viewMatrix = camera.worldToCameraMatrix;
            Matrix4x4 gpuProjMatrix = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true);
            Matrix4x4 vpMatrix = gpuProjMatrix * viewMatrix;
            Matrix4x4 invVPMatrix = vpMatrix.inverse;

            SSGIHistoryManager history = SSGIHistoryManager.GetOrCreate(camera);
            Matrix4x4 prevVPMatrix = m_CachedPrevGpuProjMatrix * m_CachedPrevViewMatrix;

            bool needSSGIHistory = m_Mode == IndirectDiffuseMode.ScreenSpace
                || m_Mode == IndirectDiffuseMode.Mixed
                || m_Mode == IndirectDiffuseMode.MixedDDGI;
            bool needRTGIHistory = m_Mode == IndirectDiffuseMode.RayTraced
                || m_Mode == IndirectDiffuseMode.Mixed
                || m_Mode == IndirectDiffuseMode.MixedDDGI;

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, s_ProfilingSampler))
            {
                int kernel = m_CopyMotionVectorsKernel;

                cmd.SetComputeVectorParam(m_TemporalFilterCS,
                    SSGIShaderIDs._SSGIScreenSize,
                    SSGIRenderPass.ComputeScreenSize(texW, texH));
                cmd.SetComputeVectorParam(m_TemporalFilterCS,
                    SSGIShaderIDs._FullScreenSize,
                    SSGIRenderPass.ComputeScreenSize(fullWidth, fullHeight));

                cmd.SetComputeMatrixParam(m_TemporalFilterCS,
                    s_InvNonJitteredViewProjMatrixID, invVPMatrix);
                cmd.SetComputeMatrixParam(m_TemporalFilterCS,
                    s_PrevViewProjMatrixID, prevVPMatrix);

                cmd.SetComputeTextureParam(m_TemporalFilterCS, kernel,
                    s_CameraMotionVectorsTextureID, motionVectorsRT);

                cmd.SetComputeTextureParam(m_TemporalFilterCS, kernel,
                    s_CameraDepthTextureID,
                    new RenderTargetIdentifier(URPTextureResolver.ResolveDepthTexture(currentRenderer)));

                SSGIRenderPass.ComputeThreadGroups(texW, texH, out int groupsX, out int groupsY);

                if (needSSGIHistory)
                {
                     history = SSGIHistoryManager.GetOrCreate(camera);
                    RenderTexture historyMV = history.GetPreviousFrame(SSGIHistoryManager.HistoryMotionVector);
                    if (historyMV != null)
                    {
                        cmd.SetComputeTextureParam(m_TemporalFilterCS, kernel,
                            SSGIShaderIDs._HistoryMotionVectorOutputRW, historyMV);
                        cmd.DispatchCompute(m_TemporalFilterCS, kernel, groupsX, groupsY, 1);
                    }
                }

                if (needRTGIHistory && ctx.RTGIHistoryMotionVector != null)
                {
                    cmd.SetComputeTextureParam(m_TemporalFilterCS, kernel,
                        SSGIShaderIDs._HistoryMotionVectorOutputRW, ctx.RTGIHistoryMotionVector);
                    cmd.DispatchCompute(m_TemporalFilterCS, kernel, groupsX, groupsY, 1);
                }
            }
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }
}
