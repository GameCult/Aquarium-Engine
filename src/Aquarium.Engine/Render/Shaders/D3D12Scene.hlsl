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

Texture2D<float4> gridHeightTexture : register(t0);
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

static const int AGENT_VISUAL_COUNT = 7;
static const int SELF_OBJECT_INDEX = AGENT_VISUAL_COUNT - 2;
static const int CURSOR_OBJECT_INDEX = AGENT_VISUAL_COUNT - 1;
static const int ROLE_AGENT_COUNT = AGENT_VISUAL_COUNT - 2;
static const float SUN_RADIUS = 1.12;
static const float FIELD_ID_SELF = 2.0;
static const float FIELD_ID_GRID = 4.0;
static const float FIELD_ID_CURSOR = 5.0;
static const float FIELD_ID_AGENT_BASE = 10.0;
static const float CURSOR_RADIUS = 0.56;
static const float CURSOR_BOUND_RADIUS = 0.72;
static const float BODY_GRID_CLEARANCE_RADIUS_SCALE = 2.0;
static const float PI = 3.14159265359;
static const float GRID_HEIGHT_TEXEL_COUNT = 128.0;
static const int BODY_LIGHT_COUNT = 8;
static const float STUDIO_PMREM_MAX_LOD = 9.0;
static const float STUDIO_PMREM_SPECULAR_INTENSITY = 0.34;
static const float STUDIO_IRRADIANCE_INTENSITY = 0.74;
static const float GRID_FLAT_REFLECTION_MAX_LOD = 3.0;
static const float BACKGROUND_PMREM_LOD = 3.0;
static const float BACKGROUND_PMREM_CONE = 0.16;
static const float GRID_FLAT_SLOPE_START = 0.018;
static const float GRID_FLAT_SLOPE_END = 0.16;

struct VertexOut
{
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
};

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

struct RayMarchResult
{
    float3 color;
    float travel;
    float fieldId;
    float3 normal;
    float coverage;
    float roleId;
    float materialId;
    float stepCount;
    float lodTier;
    float costTier;
    float eventTravel;
    float eventCoverage;
    float3 eventColor;
};

void cameraBasis(float3 camera, float2 center, out float3 forward, out float3 right, out float3 up);

VertexOut FullscreenTriangleVS(uint vertexId : SV_VertexID)
{
    float2 uv = float2((vertexId << 1) & 2, vertexId & 2);
    VertexOut output;
    output.position = float4(uv * float2(2.0, -2.0) + float2(-1.0, 1.0), 0.0, 1.0);
    output.uv = uv;
    return output;
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

float planetRadius(int index)
{
    return lerp(0.34, 0.62, hash21(float2(index, 19.7)));
}

float terrainHeight(float2 p);

float2 planetAnchorAt(int index, float sampleTime)
{
    float f = (float)index;
    float angle = f * 0.8975979 + sampleTime * (0.08 + 0.011 * f);
    float radius = 4.1 + f * 0.77;
    return float2(cos(angle), sin(angle)) * radius;
}

float3 bodyCenterAtGridHeight(float2 xy, float radius)
{
    return float3(xy, terrainHeight(xy) + radius * BODY_GRID_CLEARANCE_RADIUS_SCALE);
}

float3 planetCenterAt(int index, float sampleTime)
{
    return bodyCenterAtGridHeight(planetAnchorAt(index, sampleTime), planetRadius(index));
}

float3 shadeBody(float2 uv, float travel, float3 p, float3 normal, int primitiveId);

float2 gridLocal(float2 p)
{
    return (p - gridCenter) / max(gridRadius, 0.001);
}

float2 gridUv(float2 p)
{
    return gridLocal(p) * 0.5 + 0.5;
}

float terrainHeight(float2 p)
{
    return gridHeightTexture.SampleLevel(gridSampler, saturate(gridUv(p)), 0.0).r;
}

float2 terrainGradient(float2 p)
{
    float2 uv = saturate(gridUv(p));
    float2 texel = 1.0 / GRID_HEIGHT_TEXEL_COUNT;
    float texelWorld = max((gridRadius * 2.0) / GRID_HEIGHT_TEXEL_COUNT, 0.001);

    float hLeft = gridHeightTexture.SampleLevel(gridSampler, uv - float2(texel.x, 0.0), 0.0).r;
    float hRight = gridHeightTexture.SampleLevel(gridSampler, uv + float2(texel.x, 0.0), 0.0).r;
    float hDown = gridHeightTexture.SampleLevel(gridSampler, uv - float2(0.0, texel.y), 0.0).r;
    float hUp = gridHeightTexture.SampleLevel(gridSampler, uv + float2(0.0, texel.y), 0.0).r;

    return float2(hRight - hLeft, hUp - hDown) / (texelWorld * 2.0);
}

float3 terrainNormal(float3 p)
{
    float2 gradient = terrainGradient(p.xy);
    return normalize(float3(-gradient.x, -gradient.y, 1.0));
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

float cursorLocatorProfileRadius(float z)
{
    static const float ContactZ = -1.0;
    static const float TipZ = 1.15;
    static const float RadiusScale = 0.78;

    float u = saturate((z - ContactZ) / (TipZ - ContactZ));
    float x = u * 2.0 - 1.0;
    float halfTerm = (1.0 - x) * 0.5;
    float teardropWeight = halfTerm * halfTerm * halfTerm;
    float baseRadius = RadiusScale * sqrt(saturate(1.0 - x * x)) * teardropWeight;
    float rippleEnvelope = smoothstep(0.08, 0.22, u) * (1.0 - smoothstep(0.76, 0.94, u));
    float rippleWave = sin(z * 28.0 - timeSeconds * 4.0);
    float rippleRadius = min(0.014, baseRadius * 0.07) * rippleWave * rippleEnvelope;
    return max(baseRadius + rippleRadius, 0.0);
}

float cursorLocatorSdf(float3 p)
{
    static const float ContactZ = -1.0;
    static const float TipZ = 1.15;

    float3 center = bodyCenterAtGridHeight(cursorWorlds.xy, CURSOR_RADIUS);
    float3 local = (p - center) / CURSOR_RADIUS;
    float2 samplePoint = float2(length(local.xy), local.z);
    float profileRadius = cursorLocatorProfileRadius(samplePoint.y);
    float radialDistance = samplePoint.x - profileRadius;
    float topDistance = samplePoint.y - TipZ;
    float bottomDistance = ContactZ - samplePoint.y;
    float boundedDistance = max(radialDistance, max(topDistance, bottomDistance));
    return boundedDistance * CURSOR_RADIUS;
}

float3 cursorLocatorNormal(float3 p)
{
    float epsilon = 0.006;
    float dx = cursorLocatorSdf(p + float3(epsilon, 0.0, 0.0)) - cursorLocatorSdf(p - float3(epsilon, 0.0, 0.0));
    float dy = cursorLocatorSdf(p + float3(0.0, epsilon, 0.0)) - cursorLocatorSdf(p - float3(0.0, epsilon, 0.0));
    float dz = cursorLocatorSdf(p + float3(0.0, 0.0, epsilon)) - cursorLocatorSdf(p - float3(0.0, 0.0, epsilon));
    return normalize(float3(dx, dy, dz));
}

struct AgentSurface
{
    float distanceValue;
    float materialId;
    float costTier;
};

float sdSphere(float3 p, float radius)
{
    return length(p) - radius;
}

float sdTorus(float3 p, float2 radius)
{
    float2 q = float2(length(p.xy) - radius.x, p.z);
    return length(q) - radius.y;
}

float sdSuperellipsoid(float3 p, float3 scale, float exponent)
{
    float3 q = abs(p) / scale;
    float value = pow(pow(q.x, exponent) + pow(q.y, exponent) + pow(q.z, exponent), 1.0 / exponent);
    return (value - 1.0) * min(scale.x, min(scale.y, scale.z));
}

float sdEllipsoid(float3 p, float3 radius)
{
    float3 safeRadius = max(radius, 0.001);
    float k0 = length(p / safeRadius);
    float k1 = length(p / (safeRadius * safeRadius));
    return k0 * (k0 - 1.0) / max(k1, 0.001);
}

float smoothUnion(float a, float b, float radius)
{
    float h = saturate(0.5 + 0.5 * (b - a) / radius);
    return lerp(b, a, h) - radius * h * (1.0 - h);
}

float imaginationPetalSdf(float3 local, float angle, float activity, float heartbeat)
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

AgentSurface agentImaginationSdf(float3 local, AgentVisual agent)
{
    float activity = agent.state.x;
    float heartbeat = agent.state.y;
    float core = sdSuperellipsoid(local, float3(0.30 + activity * 0.04, 0.30 + activity * 0.04, 0.42), 1.18);
    float phase = timeSeconds * 0.42;
    float petal0 = imaginationPetalSdf(local, phase, activity, heartbeat);
    float petal1 = imaginationPetalSdf(local, phase + 1.2566371, activity, heartbeat);
    float petal2 = imaginationPetalSdf(local, phase + 2.5132741, activity, heartbeat);
    float petal3 = imaginationPetalSdf(local, phase + 3.7699112, activity, heartbeat);
    float petal4 = imaginationPetalSdf(local, phase + 5.0265482, activity, heartbeat);
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
        surface.distanceValue = cursorLocatorSdf(p);
        surface.materialId = 5.0;
        surface.costTier = 1.0;
        return surface;
    }
    else if (abs(roleId - 2.0) < 0.25)
    {
        surface = agentImaginationSdf(local, agent);
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

bool traceCursorLocator(float3 origin, float3 direction, float intervalStart, float intervalEnd, out float travel, out float3 normal)
{
    float sphereTravel;
    float3 center = bodyCenterAtGridHeight(cursorWorlds.xy, CURSOR_RADIUS);
    if (!traceSphere(origin, direction, center, CURSOR_BOUND_RADIUS, sphereTravel))
    {
        travel = farDistance + 1.0;
        normal = 0.0;
        return false;
    }

    float3 oc = origin - center;
    float b = dot(oc, direction);
    float c = dot(oc, oc) - CURSOR_BOUND_RADIUS * CURSOR_BOUND_RADIUS;
    float h = sqrt(max(b * b - c, 0.0));
    float startTravel = max(max(-b - h, intervalStart), 0.0);
    float endTravel = min(-b + h, intervalEnd);
    travel = startTravel;
    normal = 0.0;
    [loop]
    for (int stepIndex = 0; stepIndex < 48; stepIndex++)
    {
        if (travel > endTravel)
        {
            return false;
        }

        float3 p = origin + direction * travel;
        float distanceValue = cursorLocatorSdf(p);
        if (abs(distanceValue) < max(0.0025, travel * 0.00025))
        {
            normal = cursorLocatorNormal(p);
            return true;
        }

        travel += max(abs(distanceValue) * 0.36, 0.0025);
    }

    return false;
}

float3 primitiveCenterAt(int primitiveId, float sampleTime)
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

    if (primitiveId < SELF_OBJECT_INDEX)
    {
        float agentTravel;
        float3 agentNormal;
        float materialId;
        float stepCount;
        float costTier;
        if (traceAgentVisual(origin, direction, primitiveId, intervalStart, min(intervalEnd, nearest.travel), agentTravel, agentNormal, materialId, stepCount, costTier))
        {
            nearest.hit = true;
            nearest.travel = agentTravel;
            nearest.normal = agentNormal;
            nearest.fieldId = primitiveFieldId(primitiveId);
            nearest.primitiveId = primitiveId;
            nearest.roleId = agentVisuals[primitiveId].previousCenterRole.w;
            nearest.materialId = materialId;
            nearest.stepCount = stepCount;
            nearest.lodTier = agentVisuals[primitiveId].lodIndexFlags.x;
            nearest.costTier = costTier;
        }

        return;
    }

    if (primitiveId == CURSOR_OBJECT_INDEX)
    {
        float hitTravel;
        float3 hitNormal;
        if (traceCursorLocator(origin, direction, intervalStart, min(intervalEnd, nearest.travel), hitTravel, hitNormal))
        {
            nearest.hit = true;
            nearest.travel = hitTravel;
            nearest.normal = hitNormal;
            nearest.fieldId = FIELD_ID_CURSOR;
            nearest.primitiveId = primitiveId;
            nearest.roleId = 0.0;
            nearest.materialId = 5.0;
            nearest.stepCount = 1.0;
            nearest.lodTier = 0.0;
            nearest.costTier = 1.0;
        }

        return;
    }

    float radius = primitiveRadius(primitiveId);
    float3 center = primitiveCenterAt(primitiveId, timeSeconds);
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

void considerAllPrimitiveHits(float3 origin, float3 direction, float intervalStart, float intervalEnd, inout SolidHit nearest)
{
    [unroll]
    for (int i = 0; i < AGENT_VISUAL_COUNT; i++)
    {
        considerPrimitiveHit(origin, direction, i, intervalStart, intervalEnd, nearest);
    }
}

float gridSurfaceDistanceAt(float3 origin, float3 direction, float travel)
{
    float3 p = origin + direction * travel;
    return p.z - terrainHeight(p.xy);
}

bool traceGridSurfaceDirect(float3 origin, float3 direction, float intervalStart, float intervalEnd, out float3 hitPosition, out float travel)
{
    travel = max(intervalStart, 0.0);
    float previousTravel = travel;
    hitPosition = origin + direction * travel;
    float previousGap = hitPosition.z - terrainHeight(hitPosition.xy);
    float radius = max(gridRadius, 0.001);

    [loop]
    for (int stepIndex = 0; stepIndex < 96; stepIndex++)
    {
        hitPosition = origin + direction * travel;
        float2 local = (hitPosition.xy - gridCenter) / radius;
        if (length(local) > 1.08 && hitPosition.z < 4.0)
        {
            return false;
        }

        float gap = hitPosition.z - terrainHeight(hitPosition.xy);
        float hitEpsilon = max(0.002, travel * 0.00035);
        if (length(local) <= 1.0 && (abs(gap) <= hitEpsilon || (previousGap > 0.0 && gap <= 0.0)))
        {
            float alpha = previousGap / max(previousGap - gap, 0.0001);
            travel = lerp(previousTravel, travel, saturate(alpha));
            hitPosition = origin + direction * travel;
            return travel > intervalStart && travel < intervalEnd && travel < farDistance;
        }

        float2 slope = terrainGradient(hitPosition.xy);
        float terrainRate = abs(direction.z - dot(slope, direction.xy));
        float terrainStep = gap > 0.0 ? gap / max(terrainRate, 0.22) : 0.026;
        terrainStep = min(terrainStep * 0.62, max(gridRadius * 0.08, 0.026));
        previousTravel = travel;
        previousGap = gap;
        travel += max(terrainStep, 0.026);
        if (travel > intervalEnd || travel > farDistance)
        {
            return false;
        }
    }

    return false;
}

float3 studioPmremDirection(float3 worldDirection)
{
    return normalize(float3(worldDirection.x, worldDirection.z, worldDirection.y));
}

void directionBasis(float3 direction, out float3 tangent, out float3 bitangent)
{
    float3 up = abs(direction.z) > 0.94 ? float3(0.0, 1.0, 0.0) : float3(0.0, 0.0, 1.0);
    tangent = normalize(cross(up, direction));
    bitangent = cross(direction, tangent);
}

float3 studioPmremSample(float3 worldDirection, float lod)
{
    return studioPmremTexture.SampleLevel(gridSampler, studioPmremDirection(worldDirection), lod).rgb;
}

float3 studioPmremConeSample(float3 worldDirection, float lod, float cone)
{
    float3 direction = normalize(worldDirection);
    float3 tangent;
    float3 bitangent;
    directionBasis(direction, tangent, bitangent);

    float3 sum = studioPmremSample(direction, lod) * 2.0;
    sum += studioPmremSample(normalize(direction + tangent * cone), lod);
    sum += studioPmremSample(normalize(direction - tangent * cone), lod);
    sum += studioPmremSample(normalize(direction + bitangent * cone), lod);
    sum += studioPmremSample(normalize(direction - bitangent * cone), lod);
    sum += studioPmremSample(normalize(direction + (tangent + bitangent) * (cone * 0.7071)), lod);
    sum += studioPmremSample(normalize(direction + (-tangent + bitangent) * (cone * 0.7071)), lod);
    return sum * 0.125;
}

float3 gridMirrorRadiance(float3 p, float3 direction, out float3 normal)
{
    float2 gradient = terrainGradient(p.xy);
    normal = normalize(float3(-gradient.x, -gradient.y, 1.0));
    float3 reflectionDirection = reflect(direction, normal);
    float flatness = 1.0 - smoothstep(GRID_FLAT_SLOPE_START, GRID_FLAT_SLOPE_END, length(gradient));
    float lod = flatness * GRID_FLAT_REFLECTION_MAX_LOD;
    return studioPmremConeSample(reflectionDirection, lod, flatness * 0.055);
}

float3 backgroundRadiance(float3 direction)
{
    return studioPmremConeSample(direction, BACKGROUND_PMREM_LOD, BACKGROUND_PMREM_CONE);
}

RayMarchResult traverseRay(float2 uv, float2 screenUv, float3 origin, float3 direction)
{
    RayMarchResult result;
    result.color = backgroundRadiance(direction);
    result.travel = farDistance + 1.0;
    result.fieldId = 0.0;
    result.normal = 0.0;
    result.coverage = 0.0;
    result.roleId = 0.0;
    result.materialId = 0.0;
    result.stepCount = 0.0;
    result.lodTier = 0.0;
    result.costTier = 0.0;
    result.eventTravel = farDistance + 1.0;
    result.eventCoverage = 0.0;
    result.eventColor = 0.0;

    float3 gridPosition;
    float gridTravel;
    bool gridHit = traceGridSurfaceDirect(origin, direction, 0.0, farDistance, gridPosition, gridTravel);
    if (gridHit)
    {
        float3 gridNormal;
        result.color = gridMirrorRadiance(gridPosition, direction, gridNormal);
        result.travel = gridTravel;
        result.fieldId = FIELD_ID_GRID;
        result.normal = gridNormal;
        result.coverage = 1.0;
        result.roleId = 0.0;
        result.materialId = 0.0;
        result.stepCount = 0.0;
        result.lodTier = 0.0;
        result.costTier = 0.0;
        return result;
    }

    return result;
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
    static const float CursorRoughness = 0.16;

    float3 viewDirection = normalize(cameraPosition - p);
    float ndv = saturate(dot(normal, viewDirection));
    float roughness = max(CursorRoughness, MinimumRoughness);
    float alpha = roughness * roughness;
    float alpha2 = alpha * alpha;
    float k = (roughness + 1.0);
    k = (k * k) * 0.125;
    float geometryV = ndv / max(ndv * (1.0 - k) + k, 0.00001);
    float3 f0 = float3(0.95, 0.62, 0.26);
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
        static const float CursorRoughness = 0.16;
        float3 cursorF0 = float3(0.95, 0.62, 0.26);
        return emission
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

SceneOut D3D12ScenePS(VertexOut input)
{
    float2 screenUv = float2(input.uv.x, 1.0 - input.uv.y);
    float2 pixel = screenUv * resolution;
    float3 rayDirection = rayDirectionForPixel(pixel, jitterPixels, cameraPosition, gridCenter);

    RayMarchResult result = traverseRay(input.uv, screenUv, cameraPosition, rayDirection);

    SceneOut output;
    output.colorTravel = float4(result.color, min(result.travel, farDistance + 1.0));
    output.metadata = float4(result.fieldId, result.normal);
    output.control = float4(result.materialId, result.coverage, result.stepCount / 72.0, result.lodTier + result.costTier * 0.1);
    output.eventColor = float4(result.eventColor, result.eventCoverage);
    output.eventMetadata = float4(FIELD_ID_GRID, result.eventTravel, result.eventCoverage, 0.0);
    output.depth = saturate(result.travel / max(farDistance, 0.001));
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
