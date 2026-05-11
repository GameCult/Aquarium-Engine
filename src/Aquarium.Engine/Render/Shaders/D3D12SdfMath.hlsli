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
    float phase = timeSeconds * 1.35 + angle * 0.7;
    float3 center = float3(radial * 0.18, 0.04 + 0.035 * sin(phase));
    float3 p = local - center;
    float3 q = float3(dot(p.xy, radial), dot(p.xy, tangent), p.z);
    float taper = saturate((q.z + 1.0) * 0.55);
    float edgeCurl = 0.035 * sin(q.x * 8.0 + phase) * taper;
    return sdEllipsoid(q + float3(0.0, 0.0, edgeCurl), float3(0.62, 0.19, 0.72));
}

float sdHibiscusCursor(float3 local, float timeSeconds)
{
    float petal0 = sdHibiscusCursorPetal(local, 0.35 + timeSeconds * 0.12, timeSeconds);
    float petal1 = sdHibiscusCursorPetal(local, 2.444395 + timeSeconds * 0.12, timeSeconds);
    float petal2 = sdHibiscusCursorPetal(local, 4.53879 + timeSeconds * 0.12, timeSeconds);
    float petals = min(petal0, min(petal1, petal2));
    float throat = sdEllipsoid(local - float3(0.0, 0.0, -0.12), float3(0.28, 0.28, 0.50));
    float body = smoothUnion(petals, throat, 0.075);
    float contactTip = length(float2(length(local.xy) * 1.22, local.z + 1.0)) - 0.035;
    return smoothUnion(body, contactTip, 0.05);
}

#endif
