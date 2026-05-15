static const int SDF_INDEX = 7;

#include "D3D12SdfCommon.hlsli"
#include "D3D12SdfMath.hlsli"

static const float LIFE_B = 0.125;
static const float LIFE_PROFILE_B = 0.175;
static const float LIFE_PROFILE_START_RADIUS = 0.030;
static const float LIFE_PROFILE_END_RADIUS = 0.0;
static const float LIFE_TURNS = 3.10;
static const float LIFE_WALL = 0.020;
static const float LIFE_C_SPAN = 4.25;
static const float LIFE_OPEN_CENTER = PI;
static const float LIFE_APERTURE_FLARE = 0.0;
static const float LIFE_CONTACT_PULL = 1.0;
static const float LIFE_CONTACT_SPAN_PULL = 1.0;
static const float LIFE_TERMINAL_U = 0.34;

static const float LIFE_MAT_OUTER = 1.0;
static const float LIFE_MAT_INNER = 2.0;
static const float LIFE_MAT_SEAM = 3.0;
static const float LIFE_MAT_PEARL = 4.0;
static const float LIFE_MAT_EMBER = 5.0;

struct LifeDomain
{
    float body;
    float shell;
    float ember;
    float seam;
    float pearl;
    float lip;
    float mat;
    float u;
    float v;
    float w;
    float edge;
    float endpoint;
    float pressure;
};

float2 lifeRotate2(float2 p, float angle)
{
    float c = cos(angle);
    float s = sin(angle);
    return float2(c * p.x - s * p.y, s * p.x + c * p.y);
}

float lifeTri(float x)
{
    return abs(frac(x) - 0.5) * 2.0;
}

float lifeHash21(float2 p)
{
    return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
}

float2 lifeHash22(float2 p)
{
    float2 q = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
    return frac(sin(q) * 43758.5453);
}

float lifeVoronoiEdge(float2 p)
{
    float2 cell = floor(p);
    float2 local = frac(p);
    float nearest = 8.0;
    float nextNearest = 8.0;

    [unroll]
    for (int y = -1; y <= 1; y++)
    {
        [unroll]
        for (int x = -1; x <= 1; x++)
        {
            float2 offset = float2((float)x, (float)y);
            float2 jitter = lifeHash22(cell + offset);
            float2 d = offset + jitter - local;
            float distanceSquared = dot(d, d);
            if (distanceSquared < nearest)
            {
                nextNearest = nearest;
                nearest = distanceSquared;
            }
            else if (distanceSquared < nextNearest)
            {
                nextNearest = distanceSquared;
            }
        }
    }

    return sqrt(nextNearest) - sqrt(nearest);
}

float lifeAngleDelta(float a, float b)
{
    return atan2(sin(a - b), cos(a - b));
}

float lifeSpiralRadius(float u)
{
    return exp(LIFE_B * u);
}

float lifeProfileGrowthRadius(float u)
{
    float age = clamp(u + LIFE_TURNS * 2.0 * PI, 0.0, LIFE_TURNS * 2.0 * PI);
    return LIFE_PROFILE_START_RADIUS * exp(LIFE_PROFILE_B * age);
}

float lifeProfileTerminalFlare(float u)
{
    return 0.0;
}

float lifeProfileBaseRadius(float u)
{
    return lifeProfileGrowthRadius(u) + lifeProfileTerminalFlare(u);
}

float lifeShellThickness(float profileRadius)
{
    return LIFE_WALL * max(pow(profileRadius, 0.35), 0.76);
}

float lifeApertureHalfSpan(float u)
{
    return LIFE_C_SPAN * 0.5 - LIFE_APERTURE_FLARE * smoothstep(-1.05, 0.25, u);
}

float lifeArcCenter(float u)
{
    return LIFE_OPEN_CENTER + PI;
}

float lifeContactHalfSpan(float halfSpan, float u)
{
    float whorlRadius = lifeSpiralRadius(u);
    float previousProfile = lifeProfileBaseRadius(u - 2.0 * PI);
    float d = (1.0 - exp(-LIFE_B * 2.0 * PI)) * whorlRadius;
    float ratio = previousProfile / max(d, 0.0001);
    float feasible = step(0.0001, ratio);
    float kp = clamp(ratio, 0.0, 1.0);
    float minimum = lerp(halfSpan, acos(-sqrt(max(1.0 - kp * kp, 0.0))) + 0.035, feasible);
    return lerp(halfSpan, max(halfSpan, minimum), LIFE_CONTACT_SPAN_PULL);
}

float lifeContactArcRadius(float whorlRadius, float u, float center, float halfSpan)
{
    float k = exp(-LIFE_B * 2.0 * PI);
    float d = (1.0 - k) * whorlRadius;
    float previousProfile = lifeProfileBaseRadius(u - 2.0 * PI);
    float base = lifeProfileBaseRadius(u);
    float endpoint = center + halfSpan;
    float c = cos(endpoint);
    float s = sin(endpoint);
    float root = previousProfile * previousProfile - d * d * s * s;
    float solved = -d * c + sqrt(max(root, 0.0));
    float feasible = step(0.0, root) * step(0.0, solved);
    float contactRadius = lerp(base, max(solved, 0.012), feasible);
    return lerp(base, contactRadius, LIFE_CONTACT_PULL);
}

float lifeArcDistance(float2 q, float radius, float center, float halfSpan, out float v, out float w, out float edge, out float endpointMask)
{
    float lenQ = max(length(q), 0.0001);
    v = atan2(q.y, q.x);
    w = lenQ / max(radius, 0.0001);
    float da = lifeAngleDelta(v, center);
    float absDa = abs(da);

    float2 e0 = radius * float2(cos(center - halfSpan), sin(center - halfSpan));
    float2 e1 = radius * float2(cos(center + halfSpan), sin(center + halfSpan));
    float circleDistance = abs(lenQ - radius);
    float endpointDistance = min(length(q - e0), length(q - e1));

    edge = (absDa - halfSpan) * radius;
    endpointMask = smoothstep(0.035, -0.010, abs(edge));
    return absDa <= halfSpan ? circleDistance : endpointDistance;
}

float lifeBranchSolidDistance(float3 p, float t, float n, out float u, out float v, out float w, out float edge, out float endpoint)
{
    u = 2.0 * PI * n + t;
    float whorlRadius = lifeSpiralRadius(u);
    float r = max(length(p.xy), 0.0001);
    float center = lifeArcCenter(u);
    float halfSpan = lifeContactHalfSpan(lifeApertureHalfSpan(u), u);
    float profileRadius = lifeContactArcRadius(whorlRadius, u, center, halfSpan);
    float centerDistance = lifeArcDistance(float2(r - whorlRadius, p.z), profileRadius, center, halfSpan, v, w, edge, endpoint);

    float d = centerDistance - lifeShellThickness(profileRadius);
    d = max(d, (-LIFE_TURNS * 2.0 * PI - 0.16) - u);
    d = max(d, u - LIFE_TERMINAL_U);
    return d;
}

LifeDomain lifeMakeDomain(float d, float shell, float mat, float u, float v, float w, float edge, float endpoint, float pressure)
{
    LifeDomain domain;
    domain.body = d;
    domain.shell = shell;
    domain.ember = 999.0;
    domain.seam = 999.0;
    domain.pearl = 999.0;
    domain.lip = 999.0;
    domain.mat = mat;
    domain.u = u;
    domain.v = v;
    domain.w = w;
    domain.edge = edge;
    domain.endpoint = endpoint;
    domain.pressure = pressure;
    return domain;
}

LifeDomain lifeChoose(LifeDomain a, LifeDomain b)
{
    if (b.body < a.body)
    {
        return b;
    }

    return a;
}

LifeDomain lifeBranchDomain(float3 p, float t, float n, float pressure)
{
    float u;
    float v;
    float w;
    float edge;
    float endpoint;
    float shell = lifeBranchSolidDistance(p, t, n, u, v, w, edge, endpoint);

    float prevU;
    float prevV;
    float prevW;
    float prevEdge;
    float prevEndpoint;
    float previous = lifeBranchSolidDistance(p, t, n - 1.0, prevU, prevV, prevW, prevEdge, prevEndpoint);
    float hasPrevious = smoothstep(-LIFE_TURNS * 2.0 * PI + 0.12, -LIFE_TURNS * 2.0 * PI + 0.42, u);
    float seamLocality = smoothstep(0.030, 0.003, abs(edge));
    float trimmedShell = shell;

    float innerSide = (1.0 - smoothstep(0.990, 1.015, w)) * (1.0 - endpoint);
    float mat = (innerSide > 0.50 && u > -2.65) ? LIFE_MAT_INNER : LIFE_MAT_OUTER;
    LifeDomain domain = lifeMakeDomain(trimmedShell, shell, mat, u, v, w, edge, endpoint, pressure);

    float seamGate = smoothstep(-LIFE_TURNS * 2.0 * PI + 0.55, -LIFE_TURNS * 2.0 * PI + 1.20, u);
    float seam = max(abs(edge) - 0.0035, shell - 0.006);
    seam = lerp(999.0, seam, hasPrevious * seamLocality);
    domain.seam = seam;

    float pearlWave = lifeTri(u * 1.72 + 0.040 * sin(v * 1.7));
    float pearlLine = pearlWave * 0.052;
    float pearlBand = length(float2(pearlLine, shell)) - 0.009;
    float pearlGate = seamGate * (1.0 - endpoint * 0.50) * smoothstep(0.20, 0.92, w);
    domain.pearl = lerp(999.0, pearlBand, pearlGate);

    domain.lip = lerp(999.0, length(float2(abs(edge), shell)) - 0.024, endpoint);

    float pearlDetail = domain.pearl;
    float lipDetail = domain.lip;
    float detail = min(pearlDetail, lipDetail);
    domain.body = min(trimmedShell, detail);
    if (detail < trimmedShell)
    {
        domain.mat = LIFE_MAT_PEARL;
    }

    return domain;
}

LifeDomain lifeDomain(float3 local, SdfObject sdfObject)
{
    float pressure = saturate(sdfObject.state.z + (1.0 - saturate(sdfObject.state.y)) * 0.10);
    float3 p = local;
    p *= 1.08;
    p.yz = lifeRotate2(p.yz, -0.10);
    p.xy = lifeRotate2(p.xy, -0.22);

    // Shell grammar lives in xy/z: xy is the logarithmic spiral plane, z is
    // the C-profile thickness axis. Map world yz into that domain so the
    // right-side view sees the growth spiral instead of a sliced rim.
    p = float3(p.y, p.z, p.x);

    float r = max(length(p.xy), 0.0001);
    float t = atan2(p.y, p.x);
    float n = (log(max((r * r + p.z * p.z) / (2.0 * r), 0.0001)) / LIFE_B - t) / (2.0 * PI);
    n = clamp(n, -LIFE_TURNS, 0.0);

    LifeDomain domain = lifeMakeDomain(999.0, 999.0, LIFE_MAT_OUTER, 0.0, 0.0, 0.0, 999.0, 0.0, pressure);
    float n0 = floor(n);
    float n1 = ceil(n);
    domain = lifeChoose(domain, lifeBranchDomain(p, t, n0, pressure));
    domain = lifeChoose(domain, lifeBranchDomain(p, t, n1, pressure));

    float ember = sdSphere(p - float3(0.0, 0.0, 0.0), 0.060 + 0.004 * sin(timeSeconds * 2.1));
    if (ember < domain.body)
    {
        LifeDomain emberDomain = lifeMakeDomain(ember, domain.shell, LIFE_MAT_EMBER, domain.u, domain.v, domain.w, domain.edge, domain.endpoint, pressure);
        emberDomain.ember = ember;
        return emberDomain;
    }

    domain.ember = ember;
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

    float emberMask = domain.mat == LIFE_MAT_EMBER ? 1.0 : 0.0;
    float pearlMask = domain.mat == LIFE_MAT_PEARL ? 1.0 : 0.0;
    float seamMask = (1.0 - emberMask) * (1.0 - pearlMask) * (1.0 - smoothstep(0.000, 0.010, domain.seam));
    float innerMask = domain.mat == LIFE_MAT_INNER ? 1.0 : 0.0;

    float nacre = 0.5 + 0.5 * sin(domain.u * 0.78 + domain.v * 1.35 + timeSeconds * 0.06);
    float3 fractureP = local * 3.75;
    float warp = sin(dot(fractureP, float3(1.7, -0.9, 1.2))) * 0.28
        + sin(dot(fractureP, float3(-0.6, 1.9, 1.1))) * 0.18;
    float fractureEdgeA = lifeVoronoiEdge(fractureP.xy + warp);
    float fractureEdgeB = lifeVoronoiEdge(fractureP.yz * 1.13 - warp * 0.7);
    float fractureEdgeC = lifeVoronoiEdge(fractureP.zx * 0.91 + warp * 0.5);
    float fractureEdge = min(fractureEdgeA, min(fractureEdgeB, fractureEdgeC));
    float fractureMask = (1.0 - emberMask) * smoothstep(0.080, 0.010, fractureEdge);
    float3 teal = lerp(float3(0.006, 0.185, 0.250), float3(0.030, 0.470, 0.610), nacre);
    float3 innerPearl = lerp(float3(0.840, 0.805, 0.700), float3(0.985, 0.940, 0.800), nacre);
    float3 gold = lerp(float3(0.82, 0.420, 0.080), float3(1.0, 0.735, 0.205), nacre);
    float3 pearlColor = float3(0.965, 0.905, 0.735);
    float3 seamColor = float3(0.075, 0.050, 0.026);
    float3 emberColor = float3(1.0, 0.390, 0.050);

    SdfSurface surface;
    surface.baseColor = lerp(teal, innerPearl, innerMask);
    surface.baseColor = lerp(surface.baseColor, gold, fractureMask * lerp(0.42, 0.76, innerMask));
    surface.baseColor = lerp(surface.baseColor, pearlColor, pearlMask);
    surface.baseColor = lerp(surface.baseColor, seamColor, seamMask);
    surface.baseColor = lerp(surface.baseColor, emberColor, emberMask);
    surface.metallic = lerp(0.04, 0.10, saturate(fractureMask + seamMask * 0.35));
    surface.metallic = lerp(surface.metallic, 0.03, pearlMask);
    surface.roughness = lerp(0.28, 0.16, saturate(innerMask + pearlMask + seamMask * 0.6));
    surface.roughness = lerp(surface.roughness, 0.08, emberMask);
    surface.emission = primitiveEmissionRadiance(sdfFieldId(sdfIndex)) * 0.012
        + gold * innerMask * 0.050
        + gold * fractureMask * 0.095
        + pearlColor * pearlMask * 0.040
        + gold * seamMask * 0.090
        + emberColor * emberMask * (3.2 + heartbeat * 1.1 + domain.pressure * 0.8);
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
    float fresnel = pow(1.0 - saturate(dot(normal, viewDirection)), 3.2);
    float innerGlow = domain.mat == LIFE_MAT_INNER ? 1.0 : 0.0;
    return shaded
        + float3(0.020, 0.360, 0.330) * fresnel * 0.10
        + float3(1.0, 0.470, 0.080) * innerGlow * 0.050;
}

#define SDF_TRACE_STEP_SCALE 0.12
#define SDF_TRACE_MAX_STEP_RADIUS_SCALE 0.006

#include "D3D12SdfProxy.hlsli"
