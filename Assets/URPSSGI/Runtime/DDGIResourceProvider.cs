using UnityEngine;

namespace DDGI
{

    public readonly struct DDGIResourceSnapshot
    {
        public readonly RenderTexture irradianceAtlas;
        public readonly RenderTexture distanceAtlas;
        public readonly Vector3 origin;
        public readonly Vector3 probeSpacing;
        public readonly Vector3Int probeCounts;
        public readonly float normalBias;
        public readonly float viewBias;
        public readonly float irradianceGamma;
        public readonly int irradianceProbeRes;
        public readonly int distanceProbeRes;
        public readonly int probesPerRow;
        public readonly Vector2 irradianceTexelSize;
        public readonly Vector2 distanceTexelSize;
        public readonly bool isValid;

        public DDGIResourceSnapshot(
            RenderTexture irradianceAtlas,
            RenderTexture distanceAtlas,
            Vector3 origin,
            Vector3 probeSpacing,
            Vector3Int probeCounts,
            float normalBias,
            float viewBias,
            float irradianceGamma,
            int irradianceProbeRes,
            int distanceProbeRes,
            int probesPerRow,
            Vector2 irradianceTexelSize,
            Vector2 distanceTexelSize,
            bool isValid)
        {
            this.irradianceAtlas = irradianceAtlas;
            this.distanceAtlas = distanceAtlas;
            this.origin = origin;
            this.probeSpacing = probeSpacing;
            this.probeCounts = probeCounts;
            this.normalBias = normalBias;
            this.viewBias = viewBias;
            this.irradianceGamma = irradianceGamma;
            this.irradianceProbeRes = irradianceProbeRes;
            this.distanceProbeRes = distanceProbeRes;
            this.probesPerRow = probesPerRow;
            this.irradianceTexelSize = irradianceTexelSize;
            this.distanceTexelSize = distanceTexelSize;
            this.isValid = isValid;
        }
    }

    public static class DDGIResourceProvider
    {
        public static DDGIResourceSnapshot Current { get; private set; }

        public static void Register(DDGIResourceSnapshot snapshot)
        {
            Current = snapshot;
        }

        public static void Unregister()
        {
            Current = default;
        }
    }
}
