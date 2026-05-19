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
var receiptPath = Path.Combine(
    options.OutputDirectory,
    $"fractal-gpu-splat-receipt-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.json");
File.WriteAllText(receiptPath, JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));

Console.WriteLine("=== Aquarium Perfect Machine GPU Splat Receipt ===");
Console.WriteLine($"adapter: {receipt.Adapter}");
Console.WriteLine($"splats: {receipt.SplatCount:N0}");
Console.WriteLine($"frames: {receipt.MeasuredFrames}");
Console.WriteLine($"shader: D3D12 compute, GPU-resident RWStructuredBuffer");
Console.WriteLine($"packed bytes/splat: {receipt.BytesPerSplat}");
Console.WriteLine($"gpu ms/frame: {receipt.GpuMillisecondsPerFrame:0.000}");
Console.WriteLine($"gpu equivalent fps: {receipt.GpuEquivalentFps:0.0}");
Console.WriteLine($"gpu splats/sec: {receipt.GpuSplatsPerSecond:N0}");
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
    private readonly ID3D12PipelineState pipelineState;
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
        pipelineState = CreatePipelineState(device, rootSignature);
    }

    public GpuFractalSplatReceipt Run(ReceiptOptions options)
    {
        var stride = Marshal.SizeOf<AquariumPackedFractalSdfSplat3D>();
        var splatBytes = checked((ulong)stride * (ulong)options.SplatCount);
        using var splats = device.CreateCommittedResource(
            HeapType.Default,
            ResourceDescription.Buffer(splatBytes, ResourceFlags.AllowUnorderedAccess),
            ResourceStates.UnorderedAccess,
            null);
        splats.Name = "Aquarium Fractal Receipt GPU Splat Buffer";

        var readbackBytes = (ulong)Math.Min(options.ReadbackSplats, options.SplatCount) * (ulong)stride;
        using var readback = device.CreateCommittedResource(
            HeapType.Readback,
            ResourceDescription.Buffer(Math.Max(readbackBytes, 1)),
            ResourceStates.CopyDest,
            null);
        readback.Name = "Aquarium Fractal Receipt Readback";

        using var queryHeap = device.CreateQueryHeap<ID3D12QueryHeap>(new QueryHeapDescription(QueryHeapType.Timestamp, 2u));
        using var queryReadback = device.CreateCommittedResource(
            HeapType.Readback,
            ResourceDescription.Buffer(16),
            ResourceStates.CopyDest,
            null);

        queue.GetTimestampFrequency(out var timestampFrequency);
        var measuredGpuTicks = 0UL;
        var measuredCpuTicks = 0L;
        var measuredFrames = 0;

        for (var frame = 0; frame < options.WarmupFrames + options.MeasuredFrames; frame++)
        {
            allocator.Reset();
            commandList.Reset(allocator, pipelineState);
            commandList.SetComputeRootSignature(rootSignature);
            commandList.SetPipelineState(pipelineState);
            commandList.SetComputeRoot32BitConstant(0, (uint)options.SplatCount, 0);
            commandList.SetComputeRoot32BitConstant(0, (uint)frame, 1);
            commandList.SetComputeRoot32BitConstant(0, (uint)options.Depth, 2);
            commandList.SetComputeRoot32BitConstant(0, options.Seed, 3);
            commandList.SetComputeRootUnorderedAccessView(1, splats.GPUVirtualAddress);
            commandList.EndQuery(queryHeap, QueryType.Timestamp, 0);
            commandList.Dispatch((uint)((options.SplatCount + ThreadGroupSize - 1) / ThreadGroupSize), 1, 1);
            commandList.EndQuery(queryHeap, QueryType.Timestamp, 1);
            commandList.ResolveQueryData(queryHeap, QueryType.Timestamp, 0, 2, queryReadback, 0);

            if (frame == options.WarmupFrames + options.MeasuredFrames - 1 && readbackBytes > 0)
            {
                commandList.ResourceBarrier(ResourceBarrier.BarrierTransition(splats, ResourceStates.UnorderedAccess, ResourceStates.CopySource));
                commandList.CopyBufferRegion(readback, 0, splats, 0, readbackBytes);
                commandList.ResourceBarrier(ResourceBarrier.BarrierTransition(splats, ResourceStates.CopySource, ResourceStates.UnorderedAccess));
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

        var checksum = readbackBytes == 0 ? 0UL : Checksum(readback, (int)readbackBytes);
        var gpuSeconds = measuredGpuTicks / (double)timestampFrequency;
        var cpuSeconds = measuredCpuTicks / (double)Stopwatch.Frequency;
        var gpuMsPerFrame = gpuSeconds * 1000.0 / Math.Max(measuredFrames, 1);
        var cpuMsPerFrame = cpuSeconds * 1000.0 / Math.Max(measuredFrames, 1);
        var gpuSplatsPerSecond = (options.SplatCount * (double)measuredFrames) / Math.Max(gpuSeconds, 1.0e-12);

        return new GpuFractalSplatReceipt(
            adapterName,
            options.SplatCount,
            measuredFrames,
            stride,
            gpuMsPerFrame,
            1000.0 / Math.Max(gpuMsPerFrame, 1.0e-12),
            gpuSplatsPerSecond,
            cpuMsPerFrame,
            checksum);
    }

    public void Dispose()
    {
        WaitForGpu();
        pipelineState.Dispose();
        rootSignature.Dispose();
        fence.Dispose();
        commandList.Dispose();
        allocator.Dispose();
        queue.Dispose();
        device.Dispose();
        fenceEvent.Dispose();
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
            new RootParameter(new RootConstants(0, 0, 4), ShaderVisibility.All),
            new RootParameter(RootParameterType.UnorderedAccessView, new RootDescriptor(0, 0), ShaderVisibility.All),
        };
        var description = new RootSignatureDescription(RootSignatureFlags.None, rootParameters, []);
        return device.CreateRootSignature(0, in description, RootSignatureVersion.Version1);
    }

    private static ID3D12PipelineState CreatePipelineState(ID3D12Device device, ID3D12RootSignature rootSignature)
    {
        var shader = Compiler.Compile(ShaderSource, "D3D12FractalSplatReceiptCS", "receipt.hlsl", "cs_5_0", ShaderFlags.OptimizationLevel3, EffectFlags.None);
        return device.CreateComputePipelineState(new ComputePipelineStateDescription
        {
            RootSignature = rootSignature,
            ComputeShader = shader,
        });
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
struct PackedSplat
{
    float4 centerRadius;
    float4 orientation;
    float4 radiiFalloff;
    float4 materialConfidence;
    float4 key;
};

cbuffer ReceiptConstants : register(b0)
{
    uint SplatCount;
    uint FrameIndex;
    uint Depth;
    uint Seed;
};

RWStructuredBuffer<PackedSplat> Splats : register(u0);

uint Hash(uint x)
{
    x ^= x >> 16;
    x *= 747796405u;
    x ^= x >> 16;
    x *= 2891336453u;
    x ^= x >> 16;
    return x;
}

[numthreads(256, 1, 1)]
void D3D12FractalSplatReceiptCS(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    uint index = dispatchThreadId.x;
    if (index >= SplatCount)
    {
        return;
    }

    uint n = index ^ Seed;
    float3 p = float3(0.0, 0.0, 0.0);
    float scale = 1.0;
    [loop]
    for (uint depth = 0; depth < Depth; depth++)
    {
        uint branch = (n >> (depth * 2u)) & 3u;
        float2 dir = branch == 0u ? float2(1.0, 0.0)
            : branch == 1u ? float2(-0.42, 0.91)
            : branch == 2u ? float2(-0.76, -0.65)
            : float2(0.72, -0.69);
        scale *= 0.535;
        p.xy += dir * scale;
        p.z += ((float)branch - 1.5) * scale * 0.19;
        n = Hash(n + branch + FrameIndex + depth * 17u);
    }

    float radius = max(scale * 0.75, 0.0001);
    uint h = Hash(index + FrameIndex * 1664525u + Seed);
    PackedSplat splat;
    splat.centerRadius = float4(p, radius);
    splat.orientation = float4(0.0, 0.0, 0.0, 1.0);
    splat.radiiFalloff = float4(radius, radius * 0.72, radius * 0.45, 4.0);
    splat.materialConfidence = float4((float)(h & 1023u) / 1023.0, 1.0, 0.0, 1.0);
    splat.key = float4((float)index, (float)FrameIndex, (float)Depth, asfloat(h));
    Splats[index] = splat;
}
""";
}

internal sealed record ReceiptOptions(
    int SplatCount,
    int WarmupFrames,
    int MeasuredFrames,
    int Depth,
    uint Seed,
    int ReadbackSplats,
    string OutputDirectory)
{
    public static ReceiptOptions Parse(string[] args)
    {
        var options = new ReceiptOptions(
            SplatCount: 2_000_000,
            WarmupFrames: 30,
            MeasuredFrames: 120,
            Depth: 8,
            Seed: 0xA17EA11u,
            ReadbackSplats: 64,
            OutputDirectory: Path.Combine("artifacts", "fractal-splat-receipts"));

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
                "--readback-splats" => options with { ReadbackSplats = int.Parse(Next()) },
                "--out" => options with { OutputDirectory = Next() },
                _ => throw new ArgumentException($"Unknown receipt option: {arg}"),
            };
        }

        if (options.SplatCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(SplatCount), options.SplatCount, "Splat count must be positive.");
        }

        if (options.WarmupFrames < 0 || options.MeasuredFrames <= 0 || options.Depth <= 0 || options.ReadbackSplats < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(args), "Warmup must be nonnegative; frames/depth must be positive; readback splats must be nonnegative.");
        }

        return options;
    }
}

internal sealed record GpuFractalSplatReceipt(
    string Adapter,
    int SplatCount,
    int MeasuredFrames,
    int BytesPerSplat,
    double GpuMillisecondsPerFrame,
    double GpuEquivalentFps,
    double GpuSplatsPerSecond,
    double CpuSubmitAndWaitMillisecondsPerFrame,
    ulong ReadbackChecksum);
