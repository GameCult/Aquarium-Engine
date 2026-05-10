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
    float mediumCompositeIntensity;
    float mediumDebugStep;
    float4 cursorWorlds;
};

Texture2D<float4> gridHeightTexture : register(t0);
StructuredBuffer<int4> froxelPrimitiveIds : register(t1);
Texture2D<float4> mediumVolumeTexture : register(t13);
Texture2D<float4> mediumTransportTexture : register(t14);
SamplerState gridSampler : register(s0);

static const int PLANET_COUNT = 5;
static const float SUN_RADIUS = 1.12;
static const float FIELD_ID_SELF = 2.0;
static const float FIELD_ID_MEDIUM = 3.0;
static const float FIELD_ID_GRID = 4.0;
static const float FIELD_ID_CURSOR = 5.0;
static const float FIELD_ID_PLANET_BASE = 10.0;
static const int CURSOR_PRIMITIVE_ID = PLANET_COUNT + 1;
static const float CURSOR_RADIUS = 0.56;
static const float CURSOR_BOUND_RADIUS = 0.74;
static const int MEDIUM_FROXEL_ATLAS_COLUMNS = 8;
static const int MEDIUM_FROXEL_ATLAS_ROWS = 4;
static const int MEDIUM_FROXEL_DOWNSCALE = 8;
static const int MEDIUM_FROXEL_SLICE_COUNT = MEDIUM_FROXEL_ATLAS_COLUMNS * MEDIUM_FROXEL_ATLAS_ROWS;
static const int VIEW_FROXEL_PRIMITIVE_SLOT_COUNT = 2;
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
    float4 mediumPacket : SV_Target3;
    float4 eventColor : SV_Target4;
    float4 eventMetadata : SV_Target5;
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
    float mediumOpacity;
    float densityMean;
    float mediumTravel;
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

float3 shadeBody(float3 p, float3 normal, int primitiveId);

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

float cursorTopProfileRadius(float t)
{
    float x = saturate(t);
    return x * x * (3.0 - 2.0 * x);
}

float cursorBottomProfileRadius(float t)
{
    return 1.0 - sqrt(saturate(t));
}

float cursorTopSdf(float3 p)
{
    static const float BottomHeight = 0.625;
    static const float TopHeight = 1.25;
    static const float WaistRadius = 0.78;

    float3 center = float3(cursorWorlds.xy, CURSOR_RADIUS);
    float3 local = (p - center) / CURSOR_RADIUS;
    float2 samplePoint = float2(length(local.xy), local.z);
    float topT = saturate((TopHeight - samplePoint.y) / TopHeight);
    float bottomT = saturate(-samplePoint.y / BottomHeight);
    float profileRadius = samplePoint.y >= 0.0
        ? WaistRadius * cursorTopProfileRadius(topT)
        : WaistRadius * cursorBottomProfileRadius(bottomT);
    float radialDistance = samplePoint.x - profileRadius;
    float topDistance = samplePoint.y - TopHeight;
    float bottomDistance = -BottomHeight - samplePoint.y;
    float boundedDistance = max(radialDistance, max(topDistance, bottomDistance));
    return boundedDistance * CURSOR_RADIUS;
}

float3 cursorTopNormal(float3 p)
{
    float epsilon = 0.006;
    float dx = cursorTopSdf(p + float3(epsilon, 0.0, 0.0)) - cursorTopSdf(p - float3(epsilon, 0.0, 0.0));
    float dy = cursorTopSdf(p + float3(0.0, epsilon, 0.0)) - cursorTopSdf(p - float3(0.0, epsilon, 0.0));
    float dz = cursorTopSdf(p + float3(0.0, 0.0, epsilon)) - cursorTopSdf(p - float3(0.0, 0.0, epsilon));
    return normalize(float3(dx, dy, dz));
}

bool traceCursorTop(float3 origin, float3 direction, float intervalStart, float intervalEnd, out float travel, out float3 normal)
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
        float distanceValue = cursorTopSdf(p);
        if (abs(distanceValue) < max(0.0025, travel * 0.00025))
        {
            normal = cursorTopNormal(p);
            return true;
        }

        travel += max(abs(distanceValue) * 0.72, 0.004);
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

void considerFroxelPrimitiveHits(float3 origin, float3 direction, int froxelIndex, float intervalStart, float intervalEnd, inout SolidHit nearest)
{
    if (froxelIndex < 0)
    {
        return;
    }

    [unroll]
    for (int slot = 0; slot < VIEW_FROXEL_PRIMITIVE_SLOT_COUNT; slot++)
    {
        int4 ids = froxelPrimitiveIds[froxelIndex * VIEW_FROXEL_PRIMITIVE_SLOT_COUNT + slot];
        bool hasCursor =
            ids.x == CURSOR_PRIMITIVE_ID ||
            ids.y == CURSOR_PRIMITIVE_ID ||
            ids.z == CURSOR_PRIMITIVE_ID ||
            ids.w == CURSOR_PRIMITIVE_ID;
        considerPrimitiveHit(origin, direction, ids.x, intervalStart, intervalEnd, nearest);
        considerPrimitiveHit(origin, direction, ids.y, intervalStart, intervalEnd, nearest);
        considerPrimitiveHit(origin, direction, ids.z, intervalStart, intervalEnd, nearest);
        considerPrimitiveHit(origin, direction, ids.w, intervalStart, intervalEnd, nearest);
        if (hasCursor)
        {
            float hitTravel;
            float3 hitNormal;
            if (traceCursorTop(origin, direction, intervalStart, min(intervalEnd, nearest.travel), hitTravel, hitNormal))
            {
                nearest.hit = true;
                nearest.travel = hitTravel;
                nearest.normal = hitNormal;
                nearest.fieldId = FIELD_ID_CURSOR;
                nearest.primitiveId = CURSOR_PRIMITIVE_ID;
            }
        }
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

float2 mediumAtlasUv(float2 uv, int sliceIndex)
{
    int tileX = sliceIndex % MEDIUM_FROXEL_ATLAS_COLUMNS;
    int tileY = sliceIndex / MEDIUM_FROXEL_ATLAS_COLUMNS;
    return (float2(tileX, tileY) + uv) / float2(MEDIUM_FROXEL_ATLAS_COLUMNS, MEDIUM_FROXEL_ATLAS_ROWS);
}

float mediumSliceTravel(int sliceIndex)
{
    float t = ((float)sliceIndex + 0.5) / (float)MEDIUM_FROXEL_SLICE_COUNT;
    return t * farDistance;
}

float mediumSliceStartTravel(int sliceIndex)
{
    return ((float)sliceIndex / (float)MEDIUM_FROXEL_SLICE_COUNT) * farDistance;
}

float mediumSliceEndTravel(int sliceIndex)
{
    return (((float)sliceIndex + 1.0) / (float)MEDIUM_FROXEL_SLICE_COUNT) * farDistance;
}

int viewFroxelIndexForUvSlice(float2 uv, int sliceIndex)
{
    int width = max((int)(resolution.x / (float)MEDIUM_FROXEL_DOWNSCALE), 1);
    int height = max((int)(resolution.y / (float)MEDIUM_FROXEL_DOWNSCALE), 1);
    int2 cell = clamp((int2)floor(uv * float2(width, height)), int2(0, 0), int2(width - 1, height - 1));
    return cell.x + cell.y * width + sliceIndex * width * height;
}

SolidHit nearestViewFroxelSolidHit(float2 uv, float3 origin, float3 direction, int sliceIndex)
{
    float intervalStart = mediumSliceStartTravel(sliceIndex);
    float intervalEnd = min(mediumSliceEndTravel(sliceIndex), farDistance);
    SolidHit nearest;
    nearest.hit = false;
    nearest.travel = intervalEnd;
    nearest.normal = 0.0;
    nearest.fieldId = 0.0;
    nearest.primitiveId = -1;
    considerFroxelPrimitiveHits(origin, direction, viewFroxelIndexForUvSlice(uv, sliceIndex), intervalStart, intervalEnd, nearest);
    return nearest;
}

void integrateMediumSlice(
    float2 uv,
    int sliceIndex,
    float intervalFraction,
    inout float transmittance,
    inout float3 inScattering,
    inout float densityMean,
    inout float densityTravelSum,
    inout float densitySum)
{
    float2 atlasUv = mediumAtlasUv(uv, sliceIndex);
    float4 diagnostic = mediumVolumeTexture.SampleLevel(gridSampler, atlasUv, 0.0);
    float4 transport = mediumTransportTexture.SampleLevel(gridSampler, atlasUv, 0.0);
    float sliceTravel = mediumSliceTravel(sliceIndex);
    float fraction = saturate(intervalFraction);
    float fullSliceTransmittance = saturate(transport.a);
    float sliceExtinction = -log(max(fullSliceTransmittance, 0.0001));
    float partialTransmittance = exp(-sliceExtinction * fraction);
    float scatterDenominator = max(1.0 - fullSliceTransmittance, 0.0001);
    float scatterFraction = (1.0 - partialTransmittance) / scatterDenominator;
    float densityContribution = diagnostic.x * fraction;

    densityMean += densityContribution;
    densityTravelSum += densityContribution * sliceTravel;
    densitySum += densityContribution;
    inScattering += transmittance * transport.rgb * scatterFraction;
    transmittance *= saturate(partialTransmittance);
}

void integrateMediumRange(
    float2 uv,
    float rangeStart,
    float rangeEnd,
    inout float transmittance,
    inout float3 inScattering,
    inout float densityMean,
    inout float densityTravelSum,
    inout float densitySum)
{
    if (rangeEnd <= rangeStart)
    {
        return;
    }

    [loop]
    for (int sliceIndex = 0; sliceIndex < MEDIUM_FROXEL_SLICE_COUNT; sliceIndex++)
    {
        float sliceStart = mediumSliceStartTravel(sliceIndex);
        float sliceEnd = mediumSliceEndTravel(sliceIndex);
        float overlapStart = max(rangeStart, sliceStart);
        float overlapEnd = min(rangeEnd, sliceEnd);
        if (overlapEnd <= overlapStart)
        {
            continue;
        }

        float fraction = saturate((overlapEnd - overlapStart) / max(sliceEnd - sliceStart, 0.0001));
        integrateMediumSlice(
            uv,
            sliceIndex,
            fraction,
            transmittance,
            inScattering,
            densityMean,
            densityTravelSum,
            densitySum);
    }
}

RayMarchResult traverseRay(float2 uv, float2 screenUv, float3 origin, float3 direction)
{
    RayMarchResult result;
    result.color = float3(0.001, 0.003, 0.004);
    result.travel = farDistance + 1.0;
    result.fieldId = 0.0;
    result.normal = 0.0;
    result.coverage = 0.0;
    result.mediumOpacity = 0.0;
    result.densityMean = 0.0;
    result.mediumTravel = farDistance + 1.0;
    result.eventTravel = farDistance + 1.0;
    result.eventCoverage = 0.0;
    result.eventColor = 0.0;

    float transmittance = 1.0;
    float3 inScattering = 0.0;
    float densityTravelSum = 0.0;
    float densitySum = 0.0;
    float densityAccumulation = 0.0;

    SolidHit nearestSolid;
    nearestSolid.hit = false;
    nearestSolid.travel = farDistance;
    nearestSolid.normal = 0.0;
    nearestSolid.fieldId = 0.0;
    nearestSolid.primitiveId = -1;
    [loop]
    for (int sliceIndex = 0; sliceIndex < MEDIUM_FROXEL_SLICE_COUNT; sliceIndex++)
    {
        SolidHit cellSolid = nearestViewFroxelSolidHit(uv, origin, direction, sliceIndex);
        if (cellSolid.hit && cellSolid.travel < nearestSolid.travel)
        {
            nearestSolid = cellSolid;
        }
    }

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

    integrateMediumRange(uv, 0.0, stopTravel, transmittance, inScattering, densityAccumulation, densityTravelSum, densitySum);
    float mediumOpacity = saturate(1.0 - transmittance);
    float mediumDensityMean = saturate(densityAccumulation / (float)MEDIUM_FROXEL_SLICE_COUNT);
    float mediumTravel = densitySum > 0.0001 ? min(densityTravelSum / densitySum, stopTravel) : farDistance + 1.0;

    if (gridHit)
    {
        result.eventTravel = gridTravel;
        result.eventCoverage = gridAlpha;
        result.eventColor = gridColor * gridAlpha;
    }

    if (nearestSolid.hit)
    {
        float3 p = origin + direction * nearestSolid.travel;
        float3 bodyColor = shadeBody(p, nearestSolid.normal, nearestSolid.primitiveId);
        result.color = bodyColor * transmittance + inScattering;
        result.travel = nearestSolid.travel;
        result.fieldId = nearestSolid.fieldId;
        result.normal = nearestSolid.normal;
        result.coverage = 1.0;
        result.mediumOpacity = mediumOpacity;
        result.densityMean = mediumDensityMean;
        result.mediumTravel = mediumTravel;
    }
    else
    {
        result.color = result.color * transmittance + inScattering;
        result.mediumOpacity = mediumOpacity;
        result.densityMean = mediumDensityMean;
        result.mediumTravel = mediumTravel;
        if (result.mediumOpacity > 0.015)
        {
            result.fieldId = FIELD_ID_MEDIUM;
            result.normal = -direction;
            result.coverage = saturate(max(result.mediumOpacity, result.densityMean * 4.0));
            result.travel = mediumTravel;
        }
    }

    return result;
}

float3 shadeBody(float3 p, float3 normal, int primitiveId)
{
    float3 self = float3(0.0, 0.0, 2.2);
    float3 lightDirection = normalize(self - p);
    float ndl = saturate(dot(normal, lightDirection));
    if (primitiveId == 0)
    {
        return float3(10.0, 8.7, 4.2);
    }

    if (primitiveId == CURSOR_PRIMITIVE_ID)
    {
        float rim = pow(1.0 - saturate(dot(normal, normalize(cameraPosition - p))), 2.4);
        float light = 0.18 + saturate(dot(normal, lightDirection)) * 1.45;
        return float3(0.38, 0.92, 1.0) * light + float3(0.90, 0.78, 1.0) * rim * 0.65;
    }

    float hue = hash21(float2(primitiveId, 6.3));
    float3 albedo = lerp(float3(0.34, 0.42, 0.18), float3(0.70, 0.76, 0.42), hue);
    return albedo * (0.08 + ndl * 1.6);
}

SceneOut D3D12ScenePS(VertexOut input)
{
    float2 screenUv = float2(input.uv.x, 1.0 - input.uv.y);
    float2 pixel = screenUv * resolution;
    float3 rayDirection = rayDirectionForPixel(pixel, jitterPixels, cameraPosition, gridCenter);

    RayMarchResult result = traverseRay(input.uv, screenUv, cameraPosition, rayDirection);

    if (renderDebugMode >= 10.5 && renderDebugMode < 11.5)
    {
        result.color = lerp(float3(0.006, 0.016, 0.026), float3(0.32, 0.86, 1.0), saturate(result.densityMean * 6.0));
    }

    SceneOut output;
    output.colorTravel = float4(result.color, min(result.travel, farDistance + 1.0));
    output.metadata = float4(result.fieldId, result.normal);
    output.control = float4(0.0, result.coverage, result.mediumOpacity, result.densityMean);
    output.mediumPacket = float4(FIELD_ID_MEDIUM, result.mediumTravel, result.mediumOpacity, result.densityMean);
    output.eventColor = float4(result.eventColor, result.eventCoverage);
    output.eventMetadata = float4(FIELD_ID_GRID, result.eventTravel, result.eventCoverage, 0.0);
    return output;
}
