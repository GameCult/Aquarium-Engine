static const int SDF_INDEX = 0;

#include "D3D12SdfCommon.hlsli"
#include "D3D12SdfMath.hlsli"

float sdfDistance(float3 p, int sdfIndex)
{
    SdfObject sdfObject = sdfObjects[sdfIndex];
    float radius = max(sdfObject.centerRadius.w, 0.001);
    float3 local = (p - sdfObject.centerRadius.xyz) / radius;
    return sdSphere(local, 1.0) * radius;
}

SdfSurface sdfSurface(float3 p, int sdfIndex)
{
    SdfSurface surface;
    surface.albedo = 0.0;
    surface.roughness = 0.0;
    surface.f0 = 0.0;
    surface.emission = primitiveEmissionRadiance(sdfFieldId(sdfIndex));
    return surface;
}

float3 shadeSdf(float2 uv, float travel, float3 p, float3 normal, int sdfIndex, SdfSurface surface)
{
    return primitiveEmissionRadiance(sdfFieldId(sdfIndex));
}

#include "D3D12SdfProxy.hlsli"
