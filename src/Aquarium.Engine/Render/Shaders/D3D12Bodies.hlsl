cbuffer AquariumFrame : register(b0)
{
    float2 resolution;
    float timeSeconds;
    float gridRadius;
    float3 cameraPosition;
    float farDistance;
    float2 gridCenter;
    float frameIndex;
    float previousTimeSeconds;
    float3 previousCameraPosition;
    float previousGridRadius;
    float2 previousGridCenter;
    float2 jitterPixels;
    float2 previousJitterPixels;
    float renderDebugMode;
    float exposure;
    float bloomIntensity;
    float bloomVeilIntensity;
    float4 cursorWorlds;
};

TextureCube<float4> studioPmremTexture : register(t22);
TextureCube<float4> studioIrradianceTexture : register(t23);
SamplerState gridSampler : register(s0);

struct BodyLight
{
    float4 centerRadius;
    float4 radianceFieldId;
};

StructuredBuffer<BodyLight> bodyLights : register(t12);

struct AgentVisual
{
    float4 centerRadius;
    float4 previousCenterRole;
    float4 state;
    float4 lodIndexFlags;
};

StructuredBuffer<AgentVisual> agentVisuals : register(t24);

#include "D3D12SdfMath.hlsli"
#include "D3D12AgentCharacters.hlsli"

static const int AGENT_VISUAL_COUNT = 7;
static const int SELF_OBJECT_INDEX = AGENT_VISUAL_COUNT - 2;
static const int CURSOR_OBJECT_INDEX = AGENT_VISUAL_COUNT - 1;
static const int ROLE_AGENT_COUNT = AGENT_VISUAL_COUNT - 2;
static const float FIELD_ID_SELF = 2.0;
static const float FIELD_ID_GRID = 4.0;
static const float FIELD_ID_CURSOR = 5.0;
static const float FIELD_ID_AGENT_BASE = 10.0;
static const float CURSOR_RADIUS = 0.56;
static const float PI = 3.14159265359;
static const int BODY_LIGHT_COUNT = 8;
static const float STUDIO_PMREM_MAX_LOD = 9.0;
static const float STUDIO_PMREM_SPECULAR_INTENSITY = 0.34;
static const float STUDIO_IRRADIANCE_INTENSITY = 0.74;

struct AgentProxyVertexOut
{
    float4 position : SV_Position;
    nointerpolation float agentIndex : TEXCOORD0;
};

struct SceneOut
{
    float4 colorTravel : SV_Target0;
    float4 metadata : SV_Target1;
    float4 control : SV_Target2;
    float4 eventColor : SV_Target3;
    float4 eventMetadata : SV_Target4;
    float depth : SV_Depth;
};

struct SolidHit
{
    bool hit;
    float travel;
    float3 normal;
    float fieldId;
    int primitiveId;
    float roleId;
    float materialId;
    float stepCount;
    float lodTier;
    float costTier;
};

void cameraBasis(float3 camera, float2 center, out float3 forward, out float3 right, out float3 up)
{
    float3 target = float3(center, 0.0);
    forward = normalize(target - camera);
    right = normalize(cross(forward, float3(0.0, 0.0, 1.0)));
    up = cross(right, forward);
}

float3 rayDirectionForPixel(float2 pixel, float2 jitter, float3 camera, float2 center)
{
    float2 ndc = ((pixel + jitter) * 2.0 - resolution) / resolution.y;
    float3 forward;
    float3 right;
    float3 up;
    cameraBasis(camera, center, forward, right, up);
    return normalize(forward * 1.6 + right * ndc.x + up * ndc.y);
}

float hash21(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

bool traceSphere(float3 origin, float3 direction, float3 center, float radius, out float travel)
{
    float3 oc = origin - center;
    float b = dot(oc, direction);
    float c = dot(oc, oc) - radius * radius;
    float h = b * b - c;
    if (h < 0.0)
    {
        travel = farDistance + 1.0;
        return false;
    }

    h = sqrt(h);
    float t = -b - h;
    if (t < 0.0)
    {
        t = -b + h;
    }

    travel = t;
    return t > 0.0 && t < farDistance;
}

bool traceSphereInInterval(float3 origin, float3 direction, float3 center, float radius, float intervalStart, float intervalEnd, out float travel)
{
    if (!traceSphere(origin, direction, center, radius, travel))
    {
        return false;
    }

    return travel >= intervalStart && travel <= intervalEnd;
}

float cursorHibiscusSdf(float3 p)
{
    AgentVisual cursor = agentVisuals[CURSOR_OBJECT_INDEX];
    float3 local = (p - cursor.centerRadius.xyz) / CURSOR_RADIUS;
    return sdHibiscusCursor(local, timeSeconds) * CURSOR_RADIUS;
}

float3 cursorHibiscusNormal(float3 p)
{
    float epsilon = 0.006;
    float dx = cursorHibiscusSdf(p + float3(epsilon, 0.0, 0.0)) - cursorHibiscusSdf(p - float3(epsilon, 0.0, 0.0));
    float dy = cursorHibiscusSdf(p + float3(0.0, epsilon, 0.0)) - cursorHibiscusSdf(p - float3(0.0, epsilon, 0.0));
    float dz = cursorHibiscusSdf(p + float3(0.0, 0.0, epsilon)) - cursorHibiscusSdf(p - float3(0.0, 0.0, epsilon));
    return normalize(float3(dx, dy, dz));
}

AgentSurface agentVisualSdf(float3 p, int agentIndex)
{
    AgentVisual agent = agentVisuals[agentIndex];
    float radius = max(agent.centerRadius.w, 0.001);
    float3 local = (p - agent.centerRadius.xyz) / radius;
    float roleId = agent.previousCenterRole.w;
    AgentSurface surface = agentFallbackSdf(local, agent);
    if (agentIndex == SELF_OBJECT_INDEX)
    {
        surface.distanceValue = sdSphere(local, 1.0);
        surface.materialId = 0.0;
        surface.costTier = 0.0;
    }
    else if (agentIndex == CURSOR_OBJECT_INDEX)
    {
        surface.distanceValue = cursorHibiscusSdf(p);
        surface.materialId = 5.0;
        surface.costTier = 1.0;
        return surface;
    }
    else if (abs(roleId - 2.0) < 0.25)
    {
        surface = agentImaginationSdf(local, agent, timeSeconds);
    }
    else if (abs(roleId - 4.0) < 0.25)
    {
        surface = agentBodySdf(local, agent);
    }

    surface.distanceValue *= radius;
    return surface;
}

float3 agentVisualNormal(float3 p, int agentIndex)
{
    float epsilon = 0.006;
    float dx = agentVisualSdf(p + float3(epsilon, 0.0, 0.0), agentIndex).distanceValue - agentVisualSdf(p - float3(epsilon, 0.0, 0.0), agentIndex).distanceValue;
    float dy = agentVisualSdf(p + float3(0.0, epsilon, 0.0), agentIndex).distanceValue - agentVisualSdf(p - float3(0.0, epsilon, 0.0), agentIndex).distanceValue;
    float dz = agentVisualSdf(p + float3(0.0, 0.0, epsilon), agentIndex).distanceValue - agentVisualSdf(p - float3(0.0, 0.0, epsilon), agentIndex).distanceValue;
    return normalize(float3(dx, dy, dz));
}

bool traceAgentVisual(float3 origin, float3 direction, int agentIndex, float intervalStart, float intervalEnd, out float travel, out float3 normal, out float materialId, out float stepCount, out float costTier)
{
    AgentVisual agent = agentVisuals[agentIndex];
    float boundRadius = max(agent.centerRadius.w * 1.42, 0.001);
    if (!traceSphere(origin, direction, agent.centerRadius.xyz, boundRadius, travel))
    {
        normal = 0.0;
        materialId = 0.0;
        stepCount = 0.0;
        costTier = 0.0;
        return false;
    }

    float3 oc = origin - agent.centerRadius.xyz;
    float b = dot(oc, direction);
    float c = dot(oc, oc) - boundRadius * boundRadius;
    float h = sqrt(max(b * b - c, 0.0));
    float startTravel = max(max(-b - h, intervalStart), 0.0);
    float endTravel = min(-b + h, intervalEnd);
    travel = startTravel;
    normal = 0.0;
    materialId = 0.0;
    stepCount = 0.0;
    costTier = 0.0;
    [loop]
    for (int stepIndex = 0; stepIndex < 72; stepIndex++)
    {
        if (travel > endTravel)
        {
            return false;
        }

        float3 p = origin + direction * travel;
        AgentSurface surface = agentVisualSdf(p, agentIndex);
        stepCount = (float)(stepIndex + 1);
        costTier = max(costTier, surface.costTier);
        if (abs(surface.distanceValue) < max(0.0016, travel * 0.00018))
        {
            normal = agentVisualNormal(p, agentIndex);
            materialId = surface.materialId;
            return true;
        }

        travel += max(abs(surface.distanceValue) * 0.12, 0.0016);
    }

    return false;
}

float3 primitiveCenterAt(int primitiveId)
{
    int agentIndex = clamp(primitiveId, 0, AGENT_VISUAL_COUNT - 1);
    return agentVisuals[agentIndex].centerRadius.xyz;
}

float primitiveRadius(int primitiveId)
{
    int agentIndex = clamp(primitiveId, 0, AGENT_VISUAL_COUNT - 1);
    return agentVisuals[agentIndex].centerRadius.w;
}

float primitiveFieldId(int primitiveId)
{
    if (primitiveId == SELF_OBJECT_INDEX)
    {
        return FIELD_ID_SELF;
    }

    if (primitiveId == CURSOR_OBJECT_INDEX)
    {
        return FIELD_ID_CURSOR;
    }

    return FIELD_ID_AGENT_BASE + (float)primitiveId;
}

void considerPrimitiveHit(float3 origin, float3 direction, int primitiveId, float intervalStart, float intervalEnd, inout SolidHit nearest)
{
    if (primitiveId < 0 || primitiveId >= AGENT_VISUAL_COUNT)
    {
        return;
    }

    if (primitiveId < SELF_OBJECT_INDEX || primitiveId == CURSOR_OBJECT_INDEX)
    {
        float hitTravel;
        float3 hitNormal;
        float materialId;
        float stepCount;
        float costTier;
        if (traceAgentVisual(origin, direction, primitiveId, intervalStart, min(intervalEnd, nearest.travel), hitTravel, hitNormal, materialId, stepCount, costTier))
        {
            nearest.hit = true;
            nearest.travel = hitTravel;
            nearest.normal = hitNormal;
            nearest.fieldId = primitiveFieldId(primitiveId);
            nearest.primitiveId = primitiveId;
            nearest.roleId = primitiveId < SELF_OBJECT_INDEX ? agentVisuals[primitiveId].previousCenterRole.w : 0.0;
            nearest.materialId = materialId;
            nearest.stepCount = stepCount;
            nearest.lodTier = agentVisuals[primitiveId].lodIndexFlags.x;
            nearest.costTier = costTier;
        }

        return;
    }

    float radius = primitiveRadius(primitiveId);
    float3 center = primitiveCenterAt(primitiveId);
    float hitTravel;
    if (traceSphereInInterval(origin, direction, center, radius, intervalStart, min(intervalEnd, nearest.travel), hitTravel))
    {
        float3 p = origin + direction * hitTravel;
        nearest.hit = true;
        nearest.travel = hitTravel;
        nearest.normal = normalize(p - center);
        nearest.fieldId = primitiveFieldId(primitiveId);
        nearest.primitiveId = primitiveId;
        nearest.roleId = 0.0;
        nearest.materialId = 0.0;
        nearest.stepCount = 1.0;
        nearest.lodTier = 0.0;
        nearest.costTier = 0.0;
    }
}

float3 studioPmremDirection(float3 worldDirection)
{
    return normalize(float3(worldDirection.x, worldDirection.z, worldDirection.y));
}

float3 studioPmremSample(float3 worldDirection, float lod)
{
    return studioPmremTexture.SampleLevel(gridSampler, studioPmremDirection(worldDirection), lod).rgb;
}

float3 primitiveEmissionRadiance(float fieldId)
{
    [loop]
    for (int i = 0; i < BODY_LIGHT_COUNT; i++)
    {
        BodyLight light = bodyLights[i];
        if (abs(light.radianceFieldId.w - fieldId) < 0.25)
        {
            return light.radianceFieldId.rgb;
        }
    }

    return 0.0;
}

float3 bodyLightIrradianceAt(float3 p, float3 normal)
{
    float3 irradiance = 0.0;
    [loop]
    for (int i = 0; i < BODY_LIGHT_COUNT; i++)
    {
        BodyLight light = bodyLights[i];
        float3 radiance = light.radianceFieldId.rgb;
        if (dot(radiance, radiance) <= 0.000001)
        {
            continue;
        }

        float3 toLight = light.centerRadius.xyz - p;
        float distanceSquared = max(dot(toLight, toLight), 0.01);
        float3 lightDirection = toLight * rsqrt(distanceSquared);
        float cosine = saturate(dot(normal, lightDirection));
        float radius = max(light.centerRadius.w, 0.001);
        float solidAngle = saturate((radius * radius) / distanceSquared);
        irradiance += radiance * cosine * solidAngle * 6.0;
    }

    return irradiance;
}

float3 cursorSpecularBodyLightRadiance(float3 p, float3 normal)
{
    static const float MinimumRoughness = 0.045;
    static const float CursorRoughness = 0.22;

    float3 viewDirection = normalize(cameraPosition - p);
    float ndv = saturate(dot(normal, viewDirection));
    float roughness = max(CursorRoughness, MinimumRoughness);
    float alpha = roughness * roughness;
    float alpha2 = alpha * alpha;
    float k = (roughness + 1.0);
    k = (k * k) * 0.125;
    float geometryV = ndv / max(ndv * (1.0 - k) + k, 0.00001);
    float3 f0 = float3(1.0, 0.38, 0.72);
    float3 result = 0.0;
    [loop]
    for (int i = 0; i < BODY_LIGHT_COUNT; i++)
    {
        BodyLight light = bodyLights[i];
        float3 radiance = light.radianceFieldId.rgb;
        if (dot(radiance, radiance) <= 0.000001)
        {
            continue;
        }

        float3 toLight = light.centerRadius.xyz - p;
        float distanceSquared = max(dot(toLight, toLight), 0.01);
        float3 lightDirection = toLight * rsqrt(distanceSquared);
        float radius = max(light.centerRadius.w, 0.001);
        float3 irradiance = radiance * saturate((radius * radius) / distanceSquared) * 7.0;
        float3 halfVector = normalize(lightDirection + viewDirection);
        float ndl = saturate(dot(normal, lightDirection));
        float ndh = saturate(dot(normal, halfVector));
        float vdh = saturate(dot(viewDirection, halfVector));
        float denominator = ndh * ndh * (alpha2 - 1.0) + 1.0;
        float distribution = alpha2 / max(PI * denominator * denominator, 0.00001);
        float geometryL = ndl / max(ndl * (1.0 - k) + k, 0.00001);
        float geometry = geometryL * geometryV;
        float3 fresnel = f0 + (1.0 - f0) * pow(1.0 - vdh, 5.0);
        float3 specular = (distribution * geometry) * fresnel / max(4.0 * ndl * ndv, 0.00001);
        result += specular * irradiance * ndl;
    }

    return result;
}

float3 fresnelSchlickRoughness(float cosine, float3 f0, float roughness)
{
    return f0 + (max(1.0 - roughness, f0) - f0) * pow(1.0 - cosine, 5.0);
}

float3 studioPmremSpecularRadiance(float3 p, float3 normal, float roughness, float3 f0)
{
    float3 viewDirection = normalize(cameraPosition - p);
    float ndv = saturate(dot(normal, viewDirection));
    float3 reflectionDirection = reflect(-viewDirection, normal);
    float lod = saturate(roughness) * STUDIO_PMREM_MAX_LOD;
    float3 radiance = studioPmremSample(reflectionDirection, lod);
    float3 fresnel = fresnelSchlickRoughness(ndv, f0, roughness);
    return radiance * fresnel * STUDIO_PMREM_SPECULAR_INTENSITY;
}

float3 studioIrradianceDiffuseRadiance(float3 p, float3 normal, float3 albedo, float roughness, float3 f0)
{
    float3 viewDirection = normalize(cameraPosition - p);
    float ndv = saturate(dot(normal, viewDirection));
    float3 fresnel = fresnelSchlickRoughness(ndv, f0, roughness);
    float3 diffuseShare = 1.0 - fresnel;
    float3 irradiance = studioIrradianceTexture.SampleLevel(gridSampler, studioPmremDirection(normal), 0.0).rgb;
    return diffuseShare * albedo * irradiance * (STUDIO_IRRADIANCE_INTENSITY / PI);
}

float3 cursorEmissionRadiance(float3 p, float3 normal)
{
    AgentVisual cursor = agentVisuals[CURSOR_OBJECT_INDEX];
    float3 local = (p - cursor.centerRadius.xyz) / CURSOR_RADIUS;
    float petal = 0.55 + 0.45 * cos(atan2(local.y, local.x) * 3.0 - timeSeconds * 1.8);
    float throat = exp(-dot(local.xy, local.xy) * 7.0) * smoothstep(0.46, -0.28, local.z);
    float rim = pow(1.0 - saturate(dot(normal, normalize(cameraPosition - p))), 2.0);
    float3 petalColor = lerp(float3(1.25, 0.12, 0.56), float3(1.0, 0.42, 0.88), petal);
    float3 throatColor = float3(1.0, 0.76, 0.22);
    return petalColor * (0.82 + rim * 0.75) + throatColor * throat * 1.7;
}

float3 shadeBody(float2 uv, float travel, float3 p, float3 normal, int primitiveId)
{
    float fieldId = primitiveFieldId(primitiveId);
    float3 emission = primitiveEmissionRadiance(fieldId);
    if (primitiveId == SELF_OBJECT_INDEX)
    {
        return emission;
    }

    if (primitiveId == CURSOR_OBJECT_INDEX)
    {
        static const float CursorRoughness = 0.22;
        float3 cursorF0 = float3(1.0, 0.38, 0.72);
        return cursorEmissionRadiance(p, normal)
            + cursorSpecularBodyLightRadiance(p, normal)
            + studioPmremSpecularRadiance(p, normal, CursorRoughness, cursorF0);
    }

    int agentIndex = clamp(primitiveId, 0, ROLE_AGENT_COUNT - 1);
    float roleId = agentVisuals[agentIndex].previousCenterRole.w;
    float materialPulse = agentVisuals[agentIndex].state.y;
    float3 albedo = lerp(float3(0.34, 0.42, 0.18), float3(0.70, 0.76, 0.42), hash21(float2(primitiveId, 6.3)));
    float roughness = lerp(0.46, 0.72, hash21(float2(primitiveId, 11.9)));
    if (abs(roleId - 2.0) < 0.25)
    {
        albedo = lerp(float3(0.38, 0.18, 0.72), float3(0.98, 0.56, 0.92), materialPulse);
        roughness = 0.38;
        emission += albedo * (0.08 + materialPulse * 0.08);
    }
    else if (abs(roleId - 4.0) < 0.25)
    {
        albedo = lerp(float3(0.18, 0.43, 0.50), float3(0.72, 0.86, 0.72), materialPulse);
        roughness = 0.58;
    }

    float3 dielectricF0 = 0.04;
    return emission
        + albedo * bodyLightIrradianceAt(p, normal) / PI
        + studioIrradianceDiffuseRadiance(p, normal, albedo, roughness, dielectricF0)
        + studioPmremSpecularRadiance(p, normal, roughness, dielectricF0);
}

AgentProxyVertexOut D3D12AgentProxyVS(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID)
{
    float2 corners[6] =
    {
        float2(-1.0, -1.0),
        float2(1.0, -1.0),
        float2(1.0, 1.0),
        float2(-1.0, -1.0),
        float2(1.0, 1.0),
        float2(-1.0, 1.0),
    };

    AgentVisual agent = agentVisuals[instanceId];
    float3 forward;
    float3 right;
    float3 up;
    cameraBasis(cameraPosition, gridCenter, forward, right, up);
    float3 delta = agent.centerRadius.xyz - cameraPosition;
    float z = max(dot(delta, forward), 0.0001);
    float2 projected = float2(dot(delta, right), dot(delta, up)) / z * 1.6;
    float clipAspect = resolution.x / max(resolution.y, 1.0);
    float boundRadius = agent.centerRadius.w * 1.58;
    float projectedRadius = boundRadius / z * 1.6 + 0.035;
    float2 clipCenter = float2(projected.x / clipAspect, -projected.y);
    float2 clipRadius = float2(projectedRadius / clipAspect, projectedRadius);

    AgentProxyVertexOut output;
    output.position = float4(clipCenter + corners[vertexId] * clipRadius, 0.0, 1.0);
    output.agentIndex = (float)instanceId;
    return output;
}

SceneOut D3D12AgentProxyPS(AgentProxyVertexOut input)
{
    float2 pixel = input.position.xy;
    float2 uv = float2(pixel.x / max(resolution.x, 1.0), 1.0 - pixel.y / max(resolution.y, 1.0));
    float3 rayDirection = rayDirectionForPixel(pixel, jitterPixels, cameraPosition, gridCenter);

    SolidHit hit;
    hit.hit = false;
    hit.travel = farDistance;
    hit.normal = 0.0;
    hit.fieldId = 0.0;
    hit.primitiveId = -1;
    hit.roleId = 0.0;
    hit.materialId = 0.0;
    hit.stepCount = 0.0;
    hit.lodTier = 0.0;
    hit.costTier = 0.0;
    int agentIndex = clamp((int)round(input.agentIndex), 0, AGENT_VISUAL_COUNT - 1);
    considerPrimitiveHit(cameraPosition, rayDirection, agentIndex, 0.0, farDistance, hit);
    if (!hit.hit)
    {
        discard;
    }

    float3 p = cameraPosition + rayDirection * hit.travel;
    SceneOut output;
    output.colorTravel = float4(shadeBody(uv, hit.travel, p, hit.normal, hit.primitiveId), min(hit.travel, farDistance + 1.0));
    output.metadata = float4(hit.fieldId, hit.normal);
    output.control = float4(hit.materialId, 1.0, hit.stepCount / 72.0, hit.lodTier + hit.costTier * 0.1);
    output.eventColor = float4(0.0, 0.0, 0.0, 0.0);
    output.eventMetadata = float4(FIELD_ID_GRID, farDistance + 1.0, 0.0, 0.0);
    output.depth = saturate(hit.travel / max(farDistance, 0.001));
    return output;
}
