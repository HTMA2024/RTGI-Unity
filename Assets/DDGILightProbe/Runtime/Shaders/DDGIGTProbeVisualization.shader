Shader "DDGI/GTProbeVisualization"
{
    Properties
    {
        _GTTexture ("GT Octahedral Map", 2D) = "black" {}
        _UnitySHTexture ("Unity SH Octahedral Map", 2D) = "black" {}
        _ProbeRadius ("Probe Radius", Range(0.01, 2.0)) = 0.3
        _Intensity ("Intensity", Range(0.1, 10.0)) = 1.0

        _DisplayMode ("Display Mode", Int) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry+10" }
        LOD 100

        Pass
        {
            Name "GTProbeVisualization"
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5

            #include "UnityCG.cginc"

            sampler2D _GTTexture;
            sampler2D _UnitySHTexture;
            float _ProbeRadius;
            float _Intensity;
            int _DisplayMode;
            float3 _ProbeWorldPos;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 localNormal : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
            };

            float2 OctahedronEncode(float3 n)
            {
                float3 absN = abs(n);
                float sum = absN.x + absN.y + absN.z;
                n.xy /= sum;

                if (n.z < 0)
                {
                    float2 signNotZero = float2(n.x >= 0 ? 1 : -1, n.y >= 0 ? 1 : -1);
                    n.xy = (1.0 - abs(n.yx)) * signNotZero;
                }

                return n.xy * 0.5 + 0.5;
            }

            v2f vert(appdata v)
            {
                v2f o;
                float3 worldPos = v.vertex.xyz * _ProbeRadius + _ProbeWorldPos;
                o.pos = mul(UNITY_MATRIX_VP, float4(worldPos, 1.0));
                o.localNormal = v.normal;
                o.worldPos = worldPos;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 normal = normalize(i.localNormal);
                float2 octUV = OctahedronEncode(normal);
                float3 color = 0;

                float3 gt = tex2D(_GTTexture, octUV).rgb;
                float3 unitySH = tex2D(_UnitySHTexture, octUV).rgb;

                if (_DisplayMode == 0)
                {

                    color = gt * _Intensity;
                }
                else if (_DisplayMode == 1)
                {

                    color = unitySH * _Intensity;
                }
                else if (_DisplayMode == 2)
                {

                    color = abs(gt - unitySH) * 5.0 * _Intensity;
                }
                else if (_DisplayMode == 3)
                {

                    float3 viewDir = normalize(i.worldPos - _WorldSpaceCameraPos);
                    float3 viewRight = normalize(cross(float3(0, 1, 0), -viewDir));
                    float side = dot(normalize(i.worldPos - _ProbeWorldPos), viewRight);

                    color = (side > 0 ? gt : unitySH) * _Intensity;
                }

                return fixed4(color, 1.0);
            }
            ENDCG
        }
    }

    FallBack "Diffuse"
}
