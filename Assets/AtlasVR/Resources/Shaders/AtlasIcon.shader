// The story's icons on the globe: instanced camera-facing quads cut from an atlas. Placed like
// AtlasVR/Marker (ECEF instances, high/low split relative to the eye, constant angular size,
// hidden behind the horizon), drawn over terrain.
Shader "AtlasVR/Icon"
{
    Properties
    {
        _MainTex ("Atlas", 2D) = "white" {}
        _SizeWS ("Size (world meters)", Float) = 0.009
        _LiftWS ("Lift above the surface (world meters)", Float) = 0.004
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "AtlasIcon"
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
            float _AtlasGlobeScale;
            float4 _AtlasLens;
            float4 _AtlasLensUp;
            float4 _AtlasRefWorld;
            float4 _AtlasRefEcefHigh;
            float4 _AtlasRefEcefLow;

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _SizeWS;
                float _LiftWS;
            CBUFFER_END

            UNITY_INSTANCING_BUFFER_START(AtlasIconProps)
                UNITY_DEFINE_INSTANCED_PROP(float4, _InstColor)
                UNITY_DEFINE_INSTANCED_PROP(float4, _InstLow)
                UNITY_DEFINE_INSTANCED_PROP(float4, _InstUV)
            UNITY_INSTANCING_BUFFER_END(AtlasIconProps)

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                float3 positionWS : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float4x4 m = UNITY_MATRIX_M;
                float3 centerHi = float3(m._m03, m._m13, m._m23);
                float3 centerLo = UNITY_ACCESS_INSTANCED_PROP(AtlasIconProps, _InstLow).xyz;
                float k = length(float3(m._m00, m._m10, m._m20));
                float3 upE = float3(m._m01, m._m11, m._m21);
                float3 rel = (centerHi - _AtlasRefEcefHigh.xyz) + (centerLo - _AtlasRefEcefLow.xyz);
                float3x3 g = (float3x3)_AtlasGlobeToWorld;
                float3 centerW = _AtlasRefWorld.xyz + mul(g, rel);
                float3 upW = normalize(mul(g, upE));
                float dist = distance(_WorldSpaceCameraPos.xyz, centerW) / 0.8;
                float s = _SizeWS * dist * k;
                float3 c = centerW + upW * (_LiftWS * dist + 0.5 * s);
                // Face the viewer: the camera's own right and up (no degenerate case looking straight down).
                float3 r = UNITY_MATRIX_V[0].xyz;
                float3 u = UNITY_MATRIX_V[1].xyz;
                float3 ws = c + (r * v.positionOS.x + u * v.positionOS.y) * s;
                if (dot(centerHi, -rel) < 0.0) { o.positionCS = float4(2, 2, 2, 1); return o; }
                o.positionWS = ws;
                o.positionCS = TransformWorldToHClip(ws);
                float4 cell = UNITY_ACCESS_INSTANCED_PROP(AtlasIconProps, _InstUV);
                o.uv = cell.xy + v.uv * cell.zw;
                o.color = UNITY_ACCESS_INSTANCED_PROP(AtlasIconProps, _InstColor);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                if (_AtlasLensUp.w > 0.5)
                {
                    float3 d = i.positionWS - _AtlasLens.xyz;
                    d -= dot(d, _AtlasLensUp.xyz) * _AtlasLensUp.xyz;
                    clip(_AtlasLens.w - length(d));
                }
                half4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                t *= i.color;
                clip(t.a - 0.03);
                return t;
            }
            ENDHLSL
        }
    }
}
