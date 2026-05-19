static const int SDF_INDEX = 1;

#include "D3D12SdfCommon.hlsli"
#include "D3D12SdfMath.hlsli"

float umHash21(float2 p)
{
    p = frac(p * float2(97.17, 271.43));
    p += dot(p, p + 31.91);
    return frac(p.x * p.y);
}

float umTileRelief(float2 uv, float level, float amplitude, float crackBias)
{
    float scale = exp2(level);
    float2 cell = floor(uv * scale);
    float2 local = frac(uv * scale) - 0.5;
    float seed = umHash21(cell - level * 11.0);
    float angle = seed * 6.2831853;
    float2 axis = float2(cos(angle), sin(angle));
    float crack = 1.0 - smoothstep(0.018, 0.092, abs(dot(local, axis) + (seed - 0.5) * 0.22));
    float shelf = 1.0 - smoothstep(0.12, 0.45, max(abs(local.x * axis.x), abs(local.y * axis.y)));
    return shelf * amplitude * (1.0 - crackBias) - crack * amplitude * crackBias;
}

float umQuadtreeSdfRelief(float3 dir)
{
    float2 front = dir.yz * 0.5 + 0.5;
    float2 wrap = float2(atan2(dir.z, dir.y) * 0.15915494 + 0.5, dir.x * 0.5 + 0.5);
    float blend = smoothstep(0.52, 0.86, abs(dir.x));
    float2 uv = lerp(front, wrap, blend);
    float relief = 0.0;
    relief += umTileRelief(uv + 0.23, 2.0, 0.022, 0.72);
    relief += umTileRelief(uv * 1.71 - 0.11, 3.0, 0.014, 0.62);
    relief += umTileRelief(uv * 2.60 + 0.41, 4.0, 0.008, 0.54);
    return relief;
}

float umPebbleCluster(float3 dir)
{
    float province = smoothstep(cos(0.34), cos(0.12), dot(dir, normalize(float3(0.88, -0.24, 0.20))));
    float grain = sin(dir.x * 149.0 + dir.z * 47.0) * sin(dir.y * 181.0 - dir.z * 63.0);
    return province * smoothstep(0.50, 0.96, grain * 0.5 + 0.5);
}

float zyUmbrosRelief(float3 dir)
{
    float ridges =
        sin(dir.x * 15.0 + dir.y * 4.0) * 0.018 +
        sin(dir.y * 21.0 - dir.z * 7.0) * 0.012 +
        sin((dir.x + dir.y + dir.z) * 34.0) * 0.006;
    float basinA = 1.0 - smoothstep(0.055, 0.115, abs(length(dir.xy - float2(0.22, -0.18)) - 0.32));
    float basinB = 1.0 - smoothstep(0.040, 0.090, abs(length(dir.yz - float2(0.30, 0.24)) - 0.22));
    return ridges - (basinA * 0.035 + basinB * 0.026) + umQuadtreeSdfRelief(dir) - umPebbleCluster(dir) * 0.010;
}

float3 zyPrimaryStarDirection(float phase)
{
    return normalize(float3(cos(phase), sin(phase), 0.18));
}

float sdfDistance(float3 p, int sdfIndex)
{
    SdfObject sdfObject = sdfObjects[sdfIndex];
    float radius = max(sdfObject.state.x, 0.001);
    float3 local = p - sdfObject.centerRadius.xyz;
    float3 dir = normalize(local);
    return length(local) - (radius + zyUmbrosRelief(dir));
}

SdfSurface sdfSurface(float3 p, int sdfIndex)
{
    SdfObject sdfObject = sdfObjects[sdfIndex];
    float3 dir = normalize(p - sdfObject.centerRadius.xyz);
    float relief = zyUmbrosRelief(dir);
    float dust = sin(dir.x * 31.0 + dir.y * 17.0 - dir.z * 11.0) * 0.5 + 0.5;
    float cracks = saturate(-umQuadtreeSdfRelief(dir) * 20.0);
    float pebbles = umPebbleCluster(dir);
    SdfSurface surface;
    surface.baseColor = lerp(float3(0.18, 0.19, 0.20), float3(0.44, 0.42, 0.38), dust);
    surface.baseColor = lerp(surface.baseColor, float3(0.10, 0.11, 0.12), saturate(-relief * 18.0));
    surface.baseColor = lerp(surface.baseColor, float3(0.07, 0.075, 0.085), cracks);
    surface.baseColor = lerp(surface.baseColor, float3(0.50, 0.49, 0.45), pebbles * 0.45);
    surface.metallic = 0.0;
    surface.roughness = 0.92;
    surface.emission = 0.0;
    surface.temporalDetail = 0.0;
    surface.reservoirConfidence = 1.0;
    return surface;
}

float3 shadeSdf(float2 uv, float travel, float3 p, float3 normal, int sdfIndex, SdfSurface surface)
{
    SdfObject sdfObject = sdfObjects[sdfIndex];
    float3 starDirection = zyPrimaryStarDirection(sdfObject.state.y);
    float daylight = saturate(dot(normal, starDirection) * 0.74 + 0.16);
    return shadeSdfPbr(p, normal, surface) * daylight;
}

#include "D3D12SdfProxy.hlsli"
