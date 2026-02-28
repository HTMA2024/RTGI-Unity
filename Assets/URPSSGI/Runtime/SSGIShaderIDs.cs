using UnityEngine;

namespace URPSSGI
{

    public static class SSGIShaderIDs
    {

        public static readonly int _DepthPyramidTexture;
        public static readonly int _ColorPyramidTexture;
        public static readonly int _NormalBufferTexture;
        public static readonly int _CameraMotionVectorsTexture;
        public static readonly int _IndirectDiffuseTexture;
        public static readonly int _IndirectDiffuseHitPointTexture;
        public static readonly int _IndirectDiffuseHitPointTextureRW;
        public static readonly int _IndirectDiffuseTextureRW;
        public static readonly int _SSGIHistoryHFTexture;
        public static readonly int _SSGIHistoryLFTexture;
        public static readonly int _HistoryDepthTexture;

        public static readonly int _RayMarchingSteps;
        public static readonly int _RayMarchingThicknessScale;
        public static readonly int _RayMarchingThicknessBias;
        public static readonly int _RayMarchingReflectsSky;
        public static readonly int _RayMarchingFallbackHierarchy;
        public static readonly int _RayMarchingLowResPercentageInv;
        public static readonly int _IndirectDiffuseFrameIndex;
        public static readonly int _SSGIScreenSize;

        public static readonly int _DepthMipChain;
        public static readonly int _SrcOffsetAndLimit;
        public static readonly int _DstOffset;
        public static readonly int _DepthPyramidSourceDepth;
        public static readonly int _DepthPyramidMipLevelOffsets;

        public static readonly int _Source;
        public static readonly int _Destination;
        public static readonly int _Size;
        public static readonly int _Mip0;
        public static readonly int _Mip0Offset;
        public static readonly int _ColorPyramidMip1Params;

        public static readonly int _LowResolutionTexture;
        public static readonly int _OutputUpscaledTexture;
        public static readonly int _HalfScreenSize;
        public static readonly int _DistanceBasedWeights;
        public static readonly int _TapOffsets;
        public static readonly int _BlueNoiseTexture;
        public static readonly int _ScramblingTexture;
        public static readonly int _OwenScrambledTexture;
        public static readonly int _ScramblingTileXSPP;
        public static readonly int _RankingTileXSPP;
        public static readonly int _RayMarchingLowResPercentage;

        public static readonly int _SSGICurrentFrameTexture;
        public static readonly int _SSGIHistoryTexture;

        public static readonly int _HistoryNormalTexture;
        public static readonly int _HistoryNormalOutputRW;
        public static readonly int _HistoryDepthOutputRW;
        public static readonly int _TemporalFilterOutputRW;
        public static readonly int _HistoryOutputRW;
        public static readonly int _HistoryNormalOutputRW2;
        public static readonly int _HistoryDepthOutputRW2;

        public static readonly int _HistoryObjectMotionTexture;
        public static readonly int _HistoryMotionVectorOutputRW;

        public static readonly int _DenoiseInputTexture;
        public static readonly int _DenoiseOutputTextureRW;
        public static readonly int _DenoiserFilterRadius;
        public static readonly int _PixelSpreadAngleTangent;
        public static readonly int _JitterFramePeriod;
        public static readonly int _PointDistribution;
        public static readonly int _PointDistributionRW;
        public static readonly int _FullScreenSize;

        public static readonly int _GIIntensity;
        public static readonly int _SSGICompositeScreenSize;
        public static readonly int _GBuffer0;

        public static readonly int _SSGISHAr;
        public static readonly int _SSGISHAg;
        public static readonly int _SSGISHAb;
        public static readonly int _SSGISHBr;
        public static readonly int _SSGISHBg;
        public static readonly int _SSGISHBb;
        public static readonly int _SSGISHC;

        public static readonly int unity_MatrixVP;
        public static readonly int unity_MatrixInvVP;
        public static readonly int _PrevInvVPMatrix;
        public static readonly int _PrevVPMatrix;
        public static readonly int _WorldSpaceCameraPos;

        public static readonly int _RayTracingRayBias;
        public static readonly int _RayTracingDistantRayBias;

        public static readonly int _RTGIInvVPNoJitter;

        public static readonly int _ExposureMultiplier;
        public static readonly int _PrevExposureMultiplier;

        public static readonly int _PrevIndirectDiffuseTexture;
        public static readonly int _SSGIMultiBounce;

        public static readonly int _SSGIDebugMode;
        public static readonly int _SSGIDebugMipLevel;
        public static readonly int _SSGIDebugHitPointTexture;
        public static readonly int _SSGIDebugAccumCountTexture;
        public static readonly int _SSGIDebugPreDenoiseTexture;
        public static readonly int _SSGIDebugRawGITexture;
        public static readonly int _SSGIDebugOutputRW;
        public static readonly int _SSGIDebugOutputTexture;

        public static readonly int _SSGIDebugRTGITexture;
        public static readonly int _SSGIDebugRTGIRayLengthTexture;
        public static readonly int _SSGIDebugMixedMaskTexture;

        public static readonly int _MainLightPosition;
        public static readonly int _MainLightColor;
        public static readonly int _MainLightShadowmapTexture;
        public static readonly int _MainLightWorldToShadow;
        public static readonly int _MainLightShadowParams;
        public static readonly int _CascadeShadowSplitSpheres0;
        public static readonly int _CascadeShadowSplitSpheres1;
        public static readonly int _CascadeShadowSplitSpheres2;
        public static readonly int _CascadeShadowSplitSpheres3;
        public static readonly int _CascadeShadowSplitSphereRadii;

        public static readonly int _MainLightShadowmapSize;
        public static readonly int _MainLightShadowCascadeCount;

        public static readonly int _RaytracingAccelerationStructureName;
        public static readonly int _RaytracingRayMaxLength;
        public static readonly int _RaytracingNumSamples;
        public static readonly int _RaytracingMaxRecursion;
        public static readonly int _RaytracingIntensityClamp;
        public static readonly int _RayTracingLodBias;
        public static readonly int _RayTracingRayMissFallbackHierarchy;
        public static readonly int _RayTracingLastBounceFallbackHierarchy;
        public static readonly int _RayTracingAmbientProbeDimmer;
        public static readonly int _RaytracingFrameIndex;
        public static readonly int _RTGIHitValidityMask;
        public static readonly int _SkyTexture;
        public static readonly int _RTGIOutputTexture;
        public static readonly int _RTGIMixedMode;

        public static readonly int _RTGIDebugNormalMode;

        public static readonly int _RTGIDebugShadowMode;

        public static readonly int _UseAccurateNormals;

        public static readonly int _RayTracingMissFallbackWeight;

        public static readonly int _RayTracingLastBounceWeight;

        public static readonly int _UseShadowRay;

        public static readonly int _RTGIRayTracingScale;

        public static readonly int _RTGIFullScreenSize;

        public static readonly int _RTAOOutputTexture;
        public static readonly int _RTAORadius;
        public static readonly int _RTAOIntensity;
        public static readonly int _EnableRTAO;

        public static readonly int _AODenoiseInputTexture;
        public static readonly int _AODenoiseOutputTextureRW;

        public static readonly int _GBuffer1;
        public static readonly int _GBuffer2;
        public static readonly int _SSGIGBuffer0RW;
        public static readonly int _SSGIGBuffer1RW;
        public static readonly int _SSGIGBuffer2RW;
        public static readonly int _SSGIHitPositionNDCRW;
        public static readonly int _SSGIGBuffer0;
        public static readonly int _SSGIGBuffer1;
        public static readonly int _SSGIGBuffer2;
        public static readonly int _SSGIHitPositionNDC;
        public static readonly int _SSGIDeferredLighting;
        public static readonly int _AdditionalLightsCount;
        public static readonly int _AdditionalLightsPosition;
        public static readonly int _AdditionalLightsColor;
        public static readonly int _AdditionalLightsAttenuation;
        public static readonly int _AdditionalLightsSpotDir;

        public static readonly int _MergedGIOutputRW;
        public static readonly int _SSGILitResult;
        public static readonly int _RTGILitResult;
        public static readonly int _MergeMask;

        public static readonly int _RTGIMixedDDGIMode;
        public static readonly int _DDGIIrradianceAtlas;
        public static readonly int _DDGIDistanceAtlas;
        public static readonly int _DDGIVolumeOrigin;
        public static readonly int _DDGIVolumeSpacing;
        public static readonly int _DDGIVolumeProbeCounts;
        public static readonly int _DDGINormalBias;
        public static readonly int _DDGIViewBias;
        public static readonly int _DDGIIrradianceGamma;
        public static readonly int _DDGIIrradianceProbeRes;
        public static readonly int _DDGIDistanceProbeRes;
        public static readonly int _DDGIProbesPerRow;
        public static readonly int _DDGIIrradianceTexelSize;
        public static readonly int _DDGIDistanceTexelSize;

        static SSGIShaderIDs()
        {

            _DepthPyramidTexture              = Shader.PropertyToID("_DepthPyramidTexture");
            _ColorPyramidTexture              = Shader.PropertyToID("_ColorPyramidTexture");
            _NormalBufferTexture              = Shader.PropertyToID("_NormalBufferTexture");
            _CameraMotionVectorsTexture       = Shader.PropertyToID("_CameraMotionVectorsTexture");
            _IndirectDiffuseTexture           = Shader.PropertyToID("_IndirectDiffuseTexture");
            _IndirectDiffuseHitPointTexture   = Shader.PropertyToID("_IndirectDiffuseHitPointTexture");
            _IndirectDiffuseHitPointTextureRW = Shader.PropertyToID("_IndirectDiffuseHitPointTextureRW");
            _IndirectDiffuseTextureRW         = Shader.PropertyToID("_IndirectDiffuseTextureRW");
            _SSGIHistoryHFTexture             = Shader.PropertyToID("_SSGIHistoryHFTexture");
            _SSGIHistoryLFTexture             = Shader.PropertyToID("_SSGIHistoryLFTexture");
            _HistoryDepthTexture              = Shader.PropertyToID("_HistoryDepthTexture");

            _RayMarchingSteps                 = Shader.PropertyToID("_RayMarchingSteps");
            _RayMarchingThicknessScale        = Shader.PropertyToID("_RayMarchingThicknessScale");
            _RayMarchingThicknessBias         = Shader.PropertyToID("_RayMarchingThicknessBias");
            _RayMarchingReflectsSky           = Shader.PropertyToID("_RayMarchingReflectsSky");
            _RayMarchingFallbackHierarchy     = Shader.PropertyToID("_RayMarchingFallbackHierarchy");
            _RayMarchingLowResPercentageInv   = Shader.PropertyToID("_RayMarchingLowResPercentageInv");
            _IndirectDiffuseFrameIndex        = Shader.PropertyToID("_IndirectDiffuseFrameIndex");
            _SSGIScreenSize                   = Shader.PropertyToID("_SSGIScreenSize");

            _DepthMipChain                    = Shader.PropertyToID("_DepthMipChain");
            _SrcOffsetAndLimit                = Shader.PropertyToID("_SrcOffsetAndLimit");
            _DstOffset                        = Shader.PropertyToID("_DstOffset");
            _DepthPyramidSourceDepth          = Shader.PropertyToID("_DepthPyramidSourceDepth");
            _DepthPyramidMipLevelOffsets      = Shader.PropertyToID("_DepthPyramidMipLevelOffsets");

            _Source                           = Shader.PropertyToID("_Source");
            _Destination                      = Shader.PropertyToID("_Destination");
            _Size                             = Shader.PropertyToID("_Size");
            _Mip0                             = Shader.PropertyToID("_Mip0");
            _Mip0Offset                       = Shader.PropertyToID("_Mip0Offset");
            _ColorPyramidMip1Params           = Shader.PropertyToID("_ColorPyramidMip1Params");

            _LowResolutionTexture             = Shader.PropertyToID("_LowResolutionTexture");
            _OutputUpscaledTexture            = Shader.PropertyToID("_OutputUpscaledTexture");
            _HalfScreenSize                   = Shader.PropertyToID("_HalfScreenSize");
            _DistanceBasedWeights             = Shader.PropertyToID("_DistanceBasedWeights");
            _TapOffsets                       = Shader.PropertyToID("_TapOffsets");
            _BlueNoiseTexture                 = Shader.PropertyToID("_BlueNoiseTexture");
            _ScramblingTexture                = Shader.PropertyToID("_ScramblingTexture");
            _OwenScrambledTexture             = Shader.PropertyToID("_OwenScrambledTexture");
            _ScramblingTileXSPP               = Shader.PropertyToID("_ScramblingTileXSPP");
            _RankingTileXSPP                  = Shader.PropertyToID("_RankingTileXSPP");
            _RayMarchingLowResPercentage      = Shader.PropertyToID("_RayMarchingLowResPercentage");

            _SSGICurrentFrameTexture          = Shader.PropertyToID("_SSGICurrentFrameTexture");
            _SSGIHistoryTexture               = Shader.PropertyToID("_SSGIHistoryTexture");

            _HistoryNormalTexture             = Shader.PropertyToID("_HistoryNormalTexture");
            _HistoryNormalOutputRW            = Shader.PropertyToID("_HistoryNormalOutputRW");
            _HistoryDepthOutputRW             = Shader.PropertyToID("_HistoryDepthOutputRW");
            _TemporalFilterOutputRW           = Shader.PropertyToID("_TemporalFilterOutputRW");
            _HistoryOutputRW                  = Shader.PropertyToID("_HistoryOutputRW");
            _HistoryNormalOutputRW2           = Shader.PropertyToID("_HistoryNormalOutputRW2");
            _HistoryDepthOutputRW2            = Shader.PropertyToID("_HistoryDepthOutputRW2");

            _HistoryObjectMotionTexture       = Shader.PropertyToID("_HistoryObjectMotionTexture");
            _HistoryMotionVectorOutputRW      = Shader.PropertyToID("_HistoryMotionVectorOutputRW");

            _DenoiseInputTexture              = Shader.PropertyToID("_DenoiseInputTexture");
            _DenoiseOutputTextureRW           = Shader.PropertyToID("_DenoiseOutputTextureRW");
            _DenoiserFilterRadius             = Shader.PropertyToID("_DenoiserFilterRadius");
            _PixelSpreadAngleTangent          = Shader.PropertyToID("_PixelSpreadAngleTangent");
            _JitterFramePeriod                = Shader.PropertyToID("_JitterFramePeriod");
            _PointDistribution                = Shader.PropertyToID("_PointDistribution");
            _PointDistributionRW              = Shader.PropertyToID("_PointDistributionRW");
            _FullScreenSize                   = Shader.PropertyToID("_FullScreenSize");

            _GIIntensity                      = Shader.PropertyToID("_GIIntensity");
            _SSGICompositeScreenSize          = Shader.PropertyToID("_SSGICompositeScreenSize");
            _GBuffer0                         = Shader.PropertyToID("_GBuffer0");

            _SSGISHAr                         = Shader.PropertyToID("_SSGISHAr");
            _SSGISHAg                         = Shader.PropertyToID("_SSGISHAg");
            _SSGISHAb                         = Shader.PropertyToID("_SSGISHAb");
            _SSGISHBr                         = Shader.PropertyToID("_SSGISHBr");
            _SSGISHBg                         = Shader.PropertyToID("_SSGISHBg");
            _SSGISHBb                         = Shader.PropertyToID("_SSGISHBb");
            _SSGISHC                          = Shader.PropertyToID("_SSGISHC");

            unity_MatrixVP                    = Shader.PropertyToID("unity_MatrixVP");
            unity_MatrixInvVP                 = Shader.PropertyToID("unity_MatrixInvVP");
            _PrevInvVPMatrix                  = Shader.PropertyToID("_PrevInvVPMatrix");
            _PrevVPMatrix                     = Shader.PropertyToID("_PrevVPMatrix");
            _WorldSpaceCameraPos              = Shader.PropertyToID("_WorldSpaceCameraPos");

            _RayTracingRayBias                    = Shader.PropertyToID("_RayTracingRayBias");
            _RayTracingDistantRayBias             = Shader.PropertyToID("_RayTracingDistantRayBias");

            _RTGIInvVPNoJitter                = Shader.PropertyToID("_RTGIInvVPNoJitter");

            _ExposureMultiplier               = Shader.PropertyToID("_ExposureMultiplier");
            _PrevExposureMultiplier           = Shader.PropertyToID("_PrevExposureMultiplier");

            _PrevIndirectDiffuseTexture       = Shader.PropertyToID("_PrevIndirectDiffuseTexture");
            _SSGIMultiBounce                  = Shader.PropertyToID("_SSGIMultiBounce");

            _SSGIDebugMode                    = Shader.PropertyToID("_SSGIDebugMode");
            _SSGIDebugMipLevel                = Shader.PropertyToID("_SSGIDebugMipLevel");
            _SSGIDebugHitPointTexture         = Shader.PropertyToID("_SSGIDebugHitPointTexture");
            _SSGIDebugAccumCountTexture       = Shader.PropertyToID("_SSGIDebugAccumCountTexture");
            _SSGIDebugPreDenoiseTexture       = Shader.PropertyToID("_SSGIDebugPreDenoiseTexture");
            _SSGIDebugRawGITexture            = Shader.PropertyToID("_SSGIDebugRawGITexture");
            _SSGIDebugOutputRW                = Shader.PropertyToID("_SSGIDebugOutputRW");
            _SSGIDebugOutputTexture           = Shader.PropertyToID("_SSGIDebugOutputTexture");

            _SSGIDebugRTGITexture             = Shader.PropertyToID("_SSGIDebugRTGITexture");
            _SSGIDebugRTGIRayLengthTexture    = Shader.PropertyToID("_SSGIDebugRTGIRayLengthTexture");
            _SSGIDebugMixedMaskTexture        = Shader.PropertyToID("_SSGIDebugMixedMaskTexture");

            _MainLightPosition                    = Shader.PropertyToID("_MainLightPosition");
            _MainLightColor                       = Shader.PropertyToID("_MainLightColor");
            _MainLightShadowmapTexture            = Shader.PropertyToID("_MainLightShadowmapTexture");
            _MainLightWorldToShadow               = Shader.PropertyToID("_MainLightWorldToShadow");
            _MainLightShadowParams                = Shader.PropertyToID("_MainLightShadowParams");
            _CascadeShadowSplitSpheres0           = Shader.PropertyToID("_CascadeShadowSplitSpheres0");
            _CascadeShadowSplitSpheres1           = Shader.PropertyToID("_CascadeShadowSplitSpheres1");
            _CascadeShadowSplitSpheres2           = Shader.PropertyToID("_CascadeShadowSplitSpheres2");
            _CascadeShadowSplitSpheres3           = Shader.PropertyToID("_CascadeShadowSplitSpheres3");
            _CascadeShadowSplitSphereRadii        = Shader.PropertyToID("_CascadeShadowSplitSphereRadii");
            _MainLightShadowmapSize               = Shader.PropertyToID("_MainLightShadowmapSize");
            _MainLightShadowCascadeCount          = Shader.PropertyToID("_MainLightShadowCascadeCount");

            _RaytracingAccelerationStructureName  = Shader.PropertyToID("_RaytracingAccelerationStructureName");
            _RaytracingRayMaxLength               = Shader.PropertyToID("_RaytracingRayMaxLength");
            _RaytracingNumSamples                 = Shader.PropertyToID("_RaytracingNumSamples");
            _RaytracingMaxRecursion               = Shader.PropertyToID("_RaytracingMaxRecursion");
            _RaytracingIntensityClamp             = Shader.PropertyToID("_RaytracingIntensityClamp");
            _RayTracingLodBias                    = Shader.PropertyToID("_RayTracingLodBias");
            _RayTracingRayMissFallbackHierarchy   = Shader.PropertyToID("_RayTracingRayMissFallbackHierarchy");
            _RayTracingLastBounceFallbackHierarchy = Shader.PropertyToID("_RayTracingLastBounceFallbackHierarchy");
            _RayTracingAmbientProbeDimmer         = Shader.PropertyToID("_RayTracingAmbientProbeDimmer");
            _RaytracingFrameIndex                 = Shader.PropertyToID("_RaytracingFrameIndex");
            _RTGIHitValidityMask                  = Shader.PropertyToID("_RTGIHitValidityMask");
            _SkyTexture                           = Shader.PropertyToID("_SkyTexture");
            _RTGIOutputTexture                    = Shader.PropertyToID("_RTGIOutputTexture");
            _RTGIMixedMode                        = Shader.PropertyToID("_RTGIMixedMode");
            _RTGIDebugNormalMode                  = Shader.PropertyToID("_RTGIDebugNormalMode");
            _RTGIDebugShadowMode                  = Shader.PropertyToID("_RTGIDebugShadowMode");
            _UseAccurateNormals                   = Shader.PropertyToID("_UseAccurateNormals");
            _RayTracingMissFallbackWeight         = Shader.PropertyToID("_RayTracingMissFallbackWeight");
            _RayTracingLastBounceWeight           = Shader.PropertyToID("_RayTracingLastBounceWeight");
            _UseShadowRay                         = Shader.PropertyToID("_UseShadowRay");
            _RTGIRayTracingScale                  = Shader.PropertyToID("_RTGIRayTracingScale");
            _RTGIFullScreenSize                   = Shader.PropertyToID("_RTGIFullScreenSize");

            _RTAOOutputTexture                    = Shader.PropertyToID("_RTAOOutputTexture");
            _RTAORadius                           = Shader.PropertyToID("_RTAORadius");
            _RTAOIntensity                        = Shader.PropertyToID("_RTAOIntensity");
            _EnableRTAO                           = Shader.PropertyToID("_EnableRTAO");

            _AODenoiseInputTexture                = Shader.PropertyToID("_AODenoiseInputTexture");
            _AODenoiseOutputTextureRW             = Shader.PropertyToID("_AODenoiseOutputTextureRW");

            _GBuffer1                             = Shader.PropertyToID("_GBuffer1");
            _GBuffer2                             = Shader.PropertyToID("_GBuffer2");
            _SSGIGBuffer0RW                       = Shader.PropertyToID("_SSGIGBuffer0RW");
            _SSGIGBuffer1RW                       = Shader.PropertyToID("_SSGIGBuffer1RW");
            _SSGIGBuffer2RW                       = Shader.PropertyToID("_SSGIGBuffer2RW");
            _SSGIHitPositionNDCRW                 = Shader.PropertyToID("_SSGIHitPositionNDCRW");
            _SSGIGBuffer0                         = Shader.PropertyToID("_SSGIGBuffer0");
            _SSGIGBuffer1                         = Shader.PropertyToID("_SSGIGBuffer1");
            _SSGIGBuffer2                         = Shader.PropertyToID("_SSGIGBuffer2");
            _SSGIHitPositionNDC                   = Shader.PropertyToID("_SSGIHitPositionNDC");
            _SSGIDeferredLighting                 = Shader.PropertyToID("_SSGIDeferredLighting");
            _AdditionalLightsCount                = Shader.PropertyToID("_AdditionalLightsCount");
            _AdditionalLightsPosition             = Shader.PropertyToID("_AdditionalLightsPosition");
            _AdditionalLightsColor                = Shader.PropertyToID("_AdditionalLightsColor");
            _AdditionalLightsAttenuation          = Shader.PropertyToID("_AdditionalLightsAttenuation");
            _AdditionalLightsSpotDir              = Shader.PropertyToID("_AdditionalLightsSpotDir");

            _MergedGIOutputRW                     = Shader.PropertyToID("_MergedGIOutputRW");
            _SSGILitResult                        = Shader.PropertyToID("_SSGILitResult");
            _RTGILitResult                        = Shader.PropertyToID("_RTGILitResult");
            _MergeMask                            = Shader.PropertyToID("_MergeMask");

            _RTGIMixedDDGIMode                    = Shader.PropertyToID("_RTGIMixedDDGIMode");
            _DDGIIrradianceAtlas                  = Shader.PropertyToID("_DDGIIrradianceAtlas");
            _DDGIDistanceAtlas                    = Shader.PropertyToID("_DDGIDistanceAtlas");
            _DDGIVolumeOrigin                     = Shader.PropertyToID("_DDGIVolumeOrigin");
            _DDGIVolumeSpacing                    = Shader.PropertyToID("_DDGIVolumeSpacing");
            _DDGIVolumeProbeCounts                = Shader.PropertyToID("_DDGIVolumeProbeCounts");
            _DDGINormalBias                       = Shader.PropertyToID("_DDGINormalBias");
            _DDGIViewBias                         = Shader.PropertyToID("_DDGIViewBias");
            _DDGIIrradianceGamma                  = Shader.PropertyToID("_DDGIIrradianceGamma");
            _DDGIIrradianceProbeRes               = Shader.PropertyToID("_DDGIIrradianceProbeRes");
            _DDGIDistanceProbeRes                 = Shader.PropertyToID("_DDGIDistanceProbeRes");
            _DDGIProbesPerRow                     = Shader.PropertyToID("_DDGIProbesPerRow");
            _DDGIIrradianceTexelSize              = Shader.PropertyToID("_DDGIIrradianceTexelSize");
            _DDGIDistanceTexelSize                = Shader.PropertyToID("_DDGIDistanceTexelSize");
        }
    }
}
