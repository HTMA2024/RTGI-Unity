Shader "DDGI/ProbeVisualization"
{
    Properties
    {
        _IrradianceAtlas ("Irradiance Atlas", 2D) = "black" {}
        _DistanceAtlas ("Distance Atlas", 2D) = "white" {}
        _ProbeRadius ("Probe Radius", Range(0.01, 1.0)) = 0.2
        _Intensity ("Intensity", Range(0.1, 5.0)) = 1.0
        _VisualizationMode ("Visualization Mode", Int) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 100

        Pass
        {
            Name "ProbeVisualization"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma target 4.5

            #include "UnityCG.cginc"
            #include "DDGISampling.hlsl"

            sampler2D _IrradianceAtlas;
            sampler2D _DistanceAtlas;

            Texture2D<float4> _GBuffer_PositionDistance;
            Texture2D<float4> _GBuffer_NormalHitFlag;
            Texture2D<float4> _GBuffer_AlbedoRoughness;
            Texture2D<float4> _GBuffer_EmissionMetallic;
            SamplerState sampler_point_clamp;

            Texture2D<float4> _DirectIrradianceBuffer;
            Texture2D<float4> _IndirectIrradianceBuffer;
            Texture2D<float4> _RadianceBuffer;

            float4 _IrradianceAtlasParams;
            float4 _DistanceAtlasParams;

            float _IrradianceGamma;

            uint _GBufferWidth;
            uint _GBufferHeight;
            uint _RaysPerProbe;
            uint _FixedRayCount;

            float4x4 _RayRotationMatrix;

            Texture2D<float4> _ProbeData;
            uint _ProbeDataWidth;
            float3 _ProbeSpacing;
            float _BackfaceThreshold;
            float _MinFrontfaceDistance;

            Texture2D<float> _ProbeVariability;

            float _ProbeRadius;
            float _Intensity;
            int _VisualizationMode;

            #if defined(UNITY_INSTANCING_ENABLED)
            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _ProbePosition)
                UNITY_DEFINE_INSTANCED_PROP(float4, _ProbeAtlasUV)
                UNITY_DEFINE_INSTANCED_PROP(float4, _ProbeColor)
            UNITY_INSTANCING_BUFFER_END(Props)
            #endif

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 normal : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                float3 localNormal : TEXCOORD2;
                float2 irradianceUV : TEXCOORD3;
                float2 distanceUV : TEXCOORD4;
                float4 probeColor : TEXCOORD5;
                nointerpolation float probeIndex : TEXCOORD6;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float3 FibonacciSphereDirection(uint rayIndex, uint totalRays)
            {
                float goldenRatio = 1.61803398875;
                float goldenAngle = 6.28318530718 / (goldenRatio * goldenRatio);
                float theta = goldenAngle * rayIndex;
                float cosTheta = 1.0 - (2.0 * rayIndex + 1.0) / (float)totalRays;
                float sinTheta = sqrt(saturate(1.0 - cosTheta * cosTheta));

                return float3(
                    cos(theta) * sinTheta,
                    sin(theta) * sinTheta,
                    cosTheta
                );
            }

            float3 GetRayDirection(uint rayIndex)
            {

                bool isFixedRay = (rayIndex < _FixedRayCount);

                uint sampleIndex = isFixedRay ? rayIndex : (rayIndex - _FixedRayCount);
                uint numRays = isFixedRay ? _FixedRayCount : (_RaysPerProbe - _FixedRayCount);

                float3 dir = FibonacciSphereDirection(sampleIndex, numRays);

                if (isFixedRay)
                    return normalize(dir);

                dir = mul((float3x3)_RayRotationMatrix, dir);
                return normalize(dir);
            }

            uint FindClosestRayIndex(float3 direction, uint probeIndex)
            {
                float bestDot = -1.0;
                uint bestIndex = 0;

                for (uint i = 0; i < _RaysPerProbe; i++)
                {

                    float3 rayDir = GetRayDirection(i);

                    float d = dot(direction, rayDir);
                    if (d > bestDot)
                    {
                        bestDot = d;
                        bestIndex = i;
                    }
                }

                return bestIndex;
            }

            float4 SampleGBuffer(Texture2D<float4> buffer, uint probeIndex, uint rayIndex)
            {
                uint linearIndex = probeIndex * _RaysPerProbe + rayIndex;
                uint2 coord = uint2(linearIndex % _GBufferWidth, linearIndex / _GBufferWidth);
                return buffer.Load(int3(coord, 0));
            }

            float4 SampleProbeData(uint probeIndex)
            {
                uint2 coord = uint2(probeIndex % _ProbeDataWidth, probeIndex / _ProbeDataWidth);
                return _ProbeData.Load(int3(coord, 0));
            }

            float CalculateBackfaceRatio(uint probeIndex)
            {
                float backfaceCount = 0.0;
                for (uint i = 0; i < _RaysPerProbe; i++)
                {
                    float4 normalHitFlag = SampleGBuffer(_GBuffer_NormalHitFlag, probeIndex, i);
                    float4 positionDistance = SampleGBuffer(_GBuffer_PositionDistance, probeIndex, i);

                    if (normalHitFlag.w > 1.5 || positionDistance.w < 0)
                    {
                        backfaceCount += 1.0;
                    }
                }
                return backfaceCount / _RaysPerProbe;
            }

            float CalculateClosestFrontfaceDistance(uint probeIndex)
            {
                float closestDist = 1e27;
                for (uint i = 0; i < _RaysPerProbe; i++)
                {
                    float4 normalHitFlag = SampleGBuffer(_GBuffer_NormalHitFlag, probeIndex, i);
                    float4 positionDistance = SampleGBuffer(_GBuffer_PositionDistance, probeIndex, i);

                    if (normalHitFlag.w > 0.5 && normalHitFlag.w < 1.5 && positionDistance.w > 0)
                    {
                        closestDist = min(closestDist, positionDistance.w);
                    }
                }
                return closestDist;
            }

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);

                #if defined(UNITY_INSTANCING_ENABLED)
                float4 probePos = UNITY_ACCESS_INSTANCED_PROP(Props, _ProbePosition);
                float4 atlasUV = UNITY_ACCESS_INSTANCED_PROP(Props, _ProbeAtlasUV);
                float4 probeColor = UNITY_ACCESS_INSTANCED_PROP(Props, _ProbeColor);
                #else
                float4 probePos = float4(0, 0, 0, 0);
                float4 atlasUV = float4(0, 0, 0, 0);
                float4 probeColor = float4(1, 1, 1, 1);
                #endif

                float3 probeOffset = float3(0, 0, 0);
                if (_ProbeDataWidth > 0)
                {
                    uint probeIndex = (uint)probePos.w;
                    uint2 probeDataCoord = uint2(probeIndex % _ProbeDataWidth, probeIndex / _ProbeDataWidth);
                    probeOffset = _ProbeData.Load(int3(probeDataCoord, 0)).xyz;
                }

                float3 actualProbePos = probePos.xyz + probeOffset;
                float3 worldPos = v.vertex.xyz * _ProbeRadius + actualProbePos;

                o.pos = mul(UNITY_MATRIX_VP, float4(worldPos, 1.0));
                o.normal = v.normal;
                o.worldPos = worldPos;
                o.localNormal = v.normal;
                o.irradianceUV = atlasUV.xy;
                o.distanceUV = atlasUV.zw;
                o.probeColor = probeColor;
                o.probeIndex = probePos.w;

                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);

                float3 normal = normalize(i.localNormal);
                float3 color = float3(0, 0, 0);
                uint probeIndex = (uint)i.probeIndex;

                if (_VisualizationMode == 0)
                {

                    color = DDGISampleProbeIrradiance_Visualization(
                        i.irradianceUV,
                        normal,
                        _IrradianceAtlasParams.z,
                        _IrradianceAtlasParams.xy,
                        _IrradianceGamma,
                        _IrradianceAtlas
                    );
                    color *= _Intensity;
                }
                else if (_VisualizationMode == 1)
                {

                    float2 dist = DDGISampleProbeDistance_Legacy(
                        i.distanceUV,
                        normal,
                        _DistanceAtlasParams.z,
                        _DistanceAtlasParams.xy,
                        _DistanceAtlas
                    );
                    float normalizedDist = saturate(dist.x / 50.0);
                    color = float3(normalizedDist, normalizedDist, 1.0 - normalizedDist);
                }
                else if (_VisualizationMode == 2)
                {

                    color = normal * 0.5 + 0.5;
                }
                else if (_VisualizationMode == 3)
                {

                    color = i.probeColor.rgb;
                }

                else if (_VisualizationMode >= 10 && _VisualizationMode < 20 && _RaysPerProbe > 0)
                {
                    uint rayIndex = FindClosestRayIndex(normal, probeIndex);

                    if (_VisualizationMode == 10)
                    {

                        float4 positionDistance = SampleGBuffer(_GBuffer_PositionDistance, probeIndex, rayIndex);
                        float dist = positionDistance.w;
                        if (dist > 0)
                        {
                            float normalizedDist = saturate(dist / 50.0);
                            color = float3(1.0 - normalizedDist, normalizedDist * 0.5, normalizedDist);
                        }
                        else
                        {
                            color = float3(0.1, 0.1, 0.3);
                        }
                    }
                    else if (_VisualizationMode == 11)
                    {

                        float4 normalHitFlag = SampleGBuffer(_GBuffer_NormalHitFlag, probeIndex, rayIndex);

                        color = normalHitFlag.w > 0.5
                            ? normalHitFlag.xyz * 0.5 + 0.5
                            : float3(0.1, 0.1, 0.3);
                    }
                    else if (_VisualizationMode == 12)
                    {

                        float4 albedoRoughness = SampleGBuffer(_GBuffer_AlbedoRoughness, probeIndex, rayIndex);
                        float4 normalHitFlag = SampleGBuffer(_GBuffer_NormalHitFlag, probeIndex, rayIndex);
                        color = normalHitFlag.w > 0.5
                            ? albedoRoughness.rgb
                            : float3(0.1, 0.1, 0.3);
                    }
                    else if (_VisualizationMode == 13)
                    {

                        float4 emissionMetallic = SampleGBuffer(_GBuffer_EmissionMetallic, probeIndex, rayIndex);
                        color = emissionMetallic.rgb * _Intensity;
                    }
                    else if (_VisualizationMode == 14)
                    {

                        float4 positionDistance = SampleGBuffer(_GBuffer_PositionDistance, probeIndex, rayIndex);
                        float dist = positionDistance.w;
                        if (dist > 0)
                        {
                            float t = saturate(dist / 30.0);

                            if (t < 0.25)
                                color = lerp(float3(0, 0, 1), float3(0, 1, 1), t * 4.0);
                            else if (t < 0.5)
                                color = lerp(float3(0, 1, 1), float3(0, 1, 0), (t - 0.25) * 4.0);
                            else if (t < 0.75)
                                color = lerp(float3(0, 1, 0), float3(1, 1, 0), (t - 0.5) * 4.0);
                            else
                                color = lerp(float3(1, 1, 0), float3(1, 0, 0), (t - 0.75) * 4.0);
                        }
                        else
                        {
                            color = float3(0.5, 0, 0.5);
                        }
                    }
                    else if (_VisualizationMode == 15)
                    {

                        float4 directData = SampleGBuffer(_DirectIrradianceBuffer, probeIndex, rayIndex);
                        color = directData.rgb * _Intensity;
                    }
                    else if (_VisualizationMode == 16)
                    {

                        float4 indirectData = SampleGBuffer(_IndirectIrradianceBuffer, probeIndex, rayIndex);
                        color = indirectData.rgb * _Intensity;
                    }
                    else if (_VisualizationMode == 17)
                    {

                        float4 radianceData = SampleGBuffer(_RadianceBuffer, probeIndex, rayIndex);
                        color = radianceData.rgb * _Intensity;
                    }
                }

                else if (_VisualizationMode >= 20 && _ProbeDataWidth > 0 && _VisualizationMode < 30)
                {
                    if (_VisualizationMode == 20)
                    {

                        float4 probeData = SampleProbeData(probeIndex);
                        float3 offset = probeData.xyz;
                        float offsetMagnitude = length(offset);

                        if (offsetMagnitude > 0.001)
                        {

                            float3 offsetDir = offset / offsetMagnitude;
                            color = offsetDir * 0.5 + 0.5;

                            float maxOffset = min(min(_ProbeSpacing.x, _ProbeSpacing.y), _ProbeSpacing.z) * 0.45;
                            float normalizedMagnitude = saturate(offsetMagnitude / maxOffset);
                            color *= 0.3 + normalizedMagnitude * 0.7;
                        }
                        else
                        {

                            color = float3(0.2, 0.2, 0.2);
                        }
                    }
                    else if (_VisualizationMode == 21)
                    {

                        float4 probeData = SampleProbeData(probeIndex);

                        if (probeData.w == 1)
                        {

                            color = float3(1.0, 0.0, 0.0);
                        }
                        else if (probeData.w == 2)
                        {

                            color = float3(1.0, 1.0, 0.0);
                        }
                        else if(probeData.w == 3)
                        {

                            color = float3(0.0, 1.0, 0.0);
                        }
                    }
                    else if (_VisualizationMode == 22)
                    {

                        float4 probeData = SampleProbeData(probeIndex);
                        uint probeState = (uint)probeData.w;

                        if (probeState == 0)
                        {

                            color = float3(0.0, 1.0, 0.0);
                        }
                        else
                        {

                            color = float3(1.0, 0.0, 0.0);
                        }
                    }
                }

                else if (_VisualizationMode == 30)
                {

                    float3 n = normal;
                    float sum = abs(n.x) + abs(n.y) + abs(n.z);
                    n.xy /= sum;
                    if (n.z < 0.0)
                    {
                        float2 signNotZero = float2(n.x >= 0.0 ? 1.0 : -1.0, n.y >= 0.0 ? 1.0 : -1.0);
                        n.xy = (1.0 - abs(n.yx)) * signNotZero;
                    }
                    float2 octaUV = n.xy * 0.5 + 0.5;

                    float probeResolution = _IrradianceAtlasParams.z;
                    float probesPerRow = _IrradianceAtlasParams.w;
                    int probeX = probeIndex % (int)probesPerRow;
                    int probeY = probeIndex / (int)probesPerRow;

                    int gutterSize = 1;
                    int probeSizeWithGutter = (int)probeResolution + gutterSize * 2;
                    int2 probeBaseCoord = int2(
                        probeX * probeSizeWithGutter + gutterSize,
                        probeY * probeSizeWithGutter + gutterSize
                    );

                    int2 texelOffset = int2(octaUV * probeResolution);
                    texelOffset = clamp(texelOffset, int2(0, 0), int2(probeResolution - 1, probeResolution - 1));
                    int2 texelCoord = probeBaseCoord + texelOffset;

                    float variability = _ProbeVariability.Load(int3(texelCoord, 0));

                    float t = saturate(variability * 10.0);

                    if (t < 0.25)
                        color = lerp(float3(0, 0, 1), float3(0, 1, 1), t * 4.0);
                    else if (t < 0.5)
                        color = lerp(float3(0, 1, 1), float3(0, 1, 0), (t - 0.25) * 4.0);
                    else if (t < 0.75)
                        color = lerp(float3(0, 1, 0), float3(1, 1, 0), (t - 0.5) * 4.0);
                    else
                        color = lerp(float3(1, 1, 0), float3(1, 0, 0), (t - 0.75) * 4.0);
                }

                if (_VisualizationMode < 10)
                {
                    color = lerp(i.probeColor.rgb, color, i.probeColor.a);
                }

                return fixed4(color, 1.0);
            }
            ENDCG
        }
    }

    FallBack "Diffuse"
}
