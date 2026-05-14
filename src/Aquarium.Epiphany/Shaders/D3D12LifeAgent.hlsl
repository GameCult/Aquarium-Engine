static const int SDF_INDEX = 7;

#include "D3D12SdfCommon.hlsli"
#include "D3D12SdfMath.hlsli"

struct LifeDomain
{
    float body;
    float whorl;
    float lip;
    float bead;
    float rib;
    float crack;
    float chamber;
    float2 uv;
};

float lifePhase(float2 uv)
{
    float r = max(length(uv), 0.030);
    return atan2(uv.y, uv.x) - 1.38 * log(r + 0.060) + 0.55;
}

float lifeWhorlDistance(float3 local, out float2 uv, out float phase, out float growth)
{
    uv = float2(local.x + 0.060, local.z - 0.020);
    float r = length(uv);
    phase = lifePhase(uv);
    growth = smoothstep(0.035, 0.72, r);

    float spiralSeparation = abs(sin(phase)) * max(r, 0.035);
    float tube = 0.145 + 0.245 * growth;
    float shell = length(float2(spiralSeparation * 0.78, local.y * 0.74)) - tube;
    float outer = r - (0.72 + 0.028 * sin(atan2(uv.y, uv.x) * 2.0 + 0.4));
    return max(shell, outer);
}

float lifeApertureLip(float3 local)
{
    float2 q = (local.xz - float2(-0.365, 0.040)) / float2(0.36, 0.44);
    float angle = atan2(q.y, q.x);
    float aperture = abs(length(q) - 1.0) * 0.11;
    float openSide = max(angle - 1.58, -2.72 - angle);
    return max(max(aperture - 0.017, abs(local.y + 0.205) - 0.033), openSide * 0.055);
}

float lifeEmbeddedPearls(float3 local)
{
    float2 q = (local.xz - float2(-0.365, 0.040)) / float2(0.36, 0.44);
    float angle = atan2(q.y, q.x);
    float arc = saturate((angle + 2.72) / 4.30);
    float aperture = abs(length(q) - 1.0) * 0.11;
    float beadWave = abs(sin(arc * 17.0 * PI)) - 0.42;
    float arcGate = max(angle - 1.58, -2.72 - angle);
    return max(max(aperture - 0.006, abs(local.y + 0.208) - 0.022), max(beadWave * 0.026, arcGate * 0.055));
}

LifeDomain lifeDomain(float3 local, SdfObject sdfObject)
{
    float2 uv;
    float phase;
    float growth;
    float whorl = lifeWhorlDistance(local, uv, phase, growth);
    float lip = lifeApertureLip(local);
    float bead = lifeEmbeddedPearls(local);
    float shellBand = abs(whorl + 0.018) - 0.022;
    float rib = max(abs(sin(phase * 10.0 + growth * 2.2)) * max(length(uv), 0.035) - 0.014, shellBand);
    float crack = max(abs(sin(phase * 31.0 + growth * 18.0)) * max(length(uv), 0.035) - 0.004, abs(whorl + 0.004) - 0.011);
    float chamber = smoothstep(0.11, 0.0, length((local.xz - float2(-0.330, 0.055)) / float2(0.36, 0.42)) - 0.78)
        * smoothstep(0.060, -0.030, local.y + 0.155);

    float ribLift = (1.0 - smoothstep(0.0, 0.020, rib)) * 0.012;
    float lipLift = (1.0 - smoothstep(0.0, 0.020, lip)) * 0.026;
    float beadLift = (1.0 - smoothstep(0.0, 0.014, bead)) * 0.020;

    LifeDomain domain;
    domain.body = whorl - ribLift - lipLift - beadLift;
    domain.whorl = whorl;
    domain.lip = lip;
    domain.bead = bead;
    domain.rib = rib;
    domain.crack = crack;
    domain.chamber = chamber;
    domain.uv = uv;
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
    float pressure = saturate(sdfObject.state.z + (1.0 - heartbeat) * 0.14);

    float lip = 1.0 - smoothstep(0.000, 0.021, domain.lip);
    float bead = (1.0 - lip * 0.45) * (1.0 - smoothstep(0.000, 0.014, domain.bead));
    float rib = (1.0 - lip) * (1.0 - bead) * (1.0 - smoothstep(0.000, 0.021, domain.rib));
    float shell = saturate(1.0 - lip - bead - rib);
    float chamber = shell * domain.chamber;
    float crack = shell * (1.0 - chamber) * (1.0 - smoothstep(0.000, 0.010, domain.crack));
    float phase = lifePhase(domain.uv);
    float nacre = 0.5 + 0.5 * sin(phase * 6.0 + local.y * 7.0 + timeSeconds * 0.12);

    float3 teal = lerp(float3(0.018, 0.30, 0.34), float3(0.085, 0.58, 0.62), nacre);
    float3 gold = lerp(float3(0.90, 0.48, 0.10), float3(1.0, 0.78, 0.28), nacre);
    float3 nacreLip = float3(1.0, 0.88, 0.60);
    float3 pearl = lerp(float3(0.78, 0.72, 0.58), float3(1.0, 0.96, 0.82), nacre);

    SdfSurface surface;
    surface.baseColor = teal * shell * (1.0 - chamber)
        + gold * chamber
        + nacreLip * saturate(lip + rib)
        + pearl * bead;
    surface.baseColor = lerp(surface.baseColor, gold, crack * 0.55);
    surface.metallic = 0.32 * saturate(lip + rib);
    surface.roughness = 0.22 * shell + 0.13 * chamber + 0.10 * saturate(lip + rib) + 0.09 * bead;
    surface.emission = primitiveEmissionRadiance(sdfFieldId(sdfIndex)) * 0.03
        + gold * chamber * (0.52 + heartbeat * 0.34)
        + nacreLip * lip * 0.16
        + pearl * bead * 0.14
        + gold * crack * (0.05 + pressure * 0.14);
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
    float core = exp(-dot(local - float3(-0.16, -0.14, 0.04), local - float3(-0.16, -0.14, 0.04)) * 9.0);
    return shaded
        + float3(1.0, 0.50, 0.08) * core * 0.35
        + float3(0.12, 0.62, 0.70) * edge * 0.10
        + float3(0.95, 0.70, 0.30) * domain.chamber * 0.12;
}

#define SDF_TRACE_STEP_SCALE 0.15
#define SDF_TRACE_MAX_STEP_RADIUS_SCALE 0.008

#include "D3D12SdfProxy.hlsli"
