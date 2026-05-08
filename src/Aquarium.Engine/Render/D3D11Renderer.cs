using Vortice.Direct3D;
using Vortice.D3DCompiler;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using CultMath;

namespace Aquarium.Engine.Render;

public sealed class D3D11Renderer : IDisposable
{
    private const string ShaderRelativePath = "Render/Shaders/Aquarium.hlsl";

    private readonly IDXGISwapChain swapChain;
    private readonly ID3D11Device device;
    private readonly ID3D11DeviceContext context;
    private readonly ID3D11RenderTargetView renderTargetView;
    private readonly ID3D11VertexShader vertexShader;
    private readonly ID3D11PixelShader pixelShader;
    private readonly ID3D11Buffer frameConstantBuffer;
    private readonly int width;
    private readonly int height;

    public D3D11Renderer(IntPtr windowHandle, int width, int height)
    {
        this.width = width;
        this.height = height;

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

        var shaderPath = Path.Combine(AppContext.BaseDirectory, ShaderRelativePath);
        var vertexShaderBytecode = CompileShader(shaderPath, "FullscreenTriangleVS", "vs_5_0");
        var pixelShaderBytecode = CompileShader(shaderPath, "AquariumPS", "ps_5_0");

        vertexShader = device.CreateVertexShader(vertexShaderBytecode.Span, null);
        pixelShader = device.CreatePixelShader(pixelShaderBytecode.Span, null);
        frameConstantBuffer = device.CreateBuffer(
            32,
            BindFlags.ConstantBuffer,
            ResourceUsage.Default,
            CpuAccessFlags.None,
            ResourceOptionFlags.None,
            0);
    }

    public void Render(AquariumFrame frame)
    {
        var constants = new FrameConstants(
            new float2(width, height),
            frame.TimeSeconds,
            frame.Grid.Radius,
            (float3)frame.CameraPosition,
            0.0f);

        context.OMSetRenderTargets(renderTargetView);
        context.RSSetViewport(0.0f, 0.0f, width, height, 0.0f, 1.0f);
        context.ClearRenderTargetView(renderTargetView, new Color4(0.0f, 0.0f, 0.0f, 1.0f));
        context.UpdateSubresource(in constants, frameConstantBuffer);
        context.IASetInputLayout(null);
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        context.VSSetShader(vertexShader);
        context.PSSetShader(pixelShader);
        context.PSSetConstantBuffer(0, frameConstantBuffer);
        context.Draw(3, 0);
        swapChain.Present(1, PresentFlags.None);
    }

    public void Dispose()
    {
        frameConstantBuffer.Dispose();
        pixelShader.Dispose();
        vertexShader.Dispose();
        renderTargetView.Dispose();
        context.Dispose();
        device.Dispose();
        swapChain.Dispose();
    }

    private static ReadOnlyMemory<byte> CompileShader(string path, string entryPoint, string profile)
    {
        var shaderFlags = ShaderFlags.EnableStrictness;
#if DEBUG
        shaderFlags |= ShaderFlags.Debug | ShaderFlags.SkipOptimization;
#endif

        return Compiler.CompileFromFile(path, entryPoint, profile, shaderFlags, EffectFlags.None);
    }

    private readonly record struct FrameConstants(
        float2 Resolution,
        float TimeSeconds,
        float GridRadius,
        float3 CameraPosition,
        float Pad0);
}
