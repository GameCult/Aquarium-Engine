static const int SDF_INDEX = 2;

#include "D3D12SdfCommon.hlsli"
#include "D3D12SdfMath.hlsli"
#include "D3D12AgentCharacters.hlsli"

float sdfDistance(float3 p, int sdfIndex)
{
    SdfObject sdfObject = sdfObjects[sdfIndex];
    float radius = max(sdfObject.centerRadius.w, 0.001);
    float3 local = (p - sdfObject.centerRadius.xyz) / radius;
    return sdfObjectImaginationSdf(local, sdfObject, timeSeconds) * radius;
}

SdfSurface sdfSurface(float3 p, int sdfIndex)
{
    SdfObject sdfObject = sdfObjects[sdfIndex];

    SdfSurface surface;
    surface.albedo = lerp(float3(0.36, 0.16, 0.70), float3(0.96, 0.48, 0.92), sdfObject.state.x);
    surface.roughness = 0.34;
    surface.f0 = float3(0.06, 0.035, 0.12);
    surface.emission = primitiveEmissionRadiance(sdfFieldId(sdfIndex)) + surface.albedo * (0.08 + sdfObject.state.y * 0.08);
    return surface;
}

float3 shadeSdf(float2 uv, float travel, float3 p, float3 normal, int sdfIndex, SdfSurface surface)
{
    return shadeSdfPbr(p, normal, surface);
}

#include "D3D12SdfProxy.hlsli"
