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

float4 D3D12MediumDensityDebugPS(VertexOut input) : SV_Target0
{
    float4 diagnostic = sourceTexture.SampleLevel(sourceSampler, input.uv, 0.0);
    float density = saturate(diagnostic.r);
    float transmittance = saturate(diagnostic.g);
    float source = saturate(diagnostic.b);
    float3 clear = float3(0.006, 0.014, 0.022);
    float3 fog = float3(0.18, 0.48, 0.68) * density;
    fog += float3(0.95, 0.72, 0.36) * source;
    fog *= lerp(0.45, 1.0, transmittance);
    return float4(max(clear, fog), 1.0);
}

float4 D3D12MediumLightDebugPS(VertexOut input) : SV_Target0
{
    float3 light = max(sourceTexture.SampleLevel(sourceSampler, input.uv, 0.0).rgb, 0.0);
    float luminance = dot(light, float3(0.2126, 0.7152, 0.0722));
    float exposure = 1.0 - exp(-luminance * 0.85);
    float3 chroma = light / max(luminance, 0.0001);
    float3 color = saturate(chroma * exposure);
    return float4(max(float3(0.004, 0.008, 0.012), color), 1.0);
}
