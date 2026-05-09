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
SamplerState gridSampler : register(s0);

struct TransparentSurface
{
    float4 centerRadius;
    float4 kindDensity;
    float4 lineParams;
    float4 colorScatter;
};

StructuredBuffer<int4> transparentSurfaceIds : register(t15);
StructuredBuffer<TransparentSurface> transparentSurfaces : register(t16);

static const int PLANET_COUNT = 5;
static const float SUN_RADIUS = 1.12;
static const float FIELD_ID_GRID = 1.0;
static const float FIELD_ID_SELF = 2.0;
static const float FIELD_ID_MEDIUM = 3.0;
static const float FIELD_ID_TRANSPARENT_EVENT = 4.0;
static const float FIELD_ID_PLANET_BASE = 10.0;
static const int MEDIUM_FROXEL_ATLAS_COLUMNS = 8;
static const int MEDIUM_FROXEL_ATLAS_ROWS = 4;
static const int MEDIUM_FROXEL_DOWNSCALE = 8;
static const int MEDIUM_FROXEL_SLICE_COUNT = MEDIUM_FROXEL_ATLAS_COLUMNS * MEDIUM_FROXEL_ATLAS_ROWS;
static const int FROXEL_COUNT_X = 8;
static const int FROXEL_COUNT_Y = 8;
static const int FROXEL_COUNT_Z = 4;
static const int FROXEL_SLOT_COUNT = 2;
static const float FROXEL_MIN_Z = -2.0;
static const float FROXEL_MAX_Z = 6.0;
static const float PI = 3.14159265359;
static const float GRID_HEIGHT_TEXEL_COUNT = 128.0;
static const float TERRAIN_ISOLINE_SPACING = 0.12;

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

struct TransparentEvent
{
    bool hit;
    float travel;
    float3 color;
    float alpha;
    float coverage;
    int surfaceId;
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

float lineDistance(float2 p, float cell)
{
    float2 centered = abs(frac(p / cell + 0.5) - 0.5) * cell;
    return min(centered.x, centered.y);
}

float lineMaskFromDistance(float distanceValue, float width, float fade)
{
    return 1.0 - smoothstep(width, max(width + fade, width + 0.0001), distanceValue);
}

float isolineMask(float height, float width)
{
    float wrapped = abs(frac(height / TERRAIN_ISOLINE_SPACING + 0.5) - 0.5) * TERRAIN_ISOLINE_SPACING;
    return lineMaskFromDistance(wrapped, width, width * 1.8);
}

float fieldLineMask(float2 gradient, float width)
{
    float slope = length(gradient);
    if (slope < 0.0001)
    {
        return 0.0;
    }

    float angleDomain = (atan2(gradient.y, gradient.x) / PI + 1.0) * 6.0;
    float wrapped = abs(frac(angleDomain + 0.5) - 0.5);
    float angleLine = lineMaskFromDistance(wrapped, width, width * 1.6);
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

int froxelIndexForCell(int3 cell)
{
    return cell.x + cell.y * FROXEL_COUNT_X + cell.z * FROXEL_COUNT_X * FROXEL_COUNT_Y;
}

float3 froxelWorldMin()
{
    return float3(gridCenter - gridRadius.xx, FROXEL_MIN_Z);
}

float3 froxelWorldMax()
{
    return float3(gridCenter + gridRadius.xx, FROXEL_MAX_Z);
}

float3 froxelCellSize()
{
    float3 boundsMin = froxelWorldMin();
    float3 boundsMax = froxelWorldMax();
    return (boundsMax - boundsMin) / float3(FROXEL_COUNT_X, FROXEL_COUNT_Y, FROXEL_COUNT_Z);
}

bool traceFroxelBounds(float3 origin, float3 direction, out float enterTravel, out float exitTravel)
{
    float3 boundsMin = froxelWorldMin();
    float3 boundsMax = froxelWorldMax();
    enterTravel = 0.0;
    exitTravel = farDistance;
    bool hit = true;

    if (abs(direction.x) < 0.000001)
    {
        if (origin.x < boundsMin.x || origin.x > boundsMax.x)
        {
            hit = false;
        }
    }
    else
    {
        float t0 = (boundsMin.x - origin.x) / direction.x;
        float t1 = (boundsMax.x - origin.x) / direction.x;
        enterTravel = max(enterTravel, min(t0, t1));
        exitTravel = min(exitTravel, max(t0, t1));
    }

    if (abs(direction.y) < 0.000001)
    {
        if (origin.y < boundsMin.y || origin.y > boundsMax.y)
        {
            hit = false;
        }
    }
    else
    {
        float t0 = (boundsMin.y - origin.y) / direction.y;
        float t1 = (boundsMax.y - origin.y) / direction.y;
        enterTravel = max(enterTravel, min(t0, t1));
        exitTravel = min(exitTravel, max(t0, t1));
    }

    if (abs(direction.z) < 0.000001)
    {
        if (origin.z < boundsMin.z || origin.z > boundsMax.z)
        {
            hit = false;
        }
    }
    else
    {
        float t0 = (boundsMin.z - origin.z) / direction.z;
        float t1 = (boundsMax.z - origin.z) / direction.z;
        enterTravel = max(enterTravel, min(t0, t1));
        exitTravel = min(exitTravel, max(t0, t1));
    }

    enterTravel = max(enterTravel, 0.0);
    exitTravel = min(exitTravel, farDistance);
    return hit && exitTravel > enterTravel;
}

struct FroxelDda
{
    int3 cell;
    int3 stepCell;
    float3 nextBoundaryTravel;
    float3 deltaTravel;
    float exitTravel;
    int active;
};

FroxelDda beginFroxelDda(float3 origin, float3 direction, float enterTravel, float exitTravel)
{
    FroxelDda dda;
    float3 boundsMin = froxelWorldMin();
    float3 cellSize = froxelCellSize();
    float3 p = origin + direction * (enterTravel + 0.0001);
    float3 local = (p - boundsMin) / cellSize;
    dda.cell = clamp((int3)floor(local), int3(0, 0, 0), int3(FROXEL_COUNT_X - 1, FROXEL_COUNT_Y - 1, FROXEL_COUNT_Z - 1));
    dda.stepCell = int3(direction.x >= 0.0 ? 1 : -1, direction.y >= 0.0 ? 1 : -1, direction.z >= 0.0 ? 1 : -1);

    float3 nextBoundary = boundsMin + (float3)dda.cell * cellSize;
    nextBoundary += float3(
        direction.x >= 0.0 ? cellSize.x : 0.0,
        direction.y >= 0.0 ? cellSize.y : 0.0,
        direction.z >= 0.0 ? cellSize.z : 0.0);

    float3 absDirection = max(abs(direction), 0.000001);
    dda.nextBoundaryTravel = (nextBoundary - origin) / direction;
    dda.nextBoundaryTravel = float3(
        abs(direction.x) < 0.000001 ? farDistance + 1.0 : dda.nextBoundaryTravel.x,
        abs(direction.y) < 0.000001 ? farDistance + 1.0 : dda.nextBoundaryTravel.y,
        abs(direction.z) < 0.000001 ? farDistance + 1.0 : dda.nextBoundaryTravel.z);
    dda.deltaTravel = cellSize / absDirection;
    dda.deltaTravel = float3(
        abs(direction.x) < 0.000001 ? farDistance + 1.0 : dda.deltaTravel.x,
        abs(direction.y) < 0.000001 ? farDistance + 1.0 : dda.deltaTravel.y,
        abs(direction.z) < 0.000001 ? farDistance + 1.0 : dda.deltaTravel.z);
    dda.exitTravel = exitTravel;
    dda.active = 1;
    return dda;
}

float froxelDdaCellExit(FroxelDda dda)
{
    return min(min(dda.nextBoundaryTravel.x, dda.nextBoundaryTravel.y), min(dda.nextBoundaryTravel.z, dda.exitTravel));
}

void advanceFroxelDda(inout FroxelDda dda)
{
    float axisTravel = min(dda.nextBoundaryTravel.x, min(dda.nextBoundaryTravel.y, dda.nextBoundaryTravel.z));
    if (axisTravel >= dda.exitTravel)
    {
        dda.active = 0;
        return;
    }

    if (dda.nextBoundaryTravel.x <= axisTravel + 0.00001)
    {
        dda.cell.x += dda.stepCell.x;
        dda.nextBoundaryTravel.x += dda.deltaTravel.x;
    }
    if (dda.nextBoundaryTravel.y <= axisTravel + 0.00001)
    {
        dda.cell.y += dda.stepCell.y;
        dda.nextBoundaryTravel.y += dda.deltaTravel.y;
    }
    if (dda.nextBoundaryTravel.z <= axisTravel + 0.00001)
    {
        dda.cell.z += dda.stepCell.z;
        dda.nextBoundaryTravel.z += dda.deltaTravel.z;
    }

    dda.active =
        dda.cell.x >= 0 && dda.cell.x < FROXEL_COUNT_X &&
        dda.cell.y >= 0 && dda.cell.y < FROXEL_COUNT_Y &&
        dda.cell.z >= 0 && dda.cell.z < FROXEL_COUNT_Z ? 1 : 0;
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

float gridSurfaceDistanceAt(float3 origin, float3 direction, float travel)
{
    float3 p = origin + direction * travel;
    return p.z - terrainHeight(p.xy);
}

bool bracketGridSurfaceInterval(float3 origin, float3 direction, float startTravel, float endTravel, out float bracketStart, out float bracketEnd)
{
    bracketStart = startTravel;
    bracketEnd = endTravel;
    float midTravel = (startTravel + endTravel) * 0.5;
    float startDistance = gridSurfaceDistanceAt(origin, direction, startTravel);
    float midDistance = gridSurfaceDistanceAt(origin, direction, midTravel);
    float endDistance = gridSurfaceDistanceAt(origin, direction, endTravel);
    float epsilon = 0.002;

    if (abs(startDistance) <= epsilon)
    {
        bracketEnd = midTravel;
        return true;
    }

    if (startDistance * midDistance <= 0.0)
    {
        bracketEnd = midTravel;
        return true;
    }

    if (abs(midDistance) <= epsilon)
    {
        bracketStart = midTravel;
        bracketEnd = endTravel;
        return true;
    }

    if (midDistance * endDistance <= 0.0)
    {
        bracketStart = midTravel;
        return true;
    }

    return false;
}

bool traceGridSurface(float3 origin, float3 direction, float intervalStart, float intervalEnd, TransparentSurface surface, out float travel)
{
    travel = farDistance + 1.0;
    if (intervalEnd <= intervalStart || intervalStart >= farDistance)
    {
        return false;
    }

    float bracketStart;
    float bracketEnd;
    if (!bracketGridSurfaceInterval(origin, direction, max(intervalStart, 0.0), min(intervalEnd, farDistance), bracketStart, bracketEnd))
    {
        return false;
    }

    float t = (bracketStart + bracketEnd) * 0.5;
    [unroll]
    for (int i = 0; i < 6; i++)
    {
        float mid = (bracketStart + bracketEnd) * 0.5;
        float startDistance = gridSurfaceDistanceAt(origin, direction, bracketStart);
        float midDistance = gridSurfaceDistanceAt(origin, direction, mid);
        if (startDistance * midDistance <= 0.0)
        {
            bracketEnd = mid;
        }
        else
        {
            bracketStart = mid;
        }

        t = (bracketStart + bracketEnd) * 0.5;
    }

    float3 p = origin + direction * t;
    float radius = max(surface.centerRadius.w, 0.001);
    float2 local = (p.xy - surface.centerRadius.xy) / radius;
    if (t <= intervalStart || t >= intervalEnd || t >= farDistance || length(local) > 1.0)
    {
        return false;
    }

    travel = t;
    return true;
}

float3 gridEventColor(float3 p, TransparentSurface surface, out float alpha)
{
    float height = terrainHeight(p.xy);
    float2 gradient = terrainGradient(p.xy);
    float2 footprint = max(abs(ddx(p.xy)), abs(ddy(p.xy)));
    float pixelWidth = max(max(footprint.x, footprint.y), 0.006);
    float minor = lineMaskFromDistance(lineDistance(p.xy, surface.lineParams.x), pixelWidth * 0.56, pixelWidth * 1.35);
    float major = lineMaskFromDistance(lineDistance(p.xy, surface.lineParams.y), pixelWidth * 0.88, pixelWidth * 1.60);
    float contour = isolineMask(height, max(pixelWidth * 0.52, 0.010)) * smoothstep(0.025, 0.25, length(gradient));
    float fieldLine = fieldLineMask(gradient, max(pixelWidth * 0.90, 0.016));
    float support = saturate(minor * 0.38 + major * 0.92 + contour * 0.28 + fieldLine * 0.18);
    float3 color = surface.colorScatter.rgb * (minor * 0.30 + major * 0.74);
    color += float3(0.98, 1.0, 0.78) * contour * 0.16;
    color += float3(0.36, 0.92, 1.0) * fieldLine * 0.12;
    alpha = saturate(support * surface.colorScatter.w);
    return color;
}

void considerTransparentSurfaceEvent(float3 origin, float3 direction, int surfaceId, uint consumedSurfaceMask, float intervalStart, float intervalEnd, inout TransparentEvent nearest)
{
    if (surfaceId < 0)
    {
        return;
    }

    uint surfaceBit = 1u << (uint)min(surfaceId, 31);
    if ((consumedSurfaceMask & surfaceBit) != 0u)
    {
        return;
    }

    TransparentSurface surface = transparentSurfaces[surfaceId];
    float travel;
    if (!traceGridSurface(origin, direction, intervalStart, min(intervalEnd, nearest.travel), surface, travel))
    {
        return;
    }

    nearest.hit = true;
    nearest.travel = travel;
    nearest.color = 0.0;
    nearest.alpha = 1.0;
    nearest.coverage = 1.0;
    nearest.surfaceId = surfaceId;
}

void considerFroxelTransparentEvents(float3 origin, float3 direction, int froxelIndex, uint consumedSurfaceMask, float intervalStart, float intervalEnd, inout TransparentEvent nearest)
{
    if (froxelIndex < 0)
    {
        return;
    }

    [unroll]
    for (int slotGroup = 0; slotGroup < FROXEL_SLOT_COUNT; slotGroup++)
    {
        int4 ids = transparentSurfaceIds[froxelIndex * FROXEL_SLOT_COUNT + slotGroup];
        considerTransparentSurfaceEvent(origin, direction, ids.x, consumedSurfaceMask, intervalStart, intervalEnd, nearest);
        considerTransparentSurfaceEvent(origin, direction, ids.y, consumedSurfaceMask, intervalStart, intervalEnd, nearest);
        considerTransparentSurfaceEvent(origin, direction, ids.z, consumedSurfaceMask, intervalStart, intervalEnd, nearest);
        considerTransparentSurfaceEvent(origin, direction, ids.w, consumedSurfaceMask, intervalStart, intervalEnd, nearest);
    }
}

SolidHit nearestFroxelSolidHit(float3 origin, float3 direction, int froxelIndex, float intervalStart, float intervalEnd)
{
    SolidHit nearest;
    nearest.hit = false;
    nearest.travel = intervalEnd;
    nearest.normal = 0.0;
    nearest.fieldId = 0.0;
    nearest.primitiveId = -1;
    considerFroxelPrimitiveHits(origin, direction, froxelIndex, intervalStart, intervalEnd, nearest);
    return nearest;
}

TransparentEvent nearestFroxelTransparentEvent(float3 origin, float3 direction, int froxelIndex, uint consumedSurfaceMask, float intervalStart, float intervalEnd)
{
    TransparentEvent nearest;
    nearest.hit = false;
    nearest.travel = intervalEnd;
    nearest.color = 0.0;
    nearest.alpha = 0.0;
    nearest.coverage = 0.0;
    nearest.surfaceId = -1;
    considerFroxelTransparentEvents(origin, direction, froxelIndex, consumedSurfaceMask, intervalStart, intervalEnd, nearest);
    return nearest;
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

TransparentEvent nearestViewFroxelTransparentEvent(float2 uv, float3 origin, float3 direction, int sliceIndex, uint consumedSurfaceMask)
{
    float intervalStart = mediumSliceStartTravel(sliceIndex);
    float intervalEnd = min(mediumSliceEndTravel(sliceIndex), farDistance);
    TransparentEvent nearest;
    nearest.hit = false;
    nearest.travel = intervalEnd;
    nearest.color = 0.0;
    nearest.alpha = 0.0;
    nearest.coverage = 0.0;
    nearest.surfaceId = -1;

    int4 ids = transparentSurfaceIds[viewFroxelIndexForUvSlice(uv, sliceIndex)];
    considerTransparentSurfaceEvent(origin, direction, ids.x, consumedSurfaceMask, intervalStart, intervalEnd, nearest);
    considerTransparentSurfaceEvent(origin, direction, ids.y, consumedSurfaceMask, intervalStart, intervalEnd, nearest);
    considerTransparentSurfaceEvent(origin, direction, ids.z, consumedSurfaceMask, intervalStart, intervalEnd, nearest);
    considerTransparentSurfaceEvent(origin, direction, ids.w, consumedSurfaceMask, intervalStart, intervalEnd, nearest);
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

    float froxelEnter;
    float froxelExit;
    bool hasFroxelTube = traceFroxelBounds(origin, direction, froxelEnter, froxelExit);
    SolidHit nearestSolid;
    nearestSolid.hit = false;
    nearestSolid.travel = farDistance;
    nearestSolid.normal = 0.0;
    nearestSolid.fieldId = 0.0;
    nearestSolid.primitiveId = -1;

    if (hasFroxelTube)
    {
        FroxelDda dda = beginFroxelDda(origin, direction, froxelEnter, froxelExit);
        float cellStart = froxelEnter;
        [loop]
        for (int ddaStep = 0; ddaStep < 64 && dda.active != 0; ddaStep++)
        {
            float cellEnd = froxelDdaCellExit(dda);
            if (cellEnd <= cellStart + 0.00001)
            {
                advanceFroxelDda(dda);
                cellStart = cellEnd;
                continue;
            }

            int froxelIndex = froxelIndexForCell(dda.cell);
            SolidHit cellSolid = nearestFroxelSolidHit(origin, direction, froxelIndex, cellStart, min(cellEnd, nearestSolid.travel));
            if (cellSolid.hit && cellSolid.travel < nearestSolid.travel)
            {
                nearestSolid = cellSolid;
            }

            cellStart = cellEnd;
            advanceFroxelDda(dda);
        }
    }

    TransparentEvent nearestTransparent;
    nearestTransparent.hit = false;
    nearestTransparent.travel = farDistance;
    nearestTransparent.color = 0.0;
    nearestTransparent.alpha = 0.0;
    nearestTransparent.coverage = 0.0;
    nearestTransparent.surfaceId = -1;
    [loop]
    for (int sliceIndex = 0; sliceIndex < MEDIUM_FROXEL_SLICE_COUNT; sliceIndex++)
    {
        TransparentEvent cellTransparent = nearestViewFroxelTransparentEvent(uv, origin, direction, sliceIndex, 0u);
        if (cellTransparent.hit && cellTransparent.travel < nearestTransparent.travel)
        {
            nearestTransparent = cellTransparent;
        }
    }

    if (nearestTransparent.hit)
    {
        float3 transparentPosition = origin + direction * nearestTransparent.travel;
        float transparentAlpha;
        float3 transparentColor = gridEventColor(transparentPosition, transparentSurfaces[nearestTransparent.surfaceId], transparentAlpha);
        if (transparentAlpha <= 0.001)
        {
            nearestTransparent.hit = false;
        }
        else
        {
            nearestTransparent.color = transparentColor;
            nearestTransparent.alpha = transparentAlpha;
            nearestTransparent.coverage = transparentAlpha;
        }
    }

    float stopTravel = nearestSolid.hit ? nearestSolid.travel : farDistance;
    if (nearestTransparent.hit && nearestTransparent.travel < stopTravel)
    {
        integrateMediumRange(uv, 0.0, nearestTransparent.travel, transmittance, inScattering, densityAccumulation, densityTravelSum, densitySum);
        inScattering += transmittance * nearestTransparent.color * nearestTransparent.alpha;
        transmittance *= 1.0 - nearestTransparent.alpha;
        eventTravelSum += nearestTransparent.coverage * nearestTransparent.travel;
        eventSupportSum += nearestTransparent.coverage;
        integrateMediumRange(uv, nearestTransparent.travel, stopTravel, transmittance, inScattering, densityAccumulation, densityTravelSum, densitySum);
    }
    else
    {
        integrateMediumRange(uv, 0.0, stopTravel, transmittance, inScattering, densityAccumulation, densityTravelSum, densitySum);
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
        result.mediumOpacity = saturate(1.0 - transmittance);
        result.densityMean = saturate(densityAccumulation / (float)MEDIUM_FROXEL_SLICE_COUNT);
        return result;
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
