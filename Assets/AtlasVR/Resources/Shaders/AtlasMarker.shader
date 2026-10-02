// Instanced markers on the globe. Each instance matrix carries an ECEF position and an
// east-up-north frame scaled by the marker's relative size; _AtlasGlobeToWorld (set each
// frame by the globe rig) carries ECEF into the Unity world. Markers keep a constant
// angular size (sized as _SizeWS at 0.8 m from the eye), draw over terrain as the web deck
// draws them, and hide behind the Earth's horizon and outside the tabletop lens.
Shader "AtlasVR/Marker"
{
    Properties
    {
        _SizeWS ("Size (world meters)", Float) = 0.004
        _LiftWS ("Lift above the surface (world meters)", Float) = 0.002
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 10
        [Toggle] _ZWrite ("ZWrite", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "AtlasMarker"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4x4 _AtlasGlobeToWorld;  // ECEF -> world (its 3x3 part: rotation and scale)
            float _AtlasGlobeScale;
            float4 _AtlasLens;            // xyz center (world), w radius
            float4 _AtlasLensUp;          // xyz up, w > 0.5 when the lens clips
            float4 _AtlasRefWorld;        // the reference point (the eye) in the world
            float4 _AtlasRefEcefHigh;     // the same point in ECEF, float part
            float4 _AtlasRefEcefLow;      // and its remainder

            CBUFFER_START(UnityPerMaterial)
                float _SizeWS;
                float _LiftWS;
                float _SrcBlend;
                float _DstBlend;
                float _ZWrite;
            CBUFFER_END

            UNITY_INSTANCING_BUFFER_START(AtlasProps)
                UNITY_DEFINE_INSTANCED_PROP(float4, _InstColor)
                UNITY_DEFINE_INSTANCED_PROP(float4, _InstLow)
            UNITY_INSTANCING_BUFFER_END(AtlasProps)

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float3 positionWS : TEXCOORD0;
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
                float3 centerLo = UNITY_ACCESS_INSTANCED_PROP(AtlasProps, _InstLow).xyz;
                float3 offE = mul((float3x3)m, v.positionOS.xyz);
                float3 upE = float3(m._m01, m._m11, m._m21);

                // Relative to the eye in ECEF, high and low parts subtracted separately: exact up close.
                float3 rel = (centerHi - _AtlasRefEcefHigh.xyz) + (centerLo - _AtlasRefEcefLow.xyz);
                float3x3 g = (float3x3)_AtlasGlobeToWorld;
                float3 centerW = _AtlasRefWorld.xyz + mul(g, rel);
                float3 offW = mul(g, offE) / max(_AtlasGlobeScale, 1e-12);
                float3 upW = normalize(mul(g, upE));
                float dist = distance(_WorldSpaceCameraPos.xyz, centerW) / 0.8;
                float3 ws = centerW + offW * (_SizeWS * dist) + upW * (_LiftWS * dist);

                // Behind the Earth's horizon (the eye is below the point's tangent plane): draw nothing.
                if (dot(centerHi, -rel) < 0.0)
                {
                    o.positionCS = float4(2.0, 2.0, 2.0, 1.0);
                    return o;
                }

                o.positionWS = ws;
                o.positionCS = TransformWorldToHClip(ws);
                o.color = UNITY_ACCESS_INSTANCED_PROP(AtlasProps, _InstColor);
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
                return half4(i.color);
            }
            ENDHLSL
        }
    }
}
