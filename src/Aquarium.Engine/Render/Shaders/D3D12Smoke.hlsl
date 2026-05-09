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
    return float4(baseColor * (0.72 + 0.28 * vignette) * pulse, 1.0);
}
