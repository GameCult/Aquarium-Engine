static const int SDF_INDEX = 2;

#include "D3D12SdfCommon.hlsli"
#include "D3D12SdfMath.hlsli"

struct ImaginationParts
{
    float seed;
    float film;
    float rim;
    float tendrils;
    float sparks;
    float shadow;
    float distanceValue;
};

float sdImaginationSeed(float3 local)
{
    float3 p = local - float3(0.0, 0.0, 0.03);
    float egg = sdEllipsoid(p, float3(0.18, 0.18, 0.32));
    float bottomTuck = local.z + 0.22;
    return max(egg, -bottomTuck);
}

float pairCenterOut(float u, float activity)
{
    return 0.09 + 1.28 * u - 0.76 * u * u + 0.030 * activity * sin(u * 3.14159);
}

float pairCenterSide(float u, float curl)
{
    return curl * 0.30 * sin(u * 3.14159);
}

float pairWidth(float u, float activity)
{
    float blade = sin(saturate(u) * 3.14159);
    return 0.012 + blade * blade * (0.17 + activity * 0.045);
}

float pairThickness(float u)
{
    float blade = sin(saturate(u) * 3.14159);
    return 0.010 + blade * blade * 0.012;
}

float sdSheetPair(float3 local, float angle, float activity, float curl, out float film, out float rim)
{
    float2 radial = float2(cos(angle), sin(angle));
    float2 tangent = float2(-radial.y, radial.x);
    float3 q = float3(dot(local.xy, radial), dot(local.xy, tangent), local.z);
    float uRaw = (q.z + 0.25) / 1.04;
    float u = saturate(uRaw);
    float sideCenter = pairCenterSide(u, curl);
    float centerOut = pairCenterOut(u, activity);
    float width = pairWidth(u, activity);
    float thickness = pairThickness(u);
    float blade = sin(u * 3.14159);
    float lipCurl = smoothstep(0.60, 1.0, u) * 0.10 * abs(q.y);
    float outDistance = abs(q.x - centerOut + lipCurl) - thickness;
    float sideDistance = abs(abs(q.y) - sideCenter) - width;
    float endDistance = max(-uRaw, uRaw - 1.0) * 1.04;
    film = max(max(outDistance, sideDistance), endDistance);

    float edgeOffset = abs(q.y) - (sideCenter + width);
    float edgeRadius = 0.018 + 0.010 * blade;
    float edgeTube = length(float2(q.x - centerOut + lipCurl, edgeOffset)) - edgeRadius;
    float rootFold = length(float2(q.x - 0.10, abs(q.y) - sideCenter)) - (0.030 + 0.014 * smoothstep(0.0, 0.18, u));
    rim = max(min(edgeTube, rootFold), endDistance);
    return smoothUnion(film, rim, 0.018);
}

float sdCandidateTendril(float3 local, float angle, float height, float phase)
{
    float2 radial = float2(cos(angle), sin(angle));
    float3 a = float3(radial * 0.03, 0.02);
    float3 b = float3(radial * (0.08 + 0.04 * sin(phase)), height * 0.52);
    float3 c = float3(radial * (0.16 + 0.05 * cos(phase)), height);
    return sdQuadraticTube(local, a, b, c, 0.010, 0.004);
}

float sdImaginationSparks(float3 local, float phase, float activity)
{
    float spark0 = sdSphere(local - float3(cos(phase) * 0.24, sin(phase) * 0.24, 0.58), 0.030);
    float spark1 = sdSphere(local - float3(cos(phase + 2.31) * 0.16, sin(phase + 2.31) * 0.16, 0.42), 0.022 + activity * 0.008);
    float spark2 = sdSphere(local - float3(cos(phase + 4.42) * 0.28, sin(phase + 4.42) * 0.28, 0.70), 0.020);
    return min(spark0, min(spark1, spark2));
}

ImaginationParts imaginationParts(float3 local, SdfObject sdfObject, float timeSeconds)
{
    float activity = saturate(sdfObject.state.x);
    float heartbeat = saturate(sdfObject.state.y);
    float phase = timeSeconds * 0.18 + heartbeat * 1.7;
    float curl = lerp(0.55, 1.25, activity);
    float seed = sdImaginationSeed(local);

    float film0;
    float rim0;
    float sheet0 = sdSheetPair(local, phase + 0.00, activity, curl, film0, rim0);
    float film1;
    float rim1;
    float sheet1 = sdSheetPair(local, phase + 2.09, activity, curl, film1, rim1);
    float film2;
    float rim2;
    float sheet2 = sdSheetPair(local, phase + 4.18, activity, curl, film2, rim2);

    float film = min(film0, min(film1, film2));
    float rim = min(rim0, min(rim1, rim2));
    float sheets = min(sheet0, min(sheet1, sheet2));

    float tendril0 = sdCandidateTendril(local, phase + 0.40, 0.78, timeSeconds * 0.50);
    float tendril1 = sdCandidateTendril(local, phase + 2.70, 0.62, timeSeconds * 0.47 + 1.7);
    float tendrils = min(tendril0, tendril1);
    float sparks = sdImaginationSparks(local, phase * 2.1, activity);
    float shadow = 4.0;

    float bloom = smoothUnion(seed, sheets, 0.050);
    bloom = smoothUnion(bloom, tendrils, 0.018);
    bloom = smoothUnion(bloom, sparks, 0.020);

    ImaginationParts parts;
    parts.seed = seed;
    parts.film = film;
    parts.rim = rim;
    parts.tendrils = tendrils;
    parts.sparks = sparks;
    parts.shadow = shadow;
    parts.distanceValue = smoothUnion(bloom, shadow, 0.030);
    return parts;
}

float sdfDistance(float3 p, int sdfIndex)
{
    SdfObject sdfObject = sdfObjects[sdfIndex];
    float radius = max(sdfObject.centerRadius.w, 0.001);
    float3 local = (p - sdfObject.centerRadius.xyz) / radius;
    return imaginationParts(local, sdfObject, timeSeconds).distanceValue * radius;
}

SdfSurface sdfSurface(float3 p, int sdfIndex)
{
    SdfObject sdfObject = sdfObjects[sdfIndex];
    float radius = max(sdfObject.centerRadius.w, 0.001);
    float3 local = (p - sdfObject.centerRadius.xyz) / radius;
    ImaginationParts parts = imaginationParts(local, sdfObject, timeSeconds);
    float bloomPart = min(parts.seed, min(parts.film, parts.rim));
    float isSpark = parts.sparks <= min(bloomPart, parts.shadow) ? 1.0 : 0.0;
    float isTendril = (1.0 - isSpark) * (parts.tendrils <= min(bloomPart, parts.shadow) ? 1.0 : 0.0);
    float isShadow = (1.0 - isSpark) * (1.0 - isTendril) * (parts.shadow <= bloomPart ? 1.0 : 0.0);
    float isRim = (1.0 - isSpark) * (1.0 - isTendril) * (1.0 - isShadow) * (parts.rim <= min(parts.seed, parts.film) ? 1.0 : 0.0);
    float isFilm = (1.0 - isSpark) * (1.0 - isTendril) * (1.0 - isShadow) * (1.0 - isRim) * (parts.film <= parts.seed ? 1.0 : 0.0);
    float isSeed = (1.0 - isSpark) * (1.0 - isTendril) * (1.0 - isShadow) * (1.0 - isRim) * (1.0 - isFilm);
    float shimmer = 0.5 + 0.5 * sin(local.x * 5.0 - local.y * 3.0 + local.z * 4.0 + timeSeconds * 1.2);
    float3 seedColor = lerp(float3(0.34, 0.16, 0.78), float3(0.88, 0.78, 1.0), shimmer);
    float3 filmColor = lerp(float3(0.22, 0.72, 1.0), float3(1.0, 0.50, 0.94), saturate(sdfObject.state.x));
    float3 rimColor = lerp(float3(0.72, 1.0, 1.0), float3(1.0, 0.78, 0.34), shimmer);
    float3 sparkColor = float3(1.0, 0.74, 0.26);
    float3 shadowColor = float3(0.08, 0.07, 0.20);

    SdfSurface surface;
    surface.baseColor = seedColor * isSeed + filmColor * isFilm + rimColor * isRim + sparkColor * (isSpark + isTendril) + shadowColor * isShadow;
    surface.metallic = 0.0;
    surface.roughness = 0.32 * isSeed + 0.16 * isFilm + 0.20 * isRim + 0.14 * (isSpark + isTendril) + 0.72 * isShadow;
    surface.emission = primitiveEmissionRadiance(sdfFieldId(sdfIndex))
        + seedColor * isSeed * 0.12
        + filmColor * isFilm * (0.10 + sdfObject.state.y * 0.10)
        + rimColor * isRim * (0.55 + sdfObject.state.y * 0.20)
        + sparkColor * (isSpark * 1.35 + isTendril * 0.85)
        + shadowColor * isShadow * 0.015;
    surface.temporalDetail = 0.0;
    surface.reservoirConfidence = 1.0;
    return surface;
}

float3 shadeSdf(float2 uv, float travel, float3 p, float3 normal, int sdfIndex, SdfSurface surface)
{
    return shadeSdfPbr(p, normal, surface);
}

#include "D3D12SdfProxy.hlsli"
