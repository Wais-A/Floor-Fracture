Shader "Floor Fracture/Scoreboard"
{
    Properties
    {
        _MainTex("Font atlas", 2D) = "white" {}
        [Toggle] _ZWrite("Write depth", Float) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Cull Off ZTest LEqual ZWrite [_ZWrite]
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #pragma multi_compile_instancing
            #pragma multi_compile _ HARD_OCCLUSION SOFT_OCCLUSION
            #include "Packages/com.meta.xr.sdk.core/Shaders/EnvironmentDepth/URP/EnvironmentOcclusionURP.hlsl"
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            struct Attributes
            {
                float4 position : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 position : SV_POSITION;
                float3 world : TEXCOORD0;
                float2 uv : TEXCOORD1;
                half4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.world = TransformObjectToWorld(i.position.xyz);
                o.position = TransformWorldToHClip(o.world);
                o.uv = i.uv;
                o.color = i.color;
                return o;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half alpha = i.color.a * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a;
                // Allow modest scan noise at a flush mount. Both text and housing
                // use the same per-eye environment depth comparison.
                alpha *= META_DEPTH_GET_OCCLUSION_VALUE_WORLDPOS(i.world, .02);
                clip(alpha - .001h);
                return half4(i.color.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
