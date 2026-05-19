static const int SDF_INDEX = 5;

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
    surface.baseColor = lerp(float3(0.72, 0.34, 0.18), float3(1.0, 0.76, 0.38), sdfObject.state.x);
    surface.metallic = 0.0;
    surface.roughness = 0.50;
    surface.emission = primitiveEmissionRadiance(sdfFieldId(sdfIndex)) + surface.baseColor * 0.035;
    surface.temporalDetail = 0.0;
    surface.reservoirConfidence = 1.0;
    return surface;
}

float3 shadeSdf(float2 uv, float travel, float3 p, float3 normal, int sdfIndex, SdfSurface surface)
{
    return shadeSdfPbr(p, normal, surface);
}

#include "D3D12SdfProxy.hlsli"
