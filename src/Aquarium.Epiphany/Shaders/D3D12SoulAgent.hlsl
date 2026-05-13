static const int SDF_INDEX = 6;

#include "D3D12SdfCommon.hlsli"
#include "D3D12SdfMath.hlsli"

struct SoulDomain
{
    float3 local;
    float3 octant;
    float3 bary;
    float scale;
    float baseBody;
    float body;
    float outerEdge;
    float panelEdge;
    float findingEdge;
    float riskEdge;
    float fracture;
    float chamber;
    float frost;
};

float soulMin3(float3 value)
{
    return min(value.x, min(value.y, value.z));
}

float soulMedian3(float3 value)
{
    return value.x + value.y + value.z - soulMin3(value) - max(value.x, max(value.y, value.z));
}

float soulVLine(float distanceToLine, float width)
{
    return saturate(1.0 - distanceToLine / max(width, 0.0001));
}

float3 soulFacetNormal(float3 local)
{
    float3 side = float3(local.x < 0.0 ? -1.0 : 1.0, local.y < 0.0 ? -1.0 : 1.0, local.z < 0.0 ? -1.0 : 1.0);
    return normalize(side);
}

SoulDomain soulDomain(float3 local, SdfObject sdfObject)
{
    float confidence = saturate(sdfObject.state.y);
    float risk = saturate(sdfObject.state.z * 1.55 + (1.0 - confidence) * 0.35);
    float3 octant = abs(local);
    float scale = lerp(0.70, 0.78, confidence) - risk * 0.030;
    float bevel = lerp(0.040, 0.018, confidence) + risk * 0.006;
    float baseBody = (dot(octant, float3(1.0, 1.0, 1.0)) - scale) * 0.57735027 - bevel;
    float invSum = rcp(max(dot(octant, float3(1.0, 1.0, 1.0)), 0.001));
    float3 bary = octant * invSum;

    float outerEdge = soulMin3(bary);
    float panelEdge = min(abs(bary.x - bary.y), min(abs(bary.y - bary.z), abs(bary.z - bary.x)));
    float findingSelector = abs(dot(local, normalize(float3(-0.38, 0.72, 0.58)))) * 0.22;
    float findingEdge = max(abs(bary.x - bary.z) * 0.58, findingSelector - (0.040 + confidence * 0.040));

    float riskSelector = abs(dot(local, normalize(float3(0.22, 0.93, -0.30)))) * 0.24;
    float riskEdge = max(min(outerEdge, abs(bary.y - bary.z) * 0.48), riskSelector - (0.045 + risk * 0.070));
    float fracture = max(min(abs(bary.x - 0.18), abs(bary.y - bary.z) * 0.42), riskSelector - (0.030 + risk * 0.035));

    float surfaceBand = 1.0 - smoothstep(0.014, 0.130, abs(baseBody));
    float equatorLift = 1.0 - smoothstep(0.12, 0.58, abs(local.y));
    float panelLift = smoothstep(0.035, 0.150, outerEdge);
    float findingCut = soulVLine(findingEdge, lerp(0.006, 0.011, confidence));
    float riskCut = soulVLine(riskEdge, lerp(0.010, 0.024, risk)) * risk;
    float fractureCut = soulVLine(fracture, lerp(0.006, 0.014, risk)) * risk;
    float lift = (0.002 + confidence * 0.004 + risk * 0.003) * equatorLift * panelLift;
    float cut = findingCut * (0.006 + confidence * 0.004)
        + riskCut * (0.014 + risk * 0.018)
        + fractureCut * (0.010 + risk * 0.014);
    float relief = surfaceBand * (lift - cut);

    SoulDomain domain;
    domain.local = local;
    domain.octant = octant;
    domain.bary = bary;
    domain.scale = scale;
    domain.baseBody = baseBody;
    domain.body = baseBody - relief;
    domain.outerEdge = outerEdge;
    domain.panelEdge = panelEdge;
    domain.findingEdge = findingEdge;
    domain.riskEdge = riskEdge;
    domain.fracture = fracture;
    domain.chamber = saturate(1.0 - length(local) * 1.16);
    domain.frost = sin(bary.x * 63.0 + local.z * 13.0) * sin(bary.y * 47.0 - local.x * 11.0);
    return domain;
}

float sdfDistance(float3 p, int sdfIndex)
{
    SdfObject sdfObject = sdfObjects[sdfIndex];
    float radius = max(sdfObject.centerRadius.w, 0.001);
    float3 local = (p - sdfObject.centerRadius.xyz) / radius;
    return soulDomain(local, sdfObject).body * radius;
}

SdfSurface sdfSurface(float3 p, int sdfIndex)
{
    SdfObject sdfObject = sdfObjects[sdfIndex];
    float radius = max(sdfObject.centerRadius.w, 0.001);
    float3 local = (p - sdfObject.centerRadius.xyz) / radius;
    SoulDomain domain = soulDomain(local, sdfObject);
    float confidence = saturate(sdfObject.state.y);
    float risk = saturate(sdfObject.state.z * 1.55 + (1.0 - confidence) * 0.35);

    float riskMask = soulVLine(domain.riskEdge, 0.018) * risk;
    float fractureMask = (1.0 - riskMask) * soulVLine(domain.fracture, 0.010) * risk;
    float findingMask = (1.0 - riskMask) * (1.0 - fractureMask) * soulVLine(domain.findingEdge, 0.010);
    float edgeMask = (1.0 - riskMask) * soulVLine(domain.outerEdge, 0.020);
    float facetMask = saturate(1.0 - riskMask - fractureMask - findingMask - edgeMask);

    float frost = lerp(0.82, 1.14, 0.5 + 0.5 * domain.frost);
    float3 facetColor = lerp(float3(0.025, 0.038, 0.072), float3(0.22, 0.34, 0.56), confidence) * frost;
    float3 edgeColor = lerp(float3(0.16, 0.38, 0.70), float3(0.56, 0.84, 1.0), confidence);
    float3 findingColor = lerp(float3(0.08, 0.24, 0.62), float3(0.52, 0.82, 1.0), confidence);
    float3 riskColor = lerp(float3(0.28, 0.018, 0.030), float3(1.0, 0.055, 0.045), risk);

    SdfSurface surface;
    surface.baseColor = facetColor * facetMask + edgeColor * edgeMask + findingColor * findingMask + riskColor * saturate(riskMask + fractureMask);
    surface.metallic = 0.0;
    surface.roughness = 0.070 * facetMask + 0.052 * edgeMask + 0.065 * findingMask + 0.120 * saturate(riskMask + fractureMask);
    surface.emission = edgeColor * edgeMask * (0.10 + confidence * 0.26)
        + findingColor * findingMask * (0.24 + confidence * 0.56)
        + riskColor * riskMask * (0.30 + risk * 0.90)
        + riskColor * fractureMask * (0.18 + risk * 0.62);
    return surface;
}

float3 soulCrystalShade(float3 p, float3 normal, int sdfIndex, SdfSurface surface)
{
    SdfObject sdfObject = sdfObjects[sdfIndex];
    float radius = max(sdfObject.centerRadius.w, 0.001);
    float3 local = (p - sdfObject.centerRadius.xyz) / radius;
    float confidence = saturate(sdfObject.state.y);
    float risk = saturate(sdfObject.state.z * 1.55 + (1.0 - confidence) * 0.35);
    SoulDomain domain = soulDomain(local, sdfObject);
    float seamInfluence = saturate(
        soulVLine(domain.outerEdge, 0.026)
        + soulVLine(domain.findingEdge, 0.014)
        + soulVLine(domain.riskEdge, 0.018) * risk
        + soulVLine(domain.fracture, 0.010) * risk);
    float3 facetNormal = soulFacetNormal(local);
    float3 shadeNormal = normalize(lerp(facetNormal, normal, 0.10 + seamInfluence * 0.22));

    float3 viewDirection = normalize(cameraPosition - p);
    float ndv = saturate(dot(shadeNormal, viewDirection));
    float3 reflected = reflect(-viewDirection, shadeNormal);
    float3 refracted = refract(-viewDirection, shadeNormal, 0.67);
    float refractionValid = dot(refracted, refracted) > 0.001 ? 1.0 : 0.0;
    float3 glassDirection = normalize(lerp(reflected, refracted, refractionValid));
    float3 envRefract = studioPmremSample(glassDirection, 0.24);
    float3 envReflect = studioPmremSample(reflected, 0.56);

    float fresnel = pow(1.0 - ndv, 4.8);
    float thickness = saturate((domain.scale - dot(domain.octant, float3(0.45, 0.45, 0.45))) * 1.8);
    float3 glassTint = lerp(float3(0.34, 0.50, 0.84), float3(0.78, 0.90, 1.0), confidence);
    float3 transmission = envRefract * glassTint * (0.18 + thickness * 0.42);
    float3 reflection = envReflect * (0.22 + fresnel * 0.72);

    float innerCore = exp(-dot(local, local) * 5.8);
    float chamberGlow = domain.chamber * domain.chamber * (0.35 + confidence * 0.85);
    float edgeLine = soulVLine(domain.outerEdge, 0.028);
    float findingLine = soulVLine(domain.findingEdge, 0.011);
    float riskLine = soulVLine(domain.riskEdge, 0.018) * risk;
    float fractureLine = soulVLine(domain.fracture, 0.010) * risk;
    float paneFlash = soulVLine(domain.panelEdge, 0.006) * (0.25 + 0.25 * sin(domain.bary.x * 31.0 + domain.bary.y * 19.0 + timeSeconds * 0.17));

    float3 oathLight = float3(1.0, 0.96, 0.82) * (innerCore * (1.9 + confidence * 1.7) + chamberGlow * 0.42);
    float3 blueCaustic = float3(0.36, 0.70, 1.0) * (edgeLine * 0.58 + findingLine * 0.72 + paneFlash * 0.22 + fresnel * 0.36) * (0.75 + confidence * 0.9);
    float3 redFault = float3(1.0, 0.075, 0.045) * (riskLine * 1.10 + fractureLine * 0.84);

    return shadeSdfPbr(p, shadeNormal, surface) * 0.18
        + transmission
        + reflection
        + oathLight
        + blueCaustic
        + redFault;
}

float3 shadeSdf(float2 uv, float travel, float3 p, float3 normal, int sdfIndex, SdfSurface surface)
{
    return soulCrystalShade(p, normal, sdfIndex, surface);
}

#define SDF_TRACE_STEP_SCALE 0.16
#define SDF_TRACE_MAX_STEP_RADIUS_SCALE 0.009

#include "D3D12SdfProxy.hlsli"
