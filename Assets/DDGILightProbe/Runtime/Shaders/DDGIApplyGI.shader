Shader "Hidden/DDGI/ApplyGI"
{
    Properties
    {
        [HideInInspector] _MainTex("", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "DDGI Apply GI"
            ZWrite Off
            ZTest Always
            Cull Off
            Blend One One

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
            #include "DDGISampling.hlsl"

            #pragma multi_compile _ _DDGI_ACCURATE_NORMALS

            TEXTURE2D(_GBuffer0);
            TEXTURE2D(_GBuffer1);
            TEXTURE2D(_GBuffer2);

            float4 _DDGIVolumeOrigin;
            float4 _DDGIVolumeSpacing;
            float4 _DDGIVolumeProbeCounts;
            float  _DDGINormalBias;
            float  _DDGIViewBias;
            float  _DDGIIrradianceGamma;
            float  _DDGIIrradianceProbeRes;
            float  _DDGIDistanceProbeRes;
            float  _DDGIProbesPerRow;
            float4 _DDGIIrradianceTexelSize;
            float4 _DDGIDistanceTexelSize;
            float  _DDGIGIIntensity;
            float  _DDGIGIAOIntensity;

            float  _DDGIProbeDataWidth;

            Texture2D<float4> _DDGIIrradianceAtlas;
            Texture2D<float2> _DDGIDistanceAtlas;
            Texture2D<float4> _DDGIProbeData;
            SamplerState sampler_DDGIIrradianceAtlas;
            SamplerState sampler_DDGIDistanceAtlas;

            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                output.uv = GetFullScreenTriangleTexCoord(input.vertexID);
                return output;
            }

            float3 DecodeGBufferNormal(uint2 pixelCoord)
            {
                float4 gbuffer2 = LOAD_TEXTURE2D(_GBuffer2, pixelCoord);
            #ifdef _DDGI_ACCURATE_NORMALS
                half2 remappedOctNormalWS = half2(Unpack888ToFloat2(gbuffer2.rgb));
                half2 octNormalWS = remappedOctNormalWS * half(2.0) - half(1.0);
                return half3(UnpackNormalOctQuadEncode(octNormalWS));
            #else
                return normalize(gbuffer2.rgb * 2.0 - 1.0);
            #endif
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                uint2 pixelCoord = uv * _ScreenParams.xy;

                float depth = SampleSceneDepth(uv);

                #if UNITY_REVERSED_Z
                    if (depth < 0.00001) return half4(0, 0, 0, 0);
                #else
                    if (depth > 0.99999) return half4(0, 0, 0, 0);
                #endif

                half3 albedo = LOAD_TEXTURE2D(_GBuffer0, pixelCoord).rgb;
                float ao = LOAD_TEXTURE2D(_GBuffer1, pixelCoord).a;

                float3 normalWS = DecodeGBufferNormal(pixelCoord);

                #if UNITY_REVERSED_Z
                    float depthNDC = depth;
                #else
                    float depthNDC = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, depth);
                #endif
                float3 worldPos = ComputeWorldSpacePosition(uv, depthNDC, UNITY_MATRIX_I_VP);

                DDGIVolumeParams volume;
                volume.origin = _DDGIVolumeOrigin.xyz;
                volume.probeSpacing = _DDGIVolumeSpacing.xyz;
                volume.probeCounts = (int3)_DDGIVolumeProbeCounts.xyz;
                volume.normalBias = _DDGINormalBias;
                volume.viewBias = _DDGIViewBias;
                volume.irradianceGamma = _DDGIIrradianceGamma;
                volume.irradianceProbeRes = (int)_DDGIIrradianceProbeRes;
                volume.distanceProbeRes = (int)_DDGIDistanceProbeRes;
                volume.probesPerRow = (int)_DDGIProbesPerRow;
                volume.irradianceTexelSize = _DDGIIrradianceTexelSize.xy;
                volume.distanceTexelSize = _DDGIDistanceTexelSize.xy;

                float3 viewDir = normalize(_WorldSpaceCameraPos.xyz - worldPos);

                float3 surfaceBias = DDGIGetSurfaceBias(normalWS, viewDir, volume);

                float3 irradiance = DDGIGetVolumeIrradianceWithProbeState(
                    worldPos,
                    surfaceBias,
                    normalWS,
                    volume,
                    _DDGIIrradianceAtlas,
                    _DDGIDistanceAtlas,
                    _DDGIProbeData,
                    (int)_DDGIProbeDataWidth
                );

                float3 indirectDiffuse = albedo * irradiance * lerp(1,ao,_DDGIGIAOIntensity);

                return half4(indirectDiffuse * _DDGIGIIntensity, 0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
