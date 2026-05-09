using Aquarium.Engine.Input;
using SharpGen.Runtime;
using Vortice;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Aquarium.Engine.Render;

public sealed class D3D12Renderer : IAquariumRenderer
{
    private const int BackBufferCount = 2;
    private const string SmokeShaderRelativePath = "Render/Shaders/D3D12Smoke.hlsl";

    private readonly IDXGIFactory4 factory;
    private readonly ID3D12Device device;
    private readonly ID3D12CommandQueue commandQueue;
    private readonly IDXGISwapChain3 swapChain;
    private readonly ID3D12DescriptorHeap renderTargetViewHeap;
    private readonly FrameResources[] frames = new FrameResources[BackBufferCount];
    private readonly ID3D12GraphicsCommandList commandList;
    private readonly ID3D12RootSignature fullscreenRootSignature;
    private readonly ID3D12PipelineState smokePipelineState;
    private readonly ID3D12Fence fence;
    private readonly AutoResetEvent fenceEvent = new(false);
    private readonly int renderTargetViewDescriptorSize;
    private readonly Viewport viewport;
    private readonly RawRect scissorRect;
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
        ReportStartupProgress(startupProgress, "Creating D3D12 device and swapchain");

        factory = DXGI.CreateDXGIFactory2<IDXGIFactory4>(false);
        device = D3D12.D3D12CreateDevice<ID3D12Device>(IntPtr.Zero, FeatureLevel.Level_11_0);
        commandQueue = device.CreateCommandQueue(new CommandQueueDescription(CommandListType.Direct));

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

        renderTargetViewHeap = device.CreateDescriptorHeap(new DescriptorHeapDescription(DescriptorHeapType.RenderTargetView, BackBufferCount));
        renderTargetViewDescriptorSize = (int)device.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView);
        for (var index = 0; index < frames.Length; index++)
        {
            frames[index] = new FrameResources(device.CreateCommandAllocator(CommandListType.Direct));
        }

        CreateRenderTargetViews();
        commandList = device.CreateCommandList<ID3D12GraphicsCommandList>(0, CommandListType.Direct, frames[frameIndex].CommandAllocator, null);
        commandList.Close();
        ReportStartupProgress(startupProgress, "Creating D3D12 fullscreen smoke pipeline");
        fullscreenRootSignature = CreateFullscreenRootSignature();
        smokePipelineState = CreateSmokePipelineState(shaderPath ?? Path.Combine(AppContext.BaseDirectory, SmokeShaderRelativePath));
        viewport = new Viewport(0.0f, 0.0f, width, height);
        scissorRect = new RawRect(0, 0, width, height);
        fence = device.CreateFence(0);
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

    public void Render(AquariumFrame frame)
    {
        var frameResources = frames[frameIndex];
        WaitForFrame(frameResources);
        frameResources.CommandAllocator.Reset();
        commandList.Reset(frameResources.CommandAllocator, null);

        ClearBackBuffer(new D3D12PassContext(commandList, frameResources.BackBuffer, GetRenderTargetViewHandle(frameIndex)), frame);
        commandList.Close();

        commandQueue.ExecuteCommandList(commandList);
        swapChain.Present(1, PresentFlags.None);
        SignalFrame(frameResources);
        frameIndex = (int)swapChain.CurrentBackBufferIndex;
    }

    public void Dispose()
    {
        WaitForGpu();
        fenceEvent.Dispose();
        fence.Dispose();
        commandList.Dispose();
        for (var index = 0; index < frames.Length; index++)
        {
            frames[index].Dispose();
        }
        renderTargetViewHeap.Dispose();
        swapChain.Dispose();
        commandQueue.Dispose();
        device.Dispose();
        factory.Dispose();
    }

    private void CreateRenderTargetViews()
    {
        for (var index = 0; index < frames.Length; index++)
        {
            frames[index].BackBuffer = swapChain.GetBuffer<ID3D12Resource>((uint)index);
            device.CreateRenderTargetView(frames[index].BackBuffer, null, GetRenderTargetViewHandle(index));
        }
    }

    private void ClearBackBuffer(D3D12PassContext context, AquariumFrame frame)
    {
        _ = frame;
        var clearColor = new Color4(
            0.006f + settings.SceneExposure * 0.02f,
            0.014f + settings.BloomIntensity * 0.2f,
            0.022f + settings.BloomVeilIntensity * 0.8f,
            1.0f);

        context.CommandList.ResourceBarrier(ResourceBarrier.BarrierTransition(context.BackBuffer, ResourceStates.Present, ResourceStates.RenderTarget));
        context.CommandList.ClearRenderTargetView(context.RenderTargetView, clearColor);
        context.CommandList.SetPipelineState(smokePipelineState);
        context.CommandList.SetGraphicsRootSignature(fullscreenRootSignature);
        context.CommandList.RSSetViewports(viewport);
        context.CommandList.RSSetScissorRects(scissorRect);
        context.CommandList.OMSetRenderTargets(context.RenderTargetView, null);
        context.CommandList.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        context.CommandList.DrawInstanced(3, 1, 0, 0);
        context.CommandList.ResourceBarrier(ResourceBarrier.BarrierTransition(context.BackBuffer, ResourceStates.RenderTarget, ResourceStates.Present));
    }

    private CpuDescriptorHandle GetRenderTargetViewHandle(int index)
    {
        return renderTargetViewHeap.GetCPUDescriptorHandleForHeapStart() + (index * renderTargetViewDescriptorSize);
    }

    private void SignalFrame(FrameResources frameResources)
    {
        var signalValue = ++fenceValue;
        commandQueue.Signal(fence, signalValue).CheckError();
        frameResources.FenceValue = signalValue;
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

    private ID3D12RootSignature CreateFullscreenRootSignature()
    {
        var description = new RootSignatureDescription(
            RootSignatureFlags.AllowInputAssemblerInputLayout,
            [],
            []);
        return device.CreateRootSignature(0, in description, RootSignatureVersion.Version1);
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
        ID3D12Resource BackBuffer,
        CpuDescriptorHandle RenderTargetView);

    private sealed class FrameResources(ID3D12CommandAllocator commandAllocator) : IDisposable
    {
        public ID3D12CommandAllocator CommandAllocator { get; } = commandAllocator;

        public ID3D12Resource BackBuffer { get; set; } = null!;

        public ulong FenceValue { get; set; }

        public void Dispose()
        {
            BackBuffer.Dispose();
            CommandAllocator.Dispose();
        }
    }
}
