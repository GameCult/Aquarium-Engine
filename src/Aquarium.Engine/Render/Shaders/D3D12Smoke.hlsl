struct VertexOut
{
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
};

cbuffer SmokeConstants : register(b0)
{
    float4 smokeTint;
    float4 smokeParams;
};

Texture2D<float4> sourceTexture : register(t0);
RWTexture2D<float4> diagnosticUav : register(u1);
SamplerState sourceSampler : register(s0);

struct FieldInstance
{
    float4 centerRadius;
    float4 radiusAngle;
    float4 fieldFlags;
    float4 materialMedium;
    float4 colorIntensity;
    float4 mediumTerms;
};

StructuredBuffer<int4> froxelPrimitiveIds : register(t1);
StructuredBuffer<FieldInstance> fieldInstances : register(t12);

VertexOut FullscreenTriangleVS(uint vertexId : SV_VertexID)
{
    float2 uv = float2((vertexId << 1) & 2, vertexId & 2);
    VertexOut output;
    output.position = float4(uv * float2(2.0, -2.0) + float2(-1.0, 1.0), 0.0, 1.0);
    output.uv = uv;
    return output;
}

float4 D3D12SmokePS(VertexOut input) : SV_Target0
{
    float3 baseColor = lerp(float3(0.012, 0.022, 0.032), smokeTint.rgb, input.uv.y);
    float vignette = smoothstep(0.86, 0.18, length(input.uv - 0.5));
    float pulse = 0.92 + 0.08 * sin(smokeParams.x);
    float fieldProbe = saturate(fieldInstances[0].centerRadius.w);
    float froxelProbe = max(froxelPrimitiveIds[0].x, 0) / 16.0;
    diagnosticUav[uint2(input.position.xy)] = float4(input.uv, saturate(fieldProbe + froxelProbe), 1.0);
    return float4(baseColor * (0.72 + 0.28 * vignette) * pulse, 1.0);
}

float4 D3D12CopyPS(VertexOut input) : SV_Target0
{
    return sourceTexture.SampleLevel(sourceSampler, input.uv, 0.0);
}

float4 D3D12GridHeightDebugPS(VertexOut input) : SV_Target0
{
    float height = sourceTexture.SampleLevel(sourceSampler, input.uv, 0.0).r;
    float signedShape = saturate(abs(height) * 0.85);
    float3 negative = float3(0.08, 0.34, 0.52);
    float3 positive = float3(0.95, 0.62, 0.22);
    float3 baseColor = height < 0.0 ? negative : positive;
    return float4(baseColor * signedShape, 1.0);
}
