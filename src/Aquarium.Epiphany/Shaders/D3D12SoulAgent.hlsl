static const int SDF_INDEX = 6;

#include "D3D12SdfCommon.hlsli"
#include "D3D12SdfMath.hlsli"
#include "D3D12AgentCharacters.hlsli"

float sdfDistance(float3 p, int sdfIndex)
{
    SdfObject sdfObject = sdfObjects[sdfIndex];
    float radius = max(sdfObject.centerRadius.w, 0.001);
    float3 local = (p - sdfObject.centerRadius.xyz) / radius;
    return sdfObjectFallbackSdf(local, sdfObject) * radius;
}

SdfSurface sdfSurface(float3 p, int sdfIndex)
{
    SdfObject sdfObject = sdfObjects[sdfIndex];

    SdfSurface surface;
    surface.baseColor = lerp(float3(0.20, 0.18, 0.56), float3(0.68, 0.54, 1.0), sdfObject.state.y);
    surface.metallic = 0.0;
    surface.roughness = 0.40;
    surface.emission = primitiveEmissionRadiance(sdfFieldId(sdfIndex)) + surface.baseColor * 0.07;
    return surface;
}

float3 shadeSdf(float2 uv, float travel, float3 p, float3 normal, int sdfIndex, SdfSurface surface)
{
    return shadeSdfPbr(p, normal, surface);
}

#include "D3D12SdfProxy.hlsli"
