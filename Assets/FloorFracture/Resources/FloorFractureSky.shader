Shader "Floor Fracture/Sky flash"
{
    SubShader
    {
        // A soft disc above the lamps. It is not depth-occluded, so a low ceiling cannot hide the flash.
        Tags { "Queue"="Transparent+3" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Cull Off ZWrite Off ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float _FloorFractureFlash; // global, set by Lightning
            struct V { float4 p:POSITION; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct F { float4 p:SV_POSITION; float2 uv:TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            F vert(V v)
            {
                F o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.p = TransformObjectToHClip(v.p.xyz);
                o.uv = v.uv;
                return o;
            }
            half4 frag(F i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float alpha = .45 * _FloorFractureFlash * (1 - smoothstep(0, 1, length(i.uv * 2 - 1)));
                return half4(.88, .92, 1, alpha);
            }
            ENDHLSL
        }
    }
}
