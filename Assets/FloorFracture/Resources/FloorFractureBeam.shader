Shader "Floor Fracture/Overhead beam"
{
    Properties
    {
        [HideInInspector] _MappedFloorY("Mapped floor height", Float) = -10000
        [HideInInspector] _Pool("Draw as floor pool", Float) = 0
    }
    SubShader
    {
        // OverheadLights sets the queue one above the fog, so the pools wash out the fog and the cracks.
        Tags { "Queue"="Transparent+2" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
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
            CBUFFER_START(UnityPerMaterial)
                float _MappedFloorY, _Fade, _Pool;
            CBUFFER_END
            struct V { float4 p:POSITION; float3 n:NORMAL; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct F { float4 p:SV_POSITION; float3 world:TEXCOORD0; float3 normal:TEXCOORD1; float2 uv:TEXCOORD2; UNITY_VERTEX_OUTPUT_STEREO };
            F vert(V v)
            {
                F o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.world = TransformObjectToWorld(v.p.xyz); o.p = TransformWorldToHClip(o.world);
                o.normal = TransformObjectToWorldNormal(v.n, false); // the pool mesh carries no normals
                o.uv = v.uv;
                return o;
            }
            half4 frag(F i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half visibility = GlassFloorVisibility(i.world, _MappedFloorY);
                float alpha;
                if (_Pool > .5)
                    alpha = .55 * (1 - smoothstep(0, 1, length(i.uv * 2 - 1)));
                else
                {
                    // Facing squared is 1 - r² across the beam, 0 at its silhouette. Both walls draw
                    // (Cull Off), and two layers of 0.0945 combine to 0.18 along the axis.
                    float facing = abs(dot(normalize(i.normal), GetWorldSpaceNormalizeViewDir(i.world)));
                    alpha = .0945 * facing * facing;
                }
                return half4(1, .96, .88, alpha * _Fade * visibility);
            }
            ENDHLSL
        }
    }
}
