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

float sdTaperedCapsuleSegment(float3 p, float3 a, float3 b, float radiusA, float radiusB)
{
    float3 pa = p - a;
    float3 ba = b - a;
    float h = saturate(dot(pa, ba) / max(dot(ba, ba), 0.0001));
    float radius = lerp(radiusA, radiusB, h);
    return length(pa - ba * h) - radius;
}

float sdQuadraticTube(float3 p, float3 a, float3 b, float3 c, float radiusA, float radiusC)
{
    float t0 = 0.3333333;
    float u0 = 1.0 - t0;
    float3 p0 = a * (u0 * u0) + b * (2.0 * u0 * t0) + c * (t0 * t0);
    float r0 = lerp(radiusA, radiusC, t0);

    float t1 = 0.6666667;
    float u1 = 1.0 - t1;
    float3 p1 = a * (u1 * u1) + b * (2.0 * u1 * t1) + c * (t1 * t1);
    float r1 = lerp(radiusA, radiusC, t1);

    float s0 = sdTaperedCapsuleSegment(p, a, p0, radiusA, r0);
    float s1 = sdTaperedCapsuleSegment(p, p0, p1, r0, r1);
    float s2 = sdTaperedCapsuleSegment(p, p1, c, r1, radiusC);
    return min(s0, min(s1, s2));
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

float sdLensFootprint(float2 p, float2 a, float2 b, float radius)
{
    return max(length(p - a) - radius, length(p - b) - radius);
}

float sdTaperedCapsule2(float2 p, float2 a, float2 b, float radiusA, float radiusB)
{
    float2 pa = p - a;
    float2 ba = b - a;
    float h = saturate(dot(pa, ba) / max(dot(ba, ba), 0.0001));
    float radius = lerp(radiusA, radiusB, h);
    return length(pa - ba * h) - radius;
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

#endif
