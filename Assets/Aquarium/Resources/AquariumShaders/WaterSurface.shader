// Underside of the water surface: animated wave normals, Snell's window (bright sky seen
// through the surface above) and total internal reflection at grazing angles.
Shader "Aquarium/WaterSurface"
{
    Properties
    {
        _SkyColor ("Sky Colour (through Snell's window)", Color) = (0.75, 0.95, 1.0, 1)
        _ReflectColor ("Internal Reflection Colour", Color) = (0.02, 0.2, 0.28, 1)
        _WaveScale ("Wave Scale", Float) = 0.35
        _WaveSpeed ("Wave Speed", Float) = 0.5
        _Brightness ("Brightness", Float) = 2.4
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+10" }
        Cull Off

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "AquariumCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _SkyColor;
                half4 _ReflectColor;
                float _WaveScale;
                float _WaveSpeed;
                half _Brightness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float  fogCoord   : TEXCOORD1;
            };

            float WaveHeight(float2 p, float t)
            {
                float h = 0;
                h += sin(dot(p, float2(0.8, 0.6)) * 1.0 + t * 1.1) * 0.5;
                h += sin(dot(p, float2(-0.5, 0.9)) * 1.7 + t * 1.5) * 0.3;
                h += sin(dot(p, float2(0.3, -1.0)) * 2.9 + t * 2.3) * 0.15;
                h += (AquaValueNoise3(float3(p * 1.5, t * 0.4)) - 0.5) * 0.6;
                return h;
            }

            Varyings vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                o.fogCoord = ComputeFogFactor(pos.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float t = _Time.y * _WaveSpeed;
                float2 p = i.positionWS.xz * _WaveScale;
                const float e = 0.05;
                float h = WaveHeight(p, t);
                float hx = WaveHeight(p + float2(e, 0), t);
                float hz = WaveHeight(p + float2(0, e), t);
                // Normal pointing down into the water
                float3 n = normalize(float3((hx - h) / e * 0.35, -1.0, (hz - h) / e * 0.35));

                float3 viewDir = normalize(i.positionWS - GetCameraPositionWS()); // camera -> surface
                // Refraction out of water (n=1.33). Inside Snell's window we see the bright sky.
                float3 refr = refract(viewDir, n, 1.33);
                float window = (dot(refr, refr) > 0.0001) ? smoothstep(0.0, 0.25, refr.y) : 0.0;

                float caustic = AquaCaustics(float3(i.positionWS.x, 0, i.positionWS.z)) * 0.35;
                half3 col = lerp(_ReflectColor.rgb * (1.0 + h * 0.15), _SkyColor.rgb * _Brightness * (0.85 + caustic), window);
                col += pow(saturate(1.0 - abs(window - 0.5) * 2.0), 4.0) * _SkyColor.rgb * 0.4; // bright rim of the window

                col = AquaApplyFog(col, i.positionWS, i.fogCoord);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
