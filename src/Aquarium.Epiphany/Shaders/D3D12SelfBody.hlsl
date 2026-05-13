static const int SDF_INDEX = 0;

#include "D3D12SdfCommon.hlsli"
#include "D3D12SdfMath.hlsli"

struct SelfField
{
    float core;
    float inlay;
    float rail;
    float distanceValue;
};

float3x3 selfOrbitalFrame(float phase)
{
    float2 a = float2(cos(phase * 0.19), sin(phase * 0.19));
    float2 b = float2(cos(phase * -0.13 + 1.7), sin(phase * -0.13 + 1.7));
    float2 c = float2(cos(phase * 0.11 + 3.2), sin(phase * 0.11 + 3.2));

    return float3x3(
        normalize(float3(0.82 * a.x, 0.82 * a.y, 0.57)),
        normalize(float3(-0.42 * b.x, 0.72, 0.56 * b.y)),
        normalize(float3(0.38, -0.74 * c.x, 0.56 * c.y)));
}

float3 selfLatticeCoordinates(float3 dir, float shellIndex, float phase)
{
    float3x3 frame = selfOrbitalFrame(phase + shellIndex * 2.0943951);
    float3 latitude = mul(frame, dir);
    float3 shellPhase = shellIndex * float3(1.3, 2.1, 3.4);
    return latitude + 0.055 * sin(latitude.zxy * float3(3.0, 4.0, 5.0) + shellPhase + phase * 0.23);
}

float min3(float3 value)
{
    return min(value.x, min(value.y, value.z));
}

float selfShellRadius(float shellIndex, float pressure)
{
    float radius = 0.64 + shellIndex * 0.22 + shellIndex * shellIndex * 0.035;
    return lerp(radius, radius * 0.88, pressure);
}

void selfShellField(float3 dir, float r, float shellIndex, float pressure, float phase, inout float rail)
{
    float shell = r - selfShellRadius(shellIndex, pressure);
    float3 bands = abs(selfLatticeCoordinates(dir, shellIndex, phase)) * r;
    float thickness = lerp(0.020, 0.030, saturate(shellIndex * 0.5));

    float shellRail = length(float2(shell * 1.18, min3(bands) * 0.74)) - thickness;

    rail = min(rail, shellRail);
}

SelfField selfField(float3 local, SdfObject sdfObject, float timeSeconds)
{
    float heartbeat = saturate(sdfObject.state.y);
    float pressure = saturate(sdfObject.state.z);
    float phase = timeSeconds * 0.72 + heartbeat * 2.1;

    float r = max(length(local), 0.0001);
    float3 dir = local / r;
    float coreRadius = 0.50;
    float rail = 10.0;

    [unroll]
    for (int i = 0; i < 3; i++)
    {
        selfShellField(dir, r, (float)i, pressure, phase, rail);
    }

    float3 coreBands = abs(selfLatticeCoordinates(dir, -1.0, phase));

    float coreShell = r - (coreRadius + 0.006);
    float inlay = length(float2(coreShell, coreBands.x * r * 0.10)) - 0.007;

    float core = sdSphere(local, coreRadius);
    float routed = smoothUnion(core, inlay, 0.006);
    routed = smoothUnion(routed, rail, 0.026);

    SelfField field;
    field.core = core;
    field.inlay = inlay;
    field.rail = rail;
    field.distanceValue = routed;
    return field;
}

float sdfDistance(float3 p, int sdfIndex)
{
    SdfObject sdfObject = sdfObjects[sdfIndex];
    float radius = max(sdfObject.centerRadius.w, 0.001);
    float3 local = (p - sdfObject.centerRadius.xyz) / radius;
    return selfField(local, sdfObject, timeSeconds).distanceValue * radius;
}

SdfSurface sdfSurface(float3 p, int sdfIndex)
{
    SdfObject sdfObject = sdfObjects[sdfIndex];
    float radius = max(sdfObject.centerRadius.w, 0.001);
    float3 local = (p - sdfObject.centerRadius.xyz) / radius;
    SelfField field = selfField(local, sdfObject, timeSeconds);

    float isRail = field.rail <= min(field.core, field.inlay) ? 1.0 : 0.0;
    float isInlay = (1.0 - isRail) * (field.inlay <= field.core ? 1.0 : 0.0);
    float isCore = (1.0 - isRail) * (1.0 - isInlay);

    float3 coreColor = float3(0.0, 0.0, 0.0);
    float3 inlayColor = float3(1.0, 0.70, 0.22);
    float3 railColor = float3(0.86, 0.54, 0.20);

    SdfSurface surface;
    surface.baseColor = coreColor * isCore + inlayColor * isInlay + railColor * isRail;
    surface.metallic = 0.0 * isCore + 0.62 * isInlay + 0.76 * isRail;
    surface.roughness = 1.0 * isCore + 0.22 * isInlay + 0.26 * isRail;

    surface.emission = inlayColor * isInlay * 0.34
        + railColor * isRail * (0.42 + sdfObject.state.y * 0.10);
    return surface;
}

float3 selfDirectPbrRadiance(float3 p, float3 normal, SdfSurface surface, float selfFieldId)
{
    static const float MinimumRoughness = 0.045;

    float3 viewDirection = normalize(cameraPosition - p);
    float ndv = saturate(dot(normal, viewDirection));
    float safeRoughness = max(surface.roughness, MinimumRoughness);
    float alpha = safeRoughness * safeRoughness;
    float alpha2 = alpha * alpha;
    float k = (safeRoughness + 1.0);
    k = (k * k) * 0.125;
    float geometryV = ndv / max(ndv * (1.0 - k) + k, 0.00001);
    float3 f0 = pbrSpecularF0(surface);
    float3 diffuseColor = pbrDiffuseColor(surface);
    float3 result = 0.0;

    [loop]
    for (int i = 0; i < SDF_LIGHT_COUNT; i++)
    {
        SdfLight light = sdfLights[i];
        if (dot(light.radianceFieldId.rgb, light.radianceFieldId.rgb) <= 0.000001)
        {
            continue;
        }

        float sameSelf = abs(light.radianceFieldId.w - selfFieldId) < 0.25 ? 1.0 : 0.0;
        float directScale = lerp(1.0, 0.08, sameSelf);
        float3 lightDirection;
        float attenuation;
        float3 incidentRadiance = sdfLightRadianceAt(p, light, lightDirection, attenuation) * (7.0 * directScale);
        float3 irradiance = incidentRadiance * attenuation;
        float3 halfVector = normalize(lightDirection + viewDirection);
        float ndl = saturate(dot(normal, lightDirection));
        float ndh = saturate(dot(normal, halfVector));
        float vdh = saturate(dot(viewDirection, halfVector));
        float denominator = ndh * ndh * (alpha2 - 1.0) + 1.0;
        float distribution = alpha2 / max(PI * denominator * denominator, 0.00001);
        float geometryL = ndl / max(ndl * (1.0 - k) + k, 0.00001);
        float3 fresnel = f0 + (1.0 - f0) * pow(1.0 - vdh, 5.0);
        float3 specular = (distribution * geometryL * geometryV) * fresnel / max(4.0 * ndl * ndv, 0.00001);
        float3 diffuse = (1.0 - fresnel) * diffuseColor / PI;
        result += (diffuse + specular) * irradiance * ndl;
    }

    return result;
}

float3 shadeSdf(float2 uv, float travel, float3 p, float3 normal, int sdfIndex, SdfSurface surface)
{
    SdfObject sdfObject = sdfObjects[sdfIndex];
    float radius = max(sdfObject.centerRadius.w, 0.001);
    float3 local = (p - sdfObject.centerRadius.xyz) / radius;
    SelfField field = selfField(local, sdfObject, timeSeconds);
    bool isCore = field.core <= min(field.inlay, field.rail);
    if (isCore)
    {
        float3 viewDirection = normalize(cameraPosition - p);
        float edge = 1.0 - saturate(dot(normal, viewDirection));
        float rim = pow(edge, 4.0) * 8.0 + pow(edge, 11.0) * 18.0;
        float3 gold = float3(1.0, 0.62, 0.18);
        return float3(0.0007, 0.00035, 0.0) + gold * rim;
    }

    surface.baseColor = saturate(surface.baseColor);
    surface.metallic = saturate(surface.metallic);
    surface.roughness = saturate(surface.roughness);
    return surface.emission
        + selfDirectPbrRadiance(p, normal, surface, sdfFieldId(sdfIndex))
        + studioIrradianceDiffuseRadiance(p, normal, surface)
        + studioPmremSpecularRadiance(p, normal, surface);
}

#include "D3D12SdfProxy.hlsli"
