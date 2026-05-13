static const int SDF_INDEX = 0;

#include "D3D12SdfCommon.hlsli"
#include "D3D12SdfMath.hlsli"

struct SelfParts
{
    float core;
    float rail;
    float gate;
    float seam;
    float distanceValue;
};

struct SelfOrbitSpace
{
    float r;
    float theta;
    float phi;
    float shell;
    float scale;
};

float wrapPi(float value)
{
    return frac(value / (2.0 * PI) + 0.5) * (2.0 * PI) - PI;
}

float periodicDistance(float value, float period)
{
    return abs(frac(value / period + 0.5) - 0.5) * period;
}

SelfOrbitSpace selfOrbitSpace(float3 local, float phase)
{
    float r = max(length(local), 0.0001);
    float theta = atan2(local.y, local.x);
    float phi = acos(clamp(local.z / r, -1.0, 1.0));
    float logRadius = log(r / 0.58);
    float shell = logRadius * 2.35;
    float polarTighten = 0.52 + 0.48 * sin(phi);

    SelfOrbitSpace space;
    space.r = r;
    space.theta = theta + shell * 0.72 + phase * 0.18;
    space.phi = phi + 0.18 * sin(theta * 2.0 - phase * 0.35);
    space.shell = shell;
    space.scale = r * polarTighten;
    return space;
}

float sdSelfCore(float3 local, float heartbeat, float timeSeconds)
{
    float pulse = 0.020 * sin(timeSeconds * 0.75 + heartbeat * 6.28318);
    return sdSphere(local, 0.50 + pulse);
}

float sdSelfRailFamily(SelfOrbitSpace space, float pressure, float phase)
{
    float shellRadius = abs(space.r - lerp(0.76, 0.66, pressure));
    float equatorRail = periodicDistance(space.phi - PI * 0.50 + 0.16 * sin(space.theta * 2.0 + phase), PI * 0.50);
    float meridianRail = periodicDistance(space.theta + 0.34 * sin(space.phi * 2.0 - phase * 0.7), PI * 0.50);
    float spiralRail = periodicDistance(space.theta * 1.5 + space.phi * 0.85 + space.shell - phase * 0.55, PI * 0.72);
    float angularRail = min(equatorRail, min(meridianRail, spiralRail)) * space.scale;
    return max(shellRadius, angularRail - 0.020);
}

float sdSelfGateFamily(SelfOrbitSpace space, float pressure, float phase)
{
    float shellRadius = abs(space.r - lerp(0.76, 0.66, pressure));
    float routeA = periodicDistance(space.theta + space.phi * 0.50 - phase * 0.42, PI * 0.50);
    float routeB = periodicDistance(space.theta * 1.5 - space.phi + phase * 0.25, PI);
    float routeC = periodicDistance(space.phi - PI * 0.50, PI * 0.42);
    float node = length(float3(shellRadius * 1.6, routeA * space.scale, min(routeB, routeC) * space.scale)) - 0.052;
    return node;
}

float sdSelfSeam(SelfOrbitSpace space, float activity)
{
    float seamBand = periodicDistance(space.phi - PI * 0.50, PI) * space.scale;
    float shellRadius = abs(space.r - lerp(0.515, 0.49, activity));
    return max(shellRadius - 0.010, seamBand - 0.028);
}

SelfParts selfParts(float3 local, SdfObject sdfObject, float timeSeconds)
{
    float activity = saturate(sdfObject.state.x);
    float heartbeat = saturate(sdfObject.state.y);
    float pressure = saturate(sdfObject.state.z);
    float phase = timeSeconds * 0.72 + heartbeat * 2.1;
    SelfOrbitSpace space = selfOrbitSpace(local, phase);

    float core = sdSelfCore(local, heartbeat, timeSeconds);
    float rail = sdSelfRailFamily(space, pressure, phase);
    float gate = sdSelfGateFamily(space, pressure, phase);
    float seam = sdSelfSeam(space, activity);

    float routed = smoothUnion(core, rail, 0.026);
    routed = smoothUnion(routed, gate, 0.032);
    float distanceValue = smoothUnion(routed, seam, 0.012);

    SelfParts parts;
    parts.core = core;
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
    float isGate = parts.gate <= min(min(parts.core, parts.rail), parts.seam) ? 1.0 : 0.0;
    float isRail = (1.0 - isGate) * (parts.rail <= min(parts.core, parts.seam) ? 1.0 : 0.0);
    float isSeam = (1.0 - isGate) * (1.0 - isRail) * (parts.seam <= parts.core ? 1.0 : 0.0);
    float isCore = (1.0 - isGate) * (1.0 - isRail) * (1.0 - isSeam);

    float shimmer = 0.5 + 0.5 * sin(local.x * 2.3 - local.y * 1.7 + local.z * 2.9 + timeSeconds * 1.1);
    float3 coreColor = lerp(float3(0.58, 0.31, 0.18), float3(0.88, 0.58, 0.34), shimmer);
    float3 railColor = float3(0.86, 0.54, 0.20);
    float3 gateColor = lerp(float3(0.78, 0.66, 0.42), float3(1.0, 0.82, 0.42), saturate(sdfObject.state.x));
    float3 seamColor = float3(0.055, 0.025, 0.018);

    SdfSurface surface;
    surface.baseColor = coreColor * isCore + railColor * isRail + gateColor * isGate + seamColor * isSeam;
    surface.metallic = 0.0 * isCore + 0.72 * isRail + 0.18 * isGate + 0.0 * isSeam;
    surface.roughness = 0.42 * isCore + 0.24 * isRail + 0.18 * isGate + 0.80 * isSeam;

    float3 selfLight = primitiveEmissionRadiance(sdfFieldId(sdfIndex));
    surface.emission = selfLight * (isRail * 0.045 + isGate * 0.08)
        + coreColor * isCore * 0.018
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
