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

float2 harmonic5(float2 direction)
{
    float c = direction.x;
    float s = direction.y;
    float c2 = c * c;
    float c3 = c2 * c;
    float c4 = c2 * c2;
    float c5 = c4 * c;
    return float2(16.0 * c5 - 20.0 * c3 + 5.0 * c, s * (16.0 * c4 - 12.0 * c2 + 1.0));
}

float sdImaginationSeed(float3 local, float activity, float phase)
{
    float2 xy = local.xy;
    float xyLength = length(xy);
    float2 direction = xyLength > 0.0001 ? xy / xyLength : float2(1.0, 0.0);
    float2 h5 = harmonic5(direction);
    float lobe = h5.x * cos(phase) - h5.y * sin(phase);
    float verticalMask = 1.0 - smoothstep(0.40, 0.76, abs(local.z - 0.02));
    float radius = 0.24 + lerp(0.04, 0.15, activity) * lobe * verticalMask;
    float egg = length(float3(local.xy / max(radius, 0.04), (local.z - 0.04) / 0.38)) - 1.0;
    return egg * min(radius, 0.38);
}

float pairCenterOut(float u, float activity)
{
    return 0.12 + 0.92 * u - 0.42 * u * u + 0.05 * activity * sin(u * 6.28318);
}

float pairCenterSide(float u, float curl)
{
    return curl * 0.20 * sin(u * 3.14159) * smoothstep(0.08, 0.82, u);
}

float pairWidth(float u, float activity)
{
    return 0.045 + sin(saturate(u) * 3.14159) * (0.24 + activity * 0.07);
}

float3 pairPoint(float angle, float u, float signedEdge, float activity, float curl)
{
    float2 radial = float2(cos(angle), sin(angle));
    float2 tangent = float2(-radial.y, radial.x);
    float side = pairCenterSide(u, curl) + signedEdge * pairWidth(u, activity);
    float lift = -0.28 + 1.05 * u;
    return float3(radial * pairCenterOut(u, activity) + tangent * side, lift);
}

float sdSheetPairFilm(float3 local, float angle, float activity, float curl)
{
    float2 radial = float2(cos(angle), sin(angle));
    float2 tangent = float2(-radial.y, radial.x);
    float3 q = float3(dot(local.xy, radial), dot(local.xy, tangent), local.z);
    float u = saturate((q.z + 0.28) / 1.05);
    float sideCenter = pairCenterSide(u, curl);
    float sheetOut = abs(q.x - pairCenterOut(u, activity)) - 0.028;
    float sheetSide = abs(abs(q.y) - sideCenter) - pairWidth(u, activity);
    float sheetRange = max(-u, u - 1.0) * 1.05;
    return max(max(sheetOut, sheetSide), sheetRange);
}

float sdSheetPairRims(float3 local, float angle, float activity, float curl)
{
    float outerA = sdTaperedCapsuleSegment(local, pairPoint(angle, 0.00, 1.0, activity, curl), pairPoint(angle, 1.00, 1.0, activity, curl), 0.030, 0.016);
    float outerB = sdTaperedCapsuleSegment(local, pairPoint(angle, 0.00, -1.0, activity, curl), pairPoint(angle, 1.00, -1.0, activity, curl), 0.030, 0.016);
    float innerA = sdTaperedCapsuleSegment(local, pairPoint(angle, 0.10, -0.72, activity, curl), pairPoint(angle, 0.92, -0.72, activity, curl), 0.022, 0.014);
    float innerB = sdTaperedCapsuleSegment(local, pairPoint(angle, 0.10, 0.72, activity, curl), pairPoint(angle, 0.92, 0.72, activity, curl), 0.022, 0.014);
    float root = sdTaperedCapsuleSegment(local, float3(0.0, 0.0, -0.18), pairPoint(angle, 0.16, 0.0, activity, curl), 0.052, 0.030);
    return smoothUnion(min(min(outerA, outerB), min(innerA, innerB)), root, 0.035);
}

float sdSheetPair(float3 local, float angle, float activity, float curl, out float film, out float rim)
{
    film = sdSheetPairFilm(local, angle, activity, curl);
    rim = sdSheetPairRims(local, angle, activity, curl);
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
    float pressure = saturate(sdfObject.state.z);
    float phase = timeSeconds * 0.18 + heartbeat * 1.7;
    float curl = lerp(0.55, 1.25, activity);
    float seed = sdImaginationSeed(local, activity, phase);

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
    float shadowRing = sdTorus((local - float3(0.0, 0.0, -0.26 - pressure * 0.04)).xzy, float2(0.32 + pressure * 0.08, 0.024));
    float shadow = max(shadowRing, local.z + 0.23);

    float bloom = smoothUnion(seed, sheets, 0.075);
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
    return surface;
}

float3 shadeSdf(float2 uv, float travel, float3 p, float3 normal, int sdfIndex, SdfSurface surface)
{
    return shadeSdfPbr(p, normal, surface);
}

#include "D3D12SdfProxy.hlsli"
