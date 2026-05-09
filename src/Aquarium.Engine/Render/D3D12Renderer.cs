using Aquarium.Engine.Input;
using SharpGen.Runtime;
using Vortice;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;
using Vortice.Mathematics;
using System.Numerics;

namespace Aquarium.Engine.Render;

public sealed class D3D12Renderer : IAquariumRenderer
{
    private const int BackBufferCount = 2;
    private const string SmokeShaderRelativePath = "Render/Shaders/D3D12Smoke.hlsl";

    private readonly IDXGIFactory4 factory;
    private readonly ID3D12Device device;
    private readonly ID3D12CommandQueue commandQueue;
    private readonly IDXGISwapChain3 swapChain;
    private readonly D3D12ResourceRegistry resourceRegistry = new();
    private D3D12DescriptorArena renderTargetViewArena;
    private D3D12DescriptorArena staticShaderDescriptorArena;
    private readonly FrameResources[] frames = new FrameResources[BackBufferCount];
    private readonly ID3D12GraphicsCommandList commandList;
    private readonly ID3D12RootSignature fullscreenRootSignature;
    private readonly ID3D12PipelineState smokePipelineState;
    private readonly ID3D12PipelineState copyPipelineState;
    private readonly ID3D12Fence fence;
    private readonly AutoResetEvent fenceEvent = new(false);
    private D3D12RenderTarget smokeRenderTarget;
    private D3D12RenderTarget diagnosticUavTarget;
    private Viewport viewport;
    private RawRect scissorRect;
    private int width;
    private int height;
    private ulong fenceValue;
    private int frameIndex;
    private GraphicsSettings settings = GraphicsSettings.Default;
    private bool debugUiVisible = true;

    public D3D12Renderer(
        IntPtr windowHandle,
        int width,
        int height,
        string? shaderPath = null,
        GraphicsSettings? graphicsSettings = null,
        Action<string>? startupProgress = null)
    {
        _ = shaderPath;
        ApplyGraphicsSettings(graphicsSettings ?? GraphicsSettings.Default);
        this.width = width;
        this.height = height;
        ReportStartupProgress(startupProgress, "Creating D3D12 device and swapchain");

        factory = DXGI.CreateDXGIFactory2<IDXGIFactory4>(false);
        device = D3D12.D3D12CreateDevice<ID3D12Device>(IntPtr.Zero, FeatureLevel.Level_11_0);
        device.Name = "Aquarium D3D12 Device";
        commandQueue = device.CreateCommandQueue(new CommandQueueDescription(CommandListType.Direct));
        commandQueue.Name = "Aquarium D3D12 Direct Queue";

        var swapChainDescription = new SwapChainDescription1
        {
            BufferCount = BackBufferCount,
            Width = (uint)width,
            Height = (uint)height,
            Format = Format.B8G8R8A8_UNorm,
            BufferUsage = Usage.RenderTargetOutput,
            SwapEffect = SwapEffect.FlipDiscard,
            SampleDescription = new SampleDescription(1, 0),
        };

        using var swapChain1 = factory.CreateSwapChainForHwnd(commandQueue, windowHandle, swapChainDescription);
        swapChain = swapChain1.QueryInterface<IDXGISwapChain3>();
        frameIndex = (int)swapChain.CurrentBackBufferIndex;

        renderTargetViewArena = CreateRenderTargetViewArena();
        staticShaderDescriptorArena = CreateStaticShaderDescriptorArena();
        for (var index = 0; index < frames.Length; index++)
        {
            frames[index] = CreateFrameResources(index);
        }

        CreateRenderTargetViews();
        smokeRenderTarget = CreateSmokeRenderTarget();
        diagnosticUavTarget = CreateDiagnosticUavTarget();
        commandList = device.CreateCommandList<ID3D12GraphicsCommandList>(0, CommandListType.Direct, frames[frameIndex].CommandAllocator, null);
        commandList.Name = "Aquarium D3D12 Graphics Command List";
        commandList.Close();
        ReportStartupProgress(startupProgress, "Creating D3D12 fullscreen smoke pipeline");
        fullscreenRootSignature = CreateFullscreenRootSignature();
        fullscreenRootSignature.Name = "Aquarium D3D12 Fullscreen Root Signature";
        var smokeShaderPath = Path.Combine(AppContext.BaseDirectory, SmokeShaderRelativePath);
        smokePipelineState = CreateSmokePipelineState(smokeShaderPath);
        smokePipelineState.Name = "Aquarium D3D12 Smoke Pipeline";
        copyPipelineState = CreateCopyPipelineState(smokeShaderPath);
        copyPipelineState.Name = "Aquarium D3D12 Copy Pipeline";
        viewport = new Viewport(0.0f, 0.0f, width, height);
        scissorRect = new RawRect(0, 0, width, height);
        fence = device.CreateFence(0);
        fence.Name = "Aquarium D3D12 Frame Fence";
        Console.WriteLine($"D3D12 resource registry: {resourceRegistry.Describe()}");
        Console.WriteLine("D3D12 device and swapchain created.");
    }

    public int RenderDebugMode
    {
        get => settings.RenderDebugMode;
        set => settings = (settings with { RenderDebugMode = value }).Normalized();
    }

    public bool DebugUiVisible
    {
        get => debugUiVisible;
        set => debugUiVisible = value;
    }

    public void UpdateDebugUi(InputState input)
    {
        _ = input;
    }

    public void CycleRenderDebugMode()
    {
        RenderDebugMode = (RenderDebugMode + 1) % (GraphicsSettings.MaxRenderDebugMode + 1);
    }

    public GraphicsSettings CaptureGraphicsSettings()
    {
        return settings.Normalized();
    }

    public void ApplyGraphicsSettings(GraphicsSettings graphicsSettings)
    {
        settings = graphicsSettings.Normalized();
    }

    public void Render(AquariumFrame frame, int width, int height)
    {
        ResizeIfNeeded(width, height);
        var frameResources = frames[frameIndex];
        WaitForFrame(frameResources);
        frameResources.UploadRing.Reset();
        frameResources.TransientShaderDescriptors.Reset();
        var smokeConstants = frameResources.UploadRing.WriteConstant(new SmokeConstants(
            new Vector4(
                0.020f + settings.SceneExposure * 0.08f,
                0.048f + settings.BloomIntensity * 0.55f,
                0.066f + settings.BloomVeilIntensity * 1.2f,
                1.0f),
            new Vector4(frame.TimeSeconds, RenderDebugMode, DebugUiVisible ? 1.0f : 0.0f, 0.0f)));
        frameResources.SmokeConstantsDescriptor = frameResources.TransientShaderDescriptors.Allocate();
        device.CreateConstantBufferView(
            new ConstantBufferViewDescription(smokeConstants.GpuVirtualAddress, smokeConstants.SizeInBytes),
            frameResources.SmokeConstantsDescriptor.Cpu);
        frameResources.DiagnosticUavDescriptor = frameResources.TransientShaderDescriptors.Allocate();
        diagnosticUavTarget.CreateUnorderedAccessView(device, frameResources.DiagnosticUavDescriptor);
        frameResources.CommandAllocator.Reset();
        commandList.Reset(frameResources.CommandAllocator, null);

        commandList.BeginEvent("Aquarium D3D12 Frame");
        ClearBackBuffer(new D3D12PassContext(commandList, frameResources.BackBuffer, frameResources.BackBufferRenderTargetView.Cpu), frame);
        commandList.EndEvent();
        commandList.Close();

        commandQueue.ExecuteCommandList(commandList);
        swapChain.Present(1, PresentFlags.None);
        SignalFrame(frameResources);
        ReportCapacityOncePerSecond(frame.TimeSeconds, frameResources);
        frameIndex = (int)swapChain.CurrentBackBufferIndex;
    }

    public void Dispose()
    {
        WaitForGpu();
        fenceEvent.Dispose();
        fence.Dispose();
        commandList.Dispose();
        copyPipelineState.Dispose();
        smokePipelineState.Dispose();
        fullscreenRootSignature.Dispose();
        diagnosticUavTarget.Dispose();
        smokeRenderTarget.Dispose();
        for (var index = 0; index < frames.Length; index++)
        {
            frames[index].Dispose();
        }
        staticShaderDescriptorArena.Dispose();
        renderTargetViewArena.Dispose();
        swapChain.Dispose();
        commandQueue.Dispose();
        device.Dispose();
        factory.Dispose();
    }

    private void CreateRenderTargetViews()
    {
        for (var index = 0; index < frames.Length; index++)
        {
            var backBuffer = swapChain.GetBuffer<ID3D12Resource>((uint)index);
            var backBufferResource = new D3D12TrackedResource(backBuffer, ResourceStates.Present, $"Aquarium D3D12 Backbuffer {index}", ownsResource: true);
            frames[index].BackBuffer = backBufferResource;
            resourceRegistry.Add($"backbuffer-{index}", backBufferResource);
            frames[index].BackBufferRenderTargetView = renderTargetViewArena.Allocate();
            device.CreateRenderTargetView(frames[index].BackBuffer.Resource, null, frames[index].BackBufferRenderTargetView.Cpu);
        }
    }

    private void ResizeIfNeeded(int newWidth, int newHeight)
    {
        newWidth = Math.Max(1, newWidth);
        newHeight = Math.Max(1, newHeight);
        if (newWidth == width && newHeight == height)
        {
            return;
        }

        WaitForGpu();
        resourceRegistry.RemoveRenderTarget("diagnostic-uav-target");
        resourceRegistry.RemoveRenderTarget("smoke-target");
        diagnosticUavTarget.Dispose();
        smokeRenderTarget.Dispose();
        for (var index = 0; index < frames.Length; index++)
        {
            resourceRegistry.RemoveResource($"backbuffer-{index}");
            frames[index].BackBuffer.Dispose();
        }

        width = newWidth;
        height = newHeight;
        renderTargetViewArena.Dispose();
        staticShaderDescriptorArena.Dispose();
        renderTargetViewArena = CreateRenderTargetViewArena();
        staticShaderDescriptorArena = CreateStaticShaderDescriptorArena();

        swapChain.ResizeBuffers(BackBufferCount, (uint)width, (uint)height, Format.B8G8R8A8_UNorm, SwapChainFlags.None).CheckError();
        frameIndex = (int)swapChain.CurrentBackBufferIndex;
        CreateRenderTargetViews();
        smokeRenderTarget = CreateSmokeRenderTarget();
        diagnosticUavTarget = CreateDiagnosticUavTarget();
        viewport = new Viewport(0.0f, 0.0f, width, height);
        scissorRect = new RawRect(0, 0, width, height);
        Console.WriteLine($"D3D12 resized: {width}x{height}; {resourceRegistry.Describe()}");
    }

    private D3D12RenderTarget CreateSmokeRenderTarget()
    {
        var target = new D3D12RenderTarget(
            device,
            width,
            height,
            Format.B8G8R8A8_UNorm,
            renderTargetViewArena.Allocate(),
            staticShaderDescriptorArena.Allocate(),
            null,
            false,
            new Color4(0.006f, 0.014f, 0.022f, 1.0f),
            "Aquarium D3D12 Smoke Target");
        resourceRegistry.Add("smoke-target", target);
        return target;
    }

    private D3D12RenderTarget CreateDiagnosticUavTarget()
    {
        var target = new D3D12RenderTarget(
            device,
            width,
            height,
            Format.R8G8B8A8_UNorm,
            renderTargetViewArena.Allocate(),
            null,
            null,
            true,
            new Color4(0.0f, 0.0f, 0.0f, 1.0f),
            "Aquarium D3D12 Diagnostic UAV Target");
        resourceRegistry.Add("diagnostic-uav-target", target);
        return target;
    }

    private void ClearBackBuffer(D3D12PassContext context, AquariumFrame frame)
    {
        _ = frame;
        var clearColor = new Color4(
            0.006f + settings.SceneExposure * 0.02f,
            0.014f + settings.BloomIntensity * 0.2f,
            0.022f + settings.BloomVeilIntensity * 0.8f,
            1.0f);

        context.CommandList.BeginEvent("Smoke Diagnostic Pass");
        try
        {
            smokeRenderTarget.Transition(context.CommandList, ResourceStates.RenderTarget);
            diagnosticUavTarget.Transition(context.CommandList, ResourceStates.UnorderedAccess);
            context.CommandList.ClearRenderTargetView(smokeRenderTarget.RenderTargetView.Cpu, clearColor);
            context.CommandList.SetDescriptorHeaps(frames[frameIndex].TransientShaderDescriptors.Heap);
            context.CommandList.SetPipelineState(smokePipelineState);
            context.CommandList.SetGraphicsRootSignature(fullscreenRootSignature);
            context.CommandList.SetGraphicsRootDescriptorTable(0, frames[frameIndex].SmokeConstantsDescriptor.Gpu);
            context.CommandList.SetGraphicsRootDescriptorTable(2, frames[frameIndex].DiagnosticUavDescriptor.Gpu);
            context.CommandList.RSSetViewports(viewport);
            context.CommandList.RSSetScissorRects(scissorRect);
            context.CommandList.OMSetRenderTargets(smokeRenderTarget.RenderTargetView.Cpu, null);
            context.CommandList.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            context.CommandList.DrawInstanced(3, 1, 0, 0);
        }
        finally
        {
            context.CommandList.EndEvent();
        }

        context.CommandList.BeginEvent("Copy Smoke Target To Swapchain");
        try
        {
            var sourceDescriptor = smokeRenderTarget.ShaderResourceView
                ?? throw new InvalidOperationException("Smoke target was created without an SRV.");
            smokeRenderTarget.Transition(context.CommandList, ResourceStates.PixelShaderResource);
            context.BackBuffer.Transition(context.CommandList, ResourceStates.RenderTarget);
            context.CommandList.SetDescriptorHeaps(staticShaderDescriptorArena.Heap);
            context.CommandList.SetPipelineState(copyPipelineState);
            context.CommandList.SetGraphicsRootSignature(fullscreenRootSignature);
            context.CommandList.SetGraphicsRootDescriptorTable(1, sourceDescriptor.Gpu);
            context.CommandList.RSSetViewports(viewport);
            context.CommandList.RSSetScissorRects(scissorRect);
            context.CommandList.OMSetRenderTargets(context.RenderTargetView, null);
            context.CommandList.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            context.CommandList.DrawInstanced(3, 1, 0, 0);
            context.BackBuffer.Transition(context.CommandList, ResourceStates.Present);
        }
        finally
        {
            context.CommandList.EndEvent();
        }
    }

    private void SignalFrame(FrameResources frameResources)
    {
        var signalValue = ++fenceValue;
        commandQueue.Signal(fence, signalValue).CheckError();
        frameResources.FenceValue = signalValue;
    }

    private float lastCapacityReportSecond = -1.0f;

    private void ReportCapacityOncePerSecond(float timeSeconds, FrameResources frameResources)
    {
        if (timeSeconds - lastCapacityReportSecond < 1.0f)
        {
            return;
        }

        lastCapacityReportSecond = timeSeconds;
        Console.WriteLine(
            $"D3D12 capacity: {frameResources.UploadRing.Describe()}; " +
            $"{frameResources.TransientShaderDescriptors.Describe()}; " +
            $"{staticShaderDescriptorArena.Describe()}; " +
            $"{renderTargetViewArena.Describe()}");
    }

    private void WaitForFrame(FrameResources frameResources)
    {
        if (frameResources.FenceValue != 0 && fence.CompletedValue < frameResources.FenceValue)
        {
            fence.SetEventOnCompletion(frameResources.FenceValue, fenceEvent.SafeWaitHandle.DangerousGetHandle()).CheckError();
            fenceEvent.WaitOne();
        }
    }

    private void WaitForGpu()
    {
        var signalValue = ++fenceValue;
        commandQueue.Signal(fence, signalValue).CheckError();
        fence.SetEventOnCompletion(signalValue, fenceEvent.SafeWaitHandle.DangerousGetHandle()).CheckError();
        fenceEvent.WaitOne();
    }

    private static void ReportStartupProgress(Action<string>? startupProgress, string message)
    {
        startupProgress?.Invoke(message);
        Console.WriteLine(message);
    }

    private D3D12DescriptorArena CreateRenderTargetViewArena()
    {
        return new D3D12DescriptorArena(
            device,
            DescriptorHeapType.RenderTargetView,
            32,
            DescriptorHeapFlags.None,
            "Aquarium D3D12 RTV Arena");
    }

    private D3D12DescriptorArena CreateStaticShaderDescriptorArena()
    {
        return new D3D12DescriptorArena(
            device,
            DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView,
            64,
            DescriptorHeapFlags.ShaderVisible,
            "Aquarium D3D12 Static Shader Descriptor Arena");
    }

    private ID3D12RootSignature CreateFullscreenRootSignature()
    {
        var constantBufferRange = new DescriptorRange(
            DescriptorRangeType.ConstantBufferView,
            1,
            0,
            0,
            D3D12.DescriptorRangeOffsetAppend);
        var sourceTextureRange = new DescriptorRange(
            DescriptorRangeType.ShaderResourceView,
            1,
            0,
            0,
            D3D12.DescriptorRangeOffsetAppend);
        var diagnosticUavRange = new DescriptorRange(
            DescriptorRangeType.UnorderedAccessView,
            1,
            1,
            0,
            D3D12.DescriptorRangeOffsetAppend);
        var rootParameters = new[]
        {
            new RootParameter(new RootDescriptorTable([constantBufferRange]), ShaderVisibility.Pixel),
            new RootParameter(new RootDescriptorTable([sourceTextureRange]), ShaderVisibility.Pixel),
            new RootParameter(new RootDescriptorTable([diagnosticUavRange]), ShaderVisibility.Pixel),
        };
        var staticSamplers = new[]
        {
            new StaticSamplerDescription(ShaderVisibility.Pixel, 0, 0),
        };
        var description = new RootSignatureDescription(
            RootSignatureFlags.AllowInputAssemblerInputLayout,
            rootParameters,
            staticSamplers);
        return device.CreateRootSignature(0, in description, RootSignatureVersion.Version1);
    }

    private FrameResources CreateFrameResources(int index)
    {
        var commandAllocator = device.CreateCommandAllocator(CommandListType.Direct);
        commandAllocator.Name = $"Aquarium D3D12 Frame {index} Command Allocator";
        var uploadRing = new D3D12UploadRing(device, 64 * 1024, $"Aquarium D3D12 Frame {index} Upload Ring");
        var transientDescriptors = new D3D12DescriptorArena(
            device,
            DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView,
            64,
            DescriptorHeapFlags.ShaderVisible,
            $"Aquarium D3D12 Frame {index} Transient Shader Descriptor Arena");

        return new FrameResources(commandAllocator, uploadRing, transientDescriptors);
    }

    private ID3D12PipelineState CreateSmokePipelineState(string path)
    {
        var vertexShader = CompileShader(path, "FullscreenTriangleVS", "vs_5_0");
        var pixelShader = CompileShader(path, "D3D12SmokePS", "ps_5_0");
        var description = new GraphicsPipelineStateDescription
        {
            RootSignature = fullscreenRootSignature,
            VertexShader = vertexShader,
            PixelShader = pixelShader,
            BlendState = BlendDescription.Opaque,
            RasterizerState = RasterizerDescription.CullNone,
            DepthStencilState = DepthStencilDescription.None,
            SampleMask = uint.MaxValue,
            PrimitiveTopologyType = PrimitiveTopologyType.Triangle,
            RenderTargetFormats = [Format.B8G8R8A8_UNorm],
            SampleDescription = new SampleDescription(1, 0),
        };

        return device.CreateGraphicsPipelineState(description);
    }

    private ID3D12PipelineState CreateCopyPipelineState(string path)
    {
        var vertexShader = CompileShader(path, "FullscreenTriangleVS", "vs_5_0");
        var pixelShader = CompileShader(path, "D3D12CopyPS", "ps_5_0");
        var description = new GraphicsPipelineStateDescription
        {
            RootSignature = fullscreenRootSignature,
            VertexShader = vertexShader,
            PixelShader = pixelShader,
            BlendState = BlendDescription.Opaque,
            RasterizerState = RasterizerDescription.CullNone,
            DepthStencilState = DepthStencilDescription.None,
            SampleMask = uint.MaxValue,
            PrimitiveTopologyType = PrimitiveTopologyType.Triangle,
            RenderTargetFormats = [Format.B8G8R8A8_UNorm],
            SampleDescription = new SampleDescription(1, 0),
        };

        return device.CreateGraphicsPipelineState(description);
    }

    private static ReadOnlyMemory<byte> CompileShader(string path, string entryPoint, string profile)
    {
        var shaderFlags = ShaderFlags.EnableStrictness;
#if DEBUG
        shaderFlags |= ShaderFlags.Debug | ShaderFlags.SkipOptimization;
#endif

        return Compiler.CompileFromFile(path, entryPoint, profile, shaderFlags, EffectFlags.None);
    }

    private readonly record struct D3D12PassContext(
        ID3D12GraphicsCommandList CommandList,
        D3D12TrackedResource BackBuffer,
        CpuDescriptorHandle RenderTargetView);

    private readonly record struct SmokeConstants(Vector4 Tint, Vector4 Params);

    private sealed class FrameResources(
        ID3D12CommandAllocator commandAllocator,
        D3D12UploadRing uploadRing,
        D3D12DescriptorArena transientShaderDescriptors) : IDisposable
    {
        public ID3D12CommandAllocator CommandAllocator { get; } = commandAllocator;

        public D3D12UploadRing UploadRing { get; } = uploadRing;

        public D3D12DescriptorArena TransientShaderDescriptors { get; } = transientShaderDescriptors;

        public D3D12DescriptorSlot SmokeConstantsDescriptor { get; set; }

        public D3D12DescriptorSlot DiagnosticUavDescriptor { get; set; }

        public D3D12TrackedResource BackBuffer { get; set; } = null!;

        public D3D12DescriptorSlot BackBufferRenderTargetView { get; set; }

        public ulong FenceValue { get; set; }

        public void Dispose()
        {
            BackBuffer.Dispose();
            TransientShaderDescriptors.Dispose();
            UploadRing.Dispose();
            CommandAllocator.Dispose();
        }
    }
}
