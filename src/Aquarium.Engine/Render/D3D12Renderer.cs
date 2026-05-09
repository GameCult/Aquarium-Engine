using Aquarium.Engine.Input;
using SharpGen.Runtime;
using Vortice;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;
using Vortice.Mathematics;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Aquarium.Engine.Render;

public sealed class D3D12Renderer : IAquariumRenderer
{
    private const int BackBufferCount = 2;
    private const int GridHeightTextureSize = 128;
    private const int PlanetCount = 5;
    private const int GridHeightBrushCount = PlanetCount + 1;
    private const string GridShaderRelativePath = "Render/Shaders/D3D12Grid.hlsl";
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
    private readonly ID3D12PipelineState gridHeightBasePipelineState;
    private readonly ID3D12PipelineState gridHeightBrushPipelineState;
    private readonly ID3D12PipelineState smokePipelineState;
    private readonly ID3D12PipelineState copyPipelineState;
    private readonly ID3D12PipelineState gridHeightDebugPipelineState;
    private readonly ID3D12Fence fence;
    private readonly AutoResetEvent fenceEvent = new(false);
    private D3D12RenderTarget gridHeightRenderTarget;
    private D3D12RenderTarget smokeRenderTarget;
    private D3D12RenderTarget diagnosticUavTarget;
    private Viewport viewport;
    private RawRect scissorRect;
    private int width;
    private int height;
    private ulong fenceValue;
    private int temporalFrameIndex;
    private int frameIndex;
    private Vector3 previousCameraPosition;
    private Vector2 previousGridCenter;
    private float previousGridRadius = 0.001f;
    private float previousTimeSeconds;
    private GridHeightBrushConstants gridHeightBrushConstants;
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
        gridHeightRenderTarget = CreateGridHeightRenderTarget();
        smokeRenderTarget = CreateSmokeRenderTarget();
        diagnosticUavTarget = CreateDiagnosticUavTarget();
        commandList = device.CreateCommandList<ID3D12GraphicsCommandList>(0, CommandListType.Direct, frames[frameIndex].CommandAllocator, null);
        commandList.Name = "Aquarium D3D12 Graphics Command List";
        commandList.Close();
        ReportStartupProgress(startupProgress, "Creating D3D12 fullscreen smoke pipeline");
        fullscreenRootSignature = CreateFullscreenRootSignature();
        fullscreenRootSignature.Name = "Aquarium D3D12 Fullscreen Root Signature";
        var gridShaderPath = Path.Combine(AppContext.BaseDirectory, GridShaderRelativePath);
        var smokeShaderPath = Path.Combine(AppContext.BaseDirectory, SmokeShaderRelativePath);
        gridHeightBasePipelineState = CreateGridHeightBasePipelineState(gridShaderPath);
        gridHeightBasePipelineState.Name = "Aquarium D3D12 Grid Height Base Pipeline";
        gridHeightBrushPipelineState = CreateGridHeightBrushPipelineState(gridShaderPath);
        gridHeightBrushPipelineState.Name = "Aquarium D3D12 Grid Height Brush Pipeline";
        smokePipelineState = CreateSmokePipelineState(smokeShaderPath);
        smokePipelineState.Name = "Aquarium D3D12 Smoke Pipeline";
        copyPipelineState = CreateCopyPipelineState(smokeShaderPath);
        copyPipelineState.Name = "Aquarium D3D12 Copy Pipeline";
        gridHeightDebugPipelineState = CreateGridHeightDebugPipelineState(smokeShaderPath);
        gridHeightDebugPipelineState.Name = "Aquarium D3D12 Grid Height Debug Pipeline";
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
        var gridOrigin = new Vector3(frame.Grid.Center.X, frame.Grid.Center.Y, 0.0f);
        var farDistance = Vector3.Distance(frame.CameraPosition, gridOrigin) + MathF.Max(frame.Grid.Radius, 0.001f);
        if (temporalFrameIndex == 0)
        {
            previousCameraPosition = frame.CameraPosition;
            previousGridCenter = frame.Grid.Center;
            previousGridRadius = frame.Grid.Radius;
            previousTimeSeconds = frame.TimeSeconds;
        }

        BuildGridHeightBrushes(frame);
        var frameConstants = frameResources.UploadRing.WriteConstant(new FrameConstants(
            new Vector2(width, height),
            frame.TimeSeconds,
            frame.Grid.Radius,
            frame.CameraPosition,
            farDistance,
            frame.Grid.Center,
            temporalFrameIndex,
            previousTimeSeconds,
            previousCameraPosition,
            previousGridRadius,
            previousGridCenter,
            Vector2.Zero,
            Vector2.Zero,
            RenderDebugMode,
            settings.SceneExposure,
            settings.BloomIntensity,
            settings.BloomVeilIntensity,
            settings.MediumCompositeIntensity,
            settings.MediumDebugStep,
            Vector3.Zero));
        frameResources.FrameConstantsDescriptor = frameResources.TransientShaderDescriptors.Allocate();
        device.CreateConstantBufferView(
            new ConstantBufferViewDescription(frameConstants.GpuVirtualAddress, frameConstants.SizeInBytes),
            frameResources.FrameConstantsDescriptor.Cpu);
        var gridBrushConstants = frameResources.UploadRing.WriteConstant(gridHeightBrushConstants);
        frameResources.GridBrushConstantsDescriptor = frameResources.TransientShaderDescriptors.Allocate();
        device.CreateConstantBufferView(
            new ConstantBufferViewDescription(gridBrushConstants.GpuVirtualAddress, gridBrushConstants.SizeInBytes),
            frameResources.GridBrushConstantsDescriptor.Cpu);

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
        RenderGridHeight(commandList, frameResources);
        ClearBackBuffer(new D3D12PassContext(commandList, frameResources.BackBuffer, frameResources.BackBufferRenderTargetView.Cpu), frame);
        commandList.EndEvent();
        commandList.Close();

        commandQueue.ExecuteCommandList(commandList);
        swapChain.Present(1, PresentFlags.None);
        SignalFrame(frameResources);
        ReportCapacityOncePerSecond(frame.TimeSeconds, frameResources);
        previousCameraPosition = frame.CameraPosition;
        previousGridCenter = frame.Grid.Center;
        previousGridRadius = frame.Grid.Radius;
        previousTimeSeconds = frame.TimeSeconds;
        temporalFrameIndex++;
        frameIndex = (int)swapChain.CurrentBackBufferIndex;
    }

    public void Dispose()
    {
        WaitForGpu();
        fenceEvent.Dispose();
        fence.Dispose();
        commandList.Dispose();
        gridHeightDebugPipelineState.Dispose();
        copyPipelineState.Dispose();
        smokePipelineState.Dispose();
        gridHeightBrushPipelineState.Dispose();
        gridHeightBasePipelineState.Dispose();
        fullscreenRootSignature.Dispose();
        diagnosticUavTarget.Dispose();
        smokeRenderTarget.Dispose();
        gridHeightRenderTarget.Dispose();
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
        resourceRegistry.RemoveRenderTarget("grid-height-target");
        diagnosticUavTarget.Dispose();
        smokeRenderTarget.Dispose();
        gridHeightRenderTarget.Dispose();
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
        gridHeightRenderTarget = CreateGridHeightRenderTarget();
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

    private D3D12RenderTarget CreateGridHeightRenderTarget()
    {
        var target = new D3D12RenderTarget(
            device,
            GridHeightTextureSize,
            GridHeightTextureSize,
            Format.R16G16B16A16_Float,
            renderTargetViewArena.Allocate(),
            staticShaderDescriptorArena.Allocate(),
            null,
            false,
            new Color4(0.0f, 0.0f, 0.0f, 1.0f),
            "Aquarium D3D12 Grid Height Target");
        resourceRegistry.Add("grid-height-target", target);
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
            var showGridHeight = RenderDebugMode == 11;
            var sourceTarget = showGridHeight ? gridHeightRenderTarget : smokeRenderTarget;
            var sourceDescriptor = sourceTarget.ShaderResourceView
                ?? throw new InvalidOperationException($"{sourceTarget.Resource.Name} was created without an SRV.");
            var presentPipelineState = showGridHeight ? gridHeightDebugPipelineState : copyPipelineState;
            smokeRenderTarget.Transition(context.CommandList, ResourceStates.PixelShaderResource);
            sourceTarget.Transition(context.CommandList, ResourceStates.PixelShaderResource);
            context.BackBuffer.Transition(context.CommandList, ResourceStates.RenderTarget);
            context.CommandList.SetDescriptorHeaps(staticShaderDescriptorArena.Heap);
            context.CommandList.SetPipelineState(presentPipelineState);
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

    private void RenderGridHeight(ID3D12GraphicsCommandList activeCommandList, FrameResources frameResources)
    {
        var viewport = new Viewport(0.0f, 0.0f, GridHeightTextureSize, GridHeightTextureSize);
        var scissorRect = new RawRect(0, 0, GridHeightTextureSize, GridHeightTextureSize);

        activeCommandList.BeginEvent("Grid Height Pass");
        try
        {
            gridHeightRenderTarget.Transition(activeCommandList, ResourceStates.RenderTarget);
            activeCommandList.ClearRenderTargetView(gridHeightRenderTarget.RenderTargetView.Cpu, new Color4(0.0f, 0.0f, 0.0f, 1.0f));
            activeCommandList.SetDescriptorHeaps(frameResources.TransientShaderDescriptors.Heap);
            activeCommandList.SetPipelineState(gridHeightBasePipelineState);
            activeCommandList.SetGraphicsRootSignature(fullscreenRootSignature);
            activeCommandList.SetGraphicsRootDescriptorTable(0, frameResources.FrameConstantsDescriptor.Gpu);
            activeCommandList.RSSetViewports(viewport);
            activeCommandList.RSSetScissorRects(scissorRect);
            activeCommandList.OMSetRenderTargets(gridHeightRenderTarget.RenderTargetView.Cpu, null);
            activeCommandList.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            activeCommandList.DrawInstanced(3, 1, 0, 0);
            activeCommandList.SetPipelineState(gridHeightBrushPipelineState);
            activeCommandList.SetGraphicsRootDescriptorTable(3, frameResources.GridBrushConstantsDescriptor.Gpu);
            activeCommandList.DrawInstanced(6, GridHeightBrushCount, 0, 0);
        }
        finally
        {
            activeCommandList.EndEvent();
        }
    }

    private void BuildGridHeightBrushes(AquariumFrame frame)
    {
        SetGridHeightBrush(
            0,
            new Vector2(0.0f, 0.0f),
            8.5f,
            2.85f,
            -1.34f,
            0.055f,
            1.2f,
            0.74f);

        for (var index = 0; index < PlanetCount; index++)
        {
            var radius = PlanetRadius(index);
            var center = PlanetCenter(index, frame.TimeSeconds, radius);
            SetGridHeightBrush(
                index + 1,
                new Vector2(center.X, center.Y),
                3.8f + radius * 2.5f,
                2.1f,
                -0.42f,
                0.022f,
                2.4f,
                1.35f);
        }
    }

    private void SetGridHeightBrush(
        int index,
        Vector2 center,
        float radius,
        float power,
        float amplitude,
        float waveAmplitude,
        float waveFrequency,
        float waveSpeed)
    {
        var centerRadius = new Vector4(center, radius, 0.0f);
        var shape = new Vector4(power, amplitude, 0.0f, 0.0f);
        var wave = new Vector4(waveAmplitude, waveFrequency, waveSpeed, 0.0f);
        gridHeightBrushConstants.Set(index, centerRadius, shape, wave);
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
        var gridBrushRange = new DescriptorRange(
            DescriptorRangeType.ConstantBufferView,
            1,
            1,
            0,
            D3D12.DescriptorRangeOffsetAppend);
        var rootParameters = new[]
        {
            new RootParameter(new RootDescriptorTable([constantBufferRange]), ShaderVisibility.All),
            new RootParameter(new RootDescriptorTable([sourceTextureRange]), ShaderVisibility.Pixel),
            new RootParameter(new RootDescriptorTable([diagnosticUavRange]), ShaderVisibility.Pixel),
            new RootParameter(new RootDescriptorTable([gridBrushRange]), ShaderVisibility.All),
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
        return CreateFullscreenPipelineState(path, "FullscreenTriangleVS", "D3D12SmokePS", Format.B8G8R8A8_UNorm);
    }

    private ID3D12PipelineState CreateCopyPipelineState(string path)
    {
        return CreateFullscreenPipelineState(path, "FullscreenTriangleVS", "D3D12CopyPS", Format.B8G8R8A8_UNorm);
    }

    private ID3D12PipelineState CreateGridHeightDebugPipelineState(string path)
    {
        return CreateFullscreenPipelineState(path, "FullscreenTriangleVS", "D3D12GridHeightDebugPS", Format.B8G8R8A8_UNorm);
    }

    private ID3D12PipelineState CreateGridHeightBasePipelineState(string path)
    {
        return CreateFullscreenPipelineState(path, "FullscreenTriangleVS", "D3D12GridHeightBasePS", Format.R16G16B16A16_Float);
    }

    private ID3D12PipelineState CreateGridHeightBrushPipelineState(string path)
    {
        return CreateFullscreenPipelineState(
            path,
            "D3D12GridHeightBrushVS",
            "D3D12GridHeightBrushPS",
            Format.R16G16B16A16_Float,
            new BlendDescription(Blend.One, Blend.One, Blend.One, Blend.One));
    }

    private ID3D12PipelineState CreateFullscreenPipelineState(
        string path,
        string vertexEntryPoint,
        string pixelEntryPoint,
        Format renderTargetFormat,
        BlendDescription? blendDescription = null)
    {
        var vertexShader = CompileShader(path, vertexEntryPoint, "vs_5_0");
        var pixelShader = CompileShader(path, pixelEntryPoint, "ps_5_0");
        var description = new GraphicsPipelineStateDescription
        {
            RootSignature = fullscreenRootSignature,
            VertexShader = vertexShader,
            PixelShader = pixelShader,
            BlendState = blendDescription ?? BlendDescription.Opaque,
            RasterizerState = RasterizerDescription.CullNone,
            DepthStencilState = DepthStencilDescription.None,
            SampleMask = uint.MaxValue,
            PrimitiveTopologyType = PrimitiveTopologyType.Triangle,
            RenderTargetFormats = [renderTargetFormat],
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

    private static float PlanetRadius(int index)
    {
        return Lerp(0.34f, 0.62f, Hash21(index, 19.7f));
    }

    private static Vector3 PlanetCenter(int index, float timeSeconds, float radius)
    {
        var f = (float)index;
        var angle = f * 0.8975979f + timeSeconds * (0.08f + 0.011f * f);
        var orbitRadius = 4.1f + f * 0.77f;
        return new Vector3(
            MathF.Cos(angle) * orbitRadius,
            MathF.Sin(angle) * orbitRadius,
            1.15f + radius * 0.72f);
    }

    private static float Hash21(float x, float y)
    {
        x = Frac(x * 123.34f);
        y = Frac(y * 456.21f);
        var d = x * (x + 45.32f) + y * (y + 45.32f);
        x += d;
        y += d;
        return Frac(x * y);
    }

    private static float Frac(float value)
    {
        return value - MathF.Floor(value);
    }

    private static float Lerp(float a, float b, float t)
    {
        return a + (b - a) * t;
    }

    private readonly record struct D3D12PassContext(
        ID3D12GraphicsCommandList CommandList,
        D3D12TrackedResource BackBuffer,
        CpuDescriptorHandle RenderTargetView);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct SmokeConstants(Vector4 Tint, Vector4 Params);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct FrameConstants(
        Vector2 Resolution,
        float TimeSeconds,
        float GridRadius,
        Vector3 CameraPosition,
        float FarDistance,
        Vector2 GridCenter,
        float FrameIndex,
        float PreviousTimeSeconds,
        Vector3 PreviousCameraPosition,
        float PreviousGridRadius,
        Vector2 PreviousGridCenter,
        Vector2 JitterPixels,
        Vector2 PreviousJitterPixels,
        float RenderDebugMode,
        float Exposure,
        float BloomIntensity,
        float BloomVeilIntensity,
        float MediumCompositeIntensity,
        float MediumDebugStep,
        Vector3 PresentationPadding);

    [StructLayout(LayoutKind.Sequential)]
    private record struct GridHeightBrushConstants(
        Vector4 CenterRadius0,
        Vector4 CenterRadius1,
        Vector4 CenterRadius2,
        Vector4 CenterRadius3,
        Vector4 CenterRadius4,
        Vector4 CenterRadius5,
        Vector4 Shape0,
        Vector4 Shape1,
        Vector4 Shape2,
        Vector4 Shape3,
        Vector4 Shape4,
        Vector4 Shape5,
        Vector4 Wave0,
        Vector4 Wave1,
        Vector4 Wave2,
        Vector4 Wave3,
        Vector4 Wave4,
        Vector4 Wave5)
    {
        public void Set(int index, Vector4 centerRadius, Vector4 shape, Vector4 wave)
        {
            switch (index)
            {
                case 0:
                    CenterRadius0 = centerRadius;
                    Shape0 = shape;
                    Wave0 = wave;
                    break;
                case 1:
                    CenterRadius1 = centerRadius;
                    Shape1 = shape;
                    Wave1 = wave;
                    break;
                case 2:
                    CenterRadius2 = centerRadius;
                    Shape2 = shape;
                    Wave2 = wave;
                    break;
                case 3:
                    CenterRadius3 = centerRadius;
                    Shape3 = shape;
                    Wave3 = wave;
                    break;
                case 4:
                    CenterRadius4 = centerRadius;
                    Shape4 = shape;
                    Wave4 = wave;
                    break;
                case 5:
                    CenterRadius5 = centerRadius;
                    Shape5 = shape;
                    Wave5 = wave;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(index), index, "Grid height brush index is outside the fixed brush table.");
            }
        }
    }

    private sealed class FrameResources(
        ID3D12CommandAllocator commandAllocator,
        D3D12UploadRing uploadRing,
        D3D12DescriptorArena transientShaderDescriptors) : IDisposable
    {
        public ID3D12CommandAllocator CommandAllocator { get; } = commandAllocator;

        public D3D12UploadRing UploadRing { get; } = uploadRing;

        public D3D12DescriptorArena TransientShaderDescriptors { get; } = transientShaderDescriptors;

        public D3D12DescriptorSlot FrameConstantsDescriptor { get; set; }

        public D3D12DescriptorSlot GridBrushConstantsDescriptor { get; set; }

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
