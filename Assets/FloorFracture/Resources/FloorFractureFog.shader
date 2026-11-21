Shader "Floor Fracture/Low fog"
{
    Properties { [HideInInspector] _MappedFloorY("Mapped floor height", Float) = -10000 }
    SubShader
    {
        Tags { "Queue"="Transparent+10" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
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
                float _MappedFloorY;
            CBUFFER_END
            struct V { float4 p:POSITION; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct F { float4 p:SV_POSITION; float2 uv:TEXCOORD0; float3 world:TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            F vert(V v) { F o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.world=TransformObjectToWorld(v.p.xyz); o.p=TransformWorldToHClip(o.world); o.uv=v.uv; return o; }
            half4 frag(F i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half visibility = GlassFloorVisibility(i.world, _MappedFloorY);
                float2 uv=i.uv;
                float phase=.50+.39*sin(_Time.y*.56)+.06*sin(uv.x*9+_Time.y);
                float bank=saturate(1-abs(uv.y-phase)*4.1);
                float2 edge=min(uv,1-uv);
                float fade=smoothstep(.0,.10,min(edge.x,edge.y));
                float wisps=.6+.4*sin(uv.x*14+uv.y*12-_Time.y*1.2);
                return half4(.57,.77,.78,bank*bank*fade*wisps*.45*visibility);
            }
            ENDHLSL
        }
    }
}
