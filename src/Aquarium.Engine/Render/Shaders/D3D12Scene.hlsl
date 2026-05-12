cbuffer AquariumFrame : register(b0)
{
    float2 resolution;
    float timeSeconds;
    float viewRadius;
    float3 cameraPosition;
    float farDistance;
    float2 viewCenter;
    float frameIndex;
    float previousTimeSeconds;
    float3 previousCameraPosition;
    float previousViewRadius;
    float2 previousViewCenter;
    float2 jitterPixels;
    float2 previousJitterPixels;
    float renderDebugMode;
    float exposure;
    float bloomIntensity;
    float bloomVeilIntensity;
    float4 cursorWorlds;
};

Texture2D<float4> heightFieldTexture : register(t0);
TextureCube<float4> studioPmremTexture : register(t22);
SamplerState linearSampler : register(s0);

static const float FIELD_ID_HEIGHT_FIELD = 4.0;
static const float PI = 3.14159265359;
static const float HEIGHT_FIELD_TEXEL_COUNT = 128.0;
static const float SURFACE_FLAT_REFLECTION_MAX_LOD = 3.0;
static const float BACKGROUND_PMREM_LOD = 3.0;
static const float BACKGROUND_PMREM_CONE = 0.16;
static const float SURFACE_FLAT_SLOPE_START = 0.018;
static const float SURFACE_FLAT_SLOPE_END = 0.16;

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
    float depth : SV_Depth;
};

struct RayMarchResult
{
    float3 color;
    float travel;
    float fieldId;
    float3 normal;
    float coverage;
    float stepCount;
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

float2 viewLocal(float2 p)
{
    return (p - viewCenter) / max(viewRadius, 0.001);
}

float2 viewUv(float2 p)
{
    return viewLocal(p) * 0.5 + 0.5;
}

float terrainHeight(float2 p)
{
    return heightFieldTexture.SampleLevel(linearSampler, saturate(viewUv(p)), 0.0).r;
}

float2 terrainGradient(float2 p)
{
    float2 uv = saturate(viewUv(p));
    float2 texel = 1.0 / HEIGHT_FIELD_TEXEL_COUNT;
    float texelWorld = max((viewRadius * 2.0) / HEIGHT_FIELD_TEXEL_COUNT, 0.001);

    float hLeft = heightFieldTexture.SampleLevel(linearSampler, uv - float2(texel.x, 0.0), 0.0).r;
    float hRight = heightFieldTexture.SampleLevel(linearSampler, uv + float2(texel.x, 0.0), 0.0).r;
    float hDown = heightFieldTexture.SampleLevel(linearSampler, uv - float2(0.0, texel.y), 0.0).r;
    float hUp = heightFieldTexture.SampleLevel(linearSampler, uv + float2(0.0, texel.y), 0.0).r;

    return float2(hRight - hLeft, hUp - hDown) / (texelWorld * 2.0);
}

bool traceHeightFieldSurfaceDirect(float3 origin, float3 direction, float intervalStart, float intervalEnd, out float3 hitPosition, out float travel)
{
    travel = max(intervalStart, 0.0);
    float previousTravel = travel;
    hitPosition = origin + direction * travel;
    float previousGap = hitPosition.z - terrainHeight(hitPosition.xy);
    float radius = max(viewRadius, 0.001);

    [loop]
    for (int stepIndex = 0; stepIndex < 96; stepIndex++)
    {
        hitPosition = origin + direction * travel;
        float2 local = (hitPosition.xy - viewCenter) / radius;
        if (length(local) > 1.08 && hitPosition.z < 4.0)
        {
            return false;
        }

        float gap = hitPosition.z - terrainHeight(hitPosition.xy);
        float hitEpsilon = max(0.002, travel * 0.00035);
        if (length(local) <= 1.0 && (abs(gap) <= hitEpsilon || (previousGap > 0.0 && gap <= 0.0)))
        {
            float alpha = previousGap / max(previousGap - gap, 0.0001);
            travel = lerp(previousTravel, travel, saturate(alpha));
            hitPosition = origin + direction * travel;
            return travel > intervalStart && travel < intervalEnd && travel < farDistance;
        }

        float2 slope = terrainGradient(hitPosition.xy);
        float terrainRate = abs(direction.z - dot(slope, direction.xy));
        float terrainStep = gap > 0.0 ? gap / max(terrainRate, 0.22) : 0.026;
        terrainStep = min(terrainStep * 0.62, max(viewRadius * 0.08, 0.026));
        previousTravel = travel;
        previousGap = gap;
        travel += max(terrainStep, 0.026);
        if (travel > intervalEnd || travel > farDistance)
        {
            return false;
        }
    }

    return false;
}

float3 studioPmremDirection(float3 worldDirection)
{
    return normalize(float3(worldDirection.x, worldDirection.z, worldDirection.y));
}

void directionBasis(float3 direction, out float3 tangent, out float3 bitangent)
{
    float3 up = abs(direction.z) > 0.94 ? float3(0.0, 1.0, 0.0) : float3(0.0, 0.0, 1.0);
    tangent = normalize(cross(up, direction));
    bitangent = cross(direction, tangent);
}

float3 studioPmremSample(float3 worldDirection, float lod)
{
    return studioPmremTexture.SampleLevel(linearSampler, studioPmremDirection(worldDirection), lod).rgb;
}

float3 studioPmremConeSample(float3 worldDirection, float lod, float cone)
{
    float3 direction = normalize(worldDirection);
    float3 tangent;
    float3 bitangent;
    directionBasis(direction, tangent, bitangent);

    float3 sum = studioPmremSample(direction, lod) * 2.0;
    sum += studioPmremSample(normalize(direction + tangent * cone), lod);
    sum += studioPmremSample(normalize(direction - tangent * cone), lod);
    sum += studioPmremSample(normalize(direction + bitangent * cone), lod);
    sum += studioPmremSample(normalize(direction - bitangent * cone), lod);
    sum += studioPmremSample(normalize(direction + (tangent + bitangent) * (cone * 0.7071)), lod);
    sum += studioPmremSample(normalize(direction + (-tangent + bitangent) * (cone * 0.7071)), lod);
    return sum * 0.125;
}

float3 surfaceMirrorRadiance(float3 p, float3 direction, out float3 normal)
{
    float2 gradient = terrainGradient(p.xy);
    normal = normalize(float3(-gradient.x, -gradient.y, 1.0));
    float3 reflectionDirection = reflect(direction, normal);
    float flatness = 1.0 - smoothstep(SURFACE_FLAT_SLOPE_START, SURFACE_FLAT_SLOPE_END, length(gradient));
    float lod = flatness * SURFACE_FLAT_REFLECTION_MAX_LOD;
    return studioPmremConeSample(reflectionDirection, lod, flatness * 0.055);
}

float3 backgroundRadiance(float3 direction)
{
    return studioPmremConeSample(direction, BACKGROUND_PMREM_LOD, BACKGROUND_PMREM_CONE);
}

RayMarchResult traverseRay(float3 origin, float3 direction)
{
    RayMarchResult result;
    result.color = backgroundRadiance(direction);
    result.travel = farDistance + 1.0;
    result.fieldId = 0.0;
    result.normal = 0.0;
    result.coverage = 0.0;
    result.stepCount = 0.0;

    float3 surfacePosition;
    float surfaceTravel;
    bool surfaceHit = traceHeightFieldSurfaceDirect(origin, direction, 0.0, farDistance, surfacePosition, surfaceTravel);
    if (surfaceHit)
    {
        float3 surfaceNormal;
        result.color = surfaceMirrorRadiance(surfacePosition, direction, surfaceNormal);
        result.travel = surfaceTravel;
        result.fieldId = FIELD_ID_HEIGHT_FIELD;
        result.normal = surfaceNormal;
        result.coverage = 1.0;
    }

    return result;
}

SceneOut D3D12ScenePS(VertexOut input)
{
    float2 screenUv = float2(input.uv.x, 1.0 - input.uv.y);
    float2 pixel = screenUv * resolution;
    float3 rayDirection = rayDirectionForPixel(pixel, jitterPixels, cameraPosition, viewCenter);

    RayMarchResult result = traverseRay(cameraPosition, rayDirection);

    SceneOut output;
    output.colorTravel = float4(result.color, min(result.travel, farDistance + 1.0));
    output.metadata = float4(result.fieldId, result.normal);
    output.control = float4(result.coverage, result.stepCount / 72.0, 0.0, 0.0);
    output.depth = saturate(result.travel / max(farDistance, 0.001));
    return output;
}
