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

float4 D3D12SmokePS(VertexOut input) : SV_Target0
{
    float3 baseColor = lerp(float3(0.012, 0.022, 0.032), float3(0.018, 0.040, 0.058), input.uv.y);
    float vignette = smoothstep(0.86, 0.18, length(input.uv - 0.5));
    return float4(baseColor * (0.72 + 0.28 * vignette), 1.0);
}
