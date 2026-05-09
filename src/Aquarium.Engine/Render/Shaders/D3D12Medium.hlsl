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

static const int FIELD_INSTANCE_COUNT = 10;
static const int FIELD_FLAG_CLOUD = 2;
static const int MEDIUM_FROXEL_ATLAS_COLUMNS = 8;
static const int MEDIUM_FROXEL_ATLAS_ROWS = 4;
static const int MEDIUM_FROXEL_SLICE_COUNT = MEDIUM_FROXEL_ATLAS_COLUMNS * MEDIUM_FROXEL_ATLAS_ROWS;

struct VertexOut
{
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
};

struct MediumVolumeOut
{
    float4 diagnostic : SV_Target0;
    float4 transport : SV_Target1;
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

float registeredMediumDensity(float3 p, out float3 scattering)
{
    float density = 0.0;
    scattering = 0.0;

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
        density += fieldDensity;
        scattering += field.colorIntensity.rgb * fieldDensity * field.mediumTerms.y;
    }

    return saturate(density);
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
    float2 screenUv = float2(localUv.x, 1.0 - localUv.y);
    float2 pixel = screenUv * resolution;
    float3 rayDirection = rayDirectionForPixel(pixel, jitterPixels, cameraPosition, gridCenter);

    float sliceLength = farDistance / (float)MEDIUM_FROXEL_SLICE_COUNT;
    float travel = mediumSliceTravel(sliceIndex);
    float3 p = cameraPosition + rayDirection * travel;
    float3 scattering;
    float density = registeredMediumDensity(p, scattering);
    float extinction = density * 0.16;
    float transmittance = exp(-extinction * sliceLength);
    float3 inScattering = density > 0.001
        ? scattering * (1.0 - transmittance) / max(extinction, 0.001)
        : 0.0;

    float sourceDebug = saturate(dot(inScattering, float3(0.2126, 0.7152, 0.0722)) * 0.65);
    MediumVolumeOut output;
    output.diagnostic = float4(saturate(density), saturate(transmittance), sourceDebug, 1.0);
    output.transport = float4(inScattering, saturate(transmittance));
    return output;
}
