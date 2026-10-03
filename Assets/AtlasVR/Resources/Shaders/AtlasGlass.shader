// Glass for the HUD backings and the compass: a faint tinted body with a bright Fresnel rim and
// a soft top highlight, translucent so the Earth shows through.
Shader "AtlasVR/Glass"
{
    Properties
    {
        _Color ("Body", Color) = (0.08, 0.14, 0.22, 0.28)
        _RimColor ("Rim", Color) = (0.45, 0.82, 1.0, 0.9)
        _RimPower ("Rim power", Float) = 2.5
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "AtlasGlass"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _RimColor;
                float _RimPower;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 n : TEXCOORD0; float3 v : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes i)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 ws = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(ws);
                o.n = TransformObjectToWorldNormal(i.normalOS);
                o.v = _WorldSpaceCameraPos.xyz - ws;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 n = normalize(i.n), v = normalize(i.v);
                float rim = pow(1.0 - saturate(abs(dot(n, v))), _RimPower);
                float top = saturate(n.y) * 0.35;
                float3 rgb = _Color.rgb + _RimColor.rgb * rim + top * 0.25;
                float a = saturate(_Color.a + _RimColor.a * rim * 0.85 + top * 0.12);
                return half4(rgb, a);
            }
            ENDHLSL
        }
    }
}
