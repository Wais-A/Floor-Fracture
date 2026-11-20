Shader "Floor Fracture/Surrounding Brick"
{
    Properties
    {
        _BrickColor("Brick", Color) = (.49,.39,.33,1)
        _MortarColor("Recessed mortar", Color) = (.24,.225,.21,1)
        [HideInInspector] _BoardFrame("Board center and orientation", Vector) = (0,0,1,0)
        [HideInInspector] _BoardHalfSize("Board half extents and floor height", Vector) = (0,0,0,0)
        [HideInInspector] _RequireEnvironmentDepth("Require furniture occlusion", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-20" "RenderType"="Transparent" }
        Pass
        {
            Cull Off
            ZWrite Off
            Blend One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ HARD_OCCLUSION SOFT_OCCLUSION
            #include "Packages/com.meta.xr.sdk.core/Shaders/EnvironmentDepth/URP/EnvironmentOcclusionURP.hlsl"
            #include "Assets/FloorFracture/Scripts/Room/ShaftBrickwork.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BoardFrame;
                float4 _BoardHalfSize;
                half4 _BrickColor;
                half4 _MortarColor;
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

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                #if !defined(HARD_OCCLUSION) && !defined(SOFT_OCCLUSION)
                    clip(.5 - _RequireEnvironmentDepth);
                #endif
                float2 delta = input.world.xz - _BoardFrame.xy;
                float2 board = float2(dot(delta, _BoardFrame.zw), dot(delta, float2(-_BoardFrame.w, _BoardFrame.z)));
                float2 exterior = abs(board) - _BoardHalfSize.xy;
                clip(max(max(exterior.x, exterior.y), abs(input.world.y - _BoardHalfSize.z) - .06));

                // One world-space bond carries across every scanned patch, even when the board moves.
                float2 courses = input.world.xz / float2(.54, .27);
                float2 cell = ShaftBrickCell(courses, 0);
                float2 width = fwidth(courses);
                float face = ShaftBrickFace(cell, min(.16, max(width.x, width.y) * .5));
                float2 unit = frac(cell);
                float2 brickId = floor(cell);
                float shade = frac(52.9829189 * frac(dot(brickId, float2(.06711056,.00583715))));
                float bevel = smoothstep(.018,.095,min(unit.x,1-unit.y)) - smoothstep(.018,.095,min(1-unit.x,unit.y));
                float grain = sin(courses.x * 16.3 + sin(courses.y * 5.1)) * sin(courses.y * 23.7 + sin(courses.x * 4.6));
                half3 clay = _BrickColor.rgb * (.85 + .25 * shade + .13 * bevel + .028 * grain);
                half3 color = lerp(_MortarColor.rgb, clay, face);
                half coverage = META_DEPTH_GET_OCCLUSION_VALUE_WORLDPOS(input.world, 0.0);
                return half4(color * coverage, coverage);
            }
            ENDHLSL
        }
    }
}
