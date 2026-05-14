static const int SDF_INDEX = 7;

#include "D3D12SdfCommon.hlsli"
#include "D3D12SdfMath.hlsli"

static const float LIFE_SHELL_SCALE = 2.85;
static const float LIFE_SPIRAL_GROWTH = 0.17;

struct LifeShellDomain
{
    float body;
    float shell;
    float rib;
    float crack;
    float lip;
    float bead;
    float chamber;
    float pearl;
    float stripe;
    float u;
    float v;
    float w;
    float turn;
    float radius;
};

float2 lifeRotate(float2 p, float angle)
{
    float c = cos(angle);
    float s = sin(angle);
    return float2(c * p.x - s * p.y, s * p.x + c * p.y);
}

float lifeShellTexture(float u, float v)
{
    float du = u;
    float dv = v;

    [unroll]
    for (int i = 0; i < 5; i++)
    {
        float f = exp2((float)i);
        du += 0.15 / f * sin(f * du) * cos(f * dv);
        dv += 0.15 / f * cos(f * du) * sin(f * dv);
    }

    float middle = cos(46.0 * du);
    float side = cos(18.0 * du + 0.5) + 0.1;
    float sideFade = 0.5 - 0.5 * tanh(1.0 - 3.0 * sin(dv) * sin(dv));
    float shellTexture = lerp(middle, side, sideFade) + 0.5 - 0.6 * cos(dv);
    shellTexture += 0.5 + 0.5 * tanh(4.0 * (du - 1.8));
    return saturate(8.0 * shellTexture + 0.5);
}

LifeShellDomain lifeShellDomain(float3 local)
{
    float3 p = float3(local.x + 0.18, local.z, local.y) * LIFE_SHELL_SCALE;
    p.x -= 0.70;

    float r = length(p.xy);
    float yaw = lerp(0.0, 0.45, smoothstep(0.0, 1.0, 0.5 * (r - 0.6)));
    p.xy = lifeRotate(p.xy, yaw);
    r = max(length(p.xy), 0.0005);
    float t = atan2(p.y, p.x);

    float ro = exp(LIFE_SPIRAL_GROWTH * PI);
    float d = length(float2(length(p.xz - float2(-ro, 0.0)) - ro, p.y));
    float u = t;
    float dx = r - ro;
    float dy = p.z;
    float solvedTurn = 0.0;

    float n = (log(max((r * r + p.z * p.z) / (2.0 * r), 0.0001)) / LIFE_SPIRAL_GROWTH - t) / (2.0 * PI);
    n = min(n, 0.0);
    float n0 = floor(n);
    float n1 = ceil(n);
    float r0 = exp(LIFE_SPIRAL_GROWTH * (2.0 * PI * n0 + t));
    float r1 = exp(LIFE_SPIRAL_GROWTH * (2.0 * PI * n1 + t));
    float d0 = abs(length(float2(r - r0, p.z)) - r0);
    float d1 = abs(length(float2(r - r1, p.z)) - r1);

    if (d0 < d)
    {
        d = d0;
        u = 2.0 * PI * n0 + t;
        dx = r - r0;
        dy = p.z;
        solvedTurn = n0;
    }

    if (d1 < d)
    {
        d = d1;
        u = 2.0 * PI * n1 + t;
        dx = r - r1;
        dy = p.z;
        solvedTurn = n1;
    }

    float chamberPhase = t + 2.0 * PI * (n0 + 0.5);
    float turnBlend = frac(n);
    float septumCurve = 2.35 * chamberPhase
        + sqrt(max(0.25 - (turnBlend - 0.5) * (turnBlend - 0.5), 0.0))
        + 0.5 * turnBlend;
    float siphuncle = min(1.0 / (40.0 * length(float2(turnBlend - 0.5, p.z)) + 1.0), 0.5);
    septumCurve += siphuncle * siphuncle;
    float septumWave = frac(septumCurve);
    septumWave = chamberPhase > -1.8 ? abs(septumCurve + 3.25) : min(septumWave, 1.0 - septumWave);
    float septumDistance = septumWave / 2.35 * exp(LIFE_SPIRAL_GROWTH * (chamberPhase + PI));

    float chamberGate = 1.0 - smoothstep(2.6, 3.05, length(p * float3(1.0, 1.0, 1.5)));
    float septumSurface = 0.5 * septumDistance + 0.012;
    d = lerp(d, min(d, septumSurface), chamberGate);

    float sectionRadius = max(exp(LIFE_SPIRAL_GROWTH * u), 0.0005);
    float v = atan2(dy, dx);
    float w = length(float2(dx, dy)) / sectionRadius;
    float shellThickness = 0.8 * max(0.02 * pow(r, 0.4), 0.02);
    d += 0.00012 * r * sin(200.0 * u);
    float shell = abs(d) - shellThickness;
    float cut = max(-1.35 - p.z, p.z - 0.82);
    shell = max(shell, cut);

    float outerTurn = smoothstep(-1.05, -0.06, n);
    float lipBand = abs(w - 1.02) * sectionRadius;
    float apertureLip = max(lipBand - 0.040, -outerTurn);
    float cutLip = max(abs(cut) - 0.040, shell - 0.020);
    float lip = min(apertureLip, cutLip);
    float beadArc = abs(sin((v + 0.65 * sin(u)) * 19.0 + u * 1.4)) - 0.38;
    float beadRail = min(max(lipBand - 0.018, -outerTurn), cutLip);
    float bead = max(beadRail, beadArc * 0.030);
    float rib = max(septumDistance - 0.012, abs(shell + 0.006) - 0.026);
    float crackPhase = sin(u * 8.3 + sin(v * 3.1) * 1.7) * sin(v * 13.0 + u * 0.6);
    float crack = max(abs(crackPhase) - 0.965, abs(shell + 0.002) - 0.010);
    float chamber = saturate(outerTurn * smoothstep(1.20, 0.70, w) * smoothstep(0.35, -0.05, cut));
    float pearl = saturate((1.0 - smoothstep(0.000, 0.020, bead)) * outerTurn);

    float ribLift = (1.0 - smoothstep(0.0, 0.018, rib)) * 0.038;
    float lipLift = (1.0 - smoothstep(0.0, 0.040, lip)) * 0.050;
    float beadLift = pearl * 0.035;

    LifeShellDomain domain;
    domain.body = (shell - ribLift - lipLift - beadLift) / LIFE_SHELL_SCALE;
    domain.shell = shell / LIFE_SHELL_SCALE;
    domain.rib = rib / LIFE_SHELL_SCALE;
    domain.crack = crack / LIFE_SHELL_SCALE;
    domain.lip = lip / LIFE_SHELL_SCALE;
    domain.bead = bead / LIFE_SHELL_SCALE;
    domain.chamber = chamber;
    domain.pearl = pearl;
    domain.stripe = lifeShellTexture(u, v);
    domain.u = u;
    domain.v = v;
    domain.w = w;
    domain.turn = solvedTurn;
    domain.radius = r / LIFE_SHELL_SCALE;
    return domain;
}

float sdfDistance(float3 p, int sdfIndex)
{
    SdfObject sdfObject = sdfObjects[sdfIndex];
    float radius = max(sdfObject.centerRadius.w, 0.001);
    float3 local = (p - sdfObject.centerRadius.xyz) / radius;
    return lifeShellDomain(local).body * radius;
}

SdfSurface sdfSurface(float3 p, int sdfIndex)
{
    SdfObject sdfObject = sdfObjects[sdfIndex];
    float radius = max(sdfObject.centerRadius.w, 0.001);
    float3 local = (p - sdfObject.centerRadius.xyz) / radius;
    LifeShellDomain domain = lifeShellDomain(local);
    float heartbeat = saturate(sdfObject.state.y);
    float pressure = saturate(sdfObject.state.z + (1.0 - heartbeat) * 0.14);

    float lip = 1.0 - smoothstep(0.000, 0.020, domain.lip);
    float bead = domain.pearl;
    float rib = (1.0 - lip) * (1.0 - bead) * (1.0 - smoothstep(0.000, 0.010, domain.rib));
    float crack = (1.0 - lip) * (1.0 - bead) * (1.0 - smoothstep(0.000, 0.006, domain.crack));
    float chamber = domain.chamber * (1.0 - lip * 0.40);
    float ornament = saturate(max(max(lip, bead), rib));

    float3 deepTeal = float3(0.018, 0.42, 0.42);
    float3 teal = lerp(deepTeal, float3(0.060, 0.86, 0.76), domain.stripe);
    float3 warmNacre = lerp(float3(0.70, 0.54, 0.26), float3(1.0, 0.78, 0.30), saturate(domain.chamber + 0.25 * domain.stripe));
    float3 lipColor = float3(0.95, 0.82, 0.55);
    float3 pearlColor = float3(0.98, 0.92, 0.72);
    float3 crackGold = float3(1.0, 0.55, 0.10);

    SdfSurface surface;
    surface.baseColor = lerp(teal, warmNacre, chamber * 0.62);
    surface.baseColor = lerp(surface.baseColor, lipColor, saturate(lip * 0.24 + rib * 0.36));
    surface.baseColor = lerp(surface.baseColor, pearlColor, bead);
    surface.baseColor = lerp(surface.baseColor, crackGold, crack * 0.65);
    surface.metallic = 0.04 + 0.28 * ornament;
    surface.roughness = 0.20 * (1.0 - ornament) + 0.13 * chamber + 0.09 * ornament + 0.10 * bead;
    surface.emission = primitiveEmissionRadiance(sdfFieldId(sdfIndex)) * 0.025
        + teal * 0.070
        + warmNacre * chamber * (0.70 + heartbeat * 0.34)
        + lipColor * lip * 0.12
        + pearlColor * bead * 0.16
        + crackGold * crack * (0.05 + pressure * 0.16);
    return surface;
}

float3 shadeSdf(float2 uv, float travel, float3 p, float3 normal, int sdfIndex, SdfSurface surface)
{
    SdfObject sdfObject = sdfObjects[sdfIndex];
    float radius = max(sdfObject.centerRadius.w, 0.001);
    float3 local = (p - sdfObject.centerRadius.xyz) / radius;
    LifeShellDomain domain = lifeShellDomain(local);
    float3 shaded = shadeSdfPbr(p, normal, surface);

    float3 viewDirection = normalize(cameraPosition - p);
    float edge = pow(1.0 - saturate(dot(normal, viewDirection)), 3.0);
    float core = exp(-dot(local - float3(-0.20, -0.05, 0.02), local - float3(-0.20, -0.05, 0.02)) * 18.0);
    return shaded
        + float3(1.0, 0.48, 0.08) * core * 0.30
        + float3(0.10, 0.52, 0.56) * edge * 0.08
        + float3(0.95, 0.62, 0.18) * domain.chamber * 0.14;
}

#define SDF_TRACE_STEP_SCALE 0.12
#define SDF_TRACE_MAX_STEP_RADIUS_SCALE 0.006

#include "D3D12SdfProxy.hlsli"
