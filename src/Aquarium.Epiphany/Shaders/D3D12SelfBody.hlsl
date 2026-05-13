static const int SDF_INDEX = 0;

#include "D3D12SdfCommon.hlsli"
#include "D3D12SdfMath.hlsli"

struct SelfParts
{
    float core;
    float rail;
    float halo;
    float gate;
    float pupil;
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

float3 selfOrbitPlane(float3 local, float angle, float tilt)
{
    float3 p = rotateZ(local, angle);
    return float3(p.x, p.z * cos(tilt) - p.y * sin(tilt), p.z * sin(tilt) + p.y * cos(tilt));
}

float3 selfOrbitPoint(float angle, float tilt, float orbitAngle, float orbitRadius)
{
    float3 q = float3(cos(orbitAngle) * orbitRadius, sin(orbitAngle) * orbitRadius, 0.0);
    float3 p = float3(q.x, q.z * sin(tilt) + q.y * cos(tilt), q.z * cos(tilt) - q.y * sin(tilt));
    return rotateZ(p, -angle);
}

float sdRoutingArc(float3 local, float angle, float tilt, float pressure, float phase)
{
    float3 q = selfOrbitPlane(local, angle, tilt);
    float majorRadius = lerp(0.82, 0.68, pressure);
    float tubeRadius = 0.025 + 0.006 * sin(phase + angle * 1.7);
    return sdTorusArc(q, majorRadius, tubeRadius, float2(1.0, 0.0), 1.22);
}

float sdOrreryRing(float3 local, float angle, float tilt, float pressure, float radius, float tubeRadius)
{
    float3 q = selfOrbitPlane(local, angle, tilt);
    return sdTorus(q, float2(lerp(radius, radius * 0.88, pressure), tubeRadius));
}

float gateNode(float3 local, float angle, float tilt, float pressure)
{
    float majorRadius = lerp(0.82, 0.68, pressure);
    float3 node = selfOrbitPoint(angle, tilt, 0.0, majorRadius);
    return sdSphere(local - node, 0.075);
}

float eyeNode(float3 local, float angle, float tilt, float orbitAngle, float orbitRadius)
{
    float3 node = selfOrbitPoint(angle, tilt, orbitAngle, orbitRadius);
    return sdEllipsoid(local - node, float3(0.075, 0.050, 0.038));
}

float eyePupil(float3 local, float angle, float tilt, float orbitAngle, float orbitRadius)
{
    float3 node = selfOrbitPoint(angle, tilt, orbitAngle, orbitRadius);
    float3 outward = normalize(node);
    return sdSphere(local - node - outward * 0.034, 0.026);
}

SelfParts selfParts(float3 local, SdfObject sdfObject, float timeSeconds)
{
    float activity = saturate(sdfObject.state.x);
    float heartbeat = saturate(sdfObject.state.y);
    float pressure = saturate(sdfObject.state.z);
    float phase = timeSeconds * 0.55 + heartbeat * 6.28318;
    float pulse = 0.025 * sin(phase);
    float core = sdSphere(local, 0.48 + pulse);

    float arc0 = sdRoutingArc(local, 0.00, 0.50, pressure, phase);
    float arc1 = sdRoutingArc(local, 1.57, -0.58, pressure, phase + 1.2);
    float arc2 = sdRoutingArc(local, 3.14, 0.76, pressure, phase + 2.4);
    float arc3 = sdRoutingArc(local, -1.57, -0.34, pressure, phase + 3.6);
    float rail = min(min(arc0, arc1), min(arc2, arc3));

    float halo0 = sdOrreryRing(local, 0.42, 1.15, pressure, 0.98, 0.012);
    float halo1 = sdOrreryRing(local, 2.12, -0.92, pressure, 0.92, 0.010);
    float halo2 = sdOrreryRing(local, -1.18, 0.28, pressure, 0.74, 0.009);
    float halo = min(halo0, min(halo1, halo2));

    float gate0 = gateNode(local, 0.00, 0.50, pressure);
    float gate1 = gateNode(local, 1.57, -0.58, pressure);
    float gate2 = gateNode(local, 3.14, 0.76, pressure);
    float gate3 = gateNode(local, -1.57, -0.34, pressure);
    float gate = min(min(gate0, gate1), min(gate2, gate3));

    float eye0 = eyeNode(local, 0.42, 1.15, 1.30 + phase * 0.08, 0.98);
    float eye1 = eyeNode(local, 0.42, 1.15, -1.55 + phase * 0.08, 0.98);
    float eye2 = eyeNode(local, 2.12, -0.92, 2.35 - phase * 0.06, 0.92);
    float eye3 = eyeNode(local, -1.18, 0.28, -0.72 + phase * 0.05, 0.74);
    gate = min(gate, min(min(eye0, eye1), min(eye2, eye3)));

    float pupil0 = eyePupil(local, 0.42, 1.15, 1.30 + phase * 0.08, 0.98);
    float pupil1 = eyePupil(local, 0.42, 1.15, -1.55 + phase * 0.08, 0.98);
    float pupil2 = eyePupil(local, 2.12, -0.92, 2.35 - phase * 0.06, 0.92);
    float pupil3 = eyePupil(local, -1.18, 0.28, -0.72 + phase * 0.05, 0.74);
    float pupil = min(min(pupil0, pupil1), min(pupil2, pupil3));

    float seamRing = sdTorus(local.xzy, float2(0.50, 0.010));
    float seamMask = abs(local.z) - 0.10;
    float seam = max(seamRing, -seamMask);

    float routed = smoothUnion(core, rail, 0.030);
    routed = smoothUnion(routed, halo, 0.020);
    routed = smoothUnion(routed, gate, 0.036);
    routed = smoothUnion(routed, pupil, 0.010);
    float distanceValue = smoothUnion(routed, seam, lerp(0.012, 0.030, activity));

    SelfParts parts;
    parts.core = core;
    parts.rail = rail;
    parts.halo = halo;
    parts.gate = gate;
    parts.pupil = pupil;
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
    float nonCore = min(min(parts.rail, parts.halo), min(parts.gate, min(parts.pupil, parts.seam)));
    float isPupil = parts.pupil <= min(min(parts.core, parts.rail), min(parts.halo, min(parts.gate, parts.seam))) ? 1.0 : 0.0;
    float isGate = (1.0 - isPupil) * (parts.gate <= min(min(parts.core, parts.rail), min(parts.halo, parts.seam)) ? 1.0 : 0.0);
    float isRail = (1.0 - isPupil) * (1.0 - isGate) * (parts.rail <= min(min(parts.core, parts.halo), parts.seam) ? 1.0 : 0.0);
    float isHalo = (1.0 - isPupil) * (1.0 - isGate) * (1.0 - isRail) * (parts.halo <= min(parts.core, parts.seam) ? 1.0 : 0.0);
    float isSeam = (1.0 - isPupil) * (1.0 - isGate) * (1.0 - isRail) * (1.0 - isHalo) * (parts.seam <= min(parts.core, nonCore) ? 1.0 : 0.0);
    float isCore = (1.0 - isPupil) * (1.0 - isGate) * (1.0 - isRail) * (1.0 - isHalo) * (1.0 - isSeam);
    float glow = 0.5 + 0.5 * sin(timeSeconds * 1.4 + local.x * 2.0 - local.y * 1.5 + local.z * 2.5);
    float3 coreColor = lerp(float3(0.50, 0.26, 0.15), float3(0.82, 0.48, 0.26), glow);
    float3 railColor = float3(0.78, 0.45, 0.16);
    float3 haloColor = float3(0.70, 0.53, 0.34);
    float3 gateColor = lerp(float3(0.70, 0.62, 0.46), float3(0.92, 0.78, 0.50), saturate(sdfObject.state.x));
    float3 pupilColor = float3(0.10, 0.045, 0.018);
    float3 seamColor = float3(0.06, 0.028, 0.020);

    SdfSurface surface;
    surface.baseColor = coreColor * isCore + railColor * isRail + haloColor * isHalo + gateColor * isGate + pupilColor * isPupil + seamColor * isSeam;
    surface.metallic = 0.0 * isCore + 0.65 * isRail + 0.82 * isHalo + 0.0 * isGate + 0.0 * isPupil + 0.0 * isSeam;
    surface.roughness = 0.42 * isCore + 0.26 * isRail + 0.22 * isHalo + 0.18 * isGate + 0.32 * isPupil + 0.78 * isSeam;
    float3 selfLight = primitiveEmissionRadiance(sdfFieldId(sdfIndex));
    surface.emission = selfLight * (isRail * 0.05 + isGate * 0.04 + isPupil * 0.12)
        + coreColor * isCore * 0.015
        + railColor * isRail * 0.16
        + haloColor * isHalo * 0.025
        + gateColor * isGate * (0.10 + sdfObject.state.y * 0.06)
        + float3(1.0, 0.50, 0.08) * isPupil * (0.65 + sdfObject.state.y * 0.18)
        + seamColor * isSeam * 0.01;
    return surface;
}

float3 shadeSdf(float2 uv, float travel, float3 p, float3 normal, int sdfIndex, SdfSurface surface)
{
    return shadeSdfPbr(p, normal, surface);
}

#include "D3D12SdfProxy.hlsli"
