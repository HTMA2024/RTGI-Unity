using System;
using NUnit.Framework;
using UnityEngine;

namespace URPSSGI.Tests
{

    [TestFixture]
    public sealed class RTAOComplementaryRayTests
    {

        private struct PixelInput
        {
            public int mixedMode;
            public float ssgiHitMask;
            public int enableRTAO;
            public bool isBackground;
            public float aoRadius;
            public int sampleCount;
            public float[] hitDistances;
        }

        private struct PixelOutput
        {
            public float rtaoValue;
            public bool rtaoWritten;
            public bool rtgiSkipped;
            public bool earlyReturn;
        }

        private static PixelOutput SimulateRayGen_Buggy(PixelInput input)
        {
            PixelOutput output;
            output.rtaoValue = 0f;
            output.rtaoWritten = false;
            output.rtgiSkipped = false;
            output.earlyReturn = false;

            if (input.isBackground)
            {
                if (input.enableRTAO == 1)
                {
                    output.rtaoValue = 1f;
                    output.rtaoWritten = true;
                }
                output.earlyReturn = true;
                output.rtgiSkipped = true;
                return output;
            }

            if (input.mixedMode == 1)
            {
                if (input.ssgiHitMask > 0.5f)
                {

                    if (input.enableRTAO == 1)
                    {
                        output.rtaoValue = 1f;
                        output.rtaoWritten = true;
                    }
                    output.earlyReturn = true;
                    output.rtgiSkipped = true;
                    return output;
                }
            }

            float aoAccum = 0f;
            float rcpAORadius = 1f / input.aoRadius;

            for (int i = 0; i < input.sampleCount; ++i)
            {
                float hitT = (input.hitDistances != null && i < input.hitDistances.Length)
                    ? input.hitDistances[i]
                    : input.aoRadius;

                aoAccum += Mathf.Clamp01(hitT * rcpAORadius);
            }

            float rcpSamples = 1f / input.sampleCount;

            if (input.enableRTAO == 1)
            {
                output.rtaoValue = aoAccum * rcpSamples;
                output.rtaoWritten = true;
            }

            output.rtgiSkipped = false;
            output.earlyReturn = false;
            return output;
        }

        private static PixelOutput SimulateRayGen_Fixed(PixelInput input)
        {
            PixelOutput output;
            output.rtaoValue = 0f;
            output.rtaoWritten = false;
            output.rtgiSkipped = false;
            output.earlyReturn = false;

            if (input.isBackground)
            {
                if (input.enableRTAO == 1)
                {
                    output.rtaoValue = 1f;
                    output.rtaoWritten = true;
                }
                output.earlyReturn = true;
                output.rtgiSkipped = true;
                return output;
            }

            bool ssgiHit = input.mixedMode == 1 && input.ssgiHitMask > 0.5f;

            float aoAccum = 0f;
            float rcpAORadius = 1f / input.aoRadius;

            for (int i = 0; i < input.sampleCount; ++i)
            {
                float hitT = (input.hitDistances != null && i < input.hitDistances.Length)
                    ? input.hitDistances[i]
                    : input.aoRadius;

                aoAccum += Mathf.Clamp01(hitT * rcpAORadius);

            }

            output.rtgiSkipped = ssgiHit;
            output.earlyReturn = false;

            float rcpSamples = 1f / input.sampleCount;

            if (input.enableRTAO == 1)
            {
                output.rtaoValue = aoAccum * rcpSamples;
                output.rtaoWritten = true;
            }

            return output;
        }

        private static bool IsBugCondition(PixelInput input)
        {
            return input.mixedMode == 1
                && input.ssgiHitMask > 0.5f
                && input.enableRTAO == 1
                && !input.isBackground;
        }

        [Test]
        public void Property1_BugCondition_SSGIHitPixel_RTAOShouldNotBeFixed1()
        {
            var rng = new System.Random(42);
            int iterations = 100;

            for (int iter = 0; iter < iterations; ++iter)
            {

                float aoRadius = 1f + (float)(rng.NextDouble() * 9f);
                int sampleCount = rng.Next(1, 9);

                float[] hitDistances = new float[sampleCount];
                for (int i = 0; i < sampleCount; ++i)
                {

                    hitDistances[i] = (float)(rng.NextDouble() * aoRadius * 0.8);
                }

                PixelInput input;
                input.mixedMode = 1;
                input.ssgiHitMask = 0.5f + (float)(rng.NextDouble() * 0.5);
                input.enableRTAO = 1;
                input.isBackground = false;
                input.aoRadius = aoRadius;
                input.sampleCount = sampleCount;
                input.hitDistances = hitDistances;

                Assert.IsTrue(IsBugCondition(input),
                    $"迭代 {iter}: 输入应满足 bug 条件");

                PixelOutput output = SimulateRayGen_Fixed(input);

                Assert.IsTrue(output.rtaoWritten,
                    $"迭代 {iter}: bug 条件下 RTAO 应被写入");
                Assert.Less(output.rtaoValue, 1f,
                    $"迭代 {iter}: 存在近距离遮挡物时 RTAO 应 < 1.0，" +
                    $"但实际输出 {output.rtaoValue}（mask={input.ssgiHitMask:F2}, " +
                    $"aoRadius={aoRadius:F2}, sampleCount={sampleCount}）");

                Assert.IsFalse(output.earlyReturn,
                    $"迭代 {iter}: 修复后 SSGI 命中像素不应提前退出");

                float expected = ComputeExpectedRTAO(
                    input.hitDistances, input.sampleCount, input.aoRadius);
                Assert.AreEqual(expected, output.rtaoValue, 1e-5f,
                    $"迭代 {iter}: RTAO 应为 {expected}，实际 {output.rtaoValue}");

                Assert.IsTrue(output.rtgiSkipped,
                    $"迭代 {iter}: 修复后 SSGI 命中像素仍应跳过 RTGI 射线");
            }
        }

        [Test]
        public void BugConfirmation_SSGIHit_RTAOIsFixed1()
        {
            PixelInput input;
            input.mixedMode = 1;
            input.ssgiHitMask = 1f;
            input.enableRTAO = 1;
            input.isBackground = false;
            input.aoRadius = 2f;
            input.sampleCount = 4;

            input.hitDistances = new float[] { 0.5f, 0.3f, 0.8f, 0.1f };

            PixelOutput output = SimulateRayGen_Buggy(input);

            Assert.IsTrue(output.rtaoWritten, "RTAO 应被写入");
            Assert.AreEqual(1f, output.rtaoValue, 1e-5f,
                "Bug 确认：SSGI 命中像素的 RTAO 应固定为 1.0（提前退出跳过了采样循环）");
            Assert.IsTrue(output.earlyReturn,
                "Bug 确认：SSGI 命中像素应触发提前退出");
        }

        [Test]
        public void Contrast_SSGIMiss_RTAOIsCorrectAttenuation()
        {
            PixelInput input;
            input.mixedMode = 1;
            input.ssgiHitMask = 0f;
            input.enableRTAO = 1;
            input.isBackground = false;
            input.aoRadius = 2f;
            input.sampleCount = 4;

            input.hitDistances = new float[] { 0.5f, 0.3f, 0.8f, 0.1f };

            PixelOutput output = SimulateRayGen_Buggy(input);

            Assert.IsTrue(output.rtaoWritten, "RTAO 应被写入");
            Assert.IsFalse(output.earlyReturn, "SSGI 未命中不应触发提前退出");

            float expected = (0.25f + 0.15f + 0.4f + 0.05f) * 0.25f;
            Assert.AreEqual(expected, output.rtaoValue, 1e-5f,
                $"SSGI 未命中时 RTAO 应为正确衰减值 {expected}，实际 {output.rtaoValue}");
            Assert.Less(output.rtaoValue, 1f,
                "存在近距离遮挡物时 RTAO 应 < 1.0");
        }

        private static float ComputeExpectedRTAO(float[] hitDistances, int sampleCount, float aoRadius)
        {
            float aoAccum = 0f;
            float rcpAORadius = 1f / aoRadius;
            for (int i = 0; i < sampleCount; ++i)
            {
                float hitT = (hitDistances != null && i < hitDistances.Length)
                    ? hitDistances[i]
                    : aoRadius;
                aoAccum += Mathf.Clamp01(hitT * rcpAORadius);
            }
            return aoAccum / sampleCount;
        }

        private static PixelInput GeneratePreservationInput(System.Random rng, int conditionType)
        {
            PixelInput input;
            float aoRadius = 1f + (float)(rng.NextDouble() * 9f);
            int sampleCount = rng.Next(1, 9);

            float[] hitDistances = new float[sampleCount];
            for (int i = 0; i < sampleCount; ++i)
            {
                hitDistances[i] = (float)(rng.NextDouble() * aoRadius * 2f);
            }

            input.aoRadius = aoRadius;
            input.sampleCount = sampleCount;
            input.hitDistances = hitDistances;

            switch (conditionType)
            {
                case 0:
                    input.mixedMode = 0;
                    input.ssgiHitMask = (float)rng.NextDouble();
                    input.enableRTAO = 1;
                    input.isBackground = false;
                    break;

                case 1:
                    input.mixedMode = 1;
                    input.ssgiHitMask = (float)(rng.NextDouble() * 0.5f);
                    input.enableRTAO = 1;
                    input.isBackground = false;
                    break;

                case 2:
                    input.mixedMode = rng.Next(0, 2);
                    input.ssgiHitMask = (float)rng.NextDouble();
                    input.enableRTAO = 0;
                    input.isBackground = false;
                    break;

                case 3:
                    input.mixedMode = rng.Next(0, 2);
                    input.ssgiHitMask = (float)rng.NextDouble();
                    input.enableRTAO = rng.Next(0, 2);
                    input.isBackground = true;
                    break;

                default:
                    goto case 0;
            }

            return input;
        }

        [Test]
        public void Property2_Preservation_NonMixedMode_RTAOMatchesExpected()
        {
            var rng = new System.Random(100);
            int iterations = 100;

            for (int iter = 0; iter < iterations; ++iter)
            {
                PixelInput input = GeneratePreservationInput(rng, 0);

                Assert.IsFalse(IsBugCondition(input),
                    $"迭代 {iter}: 非 Mixed 模式输入不应满足 bug 条件");

                PixelOutput output = SimulateRayGen_Buggy(input);

                Assert.IsTrue(output.rtaoWritten,
                    $"迭代 {iter}: 非 Mixed 模式下 RTAO 应被写入");
                Assert.IsFalse(output.earlyReturn,
                    $"迭代 {iter}: 非 Mixed 模式下不应提前退出");
                Assert.IsFalse(output.rtgiSkipped,
                    $"迭代 {iter}: 非 Mixed 模式下不应跳过 RTGI 射线");

                float expected = ComputeExpectedRTAO(
                    input.hitDistances, input.sampleCount, input.aoRadius);
                Assert.AreEqual(expected, output.rtaoValue, 1e-5f,
                    $"迭代 {iter}: 非 Mixed 模式 RTAO 应为 {expected}，实际 {output.rtaoValue}" +
                    $"（aoRadius={input.aoRadius:F2}, sampleCount={input.sampleCount}）");

                Assert.GreaterOrEqual(output.rtaoValue, 0f,
                    $"迭代 {iter}: RTAO 值不应小于 0");
                Assert.LessOrEqual(output.rtaoValue, 1f,
                    $"迭代 {iter}: RTAO 值不应大于 1");

                PixelOutput fixedOutput = SimulateRayGen_Fixed(input);

                Assert.AreEqual(output.rtaoWritten, fixedOutput.rtaoWritten,
                    $"迭代 {iter}: Fixed 版本 rtaoWritten 应与 Buggy 一致");
                Assert.AreEqual(output.rtaoValue, fixedOutput.rtaoValue, 1e-5f,
                    $"迭代 {iter}: Fixed 版本 RTAO 应与 Buggy 一致，" +
                    $"Buggy={output.rtaoValue}, Fixed={fixedOutput.rtaoValue}");
                Assert.AreEqual(output.rtgiSkipped, fixedOutput.rtgiSkipped,
                    $"迭代 {iter}: Fixed 版本 rtgiSkipped 应与 Buggy 一致");
            }
        }

        [Test]
        public void Property2_Preservation_MixedMode_SSGIMiss_RTAOMatchesExpected()
        {
            var rng = new System.Random(200);
            int iterations = 100;

            for (int iter = 0; iter < iterations; ++iter)
            {
                PixelInput input = GeneratePreservationInput(rng, 1);

                Assert.IsFalse(IsBugCondition(input),
                    $"迭代 {iter}: SSGI 未命中输入不应满足 bug 条件");

                PixelOutput output = SimulateRayGen_Buggy(input);

                Assert.IsTrue(output.rtaoWritten,
                    $"迭代 {iter}: SSGI 未命中时 RTAO 应被写入");
                Assert.IsFalse(output.earlyReturn,
                    $"迭代 {iter}: SSGI 未命中时不应提前退出");
                Assert.IsFalse(output.rtgiSkipped,
                    $"迭代 {iter}: SSGI 未命中时不应跳过 RTGI 射线");

                float expected = ComputeExpectedRTAO(
                    input.hitDistances, input.sampleCount, input.aoRadius);
                Assert.AreEqual(expected, output.rtaoValue, 1e-5f,
                    $"迭代 {iter}: SSGI 未命中 RTAO 应为 {expected}，实际 {output.rtaoValue}");

                PixelOutput fixedOutput = SimulateRayGen_Fixed(input);

                Assert.AreEqual(output.rtaoWritten, fixedOutput.rtaoWritten,
                    $"迭代 {iter}: Fixed 版本 rtaoWritten 应与 Buggy 一致");
                Assert.AreEqual(output.rtaoValue, fixedOutput.rtaoValue, 1e-5f,
                    $"迭代 {iter}: Fixed 版本 RTAO 应与 Buggy 一致，" +
                    $"Buggy={output.rtaoValue}, Fixed={fixedOutput.rtaoValue}");
                Assert.AreEqual(output.rtgiSkipped, fixedOutput.rtgiSkipped,
                    $"迭代 {iter}: Fixed 版本 rtgiSkipped 应与 Buggy 一致");
            }
        }

        [Test]
        public void Property2_Preservation_Background_RTAOIs1AndEarlyReturn()
        {
            var rng = new System.Random(300);
            int iterations = 100;

            for (int iter = 0; iter < iterations; ++iter)
            {
                PixelInput input = GeneratePreservationInput(rng, 3);

                Assert.IsFalse(IsBugCondition(input),
                    $"迭代 {iter}: 背景像素不应满足 bug 条件");

                PixelOutput output = SimulateRayGen_Buggy(input);

                Assert.IsTrue(output.earlyReturn,
                    $"迭代 {iter}: 背景像素应提前退出");
                Assert.IsTrue(output.rtgiSkipped,
                    $"迭代 {iter}: 背景像素应跳过 RTGI 射线");

                if (input.enableRTAO == 1)
                {

                    Assert.IsTrue(output.rtaoWritten,
                        $"迭代 {iter}: 背景像素 + RTAO 启用时应写入 RTAO");
                    Assert.AreEqual(1f, output.rtaoValue, 1e-5f,
                        $"迭代 {iter}: 背景像素 RTAO 应为 1.0（无遮蔽），实际 {output.rtaoValue}");
                }
                else
                {

                    Assert.IsFalse(output.rtaoWritten,
                        $"迭代 {iter}: 背景像素 + RTAO 禁用时不应写入 RTAO");
                }

                PixelOutput fixedOutput = SimulateRayGen_Fixed(input);

                Assert.AreEqual(output.rtaoWritten, fixedOutput.rtaoWritten,
                    $"迭代 {iter}: Fixed 版本 rtaoWritten 应与 Buggy 一致");
                Assert.AreEqual(output.rtaoValue, fixedOutput.rtaoValue, 1e-5f,
                    $"迭代 {iter}: Fixed 版本 RTAO 应与 Buggy 一致，" +
                    $"Buggy={output.rtaoValue}, Fixed={fixedOutput.rtaoValue}");
                Assert.AreEqual(output.earlyReturn, fixedOutput.earlyReturn,
                    $"迭代 {iter}: Fixed 版本 earlyReturn 应与 Buggy 一致");
                Assert.AreEqual(output.rtgiSkipped, fixedOutput.rtgiSkipped,
                    $"迭代 {iter}: Fixed 版本 rtgiSkipped 应与 Buggy 一致");
            }
        }

        [Test]
        public void Property2_Preservation_RTAODisabled_NoRTAOWrite()
        {
            var rng = new System.Random(400);
            int iterations = 100;

            for (int iter = 0; iter < iterations; ++iter)
            {
                PixelInput input = GeneratePreservationInput(rng, 2);

                Assert.IsFalse(IsBugCondition(input),
                    $"迭代 {iter}: RTAO 禁用输入不应满足 bug 条件");

                PixelOutput output = SimulateRayGen_Buggy(input);

                Assert.IsFalse(output.rtaoWritten,
                    $"迭代 {iter}: RTAO 禁用时不应写入 _RTAOOutputTexture" +
                    $"（mixedMode={input.mixedMode}, mask={input.ssgiHitMask:F2}）");

                PixelOutput fixedOutput = SimulateRayGen_Fixed(input);

                Assert.IsFalse(fixedOutput.rtaoWritten,
                    $"迭代 {iter}: Fixed 版本 RTAO 禁用时也不应写入 _RTAOOutputTexture" +
                    $"（mixedMode={input.mixedMode}, mask={input.ssgiHitMask:F2}）");
            }
        }

        [Test]
        public void Property2_Preservation_SSGIHit_RTGIStillSkipped()
        {
            var rng = new System.Random(500);
            int iterations = 100;

            for (int iter = 0; iter < iterations; ++iter)
            {

                float aoRadius = 1f + (float)(rng.NextDouble() * 9f);
                int sampleCount = rng.Next(1, 9);
                float[] hitDistances = new float[sampleCount];
                for (int i = 0; i < sampleCount; ++i)
                    hitDistances[i] = (float)(rng.NextDouble() * aoRadius * 2f);

                PixelInput input;
                input.mixedMode = 1;
                input.ssgiHitMask = 0.5f + (float)(rng.NextDouble() * 0.5);
                input.enableRTAO = rng.Next(0, 2);
                input.isBackground = false;
                input.aoRadius = aoRadius;
                input.sampleCount = sampleCount;
                input.hitDistances = hitDistances;

                PixelOutput output = SimulateRayGen_Buggy(input);

                Assert.IsTrue(output.rtgiSkipped,
                    $"迭代 {iter}: SSGI 命中像素应跳过 RTGI 射线（GI 互补关系）" +
                    $"（mask={input.ssgiHitMask:F2}, enableRTAO={input.enableRTAO}）");
                Assert.IsTrue(output.earlyReturn,
                    $"迭代 {iter}: SSGI 命中像素应提前退出");

                PixelOutput fixedOutput = SimulateRayGen_Fixed(input);

                Assert.IsTrue(fixedOutput.rtgiSkipped,
                    $"迭代 {iter}: Fixed 版本 SSGI 命中像素仍应跳过 RTGI 射线" +
                    $"（mask={input.ssgiHitMask:F2}, enableRTAO={input.enableRTAO}）");

                Assert.IsFalse(fixedOutput.earlyReturn,
                    $"迭代 {iter}: Fixed 版本 SSGI 命中像素不应提前退出（修复后进入采样循环）");
            }
        }
    }
}
