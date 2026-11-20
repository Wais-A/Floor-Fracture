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
        [HideInInspector][NoScaleOffset] _FloorPreviousDepth("Previous native soft depth", 2DArray) = "" {}
        [HideInInspector] _FloorPreviousWeight("Previous capture weight", Float) = 0
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
            #if defined(SOFT_OCCLUSION)
            TEXTURE2D_ARRAY(_FloorPreviousDepth);
            SAMPLER(sampler_FloorPreviousDepth);
            float4 ReadPreviousDepth(float2 uv, float eye)
            {
                return SAMPLE_TEXTURE2D_ARRAY(_FloorPreviousDepth, sampler_FloorPreviousDepth, uv, eye);
            }
            // Compile the SDK's same occlusion routine against the archived stereo texture and pose.
            #undef META_DEPTH_ENVIRONMENT_OCCLUSION_INCLUDED
            #define SamplePreprocessedDepth ReadPreviousDepth
            #define _EnvironmentDepthReprojectionMatrices _FloorPreviousReprojection
            #define _EnvironmentDepthZBufferParams _FloorPreviousZParams
            #define SampleEnvironmentDepthLinear PreviousLinearDepth
            #define CalculateEnvironmentDepthHardOcclusion PreviousHardOcclusion
            #define CalculateEnvironmentDepthSoftOcclusion PreviousSoftOcclusion
            #define CalculateEnvironmentDepthOcclusion PreviousOcclusion
            #include "Packages/com.meta.xr.sdk.core/Shaders/EnvironmentDepth/EnvironmentOcclusion.cginc"
            #undef CalculateEnvironmentDepthOcclusion
            #undef CalculateEnvironmentDepthSoftOcclusion
            #undef CalculateEnvironmentDepthHardOcclusion
            #undef SampleEnvironmentDepthLinear
            #undef _EnvironmentDepthZBufferParams
            #undef _EnvironmentDepthReprojectionMatrices
            #undef SamplePreprocessedDepth
            #endif
            #include "Assets/FloorFracture/Scripts/Room/ShaftBrickwork.hlsl"
            #include "Assets/FloorFracture/Scripts/Room/BoardFootprint.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BoardFrame;
                float4 _BoardHalfSize;
                uint _BoardMaskLow;
                uint _BoardMaskHigh;
                float _EnvironmentDepthBias;
                float _FloorPreviousWeight;
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
                half current = META_DEPTH_GET_OCCLUSION_VALUE_WORLDPOS(world, _EnvironmentDepthBias);
                #if defined(SOFT_OCCLUSION)
                if (_FloorPreviousWeight <= 0 || current <= 0 || current >= 1) return current;
                float4 priorClip = mul(_FloorPreviousReprojection[unity_StereoEyeIndex], float4(world, 1));
                if (priorClip.w <= 0 || any(abs(priorClip.xy) >= priorClip.w)) return current;
                half prior = PreviousOcclusion(world, _EnvironmentDepthBias);
                // A newly covered or newly exposed surface adopts its current measurement immediately.
                half agreement = saturate(8 * min(current, 1 - current)) * (1 - smoothstep(.55h, .85h, abs(current - prior)));
                return lerp(current, prior, _FloorPreviousWeight * agreement);
                #else
                return current;
                #endif
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
