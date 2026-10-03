// Lit solid for the Academy emblem: a key light, a soft fill, a specular highlight, and a rim,
// all computed in the shader so the look does not depend on the scene's lights.
Shader "AtlasVR/Emblem"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _Metal ("Metallic", Range(0, 1)) = 0.8
        _LightDir ("Light direction (world)", Vector) = (0.4, 0.8, -0.45, 0)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "AtlasEmblem"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Metal;
                float4 _LightDir;
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
                o.v = normalize(_WorldSpaceCameraPos.xyz - ws);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 n = normalize(i.n), v = normalize(i.v);
                float3 l = normalize(_LightDir.xyz);
                float diff = saturate(dot(n, l)) * 0.75 + 0.25 * (0.5 + 0.5 * n.y);
                float spec = pow(saturate(dot(n, normalize(l + v))), 48.0);
                float rim = pow(1.0 - saturate(dot(n, v)), 3.0);
                float3 base = _Color.rgb;
                float3 specCol = lerp(float3(1, 1, 1), base * 2.5 + 0.15, _Metal);
                float3 c = base * diff * (1.0 - _Metal * 0.5) + specCol * spec * 0.9 + (base + 0.12) * rim * 0.6;
                return half4(c, 1.0);
            }
            ENDHLSL
        }
    }
}
