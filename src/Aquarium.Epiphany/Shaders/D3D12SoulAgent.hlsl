static const int SDF_INDEX = 6;

#include "D3D12SdfCommon.hlsli"
#include "D3D12SdfMath.hlsli"

struct SoulField
{
    float body;
    float riskSeam;
    float findingEdge;
    float paneGroove;
    float fracture;
    float chamber;
    float iceGrain;
    float distanceValue;
};

struct SoulCutField
{
    float3 warped;
    float a;
    float b;
    float c;
    float d;
};

SoulCutField soulCutField(float3 local, float confidence, float risk)
{
    float breathe = 0.018 + confidence * 0.014;
    float3 warped = local + breathe * float3(
        sin(local.y * 3.1 + local.z * 1.7 + timeSeconds * 0.17),
        sin(local.z * 2.7 - local.x * 2.2 + timeSeconds * 0.13),
        sin(local.x * 2.9 + local.y * 1.5 - timeSeconds * 0.19));

    SoulCutField cuts;
    cuts.warped = warped;
    cuts.a = dot(warped, normalize(float3(0.92, 0.58, 0.38)));
    cuts.b = dot(warped, normalize(float3(-0.42, 0.96, 0.72)));
    cuts.c = dot(warped, normalize(float3(0.70, -0.30, 1.04)));
    cuts.d = dot(warped, normalize(float3(0.22, 1.12, -0.64)));
    return cuts;
}

float soulBodyDistance(float3 local, float confidence, float risk)
{
    SoulCutField cuts = soulCutField(local, confidence, risk);
    float scale = lerp(0.66, 0.74, confidence) - risk * 0.030;
    float bevel = lerp(0.075, 0.040, confidence) + risk * 0.010;
    float octa = (dot(abs(local), float3(1.0, 1.0, 1.0)) - scale) * 0.57735027;
    float grownCuts = max(max(abs(cuts.a) - scale * 0.92, abs(cuts.b) - scale * 0.88),
        max(abs(cuts.c) - scale * 0.94, abs(cuts.d) - scale * 0.90));
    return max(octa, grownCuts * 0.82) - bevel;
}

SoulField soulField(float3 local, SdfObject sdfObject)
{
    float confidence = saturate(sdfObject.state.y);
    float risk = saturate(sdfObject.state.z * 1.55 + (1.0 - confidence) * 0.35);
    float body = soulBodyDistance(local, confidence, risk);
    SoulCutField cuts = soulCutField(local, confidence, risk);
    float4 plane = float4(cuts.a, cuts.b, cuts.c, cuts.d);
    float4 planeAbs = abs(plane);
    float dominant = max(max(planeAbs.x, planeAbs.y), max(planeAbs.z, planeAbs.w));
    float chamber = saturate(1.0 - length(cuts.warped) * 1.22);

    float riskPlane = cuts.b - cuts.c * 0.58 + cuts.d * 0.24 + 0.018 * sin(timeSeconds * 0.51 + confidence * 2.0);
    float edgeAgreement = min(min(abs(cuts.a - cuts.b), abs(cuts.a + cuts.c)), min(abs(cuts.b + cuts.d), abs(cuts.c - cuts.d)));
    float riskWidth = lerp(0.012, 0.028, risk);
    float riskSeam = min(abs(riskPlane), edgeAgreement * 0.62 + abs(riskPlane) * 0.42) - riskWidth;
    float branchA = abs(riskPlane + (cuts.a - cuts.d) * 0.20);
    float branchB = abs(riskPlane - (cuts.b + cuts.c) * 0.17);
    float branchC = abs(riskPlane + cuts.a * 0.11 + cuts.b * 0.08 - cuts.d * 0.19);
    float fracture = min(min(branchA, branchB), branchC) - riskWidth * 0.32;

    float edgeMask = smoothstep(0.04, 0.30, dominant - min(min(planeAbs.x, planeAbs.y), min(planeAbs.z, planeAbs.w)));
    float findingWidth = lerp(0.010, 0.024, confidence) * edgeMask;
    float findingEdge = edgeAgreement - findingWidth;
    float paneA = abs(cuts.a * 0.62 + cuts.b * 0.28 - cuts.c * 0.18);
    float paneB = abs(cuts.c * 0.58 - cuts.d * 0.34 + cuts.a * 0.16);
    float paneC = abs(cuts.b * 0.44 + cuts.d * 0.48);
    float paneGroove = min(min(paneA, paneB), paneC) - lerp(0.008, 0.018, confidence);
    float iceGrain = sin(cuts.a * 41.0 + cuts.c * 17.0) * sin(cuts.b * 37.0 - cuts.d * 23.0);

    SoulField field;
    field.body = body;
    field.riskSeam = riskSeam;
    field.findingEdge = findingEdge;
    field.paneGroove = paneGroove;
    field.fracture = fracture;
    field.chamber = chamber;
    field.iceGrain = iceGrain;
    field.distanceValue = body;
    return field;
}

float sdfDistance(float3 p, int sdfIndex)
{
    SdfObject sdfObject = sdfObjects[sdfIndex];
    float radius = max(sdfObject.centerRadius.w, 0.001);
    float3 local = (p - sdfObject.centerRadius.xyz) / radius;
    return soulField(local, sdfObject).distanceValue * radius;
}

SdfSurface sdfSurface(float3 p, int sdfIndex)
{
    SdfObject sdfObject = sdfObjects[sdfIndex];
    float radius = max(sdfObject.centerRadius.w, 0.001);
    float3 local = (p - sdfObject.centerRadius.xyz) / radius;
    SoulField field = soulField(local, sdfObject);
    float confidence = saturate(sdfObject.state.y);
    float risk = saturate(sdfObject.state.z * 1.55 + (1.0 - confidence) * 0.35);

    float riskMask = (1.0 - smoothstep(0.0, 0.030, abs(field.riskSeam))) * risk;
    float fractureMask = (1.0 - riskMask) * (1.0 - smoothstep(0.0, 0.020, abs(field.fracture))) * risk;
    float findingMask = (1.0 - riskMask) * (1.0 - fractureMask) * (1.0 - smoothstep(0.0, 0.024, abs(field.findingEdge)));
    float paneMask = (1.0 - riskMask) * (1.0 - fractureMask) * (1.0 - findingMask) * (1.0 - smoothstep(0.0, 0.022, abs(field.paneGroove)));
    float facetMask = saturate(1.0 - riskMask - fractureMask - findingMask - paneMask);

    float frost = 0.5 + 0.5 * field.iceGrain;
    float3 facetColor = lerp(float3(0.18, 0.24, 0.34), float3(0.70, 0.84, 1.0), confidence) * lerp(0.82, 1.18, frost);
    float3 paneColor = lerp(float3(0.34, 0.58, 0.86), float3(0.86, 0.97, 1.0), confidence);
    float3 findingColor = lerp(float3(0.10, 0.30, 0.78), float3(0.68, 0.90, 1.0), confidence);
    float3 riskColor = lerp(float3(0.30, 0.020, 0.045), float3(0.95, 0.070, 0.055), risk);

    SdfSurface surface;
    surface.baseColor = facetColor * facetMask + paneColor * paneMask + findingColor * findingMask + riskColor * saturate(riskMask + fractureMask);
    surface.metallic = 0.0;
    surface.roughness = 0.18 * facetMask + 0.10 * paneMask + 0.075 * findingMask + 0.14 * saturate(riskMask + fractureMask);
    surface.emission = paneColor * paneMask * (0.10 + confidence * 0.16)
        + findingColor * findingMask * (0.42 + confidence * 0.78)
        + riskColor * riskMask * (0.24 + risk * 0.82)
        + riskColor * fractureMask * (0.16 + risk * 0.58);
    return surface;
}

float3 soulCrystalShade(float3 p, float3 normal, int sdfIndex, SdfSurface surface)
{
    SdfObject sdfObject = sdfObjects[sdfIndex];
    float radius = max(sdfObject.centerRadius.w, 0.001);
    float3 local = (p - sdfObject.centerRadius.xyz) / radius;
    float confidence = saturate(sdfObject.state.y);
    float risk = saturate(sdfObject.state.z * 1.55 + (1.0 - confidence) * 0.35);
    SoulField field = soulField(local, sdfObject);
    SoulCutField cuts = soulCutField(local, confidence, risk);

    float3 viewDirection = normalize(cameraPosition - p);
    float ndv = saturate(dot(normal, viewDirection));
    float3 reflected = reflect(-viewDirection, normal);
    float3 refracted = refract(-viewDirection, normal, 0.74);
    float refractionValid = dot(refracted, refracted) > 0.001 ? 1.0 : 0.0;
    float3 glassDirection = normalize(lerp(reflected, refracted, refractionValid));
    float3 glassTint = lerp(float3(0.42, 0.56, 0.86), float3(0.78, 0.90, 1.0), confidence);
    float3 pseudoRefraction = studioPmremSample(glassDirection, 1.25) * glassTint * 0.88;

    float centerGlow = pow(ndv, 0.72) * saturate(1.45 - length(local) * 0.84);
    float rimGlow = pow(1.0 - ndv, 2.15);
    float facetFlash = pow(saturate(abs(normal.x) + abs(normal.y) + abs(normal.z) - 1.05), 2.0);
    float internalChamber = field.chamber * field.chamber;
    float paneCaustic = (1.0 - smoothstep(0.0, 0.020, abs(field.paneGroove)))
        * (0.55 + 0.45 * sin((cuts.a * 17.0 + cuts.b * 23.0 + cuts.c * 31.0 + cuts.d * 11.0) + timeSeconds * 0.23));
    float crackLight = (1.0 - smoothstep(0.0, 0.022, abs(field.riskSeam))) * risk
        + (1.0 - smoothstep(0.0, 0.016, abs(field.fracture))) * risk * 0.72;
    float pulse = 0.88 + 0.12 * sin(timeSeconds * 1.7 + confidence * 2.4);
    float3 oathLight = float3(1.0, 0.96, 0.82) * pulse * (0.70 + confidence * 1.60) * (centerGlow * 0.62 + internalChamber * 1.58 + rimGlow * 0.18);
    float3 edgeLight = float3(0.40, 0.74, 1.0) * (rimGlow * 1.05 + facetFlash * 0.55 + paneCaustic * 0.58) * (0.78 + confidence * 0.90);
    float3 riskFaultLight = float3(1.0, 0.10, 0.055) * crackLight * (0.62 + pow(rimGlow, 0.65) * 0.26);

    return shadeSdfPbr(p, normal, surface) * 0.32
        + pseudoRefraction
        + oathLight
        + edgeLight
        + riskFaultLight;
}

float3 shadeSdf(float2 uv, float travel, float3 p, float3 normal, int sdfIndex, SdfSurface surface)
{
    return soulCrystalShade(p, normal, sdfIndex, surface);
}

#define SDF_TRACE_STEP_SCALE 0.16
#define SDF_TRACE_MAX_STEP_RADIUS_SCALE 0.009

#include "D3D12SdfProxy.hlsli"
