cbuffer AquariumFrame : register(b0)
{
    float2 resolution;
    float timeSeconds;
    float gridRadius;
    float3 cameraPosition;
    float _pad0;
    float2 gridCenter;
    float2 _pad1;
};

Texture2D<float4> gridHeightTexture : register(t0);
SamplerState gridSampler : register(s0);

static const float PI = 3.14159265359;
static const float FAR_DISTANCE = 90.0;
static const float SURFACE_EPSILON = 0.0015;
static const float3 SUN_POSITION = float3(0.0, 0.0, 2.2);
static const float SUN_RADIUS = 1.12;
static const float GRID_WEATHER_WORLD_SCALE = 42.0;
static const float GRID_LINE_WORLD_CELL = 2.0;
static const int PLANET_COUNT = 5;
static const float GRID_HEIGHT_TEXEL_COUNT = 128.0;

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

float ridge(float value)
{
    return 1.0 - abs(value * 2.0 - 1.0);
}

float powerPulse(float distanceValue, float radius, float power)
{
    float normalized = saturate(distanceValue / max(radius, 0.001));
    float shaped = pow(1.0 - normalized, power);
    return shaped * shaped * (3.0 - 2.0 * shaped);
}

float2 gridLocal(float2 p)
{
    return (p - gridCenter) / max(gridRadius, 0.001);
}

float terrainMask(float2 p)
{
    float r = length(gridLocal(p));
    return 1.0 - smoothstep(0.78, 1.0, r);
}

float3 gridWeatherColor(float2 p, float height)
{
    float radius = length(gridLocal(p));
    float edge = terrainMask(p);
    float2 worldDomain = p / GRID_WEATHER_WORLD_SCALE;
    float2 lowWarp = float2(
        fbm(worldDomain * 1.35 + float2(0.0, height * 0.10 + timeSeconds * 0.018)),
        fbm(worldDomain.yx * 1.17 + float2(4.7, -2.3 + height * 0.08 - timeSeconds * 0.014))) - 0.5;
    float2 drift = float2(0.018, -0.011) * timeSeconds;
    float2 warped = worldDomain + lowWarp * 0.36 + drift;
    float sheet = pow(saturate(fbm(warped * 2.65 + height * 0.12) * 0.85), 1.25);
    float2 fineWarp = float2(
        fbm(warped * 4.8 + float2(1.9, 7.1 + timeSeconds * 0.041)),
        fbm(warped.yx * 4.2 + float2(-3.4, 5.6 - timeSeconds * 0.036))) - 0.5;
    float filamentNoise = fbm((warped + fineWarp * 0.075) * 9.5 + height * 0.24);
    float filament = pow(saturate(ridge(filamentNoise)), 3.4);
    float horizonDark = smoothstep(0.52, 1.02, radius);

    float3 deep = float3(0.006, 0.026, 0.045);
    float3 blue = float3(0.025, 0.20, 0.34);
    float3 teal = float3(0.09, 0.68, 0.62);
    float3 green = float3(0.55, 0.92, 0.36);
    float3 color = lerp(deep, lerp(blue, teal, sheet), edge);
    color += green * filament * edge * 0.18;
    color *= lerp(1.0, 0.22, horizonDark);
    return color;
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

float2 gridUv(float2 p)
{
    return gridLocal(p) * 0.5 + 0.5;
}

float2 gridWorld(float2 uv)
{
    return gridCenter + (uv * 2.0 - 1.0) * gridRadius;
}

float analyticTerrainHeight(float2 p)
{
    float positive = 0.0;
    float negative = 0.0;

    float selfWell = powerPulse(length(p - SUN_POSITION.xy), 8.5, 2.85);
    float selfWave = sin(length(p - SUN_POSITION.xy) * 1.2 - timeSeconds * 0.74);
    float selfHeight = -selfWell * 1.34 + selfWave * selfWell * 0.055;
    positive += max(selfHeight, 0.0);
    negative += max(-selfHeight, 0.0);

    [unroll]
    for (int i = 0; i < PLANET_COUNT; i++)
    {
        float2 delta = p - planetCenter(i).xy;
        float well = powerPulse(length(delta), 3.8 + planetRadius(i) * 2.5, 2.1);
        float wave = sin(length(delta) * 2.4 - timeSeconds * 1.35);
        float signedHeight = -well * 0.42 + wave * well * 0.022;
        positive += max(signedHeight, 0.0);
        negative += max(-signedHeight, 0.0);
    }

    float slow = sin((p.x * 0.08 + p.y * 0.06) + timeSeconds * 0.27)
        * sin((p.x * -0.04 + p.y * 0.07) - timeSeconds * 0.19) * 0.035;
    positive += max(slow, 0.0);
    negative += max(-slow, 0.0);
    return positive - negative;
}

float terrainHeight(float2 p)
{
    float2 uv = saturate(gridUv(p));
    return gridHeightTexture.SampleLevel(gridSampler, uv, 0.0).r;
}

float sphereSdf(float3 p, float3 center, float radius)
{
    return length(p - center) - radius;
}

float planetDisplacement(float3 localPosition, float radius, int index, bool isSelf)
{
    float3 domain = localPosition / max(radius, 0.001);
    float seed = hash21(float2(index, 41.17)) * 19.0;
    float broad = fbm3(domain * 1.55 + seed + timeSeconds * (isSelf ? 0.12 : 0.0));
    float fine = fbm3(domain * 5.2 + seed * 1.73 + timeSeconds * (isSelf ? -0.18 : 0.0));
    float ridged = pow(saturate(ridge(fine * 0.5 + 0.5)), isSelf ? 1.6 : 2.8);
    float amplitude = isSelf ? 0.20 : 0.105;
    return (broad * 0.62 + ridged * 0.38) * radius * amplitude;
}

float displacedSphereSdf(float3 p, float3 center, float radius, int index, bool isSelf)
{
    float3 local = p - center;
    float baseDistance = length(local) - radius;
    float maxDisplacement = radius * (isSelf ? 0.20 : 0.105);

    if (baseDistance > maxDisplacement * 1.5)
    {
        return baseDistance - maxDisplacement;
    }

    float actualDistance = baseDistance - planetDisplacement(local, radius, index, isSelf);
    float conservativeDistance = baseDistance - maxDisplacement;
    float blend = 1.0 - smoothstep(maxDisplacement * 0.25, maxDisplacement * 1.5, baseDistance);
    return lerp(conservativeDistance, actualDistance, blend);
}

float sceneSdf(float3 p, out int materialId)
{
    float terrain = p.z - terrainHeight(p.xy);
    float distanceToScene = terrain;
    materialId = 1;

    float sun = displacedSphereSdf(p, SUN_POSITION, SUN_RADIUS, 17, true);
    if (sun < distanceToScene)
    {
        distanceToScene = sun;
        materialId = 2;
    }

    [unroll]
    for (int i = 0; i < PLANET_COUNT; i++)
    {
        float planet = displacedSphereSdf(p, planetCenter(i), planetRadius(i), i, false);
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

float3 terrainNormal(float3 p)
{
    float2 uv = saturate(gridUv(p.xy));
    float2 texel = 1.0 / GRID_HEIGHT_TEXEL_COUNT;
    float texelWorld = max((gridRadius * 2.0) / GRID_HEIGHT_TEXEL_COUNT, 0.001);

    float hLeft = gridHeightTexture.SampleLevel(gridSampler, uv - float2(texel.x, 0.0), 0.0).r;
    float hRight = gridHeightTexture.SampleLevel(gridSampler, uv + float2(texel.x, 0.0), 0.0).r;
    float hDown = gridHeightTexture.SampleLevel(gridSampler, uv - float2(0.0, texel.y), 0.0).r;
    float hUp = gridHeightTexture.SampleLevel(gridSampler, uv + float2(0.0, texel.y), 0.0).r;

    float3 tangentX = float3(texelWorld * 2.0, 0.0, hRight - hLeft);
    float3 tangentY = float3(0.0, texelWorld * 2.0, hUp - hDown);
    float3 normal = normalize(cross(tangentX, tangentY));

    if (normal.z < 0.0)
    {
        normal = -normal;
    }

    return normal;
}

void nearestBody(float3 p, out float3 center, out float radius, out int index, out bool isSelf)
{
    float bestDistance = abs(length(p - SUN_POSITION) - SUN_RADIUS);
    center = SUN_POSITION;
    radius = SUN_RADIUS;
    index = 17;
    isSelf = true;

    [unroll]
    for (int i = 0; i < PLANET_COUNT; i++)
    {
        float3 bodyCenter = planetCenter(i);
        float bodyRadius = planetRadius(i);
        float distanceToBody = abs(length(p - bodyCenter) - bodyRadius);
        if (distanceToBody < bestDistance)
        {
            bestDistance = distanceToBody;
            center = bodyCenter;
            radius = bodyRadius;
            index = i;
            isSelf = false;
        }
    }
}

float3 planetNormal(float3 p)
{
    float3 center;
    float radius;
    int index;
    bool isSelf;
    nearestBody(p, center, radius, index, isSelf);

    float3 local = p - center;
    float3 radial = normalize(local);
    float3 domain = local / max(radius, 0.001);
    float seed = hash21(float2(index, 41.17)) * 19.0;
    float normalFrequency = isSelf ? 5.4 : 7.5;
    float3 detail = float3(
        noised3(domain * normalFrequency + seed),
        noised3(domain.yzx * normalFrequency + seed + 7.13),
        noised3(domain.zxy * normalFrequency + seed + 13.71));
    detail -= radial * dot(detail, radial);
    return normalize(radial + detail * (isSelf ? 0.32 : 0.24));
}

float3 surfaceNormal(float3 p, int materialId)
{
    if (materialId == 1)
    {
        return terrainNormal(p);
    }

    return planetNormal(p);
}

float gridLine(float2 p)
{
    float2 cell = abs(frac(p / GRID_LINE_WORLD_CELL) - 0.5);
    float lineDistance = min(cell.x, cell.y);
    return (1.0 - smoothstep(0.018, 0.042, lineDistance * GRID_LINE_WORLD_CELL)) * terrainMask(p);
}

bool raymarch(float3 rayOrigin, float3 rayDirection, out float3 hitPosition, out int materialId, out float travel)
{
    travel = 0.0;
    materialId = 0;

    [loop]
    for (int stepIndex = 0; stepIndex < 52; stepIndex++)
    {
        hitPosition = rayOrigin + rayDirection * travel;
        float fade = terrainMask(hitPosition.xy);
        if (fade <= 0.0001 && hitPosition.z < 4.0)
        {
            return false;
        }

        float distanceToScene = sceneSdf(hitPosition, materialId);
        float hitEpsilon = max(SURFACE_EPSILON, travel * 0.00035);
        if (distanceToScene < hitEpsilon)
        {
            hitPosition = rayOrigin + rayDirection * travel;
            return true;
        }

        travel += max(distanceToScene * 0.86, 0.026);
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
    float3 baseColor = gridWeatherColor(position.xy, position.z);
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
    float2 screenUv = float2(input.uv.x, 1.0 - input.uv.y);
    float2 pixel = screenUv * resolution;
    float2 ndc = (pixel * 2.0 - resolution) / resolution.y;

    float3 target = float3(gridCenter, 0.0);
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
        float3 normal = surfaceNormal(hitPosition, materialId);
        color = shade(hitPosition, normal, materialId, rayDirection);
        color *= exp(-travel * 0.012);
    }

    color += float3(0.001, 0.003, 0.004);
    return float4(aces(color), 1.0);
}

float4 GridHeightPS(VertexOut input) : SV_Target
{
    float2 uv = saturate(input.uv);
    float2 world = gridWorld(uv);
    float height = analyticTerrainHeight(world);
    return float4(height, 0.0, 0.0, 1.0);
}
