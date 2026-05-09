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
    float3 presentationPadding;
};

Texture2D<float4> gridHeightTexture : register(t0);
StructuredBuffer<int4> froxelPrimitiveIds : register(t1);
Texture2D<float4> mediumVolumeTexture : register(t13);
Texture2D<float4> mediumTransportTexture : register(t14);
Texture2D<float4> mediumEventTexture : register(t17);
SamplerState gridSampler : register(s0);

static const int PLANET_COUNT = 5;
static const float SUN_RADIUS = 1.12;
static const float FIELD_ID_GRID = 1.0;
static const float FIELD_ID_SELF = 2.0;
static const float FIELD_ID_MEDIUM = 3.0;
static const float FIELD_ID_TRANSPARENT_EVENT = 4.0;
static const float FIELD_ID_PLANET_BASE = 10.0;
static const int MEDIUM_FROXEL_ATLAS_COLUMNS = 8;
static const int MEDIUM_FROXEL_ATLAS_ROWS = 4;
static const int MEDIUM_FROXEL_SLICE_COUNT = MEDIUM_FROXEL_ATLAS_COLUMNS * MEDIUM_FROXEL_ATLAS_ROWS;
static const int FROXEL_COUNT_X = 8;
static const int FROXEL_COUNT_Y = 8;
static const int FROXEL_COUNT_Z = 4;
static const int FROXEL_SLOT_COUNT = 2;
static const float FROXEL_MIN_Z = -2.0;
static const float FROXEL_MAX_Z = 6.0;

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
};

VertexOut FullscreenTriangleVS(uint vertexId : SV_VertexID)
{
    float2 uv = float2((vertexId << 1) & 2, vertexId & 2);
    VertexOut output;
    output.position = float4(uv * float2(2.0, -2.0) + float2(-1.0, 1.0), 0.0, 1.0);
    output.uv = uv;
    return output;
}

float hash21(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
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

float3 primitiveCenterAt(int primitiveId, float sampleTime)
{
    if (primitiveId == 0)
    {
        return float3(0.0, 0.0, 2.2);
    }

    return planetCenterAt(primitiveId - 1, sampleTime);
}

float primitiveRadius(int primitiveId)
{
    return primitiveId == 0 ? SUN_RADIUS : planetRadius(primitiveId - 1);
}

float primitiveFieldId(int primitiveId)
{
    return primitiveId == 0 ? FIELD_ID_SELF : FIELD_ID_PLANET_BASE + (float)(primitiveId - 1);
}

int clampCell(float normalized, int count)
{
    return clamp((int)floor(normalized * (float)count), 0, count - 1);
}

int froxelIndexForPosition(float3 p)
{
    float2 local = gridLocal(p.xy);
    float localZ = (p.z - FROXEL_MIN_Z) / (FROXEL_MAX_Z - FROXEL_MIN_Z);
    if (abs(local.x) > 1.0 || abs(local.y) > 1.0 || localZ < 0.0 || localZ > 1.0)
    {
        return -1;
    }

    int x = clampCell(local.x * 0.5 + 0.5, FROXEL_COUNT_X);
    int y = clampCell(local.y * 0.5 + 0.5, FROXEL_COUNT_Y);
    int z = clampCell(localZ, FROXEL_COUNT_Z);
    return x + y * FROXEL_COUNT_X + z * FROXEL_COUNT_X * FROXEL_COUNT_Y;
}

void considerPrimitiveHit(float3 origin, float3 direction, int primitiveId, float intervalStart, float intervalEnd, inout SolidHit nearest)
{
    if (primitiveId < 0)
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
    for (int slotGroup = 0; slotGroup < FROXEL_SLOT_COUNT; slotGroup++)
    {
        int4 ids = froxelPrimitiveIds[froxelIndex * FROXEL_SLOT_COUNT + slotGroup];
        considerPrimitiveHit(origin, direction, ids.x, intervalStart, intervalEnd, nearest);
        considerPrimitiveHit(origin, direction, ids.y, intervalStart, intervalEnd, nearest);
        considerPrimitiveHit(origin, direction, ids.z, intervalStart, intervalEnd, nearest);
        considerPrimitiveHit(origin, direction, ids.w, intervalStart, intervalEnd, nearest);
    }
}

SolidHit nearestBinnedSolidHit(float3 origin, float3 direction, float intervalStart, float intervalEnd)
{
    SolidHit nearest;
    nearest.hit = false;
    nearest.travel = intervalEnd;
    nearest.normal = 0.0;
    nearest.fieldId = 0.0;
    nearest.primitiveId = -1;

    float3 startPoint = origin + direction * intervalStart;
    float3 midPoint = origin + direction * ((intervalStart + intervalEnd) * 0.5);
    float3 endPoint = origin + direction * intervalEnd;
    considerFroxelPrimitiveHits(origin, direction, froxelIndexForPosition(startPoint), intervalStart, intervalEnd, nearest);
    considerFroxelPrimitiveHits(origin, direction, froxelIndexForPosition(midPoint), intervalStart, intervalEnd, nearest);
    considerFroxelPrimitiveHits(origin, direction, froxelIndexForPosition(endPoint), intervalStart, intervalEnd, nearest);

    return nearest;
}

bool traceGrid(float3 origin, float3 direction, out float travel)
{
    travel = farDistance + 1.0;
    if (abs(direction.z) < 0.0001)
    {
        return false;
    }

    float t = (0.0 - origin.z) / direction.z;
    if (t <= 0.0 || t >= farDistance)
    {
        return false;
    }

    float3 p = origin + direction * t;
    float h = terrainHeight(p.xy);
    float correction = (h - p.z) / direction.z;
    t += correction;
    p = origin + direction * t;
    if (t <= 0.0 || t >= farDistance || length(gridLocal(p.xy)) > 1.0)
    {
        return false;
    }

    travel = t;
    return true;
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

void integrateMediumSlice(
    float2 uv,
    int sliceIndex,
    float intervalFraction,
    inout float transmittance,
    inout float3 inScattering,
    inout float densityMean,
    inout float densityTravelSum,
    inout float densitySum,
    inout float eventTravelSum,
    inout float eventSupportSum)
{
    float2 atlasUv = mediumAtlasUv(uv, sliceIndex);
    float4 diagnostic = mediumVolumeTexture.SampleLevel(gridSampler, atlasUv, 0.0);
    float4 transport = mediumTransportTexture.SampleLevel(gridSampler, atlasUv, 0.0);
    float4 eventSummary = mediumEventTexture.SampleLevel(gridSampler, atlasUv, 0.0);
    float sliceTravel = mediumSliceTravel(sliceIndex);
    float fraction = saturate(intervalFraction);
    float fullSliceTransmittance = saturate(transport.a);
    float sliceExtinction = -log(max(fullSliceTransmittance, 0.0001));
    float partialTransmittance = exp(-sliceExtinction * fraction);
    float scatterDenominator = max(1.0 - fullSliceTransmittance, 0.0001);
    float scatterFraction = (1.0 - partialTransmittance) / scatterDenominator;
    float densityContribution = diagnostic.x * fraction;
    float eventSupport = saturate(eventSummary.x) * fraction;

    densityMean += densityContribution;
    densityTravelSum += densityContribution * sliceTravel;
    densitySum += densityContribution;
    eventTravelSum += eventSupport * sliceTravel;
    eventSupportSum += eventSupport;
    inScattering += transmittance * transport.rgb * scatterFraction;
    transmittance *= saturate(partialTransmittance);
}

void integrateMedium(
    float2 uv,
    float maxTravel,
    out float densityMean,
    out float transmittance,
    out float3 inScattering,
    out float representativeTravel,
    out float transparentEventSupport,
    out float transparentEventTravel)
{
    densityMean = 0.0;
    transmittance = 1.0;
    inScattering = 0.0;
    representativeTravel = maxTravel;
    transparentEventSupport = 0.0;
    transparentEventTravel = maxTravel;
    float densityTravelSum = 0.0;
    float densitySum = 0.0;
    float eventTravelSum = 0.0;
    float eventSupportSum = 0.0;

    [loop]
    for (int sliceIndex = 0; sliceIndex < MEDIUM_FROXEL_SLICE_COUNT; sliceIndex++)
    {
        float intervalStart = mediumSliceStartTravel(sliceIndex);
        if (intervalStart >= maxTravel)
        {
            break;
        }

        float intervalEnd = mediumSliceEndTravel(sliceIndex);
        float intervalFraction = saturate((maxTravel - intervalStart) / max(intervalEnd - intervalStart, 0.0001));
        integrateMediumSlice(
            uv,
            sliceIndex,
            intervalFraction,
            transmittance,
            inScattering,
            densityMean,
            densityTravelSum,
            densitySum,
            eventTravelSum,
            eventSupportSum);
    }

    densityMean = saturate(densityMean / (float)MEDIUM_FROXEL_SLICE_COUNT);
    transparentEventSupport = saturate(eventSupportSum / (float)MEDIUM_FROXEL_SLICE_COUNT * 4.0);
    if (densitySum > 0.0001)
    {
        representativeTravel = densityTravelSum / densitySum;
    }

    if (eventSupportSum > 0.0001)
    {
        transparentEventTravel = eventTravelSum / eventSupportSum;
    }
}

RayMarchResult traverseRay(float2 uv, float3 origin, float3 direction)
{
    RayMarchResult result;
    result.color = float3(0.001, 0.003, 0.004);
    result.travel = farDistance + 1.0;
    result.fieldId = 0.0;
    result.normal = 0.0;
    result.coverage = 0.0;
    result.mediumOpacity = 0.0;
    result.densityMean = 0.0;

    float transmittance = 1.0;
    float3 inScattering = 0.0;
    float densityTravelSum = 0.0;
    float densitySum = 0.0;
    float eventTravelSum = 0.0;
    float eventSupportSum = 0.0;
    float densityAccumulation = 0.0;

    [loop]
    for (int sliceIndex = 0; sliceIndex < MEDIUM_FROXEL_SLICE_COUNT; sliceIndex++)
    {
        float intervalStart = mediumSliceStartTravel(sliceIndex);
        float intervalEnd = mediumSliceEndTravel(sliceIndex);
        SolidHit hit = nearestBinnedSolidHit(origin, direction, intervalStart, min(intervalEnd, farDistance));
        float intervalFraction = hit.hit
            ? saturate((hit.travel - intervalStart) / max(intervalEnd - intervalStart, 0.0001))
            : 1.0;

        integrateMediumSlice(
            uv,
            sliceIndex,
            intervalFraction,
            transmittance,
            inScattering,
            densityAccumulation,
            densityTravelSum,
            densitySum,
            eventTravelSum,
            eventSupportSum);

        if (hit.hit)
        {
            float3 p = origin + direction * hit.travel;
            float3 bodyColor = shadeBody(p, hit.normal, hit.primitiveId);
            result.color = bodyColor * transmittance + inScattering;
            result.travel = hit.travel;
            result.fieldId = hit.fieldId;
            result.normal = hit.normal;
            result.coverage = 1.0;
            result.mediumOpacity = saturate(1.0 - transmittance);
            result.densityMean = saturate(densityAccumulation / (float)MEDIUM_FROXEL_SLICE_COUNT);
            return result;
        }
    }

    result.color = result.color * transmittance + inScattering;
    result.mediumOpacity = saturate(1.0 - transmittance);
    result.densityMean = saturate(densityAccumulation / (float)MEDIUM_FROXEL_SLICE_COUNT);
    float transparentEventSupport = saturate(eventSupportSum / (float)MEDIUM_FROXEL_SLICE_COUNT * 4.0);
    if (transparentEventSupport > 0.010)
    {
        result.fieldId = FIELD_ID_TRANSPARENT_EVENT;
        result.normal = -direction;
        result.coverage = transparentEventSupport;
        result.travel = eventSupportSum > 0.0001 ? min(eventTravelSum / eventSupportSum, farDistance) : farDistance;
    }
    else if (result.mediumOpacity > 0.015)
    {
        result.fieldId = FIELD_ID_MEDIUM;
        result.normal = -direction;
        result.coverage = saturate(max(result.mediumOpacity, result.densityMean * 4.0));
        result.travel = densitySum > 0.0001 ? min(densityTravelSum / densitySum, farDistance) : farDistance;
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

    float hue = hash21(float2(primitiveId, 6.3));
    float3 albedo = lerp(float3(0.34, 0.42, 0.18), float3(0.70, 0.76, 0.42), hue);
    return albedo * (0.08 + ndl * 1.6);
}

SceneOut D3D12ScenePS(VertexOut input)
{
    float2 screenUv = float2(input.uv.x, 1.0 - input.uv.y);
    float2 pixel = screenUv * resolution;
    float3 rayDirection = rayDirectionForPixel(pixel, jitterPixels, cameraPosition, gridCenter);

    RayMarchResult result = traverseRay(input.uv, cameraPosition, rayDirection);

    if (renderDebugMode >= 10.5 && renderDebugMode < 11.5)
    {
        result.color = lerp(float3(0.006, 0.016, 0.026), float3(0.32, 0.86, 1.0), saturate(result.densityMean * 6.0));
    }

    SceneOut output;
    output.colorTravel = float4(result.color, min(result.travel, farDistance + 1.0));
    output.metadata = float4(result.fieldId, result.normal);
    output.control = float4(0.0, result.coverage, result.mediumOpacity, result.densityMean);
    return output;
}
