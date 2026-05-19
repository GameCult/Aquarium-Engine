static const int SDF_INDEX = 0;
static const int ZYPHOS_GEOMETRY_BRUSH_LIMIT = 4;

#include "D3D12SdfCommon.hlsli"
#include "D3D12SdfMath.hlsli"

float3 zyRotateZ(float3 p, float angle)
{
    float s = sin(angle);
    float c = cos(angle);
    return float3(c * p.x - s * p.y, s * p.x + c * p.y, p.z);
}

float3 zyPlanetDir(float3 local, SdfObject sdfObject)
{
    return normalize(zyRotateZ(local, -sdfObject.state.y));
}

float3 zyPrimaryStarDirectionLocal(SdfObject sdfObject)
{
    return normalize(float3(cos(sdfObject.state.w), sin(sdfObject.state.w), 0.18));
}

float zyUmbrosEclipse(float3 starDirectionLocal)
{
    const float umbrosAngularRadius = 0.1127;
    float alignment = dot(normalize(starDirectionLocal), float3(1.0, 0.0, 0.0));
    return smoothstep(cos(umbrosAngularRadius * 1.45), cos(umbrosAngularRadius * 0.45), alignment);
}

float zySphericalField(float3 dir)
{
    float latitude = asin(saturate(abs(dir.z)) * 2.0 - 1.0);
    float longitude = atan2(dir.y, dir.x);
    float plates =
        sin(longitude * 2.0 + dir.z * 4.5) * 0.28 +
        sin(longitude * 3.0 - dir.z * 7.0 + 1.7) * 0.22 +
        sin((dir.x + dir.y * 0.7 + dir.z * 0.4) * 8.0) * 0.16 +
        sin((dir.x * 1.3 - dir.y + dir.z * 1.9) * 13.0) * 0.08;
    float equatorialWarmth = cos(latitude) * 0.18;
    return 0.50 + plates + equatorialWarmth;
}

float zyHash21(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float zyCompactBrush(float2 delta, float2 radii, float rotation, float falloff, float shapePower)
{
    float c = cos(rotation);
    float s = sin(rotation);
    float2 local = float2(delta.x * c + delta.y * s, -delta.x * s + delta.y * c);
    float2 normalized = local / max(radii, float2(0.001, 0.001));
    float r2 = dot(normalized, normalized);
    if (r2 >= 1.0)
    {
        return 0.0;
    }

    float edgeValue = exp(-falloff);
    float gaussianValue = exp(-falloff * r2);
    float compactValue = (gaussianValue - edgeValue) / max(1.0 - edgeValue, 0.000001);
    return pow(saturate(compactValue), shapePower);
}

float2 zyCubeFaceUv(float3 dir, out float face)
{
    float3 a = abs(dir);
    if (a.x >= a.y && a.x >= a.z)
    {
        if (dir.x >= 0.0)
        {
            face = 0.0;
            return float2(-dir.z, dir.y) / max(a.x, 0.0001);
        }

        face = 1.0;
        return float2(dir.z, dir.y) / max(a.x, 0.0001);
    }

    if (a.y >= a.z)
    {
        if (dir.y >= 0.0)
        {
            face = 2.0;
            return float2(dir.x, -dir.z) / max(a.y, 0.0001);
        }

        face = 3.0;
        return float2(dir.x, dir.z) / max(a.y, 0.0001);
    }

    if (dir.z >= 0.0)
    {
        face = 4.0;
        return dir.xy / max(a.z, 0.0001);
    }

    face = 5.0;
    return float2(-dir.x, dir.y) / max(a.z, 0.0001);
}

float2 zyTileBrushPlane(float2 faceUv, float level, float tileX, float tileY, out float inTile)
{
    float axisTiles = exp2(level);
    float2 local01 = (faceUv * 0.5 + 0.5) * axisTiles - float2(tileX, tileY);
    float2 inside = step(float2(0.0, 0.0), local01) * step(local01, float2(1.0, 1.0));
    inTile = inside.x * inside.y;
    return (local01 * 2.0 - 1.0) * 30.0;
}

float zyAuthoredBrushTerrainLimited(float3 dir, int brushLimit, out float materialMask)
{
    float face;
    float2 faceUv = zyCubeFaceUv(dir, face);
    float height = 0.0;
    materialMask = 0.0;

    [loop]
    for (int index = 0; index < brushLimit; index++)
    {
        float4 centerRadius = brushCenterRadius[index];
        float4 shape = brushShape[index];
        float4 domain = brushDomain[index];
        if (centerRadius.z <= 0.0 || abs(domain.x - face) > 0.25)
        {
            continue;
        }

        float inTile;
        float2 plane = zyTileBrushPlane(faceUv, domain.y, domain.z, domain.w, inTile);
        if (inTile <= 0.0)
        {
            continue;
        }

        float2 radii = float2(centerRadius.z, centerRadius.w > 0.0 ? centerRadius.w : centerRadius.z);
        float weight = zyCompactBrush(plane - centerRadius.xy, radii, shape.z, max(shape.w, 0.001), max(shape.x, 0.001));
        height += shape.y * weight;
        materialMask = max(materialMask, abs(shape.y) * weight);
    }

    return height;
}

float zyAuthoredBrushTerrain(float3 dir, out float materialMask)
{
    return zyAuthoredBrushTerrainLimited(dir, 64, materialMask);
}

float zyTileRelief(float2 uv, float level, float amplitude, float ridgeBias)
{
    float scale = exp2(level);
    float2 cell = floor(uv * scale);
    float2 local = frac(uv * scale) - 0.5;
    float seed = zyHash21(cell + level * 17.0);
    float angle = seed * 6.2831853;
    float2 axis = float2(cos(angle), sin(angle));
    float ridge = 1.0 - smoothstep(0.035, 0.18, abs(dot(local, axis) + (seed - 0.5) * 0.16));
    float pit = 1.0 - smoothstep(0.06, 0.32, length(local - axis * 0.18));
    return (ridge * ridgeBias - pit * (1.0 - ridgeBias)) * amplitude;
}

float zyQuadtreeSdfRelief(float3 dir)
{
    float2 equatorial = dir.xy * 0.5 + 0.5;
    float2 polar = float2(atan2(dir.y, dir.x) * 0.15915494 + 0.5, abs(dir.z));
    float polarBlend = smoothstep(0.58, 0.92, abs(dir.z));
    float2 uv = lerp(equatorial, polar, polarBlend);
    float relief = 0.0;
    relief += zyTileRelief(uv + 0.07, 2.0, 0.030, 0.68);
    relief += zyTileRelief(uv * 1.37 + 0.31, 3.0, 0.019, 0.55);
    relief += zyTileRelief(uv * 2.13 - 0.19, 4.0, 0.010, 0.45);
    return relief;
}

float zyDomainMask(float3 dir, float3 center, float width)
{
    return smoothstep(cos(width), cos(width * 0.35), dot(dir, normalize(center)));
}

float zyLeafCluster(float3 dir)
{
    float forest = zyDomainMask(dir, float3(0.32, 0.68, 0.16), 0.42);
    float leafCells =
        sin(dir.x * 91.0 + dir.y * 37.0) *
        sin(dir.y * 113.0 - dir.z * 53.0) *
        sin((dir.x + dir.z) * 157.0);
    float leaf = smoothstep(0.36, 0.92, leafCells * 0.5 + 0.5);
    return forest * leaf;
}

float zyPebbleCluster(float3 dir)
{
    float coast = zyDomainMask(dir, float3(0.57, -0.28, 0.10), 0.24);
    float grains = sin(dir.x * 173.0 - dir.y * 71.0) * sin(dir.z * 127.0 + dir.y * 59.0);
    return coast * smoothstep(0.48, 0.94, grains * 0.5 + 0.5);
}

float zyTerrainOffset(float3 dir, SdfObject sdfObject)
{
    float field = zySphericalField(dir);
    float seaLevel = sdfObject.state.z;
    float land = smoothstep(seaLevel - 0.04, seaLevel + 0.08, field);
    float mountain = pow(saturate(field - seaLevel), 1.65);
    float polarCap = pow(abs(dir.z), 8.0) * 0.035;
    float authoredMaterial;
    float authoredGeometry = zyAuthoredBrushTerrainLimited(dir, ZYPHOS_GEOMETRY_BRUSH_LIMIT, authoredMaterial);
    float tileRelief = zyQuadtreeSdfRelief(dir) * land + zyLeafCluster(dir) * 0.018 + zyPebbleCluster(dir) * 0.010;
    return (field - seaLevel) * 0.10 + mountain * 0.13 + polarCap * land + tileRelief + authoredGeometry * 0.52;
}

float sdfDistance(float3 p, int sdfIndex)
{
    SdfObject sdfObject = sdfObjects[sdfIndex];
    float planetRadius = max(sdfObject.state.x, 0.001);
    float3 local = p - sdfObject.centerRadius.xyz;
    float3 dir = zyPlanetDir(local, sdfObject);
    return length(local) - (planetRadius + zyTerrainOffset(dir, sdfObject));
}

SdfSurface sdfSurface(float3 p, int sdfIndex)
{
    SdfObject sdfObject = sdfObjects[sdfIndex];
    float3 local = p - sdfObject.centerRadius.xyz;
    float3 dir = zyPlanetDir(local, sdfObject);
    float field = zySphericalField(dir);
    float seaLevel = sdfObject.state.z;
    float land = smoothstep(seaLevel - 0.03, seaLevel + 0.06, field);
    float mountain = smoothstep(seaLevel + 0.12, seaLevel + 0.28, field);
    float tileRelief = zyQuadtreeSdfRelief(dir);
    float authoredMaterial;
    float authoredHeight = zyAuthoredBrushTerrain(dir, authoredMaterial);
    float leafCluster = zyLeafCluster(dir);
    float pebbleCluster = zyPebbleCluster(dir);
    float polar = smoothstep(0.72, 0.92, abs(dir.z));
    float cloud = smoothstep(0.73, 0.91, sin(dir.x * 18.0 + dir.y * 13.0 + dir.z * 9.0 + timeSeconds * 0.19) * 0.5 + 0.5);

    float3 ocean = lerp(float3(0.004, 0.070, 0.130), float3(0.010, 0.220, 0.330), saturate(field));
    float3 lowland = float3(0.070, 0.300, 0.185);
    float3 highland = float3(0.540, 0.405, 0.220);
    float3 snow = float3(0.800, 0.865, 0.830);
    float3 landColor = lerp(lowland, highland, mountain);
    landColor = lerp(landColor, snow, saturate(polar + mountain * 0.38));
    landColor = lerp(landColor, float3(0.62, 0.54, 0.36), saturate(tileRelief * 14.0));
    landColor = lerp(landColor, float3(0.78, 0.56, 0.30), saturate(authoredMaterial * 2.6 + authoredHeight * 5.0));
    landColor = lerp(landColor, float3(0.025, 0.24, 0.08), leafCluster * 0.82);
    landColor = lerp(landColor, float3(0.42, 0.39, 0.34), pebbleCluster * 0.65);

    SdfSurface surface;
    surface.baseColor = lerp(ocean, landColor, land);
    surface.baseColor = lerp(surface.baseColor, float3(0.78, 0.84, 0.80), cloud * 0.16);
    surface.metallic = 0.0;
    surface.roughness = lerp(0.34, 0.84, land);
    surface.emission = 0.0;
    surface.temporalDetail = saturate(authoredMaterial * 3.0);
    return surface;
}

float3 shadeSdf(float2 uv, float travel, float3 p, float3 normal, int sdfIndex, SdfSurface surface)
{
    SdfObject sdfObject = sdfObjects[sdfIndex];
    float3 dir = zyPlanetDir(p - sdfObject.centerRadius.xyz, sdfObject);
    float3 viewDirection = normalize(cameraPosition - p);
    float3 starDirectionLocal = zyPrimaryStarDirectionLocal(sdfObject);
    float eclipse = zyUmbrosEclipse(starDirectionLocal);
    float daylight = saturate(dot(dir, starDirectionLocal) * 0.74 + 0.24) * (1.0 - eclipse * 0.86);
    float night = saturate(-dot(dir, starDirectionLocal) * 1.7 - 0.20 + eclipse * 0.55);
    float citySeed = sin(dir.x * 43.0 + sin(dir.y * 19.0) * 4.0 + dir.z * 31.0);
    float cityWeb = smoothstep(0.82, 0.96, citySeed * 0.5 + 0.5);
    float coast = 1.0 - smoothstep(0.020, 0.070, abs(zySphericalField(dir) - sdfObject.state.z));
    float fresnel = pow(1.0 - saturate(dot(normal, viewDirection)), 3.2);
    float3 city = float3(1.0, 0.54, 0.14) * cityWeb * coast * night * 1.25;
    float3 eclipseTint = float3(0.15, 0.19, 0.26) * eclipse * saturate(dot(dir, starDirectionLocal) * 0.5 + 0.5);
    float3 atmosphere = float3(0.07, 0.38, 0.78) * (fresnel * 1.35 + pow(fresnel, 6.0) * 2.6);

    return shadeSdfPbr(p, normal, surface) * daylight + city + atmosphere + eclipseTint;
}

#include "D3D12SdfProxy.hlsli"
