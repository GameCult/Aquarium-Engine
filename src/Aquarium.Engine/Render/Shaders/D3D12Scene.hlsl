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
SamplerState gridSampler : register(s0);

struct BodyLight
{
    float4 centerRadius;
    float4 radianceFieldId;
};

StructuredBuffer<BodyLight> bodyLights : register(t12);

static const int PLANET_COUNT = 5;
static const float SUN_RADIUS = 1.12;
static const float FIELD_ID_SELF = 2.0;
static const float FIELD_ID_GRID = 4.0;
static const float FIELD_ID_CURSOR = 5.0;
static const float FIELD_ID_PLANET_BASE = 10.0;
static const int CURSOR_PRIMITIVE_ID = PLANET_COUNT + 1;
static const float CURSOR_RADIUS = 0.56;
static const float CURSOR_BOUND_RADIUS = 0.72;
static const float PI = 3.14159265359;
static const float GRID_HEIGHT_TEXEL_COUNT = 128.0;
static const float TERRAIN_ISOLINE_SPACING = 0.12;
static const float GRID_LINE_WORLD_CELL = 2.0;
static const float GRID_MAJOR_LINE_WORLD_CELL = GRID_LINE_WORLD_CELL * 5.0;
static const float GRID_LINE_PIXEL_WIDTH = 0.46;
static const float GRID_MAJOR_LINE_PIXEL_WIDTH = 0.82;
static const float GRID_LINE_PIXEL_FADE = 0.95;
static const float TERRAIN_ISOLINE_PIXEL_WIDTH = 0.54;
static const float TERRAIN_FIELD_LINE_PIXEL_WIDTH = 0.38;
static const float3 GRID_COLOR = float3(0.30, 0.90, 0.82);
static const float GRID_ALPHA_SCALE = 0.56;
static const int BODY_LIGHT_COUNT = 8;

struct VertexOut
{
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
};

struct SceneOut
{
    float4 colorTravel : SV_Target0;
    float4 metadata : SV_Target1;
    float4 control : SV_Target2;
    float4 eventColor : SV_Target3;
    float4 eventMetadata : SV_Target4;
};

struct SolidHit
{
    bool hit;
    float travel;
    float3 normal;
    float fieldId;
    int primitiveId;
};

struct RayMarchResult
{
    float3 color;
    float travel;
    float fieldId;
    float3 normal;
    float coverage;
    float eventTravel;
    float eventCoverage;
    float3 eventColor;
};

VertexOut FullscreenTriangleVS(uint vertexId : SV_VertexID)
{
    float2 uv = float2((vertexId << 1) & 2, vertexId & 2);
    VertexOut output;
    output.position = float4(uv * float2(2.0, -2.0) + float2(-1.0, 1.0), 0.0, 1.0);
    output.uv = uv;
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

float3 planetCenterAt(int index, float sampleTime)
{
    float f = (float)index;
    float angle = f * 0.8975979 + sampleTime * (0.08 + 0.011 * f);
    float radius = 4.1 + f * 0.77;
    float2 xy = float2(cos(angle), sin(angle)) * radius;
    return float3(xy, 1.15 + planetRadius(index) * 0.72);
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

float periodicLineMask(float coordinate, float pixelWidth, float pixelFade)
{
    float distanceToLine = min(frac(coordinate), 1.0 - frac(coordinate));
    float coordinatePerPixel = max(fwidth(coordinate), 0.00001);
    float distancePixels = distanceToLine / coordinatePerPixel;
    return 1.0 - smoothstep(pixelWidth, pixelWidth + pixelFade, distancePixels);
}

float gridLine(float2 p)
{
    float2 minorDomain = p / GRID_LINE_WORLD_CELL;
    float2 majorDomain = p / GRID_MAJOR_LINE_WORLD_CELL;

    float minor = max(
        periodicLineMask(minorDomain.x, GRID_LINE_PIXEL_WIDTH, GRID_LINE_PIXEL_FADE),
        periodicLineMask(minorDomain.y, GRID_LINE_PIXEL_WIDTH, GRID_LINE_PIXEL_FADE));
    float major = max(
        periodicLineMask(majorDomain.x, GRID_MAJOR_LINE_PIXEL_WIDTH, GRID_LINE_PIXEL_FADE),
        periodicLineMask(majorDomain.y, GRID_MAJOR_LINE_PIXEL_WIDTH, GRID_LINE_PIXEL_FADE));

    return saturate(minor * 0.58 + major);
}

float isolineMask(float height)
{
    float contourDomain = height / TERRAIN_ISOLINE_SPACING;
    float contour = periodicLineMask(contourDomain, TERRAIN_ISOLINE_PIXEL_WIDTH, GRID_LINE_PIXEL_FADE);
    float contourDerivative = max(fwidth(contourDomain), 0.00001);
    float slopeFade = smoothstep(0.025, 0.25, contourDerivative);
    return contour * slopeFade;
}

float fieldLineMask(float2 gradient)
{
    float slope = length(gradient);
    if (slope < 0.0001)
    {
        return 0.0;
    }

    float angleDomain = (atan2(gradient.y, gradient.x) / PI + 1.0) * 6.0;
    float angleLine = periodicLineMask(angleDomain, TERRAIN_FIELD_LINE_PIXEL_WIDTH, GRID_LINE_PIXEL_FADE);
    float slopeStrength = smoothstep(0.015, 0.16, slope);
    return angleLine * slopeStrength;
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

    float3 center = float3(cursorWorlds.xy, CURSOR_RADIUS);
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

bool traceCursorLocator(float3 origin, float3 direction, float intervalStart, float intervalEnd, out float travel, out float3 normal)
{
    float sphereTravel;
    float3 center = float3(cursorWorlds.xy, CURSOR_RADIUS);
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
    if (primitiveId == 0)
    {
        return float3(0.0, 0.0, 2.2);
    }

    if (primitiveId == CURSOR_PRIMITIVE_ID)
    {
        return float3(cursorWorlds.xy, CURSOR_RADIUS);
    }

    return planetCenterAt(primitiveId - 1, sampleTime);
}

float primitiveRadius(int primitiveId)
{
    if (primitiveId == 0)
    {
        return SUN_RADIUS;
    }

    if (primitiveId == CURSOR_PRIMITIVE_ID)
    {
        return CURSOR_BOUND_RADIUS;
    }

    return planetRadius(primitiveId - 1);
}

float primitiveFieldId(int primitiveId)
{
    if (primitiveId == CURSOR_PRIMITIVE_ID)
    {
        return FIELD_ID_CURSOR;
    }

    return primitiveId == 0 ? FIELD_ID_SELF : FIELD_ID_PLANET_BASE + (float)(primitiveId - 1);
}

void considerPrimitiveHit(float3 origin, float3 direction, int primitiveId, float intervalStart, float intervalEnd, inout SolidHit nearest)
{
    if (primitiveId < 0 || primitiveId == CURSOR_PRIMITIVE_ID)
    {
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
    }
}

void considerAllPrimitiveHits(float3 origin, float3 direction, float intervalStart, float intervalEnd, inout SolidHit nearest)
{
    considerPrimitiveHit(origin, direction, 0, intervalStart, intervalEnd, nearest);

    [unroll]
    for (int i = 0; i < PLANET_COUNT; i++)
    {
        considerPrimitiveHit(origin, direction, i + 1, intervalStart, intervalEnd, nearest);
    }

    float hitTravel;
    float3 hitNormal;
    if (traceCursorLocator(origin, direction, intervalStart, min(intervalEnd, nearest.travel), hitTravel, hitNormal))
    {
        nearest.hit = true;
        nearest.travel = hitTravel;
        nearest.normal = hitNormal;
        nearest.fieldId = FIELD_ID_CURSOR;
        nearest.primitiveId = CURSOR_PRIMITIVE_ID;
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

float3 gridEventColor(float3 p, out float alpha)
{
    float height = terrainHeight(p.xy);
    float2 gradient = terrainGradient(p.xy);
    float gridAmount = gridLine(p.xy);
    float contour = isolineMask(height);
    float fieldLine = fieldLineMask(gradient);
    float support = saturate(gridAmount * 0.58 + contour * 0.22 + fieldLine * 0.16);
    float3 color = GRID_COLOR * gridAmount * 1.05;
    color += float3(0.98, 1.0, 0.78) * contour * 0.34;
    color += float3(0.36, 0.92, 1.0) * fieldLine * 0.22;
    alpha = saturate(support * GRID_ALPHA_SCALE);
    return color;
}

RayMarchResult traverseRay(float2 uv, float2 screenUv, float3 origin, float3 direction)
{
    RayMarchResult result;
    result.color = float3(0.0, 0.0, 0.0);
    result.travel = farDistance + 1.0;
    result.fieldId = 0.0;
    result.normal = 0.0;
    result.coverage = 0.0;
    result.eventTravel = farDistance + 1.0;
    result.eventCoverage = 0.0;
    result.eventColor = 0.0;

    SolidHit nearestSolid;
    nearestSolid.hit = false;
    nearestSolid.travel = farDistance;
    nearestSolid.normal = 0.0;
    nearestSolid.fieldId = 0.0;
    nearestSolid.primitiveId = -1;
    considerAllPrimitiveHits(origin, direction, 0.0, farDistance, nearestSolid);

    float stopTravel = nearestSolid.hit ? nearestSolid.travel : farDistance;
    float3 gridPosition;
    float gridTravel;
    bool gridHit = traceGridSurfaceDirect(origin, direction, 0.0, stopTravel, gridPosition, gridTravel);
    float gridAlpha = 0.0;
    float3 gridColor = 0.0;
    if (gridHit)
    {
        gridColor = gridEventColor(gridPosition, gridAlpha);
        gridHit = gridAlpha > 0.001;
    }

    if (gridHit)
    {
        result.eventTravel = gridTravel;
        result.eventCoverage = gridAlpha;
        result.eventColor = gridColor * gridAlpha;
    }

    if (nearestSolid.hit)
    {
        float3 p = origin + direction * nearestSolid.travel;
        float3 bodyColor = shadeBody(uv, nearestSolid.travel, p, nearestSolid.normal, nearestSolid.primitiveId);
        result.color = bodyColor;
        result.travel = nearestSolid.travel;
        result.fieldId = nearestSolid.fieldId;
        result.normal = nearestSolid.normal;
        result.coverage = 1.0;
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

float3 shadeBody(float2 uv, float travel, float3 p, float3 normal, int primitiveId)
{
    float fieldId = primitiveFieldId(primitiveId);
    float3 emission = primitiveEmissionRadiance(fieldId);
    if (primitiveId == 0)
    {
        return emission;
    }

    if (primitiveId == CURSOR_PRIMITIVE_ID)
    {
        return emission + cursorSpecularBodyLightRadiance(p, normal);
    }

    float hue = hash21(float2(primitiveId, 6.3));
    float3 albedo = lerp(float3(0.34, 0.42, 0.18), float3(0.70, 0.76, 0.42), hue);
    return emission + albedo * bodyLightIrradianceAt(p, normal) / PI;
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
    output.control = float4(0.0, result.coverage, 0.0, 0.0);
    output.eventColor = float4(result.eventColor, result.eventCoverage);
    output.eventMetadata = float4(FIELD_ID_GRID, result.eventTravel, result.eventCoverage, 0.0);
    return output;
}
