Shader "Floor Fracture/Shaft and falling glass"
{
    Properties
    {
        _Color("Tint", Color)=(1,1,1,1)
        [Toggle(_OPENING_CONCRETE)] _OpeningConcrete("Concrete wall", Float)=0
        _ConcreteMap("Concrete grain", 2D)="white" {}
        [Enum(UnityEngine.Rendering.CompareFunction)] _PortalComparison("Floor aperture test", Float) = 3
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Geometry+2" }
        Pass
        {
            Cull Off ZWrite On
            Stencil { Ref 4 ReadMask 4 WriteMask 0 Comp [_PortalComparison] Pass Keep }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            // Falling shards create their plain material at runtime. Keep both variants in the player.
            #pragma multi_compile_local _ _OPENING_CONCRETE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_ConcreteMap);
            SAMPLER(sampler_ConcreteMap);
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float4 _ConcreteMap_ST;
            CBUFFER_END
            struct V { float4 vertex:POSITION; half4 color:COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct F
            {
                float4 position:SV_POSITION;
                half4 color:COLOR;
                #if defined(_OPENING_CONCRETE)
                    float2 grainUV:TEXCOORD0;
                #endif
                UNITY_VERTEX_OUTPUT_STEREO
            };
            F vert(V v)
            {
                F o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.position=TransformObjectToHClip(v.vertex.xyz);
                o.color=v.color*_Color;
                #if defined(_OPENING_CONCRETE)
                    // The walls follow the board axes. Both faces of a corner sample the same grain.
                    float2 metres=float2(v.vertex.x + v.vertex.z, -v.vertex.y);
                    o.grainUV=TRANSFORM_TEX(metres, _ConcreteMap);
                #endif
                return o;
            }
            half4 frag(F i):SV_Target
            {
                half3 color=i.color.rgb;
                #if defined(_OPENING_CONCRETE)
                    // Meta's concrete detail map has a near-white base (0.925 in linear space).
                    half grain=SAMPLE_TEXTURE2D(_ConcreteMap, sampler_ConcreteMap, i.grainUV).r;
                    color *= clamp(1.0h + (grain - .925h) * 6.0h, .58h, 1.38h);
                #endif
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
}
