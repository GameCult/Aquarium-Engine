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
Texture2D<float4> mediumVolumeTexture : register(t13);
Texture2D<float4> mediumTransportTexture : register(t14);
SamplerState gridSampler : register(s0);

static const int PLANET_COUNT = 5;
static const float SUN_RADIUS = 1.12;
static const float FIELD_ID_GRID = 1.0;
static const int MEDIUM_FROXEL_ATLAS_COLUMNS = 8;
static const int MEDIUM_FROXEL_ATLAS_ROWS = 4;
static const int MEDIUM_FROXEL_SLICE_COUNT = MEDIUM_FROXEL_ATLAS_COLUMNS * MEDIUM_FROXEL_ATLAS_ROWS;

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

void integrateMedium(float2 uv, float maxTravel, out float densityMean, out float transmittance, out float3 inScattering)
{
    densityMean = 0.0;
    transmittance = 1.0;
    inScattering = 0.0;

    [loop]
    for (int sliceIndex = 0; sliceIndex < MEDIUM_FROXEL_SLICE_COUNT; sliceIndex++)
    {
        if (mediumSliceTravel(sliceIndex) > maxTravel)
        {
            break;
        }

        float2 atlasUv = mediumAtlasUv(uv, sliceIndex);
        float4 diagnostic = mediumVolumeTexture.SampleLevel(gridSampler, atlasUv, 0.0);
        float4 transport = mediumTransportTexture.SampleLevel(gridSampler, atlasUv, 0.0);
        densityMean += diagnostic.x;
        inScattering += transmittance * transport.rgb;
        transmittance *= saturate(transport.a);
    }

    densityMean = saturate(densityMean / (float)MEDIUM_FROXEL_SLICE_COUNT);
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

float4 D3D12ScenePS(VertexOut input) : SV_Target0
{
    float2 screenUv = float2(input.uv.x, 1.0 - input.uv.y);
    float2 pixel = screenUv * resolution;
    float3 rayDirection = rayDirectionForPixel(pixel, jitterPixels, cameraPosition, gridCenter);

    float travel = farDistance + 1.0;
    float3 color = float3(0.001, 0.003, 0.004);
    float hitTravel;
    if (traceSphere(cameraPosition, rayDirection, float3(0.0, 0.0, 2.2), SUN_RADIUS, hitTravel))
    {
        travel = hitTravel;
        float3 p = cameraPosition + rayDirection * hitTravel;
        color = shadeBody(p, normalize(p - float3(0.0, 0.0, 2.2)), 0);
    }

    [unroll]
    for (int i = 0; i < PLANET_COUNT; i++)
    {
        float radius = planetRadius(i);
        float3 center = planetCenterAt(i, timeSeconds);
        if (traceSphere(cameraPosition, rayDirection, center, radius, hitTravel) && hitTravel < travel)
        {
            travel = hitTravel;
            float3 p = cameraPosition + rayDirection * hitTravel;
            color = shadeBody(p, normalize(p - center), i + 1);
        }
    }

    float densityMean;
    float transmittance;
    float3 inScattering;
    float mediumTravel = travel <= farDistance ? travel : farDistance;
    integrateMedium(input.uv, mediumTravel, densityMean, transmittance, inScattering);
    color = color * transmittance + inScattering;

    if (renderDebugMode >= 10.5 && renderDebugMode < 11.5)
    {
        color = lerp(float3(0.006, 0.016, 0.026), float3(0.32, 0.86, 1.0), saturate(densityMean * 6.0));
    }

    return float4(aces(color * max(exposure, 0.001)), 1.0);
}
