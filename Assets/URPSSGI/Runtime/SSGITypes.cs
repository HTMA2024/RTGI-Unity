using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace URPSSGI
{

    public struct PackedMipChainInfo
    {
        public Vector2Int textureSize;
        public int mipLevelCount;
        public Vector2Int[] mipLevelSizes;
        public Vector2Int[] mipLevelOffsets;

        private Vector2Int m_CachedViewportSize;

        public void Allocate()
        {
            mipLevelOffsets = new Vector2Int[15];
            mipLevelSizes = new Vector2Int[15];
        }

        public void ComputePackedMipChainInfo(Vector2Int viewportSize)
        {
            if (m_CachedViewportSize == viewportSize)
                return;

            m_CachedViewportSize = viewportSize;

            Vector2Int atlasSize = viewportSize;
            mipLevelSizes[0] = viewportSize;
            mipLevelOffsets[0] = Vector2Int.zero;

            int mipLevel = 0;
            Vector2Int mipSize = viewportSize;

            do
            {
                mipLevel++;

                mipSize.x = Math.Max(1, (mipSize.x + 1) >> 1);
                mipSize.y = Math.Max(1, (mipSize.y + 1) >> 1);

                mipLevelSizes[mipLevel] = mipSize;

                Vector2Int prevMipBegin = mipLevelOffsets[mipLevel - 1];
                Vector2Int prevMipEnd = prevMipBegin + mipLevelSizes[mipLevel - 1];

                Vector2Int mipBegin = new Vector2Int();
                if ((mipLevel & 1) != 0)
                {
                    mipBegin.x = prevMipBegin.x;
                    mipBegin.y = prevMipEnd.y;
                }
                else
                {
                    mipBegin.x = prevMipEnd.x;
                    mipBegin.y = prevMipBegin.y;
                }

                mipLevelOffsets[mipLevel] = mipBegin;

                atlasSize.x = Math.Max(atlasSize.x, mipBegin.x + mipSize.x);
                atlasSize.y = Math.Max(atlasSize.y, mipBegin.y + mipSize.y);
            }
            while (mipSize.x > 1 || mipSize.y > 1);

            textureSize = atlasSize;
            mipLevelCount = mipLevel + 1;
        }
    }

    public enum RayMarchingFallbackHierarchy
    {
        None                  = 0x00,
        Sky                   = 0x01,
        ReflectionProbes      = 0x02,
        ReflectionProbesAndSky = 0x03
    }

    [Serializable]
    public sealed class RayMarchingFallbackHierarchyParameter
        : VolumeParameter<RayMarchingFallbackHierarchy>
    {
        public RayMarchingFallbackHierarchyParameter(
            RayMarchingFallbackHierarchy value,
            bool overrideState = false)
            : base(value, overrideState) { }
    }

    public enum SSGICompositeMode
    {
        Additive       = 0,
        ReplaceAmbient = 1
    }

    [Serializable]
    public sealed class SSGICompositeModeParameter
        : VolumeParameter<SSGICompositeMode>
    {
        public SSGICompositeModeParameter(
            SSGICompositeMode value,
            bool overrideState = false)
            : base(value, overrideState) { }
    }

    public enum SSGIDebugMode
    {
        None              = 0,
        GIOnly            = 1,
        HitPointUV        = 2,
        DepthPyramid      = 3,
        WorldNormal       = 4,
        AccumulationCount = 5,
        DenoiseComparison = 6,
        RawGI             = 7,
        RayDirection      = 8,
        HitValidity       = 9,
        MotionVector      = 10,
        ReprojectUV       = 11,
        DenoisedGI        = 12,
        RTGIOnly          = 13,
        RTGIRayLength     = 14,
        MixedMask         = 15,
        RTGINormal        = 16,
        RTAO              = 17,
        RTGIWithAO        = 18,
        RTGIShadowMap     = 19
    }

    [Serializable]
    public sealed class SSGIDebugModeParameter
        : VolumeParameter<SSGIDebugMode>
    {
        public SSGIDebugModeParameter(
            SSGIDebugMode value,
            bool overrideState = false)
            : base(value, overrideState) { }
    }

    public enum IndirectDiffuseMode
    {
        ScreenSpace = 0,
        RayTraced   = 1,
        Mixed       = 2,
        MixedDDGI   = 3
    }

    [Serializable]
    public sealed class IndirectDiffuseModeParameter
        : VolumeParameter<IndirectDiffuseMode>
    {
        public IndirectDiffuseModeParameter(
            IndirectDiffuseMode value,
            bool overrideState = false)
            : base(value, overrideState) { }
    }
}
