// A soft blue glow at the Earth's limb, seen from space or around the tabletop globe.
Shader "AtlasVR/Atmosphere"
{
    Properties
    {
        _Color ("Color", Color) = (0.35, 0.6, 1.0, 1)
        _Strength ("Strength", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-50" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "AtlasAtmosphere"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Strength;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 centerWS : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.centerWS = TransformObjectToWorld(float3(0, 0, 0));
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 n = normalize(i.positionWS - i.centerWS);
                float3 v = normalize(_WorldSpaceCameraPos.xyz - i.positionWS);
                float rim = 1.0 - saturate(abs(dot(n, v)));
                float glow = pow(rim, 3.0) * 1.4 + pow(rim, 12.0) * 1.2;
                return half4(_Color.rgb * glow * _Strength, 1.0);
            }
            ENDHLSL
        }
    }
}
