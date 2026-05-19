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
    float4 gpuFusionInfo;
};

struct GpuFusionSeed
{
    float4 centerHistoryWeight;
    float4 previousCenterFieldId;
    float4 velocityConfidence;
    float4 radiiFalloff;
    float4 colorOpacity;
    float4 shapePad;
};

struct GpuFusionPoint
{
    uint2 stableKeyHash;
    uint2 sourceTimestampNs;
    float x;
    float y;
    float z;
    float radiusMeters;
    float red;
    float green;
    float blue;
    float alpha;
    float confidence;
    float pad;
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

struct GpuSensorCamera
{
    float4 intrinsics;
    float4 distortion01;
    float4 distortion23;
    float4 extentsKind;
    float4 textureRangeTime;
    float4 worldFromSensor0;
    float4 worldFromSensor1;
    float4 worldFromSensor2;
    float4 sensorFromWorld0;
    float4 sensorFromWorld1;
    float4 sensorFromWorld2;
};

struct AcousticConstraint
{
    float4 positionRadius;
    float4 velocityConfidence;
    float4 kindTimePad;
};

StructuredBuffer<GpuFusionSeed> fusionSeeds : register(t26);
StructuredBuffer<GpuSensorCamera> sensorCameras : register(t27);
Texture2D<float4> sensorTextures[8] : register(t28);
StructuredBuffer<AcousticConstraint> acousticConstraints : register(t36);
StructuredBuffer<GpuFusionPoint> fusionPoints : register(t37);
RWStructuredBuffer<TemporalGaussian> temporalGaussiansOut : register(u0);

float Hash01(uint value)
{
    value ^= value >> 16;
    value *= 2246822519u;
    value ^= value >> 13;
    value *= 3266489917u;
    value ^= value >> 16;
    return (float)(value & 0x00ffffffu) / 16777215.0;
}

float3 TransformSensorPoint(GpuSensorCamera camera, float3 sensorPoint)
{
    float4 p = float4(sensorPoint, 1.0);
    return float3(
        dot(camera.worldFromSensor0, p),
        dot(camera.worldFromSensor1, p),
        dot(camera.worldFromSensor2, p));
}

float4 LoadSensorTexture(uint slot, int3 pixel)
{
    switch (slot)
    {
        case 0u: return sensorTextures[0].Load(pixel);
        case 1u: return sensorTextures[1].Load(pixel);
        case 2u: return sensorTextures[2].Load(pixel);
        case 3u: return sensorTextures[3].Load(pixel);
        case 4u: return sensorTextures[4].Load(pixel);
        case 5u: return sensorTextures[5].Load(pixel);
        case 6u: return sensorTextures[6].Load(pixel);
        default: return sensorTextures[7].Load(pixel);
    }
}

float AcousticSupport(float3 center, out float3 velocityBias)
{
    uint count = (uint)gpuFusionInfo.x;
    velocityBias = float3(0.0, 0.0, 0.0);
    float support = 0.0;
    float weightSum = 0.0;
    [loop]
    for (uint index = 0u; index < min(count, 128u); index++)
    {
        AcousticConstraint constraint = acousticConstraints[index];
        float radius = max(0.001, constraint.positionRadius.w);
        float distanceMeters = distance(center, constraint.positionRadius.xyz);
        float weight = saturate(1.0 - distanceMeters / radius) * saturate(constraint.velocityConfidence.w);
        support = max(support, weight);
        velocityBias += constraint.velocityConfidence.xyz * weight;
        weightSum += weight;
    }

    if (weightSum > 0.0001)
    {
        velocityBias /= weightSum;
    }

    return support;
}

[numthreads(128, 1, 1)]
void D3D12LocalCastFusionCS(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    uint index = dispatchThreadId.x;
    uint outputCount = (uint)temporalGaussianInfo.x;
    uint cameraCount = (uint)temporalGaussianInfo.y;
    uint textureCount = (uint)temporalGaussianInfo.z;
    uint seedCount = (uint)temporalGaussianInfo.w;
    uint pointCount = (uint)gpuFusionInfo.y;
    if (index >= outputCount)
    {
        return;
    }

    TemporalGaussian gaussian;
    if (index < seedCount)
    {
        GpuFusionSeed seed = fusionSeeds[index];
        gaussian.centerHistoryWeight = seed.centerHistoryWeight;
        gaussian.previousCenterFieldId = seed.previousCenterFieldId;
        gaussian.velocityConfidence = seed.velocityConfidence;
        gaussian.radiiFalloff = seed.radiiFalloff;
        gaussian.orientation = float4(0.0, 0.0, 0.0, 1.0);
        gaussian.colorOpacity = seed.colorOpacity;
        gaussian.shapePad = seed.shapePad;
        temporalGaussiansOut[index] = gaussian;
        return;
    }

    if (index < seedCount + pointCount)
    {
        GpuFusionPoint nativePoint = fusionPoints[index - seedCount];
        float confidence = saturate(nativePoint.confidence);
        float radius = clamp(nativePoint.radiusMeters, 0.0015, 0.09);
        float opacity = saturate(nativePoint.alpha * (0.35 + confidence * 0.65));
        float fieldId = (float)((nativePoint.stableKeyHash.x ^ nativePoint.stableKeyHash.y) & 0x00ffffffu);
        float3 center = float3(nativePoint.x, nativePoint.y, nativePoint.z);

        gaussian.centerHistoryWeight = float4(center, confidence);
        gaussian.previousCenterFieldId = float4(center, fieldId);
        gaussian.velocityConfidence = float4(0.0, 0.0, 0.0, confidence);
        gaussian.radiiFalloff = float4(radius * 1.55, radius * 0.52, radius * 1.12, 4.6);
        gaussian.orientation = float4(0.0, 0.0, 0.0, 1.0);
        gaussian.colorOpacity = float4(saturate(nativePoint.red), saturate(nativePoint.green), saturate(nativePoint.blue), min(opacity, 0.98));
        gaussian.shapePad = float4(1.85, 0.0, 0.0, 0.0);
        temporalGaussiansOut[index] = gaussian;
        return;
    }

    if (textureCount == 0 || cameraCount == 0)
    {
        gaussian.centerHistoryWeight = float4(0.0, 0.0, 0.0, 0.0);
        gaussian.previousCenterFieldId = float4(0.0, 0.0, 0.0, 0.0);
        gaussian.velocityConfidence = float4(0.0, 0.0, 0.0, 0.0);
        gaussian.radiiFalloff = float4(0.001, 0.001, 0.001, 4.0);
        gaussian.orientation = float4(0.0, 0.0, 0.0, 1.0);
        gaussian.colorOpacity = float4(0.0, 0.0, 0.0, 0.0);
        gaussian.shapePad = float4(1.0, 0.0, 0.0, 0.0);
        temporalGaussiansOut[index] = gaussian;
        return;
    }

    uint textureSampleIndex = index - seedCount - pointCount;
    uint textureSlot = min(textureCount - 1, textureSampleIndex / max(1u, ((outputCount - seedCount) / textureCount)));
    GpuSensorCamera camera = sensorCameras[min(cameraCount - 1, textureSlot % cameraCount)];
    uint width = max(1u, (uint)camera.extentsKind.x);
    uint height = max(1u, (uint)camera.extentsKind.y);
    uint localIndex = textureSampleIndex + textureSlot * 747796405u;
    uint x = (localIndex * 73u + (uint)(Hash01(localIndex) * 37.0)) % width;
    uint y = (localIndex / width * 41u + (uint)(Hash01(localIndex ^ 0x9e3779b9u) * 29.0)) % height;
    float4 rgba = LoadSensorTexture(textureSlot, int3((int)x, (int)y, 0));
    uint pairSlot = (textureSlot + 1u) % max(1u, textureCount);
    float4 pairRgba = LoadSensorTexture(pairSlot, int3((int)x, (int)y, 0));
    float luma = dot(rgba.rgb, float3(0.2126, 0.7152, 0.0722));
    float pairLuma = dot(pairRgba.rgb, float3(0.2126, 0.7152, 0.0722));
    float descriptor = frac(luma * 3.17 + rgba.r * 1.91 + rgba.g * 2.37 + Hash01(localIndex ^ 0x27d4eb2du) * 0.07);
    float pairDescriptor = frac(pairLuma * 3.17 + pairRgba.r * 1.91 + pairRgba.g * 2.37);
    float visualMatch = 1.0 - saturate(abs(descriptor - pairDescriptor) * 3.0 + distance(rgba.rgb, pairRgba.rgb) * 0.35);
    float fx = max(1.0, camera.intrinsics.x);
    float fy = max(1.0, camera.intrinsics.y);
    float cx = camera.intrinsics.z;
    float cy = camera.intrinsics.w;
    float depth = 0.75 + luma * 2.25;
    float3 sensorPoint = float3(((float)x - cx) / fx * depth, ((float)y - cy) / fy * depth, depth);
    float3 center = TransformSensorPoint(camera, sensorPoint);
    float targetPixels = lerp(1.75, 3.75, Hash01(localIndex ^ 0x85ebca6bu));
    float focalPixels = max(1.0, 0.5 * (fx + fy));
    float screenFitRadius = targetPixels * depth / focalPixels;
    float radius = clamp(screenFitRadius, 0.0015, 0.018);
    float3 acousticVelocity;
    float acousticSupport = AcousticSupport(center, acousticVelocity);
    float confidence = saturate((0.20 + abs(luma - 0.5) * 1.15) * lerp(0.55, 1.25, visualMatch) + acousticSupport * 0.35);

    gaussian.centerHistoryWeight = float4(center, confidence);
    gaussian.previousCenterFieldId = float4(center, (float)(textureSlot + 1u));
    gaussian.velocityConfidence = float4(acousticVelocity, confidence);
    gaussian.radiiFalloff = float4(radius * lerp(1.85, 1.25, visualMatch), radius, radius * lerp(1.45, 1.05, visualMatch), 5.8);
    gaussian.orientation = float4(0.0, 0.0, 0.0, 1.0);
    gaussian.colorOpacity = float4(rgba.rgb * 1.35, saturate(0.08 + confidence * 0.42));
    gaussian.shapePad = float4(lerp(2.35, 1.45, visualMatch), descriptor, visualMatch, acousticSupport);
    temporalGaussiansOut[index] = gaussian;
}
