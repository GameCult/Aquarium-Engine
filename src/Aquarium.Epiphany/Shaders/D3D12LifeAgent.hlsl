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
    float rib;
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
    float2 q = p.xz - float2(0.105, -0.015);
    float angle = atan2(q.y, q.x);
    float directionalRadius = 0.66
        + 0.16 * cos(angle + 0.05)
        + 0.06 * cos(2.0 * angle + 0.35)
        - 0.035 * cos(3.0 * angle - 0.40);
    float thickness = 0.330 + 0.075 * smoothstep(-0.55, 0.45, p.x) + 0.030 * cos(angle + 0.20);
    float3 normalized = float3(q.x / max(directionalRadius * 1.12, 0.001),
        p.y / max(thickness, 0.001),
        q.y / max(directionalRadius * 0.93, 0.001));
    float shell = (length(normalized) - 1.0) * min(thickness, directionalRadius * 0.93);
    float dome = sdEllipsoid(p - float3(0.135, 0.000, 0.015), float3(0.72, 0.43, 0.60));
    shell = smoothUnion(shell, dome + 0.020, 0.060);

    float breathe = 0.010 * sin(timeSeconds * 1.2 + angle * 1.7) * (1.0 - pressure * 0.45);
    surfaceBand = abs(shell) - 0.030;
    return shell + breathe;
}

float lifeSpiralField(float2 p, out float nearestTurn, out float stepAlong)
{
    float2 q = p + float2(0.055, -0.015);
    float radius = max(length(q), 0.035);
    float theta = atan2(q.y, q.x);
    const float growth = 0.185;
    const float startRadius = 0.050;

    float turn = (log(radius / startRadius) / growth - theta) / (2.0 * PI);
    nearestTurn = clamp(round(turn), 0.0, 3.0);
    float targetRadius = startRadius * exp(growth * (theta + 2.0 * PI * nearestTurn));
    float radialError = radius - targetRadius;
    stepAlong = theta + 2.0 * PI * nearestTurn;
    return abs(radialError);
}

float lifeAperture(float3 p, float pressure)
{
    float3 q = p - float3(-0.345 + pressure * 0.055, -0.045, 0.050);
    q.xz = lifeRotate2(q.xz, -0.28);
    float bowl = sdEllipsoid(q, float3(0.435 - pressure * 0.035, 0.375, 0.335 - pressure * 0.018));
    float mouthGate = p.x + 0.180 + pressure * 0.060;
    return max(bowl, mouthGate);
}

LifeDomain lifeDomain(float3 local, SdfObject sdfObject)
{
    float pressure = saturate(sdfObject.state.z + (1.0 - saturate(sdfObject.state.y)) * 0.12);
    float3 p = local;
    p.xz = lifeRotate2(p.xz, -0.18);

    float surfaceBand;
    float shell = lifeSeedShell(p, pressure, surfaceBand);
    float aperture = lifeAperture(p, pressure);
    float openedShell = max(shell, -aperture);

    float turn;
    float stepAlong;
    float spiralLine = lifeSpiralField(p.xz, turn, stepAlong);
    float spiralGate = smoothstep(0.035, 0.22, p.x + 0.44) * (1.0 - smoothstep(0.70, 0.92, length(p.xz)));
    float seam = max(spiralLine - lerp(0.008, 0.014, pressure), surfaceBand);
    float darkGroove = (1.0 - smoothstep(0.0, 0.018, seam)) * spiralGate;

    float beadPeriod = sin(stepAlong * 9.5 + 0.7 * sin(stepAlong * 0.65));
    float beadWindow = smoothstep(0.30, 0.66, p.x + 0.42) * smoothstep(0.82, 0.24, length(p.xz));
    float beadCore = length(float2(spiralLine * 1.15, beadPeriod * 0.028)) - 0.015;
    float bead = max(beadCore, surfaceBand - 0.014);
    float beadMask = (1.0 - smoothstep(0.0, 0.012, bead)) * beadWindow * spiralGate;

    float2 ribQ = p.xz + float2(0.055, -0.015);
    float ribRadius = max(length(ribQ), 0.045);
    float ribAngle = atan2(ribQ.y, ribQ.x);
    float ribPhase = ribAngle - 1.85 * log(ribRadius + 0.030);
    float ribWave = min(frac(ribPhase * 1.18 + 0.08), 1.0 - frac(ribPhase * 1.18 + 0.08));
    float rib = max(ribWave * ribRadius - 0.014, surfaceBand - 0.020);
    float apertureReliefMask = smoothstep(0.12, 0.34, p.x + 0.36);
    float ribGate = smoothstep(0.12, 0.24, ribRadius) * (1.0 - smoothstep(0.88, 1.02, ribRadius)) * apertureReliefMask;
    float ribLift = (1.0 - smoothstep(0.0, 0.018, rib)) * ribGate * 0.008;

    float3 emberP = p - float3(-0.260 + pressure * 0.035, -0.050, 0.030);
    float emberPulse = 0.012 * sin(timeSeconds * 2.1 + pressure * 1.7);
    float ember = sdSphere(emberP, 0.090 + emberPulse);

    float lip = max(abs(aperture) - 0.030, abs(shell) - 0.045);
    float lipLift = (1.0 - smoothstep(0.0, 0.024, lip)) * 0.036;
    float beadLift = beadMask * 0.034;
    float grooveCut = darkGroove * lerp(0.012, 0.026, pressure);

    float crackSeed = sin(stepAlong * 5.7 + sin(p.y * 8.0) * 0.7) * sin((p.z - p.x) * 16.0);
    float crack = max(abs(crackSeed) - 0.955, surfaceBand - 0.004);

    LifeDomain domain;
    domain.body = min(openedShell - ribLift - lipLift - beadLift + grooveCut, ember);
    domain.shell = openedShell;
    domain.aperture = aperture;
    domain.ember = ember;
    domain.seam = seam;
    domain.bead = bead;
    domain.rib = rib;
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
    float rib = (1.0 - ember) * (1.0 - smoothstep(0.000, 0.018, domain.rib));
    float crack = (1.0 - ember) * (1.0 - smoothstep(0.000, 0.006, domain.crack));
    float chamber = (1.0 - ember) * smoothstep(0.070, -0.075, domain.aperture) * smoothstep(0.32, -0.10, local.x + 0.22);

    float nacre = 0.5 + 0.5 * sin(domain.spiralStep * 1.7 + local.y * 5.0 + timeSeconds * 0.08);
    float3 teal = lerp(float3(0.018, 0.32, 0.31), float3(0.042, 0.62, 0.57), nacre);
    float3 gold = lerp(float3(0.88, 0.42, 0.10), float3(1.0, 0.78, 0.25), nacre);
    float3 pearl = float3(0.96, 0.88, 0.68);
    float3 scar = float3(0.035, 0.020, 0.015);
    float3 emberColor = float3(1.0, 0.38, 0.055);

    SdfSurface surface;
    surface.baseColor = lerp(teal, gold, chamber * 0.94);
    surface.baseColor = lerp(surface.baseColor, pearl, saturate(lip * 0.72 + bead * 1.35 + rib * 0.68));
    float scarMask = saturate(seam * (1.0 - bead) * (1.0 - rib) * (0.06 + domain.pressure * 0.14) + crack * 0.24);
    surface.baseColor = lerp(surface.baseColor, scar, scarMask);
    surface.baseColor = lerp(surface.baseColor, emberColor, ember);
    surface.metallic = 0.06 + 0.22 * saturate(lip + bead);
    surface.roughness = lerp(0.22, 0.11, saturate(lip + bead + ember * 0.5));
    surface.emission = primitiveEmissionRadiance(sdfFieldId(sdfIndex)) * 0.020
        + teal * 0.070
        + gold * chamber * (0.64 + heartbeat * 0.34)
        + pearl * saturate(bead + rib * 0.55) * 0.28
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
