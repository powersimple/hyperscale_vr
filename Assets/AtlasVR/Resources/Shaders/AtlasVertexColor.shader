// Unlit vertex color with alpha: the laser, the reticle, and the comfort vignette.
Shader "AtlasVR/VertexColor"
{
    Properties
    {
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+100" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "AtlasVertexColor"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest [_ZTest]
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Tint;
                float _ZTest;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float4 color : COLOR; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.color = v.color * _Tint;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                return half4(i.color);
            }
            ENDHLSL
        }
    }
}
