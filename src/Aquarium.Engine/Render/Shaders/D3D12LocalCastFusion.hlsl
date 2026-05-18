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

StructuredBuffer<GpuFusionSeed> fusionSeeds : register(t26);
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
    TemporalGaussian gaussian;
    gaussian.centerHistoryWeight = seed.centerHistoryWeight;
    gaussian.previousCenterFieldId = seed.previousCenterFieldId;
    gaussian.velocityConfidence = seed.velocityConfidence;
    gaussian.radiiFalloff = seed.radiiFalloff;
    gaussian.orientation = float4(0.0, 0.0, 0.0, 1.0);
    gaussian.colorOpacity = seed.colorOpacity;
    gaussian.shapePad = seed.shapePad;
    temporalGaussiansOut[index] = gaussian;
}
