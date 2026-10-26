// Touch-reactive liquid layer drawn exactly on a physical screen. Two simulations drive it:
//  - _RippleTex: wave equation (R = height in metres) -> rings and wakes
//  - _FluidVel / _FluidDye: stable fluids -> smooth swirling liquid distortion that flows with the finger
// The output is the refracted aquarium (corals, rocks and fish stay visible) blended over the untouched image
// with at most _MaxOpacity. Calm water is fully transparent. All sampling is bilinear with a wide smooth
// kernel, so the simulation grid never shows as blocks.
Shader "Aquarium/Glass"
{
    Properties
    {
        _RippleTex ("Wave State (R height m)", 2D) = "black" {}
        _FluidVel ("Fluid Velocity (texels/s)", 2D) = "black" {}
        _FluidDye ("Fluid Dye", 2D) = "black" {}
        _MetresPerTexel ("Metres Per Texel", Vector) = (0.014, 0.014, 0, 0)
        _FluidTexel ("Fluid Texel Size", Vector) = (0.01, 0.01, 0, 0)
        _Refraction ("Refraction", Range(0, 0.1)) = 0.03
        _LiquidRefraction ("Liquid Refraction", Range(0, 0.1)) = 0.025
        _NormalStrength ("Normal Strength", Float) = 2.5
        _MaxOpacity ("Max Opacity", Range(0, 1)) = 0.6
        _WaterTint ("Water Tint", Color) = (0.55, 0.85, 1.0, 1)
        _HighlightColor ("Highlight Colour", Color) = (0.85, 0.97, 1.0, 1)
        _Foam ("Foam", Range(0, 1)) = 0.35
        _Dispersion ("Dispersion", Range(0, 1)) = 0.35
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-100" "IgnoreProjector" = "True" }

        Pass
        {
            Name "WaterLayer"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            TEXTURE2D(_RippleTex); SAMPLER(sampler_RippleTex);
            TEXTURE2D(_FluidVel);  SAMPLER(sampler_FluidVel);
            TEXTURE2D(_FluidDye);  SAMPLER(sampler_FluidDye);

            CBUFFER_START(UnityPerMaterial)
                float4 _RippleTex_TexelSize;
                float4 _MetresPerTexel;
                float4 _FluidTexel;
                float _Refraction;
                float _LiquidRefraction;
                float _NormalStrength;
                half _MaxOpacity;
                half4 _WaterTint;
                half4 _HighlightColor;
                half _Foam;
                half _Dispersion;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv = input.uv;
                return o;
            }

            float H(float2 uv) { return SAMPLE_TEXTURE2D_LOD(_RippleTex, sampler_RippleTex, uv, 0).r; }
            float D(float2 uv) { return SAMPLE_TEXTURE2D_LOD(_FluidDye, sampler_FluidDye, uv, 0).r; }

            // 3x3 Sobel gradient over a 1.5-texel footprint: smooth, no grid artefacts.
            float2 SobelH(float2 uv, float2 t, out float centre, out float lap)
            {
                float2 s = t * 1.5;
                float tl = H(uv + float2(-s.x,  s.y)), tc = H(uv + float2(0,  s.y)), tr = H(uv + float2(s.x,  s.y));
                float ml = H(uv + float2(-s.x, 0)),    mc = H(uv),                  mr = H(uv + float2(s.x, 0));
                float bl = H(uv + float2(-s.x, -s.y)), bc = H(uv + float2(0, -s.y)), br = H(uv + float2(s.x, -s.y));
                centre = mc;
                lap = (ml + mr + tc + bc - 4.0 * mc) / (2.25 * _MetresPerTexel.x * _MetresPerTexel.y);
                float gx = (tr + 2.0 * mr + br) - (tl + 2.0 * ml + bl);
                float gy = (tl + 2.0 * tc + tr) - (bl + 2.0 * bc + br);
                return float2(gx, gy) / (8.0 * 1.5 * _MetresPerTexel.xy); // dimensionless slope
            }

            float2 SobelD(float2 uv, float2 t, out float centre)
            {
                float2 s = t * 1.5;
                float tl = D(uv + float2(-s.x,  s.y)), tc = D(uv + float2(0,  s.y)), tr = D(uv + float2(s.x,  s.y));
                float ml = D(uv + float2(-s.x, 0)),    mc = D(uv),                  mr = D(uv + float2(s.x, 0));
                float bl = D(uv + float2(-s.x, -s.y)), bc = D(uv + float2(0, -s.y)), br = D(uv + float2(s.x, -s.y));
                centre = mc;
                return float2((tr + 2.0 * mr + br) - (tl + 2.0 * ml + bl), (tl + 2.0 * tc + tr) - (bl + 2.0 * bc + br)) / 8.0;
            }

            half3 Scene(float2 uv) { return SampleSceneColor(saturate(uv)); }

            half4 frag(Varyings i) : SV_Target
            {
                float h, lap, dye;
                float2 slope = SobelH(i.uv, _RippleTex_TexelSize.xy, h, lap);
                float2 dyeGrad = SobelD(i.uv, _FluidTexel.xy, dye);
                float2 vel = SAMPLE_TEXTURE2D_LOD(_FluidVel, sampler_FluidVel, i.uv, 0).xy * _FluidTexel.xy; // uv / s
                float slopeLen = length(slope);

                float activity = saturate(slopeLen * 5.0 + abs(h) * 30.0 + dye * 0.3 + length(dyeGrad) * 5.0);
                if (activity < 0.003) discard;

                // Surface normal from both the waves and the liquid layer thickness.
                float3 n = normalize(float3(-slope * _NormalStrength - dyeGrad * 1.2, 1.0));

                float2 screenUV = GetNormalizedScreenSpaceUV(i.positionCS);
                float2 offset = n.xy * _Refraction + dyeGrad * _LiquidRefraction * 4.0;

                // Refraction with slight chromatic dispersion, smeared a little along the flow like moving liquid.
                float spread = 0.25 * _Dispersion;
                float2 flowStep = clamp(vel * 0.02, -0.01, 0.01);
                half3 scene = 0;
                [unroll] for (int k = 0; k < 3; k++)
                {
                    float2 uvk = screenUV + offset - flowStep * (k * 0.5);
                    scene.r += Scene(uvk + offset * spread).r;
                    scene.g += Scene(uvk).g;
                    scene.b += Scene(uvk - offset * spread).b;
                }
                scene /= 3.0;

                // Light focusing (caustics) where waves converge
                float focus = clamp(-lap * 0.05, -0.3, 0.5);
                half3 col = scene * (1.0 + focus);

                // Liquid body tint and a soft inner glow where the liquid is thick
                col = lerp(col, col * _WaterTint.rgb * 1.1, saturate(activity * 0.3 + dye * 0.08));
                col += _WaterTint.rgb * dye * 0.015;

                // Specular glints (viewer looks straight at the screen)
                float3 V = float3(0, 0, 1);
                float3 H1 = normalize(normalize(float3(0.35, 0.6, 0.72)) + V);
                float3 H2 = normalize(normalize(float3(-0.5, 0.25, 0.83)) + V);
                float spec = pow(saturate(dot(n, H1)), 160.0) * 1.4 + pow(saturate(dot(n, H2)), 48.0) * 0.3;
                float fresnel = pow(1.0 - saturate(n.z), 3.0);
                col += _HighlightColor.rgb * (spec + fresnel * 0.2);

                // Soft foam on the steepest crests (smooth, no per-texel noise)
                float foam = smoothstep(0.4, 1.0, slopeLen) * _Foam;
                col = lerp(col, _HighlightColor.rgb, foam * 0.4);

                half alpha = saturate(activity + foam * 0.5) * _MaxOpacity;
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
