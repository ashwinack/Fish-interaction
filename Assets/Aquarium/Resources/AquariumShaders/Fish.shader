// Procedurally coloured, vertex-animated fish (and ray) shader for URP.
// Two materials use it per species: the opaque body (submesh 0) and translucent fins (submesh 1,
// alpha-blended). Mesh convention (see FishMeshBuilder): nose at +Z, tail at -Z,
// uv0.x = 0 tail tip .. 1 nose, uv0.y = 0 belly .. 1 back, uv1 = (fin mask, fin type), uv2 = (fin edge, fin along).
Shader "Aquarium/Fish"
{
    Properties
    {
        [NoScaleOffset] _BaseMap ("Imported Model Texture", 2D) = "white" {}
        _BaseTint ("Imported Model Tint", Color) = (1, 1, 1, 1)
        [Toggle] _UseBaseMap ("Use Imported Texture", Float) = 0
        _ColorTop ("Back Colour", Color) = (0.2, 0.3, 0.5, 1)
        _ColorBelly ("Belly Colour", Color) = (0.9, 0.9, 0.9, 1)
        _StripeColor ("Stripe Colour", Color) = (1, 1, 1, 1)
        _StripeParams ("Stripes (count, width, offset, strength)", Vector) = (0, 0.1, 0, 0)
        _Pattern ("Pattern (spots, spot scale, lateral line, gill z)", Vector) = (0, 40, 0.35, 0.3)
        _FinColor ("Fin Colour", Color) = (0.8, 0.8, 0.8, 1)
        _TailColor ("Tail Colour", Color) = (0.8, 0.8, 0.8, 1)
        _TailStart ("Tail Colour Start (uv.x)", Range(0, 1)) = 0
        _FinOpacity ("Fin Opacity", Range(0, 1)) = 0.8
        _FinRays ("Fin Ray Count", Float) = 14
        _EyePos ("Eye Position (object space; w = ray half span)", Vector) = (0.05, 0.02, 0.4, 0)
        _EyeRadius ("Eye Radius", Float) = 0.02
        _Metallic ("Metallic", Range(0, 1)) = 0
        _Smoothness ("Smoothness", Range(0, 1)) = 0.6
        _Sheen ("Iridescent Sheen", Range(0, 1)) = 0.2

        _BodyLength ("Body Length (object space)", Float) = 1
        _SwimAmp ("Swim Amplitude", Float) = 0.08
        _WaveLength ("Body Waves", Float) = 0.9
        _AnimMode ("Anim Mode (0 fish, 1 ray)", Float) = 0
        _Phase ("Phase (driven by script)", Float) = 0
        _Bend ("Turn Bend (driven by script)", Float) = 0
        _AmpScale ("Tail Beat Scale (driven by script)", Float) = 1

        [HideInInspector] _SrcBlend ("Src Blend", Float) = 1
        [HideInInspector] _DstBlend ("Dst Blend", Float) = 0
        [HideInInspector] _ZWrite ("Z Write", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        #define AQUA_TAU_LOCAL 6.28318530718

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseTint;
            float _UseBaseMap;
            half4 _ColorTop;
            half4 _ColorBelly;
            half4 _StripeColor;
            float4 _StripeParams;
            float4 _Pattern;
            half4 _FinColor;
            half4 _TailColor;
            float _TailStart;
            half _FinOpacity;
            float _FinRays;
            float4 _EyePos;
            float _EyeRadius;
            half _Metallic;
            half _Smoothness;
            half _Sheen;
            float _BodyLength;
            float _SwimAmp;
            float _WaveLength;
            float _AnimMode;
            float _Phase;
            float _Bend;
            float _AmpScale;
            float _SrcBlend;
            float _DstBlend;
            float _ZWrite;
        CBUFFER_END

        // fin: x = mask, y = type (1 caudal, 2 dorsal/anal, 3 pectoral); finUv: x = edge (0 root .. 1), y = along.
        float3 FishDisplace(float3 p, inout float3 n, float2 fin, float2 finUv)
        {
            float halfLen = _BodyLength * 0.5;
            float amp = _SwimAmp * _AmpScale;
            if (_AnimMode < 0.5)
            {
                // s: 0 at nose, 1 at tail tip. Carangiform wave: small head recoil, big tail sweep.
                float s = saturate((halfLen - p.z) / _BodyLength);
                float envelope = 0.05 + 0.95 * s * s;
                float arg = _Phase - s * _WaveLength * AQUA_TAU_LOCAL;
                float lateral = sin(arg) * envelope * amp * _BodyLength;
                lateral += _Bend * s * s * _BodyLength;
                // Head counter-sways a little (recoil), which reads as natural propulsion.
                lateral -= sin(_Phase) * (1.0 - s) * (1.0 - s) * amp * 0.12 * _BodyLength;

                // Fin flutter: tail tips lag the body, median fins ripple, pectorals row.
                float edge = finUv.x * fin.x;
                if (fin.y > 0.5 && fin.y < 1.5) lateral += sin(arg - 0.9) * edge * edge * amp * 0.35 * _BodyLength;
                if (fin.y > 1.5 && fin.y < 2.5) lateral += sin(_Phase * 1.4 + finUv.y * 5.0) * edge * 0.012 * _BodyLength;
                p.x += lateral;
                if (fin.y > 2.5)
                {
                    float flap = sin(_Phase * 0.45 + (p.x > 0 ? 0.0 : 1.6)) * edge;
                    p.y += flap * 0.035 * _BodyLength;
                    p.x += sign(p.x) * flap * 0.02 * _BodyLength;
                }
                float slope = cos(arg) * envelope * amp * _WaveLength * AQUA_TAU_LOCAL;
                n = normalize(n + float3(0, 0, slope * n.x));
            }
            else
            {
                // Ray: wing flap travelling from the body out to the wing tips, plus a trailing tail wave.
                float span = max(_EyePos.w, 0.001);
                float w = saturate(abs(p.x) / span);
                float wave = sin(_Phase - w * 2.2 - (halfLen - p.z) / _BodyLength * 1.5);
                p.y += wave * w * w * amp * _BodyLength;
                float s = saturate((halfLen - p.z) / _BodyLength);
                p.x += sin(_Phase * 1.3 - s * 6.0) * smoothstep(0.7, 1.0, s) * 0.08 * _BodyLength;
                p.x += _Bend * s * s * _BodyLength * 0.5;
            }
            return p;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "AquariumCommon.hlsl"
            #include "AquariumLighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float2 fin        : TEXCOORD1;
                float3 finUv      : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float4 uvFin      : TEXCOORD2; // xy uv, z fin mask, w fin type
                float3 positionOS : TEXCOORD3; // rest pose, for eye / pattern placement
                float  fogCoord   : TEXCOORD4;
                float2 finUv      : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);

                float3 n = input.normalOS;
                float3 p = FishDisplace(input.positionOS.xyz, n, input.fin, input.finUv.xy);
                VertexPositionInputs pos = GetVertexPositionInputs(p);
                o.positionCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                o.normalWS = TransformObjectToWorldNormal(n);
                o.uvFin = float4(input.uv, input.fin.x, input.fin.y);
                o.positionOS = input.positionOS.xyz;
                o.fogCoord = ComputeFogFactor(pos.positionCS.z);
                o.finUv = input.finUv.xy;
                return o;
            }

            half4 frag(Varyings i, bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float2 uv = i.uvFin.xy;
                float fin = i.uvFin.z;
                float finType = i.uvFin.w;
                float3 pOS = i.positionOS;
                half alpha = 1.0;
                half3 albedo;
                float eye = 0;

                if (_UseBaseMap > 0.5)
                {
                    // Imported model: its own texture, aquarium lighting and swim animation.
                    half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv);
                    clip(tex.a - 0.3);
                    albedo = tex.rgb * _BaseTint.rgb;
                }
                else if (fin < 0.5)
                {
                    // --- Body -----------------------------------------------------------------------
                    // Countershading: dark back, light belly, soft transition
                    albedo = lerp(_ColorBelly.rgb, _ColorTop.rgb, smoothstep(0.28, 0.75, uv.y));

                    // Vertical bands
                    if (_StripeParams.x > 0.5)
                    {
                        float sx = frac(uv.x * _StripeParams.x + _StripeParams.z);
                        float d = abs(sx - 0.5);
                        float band = smoothstep(_StripeParams.y + 0.025, _StripeParams.y, d);
                        float edge = smoothstep(_StripeParams.y + 0.05, _StripeParams.y + 0.025, d) - band;
                        albedo = lerp(albedo, _StripeColor.rgb, band * _StripeParams.w);
                        albedo *= 1.0 - edge * 0.55 * _StripeParams.w;
                    }

                    // Spots
                    if (_Pattern.x > 0.001)
                    {
                        float spots = AquaValueNoise3(pOS * _Pattern.y);
                        albedo *= 1.0 - smoothstep(0.68, 0.8, spots) * _Pattern.x * smoothstep(0.35, 0.6, uv.y);
                    }

                    // Lateral line and a faint scale shimmer
                    float lateral = exp(-pow((uv.y - 0.55) / 0.015, 2.0)) * smoothstep(0.1, 0.25, uv.x) * smoothstep(0.9, 0.75, uv.x);
                    albedo = lerp(albedo, albedo * 1.35 + 0.05, lateral * _Pattern.z);
                    float scales = sin(pOS.z * 300.0) * sin(pOS.y * 300.0 + pOS.z * 150.0);
                    albedo *= 1.0 + scales * 0.035;

                    // Gill cover line
                    float gill = exp(-pow((pOS.z - _Pattern.w) / 0.006, 2.0)) * smoothstep(0.15, 0.4, uv.y) * smoothstep(0.9, 0.6, uv.y);
                    albedo *= 1.0 - gill * 0.35;

                    // Eyes (mirrored), glossy with a dark pupil and a catch-light
                    float3 eyeP = float3(abs(pOS.x), pOS.y, pOS.z);
                    float eyeD = distance(eyeP, _EyePos.xyz);
                    eye = smoothstep(_EyeRadius, _EyeRadius * 0.85, eyeD);
                    float pupil = smoothstep(_EyeRadius * 0.62, _EyeRadius * 0.5, eyeD);
                    albedo = lerp(albedo, lerp(half3(0.75, 0.68, 0.4), half3(0.01, 0.01, 0.015), pupil), eye);
                    albedo = lerp(albedo, _TailColor.rgb, step(uv.x, _TailStart) * step(0.001, _TailStart) * (1.0 - eye));
                }
                else
                {
                    // --- Fins: translucent membrane with darker rays and soft, thin edges ----------------
                    albedo = lerp(_FinColor.rgb, _TailColor.rgb, step(uv.x, _TailStart) * step(0.001, _TailStart));
                    float f = abs(frac(i.finUv.y * _FinRays) - 0.5) * 2.0;
                    float ray = smoothstep(0.7, 1.0, f);
                    float edge = i.finUv.x;
                    albedo *= 1.0 - ray * 0.25;
                    albedo = lerp(albedo, albedo * 1.2 + 0.03, smoothstep(0.6, 1.0, edge) * 0.4);
                    alpha = _FinOpacity * lerp(1.0, 0.35, smoothstep(0.35, 1.0, edge)) * (0.8 + 0.2 * ray);
                }

                float3 normalWS = normalize(i.normalWS) * (isFrontFace ? 1.0 : -1.0);
                InputData inputData = AquaBuildInputData(i.positionWS, i.positionCS, normalWS, i.fogCoord);

                SurfaceData surf = (SurfaceData)0;
                surf.albedo = albedo;
                surf.metallic = lerp(_Metallic, 0.0, fin) * (1.0 - eye);
                surf.smoothness = lerp(_Smoothness, 0.3, fin) + eye * 0.35;
                surf.occlusion = 1.0;
                surf.alpha = alpha;
                surf.normalTS = half3(0, 0, 1);

                // Subtle iridescent sheen on the flanks, and sunlight glowing through the fins
                float fres = pow(1.0 - saturate(dot(inputData.normalWS, inputData.viewDirectionWS)), 3.0);
                half3 sheen = lerp(half3(0.25, 0.85, 1.0), half3(0.8, 0.5, 1.0), frac(uv.x * 1.5 + uv.y)) * fres * _Sheen * (1.0 - fin);
                Light sun = GetMainLight();
                float through = saturate(dot(-inputData.viewDirectionWS, sun.direction) * 0.6 + 0.4);
                surf.emission = sheen * 0.3 + albedo * sun.color * through * fin * 0.35
                              + albedo * saturate(1.0 - uv.y * 1.6) * 0.04 * (1.0 - fin); // belly bounce light

                half4 col = AquaShade(inputData, surf, 1.0);
                col.a = alpha;
                return col;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 fin        : TEXCOORD1;
                float3 finUv      : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float4 vert(Attributes input) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 n = input.normalOS;
                float3 p = FishDisplace(input.positionOS.xyz, n, input.fin, input.finUv.xy);
                float3 positionWS = TransformObjectToWorld(p);
                float3 normalWS = TransformObjectToWorldNormal(n);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDir = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDir = _LightDirection;
                #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDir));
                return ApplyShadowClamping(positionCS);
            }

            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 fin        : TEXCOORD1;
                float3 finUv      : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float4 vert(Attributes input) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 n = input.normalOS;
                return TransformObjectToHClip(FishDisplace(input.positionOS.xyz, n, input.fin, input.finUv.xy));
            }

            half frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 fin        : TEXCOORD1;
                float3 finUv      : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings o;
                float3 n = input.normalOS;
                o.positionCS = TransformObjectToHClip(FishDisplace(input.positionOS.xyz, n, input.fin, input.finUv.xy));
                o.normalWS = TransformObjectToWorldNormal(n);
                return o;
            }

            half4 frag(Varyings i) : SV_Target { return half4(NormalizeNormalPerPixel(i.normalWS), 0); }
            ENDHLSL
        }
    }
    FallBack Off
}
