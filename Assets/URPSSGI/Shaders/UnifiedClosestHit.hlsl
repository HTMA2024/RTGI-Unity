#ifndef UNIFIED_CLOSEST_HIT_HLSL
#define UNIFIED_CLOSEST_HIT_HLSL

struct UnifiedHitSurface
{
    float3 positionWS;
    float3 normalWS;
    float3 geometricNormalWS;
    float2 uv;
    float3 albedo;
    float3 emission;
    float  metallic;
    float  smoothness;
    float  hitDistance;
    bool   isFrontFace;
};

UnifiedHitSurface GetHitSurface(AttributeData attribs)
{
    UnifiedHitSurface surf;

    uint3 idx = UnityRayTracingFetchTriangleIndices(PrimitiveIndex());

    float3 n0 = UnityRayTracingFetchVertexAttribute3(idx.x, kVertexAttributeNormal);
    float3 n1 = UnityRayTracingFetchVertexAttribute3(idx.y, kVertexAttributeNormal);
    float3 n2 = UnityRayTracingFetchVertexAttribute3(idx.z, kVertexAttributeNormal);

    float4 t0 = UnityRayTracingFetchVertexAttribute4(idx.x, kVertexAttributeTangent);
    float4 t1 = UnityRayTracingFetchVertexAttribute4(idx.y, kVertexAttributeTangent);
    float4 t2 = UnityRayTracingFetchVertexAttribute4(idx.z, kVertexAttributeTangent);

    float2 uv0 = UnityRayTracingFetchVertexAttribute2(idx.x, kVertexAttributeTexCoord0);
    float2 uv1 = UnityRayTracingFetchVertexAttribute2(idx.y, kVertexAttributeTexCoord0);
    float2 uv2 = UnityRayTracingFetchVertexAttribute2(idx.z, kVertexAttributeTexCoord0);

    float3 bary = float3(
        1.0 - attribs.barycentrics.x - attribs.barycentrics.y,
        attribs.barycentrics.x,
        attribs.barycentrics.y);

    float3 normalOS = n0 * bary.x + n1 * bary.y + n2 * bary.z;

    float4 tangentOS = t0 * bary.x + t1 * bary.y + t2 * bary.z;

    surf.uv = uv0 * bary.x + uv1 * bary.y + uv2 * bary.z;

    float3 normalWS  = normalize(mul(normalOS, (float3x3)WorldToObject3x4()));
    float3 tangentWS = normalize(mul(tangentOS.xyz, (float3x3)WorldToObject3x4()));

    float3 bitangentWS = cross(normalWS, tangentWS) * sign(tangentOS.w);

    surf.geometricNormalWS = normalWS;

    float2 normalUV = surf.uv * _NormalTex_ST.xy + _NormalTex_ST.zw;
    float4 normalMapSample = _NormalTex.SampleLevel(sampler_NormalTex, normalUV, 0);

    float3 normalTS;
    normalTS.xy = normalMapSample.ag * 2.0 - 1.0;
    normalTS.xy *= _NormalScale;

    normalTS.z = sqrt(saturate(1.0 - normalTS.x * normalTS.x - normalTS.y * normalTS.y));

    surf.normalWS = normalize(tangentWS * normalTS.x + bitangentWS * normalTS.y + normalWS * normalTS.z);

    float2 baseUV = surf.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
    surf.albedo   = _BaseMap.SampleLevel(sampler_BaseMap, baseUV, 0).rgb * _Albedo.rgb;
    surf.emission = _Emission.rgb;
    surf.metallic   = _Metallic;
    surf.smoothness = _Smoothness;

    surf.positionWS = WorldRayOrigin() + WorldRayDirection() * RayTCurrent();
    surf.hitDistance = RayTCurrent();
    surf.isFrontFace = HitKind() == HIT_KIND_TRIANGLE_FRONT_FACE;

    return surf;
}

#ifdef DDGI_CHS_MODE
[shader("closesthit")]
void ClosestHitMain(inout DDGIRayPayload payload : SV_RayPayload,
                    AttributeData attribs : SV_IntersectionAttributes)
{
    UnifiedHitSurface surf = GetHitSurface(attribs);

    payload.position = surf.positionWS;
    payload.normal = surf.isFrontFace ? surf.normalWS : -surf.normalWS;
    payload.albedo = surf.albedo;
    payload.emission = surf.emission;
    payload.roughness = 1.0 - surf.smoothness;
    payload.metallic = surf.metallic;

    if (surf.isFrontFace)
    {
        payload.hitDistance = surf.hitDistance;
        payload.hitFlag = 1;
    }
    else
    {
        payload.hitDistance = -surf.hitDistance * 0.2;
        payload.hitFlag = 2;
    }
}
#endif // DDGI_CHS_MODE

#endif // UNIFIED_CLOSEST_HIT_HLSL
