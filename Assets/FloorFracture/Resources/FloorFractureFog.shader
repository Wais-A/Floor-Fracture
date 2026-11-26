Shader "Floor Fracture/Floor fog"
{
    Properties
    {
        [HideInInspector] _MappedFloorY("Mapped floor height", Float) = -10000
        [HideInInspector][NoScaleOffset] _Noise("Tileable noise", 2D) = "gray" {}
    }
    SubShader
    {
        // GlassBoardView also sets the queue one above the crack material, so the fog dims the cracks.
        Tags { "Queue"="Transparent+1" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Cull Off ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma multi_compile_instancing
            #pragma multi_compile _ HARD_OCCLUSION SOFT_OCCLUSION
            #include "Assets/FloorFracture/Scripts/Glass/GlassFloorOcclusion.hlsl"
            TEXTURE2D(_Noise); SAMPLER(sampler_Noise);
            CBUFFER_START(UnityPerMaterial)
                float _MappedFloorY, _Density, _Fade, _FogTime;
                float4 _Extent, _Drift;
            CBUFFER_END
            float _FloorFractureFlash; // global lightning brightness, kept out of UnityPerMaterial
            // Board-local x, z, radius, strength. Unused patches carry radius 1 and strength 0.
            float4 _Patches[3];
            struct V { float4 p:POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct F { float4 p:SV_POSITION; float2 local:TEXCOORD0; float3 world:TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            F vert(V v)
            {
                F o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.world = TransformObjectToWorld(v.p.xyz); o.p = TransformWorldToHClip(o.world);
                o.local = v.p.xz; // slices only rise, so mesh x, z are board-local
                return o;
            }
            half4 frag(F i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half visibility = GlassFloorVisibility(i.world, _MappedFloorY);
                float height = i.world.y - _MappedFloorY;
                float2 p = i.local;
                float t = _FogTime;
                float a = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, p * .45 + _Drift.xy + float2(.031, .017) * t + height * 3.1).r;
                float b = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, p * .9 + _Drift.zw - float2(.022, .036) * t - height * 1.7).r;
                float noise = saturate(1.5 * (.6 * a + .4 * b) - .25);
                float alpha = _Density * noise * saturate(1 - height / .40);
                for (int k = 0; k < 3; ++k)
                {
                    float4 patch = _Patches[k];
                    alpha += patch.w * (1 - smoothstep(.25 * patch.z, patch.z, distance(p, patch.xy))) * (.7 + .6 * noise);
                }
                float2 edge = _Extent.xy - abs(p);
                alpha *= _Fade * smoothstep(0, .30, min(edge.x, edge.y));
                // Four slices at 0.26 combine to at most 1 - 0.74^4 = 0.70.
                // Lightning lifts the fog toward white and thickens it by up to half.
                return half4(half3(.80, .84, .86) + .16 * _FloorFractureFlash, min(alpha, .26) * (1 + .5 * _FloorFractureFlash) * visibility);
            }
            ENDHLSL
        }
    }
}
