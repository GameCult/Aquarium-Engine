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

Texture2D<float4> sourceTexture : register(t0);
Texture2D<float4> gridHeightTexture : register(t1);
Texture2D<float4> historyTexture : register(t4);
Texture2D<float4> currentSceneMetadataTexture : register(t5);
Texture2D<float4> historyMetadataTexture : register(t6);
Texture2D<float4> currentSceneControlTexture : register(t7);
Texture2D<float4> historyControlTexture : register(t8);
Texture2D<float4> bloomTexture0 : register(t9);
Texture2D<float4> bloomTexture1 : register(t10);
Texture2D<float4> bloomTexture2 : register(t11);
SamplerState sourceSampler : register(s0);

struct FieldInstance
{
    float4 centerRadius;
    float4 radiusAngle;
    float4 fieldFlags;
    float4 materialMedium;
    float4 colorIntensity;
    float4 mediumTerms;
};

StructuredBuffer<FieldInstance> fieldInstances : register(t12);

struct VertexOut
{
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
};

struct ResolveOut
{
    float4 finalColor : SV_Target0;
    float4 historyColor : SV_Target1;
    float4 historyMetadata : SV_Target2;
    float4 historyControl : SV_Target3;
};

static const float SUN_RADIUS = 1.12;
static const int PLANET_COUNT = 5;
static const float FIELD_ID_GRID = 1.0;
static const float FIELD_ID_SELF = 2.0;
static const float FIELD_ID_MEDIUM = 3.0;
static const float FIELD_ID_PLANET_BASE = 10.0;
static const float MAX_HISTORY_AGE = 32.0;
static const int FIELD_INSTANCE_COUNT = 10;
static const int FIELD_FLAG_CLOUD = 2;
static const int MEDIUM_RAY_PREVIEW_STEPS = 48;
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

VertexOut FullscreenTriangleVS(uint vertexId : SV_VertexID)
{
    float2 uv = float2((vertexId << 1) & 2, vertexId & 2);
    VertexOut output;
    output.position = float4(uv * float2(2.0, -2.0) + float2(-1.0, 1.0), 0.0, 1.0);
    output.uv = uv;
    return output;
}

float luminance(float3 color)
{
    return dot(color, float3(0.2126, 0.7152, 0.0722));
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

    if (abs(fieldId - 1.0) < 0.25)
    {
        return float3(0.0, 0.9, 1.0);
    }

    if (abs(fieldId - 2.0) < 0.25)
    {
        return float3(1.0, 0.92, 0.25);
    }

    if (abs(fieldId - FIELD_ID_MEDIUM) < 0.25)
    {
        return float3(0.28, 0.72, 1.0);
    }

    if (fieldId >= 10.0)
    {
        float phase = frac((fieldId - 10.0) * 0.37);
        return 0.35 + 0.65 * float3(
            0.5 + 0.5 * sin(phase * 6.28318 + 0.0),
            0.5 + 0.5 * sin(phase * 6.28318 + 2.1),
            0.5 + 0.5 * sin(phase * 6.28318 + 4.2));
    }

    return float3(1.0, 0.0, 1.0);
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

float3 temporalPreviousWorldPosition(float3 worldPosition, float fieldId)
{
    if (fieldId >= FIELD_ID_PLANET_BASE)
    {
        int planetIndex = clamp((int)round(fieldId - FIELD_ID_PLANET_BASE), 0, PLANET_COUNT - 1);
        float3 currentCenter = planetCenterAt(planetIndex, timeSeconds);
        float3 previousCenter = planetCenterAt(planetIndex, previousTimeSeconds);
        return previousCenter + (worldPosition - currentCenter);
    }

    return worldPosition;
}

float2 projectWorldToPreviousHistoryUv(float3 worldPosition)
{
    float3 forward;
    float3 right;
    float3 up;
    cameraBasis(previousCameraPosition, previousGridCenter, forward, right, up);
    float3 delta = worldPosition - previousCameraPosition;
    float z = max(dot(delta, forward), 0.0001);
    float2 ndc = float2(dot(delta, right), dot(delta, up)) / z * 1.6;
    float2 pixel = (ndc * resolution.y + resolution) * 0.5 - previousJitterPixels;
    return float2(pixel.x / resolution.x, 1.0 - pixel.y / resolution.y);
}

int2 pixelFromUv(float2 uv)
{
    return clamp((int2)floor(uv * resolution), int2(0, 0), (int2)resolution - int2(1, 1));
}

float4 loadCurrentMetadata(float2 uv)
{
    return currentSceneMetadataTexture.Load(int3(pixelFromUv(uv), 0));
}

float4 loadCurrentControl(float2 uv)
{
    return currentSceneControlTexture.Load(int3(pixelFromUv(uv), 0));
}

float4 loadHistoryColor(float2 uv)
{
    return historyTexture.Load(int3(pixelFromUv(uv), 0));
}

float4 loadHistoryMetadata(float2 uv)
{
    return historyMetadataTexture.Load(int3(pixelFromUv(uv), 0));
}

float4 loadHistoryControl(float2 uv)
{
    return historyControlTexture.Load(int3(pixelFromUv(uv), 0));
}

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
    return gridHeightTexture.SampleLevel(sourceSampler, saturate(gridUv(p)), 0.0).r;
}

float2 terrainGradient(float2 p)
{
    float2 uv = saturate(gridUv(p));
    float2 texel = 1.0 / GRID_HEIGHT_TEXEL_COUNT;
    float texelWorld = max((gridRadius * 2.0) / GRID_HEIGHT_TEXEL_COUNT, 0.001);

    float hLeft = gridHeightTexture.SampleLevel(sourceSampler, uv - float2(texel.x, 0.0), 0.0).r;
    float hRight = gridHeightTexture.SampleLevel(sourceSampler, uv + float2(texel.x, 0.0), 0.0).r;
    float hDown = gridHeightTexture.SampleLevel(sourceSampler, uv - float2(0.0, texel.y), 0.0).r;
    float hUp = gridHeightTexture.SampleLevel(sourceSampler, uv + float2(0.0, texel.y), 0.0).r;

    return float2(hRight - hLeft, hUp - hDown) / (texelWorld * 2.0);
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
            float3 sampleColor = sourceTexture.SampleLevel(sourceSampler, uv + float2(x, y) * texel, 0.0).rgb;
            neighborhoodMin = min(neighborhoodMin, sampleColor);
            neighborhoodMax = max(neighborhoodMax, sampleColor);
        }
    }
}

float3 bloomColorAt(float2 uv)
{
    return bloomTexture0.SampleLevel(sourceSampler, uv, 0.0).rgb * 0.42 +
        bloomTexture1.SampleLevel(sourceSampler, uv, 0.0).rgb * 0.34 +
        bloomTexture2.SampleLevel(sourceSampler, uv, 0.0).rgb * 0.24;
}

float3 presentColor(float3 scene, float2 uv)
{
    float3 bloom = bloomColorAt(uv);
    float3 exposedScene = scene * max(exposure, 0.001);
    return aces(exposedScene + bloom * bloomIntensity + luminance(bloom) * bloomVeilIntensity);
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

float2 rotate2(float2 value, float angle)
{
    float s = sin(angle);
    float c = cos(angle);
    return float2(value.x * c - value.y * s, value.x * s + value.y * c);
}

float ellipsoidSdf(float3 p, float3 radius)
{
    float3 safeRadius = max(radius, 0.001);
    float normalizedDistance = length(p / safeRadius) - 1.0;
    return normalizedDistance * min(safeRadius.x, min(safeRadius.y, safeRadius.z));
}

float fieldDistance(float3 p, FieldInstance field)
{
    float3 local = p - field.centerRadius.xyz;
    local.xy = rotate2(local.xy, -field.radiusAngle.w);
    return ellipsoidSdf(local, max(field.radiusAngle.xyz, 0.001));
}

float registeredMediumDensity(float3 p)
{
    float density = 0.0;

    [unroll]
    for (int i = 0; i < FIELD_INSTANCE_COUNT; i++)
    {
        FieldInstance field = fieldInstances[i];
        if (((int)(field.fieldFlags.y + 0.5) & FIELD_FLAG_CLOUD) == 0)
        {
            continue;
        }

        float distanceToField = fieldDistance(p, field);
        float shell = smoothstep(0.0, 0.42, -distanceToField);
        if (shell <= 0.0)
        {
            continue;
        }

        float3 local = p - field.centerRadius.xyz;
        local.xy = rotate2(local.xy, -field.radiusAngle.w);
        local /= max(field.radiusAngle.xyz, 0.001);
        float erosion = saturate(0.86 + fbm3(local * 3.4) * 0.14);
        float core = 1.0 - smoothstep(0.80, 1.05, length(local));
        density += shell * core * erosion * field.mediumTerms.w;
    }

    return saturate(density);
}

void previewRegisteredMediumStep(float3 rayOrigin, float3 rayDirection, float maxTravel, float requestedStep, out float stepDensity, out float transmittance)
{
    stepDensity = 0.0;
    transmittance = 1.0;
    float stepLength = maxTravel / (float)MEDIUM_RAY_PREVIEW_STEPS;
    int selectedStep = clamp((int)round(requestedStep), 0, MEDIUM_RAY_PREVIEW_STEPS - 1);

    [loop]
    for (int stepIndex = 0; stepIndex < MEDIUM_RAY_PREVIEW_STEPS; stepIndex++)
    {
        float travel = ((float)stepIndex + 0.5) * stepLength;
        float3 p = rayOrigin + rayDirection * travel;
        float density = registeredMediumDensity(p);
        if (stepIndex == selectedStep)
        {
            stepDensity = density;
            return;
        }

        transmittance *= exp(-density * 0.16 * stepLength);
    }
}

float4 D3D12BloomPrefilterPS(VertexOut input) : SV_Target0
{
    return float4(sourceTexture.SampleLevel(sourceSampler, input.uv, 0.0).rgb * max(exposure, 0.001), 1.0);
}

float4 D3D12BloomDownsamplePS(VertexOut input) : SV_Target0
{
    float3 sumColor = 0.0;
    float sumWeight = 0.0;
    uint sourceWidth;
    uint sourceHeight;
    sourceTexture.GetDimensions(sourceWidth, sourceHeight);
    float2 texel = 1.0 / float2(max(sourceWidth, 1), max(sourceHeight, 1));

    [unroll]
    for (int y = -1; y <= 1; y++)
    {
        [unroll]
        for (int x = -1; x <= 1; x++)
        {
            float3 sampleColor = sourceTexture.SampleLevel(sourceSampler, input.uv + float2(x, y) * texel, 0.0).rgb;
            float weight = rcp(1.0 + luminance(sampleColor) * 0.12);
            sumColor += sampleColor * weight;
            sumWeight += weight;
        }
    }

    return float4(sumColor / max(sumWeight, 0.0001), 1.0);
}

float4 D3D12BloomBlurHorizontalPS(VertexOut input) : SV_Target0
{
    uint sourceWidth;
    uint sourceHeight;
    sourceTexture.GetDimensions(sourceWidth, sourceHeight);
    float2 texel = float2(1.0 / max(sourceWidth, 1), 0.0);
    float3 color =
        sourceTexture.SampleLevel(sourceSampler, input.uv - texel * 2.0, 0.0).rgb * 0.06136 +
        sourceTexture.SampleLevel(sourceSampler, input.uv - texel, 0.0).rgb * 0.24477 +
        sourceTexture.SampleLevel(sourceSampler, input.uv, 0.0).rgb * 0.38774 +
        sourceTexture.SampleLevel(sourceSampler, input.uv + texel, 0.0).rgb * 0.24477 +
        sourceTexture.SampleLevel(sourceSampler, input.uv + texel * 2.0, 0.0).rgb * 0.06136;
    return float4(color, 1.0);
}

float4 D3D12BloomBlurVerticalPS(VertexOut input) : SV_Target0
{
    uint sourceWidth;
    uint sourceHeight;
    sourceTexture.GetDimensions(sourceWidth, sourceHeight);
    float2 texel = float2(0.0, 1.0 / max(sourceHeight, 1));
    float3 color =
        sourceTexture.SampleLevel(sourceSampler, input.uv - texel * 2.0, 0.0).rgb * 0.06136 +
        sourceTexture.SampleLevel(sourceSampler, input.uv - texel, 0.0).rgb * 0.24477 +
        sourceTexture.SampleLevel(sourceSampler, input.uv, 0.0).rgb * 0.38774 +
        sourceTexture.SampleLevel(sourceSampler, input.uv + texel, 0.0).rgb * 0.24477 +
        sourceTexture.SampleLevel(sourceSampler, input.uv + texel * 2.0, 0.0).rgb * 0.06136;
    return float4(color, 1.0);
}

ResolveOut D3D12ResolvePS(VertexOut input)
{
    float2 screenUv = float2(input.uv.x, 1.0 - input.uv.y);
    float2 pixel = screenUv * resolution;
    float4 current = sourceTexture.SampleLevel(sourceSampler, input.uv, 0.0);
    float currentTravel = current.a;
    float3 rawCurrentColor = current.rgb;
    float3 currentColor = current.rgb;
    float4 currentMetadata = loadCurrentMetadata(input.uv);
    float4 currentControl = loadCurrentControl(input.uv);
    float currentFieldId = currentMetadata.x;
    float3 currentNormal = currentMetadata.yzw;
    float currentReactive = saturate(currentControl.x);
    float currentCoverage = saturate(currentControl.y);
    float currentMediumOpacity = saturate(currentControl.z);
    bool currentIsGrid = abs(currentFieldId - FIELD_ID_GRID) < 0.25;
    bool currentIsMedium = abs(currentFieldId - FIELD_ID_MEDIUM) < 0.25;
    bool currentIsDistributed = currentIsMedium || currentIsGrid;

    if (currentIsGrid && currentTravel <= farDistance)
    {
        float3 currentRay = rayDirectionForPixel(pixel, jitterPixels, cameraPosition, gridCenter);
        float3 worldPosition = cameraPosition + currentRay * currentTravel;
        float gridAlpha;
        float3 gridColor = gridEventColor(worldPosition, gridAlpha);
        currentColor = gridColor * saturate(gridAlpha) + float3(0.001, 0.003, 0.004);
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
            float4 previousMetadata = loadHistoryMetadata(previousUv);
            float4 previousControl = loadHistoryControl(previousUv);
            float4 previous = currentIsGrid ? loadHistoryColor(previousUv) : historyTexture.SampleLevel(sourceSampler, previousUv, 0.0);
            float previousFieldId = previousMetadata.x;
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
            if (currentIsDistributed)
            {
                normalWeight = 1.0;
            }
            else if (dot(previousNormal, previousNormal) > 0.01 && dot(currentNormal, currentNormal) > 0.01)
            {
                normalWeight = smoothstep(0.68, 0.96, dot(normalize(previousNormal), normalize(currentNormal)));
            }

            float3 neighborhoodMin;
            float3 neighborhoodMax;
            currentNeighborhood(input.uv, neighborhoodMin, neighborhoodMax);
            float3 clampedHistory = currentIsDistributed ? previous.rgb : clamp(previous.rgb, neighborhoodMin, neighborhoodMax);
            float colorDelta = length(clampedHistory - currentColor);
            float colorWeight = currentIsGrid ? 1.0 : currentIsDistributed ? 1.0 - smoothstep(0.35, 1.6, colorDelta) : 1.0 - smoothstep(0.18, 1.2, colorDelta);
            float reactiveWeight = 1.0 - currentReactive;
            float coverageWeight = currentIsGrid ? 1.0 : smoothstep(0.02, 0.55, currentCoverage);
            float coverageContinuityWeight = 1.0 - smoothstep(0.10, 0.50, abs(previousCoverage - currentCoverage));
            float mediumContinuityWeight = 1.0 - smoothstep(0.04, 0.35, abs(previousMediumOpacity - currentMediumOpacity));
            float historyConfidence = smoothstep(0.0, 6.0, previousHistoryAge);
            float surfaceValidationWeight = travelWeight * colorWeight * fieldWeight * normalWeight * reactiveWeight * coverageWeight * coverageContinuityWeight * mediumContinuityWeight;
            float gridSupportWeight = smoothstep(0.015, 0.18, currentCoverage);
            float gridValidationWeight = travelWeight * fieldWeight * normalWeight * coverageContinuityWeight * mediumContinuityWeight * gridSupportWeight;
            float validationWeight = currentIsGrid ? gridValidationWeight : surfaceValidationWeight;

            historyColor = clampedHistory;
            float maxHistoryWeight = currentIsGrid ? 0.90 : currentIsMedium ? 0.88 : 0.82;
            float freshHistoryScale = currentIsGrid ? 0.58 : currentIsDistributed ? 0.48 : 0.35;
            historyWeight = maxHistoryWeight * lerp(freshHistoryScale, 1.0, historyConfidence) * validationWeight;
            historyAge = validationWeight > 0.01 ? min(previousHistoryAge + 1.0, MAX_HISTORY_AGE) : 0.0;
        }
    }

    float3 resolved = lerp(currentColor, historyColor, historyWeight);
    float3 finalColor = presentColor(resolved, input.uv);
    if (renderDebugMode > 0.5 && renderDebugMode < 1.5)
    {
        finalColor = aces(rawCurrentColor * max(exposure, 0.001));
    }
    else if (renderDebugMode >= 1.5 && renderDebugMode < 2.5)
    {
        finalColor = aces(historyColor * max(exposure, 0.001));
    }
    else if (renderDebugMode >= 2.5 && renderDebugMode < 3.5)
    {
        finalColor = (historyAge / MAX_HISTORY_AGE).xxx;
    }
    else if (renderDebugMode >= 3.5 && renderDebugMode < 4.5)
    {
        finalColor = historyWeight.xxx;
    }
    else if (renderDebugMode >= 4.5 && renderDebugMode < 5.5)
    {
        finalColor = saturate(currentControl.xyz);
    }
    else if (renderDebugMode >= 5.5 && renderDebugMode < 6.5)
    {
        finalColor = debugFieldIdColor(currentFieldId);
    }
    else if (renderDebugMode >= 6.5 && renderDebugMode < 7.5)
    {
        float3 bloom = bloomColorAt(input.uv);
        finalColor = aces(bloom * bloomIntensity + luminance(bloom) * bloomVeilIntensity);
    }
    else if (renderDebugMode >= 7.5 && renderDebugMode < 8.5)
    {
        float luma = luminance(currentColor * max(exposure, 0.001));
        finalColor = luma.xxx;
    }
    else if (renderDebugMode >= 8.5 && renderDebugMode < 9.5)
    {
        float3 debugRay = rayDirectionForPixel(pixel, jitterPixels, cameraPosition, gridCenter);
        float stepDensity;
        float stepTransmittance;
        previewRegisteredMediumStep(cameraPosition, debugRay, farDistance, mediumDebugStep, stepDensity, stepTransmittance);
        finalColor = lerp(float3(0.006, 0.016, 0.026), float3(0.32, 0.86, 1.0), saturate(stepDensity * 3.0));
    }
    else if (renderDebugMode >= 9.5 && renderDebugMode < 10.5)
    {
        float3 debugRay = rayDirectionForPixel(pixel, jitterPixels, cameraPosition, gridCenter);
        float stepDensity;
        float stepTransmittance;
        previewRegisteredMediumStep(cameraPosition, debugRay, farDistance, mediumDebugStep, stepDensity, stepTransmittance);
        finalColor = lerp(float3(0.10, 0.02, 0.01), float3(0.72, 1.0, 0.86), saturate(stepTransmittance));
    }

    ResolveOut output;
    output.finalColor = float4(finalColor, 1.0);
    output.historyColor = float4(resolved, currentTravel);
    output.historyMetadata = currentMetadata;
    output.historyControl = float4(currentControl.xyz, historyAge);
    return output;
}
