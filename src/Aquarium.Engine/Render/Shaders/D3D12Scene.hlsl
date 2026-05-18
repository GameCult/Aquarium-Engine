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

float hash11(float value)
{
    return frac(sin(value * 127.1) * 43758.5453123);
}

float hash31(float3 p)
{
    return frac(sin(dot(p, float3(12.9898, 78.233, 37.719))) * 43758.5453);
}

float valueNoise3(float3 p)
{
    float3 cell = floor(p);
    float3 local = frac(p);
    local = local * local * (3.0 - 2.0 * local);

    float c000 = hash31(cell + float3(0.0, 0.0, 0.0));
    float c100 = hash31(cell + float3(1.0, 0.0, 0.0));
    float c010 = hash31(cell + float3(0.0, 1.0, 0.0));
    float c110 = hash31(cell + float3(1.0, 1.0, 0.0));
    float c001 = hash31(cell + float3(0.0, 0.0, 1.0));
    float c101 = hash31(cell + float3(1.0, 0.0, 1.0));
    float c011 = hash31(cell + float3(0.0, 1.0, 1.0));
    float c111 = hash31(cell + float3(1.0, 1.0, 1.0));

    float x00 = lerp(c000, c100, local.x);
    float x10 = lerp(c010, c110, local.x);
    float x01 = lerp(c001, c101, local.x);
    float x11 = lerp(c011, c111, local.x);
    float y0 = lerp(x00, x10, local.y);
    float y1 = lerp(x01, x11, local.y);
    return lerp(y0, y1, local.z);
}

float fractalNoise3(float3 p)
{
    float sum = 0.0;
    float amplitude = 0.5;
    [unroll]
    for (int octave = 0; octave < 5; octave++)
    {
        sum += valueNoise3(p) * amplitude;
        p = p * 2.03 + float3(13.7, 5.1, 9.3);
        amplitude *= 0.52;
    }

    return sum;
}

float3 ifsFoldSky(float3 p)
{
    [unroll]
    for (int index = 0; index < 5; index++)
    {
        p = abs(p) / max(dot(p, p), 0.18) - float3(0.72, 0.58, 0.49);
        p = p.yzx * float3(0.91, 1.07, 0.98);
    }

    return p;
}

float3 nebulaRadiance(float3 direction)
{
    float3 p = direction * 1.75 + float3(0.17, -0.41, 0.29);
    float3 folded = ifsFoldSky(p);
    float filaments = exp(-2.2 * length(folded.xy)) * 0.42;
    filaments += exp(-3.8 * abs(folded.x + folded.z * 0.28)) * 0.16;
    float cloud = pow(saturate(fractalNoise3(direction * 4.0 + folded * 0.22) - 0.22), 2.2);
    float veil = pow(saturate(1.0 - abs(direction.z * 1.18 + direction.x * 0.26)), 2.6);

    float3 cold = float3(0.025, 0.11, 0.28);
    float3 violet = float3(0.34, 0.08, 0.45);
    float3 ember = float3(0.86, 0.28, 0.10);
    float thermal = saturate(fractalNoise3(direction.zxy * 5.7 + 8.0));
    float3 color = lerp(cold, violet, thermal);
    color = lerp(color, ember, saturate(folded.z * 0.45 + 0.35));
    return color * (filaments + cloud * 0.32 + veil * 0.10);
}

float starClusterLayer(float3 direction, float scale, float threshold, float sharpness, float seed)
{
    float3 cell = floor(direction * scale + seed);
    float3 local = frac(direction * scale + seed) - 0.5;
    float n = hash31(cell);
    float3 offset = float3(hash31(cell + 13.0), hash31(cell + 37.0), hash31(cell + 71.0)) - 0.5;
    float d = length(local - offset * 0.72);
    float star = smoothstep(threshold, 1.0, n) * exp(-d * sharpness);

    float parent = hash31(floor(direction * (scale * 0.09) + seed * 0.31));
    return star * smoothstep(0.52, 0.98, parent);
}

float3 fractalStarClusters(float3 direction)
{
    float stars = 0.0;
    stars += starClusterLayer(direction, 110.0, 0.965, 42.0, 3.0) * 0.55;
    stars += starClusterLayer(direction, 360.0, 0.988, 72.0, 17.0) * 1.15;
    stars += starClusterLayer(direction, 1140.0, 0.996, 115.0, 43.0) * 1.8;

    float3 clusterP = ifsFoldSky(direction * 0.95 + float3(0.31, 0.07, -0.22));
    float clusterCore = exp(-18.0 * dot(clusterP.xy, clusterP.xy)) * smoothstep(0.12, 0.55, clusterP.z + 0.25);
    float3 warm = float3(1.0, 0.82, 0.55);
    float3 blue = float3(0.55, 0.70, 1.0);
    return lerp(blue, warm, hash11(direction.x + direction.y * 3.1)) * stars + warm * clusterCore * 2.8;
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
    float3 studio = studioPmremConeSample(direction, BACKGROUND_PMREM_LOD, BACKGROUND_PMREM_CONE) * 0.22;
    if (sceneFlags < 1.5)
    {
        return studio;
    }

    float3 space = float3(0.001, 0.003, 0.010);
    float3 nebula = nebulaRadiance(direction);
    float3 clusters = fractalStarClusters(direction);
    float darkDust = pow(saturate(fractalNoise3(direction * 9.0 + 2.4)), 3.2);
    nebula *= lerp(1.0, 0.34, darkDust);
    return space + nebula + clusters + studio * 0.45;
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
    bool traceHeightField = fmod(sceneFlags, 2.0) >= 1.0;
    bool surfaceHit = traceHeightField && traceHeightFieldSurfaceDirect(origin, direction, 0.0, farDistance, surfacePosition, surfaceTravel);
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
    float3 rayDirection = rayDirectionForPixel(pixel, jitterPixels, cameraPosition, cameraTarget);

    RayMarchResult result = traverseRay(cameraPosition, rayDirection);

    SceneOut output;
    output.colorTravel = float4(result.color, min(result.travel, farDistance + 1.0));
    output.metadata = float4(result.fieldId, result.normal);
    output.control = float4(result.coverage, result.stepCount / 72.0, 0.0, 0.0);
    output.depth = saturate(result.travel / max(farDistance, 0.001));
    return output;
}
