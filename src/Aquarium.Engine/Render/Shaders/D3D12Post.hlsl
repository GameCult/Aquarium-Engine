cbuffer AquariumFrame : register(b0)
{
    float2 resolution;
    float timeSeconds;
    float viewRadius;
    float3 cameraPosition;
    float farDistance;
    float3 cameraTarget;
    float sceneFlags;
    float2 viewCenter;
    float frameIndex;
    float previousTimeSeconds;
    float3 previousCameraPosition;
    float previousViewRadius;
    float3 previousCameraTarget;
    float previousSceneFlags;
    float2 previousViewCenter;
    float2 jitterPixels;
    float2 previousJitterPixels;
    float renderDebugMode;
    float exposure;
    float bloomIntensity;
    float bloomVeilIntensity;
    float4 cursorWorlds;
    float4 temporalGaussianInfo;
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
SamplerState sourceSampler : register(s0);

struct SdfObject
{
    float4 centerRadius;
    float4 previousCenterPad;
    float4 state;
};

StructuredBuffer<SdfObject> sdfObjects : register(t24);

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

static const float FIELD_ID_HEIGHT_FIELD = 4.0;
static const float FIELD_ID_SDF_OBJECT_BASE = 10.0;
static const int AQUARIUM_SDF_OBJECT_CAPACITY = 64;
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

    if (fieldId >= FIELD_ID_SDF_OBJECT_BASE)
    {
        float phase = frac((fieldId - FIELD_ID_SDF_OBJECT_BASE) * 0.37);
        return 0.35 + 0.65 * float3(
            0.5 + 0.5 * sin(phase * 6.28318 + 0.0),
            0.5 + 0.5 * sin(phase * 6.28318 + 2.1),
            0.5 + 0.5 * sin(phase * 6.28318 + 4.2));
    }

    return float3(1.0, 0.0, 1.0);
}

void cameraBasis(float3 camera, float3 target, out float3 forward, out float3 right, out float3 up)
{
    forward = normalize(target - camera);
    right = normalize(cross(forward, float3(0.0, 0.0, 1.0)));
    up = cross(right, forward);
}

float3 rayDirectionForPixel(float2 pixel, float2 jitter, float3 camera, float3 target)
{
    float2 ndc = ((pixel + jitter) * 2.0 - resolution) / resolution.y;
    float3 forward;
    float3 right;
    float3 up;
    cameraBasis(camera, target, forward, right, up);
    return normalize(forward * 1.6 + right * ndc.x + up * ndc.y);
}

float3 temporalPreviousWorldPosition(float3 worldPosition, float fieldId)
{
    if (fieldId >= FIELD_ID_SDF_OBJECT_BASE)
    {
        int sdfIndex = clamp((int)round(fieldId - FIELD_ID_SDF_OBJECT_BASE), 0, AQUARIUM_SDF_OBJECT_CAPACITY - 1);
        float3 currentCenter = sdfObjects[sdfIndex].centerRadius.xyz;
        float3 previousCenter = sdfObjects[sdfIndex].previousCenterPad.xyz;
        return previousCenter + (worldPosition - currentCenter);
    }

    return worldPosition;
}

float2 projectWorldToPreviousHistoryUv(float3 worldPosition)
{
    float3 forward;
    float3 right;
    float3 up;
    cameraBasis(previousCameraPosition, previousCameraTarget, forward, right, up);
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

float3 presentColor(float3 scene, float2 uv, float bloomScale)
{
    float3 bloom = bloomColorAt(uv) * bloomScale;
    float3 exposedScene = scene * max(exposure, 0.001);
    return aces(exposedScene + bloom * bloomIntensity + luminance(bloom) * bloomVeilIntensity);
}

float3 clampBloomFirefly(float2 uv, float3 centerColor)
{
    float2 texel = 1.0 / resolution;
    float centerLuma = luminance(centerColor);
    float neighborMaxLuma = 0.0;
    float neighborSumLuma = 0.0;

    [unroll]
    for (int y = -1; y <= 1; y++)
    {
        [unroll]
        for (int x = -1; x <= 1; x++)
        {
            if (x == 0 && y == 0)
            {
                continue;
            }

            float3 sampleColor = sourceTexture.SampleLevel(sourceSampler, uv + float2(x, y) * texel, 0.0).rgb;
            float sampleLuma = luminance(sampleColor);
            neighborMaxLuma = max(neighborMaxLuma, sampleLuma);
            neighborSumLuma += sampleLuma;
        }
    }

    float neighborAvgLuma = neighborSumLuma * 0.125;
    float supportedLuma = max(neighborMaxLuma * 1.55, neighborAvgLuma * 2.75) + 1.25;
    float spike = smoothstep(supportedLuma, supportedLuma * 2.5 + 2.0, centerLuma);
    float clampedLuma = lerp(centerLuma, min(centerLuma, supportedLuma), spike);
    return centerColor * (clampedLuma / max(centerLuma, 0.0001));
}

float4 D3D12BloomPrefilterPS(VertexOut input) : SV_Target0
{
    float3 color = sourceTexture.SampleLevel(sourceSampler, input.uv, 0.0).rgb;
    return float4(clampBloomFirefly(input.uv, color) * max(exposure, 0.001), 1.0);
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
    float currentCoverage = saturate(currentControl.x);
    float currentStepRatio = saturate(currentControl.y);
    float currentTemporalDetail = saturate(currentControl.z);
    float currentReservoirConfidence = currentControl.w > 0.0 ? saturate(currentControl.w) : 1.0;

    float historyWeight = 0.0;
    float historyAge = 0.0;
    float3 historyColor = currentColor;
    if (frameIndex > 0.5 && currentTravel <= farDistance && currentFieldId > 0.5)
    {
        float3 currentRay = rayDirectionForPixel(pixel, jitterPixels, cameraPosition, cameraTarget);
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
            float previousCoverage = saturate(previousControl.x);
            float previousTemporalDetail = saturate(previousControl.z);
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
            float currentLuma = luminance(currentColor);
            float historyLuma = luminance(clampedHistory);
            float hotSdfCurrent = currentFieldId >= FIELD_ID_SDF_OBJECT_BASE ? smoothstep(1.0, 5.0, currentLuma - historyLuma) : 0.0;
            colorWeight = max(colorWeight, hotSdfCurrent * 0.82);
            float coverageWeight = smoothstep(0.02, 0.55, currentCoverage);
            float coverageContinuityWeight = 1.0 - smoothstep(0.10, 0.50, abs(previousCoverage - currentCoverage));
            float temporalDetailWeight = 1.0 - smoothstep(0.08, 0.45, abs(previousTemporalDetail - currentTemporalDetail));
            temporalDetailWeight = max(temporalDetailWeight, 1.0 - smoothstep(0.02, 0.12, max(previousTemporalDetail, currentTemporalDetail)));
            float reservoirConfidenceWeight = lerp(0.45, 1.0, currentReservoirConfidence);
            float historyConfidence = smoothstep(0.0, 6.0, previousHistoryAge);
            float validationWeight = travelWeight * colorWeight * fieldWeight * normalWeight * coverageWeight * coverageContinuityWeight * temporalDetailWeight * reservoirConfidenceWeight;

            historyColor = clampedHistory;
            historyWeight = 0.82 * lerp(0.35, 1.0, historyConfidence) * validationWeight;
            historyAge = validationWeight > 0.01 ? min(previousHistoryAge + 1.0, MAX_HISTORY_AGE) : 0.0;
        }
    }

    float combinedHistoryWeight = historyWeight;
    float combinedHistoryAge = historyAge;
    float3 resolved = lerp(currentColor, historyColor, combinedHistoryWeight);
    float bloomStability = saturate((luminance(resolved) + 0.25) / max(luminance(currentColor) + 0.25, 0.0001));
    float3 finalColor = presentColor(resolved, input.uv, bloomStability);
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
        finalColor = (combinedHistoryAge / MAX_HISTORY_AGE).xxx;
    }
    else if (renderDebugMode >= 3.5 && renderDebugMode < 4.5)
    {
        finalColor = combinedHistoryWeight.xxx;
    }
    else if (renderDebugMode >= 4.5 && renderDebugMode < 5.5)
    {
        finalColor = saturate(float3(currentCoverage, currentStepRatio, currentTemporalDetail));
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
        finalColor = currentFieldId >= FIELD_ID_SDF_OBJECT_BASE ? debugFieldIdColor(currentFieldId) : 0.0;
    }
    else if (renderDebugMode >= 9.5 && renderDebugMode < 10.5)
    {
        finalColor = currentStepRatio.xxx;
    }
    else if (renderDebugMode >= 11.5 && renderDebugMode < 12.5)
    {
        finalColor = float3(currentReservoirConfidence, currentTemporalDetail, combinedHistoryWeight);
    }
    ResolveOut output;
    output.finalColor = float4(finalColor, 1.0);
    output.historyColor = float4(resolved, currentTravel);
    output.historyMetadata = currentMetadata;
    output.historyControl = float4(currentControl.xyz, combinedHistoryAge);
    return output;
}
