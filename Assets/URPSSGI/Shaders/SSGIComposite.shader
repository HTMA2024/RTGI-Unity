Shader "Hidden/SSGI/Composite"
{
    Properties
    {

    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "SSGIComposite"
            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_local _ _SSGI_REPLACE_AMBIENT
            #pragma multi_compile _ _SSGI_ACCURATE_NORMALS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "SSGICommon.hlsl"

            TEXTURE2D(_MainTex);
            TEXTURE2D(_IndirectDiffuseTexture);
            TEXTURE2D(_CameraDepthTexture);
            TEXTURE2D(_DepthPyramidTexture);
            TEXTURE2D(_SSGIDebugHitPointTexture);
            TEXTURE2D(_SSGIDebugAccumCountTexture);
            TEXTURE2D(_SSGIDebugPreDenoiseTexture);
            TEXTURE2D(_SSGIDebugRawGITexture);
            TEXTURE2D(_SSGIDebugOutputTexture);

            TEXTURE2D(_SSGIDebugRTGITexture);
            TEXTURE2D(_SSGIDebugRTGIRayLengthTexture);
            TEXTURE2D(_SSGIDebugMixedMaskTexture);

            TEXTURE2D(_RTAOOutputTexture);

            TEXTURE2D(_HistoryObjectMotionTexture);
            half _RTAOIntensity;

            TEXTURE2D(_GBuffer0);

            StructuredBuffer<int2> _DepthPyramidMipLevelOffsets;

            half _GIIntensity;
            int _SSGIDebugMode;
            int _SSGIDebugMipLevel;
            float4 _SSGICompositeScreenSize;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            Varyings Vert(uint vertexID : SV_VertexID)
            {
                Varyings o;

                o.uv = float2((vertexID << 1) & 2, vertexID & 2);
                o.positionCS = float4(o.uv * 2.0 - 1.0, 0.0, 1.0);
                #if UNITY_UV_STARTS_AT_TOP
                o.uv.y = 1.0 - o.uv.y;
                #endif
                return o;
            }

            half4 EvaluateDebugOutput(uint2 positionSS, float2 uv)
            {

                if (_SSGIDebugMode == 1)
                {
                    half3 gi = SAMPLE_TEXTURE2D_LOD(_IndirectDiffuseTexture, sampler_LinearClamp, uv, 0).rgb;
                    float depth = LOAD_TEXTURE2D(_CameraDepthTexture, positionSS).r;

                    half skyMask = 1.0 - step(abs(depth - UNITY_RAW_FAR_CLIP_VALUE), 0.0001);
                    return half4(gi * skyMask, 1.0);
                }

                if (_SSGIDebugMode == 2)
                {
                    half2 hitUV = LOAD_TEXTURE2D(_SSGIDebugHitPointTexture, positionSS).rg;
                    return half4(hitUV, 0.0, 1.0);
                }

                if (_SSGIDebugMode == 3)
                {
                    uint2 mipCoord  = positionSS >> _SSGIDebugMipLevel;
                    int2  mipOffset = _DepthPyramidMipLevelOffsets[_SSGIDebugMipLevel];
                    float depth = LOAD_TEXTURE2D(_DepthPyramidTexture, (uint2)(mipOffset + (int2)mipCoord)).r;
                    half gray = saturate(Linear01Depth(depth, _ZBufferParams));
                    return half4(gray, gray, gray, 1.0);
                }

                if (_SSGIDebugMode == 4)
                {
                    float3 normalWS = DecodeNormalURP(positionSS);
                    return half4(normalWS * 0.5 + 0.5, 1.0);
                }

                if (_SSGIDebugMode == 5)
                {

                    half t = SAMPLE_TEXTURE2D_LOD(_SSGIDebugAccumCountTexture, sampler_LinearClamp, uv, 0).a * 0.125;
                    half t2 = t * 2.0;
                    half r = saturate(t2 - 1.0);
                    half g = 1.0 - abs(t2 - 1.0);
                    half b = saturate(1.0 - t2);
                    return half4(r, g, b, 1.0);
                }

                if (_SSGIDebugMode == 6)
                {
                    half side = step(0.5, uv.x);
                    half3 preDenoise = SAMPLE_TEXTURE2D_LOD(_SSGIDebugPreDenoiseTexture, sampler_LinearClamp, uv, 0).rgb;
                    half3 postDenoise = SAMPLE_TEXTURE2D_LOD(_IndirectDiffuseTexture, sampler_LinearClamp, uv, 0).rgb;
                    half3 result = lerp(preDenoise, postDenoise, side);
                    return half4(result, 1.0);
                }

                if (_SSGIDebugMode == 7)
                {
                    half3 rawGI = SAMPLE_TEXTURE2D_LOD(_SSGIDebugRawGITexture, sampler_LinearClamp, uv, 0).rgb;
                    return half4(rawGI, 1.0);
                }

                if (_SSGIDebugMode == 8)
                {
                    half3 rayDir = SAMPLE_TEXTURE2D_LOD(_SSGIDebugOutputTexture, sampler_LinearClamp, uv, 0).rgb;
                    return half4(rayDir, 1.0);
                }

                if (_SSGIDebugMode == 9)
                {
                    half4 validity = SAMPLE_TEXTURE2D_LOD(_SSGIDebugOutputTexture, sampler_PointClamp, uv, 0);
                    return validity;
                }

                if (_SSGIDebugMode == 10)
                {
                    half2 mv = SAMPLE_TEXTURE2D_LOD(_HistoryObjectMotionTexture, sampler_LinearClamp, uv, 0).rg;

                    half2 visMV = abs(mv) * max(_ScreenParams.x, _ScreenParams.y);
                    return half4(saturate(visMV), 0.0, 1.0);
                }

                if (_SSGIDebugMode == 11)
                {
                    half2 reprojUV = SAMPLE_TEXTURE2D_LOD(_SSGIDebugOutputTexture, sampler_LinearClamp, uv, 0).rg;
                    return half4(reprojUV, 0.0, 1.0);
                }

                if (_SSGIDebugMode == 12)
                {
                    half3 denoisedGI = SAMPLE_TEXTURE2D_LOD(_IndirectDiffuseTexture, sampler_LinearClamp, uv, 0).rgb;
                    float depth = LOAD_TEXTURE2D(_CameraDepthTexture, positionSS).r;
                    half skyMask = 1.0 - step(abs(depth - UNITY_RAW_FAR_CLIP_VALUE), 0.0001);
                    return half4(denoisedGI * skyMask, 1.0);
                }

                if (_SSGIDebugMode == 13)
                {
                    half3 rtgi = SAMPLE_TEXTURE2D_LOD(_SSGIDebugRTGITexture, sampler_LinearClamp, uv, 0).rgb;
                    float depth = LOAD_TEXTURE2D(_CameraDepthTexture, positionSS).r;
                    half skyMask = 1.0 - step(abs(depth - UNITY_RAW_FAR_CLIP_VALUE), 0.0001);
                    return half4(rtgi * skyMask, 1.0);
                }

                if (_SSGIDebugMode == 14)
                {

                    half t = SAMPLE_TEXTURE2D_LOD(_SSGIDebugRTGIRayLengthTexture, sampler_LinearClamp, uv, 0).a;
                    half t2 = t * 2.0;
                    half r = saturate(t2 - 1.0);
                    half g = 1.0 - abs(t2 - 1.0);
                    half b = saturate(1.0 - t2);
                    return half4(r, g, b, 1.0);
                }

                if (_SSGIDebugMode == 15)
                {
                    half mask = SAMPLE_TEXTURE2D_LOD(_SSGIDebugMixedMaskTexture, sampler_PointClamp, uv, 0).r;
                    return half4(1.0 - mask, mask, 0.0, 1.0);
                }

                if (_SSGIDebugMode == 16)
                {
                    half3 normal = SAMPLE_TEXTURE2D_LOD(_SSGIDebugRTGITexture, sampler_LinearClamp, uv, 0).rgb;
                    float depth = LOAD_TEXTURE2D(_CameraDepthTexture, positionSS).r;
                    half skyMask = 1.0 - step(abs(depth - UNITY_RAW_FAR_CLIP_VALUE), 0.0001);
                    return half4(normal * 0.5 + 0.5, 1.0) * skyMask;
                }

                if (_SSGIDebugMode == 17)
                {
                    half ao = SAMPLE_TEXTURE2D_LOD(_RTAOOutputTexture, sampler_LinearClamp, uv, 0).r;
                    float depth = LOAD_TEXTURE2D(_CameraDepthTexture, positionSS).r;
                    half skyMask = 1.0 - step(abs(depth - UNITY_RAW_FAR_CLIP_VALUE), 0.0001);
                    return half4(ao, ao, ao, 1.0) * skyMask;
                }

                if (_SSGIDebugMode == 18)
                {
                    half3 gi = SAMPLE_TEXTURE2D_LOD(_IndirectDiffuseTexture, sampler_LinearClamp, uv, 0).rgb;
                    half ao = SAMPLE_TEXTURE2D_LOD(_RTAOOutputTexture, sampler_LinearClamp, uv, 0).r;
                    float depth = LOAD_TEXTURE2D(_CameraDepthTexture, positionSS).r;
                    half skyMask = 1.0 - step(abs(depth - UNITY_RAW_FAR_CLIP_VALUE), 0.0001);
                    return half4(gi * ao * skyMask, 1.0);
                }

                if (_SSGIDebugMode == 19)
                {
                    half3 shadowDbg = SAMPLE_TEXTURE2D_LOD(_SSGIDebugRTGITexture, sampler_LinearClamp, uv, 0).rgb;
                    float depth = LOAD_TEXTURE2D(_CameraDepthTexture, positionSS).r;
                    half skyMask = 1.0 - step(abs(depth - UNITY_RAW_FAR_CLIP_VALUE), 0.0001);
                    return half4(shadowDbg * skyMask, 1.0);
                }

                return half4(0.0, 0.0, 0.0, 1.0);
            }

            half4 Frag(Varyings i) : SV_Target
            {

                uint2 positionSS = uint2(i.uv * _SSGICompositeScreenSize.xy);

                if (_SSGIDebugMode > 0)
                    return EvaluateDebugOutput(positionSS, i.uv);

                half3 sceneColor = LOAD_TEXTURE2D(_MainTex, positionSS).rgb;
                half3 gi         = SAMPLE_TEXTURE2D_LOD(_IndirectDiffuseTexture, sampler_LinearClamp, i.uv, 0).rgb;
                float depth      = LOAD_TEXTURE2D(_CameraDepthTexture, positionSS).r;

                half3 albedo = LOAD_TEXTURE2D(_GBuffer0, positionSS).rgb;
                gi *= albedo;

                half skyMask = 1.0 - step(abs(depth - UNITY_RAW_FAR_CLIP_VALUE), 0.0001);

                half scaledIntensity = _GIIntensity * skyMask;

                half aoFactor = lerp(1.0, SAMPLE_TEXTURE2D_LOD(_RTAOOutputTexture, sampler_LinearClamp, i.uv, 0).r, _RTAOIntensity * skyMask);
                sceneColor *= aoFactor;

                #ifdef _SSGI_REPLACE_AMBIENT

                    half3 finalColor = sceneColor - unity_AmbientSky.rgb * skyMask + gi * scaledIntensity * aoFactor;
                #else

                    half3 finalColor = sceneColor + gi * scaledIntensity * aoFactor;
                #endif

                return half4(finalColor, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "SSGICopy"
            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex VertCopy
            #pragma fragment FragCopy

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SamplerState sampler_PointClamp;

            struct VaryingsCopy
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            VaryingsCopy VertCopy(uint vertexID : SV_VertexID)
            {
                VaryingsCopy o;
                o.uv = float2((vertexID << 1) & 2, vertexID & 2);
                o.positionCS = float4(o.uv * 2.0 - 1.0, 0.0, 1.0);
                #if UNITY_UV_STARTS_AT_TOP
                o.uv.y = 1.0 - o.uv.y;
                #endif
                return o;
            }

            half4 FragCopy(VaryingsCopy i) : SV_Target
            {
                return SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_PointClamp, i.uv, 0);
            }
            ENDHLSL
        }
    }
}
