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
    float4 cursorWorlds;
};

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
Texture2D<float4> mediumVolumeTexture : register(t13);
Texture2D<float4> mediumLightTexture : register(t15);
Texture2D<float4> gridHeightTexture : register(t22);
Texture2D<float4> mediumLightDirectionTexture : register(t25);
StructuredBuffer<uint> ditherTexture : register(t26);
SamplerState sourceSampler : register(s0);

static const int FIELD_INSTANCE_COUNT = 11;
static const int FIELD_FLAG_CLOUD = 2;
static const int FIELD_FLAG_EMITTER = 8;
static const int MEDIUM_FROXEL_ATLAS_COLUMNS = 8;
static const int MEDIUM_FROXEL_ATLAS_ROWS = 4;
static const int MEDIUM_FROXEL_SLICE_COUNT = MEDIUM_FROXEL_ATLAS_COLUMNS * MEDIUM_FROXEL_ATLAS_ROWS;
static const int MEDIUM_FROXEL_DOWNSCALE = 8;
static const int DITHER_TEXTURE_SIZE = 512;
static const float PI = 3.14159265359;
static const float INV_FOUR_PI = 0.07957747155;
static const float GOLDEN_RATIO_CONJUGATE = 0.61803398875;
static const float GRID_FOG_EXTINCTION = 0.18;
static const float GRID_FOG_SCATTERING_ALBEDO = 0.82;
static const float GLOBAL_FOG_DENSITY = 0.075;
static const float GLOBAL_FOG_EXTINCTION = 0.075;
static const float GLOBAL_FOG_SCATTERING_ALBEDO = 0.78;

struct VertexOut
{
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
};

struct MediumVolumeOut
{
    float4 diagnostic : SV_Target0;
    float4 transport : SV_Target1;
    float4 light : SV_Target2;
    float4 lightDirection : SV_Target3;
};

struct MediumLightPropagationOut
{
    float4 light : SV_Target0;
    float4 lightDirection : SV_Target1;
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

float blueNoise(uint2 pixel)
{
    uint2 wrapped = pixel & (DITHER_TEXTURE_SIZE - 1);
    return ((float)(ditherTexture[wrapped.x + wrapped.y * DITHER_TEXTURE_SIZE] & 255u) + 0.5) / 256.0;
}

float animatedBlueNoise(uint2 pixel, float salt)
{
    return frac(blueNoise(pixel) + frameIndex * GOLDEN_RATIO_CONJUGATE + salt);
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
        p += dg + timeSeconds * 0.055;
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

float gridFogExtinctionApprox(float3 p)
{
    float2 local = gridLocal(p.xy);
    float radialFade = 1.0 - smoothstep(0.96, 1.12, length(local));
    float depthBelowGrid = terrainHeight(p.xy) - p.z;
    float depthRamp = smoothstep(0.035, 0.72, depthBelowGrid);
    float deepening = 1.0 - exp(-max(depthBelowGrid, 0.0) * 1.45);
    float gridDensity = saturate(radialFade * depthRamp * lerp(0.42, 1.0, deepening));
    return (gridDensity + globalFogDensity(p) * (GLOBAL_FOG_EXTINCTION / GRID_FOG_EXTINCTION)) * mediumFogDensity;
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
    density = gridFogDensity(p);
    sigmaT = density * GRID_FOG_EXTINCTION;
    sigmaS = sigmaT * GRID_FOG_SCATTERING_ALBEDO;
    float globalDensity = globalFogDensity(p);
    density += globalDensity;
    sigmaT += globalDensity * GLOBAL_FOG_EXTINCTION;
    sigmaS += globalDensity * GLOBAL_FOG_EXTINCTION * GLOBAL_FOG_SCATTERING_ALBEDO;

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
        float fieldDensity = shell * core * erosion * field.mediumTerms.w;
        float fieldSigmaT = fieldDensity * max(field.mediumTerms.x, 0.0);
        float fieldAlbedo = saturate(field.mediumTerms.y);

        density += fieldDensity;
        sigmaT += fieldSigmaT;
        sigmaS += fieldSigmaT * fieldAlbedo;
    }

    density *= mediumFogDensity;
    sigmaT *= mediumFogDensity;
    sigmaS *= mediumFogDensity;
    density = saturate(density);
    sigmaT = max(sigmaT, 0.0);
    sigmaS = min(max(sigmaS, 0.0), sigmaT);
    albedo = sigmaT > 0.0001 ? saturate(sigmaS / sigmaT) : 0.0;
}

float emissiveFieldSolidAngle(float3 p, FieldInstance emitter)
{
    float emitterRadius = max(emitter.centerRadius.w, 0.001);
    float3 toEmitter = emitter.centerRadius.xyz - p;
    float distanceToEmitter = max(length(toEmitter), emitterRadius + 0.001);
    float sinTheta = saturate(emitterRadius / distanceToEmitter);
    float cosTheta = sqrt(saturate(1.0 - sinTheta * sinTheta));
    return 2.0 * PI * (1.0 - cosTheta);
}

float emissiveSurfaceTransmittance(float3 p, FieldInstance emitter)
{
    float emitterRadius = max(emitter.centerRadius.w, 0.001);
    float3 toEmitter = emitter.centerRadius.xyz - p;
    float distanceToEmitter = length(toEmitter);
    float pathLength = max(distanceToEmitter - emitterRadius, 0.0);
    if (pathLength <= 0.001)
    {
        return 1.0;
    }

    float3 lightDirection = toEmitter / max(distanceToEmitter, 0.0001);
    float opticalDepth = 0.0;

    [loop]
    for (int i = 0; i < 4; i++)
    {
        float travel = ((float)i + 0.5) * pathLength / 4.0;
        opticalDepth += gridFogExtinctionApprox(p + lightDirection * travel);
    }

    opticalDepth *= (pathLength / 4.0) * GRID_FOG_EXTINCTION;
    return exp(-opticalDepth);
}

void injectedEmitterLighting(float3 p, out float3 irradiance, out float4 directionMoment)
{
    irradiance = 0.0;
    float3 directionWeighted = 0.0;
    float directionWeight = 0.0;

    [loop]
    for (int i = 0; i < FIELD_INSTANCE_COUNT; i++)
    {
        FieldInstance emitter = fieldInstances[i];
        if (((int)(emitter.fieldFlags.y + 0.5) & FIELD_FLAG_EMITTER) == 0)
        {
            continue;
        }

        float emitterRadius = max(emitter.centerRadius.w, 0.001);
        float3 toEmitter = emitter.centerRadius.xyz - p;
        float emitterDistance = length(toEmitter);
        float3 lightDirection = toEmitter / max(emitterDistance, 0.0001);
        float solidAngle = emissiveFieldSolidAngle(p, emitter);
        float visibility = smoothstep(emitterRadius * 0.98, emitterRadius * 1.08, emitterDistance);
        float transmittance = emissiveSurfaceTransmittance(p, emitter);
        float3 contribution = emitter.colorIntensity.rgb * solidAngle * visibility * transmittance;
        float contributionWeight = dot(contribution, float3(0.2126, 0.7152, 0.0722));
        irradiance += contribution;
        directionWeighted += lightDirection * contributionWeight;
        directionWeight += contributionWeight;
    }

    directionMoment = float4(directionWeighted, directionWeight);
}

float mediumSliceTravel(int sliceIndex)
{
    float t = ((float)sliceIndex + 0.5) / (float)MEDIUM_FROXEL_SLICE_COUNT;
    return t * farDistance;
}

MediumVolumeOut MediumVolumePS(VertexOut input)
{
    float2 atlasCoord = saturate(input.uv) * float2(MEDIUM_FROXEL_ATLAS_COLUMNS, MEDIUM_FROXEL_ATLAS_ROWS);
    int2 tile = clamp((int2)floor(atlasCoord), int2(0, 0), int2(MEDIUM_FROXEL_ATLAS_COLUMNS - 1, MEDIUM_FROXEL_ATLAS_ROWS - 1));
    int sliceIndex = tile.x + tile.y * MEDIUM_FROXEL_ATLAS_COLUMNS;
    float2 localUv = frac(atlasCoord);
    int froxelWidth = max((int)(resolution.x / (float)MEDIUM_FROXEL_DOWNSCALE), 1);
    int froxelHeight = max((int)(resolution.y / (float)MEDIUM_FROXEL_DOWNSCALE), 1);
    int2 cell = clamp((int2)floor(localUv * float2(froxelWidth, froxelHeight)), int2(0, 0), int2(froxelWidth - 1, froxelHeight - 1));
    uint2 ditherPixel = (uint2)(cell * MEDIUM_FROXEL_DOWNSCALE);
    float2 xyJitter = float2(
        animatedBlueNoise(ditherPixel + uint2(17u, 59u), 0.0),
        animatedBlueNoise(ditherPixel + uint2(113u, 211u), 0.37)) - 0.5;
    float2 screenUv = float2(localUv.x, 1.0 - localUv.y);
    float2 pixel = screenUv * resolution + xyJitter * (float)MEDIUM_FROXEL_DOWNSCALE;
    float3 rayDirection = rayDirectionForPixel(pixel, jitterPixels, cameraPosition, gridCenter);

    float sliceLength = farDistance / (float)MEDIUM_FROXEL_SLICE_COUNT;
    float zJitter = animatedBlueNoise(ditherPixel + uint2(307u, 401u), 0.73);
    float travel = (((float)sliceIndex + zJitter) / (float)MEDIUM_FROXEL_SLICE_COUNT) * farDistance;
    float3 p = cameraPosition + rayDirection * travel;
    float density;
    float sigmaT;
    float sigmaS;
    float albedo;
    registeredMediumCoefficients(p, density, sigmaT, sigmaS, albedo);
    float mediumBlend = saturate(mediumCompositeIntensity);
    density *= mediumBlend;
    sigmaT *= mediumBlend;
    sigmaS *= mediumBlend;
    float transmittance = exp(-sigmaT * sliceLength);
    float3 injectedIrradiance;
    float4 lightDirectionMoment;
    injectedEmitterLighting(p, injectedIrradiance, lightDirectionMoment);
    injectedIrradiance *= mediumBlend;
    lightDirectionMoment *= mediumBlend;

    MediumVolumeOut output;
    output.diagnostic = float4(saturate(density), sigmaT, sigmaS, albedo);
    output.transport = float4(0.0, 0.0, 0.0, saturate(transmittance));
    output.light = float4(injectedIrradiance, saturate(density));
    output.lightDirection = lightDirectionMoment;
    return output;
}

float2 atlasUvFromCell(int2 cell, int sliceIndex)
{
    int froxelWidth = max((int)(resolution.x / 8.0), 1);
    int froxelHeight = max((int)(resolution.y / 8.0), 1);
    int tileX = sliceIndex % MEDIUM_FROXEL_ATLAS_COLUMNS;
    int tileY = sliceIndex / MEDIUM_FROXEL_ATLAS_COLUMNS;
    int2 atlasPixel = int2(tileX * froxelWidth, tileY * froxelHeight) + clamp(cell, int2(0, 0), int2(froxelWidth - 1, froxelHeight - 1));
    return ((float2)atlasPixel + 0.5) / float2(froxelWidth * MEDIUM_FROXEL_ATLAS_COLUMNS, froxelHeight * MEDIUM_FROXEL_ATLAS_ROWS);
}

float3 propagatedNeighbor(int2 cell, int sliceIndex, float weight)
{
    sliceIndex = clamp(sliceIndex, 0, MEDIUM_FROXEL_SLICE_COUNT - 1);
    float2 uv = atlasUvFromCell(cell, sliceIndex);
    float sigmaT = mediumVolumeTexture.SampleLevel(sourceSampler, uv, 0.0).y;
    float occlusion = exp(-sigmaT * 0.55);
    return mediumLightTexture.SampleLevel(sourceSampler, uv, 0.0).rgb * weight * occlusion;
}

float4 propagatedDirectionNeighbor(int2 cell, int sliceIndex, float weight)
{
    sliceIndex = clamp(sliceIndex, 0, MEDIUM_FROXEL_SLICE_COUNT - 1);
    float2 uv = atlasUvFromCell(cell, sliceIndex);
    float sigmaT = mediumVolumeTexture.SampleLevel(sourceSampler, uv, 0.0).y;
    float occlusion = exp(-sigmaT * 0.55);
    return mediumLightDirectionTexture.SampleLevel(sourceSampler, uv, 0.0) * weight * occlusion;
}

MediumLightPropagationOut MediumLightPropagatePS(VertexOut input)
{
    int froxelWidth = max((int)(resolution.x / 8.0), 1);
    int froxelHeight = max((int)(resolution.y / 8.0), 1);
    int2 atlasPixel = int2(input.position.xy);
    int2 tile = clamp(atlasPixel / int2(froxelWidth, froxelHeight), int2(0, 0), int2(MEDIUM_FROXEL_ATLAS_COLUMNS - 1, MEDIUM_FROXEL_ATLAS_ROWS - 1));
    int2 cell = atlasPixel - tile * int2(froxelWidth, froxelHeight);
    int sliceIndex = tile.x + tile.y * MEDIUM_FROXEL_ATLAS_COLUMNS;
    float2 uv = atlasUvFromCell(cell, sliceIndex);

    float4 medium = mediumVolumeTexture.SampleLevel(sourceSampler, uv, 0.0);
    float density = medium.x;
    float sigmaT = medium.y;
    float3 center = mediumLightTexture.SampleLevel(sourceSampler, uv, 0.0).rgb;
    float4 centerDirection = mediumLightDirectionTexture.SampleLevel(sourceSampler, uv, 0.0);
    float3 propagated = center * 0.58;
    float4 propagatedDirection = centerDirection * 0.58;
    propagated += propagatedNeighbor(cell + int2(1, 0), sliceIndex, 0.055);
    propagated += propagatedNeighbor(cell + int2(-1, 0), sliceIndex, 0.055);
    propagated += propagatedNeighbor(cell + int2(0, 1), sliceIndex, 0.055);
    propagated += propagatedNeighbor(cell + int2(0, -1), sliceIndex, 0.055);
    propagated += propagatedNeighbor(cell, sliceIndex + 1, 0.08);
    propagated += propagatedNeighbor(cell, sliceIndex - 1, 0.08);
    propagatedDirection += propagatedDirectionNeighbor(cell + int2(1, 0), sliceIndex, 0.055);
    propagatedDirection += propagatedDirectionNeighbor(cell + int2(-1, 0), sliceIndex, 0.055);
    propagatedDirection += propagatedDirectionNeighbor(cell + int2(0, 1), sliceIndex, 0.055);
    propagatedDirection += propagatedDirectionNeighbor(cell + int2(0, -1), sliceIndex, 0.055);
    propagatedDirection += propagatedDirectionNeighbor(cell, sliceIndex + 1, 0.08);
    propagatedDirection += propagatedDirectionNeighbor(cell, sliceIndex - 1, 0.08);

    float localRetention = lerp(0.72, 1.0, saturate(sigmaT * 7.0 + density * 0.35));
    float3 light = max(center, propagated * localRetention);
    float4 directionMoment = abs(centerDirection.a) > abs(propagatedDirection.a * localRetention)
        ? centerDirection
        : propagatedDirection * localRetention;

    MediumLightPropagationOut output;
    output.light = float4(light, saturate(density));
    output.lightDirection = directionMoment;
    return output;
}
