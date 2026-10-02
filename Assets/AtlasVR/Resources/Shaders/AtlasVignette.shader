// The comfort vignette, drawn per eye in clip space so its clear circle stays centered in
// each eye's view (a mesh in front of the head would sit off-center in both eyes).
Shader "AtlasVR/Vignette"
{
    Properties
    {
        _Inner ("Clear radius (NDC)", Float) = 0.9
        _Alpha ("Strength", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Overlay+50" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "AtlasVignette"
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

            CBUFFER_START(UnityPerMaterial)
                float _Inner;
                float _Alpha;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 ndc : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = float4(v.positionOS.xy, UNITY_NEAR_CLIP_VALUE, 1.0);
                o.ndc = v.positionOS.xy;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float d = length(i.ndc);
                float a = smoothstep(_Inner, _Inner + 0.35, d) * _Alpha;
                return half4(0, 0, 0, a);
            }
            ENDHLSL
        }
    }
}
