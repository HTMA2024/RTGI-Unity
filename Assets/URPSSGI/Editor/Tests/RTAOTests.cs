using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace URPSSGI.Tests
{

    [TestFixture]
    public sealed class RTAOTests
    {
        private GameObject m_TestCamObj;
        private Camera m_TestCamera;

        [SetUp]
        public void SetUp()
        {
            SSGICameraContext.ReleaseAll();
            m_TestCamObj = new GameObject("RTAOTestCamera");
            m_TestCamera = m_TestCamObj.AddComponent<Camera>();
        }

        [TearDown]
        public void TearDown()
        {
            SSGICameraContext.ReleaseAll();
            if (m_TestCamObj != null)
                UnityEngine.Object.DestroyImmediate(m_TestCamObj);
        }

        [Test]
        public void SSGIDebugMode_RTAO_ValueIs17()
        {
            Assert.AreEqual(17, (int)SSGIDebugMode.RTAO,
                "RTAO 调试模式枚举值应为 17");
        }

        [Test]
        public void SSGIDebugMode_RTGIWithAO_ValueIs18()
        {
            Assert.AreEqual(18, (int)SSGIDebugMode.RTGIWithAO,
                "RTGIWithAO 调试模式枚举值应为 18");
        }

        [Test]
        public void SSGIDebugMode_RTAO_NoConflictWithExisting()
        {
            var allValues = Enum.GetValues(typeof(SSGIDebugMode));
            var intValues = new int[allValues.Length];
            for (int i = 0; i < allValues.Length; i++)
                intValues[i] = (int)allValues.GetValue(i);

            int distinctCount = intValues.Distinct().Count();
            Assert.AreEqual(allValues.Length, distinctCount,
                "SSGIDebugMode 枚举值不应有重复（含 RTAO=17）");
        }

        [Test]
        public void VolumeComponent_EnableRTAO_DefaultFalse()
        {
            var vol = ScriptableObject.CreateInstance<SSGIVolumeComponent>();
            Assert.IsFalse(vol.enableRTAO.value, "enableRTAO 默认应为 false");
            ScriptableObject.DestroyImmediate(vol);
        }

        [Test]
        public void VolumeComponent_RTAORadius_Default2()
        {
            var vol = ScriptableObject.CreateInstance<SSGIVolumeComponent>();
            Assert.AreEqual(2.0f, vol.rtaoRadius.value, 1e-5f,
                "rtaoRadius 默认应为 2.0");
            ScriptableObject.DestroyImmediate(vol);
        }

        [Test]
        public void VolumeComponent_RTAOIntensity_Default1()
        {
            var vol = ScriptableObject.CreateInstance<SSGIVolumeComponent>();
            Assert.AreEqual(1.0f, vol.rtaoIntensity.value, 1e-5f,
                "rtaoIntensity 默认应为 1.0");
            ScriptableObject.DestroyImmediate(vol);
        }

        [Test]
        public void ShaderIDs_RTAOProperties_NonZero()
        {
            Assert.AreNotEqual(0, SSGIShaderIDs._RTAOOutputTexture,
                "_RTAOOutputTexture 属性 ID 应已注册");
            Assert.AreNotEqual(0, SSGIShaderIDs._RTAORadius,
                "_RTAORadius 属性 ID 应已注册");
            Assert.AreNotEqual(0, SSGIShaderIDs._RTAOIntensity,
                "_RTAOIntensity 属性 ID 应已注册");
            Assert.AreNotEqual(0, SSGIShaderIDs._EnableRTAO,
                "_EnableRTAO 属性 ID 应已注册");
        }

        [Test]
        public void ShaderIDs_RTAOProperties_AllUnique()
        {
            int[] ids = new int[]
            {
                SSGIShaderIDs._RTAOOutputTexture,
                SSGIShaderIDs._RTAORadius,
                SSGIShaderIDs._RTAOIntensity,
                SSGIShaderIDs._EnableRTAO
            };
            int distinctCount = ids.Distinct().Count();
            Assert.AreEqual(ids.Length, distinctCount,
                "所有 RTAO Shader Property ID 应互不相同");
        }

        [Test]
        public void ShaderIDs_RTAOProperties_NoConflictWithRTGI()
        {
            int[] rtaoIds = new int[]
            {
                SSGIShaderIDs._RTAOOutputTexture,
                SSGIShaderIDs._RTAORadius,
                SSGIShaderIDs._RTAOIntensity,
                SSGIShaderIDs._EnableRTAO
            };
            int[] rtgiIds = new int[]
            {
                SSGIShaderIDs._RTGIOutputTexture,
                SSGIShaderIDs._RTGIMixedMode,
                SSGIShaderIDs._RTGIDebugNormalMode,
                SSGIShaderIDs._RTGIRayTracingScale,
                SSGIShaderIDs._RTGIFullScreenSize
            };

            for (int i = 0; i < rtaoIds.Length; i++)
            {
                for (int j = 0; j < rtgiIds.Length; j++)
                {
                    Assert.AreNotEqual(rtaoIds[i], rtgiIds[j],
                        $"RTAO ID[{i}] 不应与 RTGI ID[{j}] 冲突");
                }
            }
        }

        [Test]
        public void SSGICameraContext_RTAOOutputTexture_InitiallyNull()
        {
            SSGICameraContext ctx = SSGICameraContext.GetOrCreate(m_TestCamera);
            Assert.IsNull(ctx.RTAOOutputTexture,
                "新创建的上下文 RTAOOutputTexture 应为 null");
        }

        [Test]
        public void SSGICameraContext_RTAOOutputTexture_ReleasedOnReleaseRenderTargets()
        {
            SSGICameraContext ctx = SSGICameraContext.GetOrCreate(m_TestCamera);
            ctx.RTAOOutputTexture = new RenderTexture(64, 64, 0, GraphicsFormat.R8_UNorm);
            ctx.RTAOOutputTexture.enableRandomWrite = true;
            ctx.RTAOOutputTexture.Create();

            ctx.ReleaseRenderTargets();

            Assert.IsNull(ctx.RTAOOutputTexture,
                "ReleaseRenderTargets 后 RTAOOutputTexture 应为 null");
        }

        [Test]
        public void SSGICameraContext_RTAOOutputTexture_SurvivesAllocateRTGIBuffers()
        {
            SSGICameraContext ctx = SSGICameraContext.GetOrCreate(m_TestCamera);
            ctx.RTAOOutputTexture = new RenderTexture(64, 64, 0, GraphicsFormat.R8_UNorm);
            ctx.RTAOOutputTexture.enableRandomWrite = true;
            ctx.RTAOOutputTexture.Create();

            ctx.AllocateRTGIBuffersIfNeeded(64, 64);

            Assert.IsNotNull(ctx.RTAOOutputTexture,
                "AllocateRTGIBuffersIfNeeded 不应释放 RTAOOutputTexture");
            Assert.IsTrue(ctx.RTAOOutputTexture.IsCreated(),
                "RTAOOutputTexture 应仍然有效");
        }

        [Test]
        public void SSGICameraContext_RTAOHistoryBuffers_AllocatedWithRTGI()
        {
            SSGICameraContext ctx = SSGICameraContext.GetOrCreate(m_TestCamera);
            ctx.AllocateRTGIBuffersIfNeeded(64, 64);

            Assert.IsNotNull(ctx.RTAOHistoryHF,
                "AllocateRTGIBuffersIfNeeded 应分配 RTAOHistoryHF");
            Assert.IsNotNull(ctx.RTAOHistoryLF,
                "AllocateRTGIBuffersIfNeeded 应分配 RTAOHistoryLF");
            Assert.AreEqual(64, ctx.RTAOHistoryHF.width);
            Assert.AreEqual(64, ctx.RTAOHistoryHF.height);
        }

        [Test]
        public void SSGICameraContext_RTAOHistoryValid_InitiallyFalse()
        {
            SSGICameraContext ctx = SSGICameraContext.GetOrCreate(m_TestCamera);
            Assert.IsFalse(ctx.RTAOHistoryValid,
                "新创建的上下文 RTAOHistoryValid 应为 false");
        }

        [Test]
        public void SSGICameraContext_RTAOHistoryValid_ResetOnInvalidateRTGI()
        {
            SSGICameraContext ctx = SSGICameraContext.GetOrCreate(m_TestCamera);
            ctx.RTAOHistoryValid = true;
            ctx.InvalidateRTGIHistory();

            Assert.IsFalse(ctx.RTAOHistoryValid,
                "InvalidateRTGIHistory 应同时重置 RTAOHistoryValid");
            Assert.IsFalse(ctx.RTGIHistoryValid,
                "InvalidateRTGIHistory 应重置 RTGIHistoryValid");
        }

        [Test]
        public void SSGICameraContext_RTAOHistoryBuffers_ReleasedOnReleaseRenderTargets()
        {
            SSGICameraContext ctx = SSGICameraContext.GetOrCreate(m_TestCamera);
            ctx.AllocateRTGIBuffersIfNeeded(64, 64);

            Assert.IsNotNull(ctx.RTAOHistoryHF);
            Assert.IsNotNull(ctx.RTAOHistoryLF);

            ctx.ReleaseRenderTargets();

            Assert.IsNull(ctx.RTAOHistoryHF,
                "ReleaseRenderTargets 后 RTAOHistoryHF 应为 null");
            Assert.IsNull(ctx.RTAOHistoryLF,
                "ReleaseRenderTargets 后 RTAOHistoryLF 应为 null");
            Assert.IsFalse(ctx.RTAOHistoryValid,
                "ReleaseRenderTargets 后 RTAOHistoryValid 应为 false");
        }

        [Test]
        public void SSGICameraContext_RTAOHistoryBuffers_ReallocatedOnResolutionChange()
        {
            SSGICameraContext ctx = SSGICameraContext.GetOrCreate(m_TestCamera);
            ctx.AllocateRTGIBuffersIfNeeded(64, 64);

            RenderTexture oldHF = ctx.RTAOHistoryHF;
            Assert.AreEqual(64, oldHF.width);

            ctx.AllocateRTGIBuffersIfNeeded(128, 128);

            Assert.IsNotNull(ctx.RTAOHistoryHF);
            Assert.AreEqual(128, ctx.RTAOHistoryHF.width);
            Assert.IsFalse(ctx.RTAOHistoryValid,
                "分辨率变化后 RTAOHistoryValid 应重置为 false");
        }

        [Test]
        public void SSGICameraContext_RTAODenoiseTemp_InitiallyNull()
        {
            SSGICameraContext ctx = SSGICameraContext.GetOrCreate(m_TestCamera);
            Assert.IsNull(ctx.RTAOTemporalTemp,
                "新创建的上下文 RTAOTemporalTemp 应为 null");
            Assert.IsNull(ctx.RTAOSpatialTemp,
                "新创建的上下文 RTAOSpatialTemp 应为 null");
        }

        [Test]
        public void SSGICameraContext_RTAODenoiseTemp_ReleasedOnReleaseRenderTargets()
        {
            SSGICameraContext ctx = SSGICameraContext.GetOrCreate(m_TestCamera);

            ctx.RTAOTemporalTemp = new RenderTexture(64, 64, 0, GraphicsFormat.R16G16B16A16_SFloat);
            ctx.RTAOTemporalTemp.enableRandomWrite = true;
            ctx.RTAOTemporalTemp.Create();
            ctx.RTAOSpatialTemp = new RenderTexture(64, 64, 0, GraphicsFormat.B10G11R11_UFloatPack32);
            ctx.RTAOSpatialTemp.enableRandomWrite = true;
            ctx.RTAOSpatialTemp.Create();

            ctx.ReleaseRenderTargets();

            Assert.IsNull(ctx.RTAOTemporalTemp,
                "ReleaseRenderTargets 后 RTAOTemporalTemp 应为 null");
            Assert.IsNull(ctx.RTAOSpatialTemp,
                "ReleaseRenderTargets 后 RTAOSpatialTemp 应为 null");
        }

        [Test]
        public void SSGICameraContext_FinalRTAOResult_DefaultIsWhiteTexture()
        {
            SSGICameraContext ctx = SSGICameraContext.GetOrCreate(m_TestCamera);

            RenderTargetIdentifier whiteId = Texture2D.whiteTexture;
            Assert.AreEqual(whiteId, ctx.FinalRTAOResult,
                "FinalRTAOResult 默认应为白色纹理（无遮蔽）");
        }
    }
}
