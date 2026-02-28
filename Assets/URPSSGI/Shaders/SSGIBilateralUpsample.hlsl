#ifndef SSGI_BILATERAL_UPSAMPLE_HLSL
#define SSGI_BILATERAL_UPSAMPLE_HLSL

#define _UpsampleTolerance 1e-5f
#define _NoiseFilterStrength 0.99999999f

float3 BilUpColor3WithWeight(float HiDepth, float4 LowDepths,
    float3 lowValue0, float3 lowValue1, float3 lowValue2, float3 lowValue3,
    float4 initialWeight)
{
    float4 weights = initialWeight * float4(9, 3, 1, 3) * rcp(abs(HiDepth - LowDepths) + _UpsampleTolerance);
    float TotalWeight = dot(weights, 1) + _NoiseFilterStrength;
    float3 WeightedSum = lowValue0 * weights.x
                        + lowValue1 * weights.y
                        + lowValue2 * weights.z
                        + lowValue3 * weights.w
                        + _NoiseFilterStrength;
    return WeightedSum * rcp(TotalWeight);
}

float3 BilUpColor3(float HiDepth, float4 LowDepths,
    float3 lowValue0, float3 lowValue1, float3 lowValue2, float3 lowValue3)
{
    float4 weights = float4(9, 3, 1, 3) * rcp(abs(HiDepth - LowDepths) + _UpsampleTolerance);
    float TotalWeight = dot(weights, 1) + _NoiseFilterStrength;
    float3 WeightedSum = lowValue0 * weights.x
                        + lowValue1 * weights.y
                        + lowValue2 * weights.z
                        + lowValue3 * weights.w
                        + _NoiseFilterStrength;
    return WeightedSum * rcp(TotalWeight);
}

#endif // SSGI_BILATERAL_UPSAMPLE_HLSL
