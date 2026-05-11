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
    float3 center = float3(radial * 0.30, 0.12 + 0.030 * sin(phase));
    float3 p = local - center;
    float3 q = float3(dot(p.xy, radial), dot(p.xy, tangent), p.z);
    float flare = saturate((q.x + 0.28) * 1.25);
    float ruffle = 0.035 * sin(q.y * 28.0 + q.x * 7.0 + phase) * flare;
    float curl = 0.075 * sin(q.x * 3.8 + phase) * flare;
    float petal = sdEllipsoid(q + float3(0.0, ruffle, curl), float3(0.80, 0.24, 0.30));
    return petal;
}

float sdHibiscusCursor(float3 local, float timeSeconds)
{
    float sway = 0.10 * sin(timeSeconds * 0.85);
    float petal0 = sdHibiscusCursorPetal(local, 1.58 + sway, timeSeconds);
    float petal1 = sdHibiscusCursorPetal(local, 3.58 + sway * 0.6, timeSeconds);
    float petal2 = sdHibiscusCursorPetal(local, 5.42 + sway * 0.4, timeSeconds);
    float petals = min(petal0, min(petal1, petal2));

    float throat = sdEllipsoid(local - float3(0.0, 0.0, -0.03), float3(0.34, 0.30, 0.22));
    float column = sdCapsuleSegment(local, float3(0.02, 0.0, 0.06), float3(-0.22, 0.72, 0.58), 0.035);
    float bead0 = sdSphere(local - float3(-0.31, 0.83, 0.64), 0.060);
    float bead1 = sdSphere(local - float3(-0.18, 0.78, 0.68), 0.045);
    float bead2 = sdSphere(local - float3(-0.38, 0.69, 0.60), 0.040);
    float stamen = min(column, min(bead0, min(bead1, bead2)));

    float calyx = sdEllipsoid(local - float3(0.0, 0.0, -0.78), float3(0.20, 0.18, 0.18));
    float contactStem = sdCapsuleSegment(local, float3(0.0, 0.0, -1.0), float3(0.0, 0.0, -0.70), 0.030);

    float blossom = smoothUnion(petals, throat, 0.10);
    blossom = smoothUnion(blossom, stamen, 0.045);
    float base = smoothUnion(calyx, contactStem, 0.045);
    return smoothUnion(blossom, base, 0.070);
}

#endif
