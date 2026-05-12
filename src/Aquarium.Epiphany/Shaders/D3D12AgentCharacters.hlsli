#ifndef AQUARIUM_D3D12_SDF_OBJECT_CHARACTERS_HLSLI
#define AQUARIUM_D3D12_SDF_OBJECT_CHARACTERS_HLSLI

float sdfObjectSdfSdf(float3 local, SdfObject sdfObject)
{
    float pulse = sdfObject.state.y;
    float core = sdSuperellipsoid(local, float3(0.92, 0.78, 0.62 + pulse * 0.06), 1.26);
    float ribA = sdTorus(local.xzy, float2(0.70, 0.024));
    float ribB = sdTorus(local.yxz, float2(0.56, 0.020));
    float3 nodeA = local - float3(0.45, -0.36, 0.24);
    float3 nodeB = local - float3(-0.38, 0.28, -0.18);
    float node = min(sdSphere(nodeA, 0.13), sdSphere(nodeB, 0.11));
    float shell = min(ribA, min(ribB, node));
    return smoothUnion(core, shell, 0.045);
}

float sdfObjectFallbackSdf(float3 local, SdfObject sdfObject)
{
    float pulse = sdfObject.state.y;
    float core = sdSuperellipsoid(local, float3(0.70, 0.58 + pulse * 0.04, 0.62), 1.34);
    float belt = sdTorus(local.xzy, float2(0.56, 0.026));
    float crown = sdTorus((local - float3(0.0, 0.0, 0.16)).yzx, float2(0.40, 0.018));
    float detail = min(belt, crown);
    return smoothUnion(core, detail, 0.032);
}

#endif
