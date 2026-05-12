static const int SDF_INDEX = 8;

#include "D3D12SdfCommon.hlsli"
#include "D3D12SdfMath.hlsli"

static const float CURSOR_RADIUS = 0.56;

struct CursorHibiscusParts
{
    float petals;
    float throat;
    float stamen;
    float calyx;
    float distanceValue;
};

float2 hibiscusPetalCenter(float u)
{
    float radial = 0.92 * u * u * (1.0 - 0.10 * u);
    float height = -0.10 + 0.82 * u;
    return float2(radial, height);
}

float sdHibiscusPetalRibbon(float3 q)
{
    float u = saturate((q.z + 0.10) / 0.82);
    float2 center = hibiscusPetalCenter(u);
    float widthGrowth = smoothstep(0.08, 1.0, u);
    float width = lerp(0.030, 0.42, widthGrowth);
    float thickness = lerp(0.010, 0.046, widthGrowth);
    float fold = 0.055 * q.y * q.y / max(width * width, 0.001);
    float radialDistance = abs(q.x - center.x - fold) - thickness;
    float verticalDistance = max(-0.10 - q.z, q.z - 0.72);
    float sideDistance = abs(q.y) - width;
    return max(max(radialDistance, verticalDistance), sideDistance);
}

float sdHibiscusCursorPetal(float3 local, float angle, float timeSeconds)
{
    float2 radial = float2(cos(angle), sin(angle));
    float2 tangent = float2(-radial.y, radial.x);
    float sway = 0.030 * sin(timeSeconds * 0.95 + angle * 1.7);
    float3 center = float3(radial * 0.20, 0.07 + sway);
    float3 p = local - center;
    float3 q = float3(dot(p.xy, radial), dot(p.xy, tangent), p.z);
    return sdHibiscusPetalRibbon(q);
}

float sdHibiscusSepal(float3 local, float angle)
{
    float2 radial = float2(cos(angle), sin(angle));
    float3 a = float3(0.0, 0.0, -0.46);
    float3 b = float3(radial * 0.26, -0.22);
    return sdTaperedCapsuleSegment(local, a, b, 0.058, 0.022);
}

float sdHibiscusStamen(float3 local)
{
    float column = sdQuadraticTube(local, float3(0.0, 0.0, 0.02), float3(-0.12, 0.08, 0.43), float3(0.13, 0.04, 0.86), 0.048, 0.032);
    float bead0 = sdSphere(local - float3(0.14, 0.04, 0.92), 0.064);
    float bead1 = sdSphere(local - float3(0.18, 0.12, 0.72), 0.046);
    float bead2 = sdSphere(local - float3(-0.12, 0.04, 0.62), 0.044);
    float beads = min(bead0, min(bead1, bead2));
    return min(column, beads);
}

CursorHibiscusParts cursorHibiscusParts(float3 local, float timeSeconds)
{
    float sway = 0.10 * sin(timeSeconds * 0.85);
    float petal0 = sdHibiscusCursorPetal(local, 1.45 + sway, timeSeconds);
    float petal1 = sdHibiscusCursorPetal(local, 2.62 + sway * 0.7, timeSeconds);
    float petal2 = sdHibiscusCursorPetal(local, 3.88 + sway * 0.5, timeSeconds);
    float petal3 = sdHibiscusCursorPetal(local, 5.12 + sway * 0.4, timeSeconds);
    float petal4 = sdHibiscusCursorPetal(local, 0.22 + sway * 0.6, timeSeconds);
    float petals = min(min(petal0, petal1), min(min(petal2, petal3), petal4));

    float throatSphere = sdSphere(local - float3(0.0, 0.0, -0.05), 0.24);
    float throatTop = local.z - 0.07;
    float throat = max(throatSphere, throatTop);
    float stamen = sdHibiscusStamen(local);

    float calyxCore = sdQuadraticTube(local, float3(0.0, 0.0, -1.0), float3(0.06, -0.04, -0.62), float3(0.0, 0.0, -0.18), 0.014, 0.120);
    float sepals = min(sdHibiscusSepal(local, 1.57), min(sdHibiscusSepal(local, 3.66), sdHibiscusSepal(local, 5.75)));
    float calyx = smoothUnion(calyxCore, sepals, 0.035);

    float blossom = smoothUnion(petals, throat, 0.10);
    blossom = smoothUnion(blossom, stamen, 0.065);
    CursorHibiscusParts parts;
    parts.petals = petals;
    parts.throat = throat;
    parts.stamen = stamen;
    parts.calyx = calyx;
    parts.distanceValue = smoothUnion(blossom, calyx, 0.120);
    return parts;
}

float sdfDistance(float3 p, int sdfIndex)
{
    SdfObject cursor = sdfObjects[SDF_INDEX];
    float3 local = (p - cursor.centerRadius.xyz) / CURSOR_RADIUS;
    return cursorHibiscusParts(local, timeSeconds).distanceValue * CURSOR_RADIUS;
}

SdfSurface sdfSurface(float3 p, int sdfIndex)
{
    SdfObject cursor = sdfObjects[SDF_INDEX];
    float3 local = (p - cursor.centerRadius.xyz) / CURSOR_RADIUS;
    CursorHibiscusParts parts = cursorHibiscusParts(local, timeSeconds);
    float blossomPart = min(parts.petals, min(parts.throat, parts.stamen));
    float isCalyx = parts.calyx <= blossomPart ? 1.0 : 0.0;
    float isStamen = (1.0 - isCalyx) * (parts.stamen <= min(parts.petals, parts.throat) ? 1.0 : 0.0);
    float isThroat = (1.0 - isCalyx) * (1.0 - isStamen) * (parts.throat <= parts.petals ? 1.0 : 0.0);
    float isPetal = (1.0 - isCalyx) * (1.0 - isStamen) * (1.0 - isThroat);

    float petalTone = 0.55 + 0.45 * cos(atan2(local.y, local.x) * 3.0 - timeSeconds * 1.8);
    float3 petalAlbedo = lerp(float3(1.0, 0.34, 0.62), float3(1.0, 0.78, 0.90), petalTone);
    float3 throatAlbedo = float3(1.0, 0.20, 0.48);
    float3 stamenAlbedo = float3(1.0, 0.72, 0.18);
    float3 calyxAlbedo = float3(0.34, 0.66, 0.12);

    SdfSurface surface;
    surface.albedo = petalAlbedo * isPetal + throatAlbedo * isThroat + stamenAlbedo * isStamen + calyxAlbedo * isCalyx;
    surface.roughness = isCalyx > 0.5 ? 0.46 : 0.22;
    surface.f0 = lerp(float3(1.0, 0.38, 0.72), float3(0.04, 0.06, 0.03), isCalyx);
    surface.emission = surface.albedo * (isCalyx > 0.5 ? 0.05 : 0.56);
    return surface;
}

float3 shadeSdf(float2 uv, float travel, float3 p, float3 normal, int sdfIndex, SdfSurface surface)
{
    return shadeSdfPbr(p, normal, surface);
}

#include "D3D12SdfProxy.hlsli"
