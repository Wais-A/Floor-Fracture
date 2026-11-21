Shader "Floor Fracture/Ending view fade"
{
    Properties
    {
        _Tint("Tint and opacity", Color) = (0,0,0,0)
        _InteriorPlane("Surface level, line width, submerged, water", Vector) = (0,0,0,0)
        _Brightness("Interior light", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Overlay+200" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Off ZTest Always ZWrite Off
            // At full opacity the projection layer also covers the passthrough underlay.
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                float4 _InteriorPlane;
                half _Brightness;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 world : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            Varyings Vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.world = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.world);
                return o;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                if (_InteriorPlane.y == 0) return _Tint;
                float3 eye = GetCameraPositionWS();
                float up = normalize(i.world-eye).y;
                // Downward rays meet the moving material first. Even an upward ray
                // is fully covered when the horizontal surface reaches this eye.
                float distance = _InteriorPlane.x-eye.y + .5*_InteriorPlane.y*(1-up);
                half cover = max(_InteriorPlane.z,smoothstep(-.012,0,distance));
                half light = _Brightness;
                if (_InteriorPlane.w > 0)
                    light *= lerp(.22,1.48,smoothstep(-1,1,up));
                half3 interior = _Tint.rgb*light;
                half edge = 4*cover*(1-cover);
                return half4(interior + half3(.12,.076,.035)*edge,cover);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
