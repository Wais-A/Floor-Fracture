Shader "Floor Fracture/Rising room floor"
{
    Properties
    {
        [HideInInspector] _BoardFrame("Board center and orientation", Vector) = (0,0,1,0)
        [HideInInspector] _BoardHalfSize("Board half extents and floor height", Vector) = (0,0,0,0)
        [HideInInspector] _BoardMaskLow("Pane mask, cells 0 to 31", Integer) = 0
        [HideInInspector] _BoardMaskHigh("Pane mask, cells 32 to 63", Integer) = 0
        [HideInInspector] _Travel("Floor travel in metres", Float) = 0
        [HideInInspector] _OutsideOpening("Extra brick opening in a fall outside the grid", Vector) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Geometry+2" "RenderType"="Opaque" }
        Pass
        {
            Cull Off ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/FloorFracture/Scripts/Room/ShaftBrickwork.hlsl"
            #include "Assets/FloorFracture/Scripts/Room/BoardFootprint.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BoardFrame, _BoardHalfSize, _OutsideOpening;
                uint _BoardMaskLow, _BoardMaskHigh;
                float _Travel;
            CBUFFER_END
            struct Attributes { float3 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 world : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.world = TransformObjectToWorld(input.positionOS);
                output.world.y += .002;
                output.positionCS = TransformWorldToHClip(output.world);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                // Preserve the exact cutout and masonry coordinates of the anchored room floor.
                float2 delta = input.world.xz - _BoardFrame.xy;
                float2 board = float2(dot(delta, _BoardFrame.zw), dot(delta, float2(-_BoardFrame.w, _BoardFrame.z)));
                float heightAboveFloor = input.world.y - _Travel - _BoardHalfSize.z;
                if (FloorHasPane(board, heightAboveFloor, _BoardHalfSize, _BoardMaskLow, _BoardMaskHigh)) discard;
                delta = input.world.xz - _OutsideOpening.xy;
                float2 outside = abs(float2(dot(delta, _BoardFrame.zw), dot(delta, float2(-_BoardFrame.w, _BoardFrame.z))));
                clip(max(max(outside.x,outside.y) - _OutsideOpening.z, abs(heightAboveFloor) - .06));
                return half4(ShaftBrickwork(input.world.xz), 1);
            }
            ENDHLSL
        }
    }
}
