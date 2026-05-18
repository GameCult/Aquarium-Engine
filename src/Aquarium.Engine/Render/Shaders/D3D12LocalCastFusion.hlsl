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

struct GpuFusionSeed
{
    float4 centerHistoryWeight;
    float4 previousCenterFieldId;
    float4 velocityConfidence;
    float4 radiiFalloff;
    float4 colorOpacity;
    float4 shapePad;
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

StructuredBuffer<GpuFusionSeed> fusionSeeds : register(t26);
StructuredBuffer<GpuSensorCamera> sensorCameras : register(t27);
RWStructuredBuffer<TemporalGaussian> temporalGaussiansOut : register(u0);

[numthreads(128, 1, 1)]
void D3D12LocalCastFusionCS(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    uint index = dispatchThreadId.x;
    uint seedCount = (uint)temporalGaussianInfo.x;
    if (index >= seedCount)
    {
        return;
    }

    GpuFusionSeed seed = fusionSeeds[index];
    uint cameraCount = (uint)temporalGaussianInfo.y;
    TemporalGaussian gaussian;
    gaussian.centerHistoryWeight = seed.centerHistoryWeight;
    gaussian.previousCenterFieldId = seed.previousCenterFieldId;
    gaussian.velocityConfidence = seed.velocityConfidence;
    gaussian.radiiFalloff = seed.radiiFalloff;
    gaussian.orientation = float4(0.0, 0.0, 0.0, 1.0);
    gaussian.colorOpacity = seed.colorOpacity;
    gaussian.shapePad = seed.shapePad;
    if (cameraCount > 0)
    {
        GpuSensorCamera camera = sensorCameras[index % cameraCount];
        gaussian.shapePad.w = camera.textureRangeTime.z;
    }
    temporalGaussiansOut[index] = gaussian;
}
