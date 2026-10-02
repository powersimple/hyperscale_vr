// A starfield of single-pixel points on a sphere around the eye.
Shader "AtlasVR/Stars"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry+500" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "AtlasStars"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float4 color : COLOR; float size : PSIZE; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.color = v.color;
                o.size = 1.0;
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
