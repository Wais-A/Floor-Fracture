// Adapted from Meta's MIT-licensed MRUK Floor Zone Stencil.shader.
// The mask writes one stencil bit in both eyes and leaves passthrough color intact.
Shader "Floor Fracture/Hole Mask"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Geometry-5" }
        Pass
        {
            ColorMask 0 ZWrite Off Cull Off
            Stencil { Ref 4 ReadMask 4 WriteMask 4 Comp Always Pass Replace }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct V { float4 vertex:POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct F { float4 position:SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
            F vert(V v) { F o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.position=TransformObjectToHClip(v.vertex.xyz); return o; }
            half4 frag(F i):SV_Target { return 0; }
            ENDHLSL
        }
    }
}
