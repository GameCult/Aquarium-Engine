static const int SDF_INDEX = 7;

#include "D3D12SdfCommon.hlsli"
#include "D3D12SdfMath.hlsli"

struct LifeDomain
{
    float body;
    float outerShell;
    float innerCavity;
    float rib;
    float seam;
    float innerBowl;
    float bead;
    float ember;
    float crack;
    float spiralLine;
    float aperture;
    float2 shellUv;
};

float lifeSpiralPhase(float2 uv)
{
    float r = max(length(uv), 0.035);
    float theta = atan2(uv.y, uv.x);
    return theta - 1.42 * log(r + 0.075);
}

float lifeSpiralLine(float2 uv, float phaseOffset)
{
    float r = max(length(uv), 0.035);
    float phase = lifeSpiralPhase(uv) + phaseOffset;
    return abs(sin(phase)) * r;
}

float lifeShellRadius(float theta, float pressure)
{
    float broad = 0.45 + 0.19 * cos(theta - 0.10);
    float lip = 0.10 * cos(theta * 2.0 + 0.62);
    float livingAsymmetry = 0.035 * sin(theta * 3.0 - 0.7);
    return max(0.22, (broad + lip + livingAsymmetry) * lerp(1.0, 0.92, pressure));
}

float lifeShellSurface(float3 local, float pressure, out float shellBoundary, out float sideBoundary, out float aperture)
{
    float2 uv = float2(local.x + 0.030, local.z - 0.005);
    float r = length(uv);
    float theta = atan2(uv.y, uv.x);
    float shellRadius = lifeShellRadius(theta, pressure);
    float cardioidBoundary = r - shellRadius;

    float3 ellipsoidCenter = float3(0.080, 0.000, -0.005);
    float3 ellipsoidRadius = float3(0.68, 0.37, 0.58) * (1.0 - pressure * 0.045);
    float ellipsoid = sdEllipsoid(local - ellipsoidCenter, ellipsoidRadius);
    float mantleBias = smoothstep(-0.02, 0.20, -cardioidBoundary) * 0.018;
    float outerBody = ellipsoid - mantleBias;
    shellBoundary = outerBody;
    sideBoundary = ellipsoid;

    float3 cavityCenter = float3(-0.365, -0.080, 0.065);
    float cavity = sdEllipsoid(local - cavityCenter, float3(0.34, 0.27, 0.41));
    float mouthBias = dot(normalize(float3(-0.90, -0.24, 0.13)), local - cavityCenter);
    aperture = max(cavity, -mouthBias - 0.19);
    return max(outerBody, -aperture);
}

float lifeRibDistance(float3 local, float shellBoundary, float sideBoundary, float pressure)
{
    float2 uv = float2(local.x + 0.030, local.z - 0.005);
    float r = max(length(uv), 0.035);
    float theta = atan2(uv.y, uv.x);
    float ribs = abs(sin((theta - 1.05 * log(r + 0.085)) * 9.0 + 0.35 * sin(theta * 2.0))) * r;
    float ribWidth = lerp(0.018, 0.027, pressure);
    float shellBand = abs(shellBoundary + 0.020) - 0.030;
    float sideMask = sideBoundary - 0.035;
    return max(max(shellBand, ribs - ribWidth), sideMask);
}

float lifeSeamDistance(float3 local, float shellBoundary, float sideBoundary, float pressure, out float spiralLine)
{
    float2 uv = float2(local.x + 0.030, local.z - 0.005);
    spiralLine = lifeSpiralLine(uv, -0.34);
    float seamWidth = lerp(0.016, 0.036, pressure);
    float surfaceBand = abs(shellBoundary + 0.010) - 0.020;
    float frontBand = abs(local.y + 0.192) - 0.035;
    float lip = max(max(surfaceBand, frontBand), spiralLine - seamWidth);
    float cavityLip = abs(length((local - float3(-0.360, -0.090, 0.065)).xz / float2(0.34, 0.43)) - 1.0) * 0.10;
    float apertureRim = max(abs(local.y + 0.165) - 0.026, cavityLip - 0.013);
    return min(lip, apertureRim);
}

float lifeBeadDistance(float3 local, float pressure)
{
    float bead = 10.0;

    [unroll]
    for (int i = 0; i < 15; i++)
    {
        float t = ((float)i + 0.25) / 14.5;
        float angle = 2.78 - t * 5.15;
        float rr = 0.075 * exp(t * 1.96);
        float2 uv = rr * float2(cos(angle), sin(angle));
        float side = -0.245 + 0.018 * sin(t * 6.2831853);
        float3 center = float3(uv.x - 0.030, side, uv.y + 0.005);
        float beadRadius = lerp(0.022, 0.050, smoothstep(0.20, 0.86, t));
        beadRadius *= 1.0 - pressure * 0.16;
        bead = min(bead, sdSphere(local - center, beadRadius));
    }

    return bead;
}

float lifeInnerBowlDistance(float3 local)
{
    float2 bowlCenter = float2(-0.300, 0.070);
    float2 q = (local.xz - bowlCenter) / float2(0.33, 0.42);
    float footprint = (length(q) - 1.0) * 0.33;
    float cup = saturate(1.0 - length(q));
    float targetY = -0.238 + cup * 0.070;
    float sheet = abs(local.y - targetY) - 0.020;
    float spiralGrain = lifeSpiralLine(local.xz - float2(-0.030, 0.005), 0.38) - 0.012;
    return max(max(footprint, sheet), min(spiralGrain, 0.018));
}

float lifeCrackLine(float3 local, float shellBoundary, float sideBoundary)
{
    float2 uv = float2(local.x + 0.030, local.z - 0.005);
    float r = max(length(uv), 0.035);
    float theta = atan2(uv.y, uv.x);
    float webA = abs(sin(theta * 13.0 + log(r + 0.075) * 18.0 + sin(theta * 4.0))) * r;
    float webB = abs(sin(theta * 19.0 - log(r + 0.110) * 11.0 + 1.7)) * r;
    float vein = min(webA, webB);
    float surfaceBand = abs(shellBoundary + 0.003) - 0.012;
    float sideMask = sideBoundary - 0.020;
    return max(max(surfaceBand, vein - 0.0045), sideMask);
}

LifeDomain lifeDomain(float3 local, SdfObject sdfObject)
{
    float pressure = saturate(sdfObject.state.z + (1.0 - sdfObject.state.y) * 0.18);

    float shellBoundary;
    float sideBoundary;
    float aperture;
    float shell = lifeShellSurface(local, pressure, shellBoundary, sideBoundary, aperture);
    float rib = lifeRibDistance(local, shellBoundary, sideBoundary, pressure);

    float spiralLine;
    float seam = lifeSeamDistance(local, shellBoundary, sideBoundary, pressure, spiralLine);
    float innerBowl = lifeInnerBowlDistance(local);
    float bead = lifeBeadDistance(local, pressure);
    float ember = sdSphere(local - float3(0.030, -0.178, 0.020), 0.072 + sdfObject.state.y * 0.012);
    float crack = lifeCrackLine(local, shellBoundary, sideBoundary);

    LifeDomain domain;
    domain.body = min(min(shell, rib), min(min(seam, innerBowl), min(bead, ember)));
    domain.outerShell = shell;
    domain.innerCavity = -aperture;
    domain.rib = rib;
    domain.seam = seam;
    domain.innerBowl = innerBowl;
    domain.bead = bead;
    domain.ember = ember;
    domain.crack = crack;
    domain.spiralLine = spiralLine;
    domain.aperture = aperture;
    domain.shellUv = float2(local.x + 0.030, local.z - 0.005);
    return domain;
}

float sdfDistance(float3 p, int sdfIndex)
{
    SdfObject sdfObject = sdfObjects[sdfIndex];
    float radius = max(sdfObject.centerRadius.w, 0.001);
    float3 local = (p - sdfObject.centerRadius.xyz) / radius;
    return lifeDomain(local, sdfObject).body * radius;
}

SdfSurface sdfSurface(float3 p, int sdfIndex)
{
    SdfObject sdfObject = sdfObjects[sdfIndex];
    float radius = max(sdfObject.centerRadius.w, 0.001);
    float3 local = (p - sdfObject.centerRadius.xyz) / radius;
    LifeDomain domain = lifeDomain(local, sdfObject);
    float activity = saturate(sdfObject.state.x);
    float heartbeat = saturate(sdfObject.state.y);
    float pressure = saturate(sdfObject.state.z + (1.0 - heartbeat) * 0.18);

    float shellDetail = min(min(domain.outerShell, domain.rib), min(domain.seam, domain.innerBowl));
    float isEmber = domain.ember <= min(shellDetail, domain.bead) ? 1.0 : 0.0;
    float isBead = (1.0 - isEmber) * (domain.bead <= shellDetail ? 1.0 : 0.0);
    float isBowl = (1.0 - isEmber) * (1.0 - isBead) * (domain.innerBowl <= min(min(domain.outerShell, domain.rib), domain.seam) ? 1.0 : 0.0);
    float isSeam = (1.0 - isEmber) * (1.0 - isBead) * (1.0 - isBowl) * (domain.seam <= min(domain.outerShell, domain.rib) ? 1.0 : 0.0);
    float isRib = (1.0 - isEmber) * (1.0 - isBead) * (1.0 - isBowl) * (1.0 - isSeam) * (domain.rib <= domain.outerShell ? 1.0 : 0.0);
    float isShell = (1.0 - isEmber) * (1.0 - isBead) * (1.0 - isBowl) * (1.0 - isSeam) * (1.0 - isRib);

    float inner = saturate(isBowl + isShell * (domain.innerCavity >= domain.outerShell ? 1.0 : 0.0));
    float crackMask = isShell * (1.0 - inner) * (1.0 - smoothstep(0.000, 0.010, domain.crack));
    float nacre = 0.5 + 0.5 * sin(lifeSpiralPhase(domain.shellUv) * 7.0 + local.y * 16.0 + timeSeconds * 0.18);

    float3 teal = lerp(float3(0.026, 0.34, 0.38), float3(0.10, 0.62, 0.66), nacre);
    float3 innerGold = lerp(float3(0.92, 0.47, 0.10), float3(1.0, 0.78, 0.28), nacre);
    float3 ribGold = float3(1.0, 0.86, 0.58);
    float3 beadPearl = lerp(float3(0.86, 0.78, 0.62), float3(1.0, 0.96, 0.82), nacre);
    float3 ember = float3(1.0, 0.48, 0.08);
    float3 crackGold = float3(0.95, 0.56, 0.16);

    SdfSurface surface;
    surface.baseColor = teal * isShell * (1.0 - inner)
        + innerGold * inner
        + innerGold * isBowl
        + ribGold * isRib
        + ribGold * isSeam
        + beadPearl * isBead
        + ember * isEmber;
    surface.baseColor = lerp(surface.baseColor, crackGold, crackMask * 0.70);
    surface.metallic = 0.0 * isShell + 0.42 * isRib + 0.28 * isSeam + 0.0 * (isBowl + isBead + isEmber);
    surface.roughness = 0.24 * isShell + 0.18 * inner + 0.13 * isRib + 0.11 * isSeam + 0.10 * isBead + 0.30 * isEmber;

    float3 lifeLight = primitiveEmissionRadiance(sdfFieldId(sdfIndex));
    surface.emission = lifeLight * 0.03
        + teal * isShell * (1.0 - inner) * 0.030
        + innerGold * inner * (0.34 + heartbeat * 0.36)
        + ribGold * isRib * 0.070
        + ribGold * isSeam * (0.34 + activity * 0.22)
        + beadPearl * isBead * (0.24 + heartbeat * 0.20)
        + ember * isEmber * (2.4 + heartbeat * 1.1)
        + crackGold * crackMask * (0.06 + pressure * 0.18);
    return surface;
}

float3 shadeSdf(float2 uv, float travel, float3 p, float3 normal, int sdfIndex, SdfSurface surface)
{
    SdfObject sdfObject = sdfObjects[sdfIndex];
    float radius = max(sdfObject.centerRadius.w, 0.001);
    float3 local = (p - sdfObject.centerRadius.xyz) / radius;
    LifeDomain domain = lifeDomain(local, sdfObject);
    float3 pbr = shadeSdfPbr(p, normal, surface);

    float3 viewDirection = normalize(cameraPosition - p);
    float edge = pow(1.0 - saturate(dot(normal, viewDirection)), 3.4);
    float warmCore = exp(-dot(local - float3(0.030, -0.178, 0.020), local - float3(0.030, -0.178, 0.020)) * 9.0);
    float seamGlow = 1.0 - smoothstep(0.0, 0.030, min(domain.seam, domain.spiralLine));
    float shellOnly = domain.outerShell <= min(min(domain.rib, domain.seam), min(domain.bead, domain.ember)) ? 1.0 : 0.0;

    return pbr
        + float3(1.0, 0.50, 0.10) * warmCore * 0.42
        + float3(0.90, 0.72, 0.42) * seamGlow * 0.08
        + float3(0.16, 0.74, 0.78) * edge * shellOnly * 0.18;
}

#define SDF_TRACE_STEP_SCALE 0.16
#define SDF_TRACE_MAX_STEP_RADIUS_SCALE 0.008

#include "D3D12SdfProxy.hlsli"
