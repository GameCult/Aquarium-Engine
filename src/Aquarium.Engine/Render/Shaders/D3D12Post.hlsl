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

Texture2D<float4> sourceTexture : register(t0);
Texture2D<float4> historyTexture : register(t4);
Texture2D<float4> currentSceneMetadataTexture : register(t5);
Texture2D<float4> historyMetadataTexture : register(t6);
Texture2D<float4> currentSceneControlTexture : register(t7);
Texture2D<float4> historyControlTexture : register(t8);
Texture2D<float4> bloomTexture0 : register(t9);
Texture2D<float4> bloomTexture1 : register(t10);
Texture2D<float4> bloomTexture2 : register(t11);
Texture2D<float4> currentEventColorTexture : register(t18);
Texture2D<float4> currentEventMetadataTexture : register(t19);
Texture2D<float4> historyEventColorTexture : register(t20);
Texture2D<float4> historyEventMetadataTexture : register(t21);
SamplerState sourceSampler : register(s0);

struct AgentVisual
{
    float4 centerRadius;
    float4 previousCenterRole;
    float4 state;
    float4 lodIndexFlags;
};

StructuredBuffer<AgentVisual> agentVisuals : register(t24);

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
    float4 historyEventColor : SV_Target4;
    float4 historyEventMetadata : SV_Target5;
};

static const float SUN_RADIUS = 1.12;
static const int AGENT_VISUAL_COUNT = 5;
static const float FIELD_ID_SELF = 2.0;
static const float FIELD_ID_GRID = 4.0;
static const float FIELD_ID_CURSOR = 5.0;
static const float FIELD_ID_AGENT_BASE = 10.0;
static const float BODY_GRID_CLEARANCE_RADIUS_SCALE = 2.0;
static const float SELF_GRAVITY_RADIUS = 17.0;
static const float MAX_HISTORY_AGE = 32.0;
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

    if (abs(fieldId - FIELD_ID_CURSOR) < 0.25)
    {
        return float3(0.78, 0.24, 1.0);
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

float2 planetAnchorAt(int index, float sampleTime)
{
    float f = (float)index;
    float angle = f * 0.8975979 + sampleTime * (0.08 + 0.011 * f);
    float radius = 4.1 + f * 0.77;
    return float2(cos(angle), sin(angle)) * radius;
}

float powerPulse(float distanceValue, float radius, float power)
{
    float normalized = saturate(distanceValue / max(radius, 0.001));
    float shaped = pow(1.0 - normalized, power);
    return shaped * shaped * (3.0 - 2.0 * shaped);
}

float gridBrushHeight(float2 world, float2 center, float radius, float power, float amplitude, float waveAmplitude, float waveFrequency, float waveSpeed, float waveSinePower, float sampleTime)
{
    float distanceValue = length(world - center);
    if (distanceValue > radius)
    {
        return 0.0;
    }

    float well = powerPulse(distanceValue, radius, power);
    float normalizedDistance = saturate(distanceValue / max(radius, 0.001));
    float legacyPhase = distanceValue * waveFrequency - sampleTime * waveSpeed;
    float radialPhase = pow(normalizedDistance, waveSinePower) * waveFrequency - sampleTime * waveSpeed;
    float ripple = waveSinePower > 0.0 ? cos(radialPhase) : sin(legacyPhase);
    return amplitude * well + ripple * well * waveAmplitude;
}

float gridHeightAt(float2 world, float sampleTime)
{
    float height = sin((world.x * 0.08 + world.y * 0.06) + sampleTime * 0.27)
        * sin((world.x * -0.04 + world.y * 0.07) - sampleTime * 0.19) * 0.035;
    height += gridBrushHeight(world, 0.0, SELF_GRAVITY_RADIUS, 2.85, -1.34, 0.18, 6.28318530718, 0.82, 1.25, sampleTime);

    [unroll]
    for (int index = 0; index < AGENT_VISUAL_COUNT; index++)
    {
        float radius = planetRadius(index);
        height += gridBrushHeight(world, planetAnchorAt(index, sampleTime), 3.8 + radius * 2.5, 2.1, -0.42, 0.022, 2.4, 1.35, 0.0, sampleTime);
    }

    return height;
}

float3 bodyCenterAtGridHeight(float2 xy, float radius, float sampleTime)
{
    return float3(xy, gridHeightAt(xy, sampleTime) + radius * BODY_GRID_CLEARANCE_RADIUS_SCALE);
}

float3 planetCenterAt(int index, float sampleTime)
{
    return bodyCenterAtGridHeight(planetAnchorAt(index, sampleTime), planetRadius(index), sampleTime);
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
    if (fieldId >= FIELD_ID_AGENT_BASE)
    {
        int agentIndex = clamp((int)round(fieldId - FIELD_ID_AGENT_BASE), 0, AGENT_VISUAL_COUNT - 1);
        float3 currentCenter = agentVisuals[agentIndex].centerRadius.xyz;
        float3 previousCenter = agentVisuals[agentIndex].previousCenterRole.xyz;
        return previousCenter + (worldPosition - currentCenter);
    }

    if (abs(fieldId - FIELD_ID_CURSOR) < 0.25)
    {
        float3 currentCenter = bodyCenterAtGridHeight(cursorWorlds.xy, 0.56, timeSeconds);
        float3 previousCenter = bodyCenterAtGridHeight(cursorWorlds.zw, 0.56, previousTimeSeconds);
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

float4 loadCurrentEventColor(float2 uv)
{
    return currentEventColorTexture.Load(int3(pixelFromUv(uv), 0));
}

float4 loadCurrentEventMetadata(float2 uv)
{
    return currentEventMetadataTexture.Load(int3(pixelFromUv(uv), 0));
}

float4 loadHistoryEventColor(float2 uv)
{
    return historyEventColorTexture.Load(int3(pixelFromUv(uv), 0));
}

float4 loadHistoryEventMetadata(float2 uv)
{
    return historyEventMetadataTexture.Load(int3(pixelFromUv(uv), 0));
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
    float4 currentEventColor = loadCurrentEventColor(input.uv);
    float4 currentEventMetadata = loadCurrentEventMetadata(input.uv);
    float currentEventFieldId = currentEventMetadata.x;
    float currentEventTravel = currentEventMetadata.y;
    float currentEventCoverage = saturate(currentEventMetadata.z);
    float3 currentEventRadiance = currentEventColor.rgb;

    float historyWeight = 0.0;
    float historyAge = 0.0;
    float3 historyColor = currentColor;
    float eventHistoryWeight = 0.0;
    float eventHistoryAge = 0.0;
    float3 eventHistoryColor = currentEventRadiance;
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
            float4 previous = historyTexture.SampleLevel(sourceSampler, previousUv, 0.0);
            float previousFieldId = previousMetadata.x;
            float previousTravel = previous.a;
            float3 previousNormal = previousMetadata.yzw;
            float previousCoverage = saturate(previousControl.y);
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
            float3 clampedHistory = clamp(previous.rgb, neighborhoodMin, neighborhoodMax);
            float colorDelta = length(clampedHistory - currentColor);
            float colorWeight = 1.0 - smoothstep(0.18, 1.2, colorDelta);
            float reactiveWeight = 1.0 - currentReactive;
            float coverageWeight = smoothstep(0.02, 0.55, currentCoverage);
            float coverageContinuityWeight = 1.0 - smoothstep(0.10, 0.50, abs(previousCoverage - currentCoverage));
            float historyConfidence = smoothstep(0.0, 6.0, previousHistoryAge);
            float validationWeight = travelWeight * colorWeight * fieldWeight * normalWeight * reactiveWeight * coverageWeight * coverageContinuityWeight;

            historyColor = clampedHistory;
            historyWeight = 0.82 * lerp(0.35, 1.0, historyConfidence) * validationWeight;
            historyAge = validationWeight > 0.01 ? min(previousHistoryAge + 1.0, MAX_HISTORY_AGE) : 0.0;
        }
    }

    if (frameIndex > 0.5 && currentEventCoverage > 0.001 && currentEventTravel <= farDistance)
    {
        float3 currentRay = rayDirectionForPixel(pixel, jitterPixels, cameraPosition, gridCenter);
        float3 eventWorldPosition = cameraPosition + currentRay * currentEventTravel;
        float2 previousEventUv = projectWorldToPreviousHistoryUv(eventWorldPosition);
        if (all(previousEventUv >= 0.0) && all(previousEventUv <= 1.0))
        {
            float4 previousEventMetadata = loadHistoryEventMetadata(previousEventUv);
            float previousEventAge = max(previousEventMetadata.x, 0.0);
            float previousEventTravel = previousEventMetadata.y;
            float previousEventCoverage = saturate(previousEventMetadata.z);
            float previousEventFieldId = previousEventMetadata.w;
            float expectedPreviousEventTravel = distance(previousCameraPosition, eventWorldPosition);
            float travelDelta = abs(previousEventTravel - expectedPreviousEventTravel);
            float travelTolerance = max(0.045, expectedPreviousEventTravel * 0.020);
            float travelWeight = 1.0 - smoothstep(travelTolerance, travelTolerance * 4.5, travelDelta);
            float fieldWeight = abs(previousEventFieldId - currentEventFieldId) < 0.001 ? 1.0 : 0.0;
            float coverageContinuityWeight = 1.0 - smoothstep(0.12, 0.65, abs(previousEventCoverage - currentEventCoverage));
            float eventConfidence = smoothstep(0.0, 8.0, previousEventAge);
            float validationWeight = travelWeight * fieldWeight * coverageContinuityWeight * smoothstep(0.001, 0.08, currentEventCoverage);
            eventHistoryWeight = 0.985 * lerp(0.72, 1.0, eventConfidence) * validationWeight;
            eventHistoryAge = validationWeight > 0.01 ? min(previousEventAge + 1.0, MAX_HISTORY_AGE) : 0.0;
            eventHistoryColor = loadHistoryEventColor(previousEventUv).rgb;
        }
    }

    float combinedHistoryWeight = historyWeight;
    float combinedHistoryAge = max(historyAge, eventHistoryAge);
    float3 resolved = lerp(currentColor, historyColor, combinedHistoryWeight);
    float3 resolvedEvent = lerp(currentEventRadiance, eventHistoryColor, eventHistoryWeight);
    float3 finalColor = presentColor(resolved + resolvedEvent, input.uv);
    if (renderDebugMode > 0.5 && renderDebugMode < 1.5)
    {
        finalColor = aces((rawCurrentColor + currentEventRadiance) * max(exposure, 0.001));
    }
    else if (renderDebugMode >= 1.5 && renderDebugMode < 2.5)
    {
        finalColor = aces((historyColor + eventHistoryColor) * max(exposure, 0.001));
    }
    else if (renderDebugMode >= 2.5 && renderDebugMode < 3.5)
    {
        finalColor = (combinedHistoryAge / MAX_HISTORY_AGE).xxx;
    }
    else if (renderDebugMode >= 3.5 && renderDebugMode < 4.5)
    {
        finalColor = max(combinedHistoryWeight, eventHistoryWeight).xxx;
    }
    else if (renderDebugMode >= 4.5 && renderDebugMode < 5.5)
    {
        finalColor = saturate(float3(currentReactive, max(currentCoverage, currentEventCoverage), 0.0));
    }
    else if (renderDebugMode >= 5.5 && renderDebugMode < 6.5)
    {
        finalColor = currentEventCoverage > 0.001 ? debugFieldIdColor(FIELD_ID_GRID) : debugFieldIdColor(currentFieldId);
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
        finalColor = currentFieldId >= FIELD_ID_AGENT_BASE ? debugFieldIdColor(currentFieldId) : 0.0;
    }
    else if (renderDebugMode >= 9.5 && renderDebugMode < 10.5)
    {
        finalColor = currentFieldId >= FIELD_ID_AGENT_BASE ? debugFieldIdColor(floor(currentControl.x) + 20.0) : 0.0;
    }
    else if (renderDebugMode >= 10.5 && renderDebugMode < 11.5)
    {
        finalColor = currentControl.z.xxx;
    }
    else if (renderDebugMode >= 11.5 && renderDebugMode < 12.5)
    {
        finalColor = currentFieldId >= FIELD_ID_AGENT_BASE ? floor(currentControl.w).xxx * 0.33 : 0.0;
    }
    else if (renderDebugMode >= 12.5 && renderDebugMode < 13.5)
    {
        finalColor = currentFieldId >= FIELD_ID_AGENT_BASE ? frac(currentControl.w).xxx * 5.0 : 0.0;
    }
    ResolveOut output;
    output.finalColor = float4(finalColor, 1.0);
    output.historyColor = float4(resolved, currentTravel);
    output.historyMetadata = currentMetadata;
    output.historyControl = float4(currentControl.xyz, combinedHistoryAge);
    output.historyEventColor = float4(resolvedEvent, currentEventCoverage);
    output.historyEventMetadata = float4(eventHistoryAge, currentEventTravel, currentEventCoverage, currentEventFieldId);
    return output;
}
