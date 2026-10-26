// Far backdrop sphere: the same sun-scattered water colour the fog fades into, so distant water glows
// toward the sun and brightens toward the surface instead of ending in a flat colour.
Shader "Aquarium/Backdrop"
{
    SubShader
    {
        Tags { "RenderType" = "Background" "RenderPipeline" = "UniversalPipeline" "Queue" = "Background" "PreviewType" = "Skybox" }

        Pass
        {
            Name "Backdrop"
            Tags { "LightMode" = "UniversalForward" }
            Cull Front
            ZWrite Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "AquariumCommon.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 cam = GetCameraPositionWS();
                float3 viewDir = normalize(i.positionWS - cam);
                return half4(AquaFogColor(viewDir, cam.y), 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
