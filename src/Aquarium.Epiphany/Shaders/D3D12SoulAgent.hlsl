static const int SDF_INDEX = 6;

#include "D3D12SdfCommon.hlsli"
#include "D3D12SdfMath.hlsli"

struct SoulField
{
    float body;
    float riskSeam;
    float findingEdge;
    float distanceValue;
};

float soulMax3(float3 value)
{
    return max(value.x, max(value.y, value.z));
}

float soulMid3(float3 value)
{
    return value.x + value.y + value.z - min(value.x, min(value.y, value.z)) - soulMax3(value);
}

float soulBodyDistance(float3 local, float confidence, float risk)
{
    float scale = lerp(0.66, 0.74, confidence) - risk * 0.035;
    float bevel = lerp(0.030, 0.014, confidence) + risk * 0.006;
    float3 q = abs(local);
    float octahedron = (dot(q, float3(1.0, 1.0, 1.0)) - scale) * 0.57735027;
    float cap = lerp(0.46, 0.53, confidence) - risk * 0.018;
    float truncation = soulMax3(q - cap);
    return max(octahedron, truncation) - bevel;
}

SoulField soulField(float3 local, SdfObject sdfObject)
{
    float confidence = saturate(sdfObject.state.y);
    float risk = saturate(sdfObject.state.z * 1.55 + (1.0 - confidence) * 0.35);
    float body = soulBodyDistance(local, confidence, risk);
    float3 q = abs(local);

    float3 riskNormal = normalize(float3(0.24, 0.86, 0.45));
    float riskPlane = dot(local, riskNormal) + 0.018 * sin(timeSeconds * 0.51 + confidence * 2.0);
    float riskWidth = lerp(0.010, 0.036, risk);
    float riskSeam = abs(riskPlane) - riskWidth;

    float edgeAgreement = min(abs(q.x - q.y), min(abs(q.y - q.z), abs(q.z - q.x)));
    float edgeMask = smoothstep(0.02, 0.20, soulMax3(q) - soulMid3(q));
    float findingWidth = lerp(0.010, 0.020, confidence) * edgeMask;
    float findingEdge = edgeAgreement - findingWidth;

    SoulField field;
    field.body = body;
    field.riskSeam = riskSeam;
    field.findingEdge = findingEdge;
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

    float isRisk = field.riskSeam <= 0.0 ? 1.0 : 0.0;
    float isFinding = (1.0 - isRisk) * (field.findingEdge <= 0.0 ? 1.0 : 0.0);
    float isFacet = (1.0 - isRisk) * (1.0 - isFinding);

    float3 facetColor = lerp(float3(0.055, 0.070, 0.18), float3(0.26, 0.34, 0.72), confidence);
    float3 findingColor = lerp(float3(0.08, 0.22, 0.58), float3(0.58, 0.82, 1.0), confidence);
    float3 riskColor = lerp(float3(0.24, 0.015, 0.045), float3(0.86, 0.055, 0.06), risk);

    SdfSurface surface;
    surface.baseColor = facetColor * isFacet + findingColor * isFinding + riskColor * isRisk;
    surface.metallic = 0.0;
    surface.roughness = 0.055 * isFacet + 0.08 * isFinding + 0.12 * isRisk;
    surface.emission = findingColor * isFinding * (0.20 + confidence * 0.50)
        + riskColor * isRisk * (0.06 + risk * 0.36);
    return surface;
}

float3 soulCrystalShade(float3 p, float3 normal, int sdfIndex, SdfSurface surface)
{
    SdfObject sdfObject = sdfObjects[sdfIndex];
    float radius = max(sdfObject.centerRadius.w, 0.001);
    float3 local = (p - sdfObject.centerRadius.xyz) / radius;
    float confidence = saturate(sdfObject.state.y);
    float risk = saturate(sdfObject.state.z * 1.55 + (1.0 - confidence) * 0.35);

    float3 viewDirection = normalize(cameraPosition - p);
    float ndv = saturate(dot(normal, viewDirection));
    float3 reflected = reflect(-viewDirection, normal);
    float3 refracted = refract(-viewDirection, normal, 0.74);
    float refractionValid = dot(refracted, refracted) > 0.001 ? 1.0 : 0.0;
    float3 glassDirection = normalize(lerp(reflected, refracted, refractionValid));
    float3 glassTint = lerp(float3(0.30, 0.42, 0.82), float3(0.62, 0.76, 1.0), confidence);
    float3 pseudoRefraction = studioPmremSample(glassDirection, 3.2) * glassTint * 0.32;

    float centerGlow = pow(ndv, 1.25) * saturate(1.38 - length(local) * 0.88);
    float rimGlow = pow(1.0 - ndv, 3.0);
    float facetFlash = pow(saturate(abs(normal.x) + abs(normal.y) + abs(normal.z) - 1.18), 2.0);
    float pulse = 0.88 + 0.12 * sin(timeSeconds * 1.7 + confidence * 2.4);
    float3 oathLight = float3(1.0, 0.94, 0.78) * pulse * (0.26 + confidence * 0.58) * (centerGlow + rimGlow * 0.34);
    float3 edgeLight = float3(0.45, 0.68, 1.0) * (rimGlow * 0.36 + facetFlash * 0.18) * (0.6 + confidence * 0.5);
    float3 riskFaultLight = float3(1.0, 0.10, 0.055) * risk * pow(rimGlow, 0.65) * 0.14;

    return shadeSdfPbr(p, normal, surface) * 0.72
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
