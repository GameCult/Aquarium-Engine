static const int SDF_INDEX = 6;

#include "D3D12SdfCommon.hlsli"
#include "D3D12SdfMath.hlsli"

struct SoulDomain
{
    float3 local;
    float3 facetNormal;
    float scale;
    float baseBody;
    float body;
    float edge;
    float finding;
    float risk;
    float fracture;
    float chamber;
    float frost;
};

float soulMin3(float3 value)
{
    return min(value.x, min(value.y, value.z));
}

float soulLine(float distanceToLine, float width)
{
    return saturate(1.0 - distanceToLine / max(width, 0.0001));
}

float soulEdgeDistance(float firstPlane, float secondPlane, float dominantPlane)
{
    return max(abs(firstPlane - secondPlane), dominantPlane - max(firstPlane, secondPlane));
}

void soulSelectPlane(inout float dominantPlane, inout float3 facetNormal, float candidatePlane, float3 candidateNormal)
{
    if (candidatePlane > dominantPlane)
    {
        dominantPlane = candidatePlane;
        facetNormal = candidateNormal;
    }
}

SoulDomain soulDomain(float3 local, SdfObject sdfObject)
{
    float confidence = saturate(sdfObject.state.y);
    float risk = saturate(sdfObject.state.z * 1.55 + (1.0 - confidence) * 0.35);
    float scale = lerp(0.405, 0.455, confidence) - risk * 0.014;
    float bevel = lerp(0.018, 0.006, confidence) + risk * 0.004;

    float3 n0 = normalize(float3(1.00, 0.92, 1.08));
    float3 n1 = normalize(float3(-0.96, 1.04, 0.98));
    float3 n2 = normalize(float3(1.08, -0.88, 0.96));
    float3 n3 = normalize(float3(0.94, 1.10, -0.90));
    float3 n4 = normalize(float3(-1.02, -0.94, 1.04));
    float3 n5 = normalize(float3(-0.90, 1.02, -1.08));
    float3 n6 = normalize(float3(1.06, -1.00, -0.92));
    float3 n7 = normalize(float3(-1.00, -0.96, -1.02));

    float p0 = dot(local, n0) - scale * 1.00;
    float p1 = dot(local, n1) - scale * 0.99;
    float p2 = dot(local, n2) - scale * 1.01;
    float p3 = dot(local, n3) - scale * 0.98;
    float p4 = dot(local, n4) - scale * 1.02;
    float p5 = dot(local, n5) - scale * 1.00;
    float p6 = dot(local, n6) - scale * 0.99;
    float p7 = dot(local, n7) - scale * 1.01;

    float dominant = p0;
    float3 facetNormal = n0;
    soulSelectPlane(dominant, facetNormal, p1, n1);
    soulSelectPlane(dominant, facetNormal, p2, n2);
    soulSelectPlane(dominant, facetNormal, p3, n3);
    soulSelectPlane(dominant, facetNormal, p4, n4);
    soulSelectPlane(dominant, facetNormal, p5, n5);
    soulSelectPlane(dominant, facetNormal, p6, n6);
    soulSelectPlane(dominant, facetNormal, p7, n7);

    float e01 = soulEdgeDistance(p0, p1, dominant);
    float e02 = soulEdgeDistance(p0, p2, dominant);
    float e03 = soulEdgeDistance(p0, p3, dominant);
    float e14 = soulEdgeDistance(p1, p4, dominant);
    float e15 = soulEdgeDistance(p1, p5, dominant);
    float e24 = soulEdgeDistance(p2, p4, dominant);
    float e26 = soulEdgeDistance(p2, p6, dominant);
    float e35 = soulEdgeDistance(p3, p5, dominant);
    float e36 = soulEdgeDistance(p3, p6, dominant);
    float e47 = soulEdgeDistance(p4, p7, dominant);
    float e57 = soulEdgeDistance(p5, p7, dominant);
    float e67 = soulEdgeDistance(p6, p7, dominant);
    float edge = min(min(min(e01, e02), min(e03, e14)), min(min(e15, e24), min(min(e26, e35), min(min(e36, e47), min(e57, e67)))));

    float finding = min(min(e02, e35), e67);
    float riskGate = abs(dot(local, normalize(float3(0.18, 0.96, -0.24)))) * 0.16 - (0.030 + risk * 0.050);
    float riskEdge = min(min(e03, e47), e15);
    float riskSeam = max(riskEdge, riskGate);
    float fracture = max(min(e24, e57), abs(dot(local, normalize(float3(-0.72, 0.36, 0.60)))) * 0.13 - (0.022 + risk * 0.030));

    float surfaceBand = 1.0 - smoothstep(0.010, 0.090, abs(dominant - bevel));
    float faceInterior = smoothstep(0.030, 0.115, edge);
    float equatorLift = 1.0 - smoothstep(0.10, 0.58, abs(local.y));
    float edgeGroove = soulLine(edge, 0.010) * 0.004;
    float findingGroove = soulLine(finding, lerp(0.005, 0.010, confidence)) * (0.010 + confidence * 0.006);
    float riskGroove = soulLine(riskSeam, lerp(0.007, 0.018, risk)) * risk * (0.018 + risk * 0.020);
    float fractureGroove = soulLine(fracture, lerp(0.004, 0.010, risk)) * risk * (0.012 + risk * 0.014);
    float faceLift = (0.006 + confidence * 0.007 + risk * 0.004) * equatorLift * faceInterior;
    float relief = surfaceBand * (faceLift - edgeGroove - findingGroove - riskGroove - fractureGroove);
    float baseBody = dominant - bevel;

    float3 octant = abs(local);
    float3 bary = octant / max(dot(octant, float3(1.0, 1.0, 1.0)), 0.001);

    SoulDomain domain;
    domain.local = local;
    domain.facetNormal = facetNormal;
    domain.scale = scale;
    domain.baseBody = baseBody;
    domain.body = baseBody - relief;
    domain.edge = edge;
    domain.finding = finding;
    domain.risk = riskSeam;
    domain.fracture = fracture;
    domain.chamber = saturate(1.0 - length(local) * 1.18);
    domain.frost = sin(bary.x * 53.0 + local.z * 9.0) * sin(bary.y * 41.0 - local.x * 7.0);
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

    float riskMask = soulLine(domain.risk, 0.014) * risk;
    float fractureMask = (1.0 - riskMask) * soulLine(domain.fracture, 0.008) * risk;
    float findingMask = (1.0 - riskMask) * (1.0 - fractureMask) * soulLine(domain.finding, 0.008);
    float edgeMask = (1.0 - riskMask) * soulLine(domain.edge, 0.010);
    float facetMask = saturate(1.0 - riskMask - fractureMask - findingMask - edgeMask);

    float frost = lerp(0.84, 1.12, 0.5 + 0.5 * domain.frost);
    float3 facetColor = lerp(float3(0.020, 0.032, 0.064), float3(0.20, 0.32, 0.56), confidence) * frost;
    float3 edgeColor = lerp(float3(0.14, 0.34, 0.70), float3(0.56, 0.86, 1.0), confidence);
    float3 findingColor = lerp(float3(0.06, 0.22, 0.62), float3(0.46, 0.80, 1.0), confidence);
    float3 riskColor = lerp(float3(0.26, 0.012, 0.026), float3(1.0, 0.050, 0.040), risk);

    SdfSurface surface;
    surface.baseColor = facetColor * facetMask + edgeColor * edgeMask + findingColor * findingMask + riskColor * saturate(riskMask + fractureMask);
    surface.metallic = 0.0;
    surface.roughness = 0.055 * facetMask + 0.045 * edgeMask + 0.060 * findingMask + 0.105 * saturate(riskMask + fractureMask);
    surface.emission = edgeColor * edgeMask * (0.08 + confidence * 0.18)
        + findingColor * findingMask * (0.24 + confidence * 0.50)
        + riskColor * riskMask * (0.36 + risk * 0.95)
        + riskColor * fractureMask * (0.18 + risk * 0.58);
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

    float cutInfluence = saturate(soulLine(domain.edge, 0.014) + soulLine(domain.finding, 0.010) + soulLine(domain.risk, 0.014) * risk + soulLine(domain.fracture, 0.008) * risk);
    float3 shadeNormal = normalize(lerp(domain.facetNormal, normal, cutInfluence * 0.18));
    float3 viewDirection = normalize(cameraPosition - p);
    float ndv = saturate(dot(shadeNormal, viewDirection));
    float3 reflected = reflect(-viewDirection, shadeNormal);
    float3 refracted = refract(-viewDirection, shadeNormal, 0.67);
    float refractionValid = dot(refracted, refracted) > 0.001 ? 1.0 : 0.0;
    float3 glassDirection = normalize(lerp(reflected, refracted, refractionValid));
    float3 envRefract = studioPmremSample(glassDirection, 0.22);
    float3 envReflect = studioPmremSample(reflected, 0.46);

    float fresnel = pow(1.0 - ndv, 4.6);
    float thickness = saturate((domain.scale - length(local) * 0.42) * 1.9);
    float3 glassTint = lerp(float3(0.30, 0.46, 0.82), float3(0.76, 0.90, 1.0), confidence);
    float3 transmission = envRefract * glassTint * (0.16 + thickness * 0.38);
    float3 reflection = envReflect * (0.24 + fresnel * 0.72);

    float innerCore = exp(-dot(local, local) * 5.6);
    float chamberGlow = domain.chamber * domain.chamber * (0.35 + confidence * 0.82);
    float edgeLine = soulLine(domain.edge, 0.016);
    float findingLine = soulLine(domain.finding, 0.010);
    float riskLine = soulLine(domain.risk, 0.014) * risk;
    float fractureLine = soulLine(domain.fracture, 0.008) * risk;

    float3 oathLight = float3(1.0, 0.96, 0.82) * (innerCore * (1.9 + confidence * 1.7) + chamberGlow * 0.36);
    float3 blueCaustic = float3(0.34, 0.70, 1.0) * (edgeLine * 0.42 + findingLine * 0.88 + fresnel * 0.36) * (0.76 + confidence * 0.86);
    float3 redFault = float3(1.0, 0.070, 0.045) * (riskLine * 1.20 + fractureLine * 0.82);

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
