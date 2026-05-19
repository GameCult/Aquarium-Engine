using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Aquarium.Engine.Fractal;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;

var options = ReceiptOptions.Parse(args);
using var runner = new GpuFractalSplatReceiptRunner();
var receipt = runner.Run(options);

Directory.CreateDirectory(options.OutputDirectory);
var receiptPath = Path.Combine(options.OutputDirectory, $"fractal-gpu-splat-receipt-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.json");
File.WriteAllText(receiptPath, JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));

Console.WriteLine("=== Aquarium Perfect Machine GPU Splat Receipt ===");
Console.WriteLine($"adapter: {receipt.Adapter}");
Console.WriteLine($"splats: {receipt.SplatCount:N0}");
Console.WriteLine($"sdf reservoirs: {receipt.SdfReservoirCount:N0}");
Console.WriteLine($"pbr reservoirs: {receipt.PbrReservoirCount:N0}");
Console.WriteLine($"radiosity reservoirs: {receipt.RadiosityReservoirCount:N0}");
Console.WriteLine($"candidates/pass: {receipt.CandidatesPerPass}");
Console.WriteLine($"reservoir updates/pass/frame: {receipt.ReservoirUpdatesPerPass:N0}");
Console.WriteLine($"reservoir full-coverage frames: {receipt.ReservoirFullCoverageFrames:0.0}");
Console.WriteLine($"frames: {receipt.MeasuredFrames}");
Console.WriteLine("shader: D3D12 compute, independent GPU-resident SDF/PBR/radiosity reservoir passes");
Console.WriteLine($"packed bytes/splat: {receipt.BytesPerSplat}");
Console.WriteLine($"packed bytes/reservoir: {receipt.BytesPerReservoir}");
Console.WriteLine($"gpu ms/frame total: {receipt.GpuMillisecondsPerFrame:0.000}");
Console.WriteLine($"gpu equivalent fps: {receipt.GpuEquivalentFps:0.0}");
Console.WriteLine($"gpu splats/sec: {receipt.GpuSplatsPerSecond:N0}");
Console.WriteLine($"gpu reservoir candidates/sec: {receipt.GpuReservoirCandidatesPerSecond:N0}");
Console.WriteLine($"cpu submit+wait ms/frame: {receipt.CpuSubmitAndWaitMillisecondsPerFrame:0.000}");
Console.WriteLine($"readback checksum: 0x{receipt.ReadbackChecksum:X16}");
Console.WriteLine($"receipt: {receiptPath}");

internal sealed class GpuFractalSplatReceiptRunner : IDisposable
{
    private const int ThreadGroupSize = 256;

    private readonly ID3D12Device device;
    private readonly ID3D12CommandQueue queue;
    private readonly ID3D12CommandAllocator allocator;
    private readonly ID3D12GraphicsCommandList commandList;
    private readonly ID3D12Fence fence;
    private readonly AutoResetEvent fenceEvent = new(false);
    private readonly ID3D12RootSignature rootSignature;
    private readonly ID3D12PipelineState splatPipelineState;
    private readonly ID3D12PipelineState sdfPipelineState;
    private readonly ID3D12PipelineState pbrPipelineState;
    private readonly ID3D12PipelineState radiosityPipelineState;
    private readonly string adapterName;
    private ulong fenceValue;

    public GpuFractalSplatReceiptRunner()
    {
        device = D3D12.D3D12CreateDevice<ID3D12Device>(IntPtr.Zero, FeatureLevel.Level_11_0);
        adapterName = ResolveAdapterName();
        queue = device.CreateCommandQueue(new CommandQueueDescription(CommandListType.Direct));
        allocator = device.CreateCommandAllocator(CommandListType.Direct);
        commandList = device.CreateCommandList<ID3D12GraphicsCommandList>(0, CommandListType.Direct, allocator, null);
        commandList.Close();
        fence = device.CreateFence(0);
        rootSignature = CreateRootSignature(device);
        splatPipelineState = CreatePipelineState(device, rootSignature, "D3D12FractalSplatReceiptCS");
        sdfPipelineState = CreatePipelineState(device, rootSignature, "D3D12SdfEnvelopeReservoirCS");
        pbrPipelineState = CreatePipelineState(device, rootSignature, "D3D12PbrMaterialReservoirCS");
        radiosityPipelineState = CreatePipelineState(device, rootSignature, "D3D12RadiosityReservoirCS");
    }

    public GpuFractalSplatReceipt Run(ReceiptOptions options)
    {
        var splatStride = Marshal.SizeOf<AquariumPackedFractalSdfSplat3D>();
        var reservoirStride = Marshal.SizeOf<AquariumPackedSdfEnvelopeReservoir>();
        var splatBytes = checked((ulong)splatStride * (ulong)options.SplatCount);
        var reservoirBytes = checked((ulong)reservoirStride * (ulong)options.SplatCount);
        using var splats = CreateUavBuffer(splatBytes, "Aquarium Fractal Receipt GPU Splat Buffer");
        using var sdfReservoirs = CreateUavBuffer(reservoirBytes, "Aquarium Fractal Receipt SDF Reservoir Buffer");
        using var pbrReservoirs = CreateUavBuffer(reservoirBytes, "Aquarium Fractal Receipt PBR Reservoir Buffer");
        using var radiosityReservoirs = CreateUavBuffer(reservoirBytes, "Aquarium Fractal Receipt Radiosity Reservoir Buffer");

        var readbackSplatBytes = (ulong)Math.Min(options.ReadbackSplats, options.SplatCount) * (ulong)splatStride;
        var readbackReservoirBytes = (ulong)Math.Min(options.ReadbackSplats, options.SplatCount) * (ulong)reservoirStride;
        var totalReadbackBytes = readbackSplatBytes + (readbackReservoirBytes * 3UL);
        using var readback = device.CreateCommittedResource(HeapType.Readback, ResourceDescription.Buffer(Math.Max(totalReadbackBytes, 1)), ResourceStates.CopyDest, null);
        using var queryHeap = device.CreateQueryHeap<ID3D12QueryHeap>(new QueryHeapDescription(QueryHeapType.Timestamp, 2u));
        using var queryReadback = device.CreateCommittedResource(HeapType.Readback, ResourceDescription.Buffer(16), ResourceStates.CopyDest, null);

        queue.GetTimestampFrequency(out var timestampFrequency);
        var measuredGpuTicks = 0UL;
        var measuredCpuTicks = 0L;
        var measuredFrames = 0;

        for (var frame = 0; frame < options.WarmupFrames + options.MeasuredFrames; frame++)
        {
            allocator.Reset();
            commandList.Reset(allocator, splatPipelineState);
            commandList.SetComputeRootSignature(rootSignature);
            BindConstants(options, frame);
            commandList.SetComputeRootUnorderedAccessView(1, splats.GPUVirtualAddress);
            commandList.SetComputeRootUnorderedAccessView(2, sdfReservoirs.GPUVirtualAddress);
            commandList.SetComputeRootUnorderedAccessView(3, pbrReservoirs.GPUVirtualAddress);
            commandList.SetComputeRootUnorderedAccessView(4, radiosityReservoirs.GPUVirtualAddress);
            commandList.EndQuery(queryHeap, QueryType.Timestamp, 0);
            Dispatch(splatPipelineState, options.SplatCount);
            Dispatch(sdfPipelineState, options.ReservoirUpdatesPerPass);
            Dispatch(pbrPipelineState, options.ReservoirUpdatesPerPass);
            Dispatch(radiosityPipelineState, options.ReservoirUpdatesPerPass);
            commandList.EndQuery(queryHeap, QueryType.Timestamp, 1);
            commandList.ResolveQueryData(queryHeap, QueryType.Timestamp, 0, 2, queryReadback, 0);

            if (frame == options.WarmupFrames + options.MeasuredFrames - 1 && totalReadbackBytes > 0)
            {
                CopyReceiptReadback(splats, sdfReservoirs, pbrReservoirs, radiosityReservoirs, readback, readbackSplatBytes, readbackReservoirBytes);
            }

            commandList.Close();
            var cpuStart = Stopwatch.GetTimestamp();
            queue.ExecuteCommandList(commandList);
            WaitForGpu();
            var cpuEnd = Stopwatch.GetTimestamp();

            if (frame >= options.WarmupFrames)
            {
                measuredFrames++;
                measuredCpuTicks += cpuEnd - cpuStart;
                unsafe
                {
                    var timestamps = (ulong*)queryReadback.Map<byte>(0);
                    measuredGpuTicks += timestamps[1] - timestamps[0];
                    queryReadback.Unmap(0);
                }
            }
        }

        var checksum = totalReadbackBytes == 0 ? 0UL : Checksum(readback, (int)totalReadbackBytes);
        var gpuSeconds = measuredGpuTicks / (double)timestampFrequency;
        var gpuMsPerFrame = gpuSeconds * 1000.0 / Math.Max(measuredFrames, 1);
        var cpuMsPerFrame = measuredCpuTicks * 1000.0 / Stopwatch.Frequency / Math.Max(measuredFrames, 1);
        var splatsPerSecond = options.SplatCount * (double)measuredFrames / Math.Max(gpuSeconds, 1.0e-12);
        var reservoirCandidatesPerSecond = options.ReservoirUpdatesPerPass * 3.0 * options.CandidatesPerPass * measuredFrames / Math.Max(gpuSeconds, 1.0e-12);

        return new GpuFractalSplatReceipt(
            adapterName,
            options.SplatCount,
            options.SplatCount,
            options.SplatCount,
            options.SplatCount,
            options.CandidatesPerPass,
            options.ReservoirUpdatesPerPass,
            options.SplatCount / (double)options.ReservoirUpdatesPerPass,
            measuredFrames,
            splatStride,
            reservoirStride,
            gpuMsPerFrame,
            1000.0 / Math.Max(gpuMsPerFrame, 1.0e-12),
            splatsPerSecond,
            reservoirCandidatesPerSecond,
            cpuMsPerFrame,
            checksum);

        void Dispatch(ID3D12PipelineState state, int elementCount)
        {
            commandList.SetPipelineState(state);
            commandList.Dispatch((uint)((elementCount + ThreadGroupSize - 1) / ThreadGroupSize), 1, 1);
        }
    }

    public void Dispose()
    {
        WaitForGpu();
        radiosityPipelineState.Dispose();
        pbrPipelineState.Dispose();
        sdfPipelineState.Dispose();
        splatPipelineState.Dispose();
        rootSignature.Dispose();
        fence.Dispose();
        commandList.Dispose();
        allocator.Dispose();
        queue.Dispose();
        device.Dispose();
        fenceEvent.Dispose();
    }

    private ID3D12Resource CreateUavBuffer(ulong bytes, string name)
    {
        var resource = device.CreateCommittedResource(HeapType.Default, ResourceDescription.Buffer(bytes, ResourceFlags.AllowUnorderedAccess), ResourceStates.UnorderedAccess, null);
        resource.Name = name;
        return resource;
    }

    private void BindConstants(ReceiptOptions options, int frame)
    {
        commandList.SetComputeRoot32BitConstant(0, (uint)options.SplatCount, 0);
        commandList.SetComputeRoot32BitConstant(0, (uint)frame, 1);
        commandList.SetComputeRoot32BitConstant(0, (uint)options.Depth, 2);
        commandList.SetComputeRoot32BitConstant(0, options.Seed, 3);
        commandList.SetComputeRoot32BitConstant(0, (uint)options.CandidatesPerPass, 4);
        commandList.SetComputeRoot32BitConstant(0, (uint)options.ReservoirUpdatesPerPass, 5);
    }

    private void CopyReceiptReadback(ID3D12Resource splats, ID3D12Resource sdf, ID3D12Resource pbr, ID3D12Resource radiosity, ID3D12Resource readback, ulong splatBytes, ulong reservoirBytes)
    {
        commandList.ResourceBarrier(ResourceBarrier.BarrierTransition(splats, ResourceStates.UnorderedAccess, ResourceStates.CopySource));
        commandList.ResourceBarrier(ResourceBarrier.BarrierTransition(sdf, ResourceStates.UnorderedAccess, ResourceStates.CopySource));
        commandList.ResourceBarrier(ResourceBarrier.BarrierTransition(pbr, ResourceStates.UnorderedAccess, ResourceStates.CopySource));
        commandList.ResourceBarrier(ResourceBarrier.BarrierTransition(radiosity, ResourceStates.UnorderedAccess, ResourceStates.CopySource));
        commandList.CopyBufferRegion(readback, 0, splats, 0, splatBytes);
        commandList.CopyBufferRegion(readback, splatBytes, sdf, 0, reservoirBytes);
        commandList.CopyBufferRegion(readback, splatBytes + reservoirBytes, pbr, 0, reservoirBytes);
        commandList.CopyBufferRegion(readback, splatBytes + (reservoirBytes * 2), radiosity, 0, reservoirBytes);
        commandList.ResourceBarrier(ResourceBarrier.BarrierTransition(splats, ResourceStates.CopySource, ResourceStates.UnorderedAccess));
        commandList.ResourceBarrier(ResourceBarrier.BarrierTransition(sdf, ResourceStates.CopySource, ResourceStates.UnorderedAccess));
        commandList.ResourceBarrier(ResourceBarrier.BarrierTransition(pbr, ResourceStates.CopySource, ResourceStates.UnorderedAccess));
        commandList.ResourceBarrier(ResourceBarrier.BarrierTransition(radiosity, ResourceStates.CopySource, ResourceStates.UnorderedAccess));
    }

    private void WaitForGpu()
    {
        fenceValue++;
        queue.Signal(fence, fenceValue);
        if (fence.CompletedValue < fenceValue)
        {
            fence.SetEventOnCompletion(fenceValue, fenceEvent.SafeWaitHandle.DangerousGetHandle());
            fenceEvent.WaitOne();
        }
    }

    private string ResolveAdapterName()
    {
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory6>();
        for (var index = 0u; factory.EnumAdapterByGpuPreference(index, GpuPreference.HighPerformance, out IDXGIAdapter1? adapter).Success && adapter is not null; index++)
        {
            using (adapter)
            {
                var description = adapter.Description1;
                if ((description.Flags & AdapterFlags.Software) == 0)
                {
                    return description.Description;
                }
            }
        }

        return "default D3D12 adapter";
    }

    private static ID3D12RootSignature CreateRootSignature(ID3D12Device device)
    {
        var rootParameters = new[]
        {
            new RootParameter(new RootConstants(0, 0, 6), ShaderVisibility.All),
            new RootParameter(RootParameterType.UnorderedAccessView, new RootDescriptor(0, 0), ShaderVisibility.All),
            new RootParameter(RootParameterType.UnorderedAccessView, new RootDescriptor(1, 0), ShaderVisibility.All),
            new RootParameter(RootParameterType.UnorderedAccessView, new RootDescriptor(2, 0), ShaderVisibility.All),
            new RootParameter(RootParameterType.UnorderedAccessView, new RootDescriptor(3, 0), ShaderVisibility.All),
        };
        var description = new RootSignatureDescription(RootSignatureFlags.None, rootParameters, []);
        return device.CreateRootSignature(0, in description, RootSignatureVersion.Version1);
    }

    private static ID3D12PipelineState CreatePipelineState(ID3D12Device device, ID3D12RootSignature rootSignature, string entry)
    {
        var shader = Compiler.Compile(ShaderSource, entry, "receipt.hlsl", "cs_5_0", ShaderFlags.OptimizationLevel3, EffectFlags.None);
        return device.CreateComputePipelineState(new ComputePipelineStateDescription { RootSignature = rootSignature, ComputeShader = shader });
    }

    private static unsafe ulong Checksum(ID3D12Resource readback, int byteCount)
    {
        var data = (byte*)readback.Map<byte>(0);
        var hash = 1469598103934665603UL;
        for (var index = 0; index < byteCount; index++)
        {
            hash ^= data[index];
            hash *= 1099511628211UL;
        }

        readback.Unmap(0);
        return hash;
    }

    private const string ShaderSource = """
struct PackedSplat { float4 centerRadius; float4 orientation; float4 radiiFalloff; float4 materialConfidence; float4 key; };
struct SdfReservoir { float4 centerRadius; float4 radiiFalloff; float4 weightTargetCount; float4 validation; };
struct PbrReservoir { float4 baseColorRoughMetal; float4 normalVariance; float4 weightTargetCount; float4 validation; };
struct RadiosityReservoir { float4 radianceDistance; float4 directionOcclusion; float4 weightTargetCount; float4 validation; };

cbuffer ReceiptConstants : register(b0) { uint SplatCount; uint FrameIndex; uint Depth; uint Seed; uint CandidatesPerPass; uint ReservoirUpdatesPerPass; };
RWStructuredBuffer<PackedSplat> Splats : register(u0);
RWStructuredBuffer<SdfReservoir> SdfReservoirs : register(u1);
RWStructuredBuffer<PbrReservoir> PbrReservoirs : register(u2);
RWStructuredBuffer<RadiosityReservoir> RadiosityReservoirs : register(u3);

uint Hash(uint x) { x ^= x >> 16; x *= 747796405u; x ^= x >> 16; x *= 2891336453u; x ^= x >> 16; return x; }
float Random01(uint value) { return (float)(Hash(value) & 16777215u) / 16777216.0; }

float3 FractalPoint(uint index, out float radius)
{
    uint n = index ^ Seed;
    float3 p = 0.0;
    float scale = 1.0;
    [loop] for (uint depth = 0; depth < Depth; depth++)
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
    [loop] for (uint candidate = 0u; candidate < CandidatesPerPass; candidate++)
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
    uint index = id.x; if (index >= SplatCount) return;
    float radius; float3 p = FractalPoint(index, radius);
    uint h = Hash(index + FrameIndex * 1664525u + Seed);
    PackedSplat splat;
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
    uint updateIndex = id.x; if (updateIndex >= ReservoirUpdatesPerPass) return;
    uint index = ReservoirIndex(updateIndex, 0u);
    PackedSplat splat = Splats[index];
    uint selected; float4 stats = ReservoirStats(index, 0u, splat.centerRadius.w, selected);
    SdfReservoir r;
    r.centerRadius = splat.centerRadius;
    r.radiiFalloff = splat.radiiFalloff;
    r.weightTargetCount = stats;
    r.validation = float4(saturate(stats.y), (float)FrameIndex, (float)selected, asfloat(Hash(index + selected)));
    SdfReservoirs[index] = r;
}

[numthreads(256, 1, 1)]
void D3D12PbrMaterialReservoirCS(uint3 id : SV_DispatchThreadID)
{
    uint updateIndex = id.x; if (updateIndex >= ReservoirUpdatesPerPass) return;
    uint index = ReservoirIndex(updateIndex, 1u);
    PackedSplat splat = Splats[index];
    uint selected; float4 stats = ReservoirStats(index, 1u, splat.materialConfidence.x + 0.1, selected);
    PbrReservoir r;
    r.baseColorRoughMetal = float4(splat.materialConfidence.x, 1.0 - splat.materialConfidence.x * 0.4, 0.18 + 0.5 * splat.materialConfidence.x, 0.02);
    r.normalVariance = float4(normalize(splat.centerRadius.xyz + 0.001), splat.radiiFalloff.x);
    r.weightTargetCount = stats;
    r.validation = float4(saturate(stats.y), (float)FrameIndex, (float)selected, asfloat(Hash(index + selected + 17u)));
    PbrReservoirs[index] = r;
}

[numthreads(256, 1, 1)]
void D3D12RadiosityReservoirCS(uint3 id : SV_DispatchThreadID)
{
    uint updateIndex = id.x; if (updateIndex >= ReservoirUpdatesPerPass) return;
    uint index = ReservoirIndex(updateIndex, 2u);
    PackedSplat splat = Splats[index];
    uint selected; float energy = abs(cos(length(splat.centerRadius.xyz) * 7.0 + (float)FrameIndex * 0.01));
    float4 stats = ReservoirStats(index, 2u, energy + 0.05, selected);
    RadiosityReservoir r;
    r.radianceDistance = float4(energy, energy * 0.62, energy * 0.31, length(splat.centerRadius.xyz));
    r.directionOcclusion = float4(normalize(-splat.centerRadius.xyz + 0.01), 1.0 - energy * 0.35);
    r.weightTargetCount = stats;
    r.validation = float4(saturate(stats.y), (float)FrameIndex, (float)selected, asfloat(Hash(index + selected + 29u)));
    RadiosityReservoirs[index] = r;
}
""";
}

internal sealed record ReceiptOptions(
    int SplatCount,
    int WarmupFrames,
    int MeasuredFrames,
    int Depth,
    uint Seed,
    int CandidatesPerPass,
    int ReservoirUpdatesPerPass,
    int ReadbackSplats,
    string OutputDirectory)
{
    public static ReceiptOptions Parse(string[] args)
    {
        var options = new ReceiptOptions(2_000_000, 30, 120, 8, 0xA17EA11u, 2, 50_000, 64, Path.Combine("artifacts", "fractal-splat-receipts"));
        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            string Next() => index + 1 < args.Length ? args[++index] : throw new ArgumentException($"Missing value for {arg}.");
            options = arg switch
            {
                "--splats" => options with { SplatCount = int.Parse(Next()) },
                "--warmup" => options with { WarmupFrames = int.Parse(Next()) },
                "--frames" => options with { MeasuredFrames = int.Parse(Next()) },
                "--depth" => options with { Depth = int.Parse(Next()) },
                "--seed" => options with { Seed = Convert.ToUInt32(Next(), 0) },
                "--candidates" => options with { CandidatesPerPass = int.Parse(Next()) },
                "--reservoir-updates" => options with { ReservoirUpdatesPerPass = int.Parse(Next()) },
                "--readback-splats" => options with { ReadbackSplats = int.Parse(Next()) },
                "--out" => options with { OutputDirectory = Next() },
                _ => throw new ArgumentException($"Unknown receipt option: {arg}"),
            };
        }
        if (options.SplatCount <= 0 || options.WarmupFrames < 0 || options.MeasuredFrames <= 0 || options.Depth <= 0 || options.CandidatesPerPass <= 0 || options.ReservoirUpdatesPerPass <= 0 || options.ReservoirUpdatesPerPass > options.SplatCount || options.ReadbackSplats < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(args), "Splats, frames, depth, candidates, and reservoir updates must be positive; reservoir updates must not exceed splats; warmup/readback must not be negative.");
        }
        return options;
    }
}

internal sealed record GpuFractalSplatReceipt(
    string Adapter,
    int SplatCount,
    int SdfReservoirCount,
    int PbrReservoirCount,
    int RadiosityReservoirCount,
    int CandidatesPerPass,
    int ReservoirUpdatesPerPass,
    double ReservoirFullCoverageFrames,
    int MeasuredFrames,
    int BytesPerSplat,
    int BytesPerReservoir,
    double GpuMillisecondsPerFrame,
    double GpuEquivalentFps,
    double GpuSplatsPerSecond,
    double GpuReservoirCandidatesPerSecond,
    double CpuSubmitAndWaitMillisecondsPerFrame,
    ulong ReadbackChecksum);
