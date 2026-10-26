// Kelp / seagrass / anemone shader. Every blade (not just every clump) sways on its own with a travelling
// wave, bends with the tank current sampled at its own root, and curls away from nearby touches with a
// smooth, springy response (eased on the CPU). Bending preserves blade length, so tips arc down instead
// of stretching.
// Mesh convention: uv.x across the blade, uv.y = 0 root .. 1 tip, uv2 = (seed, blade length), uv3 = blade root (object space).
Shader "Aquarium/Kelp"
{
    Properties
    {
        _BaseColor ("Root Colour", Color) = (0.15, 0.3, 0.08, 1)
        _TipColor ("Tip Colour", Color) = (0.55, 0.6, 0.15, 1)
        _Sway ("Sway (amplitude m, frequency, current response, curve)", Vector) = (0.6, 0.6, 1.2, 1.6)
        _PushResponse ("Touch Push (m)", Float) = 1
        _Translucency ("Translucency", Range(0, 2)) = 0.8
        _EmissionColor ("Tip Glow", Color) = (0, 0, 0, 1)
        _Smoothness ("Smoothness", Range(0, 1)) = 0.35
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "AquariumCommon.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _TipColor;
            float4 _Sway;
            float _PushResponse;
            half _Translucency;
            half4 _EmissionColor;
            half _Smoothness;
        CBUFFER_END

        float3 KelpDisplaceWS(float3 positionWS, float2 uv, float2 blade, float3 rootOS)
        {
            float3 rootWS = TransformObjectToWorld(rootOS);
            float t = saturate(uv.y);
            float bend = pow(t, _Sway.w);
            float seed = blade.x;
            float phase = seed * 6.2831 + dot(rootWS.xz, float2(0.37, 0.23));
            float time = _Time.y * _Sway.y * (0.85 + seed * 0.3);

            // Slow sway of the whole blade plus a wave travelling up it (the tip lags behind the root).
            float2 sway = float2(sin(time + phase), sin(time * 0.77 + phase * 1.7 + 1.3)) * 0.6
                        + float2(sin(time * 1.9 - t * 3.0 + phase), cos(time * 1.6 - t * 2.5 + phase)) * 0.4 * t;
            sway *= _Sway.x;

            // Tank current at this blade's root (clamped so strong pushes bend but never fold the plant)
            float2 flow = AquaSampleFlow(rootWS);
            float flowLen = length(flow);
            flow *= min(1.0, 1.6 / max(flowLen, 1e-4)) * _Sway.z;

            // Touch push evaluated at this vertex, so blades curl around the finger
            float2 push = AquaTouchPush(positionWS) * _PushResponse;

            float2 offset = (sway + flow + push) * bend;

            // Length-preserving bend: the horizontal offset can never exceed the height above the root,
            // and the vertex drops as it swings out (arc).
            float h = max(positionWS.y - rootWS.y, 0.001);
            float len = length(offset);
            float maxLen = h * 0.92;
            if (len > maxLen) offset *= maxLen / len;
            float drop = h - sqrt(max(h * h - dot(offset, offset), 0.0));
            return positionWS + float3(offset.x, -drop, offset.y);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

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
            #include "AquariumLighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float2 uv2        : TEXCOORD1;
                float3 root       : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
                float  fogCoord   : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                float3 positionWS = KelpDisplaceWS(TransformObjectToWorld(input.positionOS.xyz), input.uv, input.uv2, input.root);
                o.positionWS = positionWS;
                o.positionCS = TransformWorldToHClip(positionWS);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.uv = input.uv;
                o.fogCoord = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i, bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half3 albedo = lerp(_BaseColor.rgb, _TipColor.rgb, smoothstep(0.0, 1.0, i.uv.y));
                // Central vein and slightly darker edges
                float across = abs(i.uv.x - 0.5) * 2.0;
                albedo *= 0.8 + 0.2 * smoothstep(0.0, 0.15, across);
                albedo *= 1.0 - 0.25 * smoothstep(0.7, 1.0, across);

                float3 normalWS = normalize(i.normalWS) * (isFrontFace ? 1.0 : -1.0);
                InputData inputData = AquaBuildInputData(i.positionWS, i.positionCS, normalWS, i.fogCoord);

                SurfaceData surf = (SurfaceData)0;
                surf.albedo = albedo;
                surf.smoothness = _Smoothness;
                surf.occlusion = lerp(0.5, 1.0, i.uv.y);
                surf.alpha = 1;
                surf.normalTS = half3(0, 0, 1);

                // Sunlight glowing through the blades
                Light sun = GetMainLight(inputData.shadowCoord, inputData.positionWS, inputData.shadowMask);
                float back = saturate(dot(-inputData.viewDirectionWS, sun.direction) * 0.5 + 0.5);
                surf.emission = albedo * sun.color * back * back * _Translucency * 0.45 * sun.shadowAttenuation
                              + _EmissionColor.rgb * i.uv.y * i.uv.y;

                return AquaShade(inputData, surf, 0.9);
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
                float2 uv         : TEXCOORD0;
                float2 uv2        : TEXCOORD1;
                float3 root       : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float4 vert(Attributes input) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 positionWS = KelpDisplaceWS(TransformObjectToWorld(input.positionOS.xyz), input.uv, input.uv2, input.root);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDir = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDir = _LightDirection;
                #endif
                return ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDir)));
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
                float2 uv         : TEXCOORD0;
                float2 uv2        : TEXCOORD1;
                float3 root       : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float4 vert(Attributes input) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return TransformWorldToHClip(KelpDisplaceWS(TransformObjectToWorld(input.positionOS.xyz), input.uv, input.uv2, input.root));
            }

            half frag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
    FallBack Off
}
