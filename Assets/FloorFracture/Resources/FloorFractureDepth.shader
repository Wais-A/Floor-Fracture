Shader "Floor Fracture/Shaft and falling glass"
{
    Properties { _Color("Tint", Color)=(1,1,1,1) _Courses("Wall detail", Range(0,1))=0 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Geometry+2" }
        Pass
        {
            Cull Off ZWrite On
            Stencil { Ref 4 ReadMask 4 WriteMask 0 Comp Equal Pass Keep }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/FloorFracture/Scripts/Room/ShaftBrickwork.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Courses;
            CBUFFER_END
            struct V { float4 vertex:POSITION; half4 color:COLOR; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct F { float4 position:SV_POSITION; half4 color:COLOR; float2 uv:TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            F vert(V v) { F o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.position=TransformObjectToHClip(v.vertex.xyz); o.color=v.color*_Color; o.uv=v.uv; return o; }
            half4 frag(F i):SV_Target
            {
                if (_Courses > .5)
                    return half4(i.color.rgb * ShaftBrickwork(i.uv), 1);
                return half4(i.color.rgb, 1);
            }
            ENDHLSL
        }
    }
}
