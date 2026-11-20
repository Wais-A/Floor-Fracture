Shader "Floor Fracture/Ending chamber"
{
    Properties
    {
        _Tint("Tint", Color) = (1,1,1,1)
        _Role("Surface", Float) = 0
        _Theme("Ending", Float) = 0
        _Age("Seconds", Float) = 0
        _Reveal("Reveal", Float) = 0
        _Level("World surface height", Float) = -20
        _Motion("Motion", Float) = 1
        _Opacity("Opacity", Float) = 1
        _Mouth("World opening center and half width", Vector) = (0,0,0,1)
        _MouthRight("Opening right, width ratio and clipping", Vector) = (1,0,1,0)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry+25" }
        Pass
        {
            Name "EndingSurface"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Off
            ZWrite On
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/FloorFracture/Scripts/Room/ShaftBrickwork.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                float _Role, _Theme, _Age, _Reveal, _Level, _Motion, _Opacity;
                float4 _Mouth, _MouthRight;
            CBUFFER_END
            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                float2 seed : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                float3 world : TEXCOORD1;
                float seed : TEXCOORD2;
                float shaftHeight : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 p = v.positionOS.xyz;
                float seed = v.seed.x;
                o.shaftHeight = p.y;
                if (_Role == 1 || _Role == 9)
                    p.xz *= lerp(_MouthRight.z,1,smoothstep(0,1.35,-p.y));
                if (_Role == 6 && _Theme != 3)
                {
                    float direction = (_Theme == 4 || _Theme == 5) ? -1 : 1;
                    float speed = _Theme == 4 ? .30 : .18;
                    float drift = (frac(seed + _Age * speed) - seed) * direction;
                    p.y += drift * 4.3 * _Motion;
                    p.x += (_Theme == 5 ? drift * 2.9 : sin(_Age*.93+seed*40)*.075) * _Motion;
                    p.z += sin(_Age+seed*25) * .05 * _Motion;
                }
                if (_Role == 7)
                {
                    float licks = sin(_Age*3.9+seed*35)*.5 + sin(_Age*6.3+seed*21)*.24;
                    p.x += licks * v.uv.y * .16 * _Motion;
                    p.y += licks * v.uv.y * .22 * _Motion;
                }
                o.world = TransformObjectToWorld(p);
                o.positionCS = TransformWorldToHClip(o.world);
                o.uv = v.uv; o.color = v.color; o.seed = seed;
                return o;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half3 color = i.color.rgb * _Tint.rgb;
                float2 xz = i.world.xz;
                if (_MouthRight.w > .5)
                {
                    // Each eye sees below-floor objects only through the broken tile until
                    // the top of the shaft passes that eye. Passthrough surrounds the opening.
                    float3 eye = GetCameraPositionWS();
                    if (eye.y > _Mouth.y + .003)
                    {
                        float down = eye.y - i.world.y;
                        clip(down - .001);
                        float t = (eye.y - _Mouth.y) / max(down,.001);
                        clip(1.001 - t);
                        float2 hit = lerp(eye.xz,xz,t) - _Mouth.xz;
                        float2 right = _MouthRight.xy;
                        float2 local = abs(float2(dot(hit,right),dot(hit,float2(-right.y,right.x))));
                        clip(_Mouth.w - max(local.x,local.y));
                        clip(_Mouth.w*1.76 - local.x - local.y);
                    }
                }
                if (_Role == 1)
                {
                    float row = -i.shaftHeight/.27;
                    float2 cell = ShaftBrickCell(float2(i.uv.x * 4, row), i.seed * .19);
                    float mortar = ShaftBrickFace(cell, 0);
                    color *= (.64+.36*mortar) * (.82+.18*sin(floor(row)*2.41+i.seed));
                    if (_Theme == 1)
                    {
                        float glow = saturate(1-abs(i.world.y - _Level - .18)*.40);
                        color += half3(.58,.105,.008) * glow;
                    }
                    if (_Theme == 0)
                    {
                        float flooded = 1-smoothstep(_Level-.035,_Level+.035,i.world.y);
                        float caustic = .5+.5*sin(xz.x*4.8+i.world.y*2.4+_Age*1.1)*sin(xz.y*4.1-i.world.y*3.2-_Age*.7);
                        color = lerp(color, color*half3(.27,.73,.83)+half3(.012,.12,.15)*caustic, flooded);
                    }
                }
                else if (_Role == 2)
                {
                    float bands = sin(xz.x*7.6+xz.y*3.2+_Age*1.7) + sin(xz.y*10.1-xz.x*2.1-_Age*1.4);
                    color += half3(.04,.22,.24)*smoothstep(1.23,1.9,bands);
                }
                else if (_Role == 3)
                {
                    float channels = abs(sin(xz.x*4+sin(xz.y*5+_Age*.32))+sin(xz.y*3.8-sin(xz.x*3.1-_Age*.23)));
                    color = lerp(half3(.26,.026,.004),half3(1,.47,.022),smoothstep(.07,.55,channels));
                }
                else if (_Role == 4)
                {
                    color *= .77+.23*sin(xz.x*13 + sin(xz.y*3.1)*2.2);
                }
                else if (_Role == 5)
                {
                    float glow = smoothstep(i.seed,i.seed+.13,_Reveal*1.55) * (1-smoothstep(.76,.98,_Reveal));
                    color *= .11+1.35*glow;
                }
                else if (_Role == 6 && _Theme == 3)
                    color *= .78+.22*sin(i.seed*51+_Age*.9);
                else if (_Role == 7)
                    color *= .86+.14*sin(i.seed*37+_Age*6.2);
                else if (_Role == 8)
                {
                    // The far sky stays fixed in the chamber while closer stars retain parallax.
                    float3 direction = normalize(TransformWorldToObject(i.world));
                    float latitude = dot(direction, float3(.31,.85,.42));
                    float wisps = sin(dot(direction,float3(8.1,-3.4,5.7))) * .055
                                + sin(dot(direction,float3(16.9,7.0,-12.8))) * .016;
                    float band = saturate(1-abs(latitude+wisps)*3.5);
                    float reveal = smoothstep(.04,.46,_Reveal) * (1-smoothstep(.79,.98,_Reveal));
                    color += half3(.065,.086,.16)*band*band*reveal;
                }
                else if (_Role == 9)
                    color *= .88 + .12 * sin(i.seed*1.9);
                return half4(color, _Opacity);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
