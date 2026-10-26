#ifndef AQUARIUM_LIGHTING_INCLUDED
#define AQUARIUM_LIGHTING_INCLUDED

// Requires Core.hlsl + Lighting.hlsl + AquariumCommon.hlsl to be included first.

InputData AquaBuildInputData(float3 positionWS, float4 positionCS, float3 normalWS, float fogCoord)
{
    InputData inputData = (InputData)0;
    inputData.positionWS = positionWS;
    inputData.positionCS = positionCS;
    inputData.normalWS = NormalizeNormalPerPixel(normalWS);
    inputData.viewDirectionWS = SafeNormalize(GetWorldSpaceViewDir(positionWS));
    inputData.shadowCoord = TransformWorldToShadowCoord(positionWS);
    inputData.fogCoord = fogCoord;
    inputData.vertexLighting = half3(0, 0, 0);
    inputData.bakedGI = SampleSH(inputData.normalWS);
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(positionCS);
    inputData.shadowMask = half4(1, 1, 1, 1);
    return inputData;
}

// Full underwater shading: URP PBR (all lights, Forward+ clusters, shadows) plus
// shadowed caustics from the sun and depth-dependent light absorption.
half4 AquaShade(InputData inputData, SurfaceData surfaceData, float causticAmount)
{
    half4 color = UniversalFragmentPBR(inputData, surfaceData);

    Light mainLight = GetMainLight(inputData.shadowCoord, inputData.positionWS, inputData.shadowMask);
    float facing = saturate(dot(inputData.normalWS, mainLight.direction) * 0.75 + 0.25);
    float caustic = AquaCaustics(inputData.positionWS) * causticAmount;
    float transmission = AquaDepthTransmission(inputData.positionWS.y);
    color.rgb += surfaceData.albedo * mainLight.color * (mainLight.shadowAttenuation * caustic * facing * transmission);

    color.rgb *= lerp(float3(1, 1, 1), AquaAbsorptionTint(inputData.positionWS.y), 0.75);
    color.rgb = AquaApplyFog(color.rgb, inputData.positionWS, inputData.fogCoord);
    return color;
}

#endif
