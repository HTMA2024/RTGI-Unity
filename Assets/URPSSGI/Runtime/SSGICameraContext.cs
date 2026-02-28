using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace URPSSGI
{

    public sealed class SSGICameraContext : IDisposable
    {

        public RenderTexture SSGIResult;
        public RenderTexture HitPointBuffer;
        public RenderTexture UpsampleResult;
        public RenderTexture HistoryDepth;
        public RenderTexture SpatialDenoiseTemp;
        public RenderTexture TemporalOutputTemp;
        public RenderTexture PreDenoiseGI;
        public RenderTexture DebugOutputBuffer;

        public RenderTexture DepthPyramidAtlas;
        public RenderTexture ColorPyramidAtlas;
        public RenderTexture ColorPyramidAtlasPrev;

        public RenderTargetIdentifier FinalGIResult = Texture2D.blackTexture;

        public bool SSGIExecutedThisFrame;

        public RenderTargetIdentifier DebugHitPointTexture;
        public RenderTargetIdentifier DebugAccumCountTexture;
        public RenderTargetIdentifier DebugPreDenoiseTexture;
        public RenderTargetIdentifier DebugRawGITexture;
        public RenderTargetIdentifier DebugOutputTexture;

        public RenderTargetIdentifier PrevIndirectDiffuseTexture = Texture2D.blackTexture;
        public bool HasDebugBindings;

        public RenderTexture RTGIHistoryHF;
        public RenderTexture RTGIHistoryLF;
        public RenderTexture RTGIHistoryDepth;
        public RenderTexture RTGIHistoryNormal;
        public bool RTGIHistoryValid;

        public RenderTexture RTGIHistoryMotionVector;

        public RenderTexture RTGIOutputTexture;
        public RenderTexture RTGIHitValidityMask;
        public RenderTexture RTGIUpsampleResult;

        public RenderTexture RTAOOutputTexture;

        public RenderTexture RTAOHistoryHF;
        public RenderTexture RTAOHistoryLF;

        public RenderTexture RTAOTemporalTemp;
        public RenderTexture RTAOSpatialTemp;
        public bool RTAOHistoryValid;

        public RenderTargetIdentifier FinalRTAOResult = Texture2D.whiteTexture;

        public RenderTexture SSGIGBuffer0;
        public RenderTexture SSGIGBuffer1;
        public RenderTexture SSGIGBuffer2;
        public RenderTexture SSGIHitPositionNDC;

        public Matrix4x4 RTGIPrevViewMatrix = Matrix4x4.identity;
        public Matrix4x4 RTGIPrevGpuProjMatrix = Matrix4x4.identity;

        public float RTGIPrevExposure = 1.0f;

        public RenderTargetIdentifier DebugRTGITexture;
        public RenderTargetIdentifier DebugRTGIRayLengthTexture;
        public RenderTargetIdentifier DebugMixedMaskTexture;
        public bool HasRTGIDebugBindings;

        public ComputeBuffer DepthMipOffsetBuffer;

        public PackedMipChainInfo DepthMipInfo;
        public PackedMipChainInfo ColorMipInfo;

        public int AllocatedFullWidth;
        public int AllocatedFullHeight;
        public bool AllocatedFullRes;
        public bool AllocatedDenoise;
        public SSGIDebugMode AllocatedDebugMode;

        public int FrameIndex;

        private static readonly Dictionary<int, SSGICameraContext> s_Instances
            = new Dictionary<int, SSGICameraContext>();

        private SSGICameraContext() { }

        public static SSGICameraContext GetOrCreate(Camera camera)
        {
            int id = camera.GetInstanceID();
            if (s_Instances.TryGetValue(id, out SSGICameraContext ctx))
                return ctx;

            ctx = new SSGICameraContext();
            s_Instances.Add(id, ctx);
            return ctx;
        }

        public static void ReleaseAll()
        {
            foreach (var kvp in s_Instances)
                kvp.Value.Dispose();
            s_Instances.Clear();
        }

        public void SwapColorPyramid()
        {
            var temp = ColorPyramidAtlas;
            ColorPyramidAtlas = ColorPyramidAtlasPrev;
            ColorPyramidAtlasPrev = temp;
        }

        public void InvalidateRTGIHistory()
        {
            RTGIHistoryValid = false;
            RTAOHistoryValid = false;
        }

        public void AllocateRTGIBuffersIfNeeded(int width, int height)
        {

            if (RTGIHistoryHF != null && RTGIHistoryHF.width == width && RTGIHistoryHF.height == height)
                return;

            ReleaseRTGIBuffers();

            RTGIHistoryHF = CreateRT(width, height,
                GraphicsFormat.R16G16B16A16_SFloat, true, "_RTGIHistoryHF");

            RTGIHistoryLF = CreateRT(width, height,
                GraphicsFormat.R16G16B16A16_SFloat, true, "_RTGIHistoryLF");

            RTGIHistoryDepth = CreateRT(width, height,
                GraphicsFormat.R32_SFloat, true, "_RTGIHistoryDepth");

            RTGIHistoryNormal = CreateRT(width, height,
                GraphicsFormat.R16G16B16A16_SFloat, true, "_RTGIHistoryNormal");

            RTGIHistoryMotionVector = CreateRT(width, height,
                GraphicsFormat.R16G16_SFloat, true, "_RTGIHistoryMotionVector");

            RTGIHistoryValid = false;

            RTAOHistoryHF = CreateRT(width, height,
                GraphicsFormat.R16G16B16A16_SFloat, true, "_RTAOHistoryHF");
            RTAOHistoryLF = CreateRT(width, height,
                GraphicsFormat.R16G16B16A16_SFloat, true, "_RTAOHistoryLF");

            RTAOHistoryValid = false;
        }

        public void ReleaseRenderTargets()
        {
            DestroyRT(ref SSGIResult);
            DestroyRT(ref HitPointBuffer);
            DestroyRT(ref UpsampleResult);
            DestroyRT(ref HistoryDepth);
            DestroyRT(ref SpatialDenoiseTemp);
            DestroyRT(ref TemporalOutputTemp);
            DestroyRT(ref PreDenoiseGI);
            DestroyRT(ref DebugOutputBuffer);
            DestroyRT(ref DepthPyramidAtlas);
            DestroyRT(ref ColorPyramidAtlas);
            DestroyRT(ref ColorPyramidAtlasPrev);

            ReleaseRTGIBuffers();
            ReleaseRTGIFrameBuffers();
            ReleaseDeferredLightingBuffers();

            DepthMipOffsetBuffer?.Release();
            DepthMipOffsetBuffer = null;

            AllocatedFullWidth = 0;
            AllocatedFullHeight = 0;
        }

        public void Dispose()
        {
            ReleaseRenderTargets();
        }

        private void ReleaseRTGIBuffers()
        {
            DestroyRT(ref RTGIHistoryHF);
            DestroyRT(ref RTGIHistoryLF);
            DestroyRT(ref RTGIHistoryDepth);
            DestroyRT(ref RTGIHistoryNormal);
            DestroyRT(ref RTGIHistoryMotionVector);
            DestroyRT(ref RTAOHistoryHF);
            DestroyRT(ref RTAOHistoryLF);

            RTGIHistoryValid = false;
            RTAOHistoryValid = false;
        }

        private void ReleaseRTGIFrameBuffers()
        {
            DestroyRT(ref RTGIOutputTexture);
            DestroyRT(ref RTGIHitValidityMask);
            DestroyRT(ref RTGIUpsampleResult);
            DestroyRT(ref RTAOOutputTexture);
            DestroyRT(ref RTAOTemporalTemp);
            DestroyRT(ref RTAOSpatialTemp);
        }

        private void ReleaseDeferredLightingBuffers()
        {
            DestroyRT(ref SSGIGBuffer0);
            DestroyRT(ref SSGIGBuffer1);
            DestroyRT(ref SSGIGBuffer2);
            DestroyRT(ref SSGIHitPositionNDC);
        }

        public void AllocateDeferredLightingBuffersIfNeeded(int width, int height)
        {
            if (SSGIGBuffer0 != null && SSGIGBuffer0.width == width && SSGIGBuffer0.height == height)
                return;

            ReleaseDeferredLightingBuffers();

            SSGIGBuffer0 = CreateRT(width, height,
                GraphicsFormat.R8G8B8A8_UNorm, true, "_SSGIGBuffer0");
            SSGIGBuffer1 = CreateRT(width, height,
                GraphicsFormat.R8G8B8A8_UNorm, true, "_SSGIGBuffer1");
            SSGIGBuffer2 = CreateRT(width, height,
                GraphicsFormat.R8G8B8A8_UNorm, true, "_SSGIGBuffer2");
            SSGIHitPositionNDC = CreateRT(width, height,
                GraphicsFormat.R16G16B16A16_SFloat, true, "_SSGIHitPositionNDC");
        }

        private static RenderTexture CreateRT(int width, int height, GraphicsFormat format, bool enableRandomWrite, string name)
        {
            var rt = new RenderTexture(width, height, 0, format);
            rt.enableRandomWrite = enableRandomWrite;
            rt.name = name;
            rt.Create();
            return rt;
        }

        private static void DestroyRT(ref RenderTexture rt)
        {
            if (rt != null)
            {
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
                rt = null;
            }
        }
    }
}
