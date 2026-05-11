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

float sdHibiscusCursorPetal(float3 local, float angle, float timeSeconds)
{
    float2 radial = float2(cos(angle), sin(angle));
    float2 tangent = float2(-radial.y, radial.x);
    float phase = timeSeconds * 0.95 + angle * 1.7;
    float3 center = float3(radial * 0.36, 0.09 + 0.030 * sin(phase));
    float3 p = local - center;
    float3 q = float3(dot(p.xy, radial), dot(p.xy, tangent), p.z);
    float lengthT = saturate((q.x + 0.34) / 1.38);
    float edgeT = saturate(abs(q.y) / max(lerp(0.22, 0.08, lengthT), 0.001));
    float lift = 0.31 * lengthT * lengthT + 0.060 * edgeT * edgeT;
    float curl = 0.075 * sin(q.x * 2.4 + phase) * smoothstep(0.08, 0.95, lengthT);
    float rib = 0.014 * sin(q.y * 36.0 + q.x * 8.0 + phase) * smoothstep(0.05, 0.90, lengthT);
    float surfaceZ = -0.075 + lift + curl;
    float thickness = lerp(0.040, 0.009, lengthT) * lerp(1.0, 0.42, edgeT);
    float sheet = abs(q.z - surfaceZ - rib) - thickness;
    float footprint = sdTaperedCapsule2(float2(q.x, q.y), float2(-0.34, 0.0), float2(1.04, 0.0), 0.23, 0.07);
    float notch = sdSphere(float3(q.x + 0.40, q.y, q.z), 0.11);
    return max(max(sheet, footprint), -notch);
}

float sdHibiscusSepal(float3 local, float angle)
{
    float2 radial = float2(cos(angle), sin(angle));
    float3 a = float3(0.0, 0.0, -0.56);
    float3 b = float3(radial * 0.22, -0.39);
    return sdTaperedCapsuleSegment(local, a, b, 0.046, 0.020);
}

float sdHibiscusStamen(float3 local)
{
    float column = sdQuadraticTube(local, float3(0.0, 0.0, 0.02), float3(-0.12, 0.08, 0.43), float3(0.13, 0.04, 0.86), 0.048, 0.032);
    float filament0 = sdQuadraticTube(local, float3(-0.02, 0.03, 0.36), float3(-0.12, 0.09, 0.46), float3(-0.18, 0.07, 0.56), 0.014, 0.008);
    float filament1 = sdQuadraticTube(local, float3(0.02, 0.04, 0.48), float3(0.15, 0.13, 0.58), float3(0.24, 0.12, 0.70), 0.014, 0.008);
    float filament2 = sdQuadraticTube(local, float3(0.04, 0.02, 0.61), float3(0.15, -0.09, 0.70), float3(0.09, -0.19, 0.80), 0.012, 0.007);
    float filament3 = sdQuadraticTube(local, float3(0.08, 0.04, 0.72), float3(0.20, 0.03, 0.84), float3(0.28, 0.00, 0.93), 0.011, 0.007);

    float bead0 = sdSphere(local - float3(0.14, 0.04, 0.92), 0.064);
    float bead1 = sdSphere(local - float3(0.24, 0.12, 0.70), 0.044);
    float bead2 = sdSphere(local - float3(-0.19, 0.07, 0.56), 0.042);
    float bead3 = sdSphere(local - float3(0.09, -0.19, 0.80), 0.039);
    float bead4 = sdSphere(local - float3(0.28, 0.00, 0.93), 0.036);
    float sideFilaments = min(min(filament0, filament1), min(filament2, filament3));
    float beads = min(min(bead0, bead1), min(min(bead2, bead3), bead4));
    return min(column, min(sideFilaments, beads));
}

float sdHibiscusCursor(float3 local, float timeSeconds)
{
    float sway = 0.10 * sin(timeSeconds * 0.85);
    float petal0 = sdHibiscusCursorPetal(local, 1.54 + sway, timeSeconds);
    float petal1 = sdHibiscusCursorPetal(local, 2.78 + sway * 0.7, timeSeconds);
    float petal2 = sdHibiscusCursorPetal(local, 4.02 + sway * 0.5, timeSeconds);
    float petal3 = sdHibiscusCursorPetal(local, 5.26 + sway * 0.4, timeSeconds);
    float petal4 = sdHibiscusCursorPetal(local, 0.34 + sway * 0.6, timeSeconds);
    float petals = min(min(petal0, petal1), min(min(petal2, petal3), petal4));

    float throatSphere = sdSphere(local - float3(0.0, 0.0, -0.05), 0.24);
    float throatTop = local.z - 0.07;
    float throat = max(throatSphere, throatTop);
    float stamen = sdHibiscusStamen(local);

    float calyxCore = sdQuadraticTube(local, float3(0.0, 0.0, -1.0), float3(0.06, -0.04, -0.72), float3(0.0, 0.0, -0.49), 0.014, 0.105);
    float sepals = min(sdHibiscusSepal(local, 1.57), min(sdHibiscusSepal(local, 3.66), sdHibiscusSepal(local, 5.75)));
    float calyx = smoothUnion(calyxCore, sepals, 0.035);

    float blossom = smoothUnion(petals, throat, 0.10);
    blossom = smoothUnion(blossom, stamen, 0.065);
    return smoothUnion(blossom, calyx, 0.070);
}

#endif
