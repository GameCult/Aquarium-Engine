#ifndef BODY_TRACE_STEPS
#define BODY_TRACE_STEPS 128
#endif

#ifndef BODY_TRACE_STEP_SCALE
#define BODY_TRACE_STEP_SCALE 0.20
#endif

#ifndef BODY_TRACE_MIN_STEP
#define BODY_TRACE_MIN_STEP 0.0009
#endif

#ifndef BODY_TRACE_MAX_STEP_RADIUS_SCALE
#define BODY_TRACE_MAX_STEP_RADIUS_SCALE 0.012
#endif

float3 bodyNormal(float3 p, int agentIndex)
{
    float epsilon = 0.006;
    float dx = bodySurface(p + float3(epsilon, 0.0, 0.0), agentIndex).distanceValue - bodySurface(p - float3(epsilon, 0.0, 0.0), agentIndex).distanceValue;
    float dy = bodySurface(p + float3(0.0, epsilon, 0.0), agentIndex).distanceValue - bodySurface(p - float3(0.0, epsilon, 0.0), agentIndex).distanceValue;
    float dz = bodySurface(p + float3(0.0, 0.0, epsilon), agentIndex).distanceValue - bodySurface(p - float3(0.0, 0.0, epsilon), agentIndex).distanceValue;
    return normalize(float3(dx, dy, dz));
}

bool refineBodyHit(float3 origin, float3 direction, int agentIndex, float lowTravel, float highTravel, out float travel, out float3 normal, out BodySurface surface)
{
    BodySurface highSurface = bodySurface(origin + direction * highTravel, agentIndex);

    [unroll]
    for (int i = 0; i < 8; i++)
    {
        float midTravel = (lowTravel + highTravel) * 0.5;
        BodySurface midSurface = bodySurface(origin + direction * midTravel, agentIndex);
        if (midSurface.distanceValue <= 0.0)
        {
            highTravel = midTravel;
            highSurface = midSurface;
        }
        else
        {
            lowTravel = midTravel;
        }
    }

    travel = highTravel;
    surface = highSurface;
    normal = bodyNormal(origin + direction * travel, agentIndex);
    return true;
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
    float previousTravel = travel;
    float previousDistance = surface.distanceValue;
    float maxStep = max(agent.centerRadius.w * BODY_TRACE_MAX_STEP_RADIUS_SCALE, BODY_TRACE_MIN_STEP);

    [loop]
    for (int stepIndex = 0; stepIndex < BODY_TRACE_STEPS; stepIndex++)
    {
        if (travel > endTravel)
        {
            return false;
        }

        float3 p = origin + direction * travel;
        surface = bodySurface(p, agentIndex);
        stepCount = (float)(stepIndex + 1);
        if (previousDistance > 0.0 && surface.distanceValue <= 0.0)
        {
            return refineBodyHit(origin, direction, agentIndex, previousTravel, travel, travel, normal, surface);
        }

        if (abs(surface.distanceValue) < max(BODY_TRACE_MIN_STEP, travel * 0.00018))
        {
            normal = bodyNormal(p, agentIndex);
            return true;
        }

        previousTravel = travel;
        previousDistance = surface.distanceValue;
        travel += clamp(abs(surface.distanceValue) * BODY_TRACE_STEP_SCALE, BODY_TRACE_MIN_STEP, maxStep);
    }

    return false;
}

SceneOut D3D12BodyProxyPS(AgentProxyVertexOut input)
{
    float2 pixel = float2(input.position.x, resolution.y - input.position.y);
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
    output.control = float4(surface.materialId, 1.0, stepCount / (float)BODY_TRACE_STEPS, surface.lodTier + surface.costTier * 0.1);
    output.eventColor = float4(0.0, 0.0, 0.0, 0.0);
    output.eventMetadata = float4(FIELD_ID_GRID, farDistance + 1.0, 0.0, 0.0);
    output.depth = saturate(travel / max(farDistance, 0.001));
    return output;
}
