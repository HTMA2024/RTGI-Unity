#ifndef DDGI_CLOSEST_HIT_HLSL
#define DDGI_CLOSEST_HIT_HLSL

#include "DDGIGBuffer.hlsl"

#include "UnityRayTracingMeshUtils.cginc"

struct AttributeData
{
    float2 barycentrics;
};

Texture2D _BaseMap;
SamplerState sampler_BaseMap;
Texture2D _NormalTex;
SamplerState sampler_NormalTex;
float4 _Albedo;
float4 _BaseMap_ST;
float4 _NormalTex_ST;
float _NormalScale;
float4 _Emission;
float _Metallic;
float _Smoothness;

#define DDGI_CHS_MODE
#include "Assets/URPSSGI/Shaders/UnifiedClosestHit.hlsl"

[shader("closesthit")]
void DDGIShadowClosestHitShader(inout DDGIRayPayload payload : SV_RayPayload,
                                 BuiltInTriangleIntersectionAttributes attribs : SV_IntersectionAttributes)
{

    payload.hitFlag = 1;
    payload.hitDistance = RayTCurrent();
}

[shader("anyhit")]
void DDGIAnyHitShader(inout DDGIRayPayload payload : SV_RayPayload,
                      BuiltInTriangleIntersectionAttributes attribs : SV_IntersectionAttributes)
{

    AcceptHitAndEndSearch();
}

#endif // DDGI_CLOSEST_HIT_HLSL
