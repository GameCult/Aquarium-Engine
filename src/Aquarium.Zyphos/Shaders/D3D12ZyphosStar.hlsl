static const int SDF_INDEX = 2;

#define SDF_TRACE_STEPS 160
#define SDF_TRACE_STEP_SCALE 0.55
#define SDF_TRACE_MAX_STEP_RADIUS_SCALE 0.08

#include "D3D12SdfCommon.hlsli"
#include "D3D12SdfMath.hlsli"

float starGranulation(float3 dir, float phase)
{
    float bands =
        sin(dir.x * 17.0 + phase * 1.7) * 0.18 +
        sin(dir.y * 23.0 - phase * 1.1) * 0.14 +
        sin((dir.x + dir.y + dir.z) * 41.0 + phase * 0.6) * 0.08;
    return bands;
}

float sdfDistance(float3 p, int sdfIndex)
{
    SdfObject star = sdfObjects[sdfIndex];
    float3 local = p - star.centerRadius.xyz;
    float radius = max(star.state.x, 0.001);
    float3 dir = normalize(local);
    float coronaLift = starGranulation(dir, timeSeconds) * 0.05;
    return length(local) - radius * (1.0 + coronaLift);
}

SdfSurface sdfSurface(float3 p, int sdfIndex)
{
    SdfObject star = sdfObjects[sdfIndex];
    float3 dir = normalize(p - star.centerRadius.xyz);
    float granulation = starGranulation(dir, timeSeconds);
    float hot = saturate(0.58 + granulation);

    SdfSurface surface;
    surface.baseColor = lerp(float3(1.0, 0.34, 0.08), float3(1.0, 0.88, 0.42), hot);
    surface.metallic = 0.0;
    surface.roughness = 0.72;
    surface.emission = surface.baseColor * lerp(3.8, 7.5, hot);
    return surface;
}

float3 shadeSdf(float2 uv, float travel, float3 p, float3 normal, int sdfIndex, SdfSurface surface)
{
    float3 viewDirection = normalize(cameraPosition - p);
    float rim = pow(1.0 - saturate(dot(normal, viewDirection)), 2.4);
    float pulse = sin(timeSeconds * 1.3 + dot(normal, float3(11.0, 7.0, 5.0))) * 0.5 + 0.5;
    float3 corona = float3(1.0, 0.42, 0.12) * (rim * 5.5 + pow(rim, 8.0) * 12.0) * lerp(0.82, 1.18, pulse);
    return surface.emission + corona;
}

#include "D3D12SdfProxy.hlsli"
