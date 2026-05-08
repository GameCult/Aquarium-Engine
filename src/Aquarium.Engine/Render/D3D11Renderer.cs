using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Aquarium.Engine.Render;

public sealed class D3D11Renderer : IDisposable
{
    private readonly IDXGISwapChain swapChain;
    private readonly ID3D11Device device;
    private readonly ID3D11DeviceContext context;
    private readonly ID3D11RenderTargetView renderTargetView;

    public D3D11Renderer(IntPtr windowHandle, int width, int height)
    {
        var featureLevels = new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 };
        var swapChainDescription = new SwapChainDescription
        {
            BufferCount = 2,
            BufferDescription = new ModeDescription((uint)width, (uint)height, Format.R8G8B8A8_UNorm),
            BufferUsage = Usage.RenderTargetOutput,
            OutputWindow = windowHandle,
            SampleDescription = new SampleDescription(1, 0),
            Windowed = true,
            SwapEffect = SwapEffect.Discard,
        };

        D3D11.D3D11CreateDeviceAndSwapChain(
            null,
            DriverType.Hardware,
            DeviceCreationFlags.BgraSupport,
            featureLevels,
            swapChainDescription,
            out var createdSwapChain,
            out var createdDevice,
            out _,
            out var createdContext).CheckError();

        swapChain = createdSwapChain ?? throw new InvalidOperationException("D3D11 did not create a swapchain.");
        device = createdDevice ?? throw new InvalidOperationException("D3D11 did not create a device.");
        context = createdContext ?? throw new InvalidOperationException("D3D11 did not create an immediate context.");

        Console.WriteLine("D3D11 device and swapchain created.");

        using var backBuffer = swapChain.GetBuffer<ID3D11Texture2D>(0);
        renderTargetView = device.CreateRenderTargetView(backBuffer);
    }

    public void Render(AquariumFrame frame)
    {
        var gridRadiusGlow = Math.Clamp(frame.Grid.Radius / 64.0f, 0.0f, 1.0f);
        var clearColor = new Color4(0.005f, 0.012f + gridRadiusGlow * 0.01f, 0.018f + gridRadiusGlow * 0.02f, 1.0f);

        context.OMSetRenderTargets(renderTargetView);
        context.ClearRenderTargetView(renderTargetView, clearColor);
        swapChain.Present(1, PresentFlags.None);
    }

    public void Dispose()
    {
        renderTargetView.Dispose();
        context.Dispose();
        device.Dispose();
        swapChain.Dispose();
    }
}
