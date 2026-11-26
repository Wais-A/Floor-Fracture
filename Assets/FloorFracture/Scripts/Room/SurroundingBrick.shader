Shader "Floor Fracture/Surrounding Brick"
{
    Properties
    {
        [HideInInspector] _PortalComparison("Outside fall preview aperture test", Float) = 8
        [HideInInspector] _BoardFrame("Board center and orientation", Vector) = (0,0,1,0)
        [HideInInspector] _BoardHalfSize("Board half extents and floor height", Vector) = (0,0,0,0)
        [HideInInspector] _BoardMaskLow("Pane mask, cells 0 to 31", Integer) = 0
        [HideInInspector] _BoardMaskHigh("Pane mask, cells 32 to 63", Integer) = 0
        _EnvironmentDepthBias("Relative floor depth bias", Range(0, .1)) = .045
        [HideInInspector] _RequireEnvironmentDepth("Require furniture occlusion", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-20" "RenderType"="Transparent" }
        Pass
        {
            Stencil { Ref 4 ReadMask 4 WriteMask 0 Comp [_PortalComparison] Pass Keep }
            Cull Off
            ZWrite Off
            Blend One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ HARD_OCCLUSION SOFT_OCCLUSION
            #include "Packages/com.meta.xr.sdk.core/Shaders/EnvironmentDepth/URP/EnvironmentOcclusionURP.hlsl"
            #include "Assets/FloorFracture/Scripts/Room/ShaftBrickwork.hlsl"
            #include "Assets/FloorFracture/Scripts/Room/BoardFootprint.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BoardFrame;
                float4 _BoardHalfSize;
                uint _BoardMaskLow;
                uint _BoardMaskHigh;
                float _EnvironmentDepthBias;
                float _RequireEnvironmentDepth;
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

            half FloorCoverage(float3 world)
            {
                return META_DEPTH_GET_OCCLUSION_VALUE_WORLDPOS(world, _EnvironmentDepthBias);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                #if !defined(HARD_OCCLUSION) && !defined(SOFT_OCCLUSION)
                    clip(.5 - _RequireEnvironmentDepth);
                #endif
                float2 delta = input.world.xz - _BoardFrame.xy;
                float2 board = float2(dot(delta, _BoardFrame.zw), dot(delta, float2(-_BoardFrame.w, _BoardFrame.z)));
                if (FloorHasPane(board, input.world.y - _BoardHalfSize.z, _BoardHalfSize, _BoardMaskLow, _BoardMaskHigh)) discard;

                // One world-space bond carries across every scanned patch, even when the board moves.
                half3 color = ShaftBrickwork(input.world.xz);
                half coverage = FloorCoverage(input.world);
                return half4(color * coverage, coverage);
            }
            ENDHLSL
        }
    }
}
