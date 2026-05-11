static const int BODY_INDEX = 4;

#include "D3D12BodyCommon.hlsli"
#include "D3D12SdfMath.hlsli"
#include "D3D12AgentCharacters.hlsli"

float bodyDistance(float3 p, int agentIndex)
{
    AgentVisual agent = agentVisuals[agentIndex];
    float radius = max(agent.centerRadius.w, 0.001);
    float3 local = (p - agent.centerRadius.xyz) / radius;
    return agentBodySdf(local, agent).distanceValue * radius;
}

BodySurface bodySurface(float3 p, int agentIndex)
{
    AgentVisual agent = agentVisuals[agentIndex];
    float radius = max(agent.centerRadius.w, 0.001);
    float3 local = (p - agent.centerRadius.xyz) / radius;
    AgentSurface agentSurface = agentBodySdf(local, agent);

    BodySurface surface;
    surface.distanceValue = agentSurface.distanceValue * radius;
    surface.materialId = agentSurface.materialId;
    surface.fieldId = FIELD_ID_AGENT_BASE + (float)agentIndex;
    surface.roleId = agent.previousCenterRole.w;
    surface.lodTier = agent.lodIndexFlags.x;
    surface.costTier = agentSurface.costTier;
    surface.albedo = 0.0;
    surface.roughness = 0.0;
    surface.f0 = 0.0;
    surface.emission = 0.0;
    return surface;
}

float3 shadeBody(float2 uv, float travel, float3 p, float3 normal, int agentIndex, BodySurface surface)
{
    return shadeRoleAgentBody(p, normal, agentIndex);
}

#include "D3D12BodyProxy.hlsli"
