static const int SDF_INDEX = 7;

#include "D3D12SdfCommon.hlsli"
#include "D3D12SdfMath.hlsli"

struct LifeDomain
{
    float body;
    float shell;
    float aperture;
    float ember;
    float seam;
    float bead;
    float crack;
    float lip;
    float spiral;
    float spiralStep;
    float surfaceBand;
    float pressure;
};

float2 lifeRotate2(float2 p, float angle)
{
    float c = cos(angle);
    float s = sin(angle);
    return float2(c * p.x - s * p.y, s * p.x + c * p.y);
}

float lifeHash(float n)
{
    return frac(sin(n * 127.1) * 43758.5453);
}

float lifeSeedShell(float3 p, float pressure, out float surfaceBand)
{
    float2 q = p.xz + float2(0.020, -0.020);
    float radius = max(length(q), 0.035);
    float theta = atan2(q.y, q.x);
    const float growth = 0.165;
    const float startRadius = 0.080;

    float n = (log(radius / startRadius) / growth - theta) / (2.0 * PI);
    n = clamp(n, 0.0, 3.15);
    float n0 = floor(n);
    float n1 = min(n0 + 1.0, 3.15);
    float u0 = theta + 2.0 * PI * n0;
    float u1 = theta + 2.0 * PI * n1;
    float r0 = startRadius * exp(growth * u0);
    float r1 = startRadius * exp(growth * u1);
    float tube0 = 0.085 + 0.060 * n0 + pressure * 0.020;
    float tube1 = 0.085 + 0.060 * n1 + pressure * 0.020;
    float d0 = length(float2((radius - r0) * 0.92, p.y * 0.96)) - tube0;
    float d1 = length(float2((radius - r1) * 0.92, p.y * 0.96)) - tube1;
    float shell = min(d0, d1);

    float2 outerQ = (p.xz - float2(0.060, -0.010)) / float2(0.94, 0.80);
    float outerBound = length(outerQ) - 1.0;
    float innerVoid = 0.118 - length(float2(q.x * 1.05, q.y * 0.95));
    float breathe = 0.010 * sin(timeSeconds * 1.2 + u0 * 0.35) * (1.0 - pressure * 0.45);

    surfaceBand = abs(shell) - 0.030;
    return max(max(shell + breathe, outerBound), innerVoid);
}

float lifeSpiralField(float2 p, out float nearestTurn, out float stepAlong)
{
    float2 q = p + float2(0.105, -0.020);
    float radius = max(length(q), 0.035);
    float theta = atan2(q.y, q.x);
    const float growth = 0.205;
    const float startRadius = 0.055;

    float turn = (log(radius / startRadius) / growth - theta) / (2.0 * PI);
    nearestTurn = clamp(round(turn), 0.0, 3.0);
    float targetRadius = startRadius * exp(growth * (theta + 2.0 * PI * nearestTurn));
    float radialError = radius - targetRadius;
    stepAlong = theta + 2.0 * PI * nearestTurn;
    return abs(radialError);
}

float lifeAperture(float3 p, float pressure)
{
    float3 q = p - float3(-0.325 + pressure * 0.060, -0.040, 0.045);
    q.xz = lifeRotate2(q.xz, -0.18);
    float bowl = sdEllipsoid(q, float3(0.325 - pressure * 0.040, 0.320, 0.255 - pressure * 0.020));
    float mouthGate = p.x + 0.090 + pressure * 0.070;
    return max(bowl, mouthGate);
}

LifeDomain lifeDomain(float3 local, SdfObject sdfObject)
{
    float pressure = saturate(sdfObject.state.z + (1.0 - saturate(sdfObject.state.y)) * 0.12);
    float3 p = local;
    p.xz = lifeRotate2(p.xz, 0.10);

    float surfaceBand;
    float shell = lifeSeedShell(p, pressure, surfaceBand);
    float aperture = lifeAperture(p, pressure);
    float openedShell = max(shell, -aperture);

    float turn;
    float stepAlong;
    float spiralLine = lifeSpiralField(p.xz, turn, stepAlong);
    float spiralGate = smoothstep(0.035, 0.22, p.x + 0.44) * (1.0 - smoothstep(0.70, 0.92, length(p.xz)));
    float seam = max(spiralLine - lerp(0.011, 0.019, pressure), surfaceBand);
    float darkGroove = (1.0 - smoothstep(0.0, 0.018, seam)) * spiralGate;

    float beadPeriod = sin(stepAlong * 7.2 + 0.8 * sin(stepAlong * 0.7));
    float beadWindow = smoothstep(0.36, 0.72, p.x + 0.40) * smoothstep(0.84, 0.28, length(p.xz));
    float bead = max(max(spiralLine - 0.018, abs(beadPeriod) - 0.34), surfaceBand - 0.014);
    float beadMask = (1.0 - smoothstep(0.0, 0.012, bead)) * beadWindow * spiralGate;

    float ribPhase = abs(sin(stepAlong * 2.35 + turn * 0.8));
    float rib = max(spiralLine - 0.024, surfaceBand - 0.016);
    float ribLift = (1.0 - smoothstep(0.0, 0.016, rib)) * spiralGate * ribPhase * 0.024;

    float3 emberP = p - float3(-0.245 + pressure * 0.040, -0.050, 0.030);
    float emberPulse = 0.012 * sin(timeSeconds * 2.1 + pressure * 1.7);
    float ember = sdSphere(emberP, 0.090 + emberPulse);

    float lip = max(abs(aperture) - 0.030, abs(shell) - 0.045);
    float lipLift = (1.0 - smoothstep(0.0, 0.024, lip)) * 0.036;
    float beadLift = beadMask * 0.034;
    float grooveCut = darkGroove * lerp(0.026, 0.050, pressure);

    float crackSeed = sin(stepAlong * 5.7 + sin(p.y * 8.0) * 0.7) * sin((p.z - p.x) * 16.0);
    float crack = max(abs(crackSeed) - 0.955, surfaceBand - 0.004);

    LifeDomain domain;
    domain.body = min(openedShell - ribLift - lipLift - beadLift + grooveCut, ember);
    domain.shell = openedShell;
    domain.aperture = aperture;
    domain.ember = ember;
    domain.seam = seam;
    domain.bead = bead;
    domain.crack = crack;
    domain.lip = lip;
    domain.spiral = spiralLine;
    domain.spiralStep = stepAlong;
    domain.surfaceBand = surfaceBand;
    domain.pressure = pressure;
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
    float heartbeat = saturate(sdfObject.state.y);

    float ember = 1.0 - smoothstep(0.000, 0.018, domain.ember);
    float lip = (1.0 - ember) * (1.0 - smoothstep(0.000, 0.018, domain.lip));
    float bead = (1.0 - ember) * (1.0 - lip * 0.40) * (1.0 - smoothstep(0.000, 0.011, domain.bead));
    float seam = (1.0 - ember) * (1.0 - smoothstep(0.000, 0.016, domain.seam));
    float crack = (1.0 - ember) * (1.0 - smoothstep(0.000, 0.006, domain.crack));
    float chamber = (1.0 - ember) * smoothstep(0.025, -0.045, domain.aperture) * smoothstep(0.16, -0.04, local.x + 0.12);

    float nacre = 0.5 + 0.5 * sin(domain.spiralStep * 1.7 + local.y * 5.0 + timeSeconds * 0.08);
    float3 teal = lerp(float3(0.022, 0.38, 0.38), float3(0.050, 0.72, 0.66), nacre);
    float3 gold = lerp(float3(0.88, 0.42, 0.10), float3(1.0, 0.78, 0.25), nacre);
    float3 pearl = float3(0.96, 0.88, 0.68);
    float3 scar = float3(0.035, 0.020, 0.015);
    float3 emberColor = float3(1.0, 0.38, 0.055);

    SdfSurface surface;
    surface.baseColor = lerp(teal, gold, chamber * 0.82);
    surface.baseColor = lerp(surface.baseColor, pearl, saturate(lip * 0.55 + bead * 1.25));
    float scarMask = saturate(seam * (1.0 - bead) * (0.45 + domain.pressure * 0.35) + crack * 0.50);
    surface.baseColor = lerp(surface.baseColor, scar, scarMask);
    surface.baseColor = lerp(surface.baseColor, emberColor, ember);
    surface.metallic = 0.06 + 0.22 * saturate(lip + bead);
    surface.roughness = lerp(0.22, 0.11, saturate(lip + bead + ember * 0.5));
    surface.emission = primitiveEmissionRadiance(sdfFieldId(sdfIndex)) * 0.020
        + teal * 0.070
        + gold * chamber * (0.45 + heartbeat * 0.28)
        + pearl * bead * 0.22
        + emberColor * ember * (2.4 + heartbeat * 1.1 + domain.pressure * 0.8)
        + gold * crack * 0.10;
    return surface;
}

float3 shadeSdf(float2 uv, float travel, float3 p, float3 normal, int sdfIndex, SdfSurface surface)
{
    SdfObject sdfObject = sdfObjects[sdfIndex];
    float radius = max(sdfObject.centerRadius.w, 0.001);
    float3 local = (p - sdfObject.centerRadius.xyz) / radius;
    LifeDomain domain = lifeDomain(local, sdfObject);
    float3 shaded = shadeSdfPbr(p, normal, surface);

    float3 viewDirection = normalize(cameraPosition - p);
    float edge = pow(1.0 - saturate(dot(normal, viewDirection)), 3.0);
    float apertureGlow = smoothstep(0.10, -0.08, domain.aperture) * smoothstep(0.20, -0.08, local.x + 0.18);
    return shaded
        + float3(0.08, 0.48, 0.44) * edge * 0.10
        + float3(1.0, 0.44, 0.06) * apertureGlow * 0.28;
}

#define SDF_TRACE_STEP_SCALE 0.12
#define SDF_TRACE_MAX_STEP_RADIUS_SCALE 0.006

#include "D3D12SdfProxy.hlsli"
