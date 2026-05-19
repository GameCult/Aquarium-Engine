cbuffer AquariumFrame : register(b0)
{
    float2 resolution;
    float timeSeconds;
    float viewRadius;
    float3 cameraPosition;
    float farDistance;
    float3 cameraTarget;
    float sceneFlags;
    float2 viewCenter;
    float frameIndex;
    float previousTimeSeconds;
    float3 previousCameraPosition;
    float previousViewRadius;
    float3 previousCameraTarget;
    float previousSceneFlags;
    float2 previousViewCenter;
    float2 jitterPixels;
    float2 previousJitterPixels;
    float renderDebugMode;
    float exposure;
    float bloomIntensity;
    float bloomVeilIntensity;
    float4 cursorWorlds;
    float4 temporalGaussianInfo;
};

cbuffer HeightFieldBrushes : register(b1)
{
    float4 brushCenterRadius[64];
    float4 brushShape[64];
    float4 brushWave[64];
    float4 brushDomain[64];
};

TextureCube<float4> studioPmremTexture : register(t22);
TextureCube<float4> studioIrradianceTexture : register(t23);
SamplerState linearSampler : register(s0);

struct SdfLight
{
    float4 centerRadius;
    float4 radianceFieldId;
};

StructuredBuffer<SdfLight> sdfLights : register(t12);

struct SdfObject
{
    float4 centerRadius;
    float4 previousCenterPad;
    float4 state;
};

StructuredBuffer<SdfObject> sdfObjects : register(t24);

static const float FIELD_ID_HEIGHT_FIELD = 4.0;
static const float FIELD_ID_SDF_OBJECT_BASE = 10.0;
static const int AQUARIUM_SDF_OBJECT_CAPACITY = 64;
static const float PI = 3.14159265359;
static const int SDF_LIGHT_COUNT = 8;
static const float STUDIO_PMREM_MAX_LOD = 9.0;
static const float STUDIO_PMREM_SPECULAR_INTENSITY = 0.34;
static const float STUDIO_IRRADIANCE_INTENSITY = 0.74;

struct SdfObjectProxyVertexOut
{
    float4 position : SV_Position;
    nointerpolation float sdfIndex : TEXCOORD0;
};

struct SceneOut
{
    float4 colorTravel : SV_Target0;
    float4 metadata : SV_Target1;
    float4 control : SV_Target2;
    float depth : SV_Depth;
};

struct SdfSurface
{
    float3 baseColor;
    float metallic;
    float roughness;
    float3 emission;
    float temporalDetail;
    float reservoirConfidence;
};

void cameraBasis(float3 camera, float3 target, out float3 forward, out float3 right, out float3 up)
{
    forward = normalize(target - camera);
    right = normalize(cross(forward, float3(0.0, 0.0, 1.0)));
    up = cross(right, forward);
}

float3 rayDirectionForPixel(float2 pixel, float2 jitter, float3 camera, float3 target)
{
    float2 ndc = ((pixel + jitter) * 2.0 - resolution) / resolution.y;
    float3 forward;
    float3 right;
    float3 up;
    cameraBasis(camera, target, forward, right, up);
    return normalize(forward * 1.6 + right * ndc.x + up * ndc.y);
}

float hash21(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

bool traceSphere(float3 origin, float3 direction, float3 center, float radius, out float travel)
{
    float3 oc = origin - center;
    float b = dot(oc, direction);
    float c = dot(oc, oc) - radius * radius;
    float h = b * b - c;
    if (h < 0.0)
    {
        travel = farDistance + 1.0;
        return false;
    }

    h = sqrt(h);
    float t = -b - h;
    if (t < 0.0)
    {
        t = -b + h;
    }

    travel = t;
    return t > 0.0 && t < farDistance;
}

float3 studioPmremDirection(float3 worldDirection)
{
    return normalize(float3(worldDirection.x, worldDirection.z, worldDirection.y));
}

float3 studioPmremSample(float3 worldDirection, float lod)
{
    return studioPmremTexture.SampleLevel(linearSampler, studioPmremDirection(worldDirection), lod).rgb;
}

float3 primitiveEmissionRadiance(float fieldId)
{
    [loop]
    for (int i = 0; i < SDF_LIGHT_COUNT; i++)
    {
        SdfLight light = sdfLights[i];
        if (abs(light.radianceFieldId.w - fieldId) < 0.25)
        {
            return light.radianceFieldId.rgb;
        }
    }

    return 0.0;
}

float sdfFieldId(int objectIndex)
{
    return FIELD_ID_SDF_OBJECT_BASE + (float)objectIndex;
}

float3 pbrDielectricF0()
{
    return float3(0.04, 0.04, 0.04);
}

float3 pbrSpecularF0(SdfSurface surface)
{
    return lerp(pbrDielectricF0(), surface.baseColor, saturate(surface.metallic));
}

float3 pbrDiffuseColor(SdfSurface surface)
{
    return surface.baseColor * (1.0 - saturate(surface.metallic));
}

float3 sdfLightRadianceAt(float3 p, SdfLight light, out float3 lightDirection, out float attenuation)
{
    float3 toLight = light.centerRadius.xyz - p;
    float distanceSquared = max(dot(toLight, toLight), 0.01);
    lightDirection = toLight * rsqrt(distanceSquared);
    float radius = max(light.centerRadius.w, 0.001);
    attenuation = saturate((radius * radius) / distanceSquared);
    return light.radianceFieldId.rgb;
}

float3 sdfLightPbrRadiance(float3 p, float3 normal, SdfSurface surface, float intensity, float shadedFieldId)
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

        float3 lightDirection;
        float attenuation;
        float sameField = abs(light.radianceFieldId.w - shadedFieldId) < 0.25 ? 1.0 : 0.0;
        float directScale = lerp(1.0, 0.08, sameField);
        float3 incidentRadiance = sdfLightRadianceAt(p, light, lightDirection, attenuation) * (intensity * directScale);
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

float3 fresnelSchlickRoughness(float cosine, float3 f0, float roughness)
{
    return f0 + (max(1.0 - roughness, f0) - f0) * pow(1.0 - cosine, 5.0);
}

float3 studioPmremSpecularRadiance(float3 p, float3 normal, SdfSurface surface)
{
    float3 viewDirection = normalize(cameraPosition - p);
    float ndv = saturate(dot(normal, viewDirection));
    float3 reflectionDirection = reflect(-viewDirection, normal);
    float lod = saturate(surface.roughness) * STUDIO_PMREM_MAX_LOD;
    float3 radiance = studioPmremSample(reflectionDirection, lod);
    float3 fresnel = fresnelSchlickRoughness(ndv, pbrSpecularF0(surface), surface.roughness);
    return radiance * fresnel * STUDIO_PMREM_SPECULAR_INTENSITY;
}

float3 studioIrradianceDiffuseRadiance(float3 p, float3 normal, SdfSurface surface)
{
    float3 viewDirection = normalize(cameraPosition - p);
    float ndv = saturate(dot(normal, viewDirection));
    float3 fresnel = fresnelSchlickRoughness(ndv, pbrSpecularF0(surface), surface.roughness);
    float3 diffuseShare = (1.0 - fresnel) * (1.0 - saturate(surface.metallic));
    float3 irradiance = studioIrradianceTexture.SampleLevel(linearSampler, studioPmremDirection(normal), 0.0).rgb;
    return diffuseShare * surface.baseColor * irradiance * (STUDIO_IRRADIANCE_INTENSITY / PI);
}

float3 shadeSdfPbr(float3 p, float3 normal, SdfSurface surface)
{
    surface.baseColor = saturate(surface.baseColor);
    surface.metallic = saturate(surface.metallic);
    surface.roughness = saturate(surface.roughness);

    return surface.emission
        + sdfLightPbrRadiance(p, normal, surface, 7.0, sdfFieldId(SDF_INDEX))
        + studioIrradianceDiffuseRadiance(p, normal, surface)
        + studioPmremSpecularRadiance(p, normal, surface);
}

SdfObjectProxyVertexOut D3D12SdfObjectProxyVS(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID)
{
    float2 corners[6] =
    {
        float2(-1.0, -1.0),
        float2(1.0, -1.0),
        float2(1.0, 1.0),
        float2(-1.0, -1.0),
        float2(1.0, 1.0),
        float2(-1.0, 1.0),
    };

    SdfObject sdfObject = sdfObjects[SDF_INDEX];
    float3 forward;
    float3 right;
    float3 up;
    cameraBasis(cameraPosition, cameraTarget, forward, right, up);
    float3 delta = sdfObject.centerRadius.xyz - cameraPosition;
    float z = max(dot(delta, forward), 0.0001);
    float2 projected = float2(dot(delta, right), dot(delta, up)) / z * 1.6;
    float clipAspect = resolution.x / max(resolution.y, 1.0);
    float boundRadius = sdfObject.centerRadius.w * 1.58;
    float projectedRadius = boundRadius / z * 1.6 + 0.035;
    float2 clipCenter = float2(projected.x / clipAspect, projected.y);
    float2 clipRadius = float2(projectedRadius / clipAspect, projectedRadius);

    SdfObjectProxyVertexOut output;
    output.position = float4(clipCenter + corners[vertexId] * clipRadius, 0.0, 1.0);
    output.sdfIndex = (float)SDF_INDEX;
    return output;
}
