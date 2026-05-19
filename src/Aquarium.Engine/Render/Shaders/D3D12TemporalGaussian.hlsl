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

struct TemporalGaussian
{
    float4 centerHistoryWeight;
    float4 previousCenterFieldId;
    float4 velocityConfidence;
    float4 radiiFalloff;
    float4 orientation;
    float4 colorOpacity;
    float4 shapePad;
};

StructuredBuffer<TemporalGaussian> temporalGaussians : register(t25);

struct TemporalGaussianVertexOut
{
    float4 position : SV_Position;
    nointerpolation uint gaussianIndex : TEXCOORD0;
};

struct SceneOut
{
    float4 colorTravel : SV_Target0;
    float4 metadata : SV_Target1;
    float4 control : SV_Target2;
    float4 reservoirGuide : SV_Target3;
    float depth : SV_Depth;
};

static const float FIELD_ID_TEMPORAL_GAUSSIAN_BASE = 2000.0;

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

float3 rotateByQuaternion(float3 p, float4 q)
{
    return p + 2.0 * cross(q.xyz, cross(q.xyz, p) + q.w * p);
}

float3 inverseRotateByQuaternion(float3 p, float4 q)
{
    return rotateByQuaternion(p, float4(-q.xyz, q.w));
}

float4 safeNormalizeQuaternion(float4 q)
{
    return dot(q, q) <= 0.000001 ? float4(0.0, 0.0, 0.0, 1.0) : normalize(q);
}

float compactGaussianWeight(TemporalGaussian gaussian, float3 p)
{
    float3 local = inverseRotateByQuaternion(p - gaussian.centerHistoryWeight.xyz, safeNormalizeQuaternion(gaussian.orientation));
    float3 radii = max(gaussian.radiiFalloff.xyz, 0.0001);
    float3 q = local / radii;
    float radiusSquared = dot(q, q);
    if (radiusSquared >= 1.0)
    {
        return 0.0;
    }

    float falloff = max(gaussian.radiiFalloff.w, 0.0001);
    float edge = exp(-falloff);
    float value = exp(-falloff * radiusSquared);
    float compact = saturate((value - edge) / max(1.0 - edge, 0.000001));
    return pow(compact, max(gaussian.shapePad.x, 0.0001));
}

TemporalGaussianVertexOut D3D12TemporalGaussianVS(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID)
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

    TemporalGaussian gaussian = temporalGaussians[instanceId];
    float3 forward;
    float3 right;
    float3 up;
    cameraBasis(cameraPosition, cameraTarget, forward, right, up);
    float3 center = gaussian.centerHistoryWeight.xyz;
    float3 delta = center - cameraPosition;
    float z = max(dot(delta, forward), 0.0001);
    float2 projected = float2(dot(delta, right), dot(delta, up)) / z * 1.6;
    float clipAspect = resolution.x / max(resolution.y, 1.0);
    float boundRadius = max(max(gaussian.radiiFalloff.x, gaussian.radiiFalloff.y), gaussian.radiiFalloff.z) * 1.12;
    float projectedRadius = boundRadius / z * 1.6 + 0.004;
    float2 clipCenter = float2(projected.x / clipAspect, projected.y);
    float2 clipRadius = float2(projectedRadius / clipAspect, projectedRadius);

    TemporalGaussianVertexOut output;
    output.position = float4(clipCenter + corners[vertexId] * clipRadius, 0.0, 1.0);
    output.gaussianIndex = instanceId;
    return output;
}

SceneOut D3D12TemporalGaussianPS(TemporalGaussianVertexOut input)
{
    TemporalGaussian gaussian = temporalGaussians[input.gaussianIndex];
    float2 pixel = float2(input.position.x, resolution.y - input.position.y);
    float3 rayDirection = rayDirectionForPixel(pixel, jitterPixels, cameraPosition, cameraTarget);
    float3 forward;
    float3 right;
    float3 up;
    cameraBasis(cameraPosition, cameraTarget, forward, right, up);

    float planeDenominator = dot(rayDirection, forward);
    if (abs(planeDenominator) <= 0.00001)
    {
        discard;
    }

    float travel = dot(gaussian.centerHistoryWeight.xyz - cameraPosition, forward) / planeDenominator;
    if (travel <= 0.0 || travel >= farDistance)
    {
        discard;
    }

    float3 p = cameraPosition + rayDirection * travel;
    float weight = compactGaussianWeight(gaussian, p);
    float opacity = saturate(weight * gaussian.colorOpacity.w * gaussian.velocityConfidence.w);
    if (opacity <= 0.002)
    {
        discard;
    }

    float4 orientation = safeNormalizeQuaternion(gaussian.orientation);
    float3 local = inverseRotateByQuaternion(p - gaussian.centerHistoryWeight.xyz, orientation);
    float3 radii = max(gaussian.radiiFalloff.xyz, 0.0001);
    float3 normal = normalize(rotateByQuaternion(local / (radii * radii), orientation));

    SceneOut output;
    output.colorTravel = float4(gaussian.colorOpacity.rgb * opacity, min(travel, farDistance + 1.0));
    output.metadata = float4(FIELD_ID_TEMPORAL_GAUSSIAN_BASE + gaussian.previousCenterFieldId.w, normal);
    output.control = float4(opacity, saturate(gaussian.centerHistoryWeight.w), saturate(gaussian.shapePad.x / 8.0), 0.0);
    output.reservoirGuide = float4(saturate(gaussian.velocityConfidence.w), 0.0, 1.0, 0.0);
    output.depth = saturate(travel / max(farDistance, 0.001));
    return output;
}
