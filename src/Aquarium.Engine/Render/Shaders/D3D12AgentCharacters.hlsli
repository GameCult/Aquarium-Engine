#ifndef AQUARIUM_D3D12_AGENT_CHARACTERS_HLSLI
#define AQUARIUM_D3D12_AGENT_CHARACTERS_HLSLI

struct AgentSurface
{
    float distanceValue;
    float materialId;
    float costTier;
};

float imaginationPetalSdf(float3 local, float angle, float activity, float heartbeat, float timeSeconds)
{
    float2 radial = float2(cos(angle), sin(angle));
    float2 tangent = float2(-radial.y, radial.x);
    float phase = timeSeconds * 1.15 + angle * 1.7 + heartbeat * 6.28318;
    float3 petalCenter = float3(radial * (0.34 + activity * 0.05), 0.028 * sin(phase));
    float3 p = local - petalCenter;
    float3 q = float3(dot(p.xy, radial), dot(p.xy, tangent), p.z);
    float3 petalRadius = float3(0.50 + activity * 0.05, 0.13, 0.64 + 0.045 * sin(phase));
    return sdEllipsoid(q, petalRadius);
}

AgentSurface agentBodySdf(float3 local, AgentVisual agent)
{
    float pulse = agent.state.y;
    float core = sdSuperellipsoid(local, float3(0.92, 0.78, 0.62 + pulse * 0.06), 1.26);
    float ribA = sdTorus(local.xzy, float2(0.70, 0.024));
    float ribB = sdTorus(local.yxz, float2(0.56, 0.020));
    float3 nodeA = local - float3(0.45, -0.36, 0.24);
    float3 nodeB = local - float3(-0.38, 0.28, -0.18);
    float node = min(sdSphere(nodeA, 0.13), sdSphere(nodeB, 0.11));
    float shell = min(ribA, min(ribB, node));
    AgentSurface surface;
    surface.distanceValue = smoothUnion(core, shell, 0.045);
    surface.materialId = core <= shell ? 4.0 : 4.35;
    surface.costTier = 2.0;
    return surface;
}

AgentSurface agentImaginationSdf(float3 local, AgentVisual agent, float timeSeconds)
{
    float activity = agent.state.x;
    float heartbeat = agent.state.y;
    float core = sdSuperellipsoid(local, float3(0.30 + activity * 0.04, 0.30 + activity * 0.04, 0.42), 1.18);
    float phase = timeSeconds * 0.42;
    float petal0 = imaginationPetalSdf(local, phase, activity, heartbeat, timeSeconds);
    float petal1 = imaginationPetalSdf(local, phase + 1.2566371, activity, heartbeat, timeSeconds);
    float petal2 = imaginationPetalSdf(local, phase + 2.5132741, activity, heartbeat, timeSeconds);
    float petal3 = imaginationPetalSdf(local, phase + 3.7699112, activity, heartbeat, timeSeconds);
    float petal4 = imaginationPetalSdf(local, phase + 5.0265482, activity, heartbeat, timeSeconds);
    float petals = min(petal0, min(petal1, min(petal2, min(petal3, petal4))));
    float bloom = smoothUnion(core, petals, 0.055);
    float ring = sdTorus(local, float2(0.72 + activity * 0.08, 0.032));
    float halo = sdTorus(local.zxy, float2(0.50, 0.024));
    float detail = min(ring, halo);
    AgentSurface surface;
    surface.distanceValue = smoothUnion(bloom, detail, 0.05);
    surface.materialId = bloom <= detail ? 2.0 : 2.4;
    surface.costTier = 2.0;
    return surface;
}

AgentSurface agentFallbackSdf(float3 local, AgentVisual agent)
{
    float roleId = agent.previousCenterRole.w;
    float pulse = agent.state.y;
    float squareness = lerp(1.18, 1.72, frac(roleId * 0.37));
    float core = sdSuperellipsoid(local, float3(0.70, 0.58 + pulse * 0.04, 0.62), squareness);
    float belt = sdTorus(local.xzy, float2(0.56, 0.026));
    float crown = sdTorus((local - float3(0.0, 0.0, 0.16)).yzx, float2(0.40, 0.018));
    float detail = min(belt, crown);
    AgentSurface surface;
    surface.distanceValue = smoothUnion(core, detail, 0.032);
    surface.materialId = roleId + (core <= detail ? 0.0 : 0.22);
    surface.costTier = 1.0;
    return surface;
}

#endif
