#ifndef DDGI_MISS_HLSL
#define DDGI_MISS_HLSL

#include "DDGIGBuffer.hlsl"

TextureCube<float4> _DDGISkybox;
SamplerState sampler_DDGISkybox;

float4 _DDGIAmbientColor;
float _DDGISkyboxIntensity;
float _DDGIMaxRayDistance;

[shader("miss")]
void DDGIMissShader(inout DDGIRayPayload payload : SV_RayPayload)
{
    float3 rayDir = WorldRayDirection();

    float3 skyColor = _DDGISkybox.SampleLevel(sampler_DDGISkybox, rayDir, 0).rgb;
    skyColor *= _DDGISkyboxIntensity;

    payload.hitFlag = 0;
    payload.hitDistance = _DDGIMaxRayDistance;
    payload.position = WorldRayOrigin() + rayDir * _DDGIMaxRayDistance;
    payload.normal = -rayDir;
    payload.albedo = float3(0, 0, 0);
    payload.emission = skyColor;
    payload.roughness = 1.0;
    payload.metallic = 0.0;
}

[shader("miss")]
void DDGIShadowMissShader(inout DDGIRayPayload payload : SV_RayPayload)
{

    payload.hitFlag = 0;
    payload.hitDistance = -1.0;
}

#endif // DDGI_MISS_HLSL
