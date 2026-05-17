static const int SDF_INDEX = 1;

#include "D3D12SdfCommon.hlsli"
#include "D3D12SdfMath.hlsli"

float zyMoonRelief(float3 dir)
{
    float ridges =
        sin(dir.x * 15.0 + dir.y * 4.0) * 0.018 +
        sin(dir.y * 21.0 - dir.z * 7.0) * 0.012 +
        sin((dir.x + dir.y + dir.z) * 34.0) * 0.006;
    float craterA = 1.0 - smoothstep(0.055, 0.115, abs(length(dir.xy - float2(0.22, -0.18)) - 0.32));
    float craterB = 1.0 - smoothstep(0.040, 0.090, abs(length(dir.yz - float2(0.30, 0.24)) - 0.22));
    return ridges - (craterA * 0.035 + craterB * 0.026);
}

float sdfDistance(float3 p, int sdfIndex)
{
    SdfObject sdfObject = sdfObjects[sdfIndex];
    float radius = max(sdfObject.state.x, 0.001);
    float3 local = p - sdfObject.centerRadius.xyz;
    float3 dir = normalize(local);
    return length(local) - (radius + zyMoonRelief(dir));
}

SdfSurface sdfSurface(float3 p, int sdfIndex)
{
    SdfObject sdfObject = sdfObjects[sdfIndex];
    float3 dir = normalize(p - sdfObject.centerRadius.xyz);
    float relief = zyMoonRelief(dir);
    float dust = sin(dir.x * 31.0 + dir.y * 17.0 - dir.z * 11.0) * 0.5 + 0.5;
    SdfSurface surface;
    surface.baseColor = lerp(float3(0.31, 0.30, 0.28), float3(0.66, 0.62, 0.54), dust);
    surface.baseColor = lerp(surface.baseColor, float3(0.20, 0.20, 0.19), saturate(-relief * 18.0));
    surface.metallic = 0.0;
    surface.roughness = 0.92;
    surface.emission = 0.0;
    return surface;
}

float3 shadeSdf(float2 uv, float travel, float3 p, float3 normal, int sdfIndex, SdfSurface surface)
{
    float3 sunDirection = normalize(float3(-0.46, -0.72, 0.52));
    float daylight = saturate(dot(normal, sunDirection) * 0.82 + 0.24);
    return shadeSdfPbr(p, normal, surface) * daylight;
}

#include "D3D12SdfProxy.hlsli"
