using Aquarium.Engine.Input;
using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Aquarium.Engine.Render;

public sealed class D3D12Renderer : IAquariumRenderer
{
    private const int BackBufferCount = 2;

    private readonly IDXGIFactory4 factory;
    private readonly ID3D12Device device;
    private readonly ID3D12CommandQueue commandQueue;
    private readonly IDXGISwapChain3 swapChain;
    private readonly ID3D12DescriptorHeap renderTargetViewHeap;
    private readonly ID3D12Resource[] renderTargets = new ID3D12Resource[BackBufferCount];
    private readonly ID3D12CommandAllocator commandAllocator;
    private readonly ID3D12GraphicsCommandList commandList;
    private readonly ID3D12Fence fence;
    private readonly AutoResetEvent fenceEvent = new(false);
    private readonly int renderTargetViewDescriptorSize;
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
        CreateRenderTargetViews();

        commandAllocator = device.CreateCommandAllocator(CommandListType.Direct);
        commandList = device.CreateCommandList<ID3D12GraphicsCommandList>(0, CommandListType.Direct, commandAllocator, null);
        commandList.Close();
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
        var clearColor = new Color4(
            0.006f + settings.SceneExposure * 0.02f,
            0.014f + settings.BloomIntensity * 0.2f,
            0.022f + settings.BloomVeilIntensity * 0.8f,
            1.0f);

        commandAllocator.Reset();
        commandList.Reset(commandAllocator, null);

        var renderTarget = renderTargets[frameIndex];
        commandList.ResourceBarrier(ResourceBarrier.BarrierTransition(renderTarget, ResourceStates.Present, ResourceStates.RenderTarget));
        var renderTargetView = GetRenderTargetViewHandle(frameIndex);
        commandList.ClearRenderTargetView(renderTargetView, clearColor);
        commandList.ResourceBarrier(ResourceBarrier.BarrierTransition(renderTarget, ResourceStates.RenderTarget, ResourceStates.Present));
        commandList.Close();

        commandQueue.ExecuteCommandList(commandList);
        swapChain.Present(1, PresentFlags.None);
        MoveToNextFrame();
    }

    public void Dispose()
    {
        WaitForGpu();
        fenceEvent.Dispose();
        fence.Dispose();
        commandList.Dispose();
        commandAllocator.Dispose();
        for (var index = 0; index < renderTargets.Length; index++)
        {
            renderTargets[index].Dispose();
        }
        renderTargetViewHeap.Dispose();
        swapChain.Dispose();
        commandQueue.Dispose();
        device.Dispose();
        factory.Dispose();
    }

    private void CreateRenderTargetViews()
    {
        for (var index = 0; index < renderTargets.Length; index++)
        {
            renderTargets[index] = swapChain.GetBuffer<ID3D12Resource>((uint)index);
            device.CreateRenderTargetView(renderTargets[index], null, GetRenderTargetViewHandle(index));
        }
    }

    private CpuDescriptorHandle GetRenderTargetViewHandle(int index)
    {
        return renderTargetViewHeap.GetCPUDescriptorHandleForHeapStart() + (index * renderTargetViewDescriptorSize);
    }

    private void MoveToNextFrame()
    {
        var signalValue = ++fenceValue;
        commandQueue.Signal(fence, signalValue).CheckError();
        frameIndex = (int)swapChain.CurrentBackBufferIndex;

        if (fence.CompletedValue < signalValue)
        {
            fence.SetEventOnCompletion(signalValue, fenceEvent.SafeWaitHandle.DangerousGetHandle()).CheckError();
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
}
