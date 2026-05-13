static const int SDF_INDEX = 0;

#include "D3D12SdfCommon.hlsli"
#include "D3D12SdfMath.hlsli"

struct SelfField
{
    float core;
    float inlay;
    float rail;
    float gate;
    float distanceValue;
};

float3x3 selfOrbitalFrame(float phase)
{
    float2 a = float2(cos(phase * 0.19), sin(phase * 0.19));
    float2 b = float2(cos(phase * -0.13 + 1.7), sin(phase * -0.13 + 1.7));
    float2 c = float2(cos(phase * 0.11 + 3.2), sin(phase * 0.11 + 3.2));

    return float3x3(
        normalize(float3(0.82 * a.x, 0.82 * a.y, 0.57)),
        normalize(float3(-0.42 * b.x, 0.72, 0.56 * b.y)),
        normalize(float3(0.38, -0.74 * c.x, 0.56 * c.y)));
}

float3 selfLatticeCoordinates(float3 dir, float shellIndex, float phase)
{
    float3x3 frame = selfOrbitalFrame(phase + shellIndex * 2.0943951);
    float3 latitude = mul(frame, dir);
    float3 shellPhase = shellIndex * float3(1.3, 2.1, 3.4);
    return latitude + 0.055 * sin(latitude.zxy * float3(3.0, 4.0, 5.0) + shellPhase + phase * 0.23);
}

float min3(float3 value)
{
    return min(value.x, min(value.y, value.z));
}

float selfShellRadius(float shellIndex, float pressure)
{
    float radius = 0.64 + shellIndex * 0.22 + shellIndex * shellIndex * 0.035;
    return lerp(radius, radius * 0.88, pressure);
}

void selfShellField(float3 dir, float r, float shellIndex, float pressure, float phase, inout float rail, inout float gate)
{
    float shell = r - selfShellRadius(shellIndex, pressure);
    float3 bands = abs(selfLatticeCoordinates(dir, shellIndex, phase)) * r;
    float thickness = lerp(0.010, 0.016, saturate(shellIndex * 0.5));

    float shellRail = length(float2(shell, min3(bands))) - thickness;
    float crossing = min(
        length(float2(bands.x, bands.y)),
        min(length(float2(bands.y, bands.z)), length(float2(bands.z, bands.x))));
    float shellGate = length(float2(shell * 1.25, crossing * 0.72)) - (thickness * 1.9);

    rail = min(rail, shellRail);
    gate = min(gate, shellGate);
}

SelfField selfField(float3 local, SdfObject sdfObject, float timeSeconds)
{
    float heartbeat = saturate(sdfObject.state.y);
    float pressure = saturate(sdfObject.state.z);
    float phase = timeSeconds * 0.72 + heartbeat * 2.1;

    float r = max(length(local), 0.0001);
    float3 dir = local / r;
    float coreRadius = 0.50;
    float rail = 10.0;
    float gate = 10.0;

    [unroll]
    for (int i = 0; i < 3; i++)
    {
        selfShellField(dir, r, (float)i, pressure, phase, rail, gate);
    }

    float3 coreBands = abs(selfLatticeCoordinates(dir, -1.0, phase));

    float coreShell = r - (coreRadius + 0.006);
    float inlay = length(float2(coreShell, coreBands.x * r * 0.10)) - 0.007;

    float core = sdSphere(local, coreRadius);
    float routed = smoothUnion(core, inlay, 0.006);
    routed = smoothUnion(routed, rail, 0.026);
    routed = smoothUnion(routed, gate, 0.030);

    SelfField field;
    field.core = core;
    field.inlay = inlay;
    field.rail = rail;
    field.gate = gate;
    field.distanceValue = routed;
    return field;
}

float sdfDistance(float3 p, int sdfIndex)
{
    SdfObject sdfObject = sdfObjects[sdfIndex];
    float radius = max(sdfObject.centerRadius.w, 0.001);
    float3 local = (p - sdfObject.centerRadius.xyz) / radius;
    return selfField(local, sdfObject, timeSeconds).distanceValue * radius;
}

SdfSurface sdfSurface(float3 p, int sdfIndex)
{
    SdfObject sdfObject = sdfObjects[sdfIndex];
    float radius = max(sdfObject.centerRadius.w, 0.001);
    float3 local = (p - sdfObject.centerRadius.xyz) / radius;
    SelfField field = selfField(local, sdfObject, timeSeconds);

    float isGate = field.gate <= min(min(field.core, field.inlay), field.rail) ? 1.0 : 0.0;
    float isRail = (1.0 - isGate) * (field.rail <= min(field.core, field.inlay) ? 1.0 : 0.0);
    float isInlay = (1.0 - isGate) * (1.0 - isRail) * (field.inlay <= field.core ? 1.0 : 0.0);
    float isCore = (1.0 - isGate) * (1.0 - isRail) * (1.0 - isInlay);

    float3 coreColor = float3(0.0, 0.0, 0.0);
    float3 inlayColor = float3(1.0, 0.70, 0.22);
    float3 railColor = float3(0.86, 0.54, 0.20);
    float3 gateColor = lerp(float3(0.78, 0.66, 0.42), float3(1.0, 0.82, 0.42), saturate(sdfObject.state.x));

    SdfSurface surface;
    surface.baseColor = coreColor * isCore + inlayColor * isInlay + railColor * isRail + gateColor * isGate;
    surface.metallic = 0.0 * isCore + 0.64 * isInlay + 0.72 * isRail + 0.18 * isGate;
    surface.roughness = 1.0 * isCore + 0.20 * isInlay + 0.24 * isRail + 0.18 * isGate;

    float3 selfLight = primitiveEmissionRadiance(sdfFieldId(sdfIndex));
    surface.emission = selfLight * (isRail * 0.045 + isGate * 0.08)
        + inlayColor * isInlay * 0.30
        + railColor * isRail * 0.18
        + gateColor * isGate * (0.22 + sdfObject.state.y * 0.08);
    return surface;
}

float3 shadeSdf(float2 uv, float travel, float3 p, float3 normal, int sdfIndex, SdfSurface surface)
{
    SdfObject sdfObject = sdfObjects[sdfIndex];
    float radius = max(sdfObject.centerRadius.w, 0.001);
    float3 local = (p - sdfObject.centerRadius.xyz) / radius;
    SelfField field = selfField(local, sdfObject, timeSeconds);
    bool isCore = field.core <= min(min(field.inlay, field.rail), field.gate);
    if (isCore)
    {
        float3 viewDirection = normalize(cameraPosition - p);
        float rim = pow(1.0 - saturate(dot(normal, viewDirection)), 2.2);
        float3 gold = float3(1.0, 0.62, 0.18);
        return float3(0.001, 0.0006, 0.0) + gold * (rim * 1.35 + pow(rim, 4.0) * 3.0);
    }

    return shadeSdfPbr(p, normal, surface);
}

#include "D3D12SdfProxy.hlsli"
