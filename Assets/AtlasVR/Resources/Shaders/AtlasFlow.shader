// Flows: ribbons of constant angular width along arcs above the globe, with bright pulses
// racing along them over a dim base (the web deck's flow material). Drawn twice: a wide soft
// glow (_Glow = 1, additive) and the core line. Vertices: ECEF centerline (position, with the
// low part in TEXCOORD1), side direction (normal), side sign and width (uv0), and the position
// along the arc with packed pulse count and speed (uv2).
Shader "AtlasVR/Flow"
{
    Properties
    {
        _WidthWS ("Width per unit (world meters)", Float) = 0.00034
        _Glow ("Glow pass", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-5" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "AtlasFlow"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha One, Zero One
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4x4 _AtlasGlobeToWorld;
            float4 _AtlasLens;
            float4 _AtlasLensUp;
            float4 _AtlasRefWorld;
            float _AtlasStill;            // 1 holds the pulses still (the reduced-motion setting)
            float4 _AtlasRefEcefHigh;
            float4 _AtlasRefEcefLow;

            CBUFFER_START(UnityPerMaterial)
                float _WidthWS;
                float _Glow;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float3 low : TEXCOORD1;
                float2 anim : TEXCOORD2;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float2 data : TEXCOORD0;   // side, position along the arc
                nointerpolation float2 pulse : TEXCOORD1;   // count, speed (decoded per vertex)
                float3 positionWS : TEXCOORD2;
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
                // Side: across the arc and facing the eye, so the ribbon never turns edge-on.
                float3 tanW = mul(g, v.normalOS);
                float3 sideW = normalize(cross(tanW, centerW - _WorldSpaceCameraPos.xyz));
                float dist = distance(_WorldSpaceCameraPos.xyz, centerW) / 0.8;
                float3 ws = centerW + sideW * (v.uv.x * 0.5 * _WidthWS * v.uv.y * dist);
                o.positionWS = ws;
                o.positionCS = TransformWorldToHClip(ws);
                o.color = v.color;
                // Behind the Earth: hide a point once the line from it to the eye passes through the globe
                // (a sphere test in ECEF; points high on the arc stay visible over the horizon).
                float3 P = v.positionOS.xyz, D = -rel;           // from the point toward the eye
                float tc = -dot(P, D) / max(dot(D, D), 1e-6);    // closest approach along the segment
                if (tc > 0.0 && tc < 1.0 && length(P + D * tc) < 6356000.0) o.color.a = 0.0;
                o.data = float2(v.uv.x, v.anim.x);
                float count = floor(v.anim.y / 16.0 + 0.001);
                o.pulse = float2(count, v.anim.y - count * 16.0);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                if (i.color.a < 0.01) discard;
                if (_AtlasLensUp.w > 0.5)
                {
                    float3 d = i.positionWS - _AtlasLens.xyz;
                    d -= dot(d, _AtlasLensUp.xyz) * _AtlasLensUp.xyz;
                    clip(_AtlasLens.w - length(d));
                }
                float count = i.pulse.x;
                float speed = i.pulse.y;
                float t = _Time.y * speed * (1.0 - _AtlasStill);
                float v = frac(i.data.y * max(count, 1.0) - t);
                float pulse = pow(1.0 - v, 5.0) * step(0.02, v);
                float edge = 1.0 - abs(i.data.x);
                float3 rgb;
                float a;
                if (_Glow > 0.5)
                {
                    // Soft halo, brightest where a pulse passes.
                    a = edge * edge * (0.10 + 0.35 * pulse);
                    rgb = i.color.rgb;
                }
                else
                {
                    float core = smoothstep(0.0, 0.35, edge);
                    a = core * (0.5 + 0.5 * pulse);
                    rgb = lerp(i.color.rgb, float3(1, 1, 1), pulse * 0.6);
                }
                return half4(rgb, a * i.color.a);
            }
            ENDHLSL
        }
    }
}
