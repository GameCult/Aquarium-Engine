static const int BODY_INDEX = 6;

#include "D3D12BodyCommon.hlsli"
#include "D3D12SdfMath.hlsli"
#include "D3D12AgentCharacters.hlsli"

BodySurface bodySurface(float3 p, int agentIndex)
{
    AgentVisual agent = agentVisuals[agentIndex];
    float radius = max(agent.centerRadius.w, 0.001);
    float3 local = (p - agent.centerRadius.xyz) / radius;
    AgentSurface agentSurface = agentFallbackSdf(local, agent);

    BodySurface surface;
    surface.distanceValue = agentSurface.distanceValue * radius;
    surface.materialId = agentSurface.materialId;
    surface.fieldId = FIELD_ID_AGENT_BASE + (float)agentIndex;
    surface.roleId = agent.previousCenterRole.w;
    surface.lodTier = agent.lodIndexFlags.x;
    surface.costTier = agentSurface.costTier;
    return surface;
}

float3 shadeBody(float2 uv, float travel, float3 p, float3 normal, int agentIndex, BodySurface surface)
{
    return shadeRoleAgentBody(p, normal, agentIndex);
}

#include "D3D12BodyProxy.hlsli"
