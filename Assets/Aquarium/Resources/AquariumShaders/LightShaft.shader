// Additive volumetric-looking god ray. The quad is billboarded around its own axis towards the
// rendering camera. All three screen cameras share one eye point, so the shafts line up across seams.
// Mesh convention: x in [-0.5, 0.5] across, y in [-1, 0] from the surface downwards, uv = (across, 1 at top).
Shader "Aquarium/LightShaft"
{
    Properties
    {
        _Color ("Colour", Color) = (0.6, 0.9, 1.0, 1)
        _Intensity ("Intensity", Float) = 0.25
        _NoiseSpeed ("Noise Speed", Float) = 0.15
        _NearFade ("Near Fade Distance", Float) = 3
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "AquariumCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Intensity;
                float _NoiseSpeed;
                float _NearFade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float  fogCoord   : TEXCOORD2;
                float  seed       : TEXCOORD3;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                float3 origin0 = TransformObjectToWorld(float3(0, 0, 0));
                float seed0 = AquaHash21(origin0.xz);
                float3 axis = normalize(TransformObjectToWorldDir(float3(0, 1, 0)));
                // Surface waves make the beams sway slowly
                axis = normalize(axis + float3(sin(_Time.y * 0.23 + seed0 * 6.0), 0, cos(_Time.y * 0.19 + seed0 * 4.0)) * 0.05);
                float3 center = origin0 + axis * input.positionOS.y * length(float3(UNITY_MATRIX_M[0].y, UNITY_MATRIX_M[1].y, UNITY_MATRIX_M[2].y));
                float width = length(float3(UNITY_MATRIX_M[0].x, UNITY_MATRIX_M[1].x, UNITY_MATRIX_M[2].x));
                float3 toCam = normalize(GetCameraPositionWS() - center);
                float3 right = normalize(cross(axis, toCam));
                float3 positionWS = center + right * input.positionOS.x * width;

                o.positionWS = positionWS;
                o.positionCS = TransformWorldToHClip(positionWS);
                o.uv = input.uv;
                o.fogCoord = ComputeFogFactor(o.positionCS.z);
                float3 origin = TransformObjectToWorld(float3(0, 0, 0));
                o.seed = AquaHash21(origin.xz);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float t = _Time.y * _NoiseSpeed;
                float across = 1.0 - abs(i.uv.x - 0.5) * 2.0;
                float edge = pow(smoothstep(0.0, 1.0, across), 2.2); // soft, feathered sides
                // Brightest just under the surface, fading with depth
                float along = pow(saturate(i.uv.y), 1.6) * smoothstep(1.0, 0.96, i.uv.y) * (1.0 + smoothstep(0.75, 0.97, i.uv.y) * 0.8);
                float streaks = AquaValueNoise3(float3(i.uv.x * 6.0 + i.seed * 10.0, i.uv.y * 0.8 - t, t * 0.5));
                float flicker = 0.6 + 0.4 * sin(_Time.y * 0.7 + i.seed * 40.0);
                float nearFade = saturate(distance(i.positionWS, GetCameraPositionWS()) / _NearFade);
                float a = edge * along * (0.4 + streaks * 0.9) * flicker * nearFade * _Intensity;
                half3 col = _Color.rgb * a;
                // Additive: fog fades the contribution to black
                col *= AquaFogFactor(i.positionWS);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
