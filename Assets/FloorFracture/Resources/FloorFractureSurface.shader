Shader "Floor Fracture/Surface"
{
    Properties
    {
        _Color("Tint", Color) = (1,1,1,1)
        _Fade("Opacity", Range(0,1)) = 1
        _Sheen("Glass sheen", Range(0,1)) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 0
        [Toggle] _ZWrite("Write depth", Float) = 0
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest("Depth test", Float) = 4
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Cull [_Cull] ZWrite [_ZWrite] ZTest [_ZTest]
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma multi_compile_instancing
            #pragma multi_compile _ HARD_OCCLUSION SOFT_OCCLUSION
            #include "Packages/com.meta.xr.sdk.core/Shaders/EnvironmentDepth/URP/EnvironmentOcclusionURP.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Fade, _Sheen;
            CBUFFER_END
            struct V { float4 vertex:POSITION; half4 color:COLOR; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct F { float4 position:SV_POSITION; half4 color:COLOR; float3 world:TEXCOORD0; half2 uv:TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            F vert(V v)
            {
                F o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.world=TransformObjectToWorld(v.vertex.xyz);
                o.position=TransformWorldToHClip(o.world);
                o.uv=v.uv; o.color=v.color*_Color; return o;
            }
            half4 frag(F i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half3 tint=i.color.rgb;
                if (_Sheen > 0)
                {
                    half3 view=GetWorldSpaceNormalizeViewDir(i.world);
                    half3 reflected=half3(-view.x,abs(view.y),-view.z);
                    half grazing=1-saturate(abs(view.y));
                    half sky=smoothstep(.60,.96,dot(reflected,half3(-.55,.64,.535)));
                    half lightCoord=dot(reflected,half3(.83,.23,-.51))+.08;
                    half broadReflection=1-smoothstep(.035,.27,abs(lightCoord));
                    // One broad reflection moves across the assembled floor. A bevel catches
                    // light at the edge of each sheet, including a moving pane.
                    half2 toEdge=min(i.uv,1-i.uv);
                    half edge=min(toEdge.x,toEdge.y);
                    half bevel=1-smoothstep(.010,.038+fwidth(edge),edge);
                    half edgeLight=.3+.7*saturate(.5+(i.uv.y-i.uv.x)*.5);
                    tint += _Sheen*(half3(.055,.105,.14)*sky + half3(.115,.21,.27)*broadReflection +
                        half3(.045,.085,.10)*grazing*grazing + half3(.10,.17,.18)*bevel*edgeLight);
                }
                half visibility = META_DEPTH_GET_OCCLUSION_VALUE_WORLDPOS(i.world, .02);
                return half4(tint,i.color.a*_Fade*visibility);
            }
            ENDHLSL
        }
    }
}
