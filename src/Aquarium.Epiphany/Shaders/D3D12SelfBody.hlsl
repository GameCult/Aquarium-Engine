static const int SDF_INDEX = 0;

#include "D3D12SdfCommon.hlsli"
#include "D3D12SdfMath.hlsli"

struct SelfParts
{
    float core;
    float coreInlay;
    float rail;
    float gate;
    float seam;
    float distanceValue;
};

struct SelfOrbitSpace
{
    float r;
    float3 dir;
    float2 spinA;
    float2 spinB;
    float z;
    float shell;
    float scale;
};

float2 rotate2(float2 p, float angle)
{
    float c = cos(angle);
    float s = sin(angle);
    return float2(p.x * c - p.y * s, p.x * s + p.y * c);
}

SelfOrbitSpace selfOrbitSpace(float3 local, float phase)
{
    float r = max(length(local), 0.0001);
    float3 dir = local / r;
    float logRadius = log(r / 0.58);
    float shell = logRadius * 2.35;
    float polarTighten = 0.58 + 0.42 * sqrt(saturate(1.0 - dir.z * dir.z));

    SelfOrbitSpace space;
    space.r = r;
    space.dir = dir;
    space.spinA = rotate2(dir.xy, shell * 0.72 + phase * 0.18);
    space.spinB = rotate2(float2(dir.x, dir.z), shell * -0.48 + phase * 0.13);
    space.z = dir.z;
    space.shell = shell;
    space.scale = r * polarTighten;
    return space;
}

float sdSelfCore(float3 local, float heartbeat, float timeSeconds)
{
    float pulse = 0.020 * sin(timeSeconds * 0.75 + heartbeat * 6.28318);
    return sdSphere(local, 0.50 + pulse);
}

float sdSelfCoreInlay(SelfOrbitSpace space, float phase)
{
    float coreShell = space.r - 0.505;
    float latitude = abs(space.z + 0.08 * sin(phase + space.spinA.x * 3.0));
    float meridianA = abs(dot(space.spinA, normalize(float2(0.92, 0.39))));
    float meridianB = abs(dot(float2(space.dir.y, space.dir.z), normalize(float2(-0.54, 0.84))));
    float seam = min(latitude, min(meridianA, meridianB)) * space.scale;
    return length(float2(coreShell, seam)) - 0.006;
}

float sdSelfRailFamily(SelfOrbitSpace space, float pressure, float phase)
{
    float innerShell = space.r - lerp(0.66, 0.58, pressure);
    float midShell = space.r - lerp(0.82, 0.70, pressure);
    float outerShell = space.r - lerp(1.06, 0.92, pressure);
    float equatorRail = abs(space.z + 0.13 * sin(phase + space.spinA.x * 3.0));
    float meridianRail = abs(dot(space.spinA, normalize(float2(0.76, 0.65))));
    float spiralRail = abs(dot(float2(space.spinB.x, space.z), normalize(float2(0.62, 0.78))) + 0.22 * sin(space.shell + phase * 0.55));
    float tiltedRailA = abs(dot(space.dir, normalize(float3(0.58, -0.36, 0.73))) + 0.16 * sin(space.shell * 1.30 - phase * 0.44));
    float tiltedRailB = abs(dot(space.dir, normalize(float3(-0.42, 0.82, 0.39))) + 0.14 * sin(space.shell * 0.90 + phase * 0.37));
    float crownRail = abs(dot(float2(space.spinA.y, space.z), normalize(float2(0.68, -0.74))) + 0.18 * sin(space.spinB.x * 2.4 + phase * 0.31));
    float shellRail = length(float2(midShell, equatorRail * space.scale)) - 0.018;
    float meridianTube = length(float2(midShell, meridianRail * space.scale)) - 0.014;
    float spiralTube = length(float2(outerShell, spiralRail * space.scale)) - 0.012;
    float tiltedTubeA = length(float2(outerShell, tiltedRailA * space.scale)) - 0.010;
    float tiltedTubeB = length(float2(innerShell, tiltedRailB * space.scale)) - 0.010;
    float crownTube = length(float2(innerShell, crownRail * space.scale)) - 0.009;
    return min(min(shellRail, meridianTube), min(min(spiralTube, tiltedTubeA), min(tiltedTubeB, crownTube)));
}

float sdSelfGateFamily(SelfOrbitSpace space, float pressure, float phase)
{
    float innerShell = space.r - lerp(0.66, 0.58, pressure);
    float midShell = space.r - lerp(0.82, 0.70, pressure);
    float outerShell = space.r - lerp(1.06, 0.92, pressure);
    float equatorRail = abs(space.z + 0.13 * sin(phase + space.spinA.x * 3.0));
    float meridianRail = abs(dot(space.spinA, normalize(float2(0.76, 0.65))));
    float spiralRail = abs(dot(float2(space.spinB.x, space.z), normalize(float2(0.62, 0.78))) + 0.22 * sin(space.shell + phase * 0.55));
    float tiltedRailA = abs(dot(space.dir, normalize(float3(0.58, -0.36, 0.73))) + 0.16 * sin(space.shell * 1.30 - phase * 0.44));
    float tiltedRailB = abs(dot(space.dir, normalize(float3(-0.42, 0.82, 0.39))) + 0.14 * sin(space.shell * 0.90 + phase * 0.37));
    float gateA = length(float3(midShell * 1.35, equatorRail * space.scale, meridianRail * space.scale)) - 0.036;
    float gateB = length(float3(outerShell * 1.35, equatorRail * space.scale, spiralRail * space.scale)) - 0.028;
    float gateC = length(float3(innerShell * 1.35, tiltedRailA * space.scale, tiltedRailB * space.scale)) - 0.024;
    return min(min(gateA, gateB), gateC);
}

float sdSelfSeam(SelfOrbitSpace space, float activity)
{
    float seamBand = min(abs(space.z), abs(dot(space.spinA, normalize(float2(0.92, 0.39))))) * space.scale;
    float shellRadius = abs(space.r - lerp(0.515, 0.49, activity));
    return max(shellRadius - 0.004, seamBand - 0.010);
}

SelfParts selfParts(float3 local, SdfObject sdfObject, float timeSeconds)
{
    float activity = saturate(sdfObject.state.x);
    float heartbeat = saturate(sdfObject.state.y);
    float pressure = saturate(sdfObject.state.z);
    float phase = timeSeconds * 0.72 + heartbeat * 2.1;
    SelfOrbitSpace space = selfOrbitSpace(local, phase);

    float core = sdSelfCore(local, heartbeat, timeSeconds);
    float coreInlay = sdSelfCoreInlay(space, phase);
    float rail = sdSelfRailFamily(space, pressure, phase);
    float gate = sdSelfGateFamily(space, pressure, phase);
    float seam = sdSelfSeam(space, activity);

    float routed = smoothUnion(core, coreInlay, 0.006);
    routed = smoothUnion(routed, rail, 0.026);
    routed = smoothUnion(routed, gate, 0.032);
    float distanceValue = smoothUnion(routed, seam, 0.012);

    SelfParts parts;
    parts.core = core;
    parts.coreInlay = coreInlay;
    parts.rail = rail;
    parts.gate = gate;
    parts.seam = seam;
    parts.distanceValue = distanceValue;
    return parts;
}

float sdfDistance(float3 p, int sdfIndex)
{
    SdfObject sdfObject = sdfObjects[sdfIndex];
    float radius = max(sdfObject.centerRadius.w, 0.001);
    float3 local = (p - sdfObject.centerRadius.xyz) / radius;
    return selfParts(local, sdfObject, timeSeconds).distanceValue * radius;
}

SdfSurface sdfSurface(float3 p, int sdfIndex)
{
    SdfObject sdfObject = sdfObjects[sdfIndex];
    float radius = max(sdfObject.centerRadius.w, 0.001);
    float3 local = (p - sdfObject.centerRadius.xyz) / radius;
    SelfParts parts = selfParts(local, sdfObject, timeSeconds);
    float isGate = parts.gate <= min(min(min(parts.core, parts.coreInlay), parts.rail), parts.seam) ? 1.0 : 0.0;
    float isRail = (1.0 - isGate) * (parts.rail <= min(min(parts.core, parts.coreInlay), parts.seam) ? 1.0 : 0.0);
    float isInlay = (1.0 - isGate) * (1.0 - isRail) * (parts.coreInlay <= min(parts.core, parts.seam) ? 1.0 : 0.0);
    float isSeam = (1.0 - isGate) * (1.0 - isRail) * (1.0 - isInlay) * (parts.seam <= parts.core ? 1.0 : 0.0);
    float isCore = (1.0 - isGate) * (1.0 - isRail) * (1.0 - isInlay) * (1.0 - isSeam);

    float coreVein = 0.5 + 0.5 * sin(local.x * 9.0 + local.y * 5.0 - local.z * 7.0 + timeSeconds * 0.55);
    float coreGlow = 0.5 + 0.5 * sin(local.x * 2.3 - local.y * 1.7 + local.z * 2.9 + timeSeconds * 1.1);
    float3 coreColor = lerp(float3(0.78, 0.53, 0.35), float3(1.0, 0.82, 0.54), coreGlow) + coreVein * 0.055;
    float3 inlayColor = float3(1.0, 0.72, 0.26);
    float3 railColor = float3(0.86, 0.54, 0.20);
    float3 gateColor = lerp(float3(0.78, 0.66, 0.42), float3(1.0, 0.82, 0.42), saturate(sdfObject.state.x));
    float3 seamColor = float3(0.12, 0.065, 0.028);

    SdfSurface surface;
    surface.baseColor = coreColor * isCore + inlayColor * isInlay + railColor * isRail + gateColor * isGate + seamColor * isSeam;
    surface.metallic = 0.0 * isCore + 0.64 * isInlay + 0.72 * isRail + 0.18 * isGate + 0.0 * isSeam;
    surface.roughness = 0.34 * isCore + 0.20 * isInlay + 0.24 * isRail + 0.18 * isGate + 0.52 * isSeam;

    float3 selfLight = primitiveEmissionRadiance(sdfFieldId(sdfIndex));
    surface.emission = selfLight * (isRail * 0.045 + isGate * 0.08)
        + coreColor * isCore * (0.070 + coreVein * 0.030)
        + inlayColor * isInlay * 0.30
        + railColor * isRail * 0.18
        + gateColor * isGate * (0.22 + sdfObject.state.y * 0.08)
        + seamColor * isSeam * 0.008;
    return surface;
}

float3 shadeSdf(float2 uv, float travel, float3 p, float3 normal, int sdfIndex, SdfSurface surface)
{
    return shadeSdfPbr(p, normal, surface);
}

#include "D3D12SdfProxy.hlsli"
