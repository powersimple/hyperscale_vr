// Submarine cables as ribbons of constant world width. Vertices hold the ECEF centerline
// point (position), the ECEF side direction (normal), the side sign and width factor
// (uv.x, uv.y), and the cable color with its emphasis in alpha (color).
Shader "AtlasVR/Ribbon"
{
    Properties
    {
        _WidthWS ("Width (world meters)", Float) = 0.0012
        _LiftWS ("Lift above the surface (world meters)", Float) = 0.0015
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "AtlasRibbon"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4x4 _AtlasGlobeToWorld;
            float4 _AtlasLens;
            float4 _AtlasLensUp;
            float4 _AtlasRefWorld;
            float4 _AtlasRefEcefHigh;
            float4 _AtlasRefEcefLow;

            CBUFFER_START(UnityPerMaterial)
                float _WidthWS;
                float _LiftWS;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float3 low : TEXCOORD1;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float3 positionWS : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 rel = (v.positionOS.xyz - _AtlasRefEcefHigh.xyz) + (v.low - _AtlasRefEcefLow.xyz);
                float3x3 g = (float3x3)_AtlasGlobeToWorld;
                float3 centerW = _AtlasRefWorld.xyz + mul(g, rel);
                float3 sideW = normalize(mul(g, v.normalOS));
                float3 upW = normalize(mul(g, normalize(v.positionOS.xyz)));
                float dist = distance(_WorldSpaceCameraPos.xyz, centerW) / 0.8;
                float3 ws = centerW + sideW * (v.uv.x * 0.5 * _WidthWS * v.uv.y * dist) + upW * (_LiftWS * dist);
                o.positionWS = ws;
                o.positionCS = TransformWorldToHClip(ws);
                o.color = v.color;
                if (dot(v.positionOS.xyz, -rel) < 0.0) o.color.a = 0.0; // behind the horizon
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                if (_AtlasLensUp.w > 0.5)
                {
                    float3 d = i.positionWS - _AtlasLens.xyz;
                    d -= dot(d, _AtlasLensUp.xyz) * _AtlasLensUp.xyz;
                    clip(_AtlasLens.w - length(d));
                }
                if (i.color.a < 0.01) discard;
                return half4(i.color);
            }
            ENDHLSL
        }
    }
}
