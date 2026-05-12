static const int SDF_INDEX = 2;

#include "D3D12SdfCommon.hlsli"
#include "D3D12SdfMath.hlsli"

struct ImaginationParts
{
    float seed;
    float ribbons;
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
    float cos5 = 16.0 * c5 - 20.0 * c3 + 5.0 * c;
    float sin5 = s * (16.0 * c4 - 12.0 * c2 + 1.0);
    return float2(cos5, sin5);
}

float2 harmonic7(float2 direction)
{
    float c = direction.x;
    float s = direction.y;
    float c2 = c * c;
    float c3 = c2 * c;
    float c4 = c2 * c2;
    float c5 = c4 * c;
    float c6 = c3 * c3;
    float c7 = c6 * c;
    float cos7 = 64.0 * c7 - 112.0 * c5 + 56.0 * c3 - 7.0 * c;
    float sin7 = s * (64.0 * c6 - 80.0 * c4 + 24.0 * c2 - 1.0);
    return float2(cos7, sin7);
}

float sdImaginationSeed(float3 local, float activity, float phase)
{
    float2 xy = local.xy;
    float xyLength = length(xy);
    float2 direction = xyLength > 0.0001 ? xy / xyLength : float2(1.0, 0.0);
    float2 h5 = harmonic5(direction);
    float2 h7 = harmonic7(direction);
    float lobe5 = h5.x * cos(phase) - h5.y * sin(phase);
    float lobe7 = h7.x * cos(phase * 1.31) - h7.y * sin(phase * 1.31);
    float amplitude = lerp(0.08, 0.28, activity);
    float lobe = lerp(lobe5, lobe7, smoothstep(0.48, 0.92, activity));
    float verticalMask = 1.0 - smoothstep(0.44, 0.92, abs(local.z));
    float radius = 0.30 + amplitude * lobe * verticalMask;
    float seed = length(float3(local.xy, local.z * 0.86)) - radius;
    return max(seed, abs(local.z) - 0.58);
}

float3 trefoilPoint(float t, float twist, float openness)
{
    float x = sin(t) + 2.0 * sin(2.0 * t);
    float y = cos(t) - 2.0 * cos(2.0 * t);
    float z = -sin(3.0 * t);
    float2 rotated = float2(
        x * cos(twist) - y * sin(twist),
        x * sin(twist) + y * cos(twist));
    return float3(rotated * (0.155 + openness * 0.015), z * (0.105 + openness * 0.020) + 0.16);
}

float sdTrefoilSegment(float3 local, float twist, float openness, float phase, float segmentIndex)
{
    float t0 = (segmentIndex / 6.0) * 6.2831853 + phase;
    float t1 = ((segmentIndex + 1.0) / 6.0) * 6.2831853 + phase;
    float u = (segmentIndex + 1.0) / 6.0;
    float tubeRadius = lerp(0.046, 0.032, smoothstep(0.35, 1.0, u));
    return sdCapsuleSegment(local, trefoilPoint(t0, twist, openness), trefoilPoint(t1, twist, openness), tubeRadius);
}

float sdTrefoilRibbonTube(float3 local, float twist, float activity, float heartbeat, float timeSeconds)
{
    float openness = lerp(0.55, 1.0, activity);
    float phase = timeSeconds * 0.25 + heartbeat * 6.28318;
    float distanceValue = sdTrefoilSegment(local, twist, openness, phase, 0.0);
    distanceValue = min(distanceValue, sdTrefoilSegment(local, twist, openness, phase, 1.0));
    distanceValue = min(distanceValue, sdTrefoilSegment(local, twist, openness, phase, 2.0));
    distanceValue = min(distanceValue, sdTrefoilSegment(local, twist, openness, phase, 3.0));
    distanceValue = min(distanceValue, sdTrefoilSegment(local, twist, openness, phase, 4.0));
    distanceValue = min(distanceValue, sdTrefoilSegment(local, twist, openness, phase, 5.0));
    return distanceValue;
}

float sdRibbonRoot(float3 local, float angle)
{
    float2 radial = float2(cos(angle), sin(angle));
    float3 a = float3(radial * 0.10, -0.02);
    float3 b = float3(radial * 0.26, 0.11);
    return sdTaperedCapsuleSegment(local, a, b, 0.040, 0.022);
}

float sdImaginationSparks(float3 local, float phase, float activity)
{
    float spark0 = sdSphere(local - float3(cos(phase) * 0.47, sin(phase) * 0.47, 0.32), 0.036);
    float spark1 = sdSphere(local - float3(cos(phase + 2.31) * 0.40, sin(phase + 2.31) * 0.40, 0.44), 0.028 + activity * 0.010);
    float spark2 = sdSphere(local - float3(cos(phase + 4.42) * 0.52, sin(phase + 4.42) * 0.52, 0.22), 0.026);
    return min(spark0, min(spark1, spark2));
}

ImaginationParts imaginationParts(float3 local, SdfObject sdfObject, float timeSeconds)
{
    float activity = saturate(sdfObject.state.x);
    float heartbeat = saturate(sdfObject.state.y);
    float pressure = saturate(sdfObject.state.z);
    float phase = timeSeconds * 0.31 + heartbeat * 1.7;
    float seed = sdImaginationSeed(local, activity, phase);

    float ribbon0 = sdTrefoilRibbonTube(local, phase + 0.00, activity, heartbeat, timeSeconds);
    float ribbon1 = sdTrefoilRibbonTube(local, phase + 2.09, activity, heartbeat, timeSeconds);
    float ribbon2 = sdTrefoilRibbonTube(local, phase + 4.18, activity, heartbeat, timeSeconds);
    float ribbons = min(ribbon0, min(ribbon1, ribbon2));

    float root0 = sdRibbonRoot(local, phase + 0.00);
    float root1 = sdRibbonRoot(local, phase + 2.09);
    float root2 = sdRibbonRoot(local, phase + 4.18);
    float roots = min(root0, min(root1, root2));
    ribbons = smoothUnion(ribbons, roots, 0.045);

    float sparks = sdImaginationSparks(local, phase * 1.7, activity);
    float shadowRing = sdTorus((local - float3(0.0, 0.0, -0.20 - pressure * 0.05)).xzy, float2(0.36 + pressure * 0.09, 0.026));
    float shadow = max(shadowRing, local.z + 0.18);

    float flower = smoothUnion(seed, ribbons, 0.075);
    flower = smoothUnion(flower, sparks, 0.026);

    ImaginationParts parts;
    parts.seed = seed;
    parts.ribbons = ribbons;
    parts.sparks = sparks;
    parts.shadow = shadow;
    parts.distanceValue = smoothUnion(flower, shadow, 0.035);
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
    float bloomPart = min(parts.seed, parts.ribbons);
    float isSpark = parts.sparks <= min(bloomPart, parts.shadow) ? 1.0 : 0.0;
    float isShadow = (1.0 - isSpark) * (parts.shadow <= bloomPart ? 1.0 : 0.0);
    float isRibbon = (1.0 - isSpark) * (1.0 - isShadow) * (parts.ribbons <= parts.seed ? 1.0 : 0.0);
    float isSeed = (1.0 - isSpark) * (1.0 - isShadow) * (1.0 - isRibbon);
    float shimmer = 0.5 + 0.5 * sin(local.x * 5.0 - local.y * 3.0 + local.z * 4.0 + timeSeconds * 1.2);
    float3 seedColor = lerp(float3(0.34, 0.16, 0.78), float3(0.88, 0.78, 1.0), shimmer);
    float3 ribbonColor = lerp(float3(0.54, 0.18, 0.96), float3(1.0, 0.56, 0.94), saturate(sdfObject.state.x));
    float3 sparkColor = float3(1.0, 0.74, 0.26);
    float3 shadowColor = float3(0.08, 0.07, 0.20);

    SdfSurface surface;
    surface.baseColor = seedColor * isSeed + ribbonColor * isRibbon + sparkColor * isSpark + shadowColor * isShadow;
    surface.metallic = 0.0;
    surface.roughness = 0.32 * isSeed + 0.24 * isRibbon + 0.16 * isSpark + 0.72 * isShadow;
    surface.emission = primitiveEmissionRadiance(sdfFieldId(sdfIndex))
        + seedColor * isSeed * 0.10
        + ribbonColor * isRibbon * (0.12 + sdfObject.state.y * 0.10)
        + sparkColor * isSpark * 1.35
        + shadowColor * isShadow * 0.015;
    return surface;
}

float3 shadeSdf(float2 uv, float travel, float3 p, float3 normal, int sdfIndex, SdfSurface surface)
{
    return shadeSdfPbr(p, normal, surface);
}

#include "D3D12SdfProxy.hlsli"
