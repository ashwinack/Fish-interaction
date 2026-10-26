// Seabed, rock and coral shader. Procedural world-space detail, vertex colour tint,
// sand ripples, shadowed caustics and optional pulsing bioluminescence.
Shader "Aquarium/Environment"
{
    Properties
    {
        _BaseColor ("Base Colour", Color) = (0.7, 0.65, 0.5, 1)
        _SecondColor ("Variation Colour", Color) = (0.5, 0.45, 0.35, 1)
        _NoiseScale ("Noise Scale", Float) = 0.6
        _Smoothness ("Smoothness", Range(0, 1)) = 0.15
        _SandRipples ("Sand Ripples", Range(0, 1)) = 0
        _CausticStrength ("Caustic Strength", Range(0, 3)) = 1
        _EmissionColor ("Emission Colour", Color) = (0, 0, 0, 1)
        _EmissionPulse ("Emission Pulse (speed, depth)", Vector) = (0.6, 0.5, 0, 0)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _SecondColor;
            float _NoiseScale;
            half _Smoothness;
            half _SandRipples;
            half _CausticStrength;
            half4 _EmissionColor;
            float4 _EmissionPulse;
        CBUFFER_END
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
            #include "AquariumCommon.hlsl"
            #include "AquariumLighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                half4  color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                half4  color      : TEXCOORD2;
                float  fogCoord   : TEXCOORD3;
                float3 objectWS   : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.color = input.color;
                o.fogCoord = ComputeFogFactor(pos.positionCS.z);
                o.objectWS = TransformObjectToWorld(float3(0, 0, 0));
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 p = i.positionWS * _NoiseScale;
                float n = AquaFbm3(p);
                float fine = AquaValueNoise3(p * 7.0);
                half3 albedo = lerp(_SecondColor.rgb, _BaseColor.rgb, saturate(n * 1.6 - 0.3));
                albedo *= (0.85 + fine * 0.3);
                albedo *= i.color.rgb;

                float3 normalWS = normalize(i.normalWS);

                // Wind-blown style sand ripples, bent by low-frequency noise
                if (_SandRipples > 0.001)
                {
                    float warp = AquaFbm3(float3(i.positionWS.xz * 0.15, 0)) * 6.0;
                    float phase = i.positionWS.x * 2.2 + i.positionWS.z * 0.9 + warp;
                    float ripple = cos(phase);
                    float3 rippleN = normalize(float3(-sin(phase) * 2.2, 6.0, -sin(phase) * 0.9));
                    normalWS = normalize(lerp(normalWS, rippleN, _SandRipples * saturate(normalWS.y)));
                    albedo *= 1.0 + ripple * 0.06 * _SandRipples;
                }

                // Mild normal perturbation from noise for rocky detail
                float3 grad = float3(
                    AquaValueNoise3(p * 3.0 + float3(0.1, 0, 0)) - AquaValueNoise3(p * 3.0 - float3(0.1, 0, 0)),
                    AquaValueNoise3(p * 3.0 + float3(0, 0.1, 0)) - AquaValueNoise3(p * 3.0 - float3(0, 0.1, 0)),
                    AquaValueNoise3(p * 3.0 + float3(0, 0, 0.1)) - AquaValueNoise3(p * 3.0 - float3(0, 0, 0.1)));
                normalWS = normalize(normalWS - grad * 1.5 * (1.0 - _SandRipples));

                InputData inputData = AquaBuildInputData(i.positionWS, i.positionCS, normalWS, i.fogCoord);

                SurfaceData surf = (SurfaceData)0;
                surf.albedo = albedo;
                surf.metallic = 0;
                surf.smoothness = _Smoothness;
                surf.occlusion = lerp(0.6, 1.0, n);
                surf.alpha = 1;
                surf.normalTS = half3(0, 0, 1);

                float pulse = 1.0 - _EmissionPulse.y * (0.5 + 0.5 * sin(_Time.y * _EmissionPulse.x + dot(i.objectWS, float3(1.3, 0.7, 2.1))));
                surf.emission = _EmissionColor.rgb * pulse * i.color.a;

                return AquaShade(inputData, surf, _CausticStrength);
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
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float4 vert(Attributes input) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
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
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float4 vert(Attributes input) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return TransformObjectToHClip(input.positionOS.xyz);
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
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target { return half4(NormalizeNormalPerPixel(i.normalWS), 0); }
            ENDHLSL
        }
    }
    FallBack Off
}
