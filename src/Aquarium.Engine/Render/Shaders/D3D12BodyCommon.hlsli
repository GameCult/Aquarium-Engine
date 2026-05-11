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

static const int ROLE_AGENT_COUNT = 7;
static const int AGENT_VISUAL_COUNT = ROLE_AGENT_COUNT + 2;
static const int SELF_OBJECT_INDEX = 0;
static const int CURSOR_OBJECT_INDEX = AGENT_VISUAL_COUNT - 1;
static const float FIELD_ID_SELF = 2.0;
static const float FIELD_ID_GRID = 4.0;
static const float FIELD_ID_CURSOR = 5.0;
static const float FIELD_ID_AGENT_BASE = 10.0;
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

struct BodySurface
{
    float distanceValue;
    float materialId;
    float fieldId;
    float roleId;
    float lodTier;
    float costTier;
    float3 albedo;
    float roughness;
    float3 f0;
    float3 emission;
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

float3 bodyLightSpecularRadiance(float3 p, float3 normal, float roughness, float3 f0, float intensity)
{
    static const float MinimumRoughness = 0.045;

    float3 viewDirection = normalize(cameraPosition - p);
    float ndv = saturate(dot(normal, viewDirection));
    float safeRoughness = max(roughness, MinimumRoughness);
    float alpha = safeRoughness * safeRoughness;
    float alpha2 = alpha * alpha;
    float k = (safeRoughness + 1.0);
    k = (k * k) * 0.125;
    float geometryV = ndv / max(ndv * (1.0 - k) + k, 0.00001);
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
        float3 irradiance = radiance * saturate((radius * radius) / distanceSquared) * intensity;
        float3 halfVector = normalize(lightDirection + viewDirection);
        float ndl = saturate(dot(normal, lightDirection));
        float ndh = saturate(dot(normal, halfVector));
        float vdh = saturate(dot(viewDirection, halfVector));
        float denominator = ndh * ndh * (alpha2 - 1.0) + 1.0;
        float distribution = alpha2 / max(PI * denominator * denominator, 0.00001);
        float geometryL = ndl / max(ndl * (1.0 - k) + k, 0.00001);
        float3 fresnel = f0 + (1.0 - f0) * pow(1.0 - vdh, 5.0);
        float3 specular = (distribution * geometryL * geometryV) * fresnel / max(4.0 * ndl * ndv, 0.00001);
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

float3 shadeBodyPbr(float3 p, float3 normal, BodySurface surface)
{
    return surface.emission
        + surface.albedo * bodyLightIrradianceAt(p, normal) / PI
        + studioIrradianceDiffuseRadiance(p, normal, surface.albedo, surface.roughness, surface.f0)
        + bodyLightSpecularRadiance(p, normal, surface.roughness, surface.f0, 7.0)
        + studioPmremSpecularRadiance(p, normal, surface.roughness, surface.f0);
}

float3 shadeRoleAgentBody(float3 p, float3 normal, int agentIndex)
{
    float roleId = agentVisuals[agentIndex].previousCenterRole.w;
    float materialPulse = agentVisuals[agentIndex].state.y;
    float3 emission = primitiveEmissionRadiance(FIELD_ID_AGENT_BASE + (float)agentIndex);
    float3 albedo = lerp(float3(0.34, 0.42, 0.18), float3(0.70, 0.76, 0.42), hash21(float2(agentIndex, 6.3)));
    float roughness = lerp(0.46, 0.72, hash21(float2(agentIndex, 11.9)));
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

    AgentVisual agent = agentVisuals[BODY_INDEX];
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
    float2 clipCenter = float2(projected.x / clipAspect, projected.y);
    float2 clipRadius = float2(projectedRadius / clipAspect, projectedRadius);

    AgentProxyVertexOut output;
    output.position = float4(clipCenter + corners[vertexId] * clipRadius, 0.0, 1.0);
    output.agentIndex = (float)BODY_INDEX;
    return output;
}
