static const int BODY_INDEX = 8;

#include "D3D12BodyCommon.hlsli"
#include "D3D12SdfMath.hlsli"

static const float CURSOR_RADIUS = 0.56;

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

float sdHibiscusCursor(float3 local, float timeSeconds)
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
    return smoothUnion(blossom, calyx, 0.120);
}

float cursorHibiscusSdf(float3 p)
{
    AgentVisual cursor = agentVisuals[CURSOR_OBJECT_INDEX];
    float3 local = (p - cursor.centerRadius.xyz) / CURSOR_RADIUS;
    return sdHibiscusCursor(local, timeSeconds) * CURSOR_RADIUS;
}

BodySurface bodySurface(float3 p, int agentIndex)
{
    BodySurface surface;
    surface.distanceValue = cursorHibiscusSdf(p);
    surface.materialId = 5.0;
    surface.fieldId = FIELD_ID_CURSOR;
    surface.roleId = 0.0;
    surface.lodTier = 0.0;
    surface.costTier = 1.0;
    return surface;
}

float3 cursorEmissionRadiance(float3 p, float3 normal)
{
    AgentVisual cursor = agentVisuals[CURSOR_OBJECT_INDEX];
    float3 local = (p - cursor.centerRadius.xyz) / CURSOR_RADIUS;
    float petal = 0.55 + 0.45 * cos(atan2(local.y, local.x) * 3.0 - timeSeconds * 1.8);
    float throat = exp(-dot(local.xy, local.xy) * 7.0) * smoothstep(0.30, -0.18, local.z);
    float stamen = smoothstep(0.018, -0.035, sdHibiscusStamen(local));
    float calyxCore = sdQuadraticTube(local, float3(0.0, 0.0, -1.0), float3(0.06, -0.04, -0.62), float3(0.0, 0.0, -0.18), 0.018, 0.130);
    float calyxSepals = min(sdHibiscusSepal(local, 1.57), min(sdHibiscusSepal(local, 3.66), sdHibiscusSepal(local, 5.75)));
    float calyx = smoothstep(0.08, -0.02, min(calyxCore, calyxSepals));
    float rim = pow(1.0 - saturate(dot(normal, normalize(cameraPosition - p))), 2.0);
    float3 petalColor = lerp(float3(1.12, 0.28, 0.58), float3(1.0, 0.78, 0.90), petal);
    float3 throatColor = float3(1.0, 0.18, 0.45);
    float3 stamenColor = float3(1.0, 0.70, 0.10);
    float3 calyxColor = float3(0.36, 0.68, 0.10);
    float3 blossom = petalColor * (0.82 + rim * 0.75) + throatColor * throat * 1.7 + stamenColor * stamen * 1.35;
    return lerp(blossom, calyxColor * (0.55 + rim * 0.25), calyx);
}

float3 shadeBody(float2 uv, float travel, float3 p, float3 normal, int agentIndex, BodySurface surface)
{
    static const float CursorRoughness = 0.22;
    float3 cursorF0 = float3(1.0, 0.38, 0.72);
    return cursorEmissionRadiance(p, normal)
        + bodyLightSpecularRadiance(p, normal, CursorRoughness, cursorF0, 7.0)
        + studioPmremSpecularRadiance(p, normal, CursorRoughness, cursorF0);
}

#include "D3D12BodyProxy.hlsli"
