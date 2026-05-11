#include "D3D12BodyCommon.hlsli"
#include "D3D12SdfMath.hlsli"

BodySurface bodySurface(float3 p, int agentIndex)
{
    AgentVisual agent = agentVisuals[agentIndex];
    float radius = max(agent.centerRadius.w, 0.001);
    float3 local = (p - agent.centerRadius.xyz) / radius;

    BodySurface surface;
    surface.distanceValue = sdSphere(local, 1.0) * radius;
    surface.materialId = 0.0;
    surface.fieldId = FIELD_ID_SELF;
    surface.roleId = 0.0;
    surface.lodTier = 0.0;
    surface.costTier = 0.0;
    return surface;
}

float3 shadeBody(float2 uv, float travel, float3 p, float3 normal, int agentIndex, BodySurface surface)
{
    return primitiveEmissionRadiance(FIELD_ID_SELF);
}

#include "D3D12BodyProxy.hlsli"
