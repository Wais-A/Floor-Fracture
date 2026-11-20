Shader "Floor Fracture/Surrounding Water"
{
    Properties
    {
        [Normal] _WaterNormal("Floor Zone ripples", 2D) = "bump" {}
        _DeepColor("Deep water", Color) = (0.035, 0.28, 0.33, 1)
        _ShallowColor("Reflected light", Color) = (0.24, 0.66, 0.67, 1)
        [HideInInspector] _BoardFrame("Board center and orientation", Vector) = (0,0,1,0)
        [HideInInspector] _BoardHalfSize("Board half extents and floor height", Vector) = (0,0,0,0)
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

            TEXTURE2D(_WaterNormal);
            SAMPLER(sampler_WaterNormal);
            CBUFFER_START(UnityPerMaterial)
                float4 _BoardFrame;
                float4 _BoardHalfSize;
                half4 _DeepColor;
                half4 _ShallowColor;
            CBUFFER_END
            struct Attributes { float3 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 world : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.world = TransformObjectToWorld(input.positionOS);
                output.positionCS = TransformWorldToHClip(output.world);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 delta = input.world.xz - _BoardFrame.xy;
                float2 board = float2(dot(delta, _BoardFrame.zw), dot(delta, float2(-_BoardFrame.w, _BoardFrame.z)));
                float2 exterior = abs(board) - _BoardHalfSize.xy;
                // Limit the opening to the board's own storey, including the accepted floor slope.
                clip(max(max(exterior.x, exterior.y), abs(input.world.y - _BoardHalfSize.z) - .06));

                // Two moving scales of the approved Floor Zone normal map soften the repeat across long rooms.
                float2 uv = input.world.xz;
                half2 broad = UnpackNormal(SAMPLE_TEXTURE2D(_WaterNormal, sampler_WaterNormal, uv * .42 + _Time.y * float2(.012, .018))).xy;
                half2 fine = UnpackNormal(SAMPLE_TEXTURE2D(_WaterNormal, sampler_WaterNormal, uv.yx * .83 + _Time.y * float2(-.022, .009))).yx;
                half2 ripple = broad * .7h + fine * .3h;
                half3 normalWS = normalize(half3(ripple.x, .85h, ripple.y));
                half3 view = GetWorldSpaceNormalizeViewDir(input.world);
                half reflection = 1.0h - saturate(dot(normalWS, view));
                half sheen = saturate(.25h + dot(ripple, half2(.58h, -.42h)) + .44h * reflection * reflection);
                half3 color = lerp(_DeepColor.rgb, _ShallowColor.rgb, sheen);
                // Account for small differences between the saved floor plane and live Quest depth.
                half coverage = META_DEPTH_GET_OCCLUSION_VALUE_WORLDPOS(input.world, .035);
                return half4(color * coverage, coverage);
            }
            ENDHLSL
        }
    }
}
