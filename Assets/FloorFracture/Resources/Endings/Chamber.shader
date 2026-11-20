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
        [Enum(UnityEngine.Rendering.CompareFunction)] _PortalComparison("Floor aperture test", Float) = 8
        _WideningPerMetre("Shaft widening per metre", Float) = .72
        [Normal] _WaterNormal("Meta Floor Zone water ripples", 2D) = "bump" {}
        _Mouth("World opening center and half width", Vector) = (0,0,0,1)
        _MouthRight("Opening right and clipping flag", Vector) = (1,0,0,0)
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
            Stencil { Ref 4 ReadMask 4 WriteMask 0 Comp [_PortalComparison] Pass Keep }
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/FloorFracture/Scripts/Room/ShaftBrickwork.hlsl"
            TEXTURE2D(_WaterNormal);
            SAMPLER(sampler_WaterNormal);
            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                float _Role, _Theme, _Age, _Reveal, _Level, _Motion, _Opacity, _WideningPerMetre;
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
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 p = v.positionOS.xyz;
                float seed = v.seed.x;
                if (_Role == 1 || _Role == 9)
                {
                    float topRadius = length(TransformObjectToWorldDir(float3(1,0,0), false));
                    p.xz *= 1 + max(0,-p.y) * _WideningPerMetre / max(.01,topRadius);
                }
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
                if (_Role == 1)
                {
                    // The eight sides alternate flat faces and bevels. Project on each
                    // face's tangent so a physical metre has the same bond as the floor.
                    float angle = seed * (PI * .25);
                    float2 along = float2(cos(angle), sin(angle));
                    float radius = length(TransformObjectToWorldDir(float3(1,0,0), false));
                    o.uv = float2(dot(p.xz, along) * radius, -p.y);
                }
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
                    half3 masonry = ShaftBrickwork(i.uv);
                    half3 themeLight = color;
                    color = masonry * i.color.rgb;
                    float landing = 1 - smoothstep(.5, 4, i.world.y - _Level);
                    color = lerp(color, masonry * .65 + themeLight * .35, landing * _Reveal);
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
                        // Reflected light on the low masonry keeps the pool readable at a shallow angle.
                        float above = max(0,i.world.y-_Level);
                        float poolLight = 1-smoothstep(.5,14,above);
                        float reflectedWave = sin(dot(xz,float2(.39,.24))+above*.86-_Time.y*1.65
                                                  +.62*sin(xz.y*.53+_Time.y*.8));
                        color += half3(.018,.13,.16)*poolLight*smoothstep(.28,.94,reflectedWave);
                    }
                }
                else if (_Role == 2)
                {
                    // Map scale is in metres. Long swells read from the rim; the approved
                    // Floor Zone ripple normals resolve gradually as the same water approaches.
                    float2 uv = xz * .085;
                    half2 a = UnpackNormal(SAMPLE_TEXTURE2D(_WaterNormal,sampler_WaterNormal,uv+_Time.y*float2(.014,.009))).xy;
                    half2 b = UnpackNormal(SAMPLE_TEXTURE2D(_WaterNormal,sampler_WaterNormal,uv.yx*1.67+_Time.y*float2(-.009,.012))).xy;
                    half2 swell = UnpackNormal(SAMPLE_TEXTURE2D(_WaterNormal,sampler_WaterNormal,
                        uv*.29+_Time.y*float2(.003,-.004))).xy;
                    half3 normal = normalize(half3(a.x+.46*b.y+.38*swell.x,.95,a.y+.46*b.x+.38*swell.y));
                    half3 view = GetWorldSpaceNormalizeViewDir(i.world);
                    half glint = smoothstep(.62,.94,dot(reflect(-view,normal),normalize(half3(.27,.81,.51))));
                    half reflection = saturate(.2+.42*(1-dot(view,normal))+.32*normal.z);
                    color = lerp(half3(.014,.08,.13),half3(.065,.38,.46),reflection)
                          + half3(.035,.095,.11)*saturate(.50+.95*swell.y) + half3(.22,.34,.36)*glint*glint;
                }
                else if (_Role == 3)
                {
                    float2 drift = xz*.40 + .64*sin(xz.yx*.31 + _Time.y*float2(.08,-.06));
                    float channels = abs(sin(drift.x*2.7+sin(drift.y*1.8))+sin(drift.y*2.4-cos(drift.x*2.1)));
                    half fissure = 1-smoothstep(.08,.88,channels);
                    half core = 1-smoothstep(.025,.24,channels);
                    color = lerp(half3(.18,.025,.009),half3(.93,.22,.01),fissure) + half3(.14,.39,.06)*core;
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
                else if (_Role == 10)
                {
                    float2 facet = abs(frac(float2(xz.x*.32+xz.y*.17,xz.y*.24-xz.x*.12))-.5);
                    float edge = abs(facet.x-facet.y);
                    color = lerp(half3(.19,.055,.34), half3(.53,.25,.72), facet.x+facet.y);
                    color += half3(.25,.58,.7)*(1-smoothstep(.012,.06,edge));
                }
                else if (_Role == 11)
                {
                    float2 cell = xz*.45;
                    float2 id = floor(cell);
                    float2 offset = frac(sin(float2(dot(id,float2(31.1,17.7)),dot(id,float2(12.7,38.1))))*4375.3);
                    float star = 1-smoothstep(.025,.10,length(frac(cell)-lerp(.15,.85,offset)));
                    float nebula = .5+.5*sin(xz.x*.19+sin(xz.y*.14));
                    color = half3(.018,.022,.075)+half3(.045,.055,.14)*nebula+half3(.7,.84,1)*star;
                }
                else if (_Role == 12)
                {
                    float drift = .5+.5*sin(xz.x*1.2+sin(xz.y*.41+_Time.y*.12));
                    color = lerp(half3(.36,.59,.78),half3(.84,.93,.99),smoothstep(.1,.93,drift));
                }
                return half4(color, _Opacity);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
