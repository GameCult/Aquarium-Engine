static const int SDF_INDEX = 1;

#include "D3D12SdfCommon.hlsli"
#include "D3D12SdfMath.hlsli"

float zyUmbrosRelief(float3 dir)
{
    float ridges =
        sin(dir.x * 15.0 + dir.y * 4.0) * 0.018 +
        sin(dir.y * 21.0 - dir.z * 7.0) * 0.012 +
        sin((dir.x + dir.y + dir.z) * 34.0) * 0.006;
    float basinA = 1.0 - smoothstep(0.055, 0.115, abs(length(dir.xy - float2(0.22, -0.18)) - 0.32));
    float basinB = 1.0 - smoothstep(0.040, 0.090, abs(length(dir.yz - float2(0.30, 0.24)) - 0.22));
    return ridges - (basinA * 0.035 + basinB * 0.026);
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
    SdfSurface surface;
    surface.baseColor = lerp(float3(0.18, 0.19, 0.20), float3(0.44, 0.42, 0.38), dust);
    surface.baseColor = lerp(surface.baseColor, float3(0.10, 0.11, 0.12), saturate(-relief * 18.0));
    surface.metallic = 0.0;
    surface.roughness = 0.92;
    surface.emission = 0.0;
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
