cbuffer AquariumFrame : register(b0)
{
    float2 resolution;
    float timeSeconds;
    float gridRadius;
    float3 cameraPosition;
    float farDistance;
    float2 gridCenter;
    float frameIndex;
    float previousTimeSeconds;
    float3 previousCameraPosition;
    float previousGridRadius;
    float2 previousGridCenter;
    float2 jitterPixels;
    float2 previousJitterPixels;
    float renderDebugMode;
    float exposure;
    float bloomIntensity;
    float bloomVeilIntensity;
    float mediumCompositeIntensity;
    float mediumDebugStep;
    float3 presentationPadding;
};

cbuffer GridHeightBrushes : register(b1)
{
    float4 brushCenterRadius[6];
    float4 brushShape[6];
    float4 brushWave[6];
};

struct VertexOut
{
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
};

struct BrushVertexOut
{
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
    nointerpolation uint brushIndex : TEXCOORD1;
    nointerpolation float4 centerRadius : TEXCOORD2;
    nointerpolation float4 shape : TEXCOORD3;
    nointerpolation float4 wave : TEXCOORD4;
};

float2 gridLocal(float2 p)
{
    return (p - gridCenter) / max(gridRadius, 0.001);
}

float2 gridUv(float2 p)
{
    return gridLocal(p) * 0.5 + 0.5;
}

float2 gridWorld(float2 uv)
{
    return gridCenter + (uv * 2.0 - 1.0) * gridRadius;
}

float powerPulse(float distanceValue, float radius, float power)
{
    float normalized = saturate(distanceValue / max(radius, 0.001));
    float shaped = pow(1.0 - normalized, power);
    return shaped * shaped * (3.0 - 2.0 * shaped);
}

VertexOut FullscreenTriangleVS(uint vertexId : SV_VertexID)
{
    float2 uv = float2((vertexId << 1) & 2, vertexId & 2);
    VertexOut output;
    output.position = float4(uv * float2(2.0, -2.0) + float2(-1.0, 1.0), 0.0, 1.0);
    output.uv = uv;
    return output;
}

float D3D12GridHeightBasePS(VertexOut input) : SV_Target
{
    float2 world = gridWorld(saturate(input.uv));
    float slow = sin((world.x * 0.08 + world.y * 0.06) + timeSeconds * 0.27)
        * sin((world.x * -0.04 + world.y * 0.07) - timeSeconds * 0.19) * 0.035;
    return slow;
}

BrushVertexOut D3D12GridHeightBrushVS(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID)
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

    float4 centerRadius = brushCenterRadius[instanceId];
    float2 world = centerRadius.xy + corners[vertexId] * centerRadius.z;
    float2 uv = gridUv(world);

    BrushVertexOut output;
    output.position = float4(uv * float2(2.0, -2.0) + float2(-1.0, 1.0), 0.0, 1.0);
    output.uv = uv;
    output.brushIndex = instanceId;
    output.centerRadius = centerRadius;
    output.shape = brushShape[instanceId];
    output.wave = brushWave[instanceId];
    return output;
}

float D3D12GridHeightBrushPS(BrushVertexOut input) : SV_Target
{
    float2 world = gridWorld(saturate(input.uv));
    float distanceValue = length(world - input.centerRadius.xy);
    float well = powerPulse(distanceValue, input.centerRadius.z, input.shape.x);
    float ripple = sin(distanceValue * input.wave.y - timeSeconds * input.wave.z);
    float signedHeight = input.shape.y * well + ripple * well * input.wave.x;
    return signedHeight;
}
