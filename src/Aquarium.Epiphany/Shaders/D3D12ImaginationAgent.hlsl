static const int SDF_INDEX = 2;

#include "D3D12SdfCommon.hlsli"
#include "D3D12SdfMath.hlsli"

struct ImaginationParts
{
    float seed;
    float petals;
    float sparks;
    float shadow;
    float distanceValue;
};

float sdImaginationRibbonPetal(float3 local, float angle, float activity, float heartbeat, float timeSeconds)
{
    float2 radial = float2(cos(angle), sin(angle));
    float2 tangent = float2(-radial.y, radial.x);
    float pulse = sin(timeSeconds * 0.92 + angle * 1.63 + heartbeat * 6.28318);
    float openness = lerp(0.42, 0.62, activity);
    float3 root = float3(radial * 0.08, -0.08);
    float3 p = local - root;
    float3 q = float3(dot(p.xy, radial), dot(p.xy, tangent), p.z);
    float u = saturate(q.x / 0.76);
    float centerZ = -0.05 + openness * u + 0.18 * u * (1.0 - u);
    float centerY = 0.16 * sin((u - 0.15) * 3.14159) * sin(angle * 2.0 + timeSeconds * 0.36);
    float width = lerp(0.045, 0.18 + activity * 0.04, smoothstep(0.06, 1.0, u));
    float thickness = lerp(0.024, 0.050, smoothstep(0.0, 0.85, u));
    float edgeTaper = smoothstep(0.0, 0.18, u) * (1.0 - smoothstep(0.86, 1.0, u));
    width *= lerp(0.55, 1.0, edgeTaper);
    thickness *= lerp(0.75, 1.0 + pulse * 0.10, edgeTaper);

    float along = max(-q.x, q.x - 0.86);
    float side = abs(q.y - centerY) - width;
    float sheet = abs(q.z - centerZ) - thickness;
    return max(max(along, side), sheet);
}

float sdImaginationSpark(float3 local, float angle, float height, float phase)
{
    float2 radial = float2(cos(angle), sin(angle));
    float3 center = float3(radial * (0.44 + 0.05 * sin(phase)), height + 0.035 * cos(phase));
    return sdSphere(local - center, 0.040);
}

ImaginationParts imaginationParts(float3 local, SdfObject sdfObject, float timeSeconds)
{
    float activity = saturate(sdfObject.state.x);
    float heartbeat = saturate(sdfObject.state.y);
    float pressure = saturate(sdfObject.state.z);
    float breath = 0.5 + 0.5 * sin(timeSeconds * 1.10 + heartbeat * 6.28318);
    float seedRadius = 0.22 + activity * 0.035 + breath * 0.018;
    float seed = sdSuperellipsoid(local, float3(seedRadius, seedRadius * 0.92, seedRadius * 1.16), 1.12);

    float phase = timeSeconds * 0.18 + activity * 0.42;
    float petal0 = sdImaginationRibbonPetal(local, phase + 0.22, activity, heartbeat, timeSeconds);
    float petal1 = sdImaginationRibbonPetal(local, phase + 1.4766371, activity, heartbeat, timeSeconds);
    float petal2 = sdImaginationRibbonPetal(local, phase + 2.7332741, activity, heartbeat, timeSeconds);
    float petal3 = sdImaginationRibbonPetal(local, phase + 3.9899112, activity, heartbeat, timeSeconds);
    float petal4 = sdImaginationRibbonPetal(local, phase + 5.2465482, activity, heartbeat, timeSeconds);
    float petals = min(min(petal0, petal1), min(petal2, min(petal3, petal4)));

    float sparkPhase = timeSeconds * 1.36 + heartbeat * 6.28318;
    float spark0 = sdImaginationSpark(local, phase + 0.84, 0.55, sparkPhase);
    float spark1 = sdImaginationSpark(local, phase + 2.66, 0.42, sparkPhase + 2.1);
    float spark2 = sdImaginationSpark(local, phase + 4.58, 0.49, sparkPhase + 4.2);
    float sparks = min(spark0, min(spark1, spark2));

    float shadowRing = sdTorus((local - float3(0.0, 0.0, -0.12 - pressure * 0.05)).xzy, float2(0.34 + pressure * 0.08, 0.024));
    float shadowClip = local.z + 0.10;
    float shadow = max(shadowRing, shadowClip);

    float bloom = smoothUnion(seed, petals, 0.115);
    bloom = smoothUnion(bloom, sparks, 0.030);
    ImaginationParts parts;
    parts.seed = seed;
    parts.petals = petals;
    parts.sparks = sparks;
    parts.shadow = shadow;
    parts.distanceValue = smoothUnion(bloom, shadow, 0.040);
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
    float nearestBloom = min(parts.seed, parts.petals);
    float isSpark = parts.sparks <= min(nearestBloom, parts.shadow) ? 1.0 : 0.0;
    float isShadow = (1.0 - isSpark) * (parts.shadow <= nearestBloom ? 1.0 : 0.0);
    float isPetal = (1.0 - isSpark) * (1.0 - isShadow) * (parts.petals <= parts.seed ? 1.0 : 0.0);
    float isSeed = (1.0 - isSpark) * (1.0 - isShadow) * (1.0 - isPetal);
    float shimmer = 0.5 + 0.5 * sin((local.x * 3.1 + local.y * -2.4 + local.z * 1.7) + timeSeconds * 1.4);
    float3 seedColor = lerp(float3(0.42, 0.24, 0.78), float3(0.92, 0.86, 1.0), shimmer);
    float3 petalColor = lerp(float3(0.62, 0.20, 0.92), float3(1.0, 0.58, 0.96), sdfObject.state.x);
    float3 sparkColor = float3(1.0, 0.78, 0.34);
    float3 shadowColor = float3(0.10, 0.08, 0.22);

    SdfSurface surface;
    surface.baseColor = seedColor * isSeed + petalColor * isPetal + sparkColor * isSpark + shadowColor * isShadow;
    surface.metallic = 0.0;
    surface.roughness = 0.30 * isSeed + 0.22 * isPetal + 0.18 * isSpark + 0.68 * isShadow;
    surface.emission = primitiveEmissionRadiance(sdfFieldId(sdfIndex))
        + seedColor * isSeed * 0.08
        + petalColor * isPetal * (0.10 + sdfObject.state.y * 0.08)
        + sparkColor * isSpark * 1.2
        + shadowColor * isShadow * 0.02;
    return surface;
}

float3 shadeSdf(float2 uv, float travel, float3 p, float3 normal, int sdfIndex, SdfSurface surface)
{
    return shadeSdfPbr(p, normal, surface);
}

#include "D3D12SdfProxy.hlsli"
