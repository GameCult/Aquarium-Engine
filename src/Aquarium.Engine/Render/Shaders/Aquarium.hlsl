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
    float _pad0;
};

Texture2D<float4> gridHeightTexture : register(t0);
StructuredBuffer<int4> froxelPrimitiveIds : register(t1);
Texture2D<float> ditherTexture : register(t2);
Texture2D<float4> currentSceneTexture : register(t3);
Texture2D<float4> historyTexture : register(t4);
Texture2D<float4> currentSceneMetadataTexture : register(t5);
Texture2D<float4> historyMetadataTexture : register(t6);
Texture2D<float4> currentSceneControlTexture : register(t7);
Texture2D<float4> historyControlTexture : register(t8);
SamplerState gridSampler : register(s0);
SamplerState ditherSampler : register(s1);

static const float PI = 3.14159265359;
static const float SURFACE_EPSILON = 0.0015;
static const float3 SUN_POSITION = float3(0.0, 0.0, 2.2);
static const float SUN_RADIUS = 1.12;
static const float GRID_WEATHER_WORLD_SCALE = 42.0;
static const float GRID_LINE_WORLD_CELL = 2.0;
static const float GRID_MAJOR_LINE_WORLD_CELL = GRID_LINE_WORLD_CELL * 5.0;
static const float GRID_LINE_PIXEL_WIDTH = 0.46;
static const float GRID_MAJOR_LINE_PIXEL_WIDTH = 0.82;
static const float GRID_LINE_PIXEL_FADE = 0.95;
static const float TERRAIN_ISOLINE_SPACING = 0.12;
static const float TERRAIN_ISOLINE_PIXEL_WIDTH = 0.54;
static const float TERRAIN_FIELD_LINE_PIXEL_WIDTH = 0.38;
static const int CLOUD_FIELD_COUNT = 4;
static const float CLOUD_MIN_STEP = 0.085;
static const float CLOUD_MAX_STEP = 0.46;
static const float CLOUD_EMPTY_STEP_SCALE = 0.42;
static const int PLANET_COUNT = 5;
static const int PRIMITIVE_COUNT = PLANET_COUNT + 1;
static const float FIELD_ID_GRID = 1.0;
static const float FIELD_ID_SELF = 2.0;
static const float FIELD_ID_PLANET_BASE = 10.0;
static const float MAX_HISTORY_AGE = 32.0;
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

float3 planetCenterAt(int index, float sampleTime);

float stochasticTransparency(float2 screenUv, float alpha)
{
    float frameSeed = floor(timeSeconds * 60.0);
    float2 ditherUv = screenUv * (resolution / 512.0);
    float dither = frac(ditherTexture.SampleLevel(ditherSampler, ditherUv, 0.0).r + frameSeed * 1.61803398875);
    return alpha - dither - 0.001 * (1.0 - ceil(alpha));
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

float2 projectWorldToPreviousHistoryUv(float3 worldPosition)
{
    float3 forward;
    float3 right;
    float3 up;
    cameraBasis(previousCameraPosition, previousGridCenter, forward, right, up);

    float3 toPoint = worldPosition - previousCameraPosition;
    float forwardDistance = max(dot(toPoint, forward), 0.0001);
    float2 previousNdc = float2(dot(toPoint, right), dot(toPoint, up)) * (1.6 / forwardDistance);
    float2 previousPixel = (previousNdc * resolution.y + resolution) * 0.5 - previousJitterPixels;
    float2 previousScreenUv = previousPixel / resolution;
    return float2(previousScreenUv.x, 1.0 - previousScreenUv.y);
}

float3 temporalPreviousWorldPosition(float3 currentWorldPosition, float fieldId)
{
    if (fieldId >= FIELD_ID_PLANET_BASE && fieldId < FIELD_ID_PLANET_BASE + PLANET_COUNT)
    {
        int planetIndex = (int)(fieldId - FIELD_ID_PLANET_BASE + 0.5);
        float3 currentCenter = planetCenterAt(planetIndex, timeSeconds);
        float3 previousCenter = planetCenterAt(planetIndex, previousTimeSeconds);
        return currentWorldPosition - currentCenter + previousCenter;
    }

    return currentWorldPosition;
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

float3 planetCenterAt(int index, float sampleTime)
{
    float f = (float)index;
    float angle = f * 0.8975979 + sampleTime * (0.08 + 0.011 * f);
    float radius = 4.1 + f * 0.77;
    float2 xy = float2(cos(angle), sin(angle)) * radius;
    return float3(xy, 1.15 + planetRadius(index) * 0.72);
}

float3 planetCenter(int index)
{
    return planetCenterAt(index, timeSeconds);
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

float ellipsoidSdf(float3 p, float3 radius)
{
    float3 safeRadius = max(radius, 0.001);
    float normalizedDistance = length(p / safeRadius) - 1.0;
    return normalizedDistance * min(safeRadius.x, min(safeRadius.y, safeRadius.z));
}

void cloudFieldInfo(int index, out float3 center, out float3 radius, out float3 tint, out float densityScale)
{
    float slowTime = timeSeconds * 0.035;
    if (index == 0)
    {
        center = float3(gridCenter + float2(0.0, -1.4), 0.34);
        radius = float3(4.6, 1.55, 0.74);
        tint = float3(0.30, 0.86, 0.92);
        densityScale = 0.23;
    }
    else if (index == 1)
    {
        center = float3(gridCenter + float2(-3.8, 2.2), -0.52);
        radius = float3(2.15, 1.36, 0.58);
        tint = float3(0.24, 0.70, 0.58);
        densityScale = 0.31;
    }
    else if (index == 2)
    {
        center = float3(gridCenter + float2(3.25, 2.85), 1.18);
        radius = float3(1.34, 2.55, 0.86);
        tint = float3(0.88, 0.72, 0.42);
        densityScale = 0.26;
    }
    else
    {
        center = float3(gridCenter + float2(sin(slowTime) * 2.2, cos(slowTime * 0.7) * 2.6), 2.18);
        radius = float3(2.8, 1.68, 0.92);
        tint = float3(0.44, 0.62, 1.0);
        densityScale = 0.18;
    }
}

float cloudFieldAngle(int index)
{
    return index * 1.73 + timeSeconds * 0.018 * (index + 1);
}

float2 rotateCloudFieldXY(float2 value, int index)
{
    float angle = cloudFieldAngle(index);
    float c = cos(angle);
    float s = sin(angle);
    return float2(value.x * c - value.y * s, value.x * s + value.y * c);
}

float3 cloudFieldLocal(float3 p, int index, float3 center)
{
    float3 local = p - center;
    local.xy = rotateCloudFieldXY(local.xy, index);
    return local;
}

float cloudFieldSdf(float3 p, int index)
{
    float3 center;
    float3 radius;
    float3 tint;
    float densityScale;
    cloudFieldInfo(index, center, radius, tint, densityScale);

    return ellipsoidSdf(cloudFieldLocal(p, index, center), radius);
}

bool cloudRayInterval(float3 rayOrigin, float3 rayDirection, int index, out float enter, out float exit)
{
    float3 center;
    float3 radius;
    float3 tint;
    float densityScale;
    cloudFieldInfo(index, center, radius, tint, densityScale);

    float3 localOrigin = cloudFieldLocal(rayOrigin, index, center) / max(radius, 0.001);
    float3 localDirection = rayDirection;
    localDirection.xy = rotateCloudFieldXY(localDirection.xy, index);
    localDirection /= max(radius, 0.001);

    float a = dot(localDirection, localDirection);
    float b = 2.0 * dot(localOrigin, localDirection);
    float c = dot(localOrigin, localOrigin) - 1.0;
    float discriminant = b * b - 4.0 * a * c;
    if (discriminant < 0.0 || a <= 0.000001)
    {
        enter = 0.0;
        exit = 0.0;
        return false;
    }

    float root = sqrt(discriminant);
    float invDenominator = 0.5 / a;
    enter = (-b - root) * invDenominator;
    exit = (-b + root) * invDenominator;
    return exit > 0.0;
}

float cloudDensity(float3 p, int index, out float3 tint)
{
    float3 center;
    float3 radius;
    float densityScale;
    cloudFieldInfo(index, center, radius, tint, densityScale);

    float distanceToCloud = cloudFieldSdf(p, index);
    float boundary = smoothstep(0.0, 0.54, -distanceToCloud);
    if (boundary <= 0.0)
    {
        return 0.0;
    }

    float3 local = cloudFieldLocal(p, index, center) / max(radius, 0.001);
    float3 wind = float3(0.035, -0.018, 0.011) * timeSeconds;
    float broad = fbm3(local * 1.45 + wind + index * 7.1) * 0.5 + 0.5;
    float fine = fbm3(local * 4.6 + wind.yzx * 2.0 + index * 11.3) * 0.5 + 0.5;
    float erosion = saturate(broad * 0.72 + fine * 0.38);
    float softCore = smoothstep(0.42, 0.86, erosion);
    float featheredShell = boundary * boundary;
    return featheredShell * softCore * densityScale;
}

void cloudIntervalState(float3 rayOrigin, float3 rayDirection, float travel, out bool insideCloud, out float nextEnter, out float nextExit)
{
    insideCloud = false;
    nextEnter = 100000.0;
    nextExit = 100000.0;

    [unroll]
    for (int i = 0; i < CLOUD_FIELD_COUNT; i++)
    {
        float enter;
        float exit;
        if (cloudRayInterval(rayOrigin, rayDirection, i, enter, exit))
        {
            enter = max(enter, 0.0);
            if (travel >= enter && travel < exit)
            {
                insideCloud = true;
                nextExit = min(nextExit, exit);
            }
            else if (travel < enter)
            {
                nextEnter = min(nextEnter, enter);
            }
        }

    }
}

void sampleCloudMedium(float3 p, float3 rayDirection, out float density, out float3 scattering)
{
    density = 0.0;
    scattering = 0.0;

    [unroll]
    for (int i = 0; i < CLOUD_FIELD_COUNT; i++)
    {
        float3 tint;
        float fieldDensity = cloudDensity(p, i, tint);
        if (fieldDensity > 0.0001)
        {
            float3 toSun = SUN_POSITION - p;
            float sunDistanceSq = max(dot(toSun, toSun), 0.001);
            float3 lightDirection = toSun * rsqrt(sunDistanceSq);
            float forwardPhase = pow(saturate(dot(rayDirection, lightDirection) * 0.5 + 0.5), 3.0);
            float backPhase = pow(saturate(dot(-rayDirection, lightDirection) * 0.5 + 0.5), 2.0);
            float heightGlow = smoothstep(-1.2, 2.4, p.z);
            float selfLight = 11.0 / sunDistanceSq;
            float3 lightColor = float3(1.0, 0.76, 0.36) * selfLight;
            float3 ambientField = lerp(float3(0.02, 0.08, 0.10), tint * 0.22, heightGlow);
            float silver = forwardPhase * 0.72 + backPhase * 0.24;
            scattering += fieldDensity * (ambientField + lightColor * (0.18 + silver));
            density += fieldDensity * 0.62;
        }
    }

    density = min(density, 1.0);
}

void integrateCloudFields(float3 rayOrigin, float3 rayDirection, float maxTravel, out float3 cloudScattering, out float cloudTransmittance)
{
    cloudScattering = 0.0;
    cloudTransmittance = 1.0;

    float travel = 0.0;
    [loop]
    for (int stepIndex = 0; stepIndex < 144; stepIndex++)
    {
        if (travel >= maxTravel || cloudTransmittance < 0.025)
        {
            break;
        }

        bool insideCloud;
        float nextEnter;
        float nextExit;
        cloudIntervalState(rayOrigin, rayDirection, travel, insideCloud, nextEnter, nextExit);

        if (!insideCloud)
        {
            if (nextEnter >= 99999.0)
            {
                break;
            }

            travel = min(maxTravel, max(nextEnter, travel + CLOUD_MIN_STEP));
            continue;
        }

        float3 p = rayOrigin + rayDirection * travel;
        float density;
        float3 scattering;
        sampleCloudMedium(p, rayDirection, density, scattering);

        float segmentEnd = min(nextExit, maxTravel);
        float stepLength = min(CLOUD_MIN_STEP, segmentEnd - travel);

        if (density > 0.001)
        {
            float extinction = density * 0.58;
            float segmentTransmittance = exp(-extinction * stepLength);
            cloudScattering += cloudTransmittance * scattering * (1.0 - segmentTransmittance) / max(extinction, 0.001);
            cloudTransmittance *= segmentTransmittance;
        }

        travel += stepLength;
    }
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
    out int materialId,
    out int hitPrimitiveId)
{
    entryTravel = 100000.0;
    hitTravel = 100000.0;
    exitTravel = currentTravel;
    materialId = 0;
    hitPrimitiveId = -1;
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
            hitPrimitiveId = primitiveId;
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

    return saturate(minor * 0.58 + major) * terrainMask(p);
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
        int bodyPrimitiveId;
        float bodyHit = traceNearestBodyInterval(
            rayOrigin,
            rayDirection,
            travel,
            bodyEntryTravel,
            bodyHitTravel,
            bodyExitTravel,
            bodyMaterialId,
            bodyPrimitiveId);

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

bool raymarchBodies(float3 rayOrigin, float3 rayDirection, out float3 hitPosition, out int materialId, out int primitiveId, out float travel)
{
    float entryTravel;
    float exitTravel;
    float bodyHit = traceNearestBodyInterval(
        rayOrigin,
        rayDirection,
        0.0,
        entryTravel,
        travel,
        exitTravel,
        materialId,
        primitiveId);

    hitPosition = rayOrigin + rayDirection * travel;
    return bodyHit > 0.5 && travel <= farDistance;
}

bool traceGridSurface(float3 rayOrigin, float3 rayDirection, out float3 hitPosition, out float travel)
{
    travel = 0.0;
    float previousTravel = 0.0;
    float previousTerrainGap = rayOrigin.z - terrainHeight(rayOrigin.xy);

    [loop]
    for (int stepIndex = 0; stepIndex < 96; stepIndex++)
    {
        hitPosition = rayOrigin + rayDirection * travel;
        float fade = terrainMask(hitPosition.xy);
        if (fade <= 0.0001 && hitPosition.z < 4.0)
        {
            return false;
        }

        float terrainGap = hitPosition.z - terrainHeight(hitPosition.xy);
        float hitEpsilon = max(SURFACE_EPSILON, travel * 0.00035);
        if (fade > 0.0001 && (terrainGap <= hitEpsilon || (previousTerrainGap > 0.0 && terrainGap <= 0.0)))
        {
            float alpha = previousTerrainGap / max(previousTerrainGap - terrainGap, 0.0001);
            travel = lerp(previousTravel, travel, saturate(alpha));
            hitPosition = rayOrigin + rayDirection * travel;
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
    float3 gridColor = float3(0.74, 1.0, 0.84) * gridAmount * 0.56;
    float3 contourColor = float3(0.98, 1.0, 0.78) * contour * 0.16;
    float3 fieldColor = float3(0.36, 0.92, 1.0) * fieldLine * 0.10;
    float3 lineColor = gridColor + contourColor + fieldColor;
    float diffuse = nDotL * attenuation * 0.85;
    float selfGlow = exp(-distance(position, SUN_POSITION) * 0.34) * 0.34;

    return (baseColor * (diffuse + selfGlow) + lineColor) * mask;
}

float4 shadeGridOverlay(float3 position)
{
    float mask = terrainMask(position.xy);
    float gridAmount = gridLine(position.xy);
    float height = terrainHeight(position.xy);
    float2 gradient = terrainGradient(position.xy);
    float contour = terrainIsolines(height);
    float fieldLine = terrainFieldLines(gradient);
    float3 baseColor = gridWeatherColor(position.xy, position.z);
    float3 gridColor = float3(0.74, 1.0, 0.84) * gridAmount;
    float3 contourColor = float3(0.98, 1.0, 0.78) * contour;
    float3 fieldColor = float3(0.36, 0.92, 1.0) * fieldLine;
    float lineAlpha = saturate(gridAmount * 0.58 + contour * 0.22 + fieldLine * 0.16);
    float fieldAlpha = saturate((0.035 + length(gradient) * 0.16) * mask);
    float alpha = saturate((fieldAlpha + lineAlpha) * mask);
    float3 overlayColor = baseColor * fieldAlpha * 1.4 + gridColor * 0.9 + contourColor * 0.32 + fieldColor * 0.22;
    return float4(overlayColor, alpha);
}

float gridTemporalSupport(float3 position)
{
    float mask = terrainMask(position.xy);
    float gridAmount = gridLine(position.xy);
    float height = terrainHeight(position.xy);
    float2 gradient = terrainGradient(position.xy);
    float contour = terrainIsolines(height);
    float fieldLine = terrainFieldLines(gradient);
    return saturate(gridAmount * 0.58 + contour * 0.22 + fieldLine * 0.16) * mask;
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

float3 debugFieldIdColor(float fieldId)
{
    if (fieldId < 0.5)
    {
        return float3(0.0, 0.0, 0.0);
    }

    if (abs(fieldId - FIELD_ID_GRID) < 0.25)
    {
        return float3(0.0, 0.9, 1.0);
    }

    if (abs(fieldId - FIELD_ID_SELF) < 0.25)
    {
        return float3(1.0, 0.92, 0.25);
    }

    if (fieldId >= FIELD_ID_PLANET_BASE)
    {
        float phase = frac((fieldId - FIELD_ID_PLANET_BASE) * 0.37);
        return 0.35 + 0.65 * float3(
            0.5 + 0.5 * sin(phase * 6.28318 + 0.0),
            0.5 + 0.5 * sin(phase * 6.28318 + 2.1),
            0.5 + 0.5 * sin(phase * 6.28318 + 4.2));
    }

    return float3(1.0, 0.0, 1.0);
}

struct SceneOut
{
    float4 colorTravel : SV_Target0;
    float4 metadata : SV_Target1;
    float4 control : SV_Target2;
};

SceneOut AquariumScenePS(VertexOut input)
{
    float2 screenUv = float2(input.uv.x, 1.0 - input.uv.y);
    float2 pixel = screenUv * resolution;
    float3 rayDirection = rayDirectionForPixel(pixel, jitterPixels, cameraPosition, gridCenter);

    float3 hitPosition;
    int materialId;
    int primitiveId;
    float travel;
    float3 color = 0.0;
    float nearestSolidTravel = farDistance + 1.0;
    float outputTravel = farDistance + 1.0;
    float outputMaterialId = 0.0;
    float3 outputNormal = 0.0;
    float outputReactive = 0.0;
    float outputCoverage = 1.0;
    float outputMediumOpacity = 0.0;

    if (raymarchBodies(cameraPosition, rayDirection, hitPosition, materialId, primitiveId, travel))
    {
        float3 normal = surfaceNormal(hitPosition, materialId);
        color = shade(hitPosition, normal, materialId, rayDirection);
        color *= exp(-travel * 0.012);
        nearestSolidTravel = travel;
        outputTravel = travel;
        outputMaterialId = primitiveId == 0 ? FIELD_ID_SELF : FIELD_ID_PLANET_BASE + (float)(primitiveId - 1);
        outputNormal = normal;
    }

    float3 gridHitPosition;
    float gridTravel;
    if (traceGridSurface(cameraPosition, rayDirection, gridHitPosition, gridTravel) && gridTravel < nearestSolidTravel)
    {
        float4 gridOverlay = shadeGridOverlay(gridHitPosition);
        float gridCoverage = saturate(gridOverlay.a);
        float gridSupport = gridTemporalSupport(gridHitPosition);
        outputTravel = gridTravel;
        outputMaterialId = FIELD_ID_GRID;
        outputNormal = terrainNormal(gridHitPosition);
        outputCoverage = gridSupport;
        outputReactive = 0.0;
        if (stochasticTransparency(screenUv, gridOverlay.a) > 0.0)
        {
            color = gridOverlay.rgb;
        }
    }

    color += float3(0.001, 0.003, 0.004);

    SceneOut output;
    output.colorTravel = float4(color, min(outputTravel, farDistance + 1.0));
    output.metadata = float4(outputMaterialId, outputNormal);
    output.control = float4(outputReactive, outputCoverage, outputMediumOpacity, 0.0);
    return output;
}

float3 sampleCurrentScene(float2 uv)
{
    return currentSceneTexture.SampleLevel(gridSampler, uv, 0.0).rgb;
}

int2 pixelFromUv(float2 uv)
{
    return clamp((int2)floor(uv * resolution), int2(0, 0), (int2)resolution - int2(1, 1));
}

float4 sampleCurrentMetadata(float2 uv)
{
    return currentSceneMetadataTexture.Load(int3(pixelFromUv(uv), 0));
}

float4 sampleCurrentControl(float2 uv)
{
    return currentSceneControlTexture.Load(int3(pixelFromUv(uv), 0));
}

float4 sampleNearestHistoryColor(float2 uv)
{
    return historyTexture.Load(int3(pixelFromUv(uv), 0));
}

float4 sampleNearestHistoryMetadata(float2 uv)
{
    return historyMetadataTexture.Load(int3(pixelFromUv(uv), 0));
}

float4 sampleNearestHistoryControl(float2 uv)
{
    return historyControlTexture.Load(int3(pixelFromUv(uv), 0));
}

void currentNeighborhood(float2 uv, out float3 neighborhoodMin, out float3 neighborhoodMax)
{
    float2 texel = 1.0 / resolution;
    neighborhoodMin = 100000.0;
    neighborhoodMax = -100000.0;

    [unroll]
    for (int y = -1; y <= 1; y++)
    {
        [unroll]
        for (int x = -1; x <= 1; x++)
        {
            float3 sampleColor = sampleCurrentScene(uv + float2(x, y) * texel);
            neighborhoodMin = min(neighborhoodMin, sampleColor);
            neighborhoodMax = max(neighborhoodMax, sampleColor);
        }
    }
}

struct ResolveOut
{
    float4 finalColor : SV_Target0;
    float4 historyColor : SV_Target1;
    float4 historyMetadata : SV_Target2;
    float4 historyControl : SV_Target3;
};

ResolveOut AquariumResolvePS(VertexOut input)
{
    float2 screenUv = float2(input.uv.x, 1.0 - input.uv.y);
    float2 pixel = screenUv * resolution;
    float4 current = currentSceneTexture.SampleLevel(gridSampler, input.uv, 0.0);
    float currentTravel = current.a;
    float3 rawCurrentColor = current.rgb;
    float3 currentColor = current.rgb;
    float4 currentMetadata = sampleCurrentMetadata(input.uv);
    float4 currentControl = sampleCurrentControl(input.uv);
    float currentFieldId = currentMetadata.x;
    float3 currentNormal = currentMetadata.yzw;
    float currentReactive = saturate(currentControl.x);
    float currentCoverage = saturate(currentControl.y);
    float currentMediumOpacity = saturate(currentControl.z);
    bool currentIsGrid = abs(currentFieldId - FIELD_ID_GRID) < 0.25;

    if (currentIsGrid && currentTravel <= farDistance)
    {
        float3 currentRay = rayDirectionForPixel(pixel, jitterPixels, cameraPosition, gridCenter);
        float3 worldPosition = cameraPosition + currentRay * currentTravel;
        float4 gridOverlay = shadeGridOverlay(worldPosition);
        currentColor = gridOverlay.rgb * saturate(gridOverlay.a) + float3(0.001, 0.003, 0.004);
    }

    float historyWeight = 0.0;
    float historyAge = 0.0;
    float3 historyColor = currentColor;
    if (frameIndex > 0.5 && currentTravel <= farDistance && currentFieldId > 0.5)
    {
        float3 currentRay = rayDirectionForPixel(pixel, jitterPixels, cameraPosition, gridCenter);
        float3 worldPosition = cameraPosition + currentRay * currentTravel;
        float3 previousWorldPosition = temporalPreviousWorldPosition(worldPosition, currentFieldId);
        float2 previousUv = projectWorldToPreviousHistoryUv(previousWorldPosition);

        if (all(previousUv >= 0.0) && all(previousUv <= 1.0))
        {
            float4 previousMetadata = sampleNearestHistoryMetadata(previousUv);
            float previousFieldId = previousMetadata.x;
            bool isGridField = abs(currentFieldId - FIELD_ID_GRID) < 0.25;
            float4 previous = isGridField ? sampleNearestHistoryColor(previousUv) : historyTexture.SampleLevel(gridSampler, previousUv, 0.0);
            float4 previousControl = sampleNearestHistoryControl(previousUv);
            float previousTravel = previous.a;
            float3 previousNormal = previousMetadata.yzw;
            float previousCoverage = saturate(previousControl.y);
            float previousMediumOpacity = saturate(previousControl.z);
            float previousHistoryAge = max(previousControl.w, 0.0);
            float expectedPreviousTravel = distance(previousCameraPosition, previousWorldPosition);
            float travelDelta = abs(previousTravel - expectedPreviousTravel);
            float travelTolerance = max(0.045, expectedPreviousTravel * 0.018);
            float travelWeight = 1.0 - smoothstep(travelTolerance, travelTolerance * 4.0, travelDelta);
            float fieldWeight = abs(previousFieldId - currentFieldId) < 0.001 ? 1.0 : 0.0;
            float normalWeight = 0.0;
            if (dot(previousNormal, previousNormal) > 0.01 && dot(currentNormal, currentNormal) > 0.01)
            {
                normalWeight = smoothstep(0.68, 0.96, dot(normalize(previousNormal), normalize(currentNormal)));
            }

            float3 neighborhoodMin;
            float3 neighborhoodMax;
            currentNeighborhood(input.uv, neighborhoodMin, neighborhoodMax);
            float3 clampedHistory = isGridField ? previous.rgb : clamp(previous.rgb, neighborhoodMin, neighborhoodMax);
            float colorDelta = length(clampedHistory - currentColor);
            float colorWeight = isGridField ? 1.0 : 1.0 - smoothstep(0.18, 1.2, colorDelta);
            float reactiveWeight = 1.0 - currentReactive;
            float coverageWeight = isGridField ? smoothstep(0.02, 0.55, currentCoverage) : 1.0;
            float coverageContinuityWeight = 1.0 - smoothstep(0.10, 0.50, abs(previousCoverage - currentCoverage));
            float mediumContinuityWeight = 1.0 - smoothstep(0.04, 0.35, abs(previousMediumOpacity - currentMediumOpacity));
            float historyConfidence = smoothstep(0.0, 6.0, previousHistoryAge);
            float surfaceValidationWeight = travelWeight * colorWeight * fieldWeight * normalWeight * reactiveWeight * coverageWeight * coverageContinuityWeight * mediumContinuityWeight;
            float gridSupportWeight = smoothstep(0.015, 0.18, currentCoverage);
            float gridValidationWeight = travelWeight * fieldWeight * normalWeight * coverageContinuityWeight * mediumContinuityWeight * gridSupportWeight;
            float validationWeight = isGridField ? gridValidationWeight : surfaceValidationWeight;
            float maxHistoryWeight = isGridField ? 0.90 : 0.82;
            float freshHistoryScale = isGridField ? 0.58 : 0.35;

            historyColor = clampedHistory;
            historyWeight = maxHistoryWeight * lerp(freshHistoryScale, 1.0, historyConfidence) * validationWeight;
            historyAge = validationWeight > 0.01 ? min(previousHistoryAge + 1.0, MAX_HISTORY_AGE) : 0.0;
        }
    }

    float3 resolved = lerp(currentColor, historyColor, historyWeight);
    if (currentIsGrid)
    {
        resolved = currentColor;
    }

    float3 finalColor = aces(resolved);
    if (renderDebugMode > 0.5 && renderDebugMode < 1.5)
    {
        finalColor = aces(rawCurrentColor);
    }
    else if (renderDebugMode >= 1.5 && renderDebugMode < 2.5)
    {
        finalColor = aces(historyColor);
    }
    else if (renderDebugMode >= 2.5 && renderDebugMode < 3.5)
    {
        finalColor = historyAge / MAX_HISTORY_AGE;
    }
    else if (renderDebugMode >= 3.5 && renderDebugMode < 4.5)
    {
        finalColor = historyWeight.xxx;
    }
    else if (renderDebugMode >= 4.5 && renderDebugMode < 5.5)
    {
        finalColor = float3(currentReactive, currentCoverage, currentMediumOpacity);
    }
    else if (renderDebugMode >= 5.5 && renderDebugMode < 6.5)
    {
        finalColor = debugFieldIdColor(currentFieldId);
    }

    ResolveOut output;
    output.finalColor = float4(finalColor, 1.0);
    output.historyColor = float4(resolved, currentTravel);
    output.historyMetadata = currentMetadata;
    output.historyControl = float4(currentControl.xyz, historyAge);
    return output;
}

float4 GridHeightPS(VertexOut input) : SV_Target
{
    float2 uv = saturate(input.uv);
    float2 world = gridWorld(uv);
    float height = analyticTerrainHeight(world);
    return float4(height, 0.0, 0.0, 1.0);
}
