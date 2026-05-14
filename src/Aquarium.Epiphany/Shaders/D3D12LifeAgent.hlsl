static const int SDF_INDEX = 7;

#include "D3D12SdfCommon.hlsli"
#include "D3D12SdfMath.hlsli"

struct LifeDomain
{
    float body;
    float shell;
    float rib;
    float rim;
    float bead;
    float crack;
    float whorl;
    float chamber;
    float2 uv;
};

float lifeSpiralPhase(float2 uv)
{
    float r = max(length(uv), 0.025);
    return atan2(uv.y, uv.x) - 1.52 * log(r + 0.052) + 0.22;
}

float lifeShellEnvelope(float2 uv)
{
    float r = length(uv);
    float theta = atan2(uv.y, uv.x);
    float d = lifeSpiralPhase(uv);
    float spiralDistance = abs(sin(d)) * r;
    float radius = 0.47 + 0.18 * cos(theta - 0.10) + 0.070 * cos(theta * 2.0 + 0.58);
    float whorlLift = smoothstep(0.040, 0.0, spiralDistance) * (0.012 + 0.018 * smoothstep(0.12, 0.64, r));
    return r - radius - whorlLift;
}

float lifeShell(float3 local, float pressure, out float whorl, out float chamber)
{
    float2 uv = float2(local.x + 0.045, local.z - 0.010);
    whorl = lifeShellEnvelope(uv);

    float convex = pow(saturate(-whorl / 0.34), 0.42);
    float halfDepth = (0.050 + 0.285 * convex) * lerp(1.0, 0.90, pressure);
    float side = abs(local.y) - halfDepth;

    float3 mouthCenter = float3(-0.415, -0.060, 0.065);
    float mouth = sdEllipsoid(local - mouthCenter, float3(0.34, 0.30, 0.44));
    float mouthGate = dot(normalize(float3(-0.92, -0.18, 0.12)), local - mouthCenter) + 0.25;
    chamber = max(mouth, -mouthGate);

    return max(max(whorl, side), -chamber);
}

float lifeSurfaceBand(float shellDistance, float offset, float halfWidth)
{
    return abs(shellDistance + offset) - halfWidth;
}

float lifeRib(float3 local, float shellDistance)
{
    float2 uv = float2(local.x + 0.045, local.z - 0.010);
    float r = max(length(uv), 0.030);
    float phase = lifeSpiralPhase(uv);
    float chamberRibs = abs(sin(phase * 13.0 + r * 3.4)) * r - 0.010;
    return max(lifeSurfaceBand(shellDistance, 0.018, 0.028), chamberRibs);
}

float lifeRim(float3 local, float shellDistance, float whorl, out float seamLine)
{
    float2 uv = float2(local.x + 0.045, local.z - 0.010);
    float r = max(length(uv), 0.030);
    seamLine = abs(sin(lifeSpiralPhase(uv))) * r;
    float seam = max(lifeSurfaceBand(shellDistance, 0.010, 0.022), seamLine - 0.020);
    float aperture = abs(length((local.xz - float2(-0.395, 0.065)) / float2(0.34, 0.44)) - 1.0) * 0.10;
    float lip = max(abs(local.y + 0.204) - 0.022, aperture - 0.011);
    return min(seam, lip);
}

float lifeBeads(float3 local)
{
    float2 q = (local.xz - float2(-0.395, 0.065)) / float2(0.34, 0.44);
    float angle = atan2(q.y, q.x);
    float arcGate = max(angle - 0.28, -2.72 - angle);
    float rimDistance = abs(length(q) - 1.0) * 0.075 - 0.005;
    float side = abs(local.y + 0.210) - 0.020;
    float beadPeriod = abs(sin((angle + 2.72) * 13.5)) - 0.30;
    return max(max(rimDistance, side), max(arcGate * 0.05, beadPeriod * 0.030));
}

float lifeCrack(float3 local, float shellDistance)
{
    float2 uv = float2(local.x + 0.045, local.z - 0.010);
    float r = max(length(uv), 0.030);
    float phase = lifeSpiralPhase(uv);
    float fine = abs(sin(phase * 23.0 + r * 21.0 + sin(phase * 3.0))) * r - 0.004;
    return max(lifeSurfaceBand(shellDistance, 0.004, 0.012), fine);
}

LifeDomain lifeDomain(float3 local, SdfObject sdfObject)
{
    float pressure = saturate(sdfObject.state.z + (1.0 - sdfObject.state.y) * 0.14);

    float whorl;
    float chamber;
    float shell = lifeShell(local, pressure, whorl, chamber);
    float rib = lifeRib(local, shell);

    float seamLine;
    float rim = lifeRim(local, shell, whorl, seamLine);
    float bead = lifeBeads(local);
    float crack = lifeCrack(local, shell);
    float ribLift = (1.0 - smoothstep(0.0, 0.020, rib)) * 0.010;
    float rimLift = (1.0 - smoothstep(0.0, 0.018, rim)) * 0.016;
    float beadLift = (1.0 - smoothstep(0.0, 0.014, bead)) * 0.018;
    float crackedShell = shell - ribLift - rimLift;

    LifeDomain domain;
    domain.body = crackedShell - beadLift;
    domain.shell = crackedShell;
    domain.rib = rib;
    domain.rim = rim;
    domain.bead = bead;
    domain.crack = crack;
    domain.whorl = whorl;
    domain.chamber = chamber;
    domain.uv = float2(local.x + 0.045, local.z - 0.010);
    return domain;
}

float sdfDistance(float3 p, int sdfIndex)
{
    SdfObject sdfObject = sdfObjects[sdfIndex];
    float radius = max(sdfObject.centerRadius.w, 0.001);
    float3 local = (p - sdfObject.centerRadius.xyz) / radius;
    return lifeDomain(local, sdfObject).body * radius;
}

SdfSurface sdfSurface(float3 p, int sdfIndex)
{
    SdfObject sdfObject = sdfObjects[sdfIndex];
    float radius = max(sdfObject.centerRadius.w, 0.001);
    float3 local = (p - sdfObject.centerRadius.xyz) / radius;
    LifeDomain domain = lifeDomain(local, sdfObject);
    float activity = saturate(sdfObject.state.x);
    float heartbeat = saturate(sdfObject.state.y);
    float pressure = saturate(sdfObject.state.z + (1.0 - heartbeat) * 0.14);

    float isBead = 1.0 - smoothstep(0.000, 0.014, domain.bead);
    float isRim = (1.0 - isBead) * (1.0 - smoothstep(0.000, 0.018, domain.rim));
    float isRib = (1.0 - isBead) * (1.0 - isRim) * (1.0 - smoothstep(0.000, 0.022, domain.rib));
    float isShell = (1.0 - isBead) * (1.0 - isRim) * (1.0 - isRib);

    float insideChamber = isShell * (1.0 - smoothstep(0.000, 0.055, abs(domain.chamber)));
    float crackMask = isShell * (1.0 - insideChamber) * (1.0 - smoothstep(0.000, 0.012, domain.crack));
    float nacre = 0.5 + 0.5 * sin(lifeSpiralPhase(domain.uv) * 7.0 + local.y * 9.0 + timeSeconds * 0.15);

    float3 teal = lerp(float3(0.020, 0.30, 0.34), float3(0.09, 0.58, 0.62), nacre);
    float3 gold = lerp(float3(0.88, 0.48, 0.10), float3(1.0, 0.80, 0.30), nacre);
    float3 nacreGold = float3(1.0, 0.86, 0.58);
    float3 pearl = lerp(float3(0.82, 0.75, 0.62), float3(1.0, 0.96, 0.84), nacre);
    float3 ember = float3(1.0, 0.46, 0.06);

    SdfSurface surface;
    surface.baseColor = teal * isShell * (1.0 - insideChamber)
        + gold * insideChamber
        + nacreGold * saturate(isRim + isRib)
        + pearl * isBead
        + ember * 0.0;
    surface.baseColor = lerp(surface.baseColor, gold, crackMask * 0.58);
    surface.metallic = 0.0 * isShell + 0.34 * saturate(isRim + isRib);
    surface.roughness = 0.22 * isShell + 0.16 * insideChamber + 0.11 * saturate(isRim + isRib) + 0.10 * isBead;
    surface.emission = primitiveEmissionRadiance(sdfFieldId(sdfIndex)) * 0.03
        + teal * isShell * 0.020
        + gold * insideChamber * (0.42 + heartbeat * 0.36)
        + nacreGold * isRim * (0.30 + activity * 0.22)
        + pearl * isBead * (0.22 + heartbeat * 0.18)
        + gold * crackMask * (0.06 + pressure * 0.16);
    return surface;
}

float3 shadeSdf(float2 uv, float travel, float3 p, float3 normal, int sdfIndex, SdfSurface surface)
{
    SdfObject sdfObject = sdfObjects[sdfIndex];
    float radius = max(sdfObject.centerRadius.w, 0.001);
    float3 local = (p - sdfObject.centerRadius.xyz) / radius;
    LifeDomain domain = lifeDomain(local, sdfObject);
    float3 shaded = shadeSdfPbr(p, normal, surface);

    float3 viewDirection = normalize(cameraPosition - p);
    float edge = pow(1.0 - saturate(dot(normal, viewDirection)), 3.2);
    float core = exp(-dot(local - float3(-0.160, -0.160, 0.040), local - float3(-0.160, -0.160, 0.040)) * 10.0);
    float seam = 1.0 - smoothstep(0.0, 0.026, min(domain.rim, abs(sin(lifeSpiralPhase(domain.uv))) * max(length(domain.uv), 0.03)));
    return shaded
        + float3(1.0, 0.48, 0.08) * core * 0.45
        + float3(0.90, 0.70, 0.34) * seam * 0.075
        + float3(0.12, 0.65, 0.70) * edge * 0.10;
}

#define SDF_TRACE_STEP_SCALE 0.15
#define SDF_TRACE_MAX_STEP_RADIUS_SCALE 0.008

#include "D3D12SdfProxy.hlsli"
