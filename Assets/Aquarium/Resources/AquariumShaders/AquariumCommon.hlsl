#ifndef AQUARIUM_COMMON_INCLUDED
#define AQUARIUM_COMMON_INCLUDED

// Shared helpers for every aquarium shader.
// Globals are pushed by UnderwaterLighting.cs and FluidSimulation.cs.

float4 _AquaCausticParams;   // x: world scale, y: speed, z: intensity, w: water surface height
float4 _AquaWaterParams;     // x: light absorption per metre of depth, y: sun sway time, z: unused, w: unused
float4 _AquaFlowBounds;      // xy: flow grid min (world XZ), zw: 1 / flow grid size (world XZ)
float4 _AquaFlowParams;      // x: 1 when the flow texture is valid
TEXTURE2D(_AquaFlowTex);     SAMPLER(sampler_AquaFlowTex);

// Active touches for plants (set by AquariumInput): origin.xyz + strength, direction.xyz + radius.
#define AQUA_MAX_PUSHES 8
float4 _AquaPushOrigin[AQUA_MAX_PUSHES];
float4 _AquaPushDir[AQUA_MAX_PUSHES];
float _AquaPushCount; // set with Shader.SetGlobalInt, which stores a float

#define AQUA_TAU 6.28318530718

// ---------------------------------------------------------------------------------------------
// Hash / noise
// ---------------------------------------------------------------------------------------------
float AquaHash31(float3 p)
{
    p = frac(p * 0.1031);
    p += dot(p, p.zyx + 31.32);
    return frac((p.x + p.y) * p.z);
}

float AquaHash21(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

float AquaValueNoise3(float3 p)
{
    float3 i = floor(p);
    float3 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float n000 = AquaHash31(i);
    float n100 = AquaHash31(i + float3(1, 0, 0));
    float n010 = AquaHash31(i + float3(0, 1, 0));
    float n110 = AquaHash31(i + float3(1, 1, 0));
    float n001 = AquaHash31(i + float3(0, 0, 1));
    float n101 = AquaHash31(i + float3(1, 0, 1));
    float n011 = AquaHash31(i + float3(0, 1, 1));
    float n111 = AquaHash31(i + float3(1, 1, 1));
    float nx00 = lerp(n000, n100, f.x);
    float nx10 = lerp(n010, n110, f.x);
    float nx01 = lerp(n001, n101, f.x);
    float nx11 = lerp(n011, n111, f.x);
    return lerp(lerp(nx00, nx10, f.y), lerp(nx01, nx11, f.y), f.z);
}

float AquaFbm3(float3 p)
{
    float v = 0.0;
    float a = 0.5;
    [unroll] for (int i = 0; i < 4; i++)
    {
        v += a * AquaValueNoise3(p);
        p = p * 2.03 + 17.1;
        a *= 0.5;
    }
    return v;
}

// ---------------------------------------------------------------------------------------------
// Caustics: iterated domain-warp pattern (tileable, cheap, no textures).
// ---------------------------------------------------------------------------------------------
float AquaCausticLayer(float2 uv, float time)
{
    float2 p = frac(uv) * AQUA_TAU - 250.0;
    float2 i = p;
    float c = 1.0;
    const float inten = 0.005;
    [unroll] for (int n = 0; n < 4; n++)
    {
        float t = time * (1.0 - (3.5 / float(n + 1)));
        i = p + float2(cos(t - i.x) + sin(t + i.y), sin(t - i.y) + cos(t + i.x));
        c += 1.0 / length(float2(p.x / (sin(i.x + t) / inten), p.y / (cos(i.y + t) / inten)));
    }
    c /= 4.0;
    c = 1.17 - pow(abs(c), 1.4);
    return pow(abs(c), 8.0);
}

// Caustic brightness at a world position (0..~1.5). Two layers at different scales for richness.
float AquaCaustics(float3 positionWS)
{
    float scale = _AquaCausticParams.x;
    float t = _Time.y * _AquaCausticParams.y;
    float2 p = positionWS.xz * scale;
    // Slight parallax with depth so caustics shift on vertical surfaces.
    p += positionWS.y * scale * 0.15;
    float a = AquaCausticLayer(p, t);
    float b = AquaCausticLayer(p * 1.73 + 0.37, t * 1.21 + 3.1);
    return (a + b * 0.6) * _AquaCausticParams.z;
}

// Sunlight transmission through the water column above a point.
float AquaDepthTransmission(float positionY)
{
    float depth = max(0.0, _AquaCausticParams.w - positionY);
    return exp(-depth * _AquaWaterParams.x);
}

// Wavelength-dependent absorption tint: red dies first, then green.
float3 AquaAbsorptionTint(float positionY)
{
    float depth = max(0.0, _AquaCausticParams.w - positionY);
    return exp(-depth * _AquaWaterParams.x * float3(3.2, 1.1, 0.55));
}

// ---------------------------------------------------------------------------------------------
// Fluid velocity field (horizontal XZ currents from the stable-fluids solver).
// ---------------------------------------------------------------------------------------------
float2 AquaSampleFlow(float3 positionWS)
{
    float2 uv = (positionWS.xz - _AquaFlowBounds.xy) * _AquaFlowBounds.zw;
    float2 v = SAMPLE_TEXTURE2D_LOD(_AquaFlowTex, sampler_AquaFlowTex, uv, 0).xy;
    float inside = step(0.0, uv.x) * step(uv.x, 1.0) * step(0.0, uv.y) * step(uv.y, 1.0);
    return v * inside * _AquaFlowParams.x;
}

// ---------------------------------------------------------------------------------------------
// Underwater fog with sunlight scattering: the water glows toward the sun and brightens toward the
// surface, instead of a flat blue haze. _AquaScatter: x forward-scatter strength, y surface brightening,
// z seabed height, w scatter sharpness.
// ---------------------------------------------------------------------------------------------
float4 _AquaScatter;

half3 AquaFogColor(float3 viewDirWS, float heightWS)
{
    float3 toSun = normalize(_MainLightPosition.xyz);
    float mu = saturate(dot(viewDirWS, toSun));
    float forward = pow(mu, max(_AquaScatter.w, 1.0)) * 0.85 + pow(mu, 2.0) * 0.15;
    float up = saturate(viewDirWS.y) * saturate(viewDirWS.y);
    float surface = _AquaCausticParams.w;
    float h01 = saturate((heightWS - _AquaScatter.z) / max(surface - _AquaScatter.z, 0.1));
    half3 fog = unity_FogColor.rgb * (1.0 + (h01 * 0.6 + up * 0.8) * _AquaScatter.y);
    fog += _MainLightColor.rgb * unity_FogColor.rgb * 4.0 * forward * _AquaScatter.x * (0.4 + 0.6 * h01);
    return fog;
}

// Exponential-squared underwater fog computed directly from distance, using _AquaFogParams
// (x = density, y = 1 when enabled). It deliberately does NOT use URP's fog keywords: player builds strip
// fog variants for scenes saved with fog off, and URP's ComputeFogIntensity() then returns 0, which would
// paint every object pure fog colour (invisible).
float4 _AquaFogParams;

// 1 = no fog, 0 = fully fogged.
float AquaFogFactor(float3 positionWS)
{
    float d = _AquaFogParams.x * distance(positionWS, GetCameraPositionWS());
    return lerp(1.0, saturate(exp2(-d * d * 1.442695)), saturate(_AquaFogParams.y));
}

half3 AquaApplyFog(half3 color, float3 positionWS, float fogCoord)
{
    float3 cam = GetCameraPositionWS();
    float3 v = positionWS - cam;
    float dist = length(v);
    float3 viewDir = v / max(dist, 1e-4);
    float fogFactor = AquaFogFactor(positionWS);
    half3 fog = AquaFogColor(viewDir, (positionWS.y + cam.y) * 0.5);
    return lerp(fog, color, fogFactor);
}

// Horizontal push away from every active touch ray, with a smooth falloff. Returns world XZ offset (m).
float2 AquaTouchPush(float3 positionWS)
{
    float2 push = 0;
    int pushCount = min((int)(_AquaPushCount + 0.5), AQUA_MAX_PUSHES);
    for (int i = 0; i < pushCount; i++)
    {
        float3 o = _AquaPushOrigin[i].xyz;
        float3 d = _AquaPushDir[i].xyz;
        float radius = _AquaPushDir[i].w;
        float along = max(0.0, dot(positionWS - o, d));
        float3 away = positionWS - (o + d * along);
        float2 awayXZ = away.xz;
        float dist = length(away);
        float w = saturate(1.0 - dist / (radius + along * 0.08));
        w = w * w * (3.0 - 2.0 * w);
        float lenXZ = length(awayXZ);
        float2 dirXZ = lenXZ > 1e-3 ? awayXZ / lenXZ : normalize(float2(0.7, 0.3));
        push += dirXZ * w * _AquaPushOrigin[i].w;
    }
    return push;
}

#endif
