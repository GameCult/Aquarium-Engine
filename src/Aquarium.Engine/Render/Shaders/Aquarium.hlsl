cbuffer AquariumFrame : register(b0)
{
    float2 resolution;
    float timeSeconds;
    float gridRadius;
    float3 cameraPosition;
    float _pad0;
    float2 gridCenter;
    float farDistance;
    float _pad1;
};

Texture2D<float4> gridHeightTexture : register(t0);
StructuredBuffer<int4> froxelPrimitiveIds : register(t1);
SamplerState gridSampler : register(s0);

static const float PI = 3.14159265359;
static const float SURFACE_EPSILON = 0.0015;
static const float3 SUN_POSITION = float3(0.0, 0.0, 2.2);
static const float SUN_RADIUS = 1.12;
static const float GRID_WEATHER_WORLD_SCALE = 42.0;
static const float GRID_LINE_WORLD_CELL = 2.0;
static const float GRID_MAJOR_LINE_WORLD_CELL = GRID_LINE_WORLD_CELL * 5.0;
static const float GRID_LINE_PIXEL_WIDTH = 1.15;
static const float GRID_MAJOR_LINE_PIXEL_WIDTH = 1.65;
static const float GRID_LINE_PIXEL_FADE = 1.25;
static const float TERRAIN_ISOLINE_SPACING = 0.12;
static const float TERRAIN_ISOLINE_PIXEL_WIDTH = 1.05;
static const float TERRAIN_FIELD_LINE_PIXEL_WIDTH = 0.72;
static const int PLANET_COUNT = 5;
static const int PRIMITIVE_COUNT = PLANET_COUNT + 1;
static const float GRID_HEIGHT_TEXEL_COUNT = 128.0;
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

float valueNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);

    float a = hash21(i);
    float b = hash21(i + float2(1.0, 0.0));
    float c = hash21(i + float2(0.0, 1.0));
    float d = hash21(i + float2(1.0, 1.0));

    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

float fbm(float2 p)
{
    float sum = 0.0;
    float amplitude = 0.5;

    [unroll]
    for (int octave = 0; octave < 4; octave++)
    {
        sum += amplitude * valueNoise(p);
        p = mul(float2x2(1.62, 1.18, -1.18, 1.62), p) + 17.31;
        amplitude *= 0.5;
    }

    return sum;
}

float hash31(float3 p)
{
    p = frac(p * 0.1031);
    p += dot(p, p.yzx + 33.33);
    return frac((p.x + p.y) * p.z) * 2.0 - 1.0;
}

float noised3(float3 x)
{
    float3 p = floor(x);
    float3 w = frac(x);
    float3 u = w * w * w * (w * (w * 6.0 - 15.0) + 10.0);

    float a = hash31(p + float3(0.0, 0.0, 0.0));
    float b = hash31(p + float3(1.0, 0.0, 0.0));
    float c = hash31(p + float3(0.0, 1.0, 0.0));
    float d = hash31(p + float3(1.0, 1.0, 0.0));
    float e = hash31(p + float3(0.0, 0.0, 1.0));
    float f = hash31(p + float3(1.0, 0.0, 1.0));
    float g = hash31(p + float3(0.0, 1.0, 1.0));
    float h = hash31(p + float3(1.0, 1.0, 1.0));

    float k0 = a;
    float k1 = b - a;
    float k2 = c - a;
    float k3 = e - a;
    float k4 = a - b - c + d;
    float k5 = a - c - e + g;
    float k6 = a - b - e + f;
    float k7 = -a + b + c - d + e - f - g + h;

    return k0
        + k1 * u.x
        + k2 * u.y
        + k3 * u.z
        + k4 * u.x * u.y
        + k5 * u.y * u.z
        + k6 * u.z * u.x
        + k7 * u.x * u.y * u.z;
}

float fbm3(float3 p)
{
    float value = 0.0;
    float amplitude = 0.5;

    [unroll]
    for (int i = 0; i < 3; i++)
    {
        value += noised3(p) * amplitude;
        p = p.yzx * 2.03 + float3(3.7, 1.9, 5.1);
        amplitude *= 0.52;
    }

    return clamp(value, -1.0, 1.0);
}

float ridge(float value)
{
    return 1.0 - abs(value * 2.0 - 1.0);
}

float powerPulse(float distanceValue, float radius, float power)
{
    float normalized = saturate(distanceValue / max(radius, 0.001));
    float shaped = pow(1.0 - normalized, power);
    return shaped * shaped * (3.0 - 2.0 * shaped);
}

float2 gridLocal(float2 p)
{
    return (p - gridCenter) / max(gridRadius, 0.001);
}

float terrainMask(float2 p)
{
    float r = length(gridLocal(p));
    return 1.0 - smoothstep(0.78, 1.0, r);
}

float3 gridWeatherColor(float2 p, float height)
{
    float radius = length(gridLocal(p));
    float edge = terrainMask(p);
    float2 worldDomain = p / GRID_WEATHER_WORLD_SCALE;
    float2 lowWarp = float2(
        fbm(worldDomain * 1.35 + float2(0.0, height * 0.10 + timeSeconds * 0.018)),
        fbm(worldDomain.yx * 1.17 + float2(4.7, -2.3 + height * 0.08 - timeSeconds * 0.014))) - 0.5;
    float2 drift = float2(0.018, -0.011) * timeSeconds;
    float2 warped = worldDomain + lowWarp * 0.36 + drift;
    float sheet = pow(saturate(fbm(warped * 2.65 + height * 0.12) * 0.85), 1.25);
    float2 fineWarp = float2(
        fbm(warped * 4.8 + float2(1.9, 7.1 + timeSeconds * 0.041)),
        fbm(warped.yx * 4.2 + float2(-3.4, 5.6 - timeSeconds * 0.036))) - 0.5;
    float filamentNoise = fbm((warped + fineWarp * 0.075) * 9.5 + height * 0.24);
    float filament = pow(saturate(ridge(filamentNoise)), 3.4);
    float horizonDark = smoothstep(0.52, 1.02, radius);

    float3 deep = float3(0.006, 0.026, 0.045);
    float3 blue = float3(0.025, 0.20, 0.34);
    float3 teal = float3(0.09, 0.68, 0.62);
    float3 green = float3(0.55, 0.92, 0.36);
    float3 color = lerp(deep, lerp(blue, teal, sheet), edge);
    color += green * filament * edge * 0.18;
    color *= lerp(1.0, 0.22, horizonDark);
    return color;
}

float planetRadius(int index)
{
    return lerp(0.34, 0.62, hash21(float2(index, 19.7)));
}

float3 planetCenter(int index)
{
    float f = (float)index;
    float angle = f * 0.8975979 + timeSeconds * (0.08 + 0.011 * f);
    float radius = 4.1 + f * 0.77;
    float2 xy = float2(cos(angle), sin(angle)) * radius;
    return float3(xy, 1.15 + planetRadius(index) * 0.72);
}

float2 gridUv(float2 p)
{
    return gridLocal(p) * 0.5 + 0.5;
}

float2 gridWorld(float2 uv)
{
    return gridCenter + (uv * 2.0 - 1.0) * gridRadius;
}

float analyticTerrainHeight(float2 p)
{
    float positive = 0.0;
    float negative = 0.0;

    float selfWell = powerPulse(length(p - SUN_POSITION.xy), 8.5, 2.85);
    float selfWave = sin(length(p - SUN_POSITION.xy) * 1.2 - timeSeconds * 0.74);
    float selfHeight = -selfWell * 1.34 + selfWave * selfWell * 0.055;
    positive += max(selfHeight, 0.0);
    negative += max(-selfHeight, 0.0);

    [unroll]
    for (int i = 0; i < PLANET_COUNT; i++)
    {
        float2 delta = p - planetCenter(i).xy;
        float well = powerPulse(length(delta), 3.8 + planetRadius(i) * 2.5, 2.1);
        float wave = sin(length(delta) * 2.4 - timeSeconds * 1.35);
        float signedHeight = -well * 0.42 + wave * well * 0.022;
        positive += max(signedHeight, 0.0);
        negative += max(-signedHeight, 0.0);
    }

    float slow = sin((p.x * 0.08 + p.y * 0.06) + timeSeconds * 0.27)
        * sin((p.x * -0.04 + p.y * 0.07) - timeSeconds * 0.19) * 0.035;
    positive += max(slow, 0.0);
    negative += max(-slow, 0.0);
    return positive - negative;
}

float terrainHeight(float2 p)
{
    float2 uv = saturate(gridUv(p));
    return gridHeightTexture.SampleLevel(gridSampler, uv, 0.0).r;
}

float sphereSdf(float3 p, float3 center, float radius)
{
    return length(p - center) - radius;
}

float planetCutDepth(float3 localPosition, float radius, int index, bool isSelf)
{
    float3 domain = localPosition / max(radius, 0.001);
    float seed = hash21(float2(index, 41.17)) * 19.0;
    float broad = fbm3(domain * 1.55 + seed + timeSeconds * (isSelf ? 0.12 : 0.0));
    float fine = fbm3(domain * 5.2 + seed * 1.73 + timeSeconds * (isSelf ? -0.18 : 0.0));
    float ridged = pow(saturate(ridge(fine * 0.5 + 0.5)), isSelf ? 1.6 : 2.8);
    float amplitude = isSelf ? 0.20 : 0.105;
    float cavity = saturate(0.56 + broad * 0.34 + ridged * 0.30);
    return cavity * radius * amplitude;
}

float displacedSphereSdf(float3 p, float3 center, float radius, int index, bool isSelf)
{
    float3 local = p - center;
    float baseDistance = length(local) - radius;
    return baseDistance + planetCutDepth(local, radius, index, isSelf);
}

void primitiveInfo(int primitiveId, out int materialId, out float3 center, out float radius, out int index, out bool isSelf)
{
    if (primitiveId == 0)
    {
        materialId = 2;
        center = SUN_POSITION;
        radius = SUN_RADIUS;
        index = 17;
        isSelf = true;
    }
    else
    {
        int planetIndex = primitiveId - 1;
        materialId = 3;
        center = planetCenter(planetIndex);
        radius = planetRadius(planetIndex);
        index = planetIndex;
        isSelf = false;
    }
}

void primitiveDistances(float3 p, int primitiveId, out int materialId, out float hitDistance, out float stepDistance)
{
    float3 center;
    float radius;
    int index;
    bool isSelf;
    primitiveInfo(primitiveId, materialId, center, radius, index, isSelf);

    float3 local = p - center;
    float baseDistance = length(local) - radius;
    hitDistance = baseDistance + planetCutDepth(local, radius, index, isSelf);
    stepDistance = baseDistance;
}

int froxelIndexForPosition(float3 p)
{
    float2 xy = gridUv(p.xy);
    float z = (p.z - FROXEL_MIN_Z) / (FROXEL_MAX_Z - FROXEL_MIN_Z);

    if (xy.x < 0.0 || xy.x >= 1.0 || xy.y < 0.0 || xy.y >= 1.0 || z < 0.0 || z >= 1.0)
    {
        return -1;
    }

    int3 cell = int3(
        min((int)(xy.x * FROXEL_COUNT_X), FROXEL_COUNT_X - 1),
        min((int)(xy.y * FROXEL_COUNT_Y), FROXEL_COUNT_Y - 1),
        min((int)(z * FROXEL_COUNT_Z), FROXEL_COUNT_Z - 1));

    return cell.x + cell.y * FROXEL_COUNT_X + cell.z * FROXEL_COUNT_X * FROXEL_COUNT_Y;
}

void bodyDistances(float3 p, out int materialId, out float hitDistance, out float stepDistance)
{
    hitDistance = 100000.0;
    stepDistance = 100000.0;
    materialId = 0;

    int froxelIndex = froxelIndexForPosition(p);
    if (froxelIndex < 0)
    {
        return;
    }

    for (int slotGroup = 0; slotGroup < FROXEL_SLOT_COUNT; slotGroup++)
    {
        int4 ids = froxelPrimitiveIds[froxelIndex * FROXEL_SLOT_COUNT + slotGroup];

        for (int slot = 0; slot < 4; slot++)
        {
            int primitiveId = ids[slot];
            if (primitiveId >= 0)
            {
                int primitiveMaterialId;
                float primitiveHitDistance;
                float primitiveStepDistance;
                primitiveDistances(p, primitiveId, primitiveMaterialId, primitiveHitDistance, primitiveStepDistance);
                if (primitiveHitDistance < hitDistance)
                {
                    hitDistance = primitiveHitDistance;
                    materialId = primitiveMaterialId;
                }

                stepDistance = min(stepDistance, primitiveStepDistance);
            }
        }
    }
}

void bodyDistancesDirect(float3 p, out int materialId, out float hitDistance, out float stepDistance)
{
    hitDistance = 100000.0;
    stepDistance = 100000.0;
    materialId = 0;

    for (int primitiveId = 0; primitiveId < PRIMITIVE_COUNT; primitiveId++)
    {
        int primitiveMaterialId;
        float primitiveHitDistance;
        float primitiveStepDistance;
        primitiveDistances(p, primitiveId, primitiveMaterialId, primitiveHitDistance, primitiveStepDistance);
        if (primitiveHitDistance < hitDistance)
        {
            hitDistance = primitiveHitDistance;
            materialId = primitiveMaterialId;
        }

        stepDistance = min(stepDistance, primitiveStepDistance);
    }
}

float tracePrimitiveInterval(
    float3 rayOrigin,
    float3 rayDirection,
    int primitiveId,
    float currentTravel,
    out float entryTravel,
    out float hitTravel,
    out float exitTravel,
    out int materialId)
{
    entryTravel = currentTravel;
    hitTravel = currentTravel;
    exitTravel = currentTravel;
    materialId = 0;

    float3 center = float3(0.0, 0.0, 0.0);
    float radius = 0.0;
    int index = 0;
    bool isSelf = false;
    primitiveInfo(primitiveId, materialId, center, radius, index, isSelf);

    float3 offset = rayOrigin - center;
    float b = dot(offset, rayDirection);
    float c = dot(offset, offset) - radius * radius;
    float discriminant = b * b - c;
    if (discriminant < 0.0)
    {
        entryTravel = currentTravel;
        hitTravel = currentTravel;
        exitTravel = currentTravel;
        return 0.0;
    }

    float root = sqrt(discriminant);
    entryTravel = -b - root;
    exitTravel = -b + root;
    if (exitTravel < 0.0)
    {
        entryTravel = currentTravel;
        hitTravel = currentTravel;
        exitTravel = currentTravel;
        return 0.0;
    }

    entryTravel = max(entryTravel, currentTravel);
    if (exitTravel < currentTravel)
    {
        entryTravel = currentTravel;
        hitTravel = currentTravel;
        return 0.0;
    }

    float previousTravel = entryTravel;
    float previousDistance = displacedSphereSdf(rayOrigin + rayDirection * previousTravel, center, radius, index, isSelf);

    if (previousDistance <= 0.0)
    {
        hitTravel = previousTravel;
        return 1.0;
    }

    float lowTravel = previousTravel;
    float highTravel = previousTravel;
    bool foundBracket = false;
    for (int sampleIndex = 1; sampleIndex <= 8; sampleIndex++)
    {
        float sampleTravel = lerp(entryTravel, exitTravel, sampleIndex / 8.0);
        float sampleDistance = displacedSphereSdf(rayOrigin + rayDirection * sampleTravel, center, radius, index, isSelf);
        if (sampleDistance <= 0.0)
        {
            lowTravel = previousTravel;
            highTravel = sampleTravel;
            foundBracket = true;
            break;
        }

        previousTravel = sampleTravel;
        previousDistance = sampleDistance;
    }

    if (!foundBracket)
    {
        hitTravel = currentTravel;
        return 0.0;
    }

    for (int refineStep = 0; refineStep < 10; refineStep++)
    {
        float midTravel = (lowTravel + highTravel) * 0.5;
        float midDistance = displacedSphereSdf(rayOrigin + rayDirection * midTravel, center, radius, index, isSelf);
        if (midDistance > 0.0)
        {
            lowTravel = midTravel;
        }
        else
        {
            highTravel = midTravel;
        }
    }

    hitTravel = highTravel;
    return 1.0;
}

float traceNearestBodyInterval(
    float3 rayOrigin,
    float3 rayDirection,
    float currentTravel,
    out float entryTravel,
    out float hitTravel,
    out float exitTravel,
    out int materialId)
{
    entryTravel = 100000.0;
    hitTravel = 100000.0;
    exitTravel = currentTravel;
    materialId = 0;
    float hit = 0.0;
    float nearestEntry = 100000.0;
    float nearestExit = 100000.0;

    for (int primitiveId = 0; primitiveId < PRIMITIVE_COUNT; primitiveId++)
    {
        float primitiveEntryTravel;
        float primitiveHitTravel;
        float primitiveExitTravel;
        int primitiveMaterialId;
        float primitiveHit = tracePrimitiveInterval(
            rayOrigin,
            rayDirection,
            primitiveId,
            currentTravel,
            primitiveEntryTravel,
            primitiveHitTravel,
            primitiveExitTravel,
            primitiveMaterialId);

        if (primitiveExitTravel >= currentTravel && primitiveEntryTravel < nearestEntry)
        {
            nearestEntry = primitiveEntryTravel;
            nearestExit = primitiveExitTravel;
        }

        if (primitiveHit > 0.5 && primitiveHitTravel < hitTravel)
        {
            entryTravel = primitiveEntryTravel;
            hitTravel = primitiveHitTravel;
            exitTravel = primitiveExitTravel;
            materialId = primitiveMaterialId;
            hit = 1.0;
        }
    }

    if (hit < 0.5 && nearestExit < 99999.0)
    {
        entryTravel = nearestEntry;
        exitTravel = nearestExit;
    }

    return hit;
}

float bodySdf(float3 p, out int materialId)
{
    float hitDistance;
    float stepDistance;
    bodyDistances(p, materialId, hitDistance, stepDistance);
    return hitDistance;
}

float sceneSdf(float3 p, out int materialId)
{
    float terrain = p.z - terrainHeight(p.xy);
    float distanceToScene = terrain;
    materialId = 1;

    int bodyMaterialId;
    float body = bodySdf(p, bodyMaterialId);
    if (body < distanceToScene)
    {
        distanceToScene = body;
        materialId = bodyMaterialId;
    }

    return distanceToScene;
}

float sceneDistance(float3 p)
{
    int materialId;
    return sceneSdf(p, materialId);
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

void nearestBody(float3 p, out float3 center, out float radius, out int index, out bool isSelf)
{
    float bestDistance = abs(length(p - SUN_POSITION) - SUN_RADIUS);
    center = SUN_POSITION;
    radius = SUN_RADIUS;
    index = 17;
    isSelf = true;

    [unroll]
    for (int i = 0; i < PLANET_COUNT; i++)
    {
        float3 bodyCenter = planetCenter(i);
        float bodyRadius = planetRadius(i);
        float distanceToBody = abs(length(p - bodyCenter) - bodyRadius);
        if (distanceToBody < bestDistance)
        {
            bestDistance = distanceToBody;
            center = bodyCenter;
            radius = bodyRadius;
            index = i;
            isSelf = false;
        }
    }
}

float3 planetNormal(float3 p)
{
    float3 center;
    float radius;
    int index;
    bool isSelf;
    nearestBody(p, center, radius, index, isSelf);

    float normalStep = max(radius * 0.01, SURFACE_EPSILON * 3.0);
    float3 k0 = float3(1.0, -1.0, -1.0);
    float3 k1 = float3(-1.0, -1.0, 1.0);
    float3 k2 = float3(-1.0, 1.0, -1.0);
    float3 k3 = float3(1.0, 1.0, 1.0);
    float3 gradient =
        k0 * displacedSphereSdf(p + k0 * normalStep, center, radius, index, isSelf) +
        k1 * displacedSphereSdf(p + k1 * normalStep, center, radius, index, isSelf) +
        k2 * displacedSphereSdf(p + k2 * normalStep, center, radius, index, isSelf) +
        k3 * displacedSphereSdf(p + k3 * normalStep, center, radius, index, isSelf);

    float gradientLength = length(gradient);
    if (gradientLength < 0.00001)
    {
        return normalize(p - center);
    }

    return gradient / gradientLength;
}

float3 surfaceNormal(float3 p, int materialId)
{
    if (materialId == 1)
    {
        return terrainNormal(p);
    }

    return planetNormal(p);
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

    return saturate(minor * 0.72 + major) * terrainMask(p);
}

float terrainIsolines(float height)
{
    float contourDomain = height / TERRAIN_ISOLINE_SPACING;
    float contour = periodicLineMask(contourDomain, TERRAIN_ISOLINE_PIXEL_WIDTH, GRID_LINE_PIXEL_FADE);
    float contourDerivative = max(fwidth(contourDomain), 0.00001);
    float slopeFade = smoothstep(0.025, 0.25, contourDerivative);
    return contour * slopeFade;
}

float terrainFieldLines(float2 gradient)
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

bool raymarch(float3 rayOrigin, float3 rayDirection, out float3 hitPosition, out int materialId, out float travel)
{
    travel = 0.0;
    materialId = 0;
    float previousTravel = 0.0;
    float previousTerrainGap = rayOrigin.z - terrainHeight(rayOrigin.xy);

    [loop]
    for (int stepIndex = 0; stepIndex < 80; stepIndex++)
    {
        hitPosition = rayOrigin + rayDirection * travel;
        float fade = terrainMask(hitPosition.xy);
        if (fade <= 0.0001 && hitPosition.z < 4.0)
        {
            return false;
        }

        float hitEpsilon = max(SURFACE_EPSILON, travel * 0.00035);
        float bodyEntryTravel;
        float bodyHitTravel;
        float bodyExitTravel;
        int bodyMaterialId;
        float bodyHit = traceNearestBodyInterval(
            rayOrigin,
            rayDirection,
            travel,
            bodyEntryTravel,
            bodyHitTravel,
            bodyExitTravel,
            bodyMaterialId);

        if (bodyHit > 0.5)
        {
            travel = bodyHitTravel;
            hitPosition = rayOrigin + rayDirection * travel;
            materialId = bodyMaterialId;
            return true;
        }

        if (bodyEntryTravel < 99999.0 && bodyExitTravel > travel)
        {
            previousTravel = travel;
            previousTerrainGap = hitPosition.z - terrainHeight(hitPosition.xy);
            travel = bodyExitTravel + 0.004;
            continue;
        }

        float terrainGap = hitPosition.z - terrainHeight(hitPosition.xy);
        if (fade > 0.0001 && (terrainGap <= hitEpsilon || (previousTerrainGap > 0.0 && terrainGap <= 0.0)))
        {
            float alpha = previousTerrainGap / max(previousTerrainGap - terrainGap, 0.0001);
            travel = lerp(previousTravel, travel, saturate(alpha));
            hitPosition = rayOrigin + rayDirection * travel;
            materialId = 1;
            return true;
        }

        float2 terrainSlope = terrainGradient(hitPosition.xy);
        float terrainRate = abs(rayDirection.z - dot(terrainSlope, rayDirection.xy));
        float terrainStep = terrainGap > 0.0
            ? terrainGap / max(terrainRate, 0.22)
            : 0.026;

        terrainStep = min(terrainStep * 0.62, max(gridRadius * 0.08, 0.026));
        previousTravel = travel;
        previousTerrainGap = terrainGap;

        travel += max(terrainStep, 0.026);
        if (travel > farDistance)
        {
            return false;
        }
    }

    return false;
}

float3 shade(float3 position, float3 normal, int materialId, float3 rayDirection)
{
    float3 toSun = SUN_POSITION - position;
    float sunDistanceSq = max(dot(toSun, toSun), 0.001);
    float3 lightDirection = toSun * rsqrt(sunDistanceSq);
    float attenuation = 20.0 / sunDistanceSq;
    float nDotL = saturate(dot(normal, lightDirection));
    float3 sunColor = float3(1.0, 0.78, 0.34) * attenuation;

    if (materialId == 2)
    {
        float pulse = 0.92 + 0.08 * sin(timeSeconds * 1.7 + fbm(position.xy * 3.5) * 8.0);
        return float3(9.5, 6.3, 2.4) * pulse;
    }

    if (materialId == 3)
    {
        float3 albedo = float3(0.64, 0.86, 0.77);
        float rim = pow(saturate(1.0 + dot(normal, rayDirection)), 3.0);
        float spec = pow(saturate(dot(reflect(-lightDirection, normal), -rayDirection)), 48.0);
        return albedo * sunColor * nDotL + sunColor * spec * 0.32 + rim * float3(0.05, 0.12, 0.11);
    }

    float mask = terrainMask(position.xy);
    float gridAmount = gridLine(position.xy);
    float height = terrainHeight(position.xy);
    float2 gradient = terrainGradient(position.xy);
    float contour = terrainIsolines(height);
    float fieldLine = terrainFieldLines(gradient);
    float3 baseColor = gridWeatherColor(position.xy, position.z);
    float3 gridColor = float3(0.74, 1.0, 0.84) * gridAmount * 0.78;
    float3 contourColor = float3(0.98, 1.0, 0.78) * contour * 0.22;
    float3 fieldColor = float3(0.36, 0.92, 1.0) * fieldLine * 0.14;
    float3 lineColor = gridColor + contourColor + fieldColor;
    float diffuse = nDotL * attenuation * 0.85;
    float selfGlow = exp(-distance(position, SUN_POSITION) * 0.34) * 0.34;

    return (baseColor * (diffuse + selfGlow) + lineColor) * mask;
}

float3 aces(float3 color)
{
    const float a = 2.51;
    const float b = 0.03;
    const float c = 2.43;
    const float d = 0.59;
    const float e = 0.14;
    return saturate((color * (a * color + b)) / (color * (c * color + d) + e));
}

float4 AquariumPS(VertexOut input) : SV_Target
{
    float2 screenUv = float2(input.uv.x, 1.0 - input.uv.y);
    float2 pixel = screenUv * resolution;
    float2 ndc = (pixel * 2.0 - resolution) / resolution.y;

    float3 target = float3(gridCenter, 0.0);
    float3 forward = normalize(target - cameraPosition);
    float3 right = normalize(cross(forward, float3(0.0, 0.0, 1.0)));
    float3 up = cross(right, forward);
    float3 rayDirection = normalize(forward * 1.6 + right * ndc.x + up * ndc.y);

    float3 hitPosition;
    int materialId;
    float travel;
    float3 color = 0.0;

    if (raymarch(cameraPosition, rayDirection, hitPosition, materialId, travel))
    {
        float3 normal = surfaceNormal(hitPosition, materialId);
        color = shade(hitPosition, normal, materialId, rayDirection);
        color *= exp(-travel * 0.012);
    }

    color += float3(0.001, 0.003, 0.004);
    return float4(aces(color), 1.0);
}

float4 GridHeightPS(VertexOut input) : SV_Target
{
    float2 uv = saturate(input.uv);
    float2 world = gridWorld(uv);
    float height = analyticTerrainHeight(world);
    return float4(height, 0.0, 0.0, 1.0);
}
