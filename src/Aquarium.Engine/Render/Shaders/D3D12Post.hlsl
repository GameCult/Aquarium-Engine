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

Texture2D<float4> sourceTexture : register(t0);
Texture2D<float4> bloomTexture0 : register(t9);
Texture2D<float4> bloomTexture1 : register(t10);
Texture2D<float4> bloomTexture2 : register(t11);
SamplerState sourceSampler : register(s0);

struct VertexOut
{
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
};

VertexOut FullscreenTriangleVS(uint vertexId : SV_VertexID)
{
    float2 uv = float2((vertexId << 1) & 2, vertexId & 2);
    VertexOut output;
    output.position = float4(uv * float2(2.0, -2.0) + float2(-1.0, 1.0), 0.0, 1.0);
    output.uv = uv;
    return output;
}

float luminance(float3 color)
{
    return dot(color, float3(0.2126, 0.7152, 0.0722));
}

float3 aces(float3 color)
{
    const float a = 2.51;
    const float b = 0.03;
    const float c = 2.43;
    const float d = 0.59;
    const float e = 0.14;
    return saturate((color * (a * color + b)) / (color * (c * color + d) + e));
}

float4 D3D12BloomPrefilterPS(VertexOut input) : SV_Target0
{
    return float4(sourceTexture.SampleLevel(sourceSampler, input.uv, 0.0).rgb * max(exposure, 0.001), 1.0);
}

float4 D3D12BloomDownsamplePS(VertexOut input) : SV_Target0
{
    float3 sumColor = 0.0;
    float sumWeight = 0.0;
    uint sourceWidth;
    uint sourceHeight;
    sourceTexture.GetDimensions(sourceWidth, sourceHeight);
    float2 texel = 1.0 / float2(max(sourceWidth, 1), max(sourceHeight, 1));

    [unroll]
    for (int y = -1; y <= 1; y++)
    {
        [unroll]
        for (int x = -1; x <= 1; x++)
        {
            float3 sampleColor = sourceTexture.SampleLevel(sourceSampler, input.uv + float2(x, y) * texel, 0.0).rgb;
            float weight = rcp(1.0 + luminance(sampleColor) * 0.12);
            sumColor += sampleColor * weight;
            sumWeight += weight;
        }
    }

    return float4(sumColor / max(sumWeight, 0.0001), 1.0);
}

float4 D3D12BloomBlurHorizontalPS(VertexOut input) : SV_Target0
{
    uint sourceWidth;
    uint sourceHeight;
    sourceTexture.GetDimensions(sourceWidth, sourceHeight);
    float2 texel = float2(1.0 / max(sourceWidth, 1), 0.0);
    float3 color =
        sourceTexture.SampleLevel(sourceSampler, input.uv - texel * 2.0, 0.0).rgb * 0.06136 +
        sourceTexture.SampleLevel(sourceSampler, input.uv - texel, 0.0).rgb * 0.24477 +
        sourceTexture.SampleLevel(sourceSampler, input.uv, 0.0).rgb * 0.38774 +
        sourceTexture.SampleLevel(sourceSampler, input.uv + texel, 0.0).rgb * 0.24477 +
        sourceTexture.SampleLevel(sourceSampler, input.uv + texel * 2.0, 0.0).rgb * 0.06136;
    return float4(color, 1.0);
}

float4 D3D12BloomBlurVerticalPS(VertexOut input) : SV_Target0
{
    uint sourceWidth;
    uint sourceHeight;
    sourceTexture.GetDimensions(sourceWidth, sourceHeight);
    float2 texel = float2(0.0, 1.0 / max(sourceHeight, 1));
    float3 color =
        sourceTexture.SampleLevel(sourceSampler, input.uv - texel * 2.0, 0.0).rgb * 0.06136 +
        sourceTexture.SampleLevel(sourceSampler, input.uv - texel, 0.0).rgb * 0.24477 +
        sourceTexture.SampleLevel(sourceSampler, input.uv, 0.0).rgb * 0.38774 +
        sourceTexture.SampleLevel(sourceSampler, input.uv + texel, 0.0).rgb * 0.24477 +
        sourceTexture.SampleLevel(sourceSampler, input.uv + texel * 2.0, 0.0).rgb * 0.06136;
    return float4(color, 1.0);
}

float4 D3D12PresentPS(VertexOut input) : SV_Target0
{
    float3 scene = sourceTexture.SampleLevel(sourceSampler, input.uv, 0.0).rgb * max(exposure, 0.001);
    float3 bloom =
        bloomTexture0.SampleLevel(sourceSampler, input.uv, 0.0).rgb * 0.42 +
        bloomTexture1.SampleLevel(sourceSampler, input.uv, 0.0).rgb * 0.34 +
        bloomTexture2.SampleLevel(sourceSampler, input.uv, 0.0).rgb * 0.24;
    if (renderDebugMode >= 6.5 && renderDebugMode < 7.5)
    {
        return float4(aces(bloom * bloomIntensity), 1.0);
    }

    if (renderDebugMode >= 7.5 && renderDebugMode < 8.5)
    {
        float luma = luminance(scene);
        return float4(luma.xxx, 1.0);
    }

    float3 presented = scene + bloom * bloomIntensity + luminance(bloom) * bloomVeilIntensity;
    return float4(aces(presented), 1.0);
}
