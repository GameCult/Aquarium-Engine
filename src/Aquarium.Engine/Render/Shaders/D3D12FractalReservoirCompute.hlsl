struct FractalSdfSplat
{
    float4 centerRadius;
    float4 orientation;
    float4 radiiFalloff;
    float4 materialConfidence;
    float4 key;
};

struct SdfEnvelopeReservoir
{
    float4 centerRadius;
    float4 radiiFalloff;
    float4 weightTargetCount;
    float4 validation;
};

struct PbrMaterialReservoir
{
    float4 baseColorRoughMetal;
    float4 normalVariance;
    float4 weightTargetCount;
    float4 validation;
};

struct RadiosityReservoir
{
    float4 radianceDistance;
    float4 directionOcclusion;
    float4 weightTargetCount;
    float4 validation;
};

cbuffer ReceiptConstants : register(b0)
{
    uint SplatCount;
    uint FrameIndex;
    uint Depth;
    uint Seed;
    uint CandidatesPerPass;
    uint ReservoirUpdatesPerPass;
};

RWStructuredBuffer<FractalSdfSplat> Splats : register(u0);
RWStructuredBuffer<SdfEnvelopeReservoir> SdfReservoirs : register(u1);
RWStructuredBuffer<PbrMaterialReservoir> PbrReservoirs : register(u2);
RWStructuredBuffer<RadiosityReservoir> RadiosityReservoirs : register(u3);

uint Hash(uint x)
{
    x ^= x >> 16;
    x *= 747796405u;
    x ^= x >> 16;
    x *= 2891336453u;
    x ^= x >> 16;
    return x;
}

float Random01(uint value)
{
    return (float)(Hash(value) & 16777215u) / 16777216.0;
}

float3 FractalPoint(uint index, out float radius)
{
    uint n = index ^ Seed;
    float3 p = 0.0;
    float scale = 1.0;
    [loop]
    for (uint depth = 0; depth < Depth; depth++)
    {
        uint branch = (n >> (depth * 2u)) & 3u;
        float2 dir = branch == 0u ? float2(1.0, 0.0) : branch == 1u ? float2(-0.42, 0.91) : branch == 2u ? float2(-0.76, -0.65) : float2(0.72, -0.69);
        scale *= 0.535;
        p.xy += dir * scale;
        p.z += ((float)branch - 1.5) * scale * 0.19;
        n = Hash(n + branch + FrameIndex + depth * 17u);
    }

    radius = max(scale * 0.75, 0.0001);
    return p;
}

uint ReservoirIndex(uint updateIndex, uint passKind)
{
    return Hash(updateIndex * 1664525u + FrameIndex * 1013904223u + passKind * 747796405u + Seed) % SplatCount;
}

float4 ReservoirStats(uint index, uint passKind, float baseTarget, out uint selectedCandidate)
{
    float weightSum = 0.0;
    float selectedTarget = 0.0;
    selectedCandidate = 0u;
    [loop]
    for (uint candidate = 0u; candidate < CandidatesPerPass; candidate++)
    {
        float phase = Random01(index * 1664525u + passKind * 1013904223u + candidate * 747796405u + FrameIndex);
        float target = max(baseTarget * (0.55 + phase), 0.000001);
        float weight = target * max((float)CandidatesPerPass, 1.0);
        float nextWeightSum = weightSum + weight;
        if (weightSum <= 0.0 || Random01(index + candidate * 13007u + passKind * 7919u) < weight / max(nextWeightSum, 0.000001))
        {
            selectedTarget = target;
            selectedCandidate = candidate;
        }

        weightSum = nextWeightSum;
    }

    float contribution = weightSum / max((float)CandidatesPerPass * selectedTarget, 0.000001);
    return float4(weightSum, selectedTarget, (float)CandidatesPerPass, contribution);
}

[numthreads(256, 1, 1)]
void D3D12FractalSplatReceiptCS(uint3 id : SV_DispatchThreadID)
{
    uint index = id.x;
    if (index >= SplatCount)
    {
        return;
    }

    float radius;
    float3 p = FractalPoint(index, radius);
    uint h = Hash(index + FrameIndex * 1664525u + Seed);
    FractalSdfSplat splat;
    splat.centerRadius = float4(p, radius);
    splat.orientation = float4(0.0, 0.0, 0.0, 1.0);
    splat.radiiFalloff = float4(radius, radius * 0.72, radius * 0.45, 4.0);
    splat.materialConfidence = float4((float)(h & 1023u) / 1023.0, 1.0, 0.0, 1.0);
    splat.key = float4((float)index, (float)FrameIndex, (float)Depth, asfloat(h));
    Splats[index] = splat;
}

[numthreads(256, 1, 1)]
void D3D12SdfEnvelopeReservoirCS(uint3 id : SV_DispatchThreadID)
{
    uint updateIndex = id.x;
    if (updateIndex >= ReservoirUpdatesPerPass)
    {
        return;
    }

    uint index = ReservoirIndex(updateIndex, 0u);
    FractalSdfSplat splat = Splats[index];
    uint selected;
    float4 stats = ReservoirStats(index, 0u, splat.centerRadius.w, selected);
    SdfEnvelopeReservoir r;
    r.centerRadius = splat.centerRadius;
    r.radiiFalloff = splat.radiiFalloff;
    r.weightTargetCount = stats;
    r.validation = float4(saturate(stats.y), (float)FrameIndex, (float)selected, asfloat(Hash(index + selected)));
    SdfReservoirs[index] = r;
}

[numthreads(256, 1, 1)]
void D3D12PbrMaterialReservoirCS(uint3 id : SV_DispatchThreadID)
{
    uint updateIndex = id.x;
    if (updateIndex >= ReservoirUpdatesPerPass)
    {
        return;
    }

    uint index = ReservoirIndex(updateIndex, 1u);
    FractalSdfSplat splat = Splats[index];
    uint selected;
    float4 stats = ReservoirStats(index, 1u, splat.materialConfidence.x + 0.1, selected);
    PbrMaterialReservoir r;
    r.baseColorRoughMetal = float4(splat.materialConfidence.x, 1.0 - splat.materialConfidence.x * 0.4, 0.18 + 0.5 * splat.materialConfidence.x, 0.02);
    r.normalVariance = float4(normalize(splat.centerRadius.xyz + 0.001), splat.radiiFalloff.x);
    r.weightTargetCount = stats;
    r.validation = float4(saturate(stats.y), (float)FrameIndex, (float)selected, asfloat(Hash(index + selected + 17u)));
    PbrReservoirs[index] = r;
}

[numthreads(256, 1, 1)]
void D3D12RadiosityReservoirCS(uint3 id : SV_DispatchThreadID)
{
    uint updateIndex = id.x;
    if (updateIndex >= ReservoirUpdatesPerPass)
    {
        return;
    }

    uint index = ReservoirIndex(updateIndex, 2u);
    FractalSdfSplat splat = Splats[index];
    uint selected;
    float energy = abs(cos(length(splat.centerRadius.xyz) * 7.0 + (float)FrameIndex * 0.01));
    float4 stats = ReservoirStats(index, 2u, energy + 0.05, selected);
    RadiosityReservoir r;
    r.radianceDistance = float4(energy, energy * 0.62, energy * 0.31, length(splat.centerRadius.xyz));
    r.directionOcclusion = float4(normalize(-splat.centerRadius.xyz + 0.01), 1.0 - energy * 0.35);
    r.weightTargetCount = stats;
    r.validation = float4(saturate(stats.y), (float)FrameIndex, (float)selected, asfloat(Hash(index + selected + 29u)));
    RadiosityReservoirs[index] = r;
}
