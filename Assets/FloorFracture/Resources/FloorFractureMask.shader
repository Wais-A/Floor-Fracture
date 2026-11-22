// Adapted from Meta's MIT-licensed MRUK Floor Zone Stencil.shader.
// The mask writes one stencil bit in both eyes and leaves passthrough color intact.
Shader "Floor Fracture/Hole Mask"
{
    Properties
    {
        [HideInInspector] _MappedFloorY("Mapped floor height", Float) = -10000
        [HideInInspector] _CoveredPane0("First moving glass footprint", Vector) = (0,0,0,0)
        [HideInInspector] _CoveredPane1("Second moving glass footprint", Vector) = (0,0,0,0)
    }
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
            #pragma target 3.5
            #pragma multi_compile_instancing
            #pragma multi_compile _ HARD_OCCLUSION SOFT_OCCLUSION
            #include "Assets/FloorFracture/Scripts/Glass/GlassFloorOcclusion.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _MappedFloorY;
                float4 _CoveredPane0, _CoveredPane1;
            CBUFFER_END
            struct V { float4 vertex:POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct F { float4 position:SV_POSITION; float3 world:TEXCOORD0; float2 boardXZ:TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            F vert(V v) { F o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.boardXZ=v.vertex.xz; o.world=TransformObjectToWorld(v.vertex.xyz); o.position=TransformWorldToHClip(o.world); return o; }
            half4 frag(F i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 a = abs(i.boardXZ - _CoveredPane0.xy), b = abs(i.boardXZ - _CoveredPane1.xy);
                clip(min(max(a.x,a.y) - _CoveredPane0.z, max(b.x,b.y) - _CoveredPane1.z));
                half visibility = GlassFloorVisibility(i.world, _MappedFloorY);
                clip(visibility - .5h);
                return 0;
            }
            ENDHLSL
        }
    }
}
