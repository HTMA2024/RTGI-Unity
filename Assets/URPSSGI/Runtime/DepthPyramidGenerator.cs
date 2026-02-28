using UnityEngine;
using UnityEngine.Rendering;

namespace URPSSGI
{

    public sealed class DepthPyramidGenerator
    {
        private readonly ComputeShader m_DepthPyramidCS;
        private readonly int m_DownsampleKernel;
        private readonly int m_CopyMip0Kernel;

        private readonly int[] m_SrcOffsetAndLimit = new int[4];
        private readonly int[] m_DstOffset = new int[4];

        public DepthPyramidGenerator(ComputeShader depthPyramidCS)
        {
            m_DepthPyramidCS = depthPyramidCS;
            m_DownsampleKernel = m_DepthPyramidCS.FindKernel("KDepthDownsample");
            m_CopyMip0Kernel = m_DepthPyramidCS.FindKernel("KDepthCopyMip0");
        }

        public void RenderDepthPyramid(
            CommandBuffer cmd,
            RenderTargetIdentifier srcDepth,
            RenderTexture dstAtlas,
            ref PackedMipChainInfo info)
        {

            cmd.SetComputeTextureParam(m_DepthPyramidCS, m_CopyMip0Kernel,
                SSGIShaderIDs._DepthPyramidSourceDepth, srcDepth);
            cmd.SetComputeTextureParam(m_DepthPyramidCS, m_CopyMip0Kernel,
                SSGIShaderIDs._DepthMipChain, dstAtlas);

            Vector2Int mip0Size = info.mipLevelSizes[0];
            int groupsX0 = (mip0Size.x + 7) >> 3;
            int groupsY0 = (mip0Size.y + 7) >> 3;
            cmd.DispatchCompute(m_DepthPyramidCS, m_CopyMip0Kernel, groupsX0, groupsY0, 1);

            for (int i = 1; i < info.mipLevelCount; i++)
            {
                Vector2Int dstSize   = info.mipLevelSizes[i];
                Vector2Int dstOff    = info.mipLevelOffsets[i];
                Vector2Int srcSize   = info.mipLevelSizes[i - 1];
                Vector2Int srcOffset = info.mipLevelOffsets[i - 1];
                Vector2Int srcLimit  = srcOffset + srcSize - Vector2Int.one;

                m_SrcOffsetAndLimit[0] = srcOffset.x;
                m_SrcOffsetAndLimit[1] = srcOffset.y;
                m_SrcOffsetAndLimit[2] = srcLimit.x;
                m_SrcOffsetAndLimit[3] = srcLimit.y;

                m_DstOffset[0] = dstOff.x;
                m_DstOffset[1] = dstOff.y;
                m_DstOffset[2] = 0;
                m_DstOffset[3] = 0;

                cmd.SetComputeIntParams(m_DepthPyramidCS, SSGIShaderIDs._SrcOffsetAndLimit, m_SrcOffsetAndLimit);
                cmd.SetComputeIntParams(m_DepthPyramidCS, SSGIShaderIDs._DstOffset, m_DstOffset);
                cmd.SetComputeTextureParam(m_DepthPyramidCS, m_DownsampleKernel,
                    SSGIShaderIDs._DepthMipChain, dstAtlas);

                int groupsX = (dstSize.x + 7) >> 3;
                int groupsY = (dstSize.y + 7) >> 3;
                cmd.DispatchCompute(m_DepthPyramidCS, m_DownsampleKernel, groupsX, groupsY, 1);
            }
        }
    }
}
