static const int BODY_INDEX = 8;

#include "D3D12BodyCommon.hlsli"
#include "D3D12SdfMath.hlsli"

static const float CURSOR_RADIUS = 0.56;

float cursorHibiscusSdf(float3 p)
{
    AgentVisual cursor = agentVisuals[CURSOR_OBJECT_INDEX];
    float3 local = (p - cursor.centerRadius.xyz) / CURSOR_RADIUS;
    return sdHibiscusCursor(local, timeSeconds) * CURSOR_RADIUS;
}

BodySurface bodySurface(float3 p, int agentIndex)
{
    BodySurface surface;
    surface.distanceValue = cursorHibiscusSdf(p);
    surface.materialId = 5.0;
    surface.fieldId = FIELD_ID_CURSOR;
    surface.roleId = 0.0;
    surface.lodTier = 0.0;
    surface.costTier = 1.0;
    return surface;
}

float3 cursorSpecularBodyLightRadiance(float3 p, float3 normal)
{
    static const float MinimumRoughness = 0.045;
    static const float CursorRoughness = 0.22;

    float3 viewDirection = normalize(cameraPosition - p);
    float ndv = saturate(dot(normal, viewDirection));
    float roughness = max(CursorRoughness, MinimumRoughness);
    float alpha = roughness * roughness;
    float alpha2 = alpha * alpha;
    float k = (roughness + 1.0);
    k = (k * k) * 0.125;
    float geometryV = ndv / max(ndv * (1.0 - k) + k, 0.00001);
    float3 f0 = float3(1.0, 0.38, 0.72);
    float3 result = 0.0;
    [loop]
    for (int i = 0; i < BODY_LIGHT_COUNT; i++)
    {
        BodyLight light = bodyLights[i];
        float3 radiance = light.radianceFieldId.rgb;
        if (dot(radiance, radiance) <= 0.000001)
        {
            continue;
        }

        float3 toLight = light.centerRadius.xyz - p;
        float distanceSquared = max(dot(toLight, toLight), 0.01);
        float3 lightDirection = toLight * rsqrt(distanceSquared);
        float radius = max(light.centerRadius.w, 0.001);
        float3 irradiance = radiance * saturate((radius * radius) / distanceSquared) * 7.0;
        float3 halfVector = normalize(lightDirection + viewDirection);
        float ndl = saturate(dot(normal, lightDirection));
        float ndh = saturate(dot(normal, halfVector));
        float vdh = saturate(dot(viewDirection, halfVector));
        float denominator = ndh * ndh * (alpha2 - 1.0) + 1.0;
        float distribution = alpha2 / max(PI * denominator * denominator, 0.00001);
        float geometryL = ndl / max(ndl * (1.0 - k) + k, 0.00001);
        float geometry = geometryL * geometryV;
        float3 fresnel = f0 + (1.0 - f0) * pow(1.0 - vdh, 5.0);
        float3 specular = (distribution * geometry) * fresnel / max(4.0 * ndl * ndv, 0.00001);
        result += specular * irradiance * ndl;
    }

    return result;
}

float3 cursorEmissionRadiance(float3 p, float3 normal)
{
    AgentVisual cursor = agentVisuals[CURSOR_OBJECT_INDEX];
    float3 local = (p - cursor.centerRadius.xyz) / CURSOR_RADIUS;
    float petal = 0.55 + 0.45 * cos(atan2(local.y, local.x) * 3.0 - timeSeconds * 1.8);
    float throat = exp(-dot(local.xy, local.xy) * 7.0) * smoothstep(0.46, -0.28, local.z);
    float rim = pow(1.0 - saturate(dot(normal, normalize(cameraPosition - p))), 2.0);
    float3 petalColor = lerp(float3(1.25, 0.12, 0.56), float3(1.0, 0.42, 0.88), petal);
    float3 throatColor = float3(1.0, 0.76, 0.22);
    return petalColor * (0.82 + rim * 0.75) + throatColor * throat * 1.7;
}

float3 shadeBody(float2 uv, float travel, float3 p, float3 normal, int agentIndex, BodySurface surface)
{
    static const float CursorRoughness = 0.22;
    float3 cursorF0 = float3(1.0, 0.38, 0.72);
    return cursorEmissionRadiance(p, normal)
        + cursorSpecularBodyLightRadiance(p, normal)
        + studioPmremSpecularRadiance(p, normal, CursorRoughness, cursorF0);
}

#include "D3D12BodyProxy.hlsli"
