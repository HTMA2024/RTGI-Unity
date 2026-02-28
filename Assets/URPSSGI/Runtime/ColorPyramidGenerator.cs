using UnityEngine;
using UnityEngine.Rendering;

namespace URPSSGI
{

    public sealed class ColorPyramidGenerator
    {
        private readonly ComputeShader m_ColorPyramidCS;
        private readonly int m_GaussianKernel;
        private readonly int m_DownsampleKernel;
        private readonly int m_CopyMip0Kernel;

        private Vector4 m_SizeParam;
        private readonly int[] m_SrcOffsetAndLimit = new int[4];
        private readonly int[] m_DstOffset = new int[4];
        private readonly int[] m_Mip0Offset = new int[4];

        public ColorPyramidGenerator(ComputeShader colorPyramidCS)
        {
            m_ColorPyramidCS = colorPyramidCS;
            m_GaussianKernel   = m_ColorPyramidCS.FindKernel("KColorGaussian");
            m_DownsampleKernel = m_ColorPyramidCS.FindKernel("KColorDownsample");
            m_CopyMip0Kernel   = m_ColorPyramidCS.FindKernel("KCopyMip0");
        }

        public int RenderColorPyramid(
            CommandBuffer cmd,
            RenderTargetIdentifier srcColor,
            RenderTexture dstAtlas,
            ref PackedMipChainInfo info)
        {
            Vector2Int mip0Size = info.mipLevelSizes[0];

            if (mip0Size.x < 8 || mip0Size.y < 8)
                return 0;

            m_SizeParam.x = mip0Size.x;
            m_SizeParam.y = mip0Size.y;
            cmd.SetComputeVectorParam(m_ColorPyramidCS, SSGIShaderIDs._Size, m_SizeParam);

            Vector2Int mip0Off = info.mipLevelOffsets[0];
            m_Mip0Offset[0] = mip0Off.x;
            m_Mip0Offset[1] = mip0Off.y;
            m_Mip0Offset[2] = 0;
            m_Mip0Offset[3] = 0;
            cmd.SetComputeIntParams(m_ColorPyramidCS, SSGIShaderIDs._Mip0Offset, m_Mip0Offset);

            cmd.SetComputeTextureParam(m_ColorPyramidCS, m_CopyMip0Kernel,
                SSGIShaderIDs._Source, srcColor);
            cmd.SetComputeTextureParam(m_ColorPyramidCS, m_CopyMip0Kernel,
                SSGIShaderIDs._Mip0, dstAtlas);

            int copyGroupsX = (mip0Size.x + 7) >> 3;
            int copyGroupsY = (mip0Size.y + 7) >> 3;
            cmd.DispatchCompute(m_ColorPyramidCS, m_CopyMip0Kernel, copyGroupsX, copyGroupsY, 1);

            Vector2Int mip0Lim = mip0Off + mip0Size - Vector2Int.one;
            m_SrcOffsetAndLimit[0] = mip0Off.x;
            m_SrcOffsetAndLimit[1] = mip0Off.y;
            m_SrcOffsetAndLimit[2] = mip0Lim.x;
            m_SrcOffsetAndLimit[3] = mip0Lim.y;
            cmd.SetComputeIntParams(m_ColorPyramidCS, SSGIShaderIDs._SrcOffsetAndLimit, m_SrcOffsetAndLimit);

            m_SizeParam.x = mip0Size.x;
            m_SizeParam.y = mip0Size.y;
            cmd.SetComputeVectorParam(m_ColorPyramidCS, SSGIShaderIDs._Size, m_SizeParam);

            Vector2Int dstOff1 = info.mipLevelOffsets[1];
            m_DstOffset[0] = dstOff1.x;
            m_DstOffset[1] = dstOff1.y;
            m_DstOffset[2] = 0;
            m_DstOffset[3] = 0;
            cmd.SetComputeIntParams(m_ColorPyramidCS, SSGIShaderIDs._DstOffset, m_DstOffset);

            cmd.SetComputeTextureParam(m_ColorPyramidCS, m_GaussianKernel,
                SSGIShaderIDs._Source, dstAtlas);
            cmd.SetComputeTextureParam(m_ColorPyramidCS, m_GaussianKernel,
                SSGIShaderIDs._Destination, dstAtlas);

            Vector2Int dstSize1 = info.mipLevelSizes[1];
            int groupsX = (dstSize1.x + 7) >> 3;
            int groupsY = (dstSize1.y + 7) >> 3;
            cmd.DispatchCompute(m_ColorPyramidCS, m_GaussianKernel, groupsX, groupsY, 1);

            int mipLevel = 2;

            while (mipLevel < info.mipLevelCount)
            {
                Vector2Int srcSize = info.mipLevelSizes[mipLevel - 1];

                if (srcSize.x < 8 && srcSize.y < 8)
                    break;

                m_SizeParam.x = srcSize.x;
                m_SizeParam.y = srcSize.y;
                cmd.SetComputeVectorParam(m_ColorPyramidCS, SSGIShaderIDs._Size, m_SizeParam);

                Vector2Int srcOff = info.mipLevelOffsets[mipLevel - 1];
                Vector2Int srcLim = srcOff + srcSize - Vector2Int.one;
                m_SrcOffsetAndLimit[0] = srcOff.x;
                m_SrcOffsetAndLimit[1] = srcOff.y;
                m_SrcOffsetAndLimit[2] = srcLim.x;
                m_SrcOffsetAndLimit[3] = srcLim.y;
                cmd.SetComputeIntParams(m_ColorPyramidCS, SSGIShaderIDs._SrcOffsetAndLimit, m_SrcOffsetAndLimit);

                Vector2Int dstOff = info.mipLevelOffsets[mipLevel];
                m_DstOffset[0] = dstOff.x;
                m_DstOffset[1] = dstOff.y;
                m_DstOffset[2] = 0;
                m_DstOffset[3] = 0;
                cmd.SetComputeIntParams(m_ColorPyramidCS, SSGIShaderIDs._DstOffset, m_DstOffset);

                cmd.SetComputeTextureParam(m_ColorPyramidCS, m_GaussianKernel,
                    SSGIShaderIDs._Source, dstAtlas);
                cmd.SetComputeTextureParam(m_ColorPyramidCS, m_GaussianKernel,
                    SSGIShaderIDs._Destination, dstAtlas);

                Vector2Int dstSize = info.mipLevelSizes[mipLevel];
                groupsX = (dstSize.x + 7) >> 3;
                groupsY = (dstSize.y + 7) >> 3;
                cmd.DispatchCompute(m_ColorPyramidCS, m_GaussianKernel, groupsX, groupsY, 1);

                mipLevel++;
            }

            return mipLevel;
        }
    }
}
