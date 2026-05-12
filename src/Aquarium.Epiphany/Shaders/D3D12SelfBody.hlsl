static const int BODY_INDEX = 0;

#include "D3D12BodyCommon.hlsli"
#include "D3D12SdfMath.hlsli"

float bodyDistance(float3 p, int agentIndex)
{
    AgentVisual agent = agentVisuals[agentIndex];
    float radius = max(agent.centerRadius.w, 0.001);
    float3 local = (p - agent.centerRadius.xyz) / radius;
    return sdSphere(local, 1.0) * radius;
}

BodySurface bodySurface(float3 p, int agentIndex)
{
    BodySurface surface;
    surface.albedo = 0.0;
    surface.roughness = 0.0;
    surface.f0 = 0.0;
    surface.emission = primitiveEmissionRadiance(FIELD_ID_SELF);
    return surface;
}

float3 shadeBody(float2 uv, float travel, float3 p, float3 normal, int agentIndex, BodySurface surface)
{
    return primitiveEmissionRadiance(FIELD_ID_SELF);
}

#include "D3D12BodyProxy.hlsli"
