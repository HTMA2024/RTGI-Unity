#ifndef SSGI_COMMON_HLSL
#define SSGI_COMMON_HLSL

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UnityGBuffer.hlsl"

#ifndef UNITY_MATRIX_VP
float4x4 unity_MatrixVP;
float4x4 unity_MatrixInvVP;
#define UNITY_MATRIX_VP    unity_MatrixVP
#define UNITY_MATRIX_I_VP  unity_MatrixInvVP
#endif

CBUFFER_START(UnityScreenSpaceGlobalIllumination)
    int   _RayMarchingSteps;
    float _RayMarchingThicknessScale;
    float _RayMarchingThicknessBias;
    int   _RayMarchingReflectsSky;

    int   _RayMarchingFallbackHierarchy;
    int   _IndirectDiffuseFrameIndex;
    float _RayMarchingLowResPercentageInv;
    float _SSGIUnused0;

    float4 _SSGIScreenSize;
CBUFFER_END

Texture2D<float4> _CameraNormalsTexture;
Texture2D<float>  _BlueNoiseTexture;
Texture2D<float4> _CameraMotionVectorsTexture;

#ifdef _SSGI_ACCURATE_NORMALS

float3 SSGIUnpackNormalOctQuadEncode(float3 f)
{
    half2 remappedOctNormalWS = half2(Unpack888ToFloat2(f));
    half2 octNormalWS = remappedOctNormalWS.xy * half(2.0) - half(1.0);
    return half3(UnpackNormalOctQuadEncode(octNormalWS));
}

float3 DecodeNormalURP(uint2 coord)
{
    float3 rg = LOAD_TEXTURE2D(_CameraNormalsTexture, coord);
    return SSGIUnpackNormalOctQuadEncode(rg);
}

#else

float3 DecodeNormalURP(uint2 coord)
{
    return LOAD_TEXTURE2D(_CameraNormalsTexture, coord).xyz * 2.0 - 1.0;
}

#endif // _SSGI_ACCURATE_NORMALS

Texture2D<float> _OwenScrambledTexture;
Texture2D<float> _ScramblingTileXSPP;
Texture2D<float> _RankingTileXSPP;

float GetBNDSequenceSample(uint2 pixelCoord, uint sampleIndex, uint sampleDimension)
{

    pixelCoord = pixelCoord & 127;
    sampleIndex = sampleIndex & 255;
    sampleDimension = sampleDimension & 255;

    uint rankingIndex = (pixelCoord.x + pixelCoord.y * 128) * 8 + (sampleDimension & 7);
    uint rankedSampleIndex = sampleIndex ^ clamp((uint)(_RankingTileXSPP[uint2(rankingIndex & 127, rankingIndex >> 7)] * 256.0), 0, 255);

    uint value = clamp((uint)(_OwenScrambledTexture[uint2(sampleDimension, rankedSampleIndex)] * 256.0), 0, 255);

    uint scramblingIndex = (pixelCoord.x + pixelCoord.y * 128) * 8 + (sampleDimension & 7);
    float scramblingValue = min(_ScramblingTileXSPP[uint2(scramblingIndex & 127, scramblingIndex >> 7)], 0.999);
    value = value ^ uint(scramblingValue * 256.0);

    return (scramblingValue + value) * rcp(256.0);
}

float SampleBlueNoise(uint2 coord, int frameIndex, int dimension)
{
    uint2 wrappedCoord = (coord + uint2(frameIndex * 7u, frameIndex * 11u + dimension * 37u)) & 127u;
    return LOAD_TEXTURE2D(_BlueNoiseTexture, wrappedCoord).r;
}

float EdgeOfScreenFade(float2 coordNDC, float fadeRcpLength)
{
    float2 coordCS = coordNDC * 2.0 - 1.0;
    float2 t = Remap10(abs(coordCS), fadeRcpLength, fadeRcpLength);
    return Smoothstep01(t.x) * Smoothstep01(t.y);
}

uint2 GetLowResCoord(uint2 inputCoord)
{
    return min((uint2)round((float2)(inputCoord + 0.5f) * _RayMarchingLowResPercentageInv),
               (uint2)_SSGIScreenSize.xy - 1u);
}

#endif // SSGI_COMMON_HLSL
