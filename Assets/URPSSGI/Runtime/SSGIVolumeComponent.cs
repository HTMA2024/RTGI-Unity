using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace URPSSGI
{

    [Serializable, VolumeComponentMenu("SSGI/Screen Space Global Illumination")]
    public sealed class SSGIVolumeComponent : VolumeComponent, IPostProcessComponent
    {

        public BoolParameter enable = new BoolParameter(false);
        public BoolParameter fullResolution = new BoolParameter(false);
        public BoolParameter useAccurateNormals = new BoolParameter(false);
        public IndirectDiffuseModeParameter giMode =
            new IndirectDiffuseModeParameter(IndirectDiffuseMode.ScreenSpace);

        public ClampedIntParameter maxRaySteps = new ClampedIntParameter(32, 1, 128);
        public ClampedFloatParameter depthBufferThickness = new ClampedFloatParameter(0.1f, 0.001f, 1.0f);
        public RayMarchingFallbackHierarchyParameter rayMissFallback =
            new RayMarchingFallbackHierarchyParameter(RayMarchingFallbackHierarchy.ReflectionProbes);

        public BoolParameter multiBounce = new BoolParameter(false);

        public BoolParameter deferredLighting = new BoolParameter(false);

        public MinFloatParameter rtRayLength = new MinFloatParameter(50.0f, 0.01f);
        public ClampedIntParameter rtSampleCount = new ClampedIntParameter(2, 1, 32);
        public ClampedIntParameter rtBounceCount = new ClampedIntParameter(1, 1, 8);
        public MinFloatParameter rtClampValue = new MinFloatParameter(100.0f, 0.001f);
        public ClampedIntParameter rtTextureLodBias = new ClampedIntParameter(7, 0, 7);
        public RayMarchingFallbackHierarchyParameter rtLastBounceFallbackHierarchy =
            new RayMarchingFallbackHierarchyParameter(RayMarchingFallbackHierarchy.ReflectionProbesAndSky);
        public ClampedFloatParameter rtAmbientProbeDimmer = new ClampedFloatParameter(1.0f, 0.0f, 1.0f);

        public ClampedFloatParameter rtRayBias = new ClampedFloatParameter(0.01f, 0.001f, 0.5f);
        public ClampedFloatParameter rtDistantRayBias = new ClampedFloatParameter(0.1f, 0.001f, 1.0f);

        public BoolParameter rtShadowRay = new BoolParameter(false);

        public BoolParameter enableRTAO = new BoolParameter(false);
        public MinFloatParameter rtaoRadius = new MinFloatParameter(2.0f, 0.01f);
        public ClampedFloatParameter rtaoIntensity = new ClampedFloatParameter(1.0f, 0.0f, 1.0f);

        public BoolParameter rtDenoise = new BoolParameter(true);
        public ClampedFloatParameter rtDenoiserRadius = new ClampedFloatParameter(0.6f, 0.001f, 1.0f);
        public BoolParameter rtHalfResolutionDenoiser = new BoolParameter(false);
        public BoolParameter rtSecondDenoiserPass = new BoolParameter(true);

        public ClampedIntParameter rtMixedRaySteps = new ClampedIntParameter(48, 0, 128);

        public BoolParameter denoise = new BoolParameter(true);
        public ClampedFloatParameter denoiserRadius = new ClampedFloatParameter(0.5f, 0.001f, 1.0f);
        public BoolParameter halfResolutionDenoiser = new BoolParameter(false);
        public BoolParameter secondDenoiserPass = new BoolParameter(true);

        public ClampedFloatParameter compositeIntensity = new ClampedFloatParameter(1.0f, 0.0f, 5.0f);
        public SSGICompositeModeParameter compositeMode =
            new SSGICompositeModeParameter(SSGICompositeMode.Additive);

        public SSGIDebugModeParameter debugMode = new SSGIDebugModeParameter(SSGIDebugMode.None);
        public ClampedIntParameter debugMipLevel = new ClampedIntParameter(0, 0, 10);

        public bool IsActive() => enable.value;
        public bool IsTileCompatible() => false;
    }
}
