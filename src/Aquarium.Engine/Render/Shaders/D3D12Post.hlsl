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
    float mediumFogDensity;
    float mediumFogHeightFalloff;
    float mediumNoiseScale;
    float mediumNoiseContrast;
    float mediumGridFogDensity;
    float mediumPrimitiveFogDensity;
    float mediumNoiseSpeed;
    float mediumReserved0;
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
Texture2D<float4> currentMediumPacketTexture : register(t16);
Texture2D<float4> historyMediumPacketTexture : register(t17);
Texture2D<float4> currentEventColorTexture : register(t18);
Texture2D<float4> currentEventMetadataTexture : register(t19);
Texture2D<float4> historyEventColorTexture : register(t20);
Texture2D<float4> historyEventMetadataTexture : register(t21);
Texture2D<float4> gridHeightTexture : register(t22);
Texture2D<float4> currentMediumColorTexture : register(t23);
Texture2D<float4> historyMediumColorTexture : register(t24);
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
    float4 historyMediumPacket : SV_Target4;
    float4 historyMediumColor : SV_Target5;
    float4 historyEventColor : SV_Target6;
    float4 historyEventMetadata : SV_Target7;
};

static const float SUN_RADIUS = 1.12;
static const int PLANET_COUNT = 5;
static const float FIELD_ID_SELF = 2.0;
static const float FIELD_ID_MEDIUM = 3.0;
static const float FIELD_ID_GRID = 4.0;
static const float FIELD_ID_CURSOR = 5.0;
static const float FIELD_ID_PLANET_BASE = 10.0;
static const float MAX_HISTORY_AGE = 32.0;
static const int FIELD_INSTANCE_COUNT = 11;
static const int FIELD_FLAG_CLOUD = 2;
static const int MEDIUM_RAY_PREVIEW_STEPS = 48;
static const float GRID_FOG_EXTINCTION = 0.18;
static const float GRID_FOG_SCATTERING_ALBEDO = 0.82;
static const float GLOBAL_FOG_DENSITY = 0.075;
static const float GLOBAL_FOG_EXTINCTION = 0.075;
static const float GLOBAL_FOG_SCATTERING_ALBEDO = 0.78;
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

    if (abs(fieldId - FIELD_ID_CURSOR) < 0.25)
    {
        float3 currentCenter = float3(cursorWorlds.xy, 0.56);
        float3 previousCenter = float3(cursorWorlds.zw, 0.56);
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

float4 loadCurrentMediumPacket(float2 uv)
{
    return currentMediumPacketTexture.Load(int3(pixelFromUv(uv), 0));
}

float4 loadHistoryMediumPacket(float2 uv)
{
    return historyMediumPacketTexture.Load(int3(pixelFromUv(uv), 0));
}

float4 loadCurrentMediumColor(float2 uv)
{
    return currentMediumColorTexture.Load(int3(pixelFromUv(uv), 0));
}

float4 loadHistoryMediumColor(float2 uv)
{
    return historyMediumColorTexture.Load(int3(pixelFromUv(uv), 0));
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

float tri(float x)
{
    return abs(frac(x) - 0.5);
}

float3 tri3(float3 p)
{
    return float3(
        tri(p.z + tri(p.y)),
        tri(p.z + tri(p.x)),
        tri(p.y + tri(p.x)));
}

float triNoise3d(float3 p)
{
    float z = 1.4;
    float value = 0.001;
    float3 basePoint = p;

    [unroll]
    for (int i = 0; i < 2; i++)
    {
        float3 dg = tri3(basePoint * 2.0);
        p += dg + timeSeconds * mediumNoiseSpeed * 0.055;
        basePoint *= 1.8;
        z *= 1.5;
        p *= 1.2;
        value += tri(p.z + tri(p.x + tri(p.y))) / z;
        basePoint += 0.14;
    }

    return value;
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

float gridFogDensity(float3 p)
{
    float2 local = gridLocal(p.xy);
    float radialFade = 1.0 - smoothstep(0.96, 1.12, length(local));
    float depthBelowGrid = terrainHeight(p.xy) - p.z;
    float depthRamp = smoothstep(0.035, 0.72, depthBelowGrid);
    if (radialFade <= 0.0 || depthRamp <= 0.0)
    {
        return 0.0;
    }

    float noiseScale = max(mediumNoiseScale, 0.001);
    float3 domain = float3(p.xy - gridCenter, p.z * 1.7) * (0.18 * noiseScale);
    float3 flow = float3(0.11, -0.07, 0.05) * timeSeconds;
    float3 warp = float3(
        triNoise3d(domain + flow + 13.1),
        triNoise3d(domain.yzx - flow + 27.7),
        triNoise3d(domain.zxy + flow * 0.63 + 41.3)) * 2.0 - 1.0;

    float3 warped = domain + warp * 1.05;
    float low = triNoise3d(warped * 1.55 + flow);
    float mid = triNoise3d(warped * 3.45 - flow.yzx * 0.72 + 8.3);
    float high = triNoise3d(warped * 8.6 + flow.zxy * 1.35 + 19.7);
    float strand = 1.0 - abs(mid * 2.0 - 1.0);
    float filament = strand * strand * lerp(0.42, 1.0, high);
    float billow = low * 0.86 + filament * 0.62 - high * 0.24;
    float textureWeight = saturate(0.34 + billow * 1.44);
    float deepening = 1.0 - exp(-max(depthBelowGrid, 0.0) * 1.45);
    float strata = lerp(0.78, 1.28, triNoise3d(float3(domain.xy * 0.48, p.z * 0.22) + flow.zxy * 0.5));
    float detail = lerp(1.0, lerp(0.58, 1.82, textureWeight) * strata, mediumNoiseContrast);
    return saturate(radialFade * depthRamp * lerp(0.42, 1.0, deepening) * detail);
}

float globalFogDensity(float3 p)
{
    float upwardDecay = exp(-max(p.z, 0.0) * max(mediumFogHeightFalloff, 0.0));
    float belowGridLift = lerp(1.0, 1.42, saturate(-p.z * 0.035));
    float noiseScale = max(mediumNoiseScale, 0.001);
    float3 domain = float3((p.xy - gridCenter) * 0.045, p.z * 0.065) * noiseScale;
    float slow = triNoise3d(domain + float3(0.018, -0.011, 0.006) * timeSeconds);
    float breakup = lerp(1.0, lerp(0.72, 1.18, slow), mediumNoiseContrast);
    return GLOBAL_FOG_DENSITY * upwardDecay * belowGridLift * breakup;
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

void registeredMediumCoefficients(float3 p, out float density, out float sigmaT, out float sigmaS, out float albedo)
{
    float gridDensity = gridFogDensity(p) * mediumGridFogDensity;
    float atmosphereDensity = globalFogDensity(p) * mediumFogDensity;
    density = gridDensity + atmosphereDensity;
    sigmaT = gridDensity * GRID_FOG_EXTINCTION + atmosphereDensity * GLOBAL_FOG_EXTINCTION;
    sigmaS = gridDensity * GRID_FOG_EXTINCTION * GRID_FOG_SCATTERING_ALBEDO
        + atmosphereDensity * GLOBAL_FOG_EXTINCTION * GLOBAL_FOG_SCATTERING_ALBEDO;

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
        float fieldDensity = shell * core * erosion * field.mediumTerms.w * mediumPrimitiveFogDensity;
        float fieldSigmaT = fieldDensity * max(field.mediumTerms.x, 0.0);
        float fieldAlbedo = saturate(field.mediumTerms.y);

        density += fieldDensity;
        sigmaT += fieldSigmaT;
        sigmaS += fieldSigmaT * fieldAlbedo;
    }

    density = saturate(density);
    sigmaT = max(sigmaT, 0.0);
    sigmaS = min(max(sigmaS, 0.0), sigmaT);
    albedo = sigmaT > 0.0001 ? saturate(sigmaS / sigmaT) : 0.0;
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
        float density;
        float sigmaT;
        float sigmaS;
        float albedo;
        registeredMediumCoefficients(p, density, sigmaT, sigmaS, albedo);
        if (stepIndex == selectedStep)
        {
            stepDensity = density;
            return;
        }

        transmittance *= exp(-sigmaT * stepLength);
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
    float4 currentMediumPacket = loadCurrentMediumPacket(input.uv);
    float currentMediumTravel = currentMediumPacket.y;
    float currentMediumLaneOpacity = saturate(currentMediumPacket.z);
    float currentMediumDensity = saturate(currentMediumPacket.w);
    float3 currentMediumRadiance = loadCurrentMediumColor(input.uv).rgb;
    currentColor = max(currentColor - currentMediumRadiance, 0.0);
    float4 currentEventColor = loadCurrentEventColor(input.uv);
    float4 currentEventMetadata = loadCurrentEventMetadata(input.uv);
    float currentEventFieldId = currentEventMetadata.x;
    float currentEventTravel = currentEventMetadata.y;
    float currentEventCoverage = saturate(currentEventMetadata.z);
    float3 currentEventRadiance = currentEventColor.rgb;
    bool currentIsMedium = abs(currentFieldId - FIELD_ID_MEDIUM) < 0.25;

    float historyWeight = 0.0;
    float historyAge = 0.0;
    float3 historyColor = currentColor;
    float mediumHistoryWeight = 0.0;
    float mediumHistoryAge = 0.0;
    float3 mediumHistoryColor = currentMediumRadiance;
    float eventHistoryWeight = 0.0;
    float eventHistoryAge = 0.0;
    float3 eventHistoryColor = currentEventRadiance;
    if (!currentIsMedium && frameIndex > 0.5 && currentTravel <= farDistance && currentFieldId > 0.5)
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
            float3 clampedHistory = clamp(previous.rgb, neighborhoodMin, neighborhoodMax);
            float colorDelta = length(clampedHistory - currentColor);
            float colorWeight = 1.0 - smoothstep(0.18, 1.2, colorDelta);
            float reactiveWeight = 1.0 - currentReactive;
            float coverageWeight = smoothstep(0.02, 0.55, currentCoverage);
            float coverageContinuityWeight = 1.0 - smoothstep(0.10, 0.50, abs(previousCoverage - currentCoverage));
            float mediumContinuityWeight = 1.0 - smoothstep(0.04, 0.35, abs(previousMediumOpacity - currentMediumOpacity));
            float historyConfidence = smoothstep(0.0, 6.0, previousHistoryAge);
            float validationWeight = travelWeight * colorWeight * fieldWeight * normalWeight * reactiveWeight * coverageWeight * coverageContinuityWeight * mediumContinuityWeight;

            historyColor = clampedHistory;
            historyWeight = 0.82 * lerp(0.35, 1.0, historyConfidence) * validationWeight;
            historyAge = validationWeight > 0.01 ? min(previousHistoryAge + 1.0, MAX_HISTORY_AGE) : 0.0;
        }
    }

    if (frameIndex > 0.5 && currentMediumLaneOpacity > 0.015 && currentMediumTravel <= farDistance)
    {
        float3 currentRay = rayDirectionForPixel(pixel, jitterPixels, cameraPosition, gridCenter);
        float3 mediumWorldPosition = cameraPosition + currentRay * currentMediumTravel;
        float2 previousMediumUv = projectWorldToPreviousHistoryUv(mediumWorldPosition);
        if (all(previousMediumUv >= 0.0) && all(previousMediumUv <= 1.0))
        {
            float4 previousMediumPacket = loadHistoryMediumPacket(previousMediumUv);
            float previousMediumTravel = previousMediumPacket.y;
            float previousMediumOpacity = saturate(previousMediumPacket.z);
            float previousMediumDensity = saturate(previousMediumPacket.w);
            float previousMediumAge = max(previousMediumPacket.x, 0.0);
            float expectedPreviousMediumTravel = distance(previousCameraPosition, mediumWorldPosition);
            float travelDelta = abs(previousMediumTravel - expectedPreviousMediumTravel);
            float travelTolerance = max(0.08, expectedPreviousMediumTravel * 0.035);
            float travelWeight = 1.0 - smoothstep(travelTolerance, travelTolerance * 5.0, travelDelta);
            float opacityWeight = 1.0 - smoothstep(0.06, 0.42, abs(previousMediumOpacity - currentMediumLaneOpacity));
            float densityWeight = 1.0 - smoothstep(0.06, 0.42, abs(previousMediumDensity - currentMediumDensity));
            float validationWeight = travelWeight * opacityWeight * densityWeight;
            float mediumConfidence = smoothstep(0.0, 6.0, previousMediumAge);
            mediumHistoryWeight = 0.74 * lerp(0.22, 1.0, mediumConfidence) * validationWeight * smoothstep(0.015, 0.35, currentMediumLaneOpacity);
            mediumHistoryAge = validationWeight > 0.01 ? min(previousMediumAge + 1.0, MAX_HISTORY_AGE) : 0.0;
            mediumHistoryColor = loadHistoryMediumColor(previousMediumUv).rgb;
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
    float combinedHistoryAge = max(max(historyAge, mediumHistoryAge), eventHistoryAge);
    float3 resolved = lerp(currentColor, historyColor, combinedHistoryWeight);
    float3 resolvedMedium = lerp(currentMediumRadiance, mediumHistoryColor, mediumHistoryWeight);
    float3 resolvedEvent = lerp(currentEventRadiance, eventHistoryColor, eventHistoryWeight);
    float3 finalColor = presentColor(resolved + resolvedMedium + resolvedEvent, input.uv);
    if (renderDebugMode > 0.5 && renderDebugMode < 1.5)
    {
        finalColor = aces((rawCurrentColor + currentEventRadiance) * max(exposure, 0.001));
    }
    else if (renderDebugMode >= 1.5 && renderDebugMode < 2.5)
    {
        finalColor = aces((historyColor + mediumHistoryColor + eventHistoryColor) * max(exposure, 0.001));
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
        finalColor = saturate(float3(currentReactive, max(max(currentCoverage, currentMediumLaneOpacity), currentEventCoverage), currentMediumOpacity));
    }
    else if (renderDebugMode >= 5.5 && renderDebugMode < 6.5)
    {
        finalColor = currentEventCoverage > 0.001 ? debugFieldIdColor(FIELD_ID_GRID) : (currentMediumLaneOpacity > 0.015 ? debugFieldIdColor(FIELD_ID_MEDIUM) : debugFieldIdColor(currentFieldId));
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
    output.historyControl = float4(currentControl.xyz, combinedHistoryAge);
    output.historyMediumPacket = float4(mediumHistoryAge, currentMediumTravel, currentMediumLaneOpacity, currentMediumDensity);
    output.historyMediumColor = float4(resolvedMedium, currentMediumLaneOpacity);
    output.historyEventColor = float4(resolvedEvent, currentEventCoverage);
    output.historyEventMetadata = float4(eventHistoryAge, currentEventTravel, currentEventCoverage, currentEventFieldId);
    return output;
}
