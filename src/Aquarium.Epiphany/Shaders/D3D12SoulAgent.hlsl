static const int SDF_INDEX = 6;

#include "D3D12SdfCommon.hlsli"
#include "D3D12SdfMath.hlsli"

struct SoulField
{
    float body;
    float relief;
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
    float breathe = 0.020 + confidence * 0.018;
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
    float scale = lerp(0.58, 0.66, confidence) - risk * 0.026;
    float bevel = lerp(0.085, 0.040, confidence) + risk * 0.012;
    float core = dot(abs(local), float3(0.82, 0.95, 0.88)) * 0.60 - scale;
    float grownCuts = max(max(abs(cuts.a) - (scale * 0.82), abs(cuts.b) - (scale * 0.78)),
        max(abs(cuts.c) - (scale * 0.84), abs(cuts.d) - (scale * 0.80)));
    return max(core, grownCuts) - bevel;
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
    float smallest = min(min(planeAbs.x, planeAbs.y), min(planeAbs.z, planeAbs.w));
    float chamber = saturate(1.0 - length(cuts.warped) * 1.22);

    float riskPlane = cuts.b - cuts.c * 0.58 + cuts.d * 0.24 + 0.018 * sin(timeSeconds * 0.51 + confidence * 2.0);
    float edgeAgreement = min(min(abs(cuts.a - cuts.b), abs(cuts.a + cuts.c)), min(abs(cuts.b + cuts.d), abs(cuts.c - cuts.d)));
    float riskWidth = lerp(0.010, 0.036, risk);
    float riskSeam = min(abs(riskPlane), edgeAgreement * 0.62 + abs(riskPlane) * 0.42) - riskWidth;
    float branchA = abs(riskPlane + (cuts.a - cuts.d) * 0.24);
    float branchB = abs(riskPlane - (cuts.b + cuts.c) * 0.20);
    float branchC = abs(riskPlane + cuts.a * 0.12 + cuts.b * 0.09 - cuts.d * 0.21);
    float fracture = min(min(branchA, branchB), branchC) - riskWidth * 0.40;

    float edgeMask = smoothstep(0.04, 0.30, dominant - smallest);
    float findingWidth = lerp(0.008, 0.020, confidence) * edgeMask;
    float findingEdge = edgeAgreement - findingWidth;
    float paneA = abs(cuts.a * 0.62 + cuts.b * 0.28 - cuts.c * 0.18);
    float paneB = abs(cuts.c * 0.58 - cuts.d * 0.34 + cuts.a * 0.16);
    float paneC = abs(cuts.b * 0.44 + cuts.d * 0.48);
    float paneGroove = min(min(paneA, paneB), paneC) - lerp(0.006, 0.014, confidence);
    float iceGrain = sin(cuts.a * 41.0 + cuts.c * 17.0) * sin(cuts.b * 37.0 - cuts.d * 23.0);
    float surfaceBand = 1.0 - smoothstep(0.010, 0.18, abs(body));
    float equatorLift = 1.0 - smoothstep(0.08, 0.56, abs(local.y));
    float activeFacet = smoothstep(0.02, 0.16, dominant - smallest);
    float seamMask = 1.0 - smoothstep(0.0, riskWidth + 0.020, abs(riskSeam));
    float findingMask = 1.0 - smoothstep(0.0, findingWidth + 0.018, abs(findingEdge));
    float fractureMask = (1.0 - smoothstep(0.0, riskWidth + 0.016, abs(fracture))) * risk;
    float facetLift = (0.018 + confidence * 0.018 + risk * 0.018) * equatorLift * activeFacet;
    float grooveCut = (0.014 + confidence * 0.010) * findingMask
        + (0.018 + risk * 0.024) * seamMask * risk
        + (0.012 + risk * 0.020) * fractureMask;
    float relief = surfaceBand * (facetLift - grooveCut);

    SoulField field;
    field.body = body;
    field.relief = relief;
    field.riskSeam = riskSeam;
    field.findingEdge = findingEdge;
    field.paneGroove = paneGroove;
    field.fracture = fracture;
    field.chamber = chamber;
    field.iceGrain = iceGrain;
    field.distanceValue = body - relief;
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
    float fractureMask = (1.0 - riskMask) * (1.0 - smoothstep(0.0, 0.022, abs(field.fracture))) * risk;
    float findingMask = (1.0 - riskMask) * (1.0 - fractureMask) * (1.0 - smoothstep(0.0, 0.028, abs(field.findingEdge)));
    float paneMask = (1.0 - riskMask) * (1.0 - fractureMask) * (1.0 - findingMask) * (1.0 - smoothstep(0.0, 0.020, abs(field.paneGroove)));
    float facetMask = saturate(1.0 - riskMask - fractureMask - findingMask - paneMask);

    float frost = 0.5 + 0.5 * field.iceGrain;
    float3 facetColor = lerp(float3(0.055, 0.075, 0.13), float3(0.46, 0.66, 0.96), confidence) * lerp(0.74, 1.16, frost);
    float3 paneColor = lerp(float3(0.16, 0.36, 0.70), float3(0.82, 0.96, 1.0), confidence);
    float3 findingColor = lerp(float3(0.10, 0.30, 0.78), float3(0.68, 0.90, 1.0), confidence);
    float3 riskColor = lerp(float3(0.30, 0.020, 0.045), float3(0.95, 0.070, 0.055), risk);

    SdfSurface surface;
    surface.baseColor = facetColor * facetMask + paneColor * paneMask + findingColor * findingMask + riskColor * saturate(riskMask + fractureMask);
    surface.metallic = 0.0;
    surface.roughness = 0.055 * facetMask + 0.070 * paneMask + 0.080 * findingMask + 0.13 * saturate(riskMask + fractureMask);
    surface.emission = paneColor * paneMask * (0.12 + confidence * 0.22)
        + findingColor * findingMask * (0.42 + confidence * 0.82)
        + riskColor * riskMask * (0.28 + risk * 0.95)
        + riskColor * fractureMask * (0.20 + risk * 0.74);
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
    float3 refracted = refract(-viewDirection, normal, 0.69);
    float refractionValid = dot(refracted, refracted) > 0.001 ? 1.0 : 0.0;
    float3 glassDirection = normalize(lerp(reflected, refracted, refractionValid));
    float3 glassTint = lerp(float3(0.36, 0.50, 0.86), float3(0.72, 0.88, 1.0), confidence);
    float3 refractedEnv = studioPmremSample(glassDirection, 0.38);
    float3 reflectedEnv = studioPmremSample(reflected, 0.72);
    float fresnel = pow(1.0 - ndv, 4.0);
    float3 pseudoRefraction = (refractedEnv * 0.86 + pow(saturate(refractedEnv), float3(1.45, 1.45, 1.45)) * 0.46) * glassTint
        + reflectedEnv * fresnel * 0.38;

    float centerGlow = pow(ndv, 0.72) * saturate(1.45 - length(local) * 0.84);
    float rimGlow = pow(1.0 - ndv, 2.15);
    float facetFlash = pow(saturate(abs(normal.x) + abs(normal.y) + abs(normal.z) - 1.05), 2.0);
    float internalChamber = field.chamber * field.chamber;
    float paneCaustic = (1.0 - smoothstep(0.0, 0.020, abs(field.paneGroove)))
        * (0.55 + 0.45 * sin((cuts.a * 17.0 + cuts.b * 23.0 + cuts.c * 31.0 + cuts.d * 11.0) + timeSeconds * 0.23));
    float crackLight = (1.0 - smoothstep(0.0, 0.024, abs(field.riskSeam))) * risk
        + (1.0 - smoothstep(0.0, 0.018, abs(field.fracture))) * risk * 0.82;
    float pulse = 0.88 + 0.12 * sin(timeSeconds * 1.7 + confidence * 2.4);
    float3 oathLight = float3(1.0, 0.95, 0.78) * pulse * (0.58 + confidence * 1.35) * (centerGlow * 0.78 + internalChamber * 1.32 + rimGlow * 0.26);
    float3 edgeLight = float3(0.42, 0.72, 1.0) * (rimGlow * 0.90 + facetFlash * 0.55 + paneCaustic * 0.60) * (0.72 + confidence * 0.9);
    float3 riskFaultLight = float3(1.0, 0.10, 0.055) * crackLight * (0.62 + pow(rimGlow, 0.65) * 0.34);

    return shadeSdfPbr(p, normal, surface) * 0.38
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
