#ifndef SDF_TRACE_STEPS
#define SDF_TRACE_STEPS 384
#endif

#ifndef SDF_TRACE_STEP_SCALE
#define SDF_TRACE_STEP_SCALE 0.20
#endif

#ifndef SDF_TRACE_MIN_STEP
#define SDF_TRACE_MIN_STEP 0.0009
#endif

#ifndef SDF_TRACE_MAX_STEP_RADIUS_SCALE
#define SDF_TRACE_MAX_STEP_RADIUS_SCALE 0.012
#endif

float3 sdfNormal(float3 p, int sdfIndex)
{
    float epsilon = 0.006;
    float dx = sdfDistance(p + float3(epsilon, 0.0, 0.0), sdfIndex) - sdfDistance(p - float3(epsilon, 0.0, 0.0), sdfIndex);
    float dy = sdfDistance(p + float3(0.0, epsilon, 0.0), sdfIndex) - sdfDistance(p - float3(0.0, epsilon, 0.0), sdfIndex);
    float dz = sdfDistance(p + float3(0.0, 0.0, epsilon), sdfIndex) - sdfDistance(p - float3(0.0, 0.0, epsilon), sdfIndex);
    return normalize(float3(dx, dy, dz));
}

bool refineSdfHit(float3 origin, float3 direction, int sdfIndex, float lowTravel, float highTravel, out float travel, out float3 normal, out SdfSurface surface)
{
    [unroll]
    for (int i = 0; i < 8; i++)
    {
        float midTravel = (lowTravel + highTravel) * 0.5;
        float midDistance = sdfDistance(origin + direction * midTravel, sdfIndex);
        if (midDistance <= 0.0)
        {
            highTravel = midTravel;
        }
        else
        {
            lowTravel = midTravel;
        }
    }

    travel = highTravel;
    float3 p = origin + direction * travel;
    surface = sdfSurface(p, sdfIndex);
    normal = sdfNormal(p, sdfIndex);
    return true;
}

bool traceSdf(float3 origin, float3 direction, int sdfIndex, out float travel, out float3 normal, out SdfSurface surface, out float stepCount)
{
    SdfObject sdfObject = sdfObjects[sdfIndex];
    float boundRadius = max(sdfObject.centerRadius.w * 1.42, 0.001);
    if (!traceSphere(origin, direction, sdfObject.centerRadius.xyz, boundRadius, travel))
    {
        normal = 0.0;
        stepCount = 0.0;
        surface = sdfSurface(sdfObject.centerRadius.xyz, sdfIndex);
        return false;
    }

    float3 oc = origin - sdfObject.centerRadius.xyz;
    float b = dot(oc, direction);
    float c = dot(oc, oc) - boundRadius * boundRadius;
    float h = sqrt(max(b * b - c, 0.0));
    float endTravel = min(-b + h, farDistance);
    travel = max(-b - h, 0.0);
    normal = 0.0;
    stepCount = 0.0;
    surface.albedo = 0.0;
    surface.roughness = 0.0;
    surface.f0 = 0.0;
    surface.emission = 0.0;
    float previousTravel = travel;
    float previousDistance = sdfDistance(origin + direction * travel, sdfIndex);
    float maxStep = max(sdfObject.centerRadius.w * SDF_TRACE_MAX_STEP_RADIUS_SCALE, SDF_TRACE_MIN_STEP);

    [loop]
    for (int stepIndex = 0; stepIndex < SDF_TRACE_STEPS; stepIndex++)
    {
        if (travel > endTravel)
        {
            return false;
        }

        float3 p = origin + direction * travel;
        float distanceValue = sdfDistance(p, sdfIndex);
        stepCount = (float)(stepIndex + 1);
        if (distanceValue <= 0.0)
        {
            if (previousDistance > 0.0)
            {
                return refineSdfHit(origin, direction, sdfIndex, previousTravel, travel, travel, normal, surface);
            }

            surface = sdfSurface(p, sdfIndex);
            normal = sdfNormal(p, sdfIndex);
            return true;
        }

        previousTravel = travel;
        previousDistance = distanceValue;
        travel += clamp(abs(distanceValue) * SDF_TRACE_STEP_SCALE, SDF_TRACE_MIN_STEP, maxStep);
    }

    return false;
}

SceneOut D3D12SdfProxyPS(SdfObjectProxyVertexOut input)
{
    float2 pixel = float2(input.position.x, resolution.y - input.position.y);
    float2 uv = float2(pixel.x / max(resolution.x, 1.0), 1.0 - pixel.y / max(resolution.y, 1.0));
    float3 rayDirection = rayDirectionForPixel(pixel, jitterPixels, cameraPosition, viewCenter);
    int sdfIndex = clamp((int)round(input.sdfIndex), 0, SDF_OBJECT_VISUAL_COUNT - 1);

    float travel;
    float3 normal;
    SdfSurface surface;
    float stepCount;
    if (!traceSdf(cameraPosition, rayDirection, sdfIndex, travel, normal, surface, stepCount))
    {
        discard;
    }

    float3 p = cameraPosition + rayDirection * travel;
    float fieldId = sdfFieldId(sdfIndex);
    SceneOut output;
    output.colorTravel = float4(shadeSdf(uv, travel, p, normal, sdfIndex, surface), min(travel, farDistance + 1.0));
    output.metadata = float4(fieldId, normal);
    output.control = float4(1.0, stepCount / (float)SDF_TRACE_STEPS, 0.0, 0.0);
    output.depth = saturate(travel / max(farDistance, 0.001));
    return output;
}
