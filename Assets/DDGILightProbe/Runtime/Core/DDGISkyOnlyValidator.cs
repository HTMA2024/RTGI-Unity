using System;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace DDGI
{

    [RequireComponent(typeof(DDGIVolume))]
    [RequireComponent(typeof(DDGIProbeUpdater))]
    [ExecuteInEditMode]
    public sealed class DDGISkyOnlyValidator : MonoBehaviour
    {
        #region 常量

        private const float GOLDEN_RATIO = 1.61803398875f;
        private const float TWO_PI = 6.28318530718f;

        #endregion

        #region 配置

        [Header("验证配置")]
        [Tooltip("用于对比的Probe索引（0-based）")]
        [SerializeField] private int m_ProbeIndex;

        [Tooltip("用于GT验证的球面采样方向数")]
        [SerializeField] private int m_SHCompareDirections = 64;

        [Tooltip("Phase 1 天空采样容差")]
        [SerializeField] private float m_Phase1Tolerance = 0.02f;

        [Tooltip("Phase 2 光照缓冲区容差")]
        [SerializeField] private float m_Phase2Tolerance = 0.01f;

        [Tooltip("Phase 3 蒙特卡洛积分容差")]
        [SerializeField] private float m_Phase3Tolerance = 0.02f;

        [Tooltip("最终对比容差（DDGI vs Unity）")]
        [SerializeField] private float m_FinalTolerance = 0.1f;

        #endregion

        #region 缓存引用

        private DDGIVolume m_Volume;
        private DDGIProbeUpdater m_Updater;

        #endregion

        #region GT可视化

        [HideInInspector] public Texture2D gtTexture;

        [HideInInspector] public Texture2D unitySHTexture;

        [HideInInspector] public bool gtVisualizationEnabled;

        [HideInInspector] public int gtDisplayMode;

        [HideInInspector] public float gtProbeRadius = 0.3f;

        [HideInInspector] public float gtIntensity = 1.0f;

        [HideInInspector] public int gtTextureResolution = 64;

        [HideInInspector] public int gtMCSamples = 1024;

        private Material m_GTVisMaterial;
        private Mesh m_GTSphereCache;

        #endregion

        #region 验证结果数据

        [Serializable]
        public sealed class PhaseResult
        {
            public string phaseName;
            public bool passed;
            public float rmse;
            public float maxError;
            public int maxErrorIndex;
            public string details;
        }

        [Serializable]
        public sealed class ValidationReport
        {
            public PhaseResult step0_GTvsUnity;
            public PhaseResult step1_RayGen;
            public PhaseResult step2_Lighting;
            public PhaseResult step3_MonteCarlo;
            public PhaseResult step4_Final;
            public string fullLog;
        }

        [HideInInspector]
        public ValidationReport lastReport;

        #endregion

        #region CPU辅助函数（精确复现GPU算法）

        private static Vector3 FibonacciSphereDirection(int rayIndex, int totalRays)
        {
            float goldenAngle = TWO_PI / (GOLDEN_RATIO * GOLDEN_RATIO);
            float theta = goldenAngle * rayIndex;
            float cosTheta = 1f - (2f * rayIndex + 1f) / (float)totalRays;
            float sinTheta = Mathf.Sqrt(Mathf.Max(0f, 1f - cosTheta * cosTheta));
            return new Vector3(
                Mathf.Cos(theta) * sinTheta,
                Mathf.Sin(theta) * sinTheta,
                cosTheta
            );
        }

        private static float HaltonSequence(uint index, int baseValue)
        {
            float result = 0f;
            float fraction = 1f;
            uint i = index;
            while (i > 0)
            {
                fraction *= 1f / baseValue;
                result += (i % (uint)baseValue) * fraction;
                i /= (uint)baseValue;
            }
            return result;
        }

        private static Matrix4x4 ComputeRayRotationMatrix(uint frameIndex)
        {
            float h2 = HaltonSequence(frameIndex, 2);
            float h3 = HaltonSequence(frameIndex, 3);
            float h5 = HaltonSequence(frameIndex, 5);

            float cosTheta = 1f - 2f * h2;
            float sinTheta = Mathf.Sqrt(Mathf.Max(0f, 1f - cosTheta * cosTheta));
            float axisPhi = h3 * Mathf.PI * 2f;
            Vector3 rotAxis = new Vector3(
                Mathf.Cos(axisPhi) * sinTheta,
                cosTheta,
                Mathf.Sin(axisPhi) * sinTheta
            );
            float rotAngleDeg = h5 * 360f;
            return Matrix4x4.Rotate(Quaternion.AngleAxis(rotAngleDeg, rotAxis));
        }

        private static Vector3 GetRotatedRayDirection(int rayIndex, int raysPerProbe, Matrix4x4 rotMatrix)
        {
            Vector3 baseDir = FibonacciSphereDirection(rayIndex, raysPerProbe);
            Vector3 rotated = rotMatrix.MultiplyVector(baseDir);
            return rotated.normalized;
        }

        private static Vector3 OctahedronDecode(Vector2 uv)
        {
            uv = uv * 2f - Vector2.one;
            Vector3 n = new Vector3(uv.x, uv.y, 1f - Mathf.Abs(uv.x) - Mathf.Abs(uv.y));
            if (n.z < 0f)
            {
                float nx = n.x;
                float ny = n.y;
                n.x = (1f - Mathf.Abs(ny)) * (nx >= 0f ? 1f : -1f);
                n.y = (1f - Mathf.Abs(nx)) * (ny >= 0f ? 1f : -1f);
            }
            return n.normalized;
        }

        private static Vector2 OctahedronEncode(Vector3 n)
        {
            float sum = Mathf.Abs(n.x) + Mathf.Abs(n.y) + Mathf.Abs(n.z);
            float nx = n.x / sum;
            float ny = n.y / sum;
            if (n.z < 0f)
            {
                float onx = nx;
                float ony = ny;
                nx = (1f - Mathf.Abs(ony)) * (onx >= 0f ? 1f : -1f);
                ny = (1f - Mathf.Abs(onx)) * (ony >= 0f ? 1f : -1f);
            }
            return new Vector2(nx * 0.5f + 0.5f, ny * 0.5f + 0.5f);
        }

        private static Color SampleCubemapCPU(Cubemap cubemap, Vector3 direction)
        {

            float absX = Mathf.Abs(direction.x);
            float absY = Mathf.Abs(direction.y);
            float absZ = Mathf.Abs(direction.z);

            CubemapFace face;
            float u, v, ma;

            if (absX >= absY && absX >= absZ)
            {
                ma = absX;
                if (direction.x > 0) { face = CubemapFace.PositiveX; u = -direction.z; v = -direction.y; }
                else { face = CubemapFace.NegativeX; u = direction.z; v = -direction.y; }
            }
            else if (absY >= absX && absY >= absZ)
            {
                ma = absY;
                if (direction.y > 0) { face = CubemapFace.PositiveY; u = direction.x; v = direction.z; }
                else { face = CubemapFace.NegativeY; u = direction.x; v = -direction.z; }
            }
            else
            {
                ma = absZ;
                if (direction.z > 0) { face = CubemapFace.PositiveZ; u = direction.x; v = -direction.y; }
                else { face = CubemapFace.NegativeZ; u = -direction.x; v = -direction.y; }
            }

            float invMA = 0.5f / ma;
            u = u * invMA + 0.5f;
            v = v * invMA + 0.5f;

            int size = cubemap.width;
            Color[] pixels = cubemap.GetPixels(face, 0);

            float fx = u * size - 0.5f;
            float fy = v * size - 0.5f;
            int x0 = Mathf.Clamp(Mathf.FloorToInt(fx), 0, size - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(fy), 0, size - 1);
            int x1 = Mathf.Min(x0 + 1, size - 1);
            int y1 = Mathf.Min(y0 + 1, size - 1);
            float sx = Mathf.Clamp01(fx - x0);
            float sy = Mathf.Clamp01(fy - y0);

            Color c00 = pixels[y0 * size + x0];
            Color c10 = pixels[y0 * size + x1];
            Color c01 = pixels[y1 * size + x0];
            Color c11 = pixels[y1 * size + x1];

            Color row0 = Color.Lerp(c00, c10, sx);
            Color row1 = Color.Lerp(c01, c11, sx);
            Color result = Color.Lerp(row0, row1, sy);

            if (IsCubemapSRGB(cubemap))
            {
                result = result.linear;
            }

            return result;
        }

        private static bool IsCubemapSRGB(Cubemap cubemap)
        {
            string formatName = cubemap.graphicsFormat.ToString();
            return formatName.Contains("SRGB") || formatName.Contains("SRgb") || formatName.Contains("sRGB");
        }

        private static Vector3[] GenerateUniformSphereDirections(int count)
        {
            var dirs = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                dirs[i] = FibonacciSphereDirection(i, count);
            }
            return dirs;
        }

        private static Color[] ReadbackRT(RenderTexture rt)
        {
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBAFloat, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            Color[] pixels = tex.GetPixels();
            DestroyImmediate(tex);
            return pixels;
        }

        private static void ComputeErrorStats(Color[] a, Color[] b, int count,
            out float rmse, out float maxErr, out int maxErrIdx)
        {
            float sumSqErr = 0f;
            maxErr = 0f;
            maxErrIdx = 0;

            for (int i = 0; i < count; i++)
            {
                float dr = a[i].r - b[i].r;
                float dg = a[i].g - b[i].g;
                float db = a[i].b - b[i].b;
                float err = Mathf.Sqrt(dr * dr + dg * dg + db * db);
                sumSqErr += err * err;
                if (err > maxErr)
                {
                    maxErr = err;
                    maxErrIdx = i;
                }
            }
            rmse = Mathf.Sqrt(sumSqErr / Mathf.Max(count, 1));
        }

        private static void ComputeErrorStats(Vector3[] a, Vector3[] b, int count,
            out float rmse, out float maxErr, out int maxErrIdx)
        {
            float sumSqErr = 0f;
            maxErr = 0f;
            maxErrIdx = 0;

            for (int i = 0; i < count; i++)
            {
                float err = (a[i] - b[i]).magnitude;
                sumSqErr += err * err;
                if (err > maxErr)
                {
                    maxErr = err;
                    maxErrIdx = i;
                }
            }
            rmse = Mathf.Sqrt(sumSqErr / Mathf.Max(count, 1));
        }

        #endregion

        #region Step 0: CPU Ground Truth vs Unity LightProbe

        private Vector3 ComputeSkyIrradianceGT(Cubemap skybox, float skyIntensity, Vector3 normal, int sampleCount)
        {
            Vector3 irradianceSum = Vector3.zero;

            for (int i = 0; i < sampleCount; i++)
            {
                Vector3 dir = FibonacciSphereDirection(i, sampleCount);
                float cosWeight = Mathf.Max(0f, Vector3.Dot(dir, normal));
                if (cosWeight <= 0f) continue;

                Color sky = SampleCubemapCPU(skybox, dir);
                Vector3 radiance = new Vector3(sky.r, sky.g, sky.b) * skyIntensity;

                irradianceSum += radiance * cosWeight;
            }

            float monteCarloFactor = 4f * Mathf.PI / sampleCount;
            return irradianceSum * monteCarloFactor;
        }

        public PhaseResult ValidateStep0_GTvsUnityLightProbe()
        {
            var result = new PhaseResult { phaseName = "Step 0: CPU Ground Truth vs Unity LightProbe" };
            var sb = new StringBuilder();

            Cubemap skybox = GetSkyboxCubemap();
            if (skybox == null)
            {
                result.passed = false;
                result.details = "无法获取天空盒Cubemap。请确保使用了Skybox/Cubemap材质。";
                return result;
            }

            sb.AppendLine("===== 环境诊断 =====");
            sb.AppendLine($"天空盒: {skybox.name} ({skybox.width}x{skybox.width})");
            sb.AppendLine($"GraphicsFormat: {skybox.graphicsFormat}");
            sb.AppendLine($"IsCubemapSRGB: {IsCubemapSRGB(skybox)}");
            sb.AppendLine($"Color Space: {QualitySettings.activeColorSpace}");
            sb.AppendLine($"环境光模式: {RenderSettings.ambientMode}");
            sb.AppendLine($"环境光强度(ambientIntensity): {RenderSettings.ambientIntensity}");
            sb.AppendLine($"反射强度(reflectionIntensity): {RenderSettings.reflectionIntensity}");

            Material skyboxMat = RenderSettings.skybox;
            float skyboxMatExposure = 1f;
            if (skyboxMat != null)
            {
                if (skyboxMat.HasFloat("_Exposure"))
                    skyboxMatExposure = skyboxMat.GetFloat("_Exposure");
                sb.AppendLine($"Skybox Material Exposure: {skyboxMatExposure}");
                sb.AppendLine($"Skybox Material Shader: {skyboxMat.shader.name}");
            }

            EnsureReferences();
            Vector3 probePos = GetProbeWorldPosition(m_ProbeIndex);
            sb.AppendLine($"Probe位置: {probePos}");

            SphericalHarmonicsL2 sh;
            LightProbes.GetInterpolatedProbe(probePos, null, out sh);

            float unityAmbientIntensity = RenderSettings.ambientIntensity;

            float unityEffectiveSkyIntensity = skyboxMatExposure * unityAmbientIntensity;
            sb.AppendLine($"Unity有效天空强度 (Exposure × ambientIntensity): {unityEffectiveSkyIntensity}");
            sb.AppendLine($"DDGI天空盒强度: {GetDDGISkyboxIntensity()}");

            sb.AppendLine();
            sb.AppendLine("===== SH系数原始值 (L0, L1) =====");
            sb.AppendLine("  Baked LightProbe SH:");
            for (int ch = 0; ch < 3; ch++)
            {
                string chName = ch == 0 ? "R" : (ch == 1 ? "G" : "B");
                sb.AppendLine($"    {chName}: L0={sh[ch, 0]:F6}  L1=({sh[ch, 1]:F6}, {sh[ch, 2]:F6}, {sh[ch, 3]:F6})");
            }

            Vector3[] directions = GenerateUniformSphereDirections(m_SHCompareDirections);
            float invPI = 1f / Mathf.PI;

            var gtValues = new Vector3[m_SHCompareDirections];
            var unityValues = new Vector3[m_SHCompareDirections];
            var dirArr = new Vector3[1];
            var colArr = new Color[1];

            for (int i = 0; i < m_SHCompareDirections; i++)
            {

                Vector3 irradiance = ComputeSkyIrradianceGT(skybox, unityEffectiveSkyIntensity, directions[i], 1024);
                gtValues[i] = irradiance * invPI;

                dirArr[0] = directions[i];
                sh.Evaluate(dirArr, colArr);
                unityValues[i] = new Vector3(
                    Mathf.Max(0f, colArr[0].r),
                    Mathf.Max(0f, colArr[0].g),
                    Mathf.Max(0f, colArr[0].b));
            }

            sb.AppendLine();
            sb.AppendLine("===== GT(E/π) vs Unity SH 详细对比 =====");
            sb.AppendLine($"公式: GT(n) = (1/π) × ∫ L_sky(ω) × {unityEffectiveSkyIntensity:F2} × max(0,n·ω) dω");
            sb.AppendLine($"MC采样数: 1024");
            sb.AppendLine();
            sb.AppendLine($"{"方向",-25} {"GT_R",8} {"GT_G",8} {"GT_B",8} | {"U_R",8} {"U_G",8} {"U_B",8} | {"Err",8}");
            sb.AppendLine(new string('-', 100));

            for (int i = 0; i < m_SHCompareDirections; i++)
            {
                float err = (gtValues[i] - unityValues[i]).magnitude;
                string dirName = FormatDirection(directions[i]);

                if (i < 16 || err > 0.05f)
                {
                    string marker = err > 0.05f ? " <<<" : "";
                    sb.AppendLine($"{dirName,-25} {gtValues[i].x,8:F4} {gtValues[i].y,8:F4} {gtValues[i].z,8:F4} | " +
                                  $"{unityValues[i].x,8:F4} {unityValues[i].y,8:F4} {unityValues[i].z,8:F4} | {err,8:F4}{marker}");
                }
            }

            ComputeErrorStats(gtValues, unityValues, m_SHCompareDirections,
                out float rmse, out float maxErr, out int maxErrIdx);

            float gtTotalEnergy = 0f, unityTotalEnergy = 0f;
            for (int i = 0; i < m_SHCompareDirections; i++)
            {
                gtTotalEnergy += gtValues[i].magnitude;
                unityTotalEnergy += unityValues[i].magnitude;
            }
            float energyRatio = unityTotalEnergy > 0.0001f ? gtTotalEnergy / unityTotalEnergy : 0f;

            sb.AppendLine();
            sb.AppendLine($"RMSE: {rmse:F6}");
            sb.AppendLine($"最大误差: {maxErr:F6} (方向#{maxErrIdx}: {FormatDirection(directions[maxErrIdx])})");
            sb.AppendLine($"总能量比 (GT/Unity): {energyRatio:F4}");

            bool passed = rmse < 0.15f && Mathf.Abs(energyRatio - 1f) < 0.2f;
            sb.AppendLine($"判定: {(passed ? "PASS" : "FAIL")} (RMSE阈值<0.15, 能量比偏差<20%)");

            if (!passed)
            {
                sb.AppendLine();
                sb.AppendLine("!! GT与Unity LightProbe差异过大 !!");
                sb.AppendLine("可能原因:");
                sb.AppendLine("  1. LightProbe未烘焙或数据过时（请重新烘焙后重试）");
                sb.AppendLine("  2. 当前skybox cubemap与烘焙时使用的不同");
                sb.AppendLine("  3. 场景中仍有几何体影响LightProbe烘焙结果");
                sb.AppendLine("  4. 天空盒Exposure/Intensity参数与烘焙时不一致");
            }

            result.passed = passed;
            result.rmse = rmse;
            result.maxError = maxErr;
            result.maxErrorIndex = maxErrIdx;
            result.details = sb.ToString();
            return result;
        }

        #endregion

        #region Step 1: Phase 1 验证 - 光线方向 + 天空采样

        public PhaseResult ValidateStep1_RayGen()
        {
            var result = new PhaseResult { phaseName = "Step 1: Phase 1 - RayGen + Sky Sampling" };
            var sb = new StringBuilder();

            EnsureReferences();
            var rtManager = m_Updater.RaytracingManager;
            if (rtManager == null || !rtManager.IsInitialized)
            {
                result.passed = false;
                result.details = "RaytracingManager未初始化。请确保使用Raytracing更新模式并已初始化。";
                return result;
            }

            int raysPerProbe = rtManager.RaysPerProbe;
            int probeCount = m_Volume.Descriptor.TotalProbeCount;
            int probeIndex = Mathf.Clamp(m_ProbeIndex, 0, probeCount - 1);

            Matrix4x4 rotMatrix = rtManager.GetCurrentRayRotationMatrix();

            Cubemap skybox = GetSkyboxCubemap();
            float skyIntensity = GetDDGISkyboxIntensity();

            Color[] gpuEmission = ReadbackRT(rtManager.GBufferEmissionMetallic);
            Color[] gpuNormalHitFlag = ReadbackRT(rtManager.GBufferNormalHitFlag);
            Color[] gpuPositionDist = ReadbackRT(rtManager.GBufferPositionDistance);

            int gBufferWidth = rtManager.GetGBufferSize().x;

            sb.AppendLine($"Probe #{probeIndex}, RaysPerProbe={raysPerProbe}");
            sb.AppendLine($"G-Buffer大小: {rtManager.GetGBufferSize()}");
            sb.AppendLine();

            int missCount = 0;
            int hitCount = 0;
            float maxRayDist = rtManager.RayMaxDistance;

            var cpuEmission = new Color[raysPerProbe];
            var gpuEmissionSlice = new Color[raysPerProbe];

            sb.AppendLine($"{"Ray",-5} {"HitFlag",8} {"HitDist",10} | {"CPU_R",8} {"CPU_G",8} {"CPU_B",8} | {"GPU_R",8} {"GPU_G",8} {"GPU_B",8} | {"Err",8}");
            sb.AppendLine(new string('-', 110));

            for (int rayIdx = 0; rayIdx < raysPerProbe; rayIdx++)
            {
                int linearIndex = probeIndex * raysPerProbe + rayIdx;
                int x = linearIndex % gBufferWidth;
                int y = linearIndex / gBufferWidth;
                int pixelIndex = y * rtManager.GetGBufferSize().x + x;

                float hitFlag = gpuNormalHitFlag[pixelIndex].a;
                float hitDist = gpuPositionDist[pixelIndex].a;

                if (hitFlag < 0.5f) missCount++;
                else hitCount++;

                Vector3 rayDir = GetRotatedRayDirection(rayIdx, raysPerProbe, rotMatrix);
                Color cpuSky = Color.black;
                if (skybox != null)
                {
                    cpuSky = SampleCubemapCPU(skybox, rayDir);
                    cpuSky.r *= skyIntensity;
                    cpuSky.g *= skyIntensity;
                    cpuSky.b *= skyIntensity;
                }

                cpuEmission[rayIdx] = cpuSky;
                gpuEmissionSlice[rayIdx] = gpuEmission[pixelIndex];

                float err = Mathf.Sqrt(
                    Sqr(cpuSky.r - gpuEmission[pixelIndex].r) +
                    Sqr(cpuSky.g - gpuEmission[pixelIndex].g) +
                    Sqr(cpuSky.b - gpuEmission[pixelIndex].b));

                if (rayIdx < 16 || err > m_Phase1Tolerance)
                {
                    string marker = err > m_Phase1Tolerance ? " <<<" : "";
                    sb.AppendLine($"{rayIdx,-5} {hitFlag,8:F1} {hitDist,10:F2} | " +
                                  $"{cpuSky.r,8:F4} {cpuSky.g,8:F4} {cpuSky.b,8:F4} | " +
                                  $"{gpuEmission[pixelIndex].r,8:F4} {gpuEmission[pixelIndex].g,8:F4} {gpuEmission[pixelIndex].b,8:F4} | " +
                                  $"{err,8:F4}{marker}");
                }
            }

            ComputeErrorStats(cpuEmission, gpuEmissionSlice, raysPerProbe,
                out float rmse, out float maxErr, out int maxErrIdx);

            sb.AppendLine();
            sb.AppendLine($"Miss/Hit: {missCount}/{hitCount} (期望: {raysPerProbe}/0)");
            sb.AppendLine($"天空采样 RMSE: {rmse:F6}");
            sb.AppendLine($"天空采样最大误差: {maxErr:F6} (ray #{maxErrIdx})");

            bool allMiss = hitCount == 0;
            bool emissionOk = rmse < m_Phase1Tolerance;
            bool passed = allMiss && emissionOk;

            sb.AppendLine($"判定: {(passed ? "PASS" : "FAIL")}");
            if (!allMiss) sb.AppendLine("  !! 存在Hit光线，场景中可能还有几何体 !!");
            if (!emissionOk) sb.AppendLine($"  !! 天空采样RMSE={rmse:F6} 超过阈值{m_Phase1Tolerance} !!");

            result.passed = passed;
            result.rmse = rmse;
            result.maxError = maxErr;
            result.maxErrorIndex = maxErrIdx;
            result.details = sb.ToString();
            return result;
        }

        #endregion

        #region Step 2: Phase 2 验证 - 光照缓冲区

        public PhaseResult ValidateStep2_Lighting()
        {
            var result = new PhaseResult { phaseName = "Step 2: Phase 2 - Lighting Buffers" };
            var sb = new StringBuilder();

            EnsureReferences();
            var rtManager = m_Updater.RaytracingManager;
            if (rtManager == null || !rtManager.IsInitialized)
            {
                result.passed = false;
                result.details = "RaytracingManager未初始化。";
                return result;
            }

            int raysPerProbe = rtManager.RaysPerProbe;
            int probeIndex = Mathf.Clamp(m_ProbeIndex, 0, m_Volume.Descriptor.TotalProbeCount - 1);
            int gBufferWidth = rtManager.GetGBufferSize().x;

            Color[] gpuDirect = ReadbackRT(rtManager.DirectIrradianceBuffer);
            Color[] gpuIndirect = ReadbackRT(rtManager.IndirectIrradianceBuffer);
            Color[] gpuRadiance = ReadbackRT(rtManager.RadianceBuffer);
            Color[] gpuEmission = ReadbackRT(rtManager.GBufferEmissionMetallic);

            float directMax = 0f;
            float indirectMax = 0f;
            float radianceVsEmissionMaxErr = 0f;
            int radianceErrIdx = 0;

            sb.AppendLine($"Probe #{probeIndex}, RaysPerProbe={raysPerProbe}");
            sb.AppendLine();

            for (int rayIdx = 0; rayIdx < raysPerProbe; rayIdx++)
            {
                int linearIndex = probeIndex * raysPerProbe + rayIdx;
                int x = linearIndex % gBufferWidth;
                int y = linearIndex / gBufferWidth;
                int pixelIndex = y * rtManager.GetGBufferSize().x + x;

                float dMag = Mathf.Sqrt(
                    Sqr(gpuDirect[pixelIndex].r) +
                    Sqr(gpuDirect[pixelIndex].g) +
                    Sqr(gpuDirect[pixelIndex].b));
                directMax = Mathf.Max(directMax, dMag);

                float iMag = Mathf.Sqrt(
                    Sqr(gpuIndirect[pixelIndex].r) +
                    Sqr(gpuIndirect[pixelIndex].g) +
                    Sqr(gpuIndirect[pixelIndex].b));
                indirectMax = Mathf.Max(indirectMax, iMag);

                float rErr = Mathf.Sqrt(
                    Sqr(gpuRadiance[pixelIndex].r - gpuEmission[pixelIndex].r) +
                    Sqr(gpuRadiance[pixelIndex].g - gpuEmission[pixelIndex].g) +
                    Sqr(gpuRadiance[pixelIndex].b - gpuEmission[pixelIndex].b));
                if (rErr > radianceVsEmissionMaxErr)
                {
                    radianceVsEmissionMaxErr = rErr;
                    radianceErrIdx = rayIdx;
                }
            }

            sb.AppendLine($"DirectIrradiance最大值: {directMax:F6} (期望: 0)");
            sb.AppendLine($"IndirectIrradiance最大值: {indirectMax:F6} (期望: 0)");
            sb.AppendLine($"Radiance vs Emission最大误差: {radianceVsEmissionMaxErr:F6} (ray #{radianceErrIdx})");

            bool directOk = directMax < m_Phase2Tolerance;
            bool indirectOk = indirectMax < m_Phase2Tolerance;
            bool radianceOk = radianceVsEmissionMaxErr < m_Phase2Tolerance;
            bool passed = directOk && indirectOk && radianceOk;

            sb.AppendLine();
            sb.AppendLine($"DirectIrradiance=0: {(directOk ? "PASS" : "FAIL")}");
            sb.AppendLine($"IndirectIrradiance=0: {(indirectOk ? "PASS" : "FAIL")}");
            sb.AppendLine($"Radiance透传emission: {(radianceOk ? "PASS" : "FAIL")}");
            sb.AppendLine($"判定: {(passed ? "PASS" : "FAIL")}");

            if (!directOk) sb.AppendLine("  !! DirectIrradiance非零，Miss时DeferredLighting应输出0 !!");
            if (!indirectOk) sb.AppendLine("  !! IndirectIrradiance非零，检查IndirectLighting的Miss处理 !!");
            if (!radianceOk) sb.AppendLine("  !! RadianceBuffer不等于emission，检查RadianceComposite的Miss分支 !!");

            result.passed = passed;
            result.rmse = Mathf.Max(directMax, Mathf.Max(indirectMax, radianceVsEmissionMaxErr));
            result.maxError = radianceVsEmissionMaxErr;
            result.maxErrorIndex = radianceErrIdx;
            result.details = sb.ToString();
            return result;
        }

        #endregion

        #region Step 3: Phase 3 验证 - 蒙特卡洛积分

        public PhaseResult ValidateStep3_MonteCarlo()
        {
            var result = new PhaseResult { phaseName = "Step 3: Phase 3 - Monte Carlo Integration" };
            var sb = new StringBuilder();

            EnsureReferences();
            var rtManager = m_Updater.RaytracingManager;
            if (rtManager == null || !rtManager.IsInitialized)
            {
                result.passed = false;
                result.details = "RaytracingManager未初始化。";
                return result;
            }

            var atlasManager = m_Volume.AtlasManager;
            var atlasConfig = m_Volume.AtlasConfig;
            var desc = m_Volume.Descriptor;

            int raysPerProbe = rtManager.RaysPerProbe;
            int probeIndex = Mathf.Clamp(m_ProbeIndex, 0, desc.TotalProbeCount - 1);
            int irradianceRes = atlasConfig.irradianceProbeResolution;
            float irradianceGamma = desc.irradianceGamma;

            Matrix4x4 rotMatrix = rtManager.GetCurrentRayRotationMatrix();

            Color[] gpuRadiance = ReadbackRT(rtManager.RadianceBuffer);
            int gBufferWidth = rtManager.GetGBufferSize().x;
            int gBufferTotalWidth = rtManager.GetGBufferSize().x;

            Color[] gpuAtlas = ReadbackRT(atlasManager.IrradianceAtlas);
            int atlasWidth = atlasManager.IrradianceAtlasSize.x;

            Vector2Int probeBaseCoord = atlasManager.GetIrradianceProbePixelCoord(probeIndex);

            sb.AppendLine($"Probe #{probeIndex}");
            sb.AppendLine($"IrradianceRes: {irradianceRes}, Gamma: {irradianceGamma}");
            sb.AppendLine($"Hysteresis: {desc.hysteresis}");
            sb.AppendLine($"Atlas基坐标: {probeBaseCoord}");
            sb.AppendLine($"Atlas大小: {atlasManager.IrradianceAtlasSize}");
            sb.AppendLine();

            int totalTexels = irradianceRes * irradianceRes;
            var cpuIrradiance = new Vector3[totalTexels];
            var cpuEncoded = new Vector3[totalTexels];
            var gpuValues = new Vector3[totalTexels];

            sb.AppendLine($"{"Texel",-10} {"Dir",-20} | {"CPU_R",8} {"CPU_G",8} {"CPU_B",8} | {"GPU_R",8} {"GPU_G",8} {"GPU_B",8} | {"Err",8}");
            sb.AppendLine(new string('-', 110));

            for (int ty = 0; ty < irradianceRes; ty++)
            {
                for (int tx = 0; tx < irradianceRes; tx++)
                {
                    int texelIdx = ty * irradianceRes + tx;

                    Vector2 uv = new Vector2(
                        (tx + 0.5f) / irradianceRes,
                        (ty + 0.5f) / irradianceRes
                    );
                    Vector3 texelDir = OctahedronDecode(uv);

                    Vector3 irradianceSum = Vector3.zero;
                    float weightSum = 0f;

                    for (int rayIdx = 0; rayIdx < raysPerProbe; rayIdx++)
                    {
                        Vector3 rayDir = GetRotatedRayDirection(rayIdx, raysPerProbe, rotMatrix);
                        float cosWeight = Vector3.Dot(rayDir, texelDir);
                        if (cosWeight <= 0f) continue;

                        int linearIndex = probeIndex * raysPerProbe + rayIdx;
                        int gx = linearIndex % gBufferWidth;
                        int gy = linearIndex / gBufferWidth;
                        int gpIdx = gy * gBufferTotalWidth + gx;

                        Vector3 radiance = new Vector3(
                            gpuRadiance[gpIdx].r,
                            gpuRadiance[gpIdx].g,
                            gpuRadiance[gpIdx].b);

                        irradianceSum += radiance * cosWeight;
                        weightSum += cosWeight;
                    }

                    Vector3 newIrr = Vector3.zero;
                    if (weightSum > 0.0001f)
                    {
                        newIrr = irradianceSum / weightSum;
                    }
                    cpuIrradiance[texelIdx] = newIrr;

                    Vector3 encoded = new Vector3(
                        Mathf.Pow(Mathf.Max(newIrr.x, 0.0001f), 1f / irradianceGamma),
                        Mathf.Pow(Mathf.Max(newIrr.y, 0.0001f), 1f / irradianceGamma),
                        Mathf.Pow(Mathf.Max(newIrr.z, 0.0001f), 1f / irradianceGamma)
                    );

                    cpuEncoded[texelIdx] = encoded;

                    int atlasX = probeBaseCoord.x + tx;
                    int atlasY = probeBaseCoord.y + ty;
                    int atlasPixIdx = atlasY * atlasWidth + atlasX;
                    gpuValues[texelIdx] = new Vector3(
                        gpuAtlas[atlasPixIdx].r,
                        gpuAtlas[atlasPixIdx].g,
                        gpuAtlas[atlasPixIdx].b);

                    float err = (cpuEncoded[texelIdx] - gpuValues[texelIdx]).magnitude;

                    if (texelIdx < 16 || err > m_Phase3Tolerance)
                    {
                        string dirStr = $"({texelDir.x:F2},{texelDir.y:F2},{texelDir.z:F2})";
                        string marker = err > m_Phase3Tolerance ? " <<<" : "";
                        sb.AppendLine($"[{tx},{ty}]     {dirStr,-20} | " +
                                      $"{cpuEncoded[texelIdx].x,8:F4} {cpuEncoded[texelIdx].y,8:F4} {cpuEncoded[texelIdx].z,8:F4} | " +
                                      $"{gpuValues[texelIdx].x,8:F4} {gpuValues[texelIdx].y,8:F4} {gpuValues[texelIdx].z,8:F4} | " +
                                      $"{err,8:F4}{marker}");
                    }
                }
            }

            ComputeErrorStats(cpuEncoded, gpuValues, totalTexels,
                out float rmse, out float maxErr, out int maxErrIdx);

            int mTexX = maxErrIdx % irradianceRes;
            int mTexY = maxErrIdx / irradianceRes;

            sb.AppendLine();
            sb.AppendLine($"积分结果RMSE (Gamma编码后): {rmse:F6}");
            sb.AppendLine($"最大误差: {maxErr:F6} (texel [{mTexX},{mTexY}])");
            sb.AppendLine();

            if (desc.hysteresis > 0.01f)
            {
                sb.AppendLine($"注意: Hysteresis={desc.hysteresis:F2}，当前Atlas包含历史帧融合数据。");
                sb.AppendLine("若要精确验证积分算法，请临时将Hysteresis设为0后重新执行一帧更新。");
                sb.AppendLine("当前仅检查CPU和GPU积分结果的方向趋势一致性。");
            }

            bool passed = rmse < m_Phase3Tolerance;
            if (desc.hysteresis > 0.01f)
            {

                passed = rmse < 0.5f;
                sb.AppendLine($"(Hysteresis模式，放宽阈值到0.5)");
            }

            sb.AppendLine($"判定: {(passed ? "PASS" : "FAIL")}");

            result.passed = passed;
            result.rmse = rmse;
            result.maxError = maxErr;
            result.maxErrorIndex = maxErrIdx;
            result.details = sb.ToString();
            return result;
        }

        #endregion

        #region Step 4: 最终对比 - DDGI vs Unity LightProbe

        public PhaseResult ValidateStep4_DDGIvsUnity()
        {
            var result = new PhaseResult { phaseName = "Step 4: DDGI Irradiance vs Unity LightProbe" };
            var sb = new StringBuilder();

            EnsureReferences();

            var atlasManager = m_Volume.AtlasManager;
            var atlasConfig = m_Volume.AtlasConfig;
            var desc = m_Volume.Descriptor;

            int probeIndex = Mathf.Clamp(m_ProbeIndex, 0, desc.TotalProbeCount - 1);
            int irradianceRes = atlasConfig.irradianceProbeResolution;
            float irradianceGamma = desc.irradianceGamma;

            Vector3 probePos = GetProbeWorldPosition(probeIndex);

            Color[] gpuAtlas = ReadbackRT(atlasManager.IrradianceAtlas);
            int atlasWidth = atlasManager.IrradianceAtlasSize.x;
            Vector2Int probeBaseCoord = atlasManager.GetIrradianceProbePixelCoord(probeIndex);

            SphericalHarmonicsL2 sh;
            LightProbes.GetInterpolatedProbe(probePos, null, out sh);

            Vector3[] directions = GenerateUniformSphereDirections(m_SHCompareDirections);

            var ddgiIrradiance = new Vector3[m_SHCompareDirections];
            var unityIrradiance = new Vector3[m_SHCompareDirections];

            var dirArray = new Vector3[1];
            var colorArray = new Color[1];

            sb.AppendLine($"Probe #{probeIndex}, 位置: {probePos}");
            sb.AppendLine($"IrradianceRes: {irradianceRes}, Gamma: {irradianceGamma}");
            sb.AppendLine();
            sb.AppendLine($"{"方向",-25} {"DDGI_R",8} {"DDGI_G",8} {"DDGI_B",8} | {"Unity_R",8} {"Unity_G",8} {"Unity_B",8} | {"Err",8} {"Rel%",6}");
            sb.AppendLine(new string('-', 115));

            for (int i = 0; i < m_SHCompareDirections; i++)
            {

                ddgiIrradiance[i] = SampleDDGIIrradiance(
                    gpuAtlas, atlasWidth, probeBaseCoord,
                    irradianceRes, irradianceGamma, directions[i]);

                dirArray[0] = directions[i];
                sh.Evaluate(dirArray, colorArray);
                unityIrradiance[i] = new Vector3(
                    Mathf.Max(0f, colorArray[0].r),
                    Mathf.Max(0f, colorArray[0].g),
                    Mathf.Max(0f, colorArray[0].b));

                float err = (ddgiIrradiance[i] - unityIrradiance[i]).magnitude;
                float relErr = unityIrradiance[i].magnitude > 0.001f
                    ? err / unityIrradiance[i].magnitude * 100f : 0f;

                string dirName = FormatDirection(directions[i]);
                if (i < 20 || err > m_FinalTolerance)
                {
                    string marker = err > m_FinalTolerance ? " <<<" : "";
                    sb.AppendLine($"{dirName,-25} " +
                                  $"{ddgiIrradiance[i].x,8:F4} {ddgiIrradiance[i].y,8:F4} {ddgiIrradiance[i].z,8:F4} | " +
                                  $"{unityIrradiance[i].x,8:F4} {unityIrradiance[i].y,8:F4} {unityIrradiance[i].z,8:F4} | " +
                                  $"{err,8:F4} {relErr,5:F1}%{marker}");
                }
            }

            ComputeErrorStats(ddgiIrradiance, unityIrradiance, m_SHCompareDirections,
                out float rmse, out float maxErr, out int maxErrIdx);

            float ddgiTotalEnergy = 0f, unityTotalEnergy = 0f;
            for (int i = 0; i < m_SHCompareDirections; i++)
            {
                ddgiTotalEnergy += ddgiIrradiance[i].magnitude;
                unityTotalEnergy += unityIrradiance[i].magnitude;
            }
            float energyRatio = unityTotalEnergy > 0.0001f ? ddgiTotalEnergy / unityTotalEnergy : 0f;

            sb.AppendLine();
            sb.AppendLine($"RMSE: {rmse:F6}");
            sb.AppendLine($"最大误差: {maxErr:F6} (方向#{maxErrIdx}: {FormatDirection(directions[maxErrIdx])})");
            sb.AppendLine($"总能量比 (DDGI/Unity): {energyRatio:F4}");
            sb.AppendLine($"Hysteresis: {desc.hysteresis} (若>0，DDGI可能尚未收敛)");

            bool passed = rmse < m_FinalTolerance && Mathf.Abs(energyRatio - 1f) < 0.3f;
            sb.AppendLine($"判定: {(passed ? "PASS" : "FAIL")}");

            if (!passed)
            {
                sb.AppendLine();
                sb.AppendLine("定位建议:");
                if (energyRatio < 0.5f)
                    sb.AppendLine("  >> DDGI能量严重偏低，可能原因: Hysteresis未收敛 / Gamma编码错误 / 天空盒强度不匹配");
                else if (energyRatio > 1.5f)
                    sb.AppendLine("  >> DDGI能量偏高，可能原因: 天空盒强度倍数错误 / Gamma解码错误");
                if (rmse > 0.3f)
                    sb.AppendLine("  >> 误差很大，请先检查Step 0-3是否通过");
            }

            result.passed = passed;
            result.rmse = rmse;
            result.maxError = maxErr;
            result.maxErrorIndex = maxErrIdx;
            result.details = sb.ToString();
            return result;
        }

        private static Vector3 SampleDDGIIrradiance(
            Color[] atlasData, int atlasWidth, Vector2Int probeBaseCoord,
            int irradianceRes, float irradianceGamma, Vector3 direction)
        {

            Vector2 octUV = OctahedronEncode(direction);

            int px = Mathf.Clamp(Mathf.FloorToInt(octUV.x * irradianceRes), 0, irradianceRes - 1);
            int py = Mathf.Clamp(Mathf.FloorToInt(octUV.y * irradianceRes), 0, irradianceRes - 1);

            int atlasX = probeBaseCoord.x + px;
            int atlasY = probeBaseCoord.y + py;
            int atlasIdx = atlasY * atlasWidth + atlasX;

            Vector3 encoded = new Vector3(
                atlasData[atlasIdx].r,
                atlasData[atlasIdx].g,
                atlasData[atlasIdx].b);

            float halfGamma = irradianceGamma * 0.5f;
            Vector3 halfDecoded = new Vector3(
                Mathf.Pow(Mathf.Max(encoded.x, 0.0001f), halfGamma),
                Mathf.Pow(Mathf.Max(encoded.y, 0.0001f), halfGamma),
                Mathf.Pow(Mathf.Max(encoded.z, 0.0001f), halfGamma));

            return new Vector3(
                halfDecoded.x * halfDecoded.x,
                halfDecoded.y * halfDecoded.y,
                halfDecoded.z * halfDecoded.z);
        }

        #endregion

        #region 完整验证流程

        public ValidationReport RunFullValidation()
        {
            var report = new ValidationReport();
            var sb = new StringBuilder();

            sb.AppendLine("========================================");
            sb.AppendLine(" DDGI Sky-Only Ground Truth Validation");
            sb.AppendLine($" 时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine("========================================");
            sb.AppendLine();

            sb.AppendLine(">>> Step 0: CPU Ground Truth vs Unity LightProbe <<<");
            report.step0_GTvsUnity = ValidateStep0_GTvsUnityLightProbe();
            sb.AppendLine(report.step0_GTvsUnity.details);
            sb.AppendLine();

            sb.AppendLine(">>> Step 1: Phase 1 - RayGen + Sky Sampling <<<");
            report.step1_RayGen = ValidateStep1_RayGen();
            sb.AppendLine(report.step1_RayGen.details);
            sb.AppendLine();

            sb.AppendLine(">>> Step 2: Phase 2 - Lighting Buffers <<<");
            report.step2_Lighting = ValidateStep2_Lighting();
            sb.AppendLine(report.step2_Lighting.details);
            sb.AppendLine();

            sb.AppendLine(">>> Step 3: Phase 3 - Monte Carlo Integration <<<");
            report.step3_MonteCarlo = ValidateStep3_MonteCarlo();
            sb.AppendLine(report.step3_MonteCarlo.details);
            sb.AppendLine();

            sb.AppendLine(">>> Step 4: DDGI Irradiance vs Unity LightProbe <<<");
            report.step4_Final = ValidateStep4_DDGIvsUnity();
            sb.AppendLine(report.step4_Final.details);
            sb.AppendLine();

            sb.AppendLine("========================================");
            sb.AppendLine(" 总结");
            sb.AppendLine("========================================");
            sb.AppendLine($" Step 0 (GT vs Unity):  {PassFail(report.step0_GTvsUnity)}  RMSE={report.step0_GTvsUnity.rmse:F6}");
            sb.AppendLine($" Step 1 (RayGen):       {PassFail(report.step1_RayGen)}  RMSE={report.step1_RayGen.rmse:F6}");
            sb.AppendLine($" Step 2 (Lighting):     {PassFail(report.step2_Lighting)}  RMSE={report.step2_Lighting.rmse:F6}");
            sb.AppendLine($" Step 3 (MonteCarlo):   {PassFail(report.step3_MonteCarlo)}  RMSE={report.step3_MonteCarlo.rmse:F6}");
            sb.AppendLine($" Step 4 (DDGI vs Unity):{PassFail(report.step4_Final)}  RMSE={report.step4_Final.rmse:F6}");

            bool allPassed = report.step0_GTvsUnity.passed &&
                             report.step1_RayGen.passed &&
                             report.step2_Lighting.passed &&
                             report.step3_MonteCarlo.passed &&
                             report.step4_Final.passed;

            sb.AppendLine();
            sb.AppendLine($" 最终结果: {(allPassed ? "ALL PASSED" : "SOME FAILED")}");

            if (!allPassed)
            {
                sb.AppendLine();
                sb.AppendLine(" 问题定位指引:");
                if (!report.step0_GTvsUnity.passed)
                    sb.AppendLine("   Step 0 FAIL -> GT本身与Unity不匹配，检查天空盒强度/色彩空间");
                if (!report.step1_RayGen.passed)
                    sb.AppendLine("   Step 1 FAIL -> RayGen天空采样有误，检查Cubemap绑定/旋转矩阵");
                if (!report.step2_Lighting.passed)
                    sb.AppendLine("   Step 2 FAIL -> 光照计算有误，检查Miss分支逻辑");
                if (!report.step3_MonteCarlo.passed)
                    sb.AppendLine("   Step 3 FAIL -> 积分算法有误，检查八面体映射/Gamma编码/Hysteresis");
                if (!report.step4_Final.passed)
                    sb.AppendLine("   Step 4 FAIL -> DDGI最终结果与Unity不匹配（如果0-3都PASS，检查Gamma解码链路）");
            }

            sb.AppendLine("========================================");

            report.fullLog = sb.ToString();
            lastReport = report;

            Debug.Log(report.fullLog);
            return report;
        }

        public ValidationReport RunSingleFrameValidation()
        {
            EnsureReferences();

            var descField = typeof(DDGIVolume).GetField("m_Descriptor",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var desc = (DDGIVolumeDescriptor)descField.GetValue(m_Volume);
            float savedHysteresis = desc.hysteresis;

            desc.hysteresis = 0f;
            descField.SetValue(m_Volume, desc);

            m_Volume.AtlasManager.ClearAtlases();

            m_Updater.ForceUpdate();

            var report = RunFullValidation();

            desc.hysteresis = savedHysteresis;
            descField.SetValue(m_Volume, desc);

            return report;
        }

        #endregion

        #region GT可视化方法

        public void BakeGTTexture()
        {
            Cubemap skybox = GetSkyboxCubemap();
            if (skybox == null)
            {
                Debug.LogError("[DDGISkyOnlyValidator] 无法获取天空盒Cubemap");
                return;
            }

            Material skyboxMat = RenderSettings.skybox;
            float skyboxMatExposure = 1f;
            if (skyboxMat != null && skyboxMat.HasFloat("_Exposure"))
                skyboxMatExposure = skyboxMat.GetFloat("_Exposure");
            float effectiveSkyIntensity = skyboxMatExposure * RenderSettings.ambientIntensity;

            int res = gtTextureResolution;
            int samples = gtMCSamples;
            float invPI = 1f / Mathf.PI;

            EnsureReferences();
            Vector3 probePos = GetProbeWorldPosition(m_ProbeIndex);
            SphericalHarmonicsL2 sh;
            LightProbes.GetInterpolatedProbe(probePos, null, out sh);

            var dirArr = new Vector3[1];
            var colArr = new Color[1];

            if (gtTexture == null || gtTexture.width != res)
            {
                if (gtTexture != null) DestroyImmediate(gtTexture);
                gtTexture = new Texture2D(res, res, TextureFormat.RGBAHalf, false, true);
                gtTexture.name = "GT_OctahedralMap";
                gtTexture.filterMode = FilterMode.Bilinear;
                gtTexture.wrapMode = TextureWrapMode.Clamp;
            }
            if (unitySHTexture == null || unitySHTexture.width != res)
            {
                if (unitySHTexture != null) DestroyImmediate(unitySHTexture);
                unitySHTexture = new Texture2D(res, res, TextureFormat.RGBAHalf, false, true);
                unitySHTexture.name = "UnitySH_OctahedralMap";
                unitySHTexture.filterMode = FilterMode.Bilinear;
                unitySHTexture.wrapMode = TextureWrapMode.Clamp;
            }

            Color[] gtPixels = new Color[res * res];
            Color[] shPixels = new Color[res * res];

            for (int ty = 0; ty < res; ty++)
            {
                for (int tx = 0; tx < res; tx++)
                {

                    Vector2 uv = new Vector2(
                        (tx + 0.5f) / res,
                        (ty + 0.5f) / res);
                    Vector3 dir = OctahedronDecode(uv);
                    int idx = ty * res + tx;

                    Vector3 irradiance = ComputeSkyIrradianceGT(skybox, effectiveSkyIntensity, dir, samples);
                    Vector3 gtVal = irradiance * invPI;
                    gtPixels[idx] = new Color(gtVal.x, gtVal.y, gtVal.z, 1f);

                    dirArr[0] = dir; ;
                    sh.Evaluate(dirArr, colArr);
                    shPixels[idx] = new Color(
                        Mathf.Max(0f, colArr[0].r),
                        Mathf.Max(0f, colArr[0].g),
                        Mathf.Max(0f, colArr[0].b), 1f);
                }
            }

            gtTexture.SetPixels(gtPixels);
            gtTexture.Apply();
            unitySHTexture.SetPixels(shPixels);
            unitySHTexture.Apply();

            Debug.Log($"[DDGISkyOnlyValidator] GT贴图已烘焙: {res}x{res}, MC采样={samples}, " +
                      $"天空强度={effectiveSkyIntensity:F2}");
        }

        public void RenderGTVisualization()
        {
            if (!gtVisualizationEnabled || gtTexture == null) return;

            EnsureGTVisResources();
            if (m_GTVisMaterial == null || m_GTSphereCache == null) return;

            EnsureReferences();
            Vector3 probePos = GetProbeWorldPosition(m_ProbeIndex);

            m_GTVisMaterial.SetTexture("_GTTexture", gtTexture);
            if (unitySHTexture != null)
                m_GTVisMaterial.SetTexture("_UnitySHTexture", unitySHTexture);
            m_GTVisMaterial.SetFloat("_ProbeRadius", gtProbeRadius);
            m_GTVisMaterial.SetFloat("_Intensity", gtIntensity);
            m_GTVisMaterial.SetInt("_DisplayMode", gtDisplayMode);
            m_GTVisMaterial.SetVector("_ProbeWorldPos", probePos);

            Matrix4x4 matrix = Matrix4x4.TRS(probePos, Quaternion.identity, Vector3.one);
            Graphics.DrawMesh(m_GTSphereCache, matrix, m_GTVisMaterial, 0);
        }

        public void CleanupGTVisualization()
        {
            if (gtTexture != null)
            {
                DestroyImmediate(gtTexture);
                gtTexture = null;
            }
            if (unitySHTexture != null)
            {
                DestroyImmediate(unitySHTexture);
                unitySHTexture = null;
            }
            if (m_GTVisMaterial != null)
            {
                DestroyImmediate(m_GTVisMaterial);
                m_GTVisMaterial = null;
            }
            gtVisualizationEnabled = false;
        }

        private void EnsureGTVisResources()
        {
            if (m_GTVisMaterial == null)
            {
                Shader shader = Shader.Find("DDGI/GTProbeVisualization");
                if (shader != null)
                {
                    m_GTVisMaterial = new Material(shader);
                    m_GTVisMaterial.name = "GT_ProbeVisualization";
                }
                else
                {
                    Debug.LogError("[DDGISkyOnlyValidator] 找不到 DDGI/GTProbeVisualization shader");
                }
            }

            if (m_GTSphereCache == null)
            {
                m_GTSphereCache = CreateUnitSphere(32, 24);
            }
        }

        private static Mesh CreateUnitSphere(int lonSegments, int latSegments)
        {
            var mesh = new Mesh();
            mesh.name = "GT_ProbeSphere";

            int vertCount = (lonSegments + 1) * (latSegments + 1);
            var vertices = new Vector3[vertCount];
            var normals = new Vector3[vertCount];

            int idx = 0;
            for (int lat = 0; lat <= latSegments; lat++)
            {
                float theta = lat * Mathf.PI / latSegments;
                float sinT = Mathf.Sin(theta);
                float cosT = Mathf.Cos(theta);

                for (int lon = 0; lon <= lonSegments; lon++)
                {
                    float phi = lon * TWO_PI / lonSegments;
                    Vector3 n = new Vector3(
                        Mathf.Cos(phi) * sinT,
                        cosT,
                        Mathf.Sin(phi) * sinT);
                    vertices[idx] = n;
                    normals[idx] = n;
                    idx++;
                }
            }

            int triCount = lonSegments * latSegments * 6;
            var triangles = new int[triCount];
            int ti = 0;
            for (int lat = 0; lat < latSegments; lat++)
            {
                for (int lon = 0; lon < lonSegments; lon++)
                {
                    int cur = lat * (lonSegments + 1) + lon;
                    int next = cur + lonSegments + 1;
                    triangles[ti++] = cur;
                    triangles[ti++] = next;
                    triangles[ti++] = cur + 1;
                    triangles[ti++] = cur + 1;
                    triangles[ti++] = next;
                    triangles[ti++] = next + 1;
                }
            }

            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        private void Update()
        {
            RenderGTVisualization();
        }

        private void OnDisable()
        {
            CleanupGTVisualization();
        }

        #endregion

        #region 内部辅助

        private void EnsureReferences()
        {
            if (m_Volume == null) m_Volume = GetComponent<DDGIVolume>();
            if (m_Updater == null) m_Updater = GetComponent<DDGIProbeUpdater>();
        }

        private Vector3 GetProbeWorldPosition(int probeIndex)
        {
            return m_Volume.GetProbeWorldPosition(probeIndex);
        }

        private Cubemap GetSkyboxCubemap()
        {
            Material skyboxMat = RenderSettings.skybox;
            if (skyboxMat == null) return null;

            string[] texNames = { "_Tex", "_MainTex", "_Cubemap" };
            foreach (string name in texNames)
            {
                if (skyboxMat.HasTexture(name))
                {
                    Texture tex = skyboxMat.GetTexture(name);
                    if (tex is Cubemap cubemap)
                    {
                        if (!cubemap.isReadable)
                        {
                            Debug.LogError($"[DDGISkyOnlyValidator] Cubemap '{cubemap.name}' 不可读。" +
                                           "请在Import Settings中勾选 Read/Write Enabled。");
                            return null;
                        }
                        return cubemap;
                    }
                }
            }
            return null;
        }

        private float GetDDGISkyboxIntensity()
        {
            EnsureReferences();
            var rtManager = m_Updater.RaytracingManager;
            if (rtManager != null)
                return rtManager.SkyboxIntensity;
            return 1f;
        }

        private static string FormatDirection(Vector3 dir)
        {
            return $"({dir.x:F3}, {dir.y:F3}, {dir.z:F3})";
        }

        private static string PassFail(PhaseResult r)
        {
            return r != null && r.passed ? "PASS" : "FAIL";
        }

        private static float Sqr(float x) { return x * x; }

        #endregion
    }
}
