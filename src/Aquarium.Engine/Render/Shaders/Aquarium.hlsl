cbuffer AquariumFrame : register(b0)
{
    float2 resolution;
    float timeSeconds;
    float gridRadius;
    float3 cameraPosition;
    float _pad0;
};

static const float PI = 3.14159265359;
static const float FAR_DISTANCE = 90.0;
static const float SURFACE_EPSILON = 0.0015;
static const float3 SUN_POSITION = float3(0.0, 0.0, 2.2);
static const float SUN_RADIUS = 1.12;

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

float terrainHeight(float2 p)
{
    float2 warp = float2(
        fbm(p * 0.055 + float2(0.0, timeSeconds * 0.035)),
        fbm(p * 0.052 + float2(19.4, -timeSeconds * 0.025))) - 0.5;

    float low = fbm(p * 0.075 + warp * 2.5);
    float ridges = abs(fbm(p * 0.18 + warp * 4.5) - 0.5);
    float center = exp(-dot(p, p) * 0.006);

    return (low - 0.42) * 1.8 + ridges * 0.55 - center * 1.1;
}

float sphereSdf(float3 p, float3 center, float radius)
{
    return length(p - center) - radius;
}

float planetRadius(int index)
{
    return lerp(0.34, 0.62, hash21(float2(index, 19.7)));
}

float3 planetCenter(int index)
{
    float f = (float)index;
    float angle = f * 0.8975979 + timeSeconds * (0.08 + 0.011 * f);
    float radius = 4.1 + f * 0.77;
    float2 xy = float2(cos(angle), sin(angle)) * radius;
    return float3(xy, 1.15 + planetRadius(index) * 0.72);
}

float sceneSdf(float3 p, out int materialId)
{
    float terrain = p.z - terrainHeight(p.xy);
    float distanceToScene = terrain;
    materialId = 1;

    float sun = sphereSdf(p, SUN_POSITION, SUN_RADIUS);
    if (sun < distanceToScene)
    {
        distanceToScene = sun;
        materialId = 2;
    }

    [unroll]
    for (int i = 0; i < 7; i++)
    {
        float planet = sphereSdf(p, planetCenter(i), planetRadius(i));
        if (planet < distanceToScene)
        {
            distanceToScene = planet;
            materialId = 3;
        }
    }

    return distanceToScene;
}

float sceneDistance(float3 p)
{
    int materialId;
    return sceneSdf(p, materialId);
}

float3 sceneNormal(float3 p)
{
    float2 e = float2(0.0025, 0.0);
    return normalize(float3(
        sceneDistance(p + e.xyy) - sceneDistance(p - e.xyy),
        sceneDistance(p + e.yxy) - sceneDistance(p - e.yxy),
        sceneDistance(p + e.yyx) - sceneDistance(p - e.yyx)));
}

float gridLine(float2 p)
{
    float2 cell = abs(frac(p) - 0.5);
    float lineDistance = min(cell.x, cell.y);
    float width = 0.018;
    return 1.0 - smoothstep(width, width + 0.01, lineDistance);
}

float terrainMask(float2 p)
{
    float r = length(p) / gridRadius;
    return 1.0 - smoothstep(0.78, 1.0, r);
}

bool raymarch(float3 rayOrigin, float3 rayDirection, out float3 hitPosition, out int materialId, out float travel)
{
    travel = 0.0;
    materialId = 0;

    [loop]
    for (int stepIndex = 0; stepIndex < 96; stepIndex++)
    {
        hitPosition = rayOrigin + rayDirection * travel;
        float fade = terrainMask(hitPosition.xy);
        if (fade <= 0.0001 && hitPosition.z < 4.0)
        {
            return false;
        }

        float distanceToScene = sceneSdf(hitPosition, materialId);
        if (distanceToScene < SURFACE_EPSILON * max(1.0, travel * 0.15))
        {
            return true;
        }

        travel += max(distanceToScene * 0.72, 0.018);
        if (travel > FAR_DISTANCE)
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
    float contour = 1.0 - smoothstep(0.015, 0.04, abs(frac(terrainHeight(position.xy) * 1.2) - 0.5));
    float flow = fbm(position.xy * 0.23 + timeSeconds * 0.035);
    float3 baseColor = lerp(float3(0.02, 0.19, 0.14), float3(0.05, 0.62, 0.52), flow);
    float3 lineColor = float3(0.8, 1.0, 0.82) * (gridAmount * 0.85 + contour * 0.16);
    float diffuse = nDotL * attenuation * 0.85;
    float selfGlow = exp(-distance(position, SUN_POSITION) * 0.34) * 0.34;

    return (baseColor * (diffuse + selfGlow) + lineColor) * mask;
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

float4 AquariumPS(VertexOut input) : SV_Target
{
    float2 pixel = input.uv * resolution;
    float2 ndc = (pixel * 2.0 - resolution) / resolution.y;

    float3 target = float3(0.0, 0.0, 0.0);
    float3 forward = normalize(target - cameraPosition);
    float3 right = normalize(cross(forward, float3(0.0, 0.0, 1.0)));
    float3 up = cross(right, forward);
    float3 rayDirection = normalize(forward * 1.6 + right * ndc.x + up * ndc.y);

    float3 hitPosition;
    int materialId;
    float travel;
    float3 color = 0.0;

    if (raymarch(cameraPosition, rayDirection, hitPosition, materialId, travel))
    {
        float3 normal = sceneNormal(hitPosition);
        color = shade(hitPosition, normal, materialId, rayDirection);
        color *= exp(-travel * 0.012);
    }

    color += float3(0.001, 0.003, 0.004);
    return float4(aces(color), 1.0);
}
