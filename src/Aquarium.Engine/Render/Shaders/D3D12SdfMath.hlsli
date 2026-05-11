#ifndef AQUARIUM_D3D12_SDF_MATH_HLSLI
#define AQUARIUM_D3D12_SDF_MATH_HLSLI

float sdSphere(float3 p, float radius)
{
    return length(p) - radius;
}

float sdTorus(float3 p, float2 radius)
{
    float2 q = float2(length(p.xy) - radius.x, p.z);
    return length(q) - radius.y;
}

float sdCapsuleSegment(float3 p, float3 a, float3 b, float radius)
{
    float3 pa = p - a;
    float3 ba = b - a;
    float h = saturate(dot(pa, ba) / max(dot(ba, ba), 0.0001));
    return length(pa - ba * h) - radius;
}

float sdSuperellipsoid(float3 p, float3 scale, float exponent)
{
    float3 q = abs(p) / scale;
    float value = pow(pow(q.x, exponent) + pow(q.y, exponent) + pow(q.z, exponent), 1.0 / exponent);
    return (value - 1.0) * min(scale.x, min(scale.y, scale.z));
}

float sdEllipsoid(float3 p, float3 radius)
{
    float3 safeRadius = max(radius, 0.001);
    float k0 = length(p / safeRadius);
    float k1 = length(p / (safeRadius * safeRadius));
    return k0 * (k0 - 1.0) / max(k1, 0.001);
}

float smoothUnion(float a, float b, float radius)
{
    float h = saturate(0.5 + 0.5 * (b - a) / radius);
    return lerp(b, a, h) - radius * h * (1.0 - h);
}

float sdEllipseFootprint(float2 p, float2 radius)
{
    float2 safeRadius = max(radius, 0.001);
    return (length(p / safeRadius) - 1.0) * min(safeRadius.x, safeRadius.y);
}

float teardropProfileRadius(float z, float contactZ, float tipZ, float radiusScale, float ripplePhase, float rippleAmount)
{
    float u = saturate((z - contactZ) / (tipZ - contactZ));
    float x = u * 2.0 - 1.0;
    float halfTerm = (1.0 - x) * 0.5;
    float teardropWeight = halfTerm * halfTerm * halfTerm;
    float baseRadius = radiusScale * sqrt(saturate(1.0 - x * x)) * teardropWeight;
    float rippleEnvelope = smoothstep(0.08, 0.22, u) * (1.0 - smoothstep(0.76, 0.94, u));
    float rippleRadius = min(rippleAmount, baseRadius * 0.07) * sin(ripplePhase) * rippleEnvelope;
    return max(baseRadius + rippleRadius, 0.0);
}

float sdTeardropRevolution(float3 local, float contactZ, float tipZ, float radiusScale, float ripplePhase, float rippleAmount)
{
    float2 samplePoint = float2(length(local.xy), local.z);
    float profileRadius = teardropProfileRadius(samplePoint.y, contactZ, tipZ, radiusScale, ripplePhase, rippleAmount);
    float radialDistance = samplePoint.x - profileRadius;
    float topDistance = samplePoint.y - tipZ;
    float bottomDistance = contactZ - samplePoint.y;
    return max(radialDistance, max(topDistance, bottomDistance));
}

float sdHibiscusCursorPetal(float3 local, float angle, float timeSeconds)
{
    float2 radial = float2(cos(angle), sin(angle));
    float2 tangent = float2(-radial.y, radial.x);
    float phase = timeSeconds * 0.95 + angle * 1.7;
    float3 center = float3(radial * 0.34, 0.10 + 0.030 * sin(phase));
    float3 p = local - center;
    float3 q = float3(dot(p.xy, radial), dot(p.xy, tangent), p.z);
    float flare = saturate((q.x + 0.34) * 1.18);
    float surfaceZ = -0.08 * q.x * q.x + 0.10 * sin(q.x * 2.6 + phase) * flare;
    float rib = 0.018 * sin(q.y * 34.0 + q.x * 9.0 + phase) * flare;
    float sheet = abs(q.z - surfaceZ - rib) - 0.026;
    float footprint = sdEllipseFootprint(float2(q.x - 0.12, q.y), float2(0.92, 0.26));
    float notch = sdEllipseFootprint(float2(q.x + 0.54, q.y), float2(0.20, 0.18));
    return max(max(sheet, footprint), -notch);
}

float sdHibiscusCursor(float3 local, float timeSeconds)
{
    float sway = 0.10 * sin(timeSeconds * 0.85);
    float petal0 = sdHibiscusCursorPetal(local, 1.58 + sway, timeSeconds);
    float petal1 = sdHibiscusCursorPetal(local, 3.58 + sway * 0.6, timeSeconds);
    float petal2 = sdHibiscusCursorPetal(local, 5.42 + sway * 0.4, timeSeconds);
    float petals = min(petal0, min(petal1, petal2));

    float throat = sdEllipsoid(local - float3(0.0, 0.0, -0.02), float3(0.38, 0.34, 0.20));
    float column = sdCapsuleSegment(local, float3(0.0, 0.0, 0.02), float3(0.0, 0.0, 0.82), 0.055);
    float bead0 = sdSphere(local - float3(0.00, 0.00, 0.88), 0.070);
    float bead1 = sdSphere(local - float3(0.10, 0.02, 0.74), 0.050);
    float bead2 = sdSphere(local - float3(-0.09, -0.03, 0.70), 0.046);
    float bead3 = sdSphere(local - float3(0.04, -0.10, 0.78), 0.042);
    float stamen = min(column, min(min(bead0, bead1), min(bead2, bead3)));

    float calyx = sdEllipsoid(local - float3(0.0, 0.0, -0.70), float3(0.24, 0.22, 0.16));
    float contactStem = sdCapsuleSegment(local, float3(0.0, 0.0, -1.0), float3(0.0, 0.0, -0.66), 0.038);

    float blossom = smoothUnion(petals, throat, 0.10);
    blossom = smoothUnion(blossom, stamen, 0.065);
    float base = smoothUnion(calyx, contactStem, 0.045);
    return smoothUnion(blossom, base, 0.070);
}

#endif
