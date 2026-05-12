static const int BODY_INDEX = 2;

#include "D3D12BodyCommon.hlsli"
#include "D3D12SdfMath.hlsli"
#include "D3D12AgentCharacters.hlsli"

float bodyDistance(float3 p, int agentIndex)
{
    AgentVisual agent = agentVisuals[agentIndex];
    float radius = max(agent.centerRadius.w, 0.001);
    float3 local = (p - agent.centerRadius.xyz) / radius;
    return agentImaginationSdf(local, agent, timeSeconds).distanceValue * radius;
}

BodySurface bodySurface(float3 p, int agentIndex)
{
    AgentVisual agent = agentVisuals[agentIndex];

    BodySurface surface;
    surface.albedo = lerp(float3(0.36, 0.16, 0.70), float3(0.96, 0.48, 0.92), agent.state.x);
    surface.roughness = 0.34;
    surface.f0 = float3(0.06, 0.035, 0.12);
    surface.emission = primitiveEmissionRadiance(bodyFieldId(agentIndex)) + surface.albedo * (0.08 + agent.state.y * 0.08);
    return surface;
}

float3 shadeBody(float2 uv, float travel, float3 p, float3 normal, int agentIndex, BodySurface surface)
{
    return shadeBodyPbr(p, normal, surface);
}

#include "D3D12BodyProxy.hlsli"
