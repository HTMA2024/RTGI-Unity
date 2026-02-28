using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace URPSSGI.Tests
{

    [TestFixture]
    public sealed class SSGICameraContextTests
    {
        private GameObject m_CamObjA;
        private GameObject m_CamObjB;
        private Camera m_CameraA;
        private Camera m_CameraB;

        [SetUp]
        public void SetUp()
        {

            SSGICameraContext.ReleaseAll();

            m_CamObjA = new GameObject("TestCameraA");
            m_CameraA = m_CamObjA.AddComponent<Camera>();

            m_CamObjB = new GameObject("TestCameraB");
            m_CameraB = m_CamObjB.AddComponent<Camera>();
        }

        [TearDown]
        public void TearDown()
        {
            SSGICameraContext.ReleaseAll();

            if (m_CamObjA != null) Object.DestroyImmediate(m_CamObjA);
            if (m_CamObjB != null) Object.DestroyImmediate(m_CamObjB);
        }

        [Test]
        public void GetOrCreate_SameCamera_ReturnsSameInstance()
        {
            SSGICameraContext ctxA1 = SSGICameraContext.GetOrCreate(m_CameraA);
            SSGICameraContext ctxA2 = SSGICameraContext.GetOrCreate(m_CameraA);
            Assert.AreSame(ctxA1, ctxA2, "同一相机应返回同一上下文实例");
        }

        [Test]
        public void GetOrCreate_DifferentCameras_ReturnsDifferentInstances()
        {
            SSGICameraContext ctxA = SSGICameraContext.GetOrCreate(m_CameraA);
            SSGICameraContext ctxB = SSGICameraContext.GetOrCreate(m_CameraB);
            Assert.AreNotSame(ctxA, ctxB, "不同相机应返回不同上下文实例");
        }

        [Test]
        public void FinalGIResult_PerCamera_Independent()
        {
            SSGICameraContext ctxA = SSGICameraContext.GetOrCreate(m_CameraA);
            SSGICameraContext ctxB = SSGICameraContext.GetOrCreate(m_CameraB);

            var rtA = new RenderTexture(64, 64, 0);
            var rtB = new RenderTexture(128, 128, 0);
            rtA.Create();
            rtB.Create();

            ctxA.FinalGIResult = rtA;
            ctxB.FinalGIResult = rtB;

            Assert.AreEqual((RenderTargetIdentifier)rtA, ctxA.FinalGIResult,
                "相机 A 的 FinalGIResult 不应被相机 B 覆盖");
            Assert.AreEqual((RenderTargetIdentifier)rtB, ctxB.FinalGIResult,
                "相机 B 的 FinalGIResult 不应被相机 A 覆盖");

            rtA.Release();
            Object.DestroyImmediate(rtA);
            rtB.Release();
            Object.DestroyImmediate(rtB);
        }

        [Test]
        public void HasDebugBindings_DefaultFalse()
        {
            SSGICameraContext ctx = SSGICameraContext.GetOrCreate(m_CameraA);
            Assert.IsFalse(ctx.HasDebugBindings, "新创建的上下文 HasDebugBindings 应为 false");
        }

        [Test]
        public void HasDebugBindings_PerCamera_Independent()
        {
            SSGICameraContext ctxA = SSGICameraContext.GetOrCreate(m_CameraA);
            SSGICameraContext ctxB = SSGICameraContext.GetOrCreate(m_CameraB);

            ctxA.HasDebugBindings = true;
            ctxB.HasDebugBindings = false;

            Assert.IsTrue(ctxA.HasDebugBindings);
            Assert.IsFalse(ctxB.HasDebugBindings);
        }

        [Test]
        public void ReleaseAll_ClearsAllInstances()
        {
            SSGICameraContext.GetOrCreate(m_CameraA);
            SSGICameraContext.GetOrCreate(m_CameraB);

            SSGICameraContext.ReleaseAll();

            SSGICameraContext ctxA2 = SSGICameraContext.GetOrCreate(m_CameraA);
            Assert.IsNotNull(ctxA2, "ReleaseAll 后应能创建新实例");

            Assert.AreEqual(0, ctxA2.AllocatedFullWidth, "新实例应未分配 RT");
        }

        [Test]
        public void SwapColorPyramid_SwapsReferences()
        {
            SSGICameraContext ctx = SSGICameraContext.GetOrCreate(m_CameraA);

            var rtCurrent = new RenderTexture(64, 64, 0);
            var rtPrev = new RenderTexture(64, 64, 0);
            rtCurrent.name = "Current";
            rtPrev.name = "Prev";
            rtCurrent.Create();
            rtPrev.Create();

            ctx.ColorPyramidAtlas = rtCurrent;
            ctx.ColorPyramidAtlasPrev = rtPrev;

            ctx.SwapColorPyramid();

            Assert.AreSame(rtPrev, ctx.ColorPyramidAtlas, "Swap 后 Atlas 应指向原 Prev");
            Assert.AreSame(rtCurrent, ctx.ColorPyramidAtlasPrev, "Swap 后 Prev 应指向原 Atlas");

            rtCurrent.Release();
            Object.DestroyImmediate(rtCurrent);
            rtPrev.Release();
            Object.DestroyImmediate(rtPrev);
        }

        [Test]
        public void FrameIndex_PerCamera_Independent()
        {
            SSGICameraContext ctxA = SSGICameraContext.GetOrCreate(m_CameraA);
            SSGICameraContext ctxB = SSGICameraContext.GetOrCreate(m_CameraB);

            ctxA.FrameIndex = 10;
            ctxB.FrameIndex = 20;

            Assert.AreEqual(10, ctxA.FrameIndex);
            Assert.AreEqual(20, ctxB.FrameIndex);
        }

        [Test]
        public void SSGIExecutedThisFrame_DefaultFalse()
        {
            SSGICameraContext ctx = SSGICameraContext.GetOrCreate(m_CameraA);
            Assert.IsFalse(ctx.SSGIExecutedThisFrame, "新创建的上下文 SSGIExecutedThisFrame 应为 false");
        }

        [Test]
        public void SSGIExecutedThisFrame_PerCamera_Independent()
        {
            SSGICameraContext ctxA = SSGICameraContext.GetOrCreate(m_CameraA);
            SSGICameraContext ctxB = SSGICameraContext.GetOrCreate(m_CameraB);

            ctxA.SSGIExecutedThisFrame = true;
            ctxB.SSGIExecutedThisFrame = false;

            Assert.IsTrue(ctxA.SSGIExecutedThisFrame, "相机 A 标记不应被相机 B 覆盖");
            Assert.IsFalse(ctxB.SSGIExecutedThisFrame, "相机 B 标记不应被相机 A 覆盖");
        }

        [Test]
        public void FinalGIResult_DefaultIsBlackTexture()
        {
            SSGICameraContext ctx = SSGICameraContext.GetOrCreate(m_CameraA);

            Assert.AreEqual((RenderTargetIdentifier)Texture2D.blackTexture, ctx.FinalGIResult,
                "FinalGIResult 初始值应为黑色纹理");
        }

        [Test]
        public void PrevIndirectDiffuseTexture_DefaultIsBlackTexture()
        {
            SSGICameraContext ctx = SSGICameraContext.GetOrCreate(m_CameraA);
            Assert.AreEqual((RenderTargetIdentifier)Texture2D.blackTexture, ctx.PrevIndirectDiffuseTexture,
                "PrevIndirectDiffuseTexture 初始值应为黑色纹理");
        }
    }
}
