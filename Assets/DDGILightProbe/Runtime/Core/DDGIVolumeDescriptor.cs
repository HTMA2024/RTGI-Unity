using System;
using UnityEngine;

namespace DDGI
{

    public enum VolumeEditMode
    {

        ProbeCountsAndSpacing,

        VolumeSizeAutoProbes,

        VolumeSizeAutoSpacing
    }

    [Serializable]
    public struct DDGIVolumeDescriptor
    {
        [Header("编辑模式")]
        [Tooltip("选择Volume的编辑方式")]
        public VolumeEditMode editMode;

        [Header("空间布局")]
        [Tooltip("体积起点（本地坐标）")]
        public Vector3 origin;

        [Header("空间布局")]
        [Tooltip("探针间距（米），推荐1-2米")]
        public Vector3 probeSpacing;

        [Tooltip("各轴探针数量，建议为2的幂")]
        public Vector3Int probeCounts;

        [Header("Volume尺寸模式")]
        [Tooltip("Volume的尺寸（米）")]
        public Vector3 volumeSize;

        [Tooltip("时间滞后系数，越高越稳定但响应越慢，推荐0.97")]
        [Range(0.0f, 1.0f)]
        public float hysteresis;

        [Tooltip("辐照度Gamma校正（RTXGI默认5.0）")]
        [Range(1.0f, 10.0f)]
        public float irradianceGamma;

        [Tooltip("辐照度变化阈值，超过此值会降低hysteresis以加快响应\n设为0可禁用per-texel加速（推荐，避免探针不均匀斑）")]
        [Range(0.0f, 2.0f)]
        public float irradianceThreshold;

        [Tooltip("亮度变化阈值，超过此值会限制每帧最大变化量\n设为较大值（如10）可禁用此限制")]
        [Range(0.0f, 10.0f)]
        public float brightnessThreshold;

        [Tooltip("视线方向偏移，避免漏光")]
        public float viewBias;

        [Tooltip("是否启用探针重定位")]
        public bool enableProbeRelocation;

        [Tooltip("探针与最近表面的最小距离（米）")]
        [Range(0.01f, 1.0f)]
        public float probeMinFrontfaceDistance;

        [Tooltip("背面命中比例阈值，超过此值认为探针在几何体内部")]
        [Range(0.1f, 0.5f)]
        public float probeBackfaceThreshold;

        [Tooltip("重定位更新间隔（帧数），0表示每帧更新")]
        [Range(0, 30)]
        public int relocationUpdateInterval;

        [Tooltip("是否启用探针分类（自动标记无效探针）")]
        public bool enableProbeClassification;

        [Tooltip("分类更新间隔（帧数），0表示每帧更新")]
        [Range(0, 30)]
        public int classificationUpdateInterval;

        [Header("G-Buffer精度")]
        [Tooltip("使用高精度G-Buffer（R32格式），默认关闭使用R16以节省带宽")]
        public bool useHighPrecisionGBuffer;

        [Tooltip("是否启用探针变异度计算（用于自适应更新）")]
        public bool enableProbeVariability;

        [Tooltip("是否启用自适应更新（基于Variability动态调整更新频率）")]
        public bool enableAdaptiveUpdate;

        [Tooltip("低变异度阈值，低于此值认为场景稳定。\n建议值: 0.02-0.05，RTXGI默认0.02")]
        [Range(0.001f, 0.05f)]
        public float lowVariabilityThreshold;

        [Tooltip("高变异度阈值，高于此值认为场景剧烈变化。\n建议值: 0.1-0.2，需要大于lowThreshold")]
        [Range(0.1f, 0.5f)]
        public float highVariabilityThreshold;

        [Tooltip("最小更新间隔（帧数），场景剧烈变化时使用。\n建议保持1帧以确保响应速度")]
        [Range(1, 8)]
        public int minUpdateInterval;

        [Tooltip("最大更新间隔（帧数），场景稳定时使用。\n建议值: 4-8帧，过大可能导致对变化响应迟钝")]
        [Range(2, 16)]
        public int maxUpdateInterval;

        public static DDGIVolumeDescriptor Default => new DDGIVolumeDescriptor
        {
            editMode = VolumeEditMode.VolumeSizeAutoProbes,
            probeSpacing = new Vector3(2f, 2f, 2f),
            probeCounts = new Vector3Int(5, 3, 5),
            volumeSize = new Vector3(8f, 4f, 8f),
            hysteresis = 0.97f,
            irradianceGamma = 5.0f,

            irradianceThreshold = 0.0f,
            brightnessThreshold = 10.0f,
            viewBias = 0.2f,
            enableProbeRelocation = true,
            probeMinFrontfaceDistance = 0.1f,
            probeBackfaceThreshold = 0.25f,
            relocationUpdateInterval = 0,
            enableProbeClassification = true,
            classificationUpdateInterval = 0,

            useHighPrecisionGBuffer = false,
            enableProbeVariability = true,
            enableAdaptiveUpdate = true,
            lowVariabilityThreshold = 0.02f,
            highVariabilityThreshold = 0.15f,
            minUpdateInterval = 1,
            maxUpdateInterval = 4
        };

        public int TotalProbeCount => probeCounts.x * probeCounts.y * probeCounts.z;

        public void CalculateProbeCountsFromSize()
        {
            probeCounts = new Vector3Int(
                Mathf.Max(2, Mathf.FloorToInt(volumeSize.x / probeSpacing.x) + 1),
                Mathf.Max(2, Mathf.FloorToInt(volumeSize.y / probeSpacing.y) + 1),
                Mathf.Max(2, Mathf.FloorToInt(volumeSize.z / probeSpacing.z) + 1)
            );
        }

        public void CalculateSizeFromProbeCounts()
        {
            volumeSize = new Vector3(
                (probeCounts.x - 1) * probeSpacing.x,
                (probeCounts.y - 1) * probeSpacing.y,
                (probeCounts.z - 1) * probeSpacing.z
            );
        }

        public Vector3Int GetEffectiveProbeCounts()
        {
            if (editMode == VolumeEditMode.VolumeSizeAutoProbes)
            {
                return new Vector3Int(
                    Mathf.Max(2, Mathf.FloorToInt(volumeSize.x / probeSpacing.x) + 1),
                    Mathf.Max(2, Mathf.FloorToInt(volumeSize.y / probeSpacing.y) + 1),
                    Mathf.Max(2, Mathf.FloorToInt(volumeSize.z / probeSpacing.z) + 1)
                );
            }
            return probeCounts;
        }

        public void CalculateSpacingFromSizeAndCounts()
        {
            Vector3Int safeCounts = new Vector3Int(
                Mathf.Max(2, probeCounts.x),
                Mathf.Max(2, probeCounts.y),
                Mathf.Max(2, probeCounts.z)
            );
            probeCounts = safeCounts;

            probeSpacing = new Vector3(
                volumeSize.x / (safeCounts.x - 1),
                volumeSize.y / (safeCounts.y - 1),
                volumeSize.z / (safeCounts.z - 1)
            );
        }

        public void SyncData()
        {
            switch (editMode)
            {
                case VolumeEditMode.ProbeCountsAndSpacing:
                    CalculateSizeFromProbeCounts();
                    break;
                case VolumeEditMode.VolumeSizeAutoProbes:
                    CalculateProbeCountsFromSize();
                    break;
                case VolumeEditMode.VolumeSizeAutoSpacing:
                    CalculateSpacingFromSizeAndCounts();
                    break;
            }
        }

        public Bounds GetLocalBounds()
        {
            Vector3 size = new Vector3(
                (probeCounts.x - 1) * probeSpacing.x,
                (probeCounts.y - 1) * probeSpacing.y,
                (probeCounts.z - 1) * probeSpacing.z
            );
            Vector3 center = size * 0.5f;
            return new Bounds(center, size);
        }

        public int GetProbeIndex(int x, int y, int z)
        {
            return x + y * probeCounts.x + z * probeCounts.x * probeCounts.y;
        }

        public int GetProbeIndex(Vector3Int index)
        {
            return GetProbeIndex(index.x, index.y, index.z);
        }

        public Vector3Int GetProbeIndex3D(int flatIndex)
        {
            int z = flatIndex / (probeCounts.x * probeCounts.y);
            int remainder = flatIndex % (probeCounts.x * probeCounts.y);
            int y = remainder / probeCounts.x;
            int x = remainder % probeCounts.x;
            return new Vector3Int(x, y, z);
        }

        public Vector3 GetProbeLocalPosition(int x, int y, int z)
        {
            return new Vector3(
                x * probeSpacing.x,
                y * probeSpacing.y,
                z * probeSpacing.z
            );
        }

        public Vector3 GetProbeLocalPosition(Vector3Int index)
        {
            return GetProbeLocalPosition(index.x, index.y, index.z);
        }

        public Vector3 GetProbeLocalPosition(int flatIndex)
        {
            Vector3Int index3D = GetProbeIndex3D(flatIndex);
            return GetProbeLocalPosition(index3D);
        }

        public bool IsValidIndex(int x, int y, int z)
        {
            return x >= 0 && x < probeCounts.x &&
                   y >= 0 && y < probeCounts.y &&
                   z >= 0 && z < probeCounts.z;
        }

        public bool IsValidIndex(Vector3Int index)
        {
            return IsValidIndex(index.x, index.y, index.z);
        }
    }
}
