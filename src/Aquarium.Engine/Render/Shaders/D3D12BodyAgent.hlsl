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

    BodySurface surface;
    surface.albedo = lerp(float3(0.18, 0.42, 0.34), float3(0.70, 0.86, 0.60), agent.state.y);
    surface.roughness = 0.56;
    surface.f0 = 0.04;
    surface.emission = primitiveEmissionRadiance(bodyFieldId(agentIndex));
    return surface;
}

float3 shadeBody(float2 uv, float travel, float3 p, float3 normal, int agentIndex, BodySurface surface)
{
    return shadeBodyPbr(p, normal, surface);
}

#include "D3D12BodyProxy.hlsli"
