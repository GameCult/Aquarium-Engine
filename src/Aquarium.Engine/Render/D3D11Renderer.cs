using Vortice.Direct3D;
using Vortice.D3DCompiler;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using CultMath;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Aquarium.Engine.Render;

public sealed class D3D11Renderer : IDisposable
{
    private const string ShaderRelativePath = "Render/Shaders/Aquarium.hlsl";
    private static readonly TimeSpan ShaderReloadPollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan ShaderReloadWriteSettleTime = TimeSpan.FromMilliseconds(150);
    private const int GridHeightTextureSize = 128;
    private const int FroxelCountX = 8;
    private const int FroxelCountY = 8;
    private const int FroxelCountZ = 4;
    private const int FroxelSlotCount = 2;
    private const int FroxelBufferElementCount = FroxelCountX * FroxelCountY * FroxelCountZ * FroxelSlotCount;
    private const int PlanetCount = 5;
    private const float SunRadius = 1.12f;
    private const float FroxelMinZ = -2.0f;
    private const float FroxelMaxZ = 6.0f;

    private readonly IDXGISwapChain swapChain;
    private readonly ID3D11Device device;
    private readonly ID3D11DeviceContext context;
    private readonly ID3D11RenderTargetView renderTargetView;
    private readonly ID3D11Texture2D gridHeightTexture;
    private readonly ID3D11RenderTargetView gridHeightRenderTargetView;
    private readonly ID3D11ShaderResourceView gridHeightShaderResourceView;
    private readonly ID3D11SamplerState gridSampler;
    private readonly ID3D11Buffer frameConstantBuffer;
    private readonly ID3D11Buffer froxelPrimitiveBuffer;
    private readonly ID3D11ShaderResourceView froxelPrimitiveShaderResourceView;
    private readonly Int4[] froxelPrimitiveIds = new Int4[FroxelBufferElementCount];
    private readonly int width;
    private readonly int height;
    private readonly string shaderPath;
    private readonly Stopwatch shaderReloadClock = Stopwatch.StartNew();
    private ID3D11VertexShader vertexShader;
    private ID3D11PixelShader gridHeightPixelShader;
    private ID3D11PixelShader pixelShader;
    private DateTime lastShaderWriteUtc;
    private TimeSpan lastShaderReloadCheck;
    private bool shaderReloadFailureReported;

    public D3D11Renderer(IntPtr windowHandle, int width, int height, string? shaderPath = null)
    {
        this.width = width;
        this.height = height;
        this.shaderPath = shaderPath ?? Path.Combine(AppContext.BaseDirectory, ShaderRelativePath);

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

        var initialShaders = CompileShaderSet(this.shaderPath);
        vertexShader = initialShaders.VertexShader;
        gridHeightPixelShader = initialShaders.GridHeightPixelShader;
        pixelShader = initialShaders.AquariumPixelShader;
        lastShaderWriteUtc = File.GetLastWriteTimeUtc(this.shaderPath);
        gridHeightTexture = CreateGridHeightTexture();
        gridHeightRenderTargetView = device.CreateRenderTargetView(gridHeightTexture);
        gridHeightShaderResourceView = device.CreateShaderResourceView(gridHeightTexture);
        gridSampler = device.CreateSamplerState(new SamplerDescription
        {
            Filter = Filter.MinMagMipLinear,
            AddressU = TextureAddressMode.Clamp,
            AddressV = TextureAddressMode.Clamp,
            AddressW = TextureAddressMode.Clamp,
            ComparisonFunc = ComparisonFunction.Never,
            MinLOD = 0.0f,
            MaxLOD = float.MaxValue,
        });
        frameConstantBuffer = device.CreateBuffer(
            48,
            BindFlags.ConstantBuffer,
            ResourceUsage.Default,
            CpuAccessFlags.None,
            ResourceOptionFlags.None,
            0);
        froxelPrimitiveBuffer = device.CreateBuffer(
            froxelPrimitiveIds,
            BindFlags.ShaderResource,
            ResourceUsage.Default,
            CpuAccessFlags.None,
            ResourceOptionFlags.BufferStructured,
            0,
            (uint)Marshal.SizeOf<Int4>());
        froxelPrimitiveShaderResourceView = device.CreateShaderResourceView(
            froxelPrimitiveBuffer,
            new ShaderResourceViewDescription(froxelPrimitiveBuffer, Format.Unknown, 0, FroxelBufferElementCount));
    }

    public void Render(AquariumFrame frame)
    {
        TryHotReloadShaders();

        var constants = new FrameConstants(
            new float2(width, height),
            frame.TimeSeconds,
            frame.Grid.Radius,
            (float3)frame.CameraPosition,
            0.0f,
            (float2)frame.Grid.Center,
            new float2(0.0f, 0.0f));

        BuildFroxelPrimitiveTable(frame);
        context.UpdateSubresource(in constants, frameConstantBuffer);
        context.UpdateSubresource(froxelPrimitiveIds, froxelPrimitiveBuffer);
        RenderGridHeight();
        RenderAquarium();
        swapChain.Present(1, PresentFlags.None);
    }

    public void Dispose()
    {
        froxelPrimitiveShaderResourceView.Dispose();
        froxelPrimitiveBuffer.Dispose();
        frameConstantBuffer.Dispose();
        gridSampler.Dispose();
        gridHeightShaderResourceView.Dispose();
        gridHeightRenderTargetView.Dispose();
        gridHeightTexture.Dispose();
        pixelShader.Dispose();
        gridHeightPixelShader.Dispose();
        vertexShader.Dispose();
        renderTargetView.Dispose();
        context.Dispose();
        device.Dispose();
        swapChain.Dispose();
    }

    private ID3D11Texture2D CreateGridHeightTexture()
    {
        var description = new Texture2DDescription
        {
            Width = GridHeightTextureSize,
            Height = GridHeightTextureSize,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.R32G32B32A32_Float,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
            CPUAccessFlags = CpuAccessFlags.None,
            MiscFlags = ResourceOptionFlags.None,
        };

        return device.CreateTexture2D(description);
    }

    private void RenderGridHeight()
    {
        context.PSUnsetShaderResource(0);
        context.PSUnsetShaderResource(1);
        context.OMSetRenderTargets(gridHeightRenderTargetView);
        context.RSSetViewport(0.0f, 0.0f, GridHeightTextureSize, GridHeightTextureSize, 0.0f, 1.0f);
        context.ClearRenderTargetView(gridHeightRenderTargetView, new Color4(0.0f, 0.0f, 0.0f, 0.0f));
        context.IASetInputLayout(null);
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        context.VSSetShader(vertexShader);
        context.PSSetShader(gridHeightPixelShader);
        context.PSSetConstantBuffer(0, frameConstantBuffer);
        context.Draw(3, 0);
    }

    private void RenderAquarium()
    {
        context.OMSetRenderTargets(renderTargetView);
        context.RSSetViewport(0.0f, 0.0f, width, height, 0.0f, 1.0f);
        context.ClearRenderTargetView(renderTargetView, new Color4(0.0f, 0.0f, 0.0f, 1.0f));
        context.IASetInputLayout(null);
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        context.VSSetShader(vertexShader);
        context.PSSetShader(pixelShader);
        context.PSSetConstantBuffer(0, frameConstantBuffer);
        context.PSSetShaderResource(0, gridHeightShaderResourceView);
        context.PSSetShaderResource(1, froxelPrimitiveShaderResourceView);
        context.PSSetSampler(0, gridSampler);
        context.Draw(3, 0);
    }

    private void TryHotReloadShaders()
    {
        var now = shaderReloadClock.Elapsed;
        if (now - lastShaderReloadCheck < ShaderReloadPollInterval)
        {
            return;
        }

        lastShaderReloadCheck = now;
        DateTime writeTimeUtc;
        try
        {
            writeTimeUtc = File.GetLastWriteTimeUtc(shaderPath);
        }
        catch (Exception error)
        {
            if (!shaderReloadFailureReported)
            {
                Console.Error.WriteLine($"Shader hot reload cannot stat {shaderPath}: {error.Message}");
                shaderReloadFailureReported = true;
            }

            return;
        }

        if (writeTimeUtc <= lastShaderWriteUtc || DateTime.UtcNow - writeTimeUtc < ShaderReloadWriteSettleTime)
        {
            return;
        }

        shaderReloadFailureReported = false;
        try
        {
            var replacement = CompileShaderSet(shaderPath);
            var oldVertexShader = vertexShader;
            var oldGridHeightPixelShader = gridHeightPixelShader;
            var oldPixelShader = pixelShader;

            vertexShader = replacement.VertexShader;
            gridHeightPixelShader = replacement.GridHeightPixelShader;
            pixelShader = replacement.AquariumPixelShader;

            oldPixelShader.Dispose();
            oldGridHeightPixelShader.Dispose();
            oldVertexShader.Dispose();

            lastShaderWriteUtc = writeTimeUtc;
            Console.WriteLine($"Shader hot reload applied: {shaderPath}");
        }
        catch (Exception error)
        {
            lastShaderWriteUtc = writeTimeUtc;
            if (!shaderReloadFailureReported)
            {
                Console.Error.WriteLine($"Shader hot reload failed; keeping previous shaders. {error.Message}");
                shaderReloadFailureReported = true;
            }
        }
    }

    private void BuildFroxelPrimitiveTable(AquariumFrame frame)
    {
        for (var i = 0; i < froxelPrimitiveIds.Length; i++)
        {
            froxelPrimitiveIds[i] = new Int4(-1, -1, -1, -1);
        }

        AddPrimitiveToFroxels(frame, 0, new Vector3(0.0f, 0.0f, 2.2f), SunRadius * 1.22f);

        for (var i = 0; i < PlanetCount; i++)
        {
            var radius = PlanetRadius(i);
            AddPrimitiveToFroxels(frame, i + 1, PlanetCenter(i, frame.TimeSeconds, radius), radius * 1.16f);
        }
    }

    private void AddPrimitiveToFroxels(AquariumFrame frame, int primitiveId, Vector3 center, float boundRadius)
    {
        var min = center - new Vector3(boundRadius);
        var max = center + new Vector3(boundRadius);
        var minCell = FroxelCellForPosition(frame, min);
        var maxCell = FroxelCellForPosition(frame, max);

        for (var z = minCell.Z; z <= maxCell.Z; z++)
        {
            for (var y = minCell.Y; y <= maxCell.Y; y++)
            {
                for (var x = minCell.X; x <= maxCell.X; x++)
                {
                    AddPrimitiveToFroxel(FroxelIndex(x, y, z), primitiveId);
                }
            }
        }
    }

    private void AddPrimitiveToFroxel(int froxelIndex, int primitiveId)
    {
        var baseElement = froxelIndex * FroxelSlotCount;
        for (var slotGroup = 0; slotGroup < FroxelSlotCount; slotGroup++)
        {
            var elementIndex = baseElement + slotGroup;
            var element = froxelPrimitiveIds[elementIndex];

            if (element.X == primitiveId || element.Y == primitiveId || element.Z == primitiveId || element.W == primitiveId)
            {
                return;
            }

            if (element.X == -1)
            {
                froxelPrimitiveIds[elementIndex] = element with { X = primitiveId };
                return;
            }

            if (element.Y == -1)
            {
                froxelPrimitiveIds[elementIndex] = element with { Y = primitiveId };
                return;
            }

            if (element.Z == -1)
            {
                froxelPrimitiveIds[elementIndex] = element with { Z = primitiveId };
                return;
            }

            if (element.W == -1)
            {
                froxelPrimitiveIds[elementIndex] = element with { W = primitiveId };
                return;
            }
        }
    }

    private static FroxelCell FroxelCellForPosition(AquariumFrame frame, Vector3 position)
    {
        var gridCenter = frame.Grid.Center;
        var gridRadius = MathF.Max(frame.Grid.Radius, 0.001f);
        var localX = ((position.X - gridCenter.X) / gridRadius) * 0.5f + 0.5f;
        var localY = ((position.Y - gridCenter.Y) / gridRadius) * 0.5f + 0.5f;
        var localZ = (position.Z - FroxelMinZ) / (FroxelMaxZ - FroxelMinZ);

        return new FroxelCell(
            ClampCell(localX, FroxelCountX),
            ClampCell(localY, FroxelCountY),
            ClampCell(localZ, FroxelCountZ));
    }

    private static int ClampCell(float normalized, int count)
    {
        return Math.Clamp((int)MathF.Floor(normalized * count), 0, count - 1);
    }

    private static int FroxelIndex(int x, int y, int z)
    {
        return x + y * FroxelCountX + z * FroxelCountX * FroxelCountY;
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

    private static ReadOnlyMemory<byte> CompileShader(string path, string entryPoint, string profile)
    {
        var shaderFlags = ShaderFlags.EnableStrictness;
#if DEBUG
        shaderFlags |= ShaderFlags.Debug | ShaderFlags.SkipOptimization;
#endif

        return Compiler.CompileFromFile(path, entryPoint, profile, shaderFlags, EffectFlags.None);
    }

    private ShaderSet CompileShaderSet(string path)
    {
        var vertexShaderBytecode = CompileShader(path, "FullscreenTriangleVS", "vs_5_0");
        var gridHeightShaderBytecode = CompileShader(path, "GridHeightPS", "ps_5_0");
        var pixelShaderBytecode = CompileShader(path, "AquariumPS", "ps_5_0");

        return new ShaderSet(
            device.CreateVertexShader(vertexShaderBytecode.Span, null),
            device.CreatePixelShader(gridHeightShaderBytecode.Span, null),
            device.CreatePixelShader(pixelShaderBytecode.Span, null));
    }

    private readonly record struct FrameConstants(
        float2 Resolution,
        float TimeSeconds,
        float GridRadius,
        float3 CameraPosition,
        float CameraPad,
        float2 GridCenter,
        float2 Pad0);

    private readonly record struct FroxelCell(int X, int Y, int Z);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct Int4(int X, int Y, int Z, int W);

    private readonly record struct ShaderSet(
        ID3D11VertexShader VertexShader,
        ID3D11PixelShader GridHeightPixelShader,
        ID3D11PixelShader AquariumPixelShader);
}
