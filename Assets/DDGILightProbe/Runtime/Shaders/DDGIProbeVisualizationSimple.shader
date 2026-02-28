Shader "DDGI/ProbeVisualizationSimple"
{
    Properties
    {
        _Color ("Color", Color) = (0.3, 0.7, 1.0, 1.0)
        _ProbeRadius ("Probe Radius", Range(0.01, 1.0)) = 0.2
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 100

        Pass
        {
            Name "SimpleVisualization"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma target 3.5

            #include "UnityCG.cginc"

            float4 _Color;
            float _ProbeRadius;

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _ProbePosition)
                UNITY_DEFINE_INSTANCED_PROP(float4, _ProbeColor)
            UNITY_INSTANCING_BUFFER_END(Props)

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
                float4 color : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);

                #if defined(UNITY_INSTANCING_ENABLED)
                float4 probePos = UNITY_ACCESS_INSTANCED_PROP(Props, _ProbePosition);
                float4 probeColor = UNITY_ACCESS_INSTANCED_PROP(Props, _ProbeColor);
                #else
                float4 probePos = float4(0, 0, 0, 0);
                float4 probeColor = _Color;
                #endif

                float3 worldPos = v.vertex.xyz * _ProbeRadius + probePos.xyz;

                o.pos = mul(UNITY_MATRIX_VP, float4(worldPos, 1.0));
                o.normal = v.normal;
                o.color = probeColor;

                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);

                float3 lightDir = normalize(float3(0.5, 1.0, 0.3));
                float ndotl = dot(normalize(i.normal), lightDir) * 0.5 + 0.5;

                float3 color = i.color.rgb * ndotl;

                return fixed4(color, 1.0);
            }
            ENDCG
        }
    }

    FallBack "Diffuse"
}
