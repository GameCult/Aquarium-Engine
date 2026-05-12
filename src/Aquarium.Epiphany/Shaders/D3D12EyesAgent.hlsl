static const int BODY_INDEX = 3;

#include "D3D12BodyCommon.hlsli"
#include "D3D12SdfMath.hlsli"
#include "D3D12AgentCharacters.hlsli"

float bodyDistance(float3 p, int agentIndex)
{
    AgentVisual agent = agentVisuals[agentIndex];
    float radius = max(agent.centerRadius.w, 0.001);
    float3 local = (p - agent.centerRadius.xyz) / radius;
    return agentFallbackSdf(local, agent) * radius;
}

BodySurface bodySurface(float3 p, int agentIndex)
{
    AgentVisual agent = agentVisuals[agentIndex];

    BodySurface surface;
    surface.albedo = lerp(float3(0.14, 0.46, 0.58), float3(0.78, 0.94, 0.90), agent.state.y);
    surface.roughness = 0.28;
    surface.f0 = float3(0.05, 0.09, 0.10);
    surface.emission = primitiveEmissionRadiance(bodyFieldId(agentIndex)) + surface.albedo * 0.06;
    return surface;
}

float3 shadeBody(float2 uv, float travel, float3 p, float3 normal, int agentIndex, BodySurface surface)
{
    return shadeBodyPbr(p, normal, surface);
}

#include "D3D12BodyProxy.hlsli"
