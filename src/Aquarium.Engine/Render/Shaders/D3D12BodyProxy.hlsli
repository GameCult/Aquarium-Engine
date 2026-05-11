float3 bodyNormal(float3 p, int agentIndex)
{
    float epsilon = 0.006;
    float dx = bodySurface(p + float3(epsilon, 0.0, 0.0), agentIndex).distanceValue - bodySurface(p - float3(epsilon, 0.0, 0.0), agentIndex).distanceValue;
    float dy = bodySurface(p + float3(0.0, epsilon, 0.0), agentIndex).distanceValue - bodySurface(p - float3(0.0, epsilon, 0.0), agentIndex).distanceValue;
    float dz = bodySurface(p + float3(0.0, 0.0, epsilon), agentIndex).distanceValue - bodySurface(p - float3(0.0, 0.0, epsilon), agentIndex).distanceValue;
    return normalize(float3(dx, dy, dz));
}

bool traceBody(float3 origin, float3 direction, int agentIndex, out float travel, out float3 normal, out BodySurface surface, out float stepCount)
{
    AgentVisual agent = agentVisuals[agentIndex];
    float boundRadius = max(agent.centerRadius.w * 1.42, 0.001);
    if (!traceSphere(origin, direction, agent.centerRadius.xyz, boundRadius, travel))
    {
        normal = 0.0;
        stepCount = 0.0;
        surface = bodySurface(agent.centerRadius.xyz, agentIndex);
        return false;
    }

    float3 oc = origin - agent.centerRadius.xyz;
    float b = dot(oc, direction);
    float c = dot(oc, oc) - boundRadius * boundRadius;
    float h = sqrt(max(b * b - c, 0.0));
    float endTravel = min(-b + h, farDistance);
    travel = max(-b - h, 0.0);
    normal = 0.0;
    stepCount = 0.0;
    surface = bodySurface(origin + direction * travel, agentIndex);

    [loop]
    for (int stepIndex = 0; stepIndex < 72; stepIndex++)
    {
        if (travel > endTravel)
        {
            return false;
        }

        float3 p = origin + direction * travel;
        surface = bodySurface(p, agentIndex);
        stepCount = (float)(stepIndex + 1);
        if (abs(surface.distanceValue) < max(0.0016, travel * 0.00018))
        {
            normal = bodyNormal(p, agentIndex);
            return true;
        }

        travel += max(abs(surface.distanceValue) * 0.12, 0.0016);
    }

    return false;
}

SceneOut D3D12BodyProxyPS(AgentProxyVertexOut input)
{
    float2 pixel = input.position.xy;
    float2 uv = float2(pixel.x / max(resolution.x, 1.0), 1.0 - pixel.y / max(resolution.y, 1.0));
    float3 rayDirection = rayDirectionForPixel(pixel, jitterPixels, cameraPosition, gridCenter);
    int agentIndex = clamp((int)round(input.agentIndex), 0, AGENT_VISUAL_COUNT - 1);

    float travel;
    float3 normal;
    BodySurface surface;
    float stepCount;
    if (!traceBody(cameraPosition, rayDirection, agentIndex, travel, normal, surface, stepCount))
    {
        discard;
    }

    float3 p = cameraPosition + rayDirection * travel;
    SceneOut output;
    output.colorTravel = float4(shadeBody(uv, travel, p, normal, agentIndex, surface), min(travel, farDistance + 1.0));
    output.metadata = float4(surface.fieldId, normal);
    output.control = float4(surface.materialId, 1.0, stepCount / 72.0, surface.lodTier + surface.costTier * 0.1);
    output.eventColor = float4(0.0, 0.0, 0.0, 0.0);
    output.eventMetadata = float4(FIELD_ID_GRID, farDistance + 1.0, 0.0, 0.0);
    output.depth = saturate(travel / max(farDistance, 0.001));
    return output;
}
