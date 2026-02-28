#ifndef DDGI_SAMPLING_HLSL
#define DDGI_SAMPLING_HLSL

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/GlobalSamplers.hlsl"

#define DDGI_PI 3.14159265359
#define DDGI_TWO_PI 6.28318530718

#define DDGI_PROBE_STATE_ACTIVE   0   // 活跃状态，正常更新和采样
#define DDGI_PROBE_STATE_INACTIVE 1   // 非活跃状态，跳过更新和采样

struct DDGIVolumeParams
{
    float3 origin;
    float3 probeSpacing;
    int3 probeCounts;
    float normalBias;
    float viewBias;
    float irradianceGamma;
    int irradianceProbeRes;
    int distanceProbeRes;
    int probesPerRow;
    float2 irradianceTexelSize;
    float2 distanceTexelSize;
};

float2 DDGIOctahedronEncode(float3 n)
{
    float3 absN = abs(n);
    float sum = absN.x + absN.y + absN.z;
    n.xy /= sum;

    if (n.z < 0.0)
    {
        float2 signNotZero = float2(n.x >= 0.0 ? 1.0 : -1.0, n.y >= 0.0 ? 1.0 : -1.0);
        n.xy = (1.0 - abs(n.yx)) * signNotZero;
    }

    return n.xy * 0.5 + 0.5;
}

int3 DDGIGetBaseProbeGridCoords(float3 worldPos, DDGIVolumeParams volume)
{
    float3 localPos = worldPos - volume.origin;
    float3 probeCoord = localPos / volume.probeSpacing;
    return int3(floor(probeCoord));
}

float3 DDGIGetProbeWorldPosition(int3 probeCoord, DDGIVolumeParams volume)
{
    return volume.origin + float3(probeCoord) * volume.probeSpacing;
}

int DDGIGetProbeLinearIndex(int3 probeCoord, DDGIVolumeParams volume)
{
    return probeCoord.x +
           probeCoord.y * volume.probeCounts.x +
           probeCoord.z * volume.probeCounts.x * volume.probeCounts.y;
}

bool DDGIIsProbeCoordValid(int3 probeCoord, DDGIVolumeParams volume)
{
    return all(probeCoord >= 0) && all(probeCoord < volume.probeCounts);
}

float2 DDGIGetProbeAtlasBaseUV(int probeIndex, int probeRes, int probesPerRow, float2 texelSize)
{
    int probeX = probeIndex % probesPerRow;
    int probeY = probeIndex / probesPerRow;

    float2 baseUV = float2(probeX, probeY) * (probeRes + 2) * texelSize;
    baseUV += texelSize;

    return baseUV;
}

float2 DDGIGetProbeUV(int probeIndex, float3 direction, int probeRes, int probesPerRow, float2 texelSize)
{
    float2 baseUV = DDGIGetProbeAtlasBaseUV(probeIndex, probeRes, probesPerRow, texelSize);
    float2 octUV = DDGIOctahedronEncode(direction);
    return baseUV + octUV * probeRes * texelSize;
}

float3 DDGIGetSurfaceBias(float3 surfaceNormal, float3 viewDirection, DDGIVolumeParams volume)
{
    return (surfaceNormal * volume.normalBias) + (-viewDirection * volume.viewBias);
}

float DDGIGetVolumeBlendWeight(float3 worldPosition, DDGIVolumeParams volume)
{
    float3 extent = (volume.probeSpacing * (float3(volume.probeCounts) - 1.0)) * 0.5;
    float3 center = volume.origin + extent;
    float3 position = abs(worldPosition - center);
    float3 delta = position - extent;

    if (all(delta < 0.0))
        return 1.0;

    float volumeBlendWeight = 1.0;
    volumeBlendWeight *= (1.0 - saturate(delta.x / volume.probeSpacing.x));
    volumeBlendWeight *= (1.0 - saturate(delta.y / volume.probeSpacing.y));
    volumeBlendWeight *= (1.0 - saturate(delta.z / volume.probeSpacing.z));

    return volumeBlendWeight;
}

float3 DDGIGetVolumeIrradiance(
    float3 worldPosition,
    float3 surfaceBias,
    float3 direction,
    DDGIVolumeParams volume,
    Texture2D<float4> irradianceAtlas,
    Texture2D<float2> distanceAtlas,
    SamplerState atlasSampler)
{
    float3 irradiance = float3(0.0, 0.0, 0.0);
    float accumulatedWeights = 0.0;

    float3 biasedWorldPosition = worldPosition + surfaceBias;

    int3 baseProbeCoords = DDGIGetBaseProbeGridCoords(biasedWorldPosition, volume);

    float3 baseProbeWorldPosition = DDGIGetProbeWorldPosition(baseProbeCoords, volume);

    float3 gridSpaceDistance = biasedWorldPosition - baseProbeWorldPosition;
    float3 alpha = saturate(gridSpaceDistance / volume.probeSpacing);

    [unroll]
    for (int probeIdx = 0; probeIdx < 8; probeIdx++)
    {

        int3 adjacentProbeOffset = int3(probeIdx, probeIdx >> 1, probeIdx >> 2) & int3(1, 1, 1);

        int3 adjacentProbeCoords = clamp(
            baseProbeCoords + adjacentProbeOffset,
            int3(0, 0, 0),
            volume.probeCounts - int3(1, 1, 1)
        );

        if (!DDGIIsProbeCoordValid(adjacentProbeCoords, volume))
            continue;

        int adjacentProbeIndex = DDGIGetProbeLinearIndex(adjacentProbeCoords, volume);

        float3 adjacentProbeWorldPosition = DDGIGetProbeWorldPosition(adjacentProbeCoords, volume);

        float3 worldPosToAdjProbe = normalize(adjacentProbeWorldPosition - worldPosition);
        float3 biasedPosToAdjProbe = adjacentProbeWorldPosition - biasedWorldPosition;
        float biasedPosToAdjProbeDist = length(biasedPosToAdjProbe);
        biasedPosToAdjProbe = biasedPosToAdjProbe / max(biasedPosToAdjProbeDist, 0.0001);

        float3 trilinear = max(0.001, lerp(1.0 - alpha, alpha, float3(adjacentProbeOffset)));
        float trilinearWeight = trilinear.x * trilinear.y * trilinear.z;
        float weight = 1.0;

        float wrapShading = (dot(worldPosToAdjProbe, direction) + 1.0) * 0.5;
        weight *= (wrapShading * wrapShading) + 0.2;

        float2 distanceUV = DDGIGetProbeUV(
            adjacentProbeIndex,
            -biasedPosToAdjProbe,
            volume.distanceProbeRes,
            volume.probesPerRow,
            volume.distanceTexelSize
        );

        float2 filteredDistance = 2.0 * distanceAtlas.SampleLevel(atlasSampler, distanceUV, 0).rg;

        float variance = abs(filteredDistance.x * filteredDistance.x - filteredDistance.y);

        float chebyshevWeight = 1.0;
        if (biasedPosToAdjProbeDist > filteredDistance.x)
        {

            float v = biasedPosToAdjProbeDist - filteredDistance.x;
            chebyshevWeight = variance / (variance + v * v);

            chebyshevWeight = max(chebyshevWeight * chebyshevWeight * chebyshevWeight, 0.0);
        }

        weight *= max(0.05, chebyshevWeight);

        weight = max(0.000001, weight);

        const float crushThreshold = 0.2;
        if (weight < crushThreshold)
        {
            weight *= (weight * weight) * (1.0 / (crushThreshold * crushThreshold));
        }

        weight *= trilinearWeight;

        float2 irradianceUV = DDGIGetProbeUV(
            adjacentProbeIndex,
            direction,
            volume.irradianceProbeRes,
            volume.probesPerRow,
            volume.irradianceTexelSize
        );

        float3 probeIrradiance = irradianceAtlas.SampleLevel(atlasSampler, irradianceUV, 0).rgb;

        float3 exponent = volume.irradianceGamma * 0.5;
        probeIrradiance = pow(max(probeIrradiance, 0.0001), exponent);

        irradiance += weight * probeIrradiance;
        accumulatedWeights += weight;
    }

    if (accumulatedWeights == 0.0)
        return float3(0.0, 0.0, 0.0);

    irradiance *= (1.0 / accumulatedWeights);

    irradiance *= irradiance;

    irradiance *= DDGI_TWO_PI;

    return irradiance;
}

float3 DDGIGetVolumeIrradianceWithProbeState(
    float3 worldPosition,
    float3 surfaceBias,
    float3 direction,
    DDGIVolumeParams volume,
    Texture2D<float4> irradianceAtlas,
    Texture2D<float2> distanceAtlas,
    Texture2D<float4> probeData,
    int probeDataWidth)
{
    float3 irradiance = float3(0.0, 0.0, 0.0);
    float accumulatedWeights = 0.0;

    float3 biasedWorldPosition = worldPosition + surfaceBias;

    int3 baseProbeCoords = DDGIGetBaseProbeGridCoords(biasedWorldPosition, volume);

    float3 baseProbeWorldPosition = DDGIGetProbeWorldPosition(baseProbeCoords, volume);

    float3 gridSpaceDistance = biasedWorldPosition - baseProbeWorldPosition;
    float3 alpha = saturate(gridSpaceDistance / volume.probeSpacing);

    [unroll]
    for (int probeIdx = 0; probeIdx < 8; probeIdx++)
    {

        int3 adjacentProbeOffset = int3(probeIdx, probeIdx >> 1, probeIdx >> 2) & int3(1, 1, 1);

        int3 adjacentProbeCoords = clamp(
            baseProbeCoords + adjacentProbeOffset,
            int3(0, 0, 0),
            volume.probeCounts - int3(1, 1, 1)
        );

        if (!DDGIIsProbeCoordValid(adjacentProbeCoords, volume))
            continue;

        int adjacentProbeIndex = DDGIGetProbeLinearIndex(adjacentProbeCoords, volume);

        float3 relocationOffset = float3(0.0, 0.0, 0.0);
        if (probeDataWidth > 0)
        {
            int2 probeDataCoord = int2(adjacentProbeIndex % probeDataWidth, adjacentProbeIndex / probeDataWidth);
            float4 probeDataValue = probeData.Load(int3(probeDataCoord, 0));

            if ((int)probeDataValue.w == DDGI_PROBE_STATE_INACTIVE)
                continue;

            relocationOffset = probeDataValue.xyz;
        }

        float3 adjacentProbeWorldPosition = DDGIGetProbeWorldPosition(adjacentProbeCoords, volume) + relocationOffset;

        float3 worldPosToAdjProbe = normalize(adjacentProbeWorldPosition - worldPosition);
        float3 biasedPosToAdjProbe = adjacentProbeWorldPosition - biasedWorldPosition;
        float biasedPosToAdjProbeDist = length(biasedPosToAdjProbe);
        biasedPosToAdjProbe = biasedPosToAdjProbe / max(biasedPosToAdjProbeDist, 0.0001);

        float3 trilinear = max(0.001, lerp(1.0 - alpha, alpha, float3(adjacentProbeOffset)));
        float trilinearWeight = trilinear.x * trilinear.y * trilinear.z;
        float weight = 1.0;

        float wrapShading = (dot(worldPosToAdjProbe, direction) + 1.0) * 0.5;
        weight *= (wrapShading * wrapShading) + 0.2;

        float2 distanceUV = DDGIGetProbeUV(
            adjacentProbeIndex,
            -biasedPosToAdjProbe,
            volume.distanceProbeRes,
            volume.probesPerRow,
            volume.distanceTexelSize
        );

        float2 filteredDistance = 2.0 * distanceAtlas.SampleLevel(sampler_LinearClamp, distanceUV, 0).rg;
        float variance = abs(filteredDistance.x * filteredDistance.x - filteredDistance.y);

        float chebyshevWeight = 1.0;
        if (biasedPosToAdjProbeDist > filteredDistance.x)
        {
            float v = biasedPosToAdjProbeDist - filteredDistance.x;
            chebyshevWeight = variance / (variance + v * v);
            chebyshevWeight = max(chebyshevWeight * chebyshevWeight * chebyshevWeight, 0.0);
        }

        weight *= max(0.05, chebyshevWeight);
        weight = max(0.000001, weight);

        const float crushThreshold = 0.2;
        if (weight < crushThreshold)
        {
            weight *= (weight * weight) * (1.0 / (crushThreshold * crushThreshold));
        }

        weight *= trilinearWeight;

        float2 irradianceUV = DDGIGetProbeUV(
            adjacentProbeIndex,
            direction,
            volume.irradianceProbeRes,
            volume.probesPerRow,
            volume.irradianceTexelSize
        );

        float3 probeIrradiance = irradianceAtlas.SampleLevel(sampler_LinearClamp, irradianceUV, 0).rgb;
        float3 exponent = volume.irradianceGamma * 0.5;
        probeIrradiance = pow(max(probeIrradiance, 0.0001), exponent);

        irradiance += weight * probeIrradiance;
        accumulatedWeights += weight;
    }

    if (accumulatedWeights == 0.0)
        return float3(0.0, 0.0, 0.0);

    irradiance *= (1.0 / accumulatedWeights);
    irradiance *= irradiance;
    irradiance *= DDGI_TWO_PI;
    return irradiance;
}

float3 DDGISampleIrradiance(
    float3 worldPosition,
    float3 surfaceNormal,
    float3 viewDirection,
    DDGIVolumeParams volume,
    Texture2D<float4> irradianceAtlas,
    Texture2D<float2> distanceAtlas,
    SamplerState atlasSampler)
{
    float3 surfaceBias = DDGIGetSurfaceBias(surfaceNormal, viewDirection, volume);
    return DDGIGetVolumeIrradiance(
        worldPosition,
        surfaceBias,
        surfaceNormal,
        volume,
        irradianceAtlas,
        distanceAtlas,
        atlasSampler
    );
}

float3 DDGISampleProbeIrradiance(
    float2 baseUV,
    float3 direction,
    float probeRes,
    float2 texelSize,
    float gamma,
    Texture2D<float4> irradianceAtlas,
    SamplerState atlasSampler)
{
    float2 octUV = DDGIOctahedronEncode(direction);
    float2 sampleUV = baseUV + octUV * probeRes * texelSize;

    float3 irradiance = irradianceAtlas.SampleLevel(atlasSampler, sampleUV, 0).rgb;

    float3 exponent = gamma * 0.5;
    irradiance = pow(max(irradiance, 0.0001), exponent);
    irradiance *= irradiance;
    irradiance *= DDGI_TWO_PI;

    return irradiance;
}

float3 DDGISampleProbeIrradiance_Legacy(
    float2 baseUV,
    float3 direction,
    float probeRes,
    float2 texelSize,
    float gamma,
    sampler2D irradianceAtlas)
{
    float2 octUV = DDGIOctahedronEncode(direction);
    float2 sampleUV = baseUV + octUV * probeRes * texelSize;

    float3 irradiance = tex2Dlod(irradianceAtlas, float4(sampleUV, 0, 0)).rgb;

    float3 exponent = gamma * 0.5;
    irradiance = pow(max(irradiance, 0.0001), exponent);
    irradiance *= irradiance;
    irradiance *= DDGI_TWO_PI;

    return irradiance;
}

float3 DDGISampleProbeIrradiance_Visualization(
    float2 baseUV,
    float3 direction,
    float probeRes,
    float2 texelSize,
    float gamma,
    sampler2D irradianceAtlas)
{
    float2 octUV = DDGIOctahedronEncode(direction);
    float2 sampleUV = baseUV + octUV * probeRes * texelSize;

    float3 irradiance = tex2Dlod(irradianceAtlas, float4(sampleUV, 0, 0)).rgb;

    float3 exponent = gamma * 0.5;
    irradiance = pow(max(irradiance, 0.0001), exponent);
    irradiance *= irradiance;

    return irradiance;
}

float2 DDGISampleProbeDistance(
    float2 baseUV,
    float3 direction,
    float probeRes,
    float2 texelSize,
    Texture2D<float2> distanceAtlas,
    SamplerState atlasSampler)
{
    float2 octUV = DDGIOctahedronEncode(direction);
    float2 sampleUV = baseUV + octUV * probeRes * texelSize;

    return 2.0 * distanceAtlas.SampleLevel(atlasSampler, sampleUV, 0).rg;
}

float2 DDGISampleProbeDistance_Legacy(
    float2 baseUV,
    float3 direction,
    float probeRes,
    float2 texelSize,
    sampler2D distanceAtlas)
{
    float2 octUV = DDGIOctahedronEncode(direction);
    float2 sampleUV = baseUV + octUV * probeRes * texelSize;

    return 2.0 * tex2Dlod(distanceAtlas, float4(sampleUV, 0, 0)).rg;
}

#endif // DDGI_SAMPLING_HLSL
