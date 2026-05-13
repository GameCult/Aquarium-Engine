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

float sdTorusArc(float3 q, float majorRadius, float tubeRadius, float2 midDirection, float halfAngle)
{
    float2 radial = q.xy;
    float radialLength = max(length(radial), 0.0001);
    float2 direction = radial / radialLength;
    float angularDistance = (cos(halfAngle) - dot(direction, midDirection)) * majorRadius;
    float torus = length(float2(radialLength - majorRadius, q.z)) - tubeRadius;
    return max(torus, angularDistance);
}

float3 rotateZ(float3 p, float angle)
{
    float c = cos(angle);
    float s = sin(angle);
    return float3(p.x * c - p.y * s, p.x * s + p.y * c, p.z);
}

float sdRoutingArc(float3 local, float angle, float tilt, float pressure, float phase)
{
    float3 p = rotateZ(local, angle);
    float3 q = float3(p.x, p.z * cos(tilt) - p.y * sin(tilt), p.z * sin(tilt) + p.y * cos(tilt));
    float majorRadius = lerp(0.82, 0.68, pressure);
    float tubeRadius = 0.025 + 0.006 * sin(phase + angle * 1.7);
    return sdTorusArc(q, majorRadius, tubeRadius, float2(1.0, 0.0), 1.22);
}

float gateNode(float3 local, float angle, float tilt, float pressure)
{
    float majorRadius = lerp(0.82, 0.68, pressure);
    float3 p = float3(majorRadius, 0.0, 0.0);
    float3 untilt = float3(p.x, p.z * sin(tilt) + p.y * cos(tilt), p.z * cos(tilt) - p.y * sin(tilt));
    float3 node = rotateZ(untilt, -angle);
    return sdSphere(local - node, 0.075);
}

SelfParts selfParts(float3 local, SdfObject sdfObject, float timeSeconds)
{
    float activity = saturate(sdfObject.state.x);
    float heartbeat = saturate(sdfObject.state.y);
    float pressure = saturate(sdfObject.state.z);
    float phase = timeSeconds * 0.55 + heartbeat * 6.28318;
    float pulse = 0.025 * sin(phase);
    float core = sdSphere(local, 0.62 + pulse);

    float arc0 = sdRoutingArc(local, 0.00, 0.50, pressure, phase);
    float arc1 = sdRoutingArc(local, 1.57, -0.58, pressure, phase + 1.2);
    float arc2 = sdRoutingArc(local, 3.14, 0.76, pressure, phase + 2.4);
    float arc3 = sdRoutingArc(local, -1.57, -0.34, pressure, phase + 3.6);
    float rail = min(min(arc0, arc1), min(arc2, arc3));

    float gate0 = gateNode(local, 0.00, 0.50, pressure);
    float gate1 = gateNode(local, 1.57, -0.58, pressure);
    float gate2 = gateNode(local, 3.14, 0.76, pressure);
    float gate3 = gateNode(local, -1.57, -0.34, pressure);
    float gate = min(min(gate0, gate1), min(gate2, gate3));

    float seamRing = sdTorus(local.xzy, float2(0.63, 0.012));
    float seamMask = abs(local.z) - 0.10;
    float seam = max(seamRing, -seamMask);

    float routed = smoothUnion(core, rail, 0.045);
    routed = smoothUnion(routed, gate, 0.040);
    float distanceValue = smoothUnion(routed, seam, lerp(0.012, 0.030, activity));

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
    float glow = 0.5 + 0.5 * sin(timeSeconds * 1.4 + local.x * 2.0 - local.y * 1.5 + local.z * 2.5);
    float3 coreColor = lerp(float3(1.0, 0.58, 0.26), float3(1.0, 0.88, 0.62), glow);
    float3 railColor = float3(1.0, 0.62, 0.18);
    float3 gateColor = lerp(float3(1.0, 0.82, 0.24), float3(1.0, 0.96, 0.68), saturate(sdfObject.state.x));
    float3 seamColor = float3(0.09, 0.045, 0.032);

    SdfSurface surface;
    surface.baseColor = coreColor * isCore + railColor * isRail + gateColor * isGate + seamColor * isSeam;
    surface.metallic = 0.0;
    surface.roughness = 0.34 * isCore + 0.22 * isRail + 0.16 * isGate + 0.74 * isSeam;
    surface.emission = primitiveEmissionRadiance(sdfFieldId(sdfIndex))
        + coreColor * isCore * 0.40
        + railColor * isRail * 1.15
        + gateColor * isGate * (1.35 + sdfObject.state.y * 0.45)
        + seamColor * isSeam * 0.03;
    return surface;
}

float3 shadeSdf(float2 uv, float travel, float3 p, float3 normal, int sdfIndex, SdfSurface surface)
{
    return shadeSdfPbr(p, normal, surface);
}

#include "D3D12SdfProxy.hlsli"
