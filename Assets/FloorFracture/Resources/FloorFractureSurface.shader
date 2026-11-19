Shader "Floor Fracture/Surface"
{
    Properties
    {
        _Color("Tint", Color) = (1,1,1,1)
        _Fade("Opacity", Range(0,1)) = 1
        _Sheen("Glass sheen", Range(0,1)) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 0
        [Toggle] _ZWrite("Write depth", Float) = 0
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest("Depth test", Float) = 4
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Cull [_Cull] ZWrite [_ZWrite] ZTest [_ZTest]
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Fade, _Sheen;
            CBUFFER_END
            struct V { float4 vertex:POSITION; half4 color:COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct F { float4 position:SV_POSITION; half4 color:COLOR; float3 local:TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            F vert(V v)
            {
                F o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.position=TransformObjectToHClip(v.vertex.xyz); o.local=v.vertex.xyz; o.color=v.color*_Color; return o;
            }
            half4 frag(F i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half streak=saturate(1-abs(i.local.x*1.4+i.local.z-0.08)*6);
                return half4(i.color.rgb+_Sheen*streak*streak*half3(.10,.18,.23),i.color.a*_Fade);
            }
            ENDHLSL
        }
    }
}
