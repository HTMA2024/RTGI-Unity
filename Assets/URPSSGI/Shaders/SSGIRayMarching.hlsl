#ifndef SSGI_RAY_MARCHING_HLSL
#define SSGI_RAY_MARCHING_HLSL

Texture2D<float> _DepthPyramidTexture;

StructuredBuffer<int2> _DepthPyramidMipLevelOffsets;

#define RAY_TRACE_EPS 0.00024414

bool RayMarch(float3 positionWS, float3 sampleDir, float3 normalWS,
              float2 positionSS, float deviceDepth, bool killRay, out float3 rayPos)
{
    rayPos = float3(-1.0, -1.0, -1.0);
    bool status = false;

    float3 rayOrigin = float3(positionSS + 0.5, deviceDepth);

    float3 reflPosWS  = positionWS + sampleDir;
    float3 reflPosNDC = ComputeNormalizedDeviceCoordinatesWithZ(reflPosWS, UNITY_MATRIX_VP);
    float3 reflPosSS  = float3(reflPosNDC.xy * _SSGIScreenSize.xy, reflPosNDC.z);
    float3 rayDir     = reflPosSS - rayOrigin;
    float3 rcpRayDir  = rcp(rayDir);
    int2   rayStep    = int2(rcpRayDir.x >= 0 ? 1 : 0,
                             rcpRayDir.y >= 0 ? 1 : 0);
    float3 raySign    = float3(rcpRayDir.x >= 0 ? 1 : -1,
                               rcpRayDir.y >= 0 ? 1 : -1,
                               rcpRayDir.z >= 0 ? 1 : -1);
    bool rayTowardsEye = rcpRayDir.z >= 0;

    killRay = killRay || (reflPosSS.z <= 0);

    if (!killRay)
    {
        float tMax;
        {
            const float halfTexel = 0.5;
            float3 bounds;
            bounds.x = (rcpRayDir.x >= 0) ? _SSGIScreenSize.x - halfTexel : halfTexel;
            bounds.y = (rcpRayDir.y >= 0) ? _SSGIScreenSize.y - halfTexel : halfTexel;
            float maxDepth = (_RayMarchingReflectsSky != 0) ? -0.00000024 : 0.00000024;
            bounds.z = (rcpRayDir.z >= 0) ? 1 : maxDepth;

            float3 dist = bounds * rcpRayDir - (rayOrigin * rcpRayDir);
            tMax = Min3(dist.x, dist.y, dist.z);
        }

        float t;
        {
            float2 dist = abs(0.5 * rcpRayDir.xy);
            t = min(dist.x, dist.y);
        }

        int  mipLevel  = 0;
        int  iterCount = 0;
        bool hit       = false;
        bool miss      = false;
        bool belowMip0 = false;

        while (!(hit || miss) && (t <= tMax) && (iterCount < _RayMarchingSteps))
        {
            rayPos = rayOrigin + t * rayDir;

            float2 sgnEdgeDist = round(rayPos.xy) - rayPos.xy;
            float2 satEdgeDist = clamp(raySign.xy * sgnEdgeDist + RAY_TRACE_EPS, 0, RAY_TRACE_EPS);
            rayPos.xy += raySign.xy * satEdgeDist;

            int2 mipCoord  = (int2)rayPos.xy >> mipLevel;
            int2 mipOffset = _DepthPyramidMipLevelOffsets[mipLevel];

            float4 bounds;

            bounds.z = _DepthPyramidTexture[mipOffset + mipCoord];
            bounds.xy = (mipCoord + rayStep) << mipLevel;

            bounds.w = bounds.z * _RayMarchingThicknessScale + _RayMarchingThicknessBias;

            float4 dist      = bounds * rcpRayDir.xyzz - (rayOrigin.xyzz * rcpRayDir.xyzz);
            float  distWall  = min(dist.x, dist.y);
            float  distFloor = dist.z;
            float  distBase  = dist.w;

            bool belowFloor  = rayPos.z  < bounds.z;
            bool aboveBase   = rayPos.z >= bounds.w;
            bool insideFloor = belowFloor && aboveBase;
            bool hitFloor    = (t <= distFloor) && (distFloor <= distWall);

            miss      = belowMip0 && insideFloor;
            hit       = (mipLevel == 0) && (hitFloor || insideFloor);
            belowMip0 = (mipLevel == 0) && belowFloor;

            t = hitFloor ? distFloor : (((mipLevel != 0) && belowFloor) ? t : distWall);
            rayPos.z = bounds.z;

            mipLevel += (hitFloor || belowFloor || rayTowardsEye) ? -1 : 1;
            mipLevel  = clamp(mipLevel, 0, 6);

            iterCount++;
        }

        miss = miss || ((_RayMarchingReflectsSky == 0) && (rayPos.z == 0));
        status = hit && !miss;
    }
    return status;
}

#endif // SSGI_RAY_MARCHING_HLSL
