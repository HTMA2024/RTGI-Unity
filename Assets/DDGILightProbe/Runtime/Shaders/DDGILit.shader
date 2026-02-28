Shader "DDGI/Lit"
{
    Properties
    {
        _Color("Color", Color) = (1, 1, 1, 1)
        _MainTex("Albedo", 2D) = "white" {}

        [Toggle(_EMISSION)] _Emission("Emission", Float) = 0
        [HDR] _EmissionColor("Emission Color", Color) = (0, 0, 0, 0)
        _EmissionTex("Emission", 2D) = "white" {}

        _Glossiness("Smoothness", Range(0.0, 1.0)) = 0.5
        [Gamma] _Metallic("Metallic", Range(0.0, 1.0)) = 0.0

        _BumpMap("Normal Map", 2D) = "bump" {}
        _BumpScale("Normal Scale", Float) = 1.0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 200

        Pass
        {
            Name "FORWARD"
            Tags { "LightMode" = "ForwardBase" }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase
            #pragma shader_feature _EMISSION

            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 tangent : TANGENT;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                float3 worldNormal : TEXCOORD2;
                float3 worldTangent : TEXCOORD3;
                float3 worldBitangent : TEXCOORD4;
                SHADOW_COORDS(5)
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            sampler2D _EmissionTex;
            float4 _EmissionTex_ST;
            sampler2D _BumpMap;

            float4 _Color;
            float4 _EmissionColor;
            float _Glossiness;
            float _Metallic;
            float _BumpScale;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.worldTangent = UnityObjectToWorldDir(v.tangent.xyz);
                o.worldBitangent = cross(o.worldNormal, o.worldTangent) * v.tangent.w;
                TRANSFER_SHADOW(o);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {

                fixed4 albedo = tex2D(_MainTex, i.uv) * _Color;

                fixed3 normalTS = UnpackScaleNormal(tex2D(_BumpMap, i.uv), _BumpScale);
                float3x3 TBN = float3x3(i.worldTangent, i.worldBitangent, i.worldNormal);
                fixed3 worldNormal = normalize(mul(normalTS, TBN));

                fixed3 lightDir = normalize(_WorldSpaceLightPos0.xyz);
                fixed NdotL = saturate(dot(worldNormal, lightDir));
                fixed shadow = SHADOW_ATTENUATION(i);
                fixed3 diffuse = albedo.rgb * _LightColor0.rgb * NdotL * shadow;

                fixed3 ambient = ShadeSH9(float4(worldNormal, 1.0)) * albedo.rgb;

                fixed3 finalColor = diffuse + ambient;

                #if _EMISSION
                    fixed3 emission = tex2D(_EmissionTex, i.uv).rgb * _EmissionColor.rgb;
                    finalColor += emission;
                #endif

                return fixed4(finalColor, albedo.a);
            }
            ENDCG
        }

        Pass
        {
            Name "FORWARD_ADD"
            Tags { "LightMode" = "ForwardAdd" }
            Blend One One
            ZWrite Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdadd_fullshadows

            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 tangent : TANGENT;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                float3 worldNormal : TEXCOORD2;
                float3 worldTangent : TEXCOORD3;
                float3 worldBitangent : TEXCOORD4;
                SHADOW_COORDS(5)
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            sampler2D _BumpMap;

            float4 _Color;
            float _BumpScale;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.worldTangent = UnityObjectToWorldDir(v.tangent.xyz);
                o.worldBitangent = cross(o.worldNormal, o.worldTangent) * v.tangent.w;
                TRANSFER_SHADOW(o);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {

                fixed4 albedo = tex2D(_MainTex, i.uv) * _Color;

                fixed3 normalTS = UnpackScaleNormal(tex2D(_BumpMap, i.uv), _BumpScale);
                float3x3 TBN = float3x3(i.worldTangent, i.worldBitangent, i.worldNormal);
                fixed3 worldNormal = normalize(mul(normalTS, TBN));

                #ifdef USING_DIRECTIONAL_LIGHT
                    fixed3 lightDir = normalize(_WorldSpaceLightPos0.xyz);
                #else
                    fixed3 lightDir = normalize(_WorldSpaceLightPos0.xyz - i.worldPos);
                #endif

                UNITY_LIGHT_ATTENUATION(atten, i, i.worldPos);

                fixed NdotL = saturate(dot(worldNormal, lightDir));
                fixed3 diffuse = albedo.rgb * _LightColor0.rgb * NdotL * atten;

                return fixed4(diffuse, 1.0);
            }
            ENDCG
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            Cull Back

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_shadowcaster

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                V2F_SHADOW_CASTER;
            };

            v2f vert(appdata v)
            {
                v2f o;
                TRANSFER_SHADOW_CASTER_NORMALOFFSET(o);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                SHADOW_CASTER_FRAGMENT(i);
            }
            ENDCG
        }

        Pass
        {
            Name "META"
            Tags { "LightMode" = "Meta" }

            Cull Off

            CGPROGRAM
            #pragma vertex vert_meta
            #pragma fragment frag_meta
            #pragma shader_feature _EMISSION

            #include "UnityCG.cginc"
            #include "UnityMetaPass.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                float2 uv2 : TEXCOORD2;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            sampler2D _EmissionTex;

            float4 _Color;
            float4 _EmissionColor;

            v2f vert_meta(appdata v)
            {
                v2f o;
                o.pos = UnityMetaVertexPosition(v.vertex, v.uv1.xy, v.uv2.xy, unity_LightmapST, unity_DynamicLightmapST);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag_meta(v2f i) : SV_Target
            {
                UnityMetaInput metaInput;
                UNITY_INITIALIZE_OUTPUT(UnityMetaInput, metaInput);

                fixed4 albedo = tex2D(_MainTex, i.uv) * _Color;
                metaInput.Albedo = albedo.rgb;

                #if _EMISSION
                    metaInput.Emission = tex2D(_EmissionTex, i.uv).rgb * _EmissionColor.rgb;
                #else
                    metaInput.Emission = 0;
                #endif

                return UnityMetaFragment(metaInput);
            }
            ENDCG
        }

        Pass
        {
            Name "DDGIRayTracing"
            Tags{ "LightMode" = "RayTracing" }

            HLSLPROGRAM

            #include "UnityRaytracingMeshUtils.cginc"
            #include "DDGIRaytracing/DDGIGBuffer.hlsl"

            #pragma raytracing DDGIHit
            #pragma shader_feature_raytracing _EMISSION

            float4 _Color;

            Texture2D<float4> _MainTex;
            float4 _MainTex_ST;
            SamplerState sampler_MainTex;

            Texture2D<float4> _EmissionTex;
            float4 _EmissionTex_ST;
            SamplerState sampler_EmissionTex;

            float4 _EmissionColor;
            float _Glossiness;
            float _Metallic;

            struct Vertex
            {
                float3 position;
                float3 normal;
                float2 uv;
            };

            Vertex FetchVertex(uint vertexIndex)
            {
                Vertex v;
                v.position = UnityRayTracingFetchVertexAttribute3(vertexIndex, kVertexAttributePosition);
                v.normal = UnityRayTracingFetchVertexAttribute3(vertexIndex, kVertexAttributeNormal);
                v.uv = UnityRayTracingFetchVertexAttribute2(vertexIndex, kVertexAttributeTexCoord0);
                return v;
            }

            Vertex InterpolateVertices(Vertex v0, Vertex v1, Vertex v2, float3 barycentrics)
            {
                Vertex v;
                v.position = v0.position * barycentrics.x + v1.position * barycentrics.y + v2.position * barycentrics.z;
                v.normal = v0.normal * barycentrics.x + v1.normal * barycentrics.y + v2.normal * barycentrics.z;
                v.uv = v0.uv * barycentrics.x + v1.uv * barycentrics.y + v2.uv * barycentrics.z;
                return v;
            }

            [shader("closesthit")]
            void DDGIClosestHitShader(inout DDGIRayPayload payload : SV_RayPayload,
                                      BuiltInTriangleIntersectionAttributes attribs : SV_IntersectionAttributes)
            {

                uint3 triangleIndices = UnityRayTracingFetchTriangleIndices(PrimitiveIndex());

                Vertex v0 = FetchVertex(triangleIndices.x);
                Vertex v1 = FetchVertex(triangleIndices.y);
                Vertex v2 = FetchVertex(triangleIndices.z);

                float3 barycentrics = float3(
                    1.0 - attribs.barycentrics.x - attribs.barycentrics.y,
                    attribs.barycentrics.x,
                    attribs.barycentrics.y
                );

                Vertex v = InterpolateVertices(v0, v1, v2, barycentrics);

                float3 worldPos = WorldRayOrigin() + WorldRayDirection() * RayTCurrent();

                bool isFrontFace = HitKind() == HIT_KIND_TRIANGLE_FRONT_FACE;
                float3 localNormal = isFrontFace ? v.normal : -v.normal;
                float3 worldNormal = normalize(mul((float3x3)ObjectToWorld(),localNormal));

                float2 uv = v.uv;
                float4 albedoSample = _MainTex.SampleLevel(sampler_MainTex, uv, 0);
                float3 albedo = albedoSample.rgb * _Color.rgb;

                float3 emission = float3(0, 0, 0);
                #if _EMISSION
                    float2 emissionUV = _EmissionTex_ST.xy * v.uv + _EmissionTex_ST.zw;
                    emission = _EmissionTex.SampleLevel(sampler_EmissionTex, emissionUV, 0).rgb * _EmissionColor.rgb;
                #endif

                payload.position = worldPos;
                payload.hitDistance = RayTCurrent();
                payload.normal = worldNormal;
                payload.hitFlag = isFrontFace ? 1 : 2;
                payload.albedo = albedo;
                payload.emission = emission;
                payload.roughness = 1.0 - _Glossiness;
                payload.metallic = _Metallic;
            }

            ENDHLSL
        }
    }

    FallBack "Standard"
}
