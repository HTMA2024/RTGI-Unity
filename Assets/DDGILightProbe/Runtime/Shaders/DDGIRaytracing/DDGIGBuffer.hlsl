#ifndef DDGI_GBUFFER_HLSL
#define DDGI_GBUFFER_HLSL

struct DDGIHitResult
{
    float3 position;
    float  hitDistance;
    float3 normal;
    uint   hitFlag;
    float3 albedo;
    float  roughness;
    float3 emission;
    float  metallic;
};

struct DDGIRayPayload
{
    float3 position;
    float  hitDistance;
    float3 normal;
    uint   hitFlag;
    float3 albedo;
    float  roughness;
    float3 emission;
    float  metallic;
};

DDGIRayPayload InitDDGIRayPayload()
{
    DDGIRayPayload payload;
    payload.position = float3(0, 0, 0);
    payload.hitDistance = -1.0;
    payload.normal = float3(0, 1, 0);
    payload.hitFlag = 0;
    payload.albedo = float3(0, 0, 0);
    payload.roughness = 1.0;
    payload.emission = float3(0, 0, 0);
    payload.metallic = 0.0;
    return payload;
}

uint2 GetGBufferTexelCoord(uint probeIndex, uint rayIndex, uint raysPerProbe, uint textureWidth)
{
    uint linearIndex = probeIndex * raysPerProbe + rayIndex;
    uint x = linearIndex % textureWidth;
    uint y = linearIndex / textureWidth;
    return uint2(x, y);
}

void GetProbeRayFromTexelCoord(uint2 texelCoord, uint raysPerProbe, uint textureWidth,
                                out uint probeIndex, out uint rayIndex)
{
    uint linearIndex = texelCoord.y * textureWidth + texelCoord.x;
    probeIndex = linearIndex / raysPerProbe;
    rayIndex = linearIndex % raysPerProbe;
}

#define DDGI_PI 3.14159265359
#define DDGI_TWO_PI 6.28318530718
#define DDGI_GOLDEN_RATIO 1.61803398875

float3 FibonacciSphereDirection(uint rayIndex, uint totalRays)
{

    float goldenAngle = DDGI_TWO_PI / (DDGI_GOLDEN_RATIO * DDGI_GOLDEN_RATIO);

    float theta = goldenAngle * rayIndex;

    float cosTheta = 1.0 - (2.0 * rayIndex + 1.0) / (float)totalRays;
    float sinTheta = sqrt(saturate(1.0 - cosTheta * cosTheta));

    return float3(
        cos(theta) * sinTheta,
        sin(theta) * sinTheta,
        cosTheta
    );
}

float3 FibonacciSphereDirectionRotated(uint rayIndex, uint totalRays, float rotationAngle)
{
    float3 dir = FibonacciSphereDirection(rayIndex, totalRays);

    float cosR = cos(rotationAngle);
    float sinR = sin(rotationAngle);

    return float3(
        dir.x * cosR - dir.y * sinR,
        dir.x * sinR + dir.y * cosR,
        dir.z
    );
}

uint WangHashDDGI(inout uint seed)
{
    seed = (seed ^ 61) ^ (seed >> 16);
    seed *= 9;
    seed = seed ^ (seed >> 4);
    seed *= 0x27d4eb2d;
    seed = seed ^ (seed >> 15);
    return seed;
}

float RandomFloat01DDGI(inout uint seed)
{
    return float(WangHashDDGI(seed)) / float(0xFFFFFFFF);
}

float3 RandomUnitVectorDDGI(inout uint seed)
{
    float z = RandomFloat01DDGI(seed) * 2.0 - 1.0;
    float a = RandomFloat01DDGI(seed) * DDGI_TWO_PI;
    float r = sqrt(1.0 - z * z);
    return float3(r * cos(a), r * sin(a), z);
}

float3 CosineSampleHemisphere(float2 u, float3 normal)
{
    float r = sqrt(u.x);
    float phi = DDGI_TWO_PI * u.y;

    float3 localDir = float3(r * cos(phi), r * sin(phi), sqrt(1.0 - u.x));

    float3 up = abs(normal.z) < 0.999 ? float3(0, 0, 1) : float3(1, 0, 0);
    float3 tangent = normalize(cross(up, normal));
    float3 bitangent = cross(normal, tangent);

    return tangent * localDir.x + bitangent * localDir.y + normal * localDir.z;
}

#endif // DDGI_GBUFFER_HLSL
