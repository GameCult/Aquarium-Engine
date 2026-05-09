using Vortice.Direct3D;
using Vortice.D3DCompiler;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using Aquarium.Engine.Input;
using Aquarium.Engine.Render.Ui;
using CultMath;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Aquarium.Engine.Render;

public sealed class D3D11Renderer : IDisposable
{
    private const string ShaderRelativePath = "Render/Shaders/Aquarium.hlsl";
    private const string DitherTextureRelativePath = "Assets/Textures/Aetheria-LDR_LLL1_0.r8";
    private static readonly TimeSpan ShaderReloadPollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan ShaderReloadWriteSettleTime = TimeSpan.FromMilliseconds(150);
    private const float TemporalJitterScale = 0.0f;
    private const int GridHeightTextureSize = 128;
    private const int DitherTextureSize = 512;
    private const int BloomLevelCount = 3;
    private const int FroxelCountX = 8;
    private const int FroxelCountY = 8;
    private const int FroxelCountZ = 4;
    private const int FroxelSlotCount = 2;
    private const int FroxelBufferElementCount = FroxelCountX * FroxelCountY * FroxelCountZ * FroxelSlotCount;
    private const int PlanetCount = 5;
    private const int FieldInstanceCount = PlanetCount + 5;
    private static readonly DebugUi.DebugUiOption[] RenderDebugOptions =
    [
        new(0, "Final"),
        new(1, "Raw Scene"),
        new(2, "History"),
        new(3, "History Age"),
        new(4, "History Weight"),
        new(5, "Temporal Control"),
        new(6, "Field Identity"),
        new(7, "Bloom"),
        new(8, "Exposed Luminance"),
        new(9, "Medium Density"),
        new(10, "Medium Transmittance"),
    ];

    private const float SunRadius = 1.12f;
    private const float FroxelMinZ = -2.0f;
    private const float FroxelMaxZ = 6.0f;

    private readonly IDXGISwapChain swapChain;
    private readonly ID3D11Device device;
    private readonly ID3D11DeviceContext context;
    private readonly ID3D11RenderTargetView renderTargetView;
    private readonly DirectWriteOverlay overlay;
    private readonly DebugUi debugUi;
    private readonly ID3D11Texture2D gridHeightTexture;
    private readonly ID3D11RenderTargetView gridHeightRenderTargetView;
    private readonly ID3D11ShaderResourceView gridHeightShaderResourceView;
    private readonly ID3D11Texture2D ditherTexture;
    private readonly ID3D11ShaderResourceView ditherShaderResourceView;
    private readonly ID3D11Texture2D sceneTexture;
    private readonly ID3D11RenderTargetView sceneRenderTargetView;
    private readonly ID3D11ShaderResourceView sceneShaderResourceView;
    private readonly ID3D11Texture2D sceneMetadataTexture;
    private readonly ID3D11RenderTargetView sceneMetadataRenderTargetView;
    private readonly ID3D11ShaderResourceView sceneMetadataShaderResourceView;
    private readonly ID3D11Texture2D sceneControlTexture;
    private readonly ID3D11RenderTargetView sceneControlRenderTargetView;
    private readonly ID3D11ShaderResourceView sceneControlShaderResourceView;
    private readonly ID3D11Texture2D[] bloomTextures = new ID3D11Texture2D[BloomLevelCount];
    private readonly ID3D11RenderTargetView[] bloomRenderTargetViews = new ID3D11RenderTargetView[BloomLevelCount];
    private readonly ID3D11ShaderResourceView[] bloomShaderResourceViews = new ID3D11ShaderResourceView[BloomLevelCount];
    private readonly ID3D11Texture2D[] bloomScratchTextures = new ID3D11Texture2D[BloomLevelCount];
    private readonly ID3D11RenderTargetView[] bloomScratchRenderTargetViews = new ID3D11RenderTargetView[BloomLevelCount];
    private readonly ID3D11ShaderResourceView[] bloomScratchShaderResourceViews = new ID3D11ShaderResourceView[BloomLevelCount];
    private readonly ID3D11Texture2D historyTextureA;
    private readonly ID3D11RenderTargetView historyRenderTargetViewA;
    private readonly ID3D11ShaderResourceView historyShaderResourceViewA;
    private readonly ID3D11Texture2D historyMetadataTextureA;
    private readonly ID3D11RenderTargetView historyMetadataRenderTargetViewA;
    private readonly ID3D11ShaderResourceView historyMetadataShaderResourceViewA;
    private readonly ID3D11Texture2D historyControlTextureA;
    private readonly ID3D11RenderTargetView historyControlRenderTargetViewA;
    private readonly ID3D11ShaderResourceView historyControlShaderResourceViewA;
    private readonly ID3D11Texture2D historyTextureB;
    private readonly ID3D11RenderTargetView historyRenderTargetViewB;
    private readonly ID3D11ShaderResourceView historyShaderResourceViewB;
    private readonly ID3D11Texture2D historyMetadataTextureB;
    private readonly ID3D11RenderTargetView historyMetadataRenderTargetViewB;
    private readonly ID3D11ShaderResourceView historyMetadataShaderResourceViewB;
    private readonly ID3D11Texture2D historyControlTextureB;
    private readonly ID3D11RenderTargetView historyControlRenderTargetViewB;
    private readonly ID3D11ShaderResourceView historyControlShaderResourceViewB;
    private readonly ID3D11SamplerState gridSampler;
    private readonly ID3D11SamplerState ditherSampler;
    private readonly ID3D11Buffer frameConstantBuffer;
    private readonly ID3D11Buffer froxelPrimitiveBuffer;
    private readonly ID3D11ShaderResourceView froxelPrimitiveShaderResourceView;
    private readonly Int4[] froxelPrimitiveIds = new Int4[FroxelBufferElementCount];
    private readonly ID3D11Buffer fieldInstanceBuffer;
    private readonly ID3D11ShaderResourceView fieldInstanceShaderResourceView;
    private readonly FieldInstanceGpu[] fieldInstances = new FieldInstanceGpu[FieldInstanceCount];
    private readonly int width;
    private readonly int height;
    private readonly string shaderPath;
    private readonly Stopwatch shaderReloadClock = Stopwatch.StartNew();
    private ID3D11VertexShader vertexShader;
    private ID3D11PixelShader gridHeightPixelShader;
    private ID3D11PixelShader scenePixelShader;
    private ID3D11PixelShader bloomPrefilterPixelShader;
    private ID3D11PixelShader bloomDownsamplePixelShader;
    private ID3D11PixelShader bloomBlurHorizontalPixelShader;
    private ID3D11PixelShader bloomBlurVerticalPixelShader;
    private ID3D11PixelShader resolvePixelShader;
    private DateTime lastShaderWriteUtc;
    private TimeSpan lastShaderReloadCheck;
    private bool shaderReloadFailureReported;
    private int frameIndex;
    private Vector3 previousCameraPosition;
    private Vector2 previousGridCenter;
    private float previousGridRadius;
    private float previousTimeSeconds;

    public D3D11Renderer(IntPtr windowHandle, int width, int height, string? shaderPath = null, GraphicsSettings? graphicsSettings = null, Action<string>? startupProgress = null)
    {
        this.width = width;
        this.height = height;
        ApplyGraphicsSettings(graphicsSettings ?? GraphicsSettings.Default);
        this.shaderPath = shaderPath ?? Path.Combine(AppContext.BaseDirectory, ShaderRelativePath);
        startupProgress?.Invoke("Creating D3D11 device and swapchain");

        var featureLevels = new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 };
        var swapChainDescription = new SwapChainDescription
        {
            BufferCount = 2,
            BufferDescription = new ModeDescription((uint)width, (uint)height, Format.B8G8R8A8_UNorm),
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

        startupProgress?.Invoke("Creating swapchain render targets");
        using var backBuffer = swapChain.GetBuffer<ID3D11Texture2D>(0);
        renderTargetView = device.CreateRenderTargetView(backBuffer);
        using var backBufferSurface = swapChain.GetBuffer<IDXGISurface>(0);
        overlay = new DirectWriteOverlay(backBufferSurface, width, height);
        debugUi = CreateDebugUi();

        startupProgress?.Invoke("Compiling aquarium shaders");
        var initialShaders = CompileShaderSet(this.shaderPath);
        vertexShader = initialShaders.VertexShader;
        gridHeightPixelShader = initialShaders.GridHeightPixelShader;
        scenePixelShader = initialShaders.AquariumScenePixelShader;
        bloomPrefilterPixelShader = initialShaders.BloomPrefilterPixelShader;
        bloomDownsamplePixelShader = initialShaders.BloomDownsamplePixelShader;
        bloomBlurHorizontalPixelShader = initialShaders.BloomBlurHorizontalPixelShader;
        bloomBlurVerticalPixelShader = initialShaders.BloomBlurVerticalPixelShader;
        resolvePixelShader = initialShaders.AquariumResolvePixelShader;
        lastShaderWriteUtc = File.GetLastWriteTimeUtc(this.shaderPath);
        startupProgress?.Invoke("Creating Grid render targets and buffers");
        gridHeightTexture = CreateGridHeightTexture();
        gridHeightRenderTargetView = device.CreateRenderTargetView(gridHeightTexture);
        gridHeightShaderResourceView = device.CreateShaderResourceView(gridHeightTexture);
        startupProgress?.Invoke("Creating temporal render targets");
        sceneTexture = CreateHdrTexture(width, height);
        sceneRenderTargetView = device.CreateRenderTargetView(sceneTexture);
        sceneShaderResourceView = device.CreateShaderResourceView(sceneTexture);
        sceneMetadataTexture = CreateHdrTexture(width, height);
        sceneMetadataRenderTargetView = device.CreateRenderTargetView(sceneMetadataTexture);
        sceneMetadataShaderResourceView = device.CreateShaderResourceView(sceneMetadataTexture);
        sceneControlTexture = CreateHdrTexture(width, height);
        sceneControlRenderTargetView = device.CreateRenderTargetView(sceneControlTexture);
        sceneControlShaderResourceView = device.CreateShaderResourceView(sceneControlTexture);
        for (var level = 0; level < BloomLevelCount; level++)
        {
            var divisor = 1 << (level + 1);
            var bloomWidth = Math.Max(1, width / divisor);
            var bloomHeight = Math.Max(1, height / divisor);
            bloomTextures[level] = CreateHdrTexture(bloomWidth, bloomHeight);
            bloomRenderTargetViews[level] = device.CreateRenderTargetView(bloomTextures[level]);
            bloomShaderResourceViews[level] = device.CreateShaderResourceView(bloomTextures[level]);
            bloomScratchTextures[level] = CreateHdrTexture(bloomWidth, bloomHeight);
            bloomScratchRenderTargetViews[level] = device.CreateRenderTargetView(bloomScratchTextures[level]);
            bloomScratchShaderResourceViews[level] = device.CreateShaderResourceView(bloomScratchTextures[level]);
        }
        historyTextureA = CreateHdrTexture(width, height);
        historyRenderTargetViewA = device.CreateRenderTargetView(historyTextureA);
        historyShaderResourceViewA = device.CreateShaderResourceView(historyTextureA);
        historyMetadataTextureA = CreateHdrTexture(width, height);
        historyMetadataRenderTargetViewA = device.CreateRenderTargetView(historyMetadataTextureA);
        historyMetadataShaderResourceViewA = device.CreateShaderResourceView(historyMetadataTextureA);
        historyControlTextureA = CreateHdrTexture(width, height);
        historyControlRenderTargetViewA = device.CreateRenderTargetView(historyControlTextureA);
        historyControlShaderResourceViewA = device.CreateShaderResourceView(historyControlTextureA);
        historyTextureB = CreateHdrTexture(width, height);
        historyRenderTargetViewB = device.CreateRenderTargetView(historyTextureB);
        historyShaderResourceViewB = device.CreateShaderResourceView(historyTextureB);
        historyMetadataTextureB = CreateHdrTexture(width, height);
        historyMetadataRenderTargetViewB = device.CreateRenderTargetView(historyMetadataTextureB);
        historyMetadataShaderResourceViewB = device.CreateShaderResourceView(historyMetadataTextureB);
        historyControlTextureB = CreateHdrTexture(width, height);
        historyControlRenderTargetViewB = device.CreateRenderTargetView(historyControlTextureB);
        historyControlShaderResourceViewB = device.CreateShaderResourceView(historyControlTextureB);
        startupProgress?.Invoke("Loading Aetheria blue-noise dither texture");
        ditherTexture = CreateDitherTexture(Path.Combine(AppContext.BaseDirectory, DitherTextureRelativePath));
        ditherShaderResourceView = device.CreateShaderResourceView(ditherTexture);
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
        ditherSampler = device.CreateSamplerState(new SamplerDescription
        {
            Filter = Filter.MinMagMipPoint,
            AddressU = TextureAddressMode.Wrap,
            AddressV = TextureAddressMode.Wrap,
            AddressW = TextureAddressMode.Wrap,
            ComparisonFunc = ComparisonFunction.Never,
            MinLOD = 0.0f,
            MaxLOD = float.MaxValue,
        });
        frameConstantBuffer = device.CreateBuffer(
            112,
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
        fieldInstanceBuffer = device.CreateBuffer(
            fieldInstances,
            BindFlags.ShaderResource,
            ResourceUsage.Default,
            CpuAccessFlags.None,
            ResourceOptionFlags.BufferStructured,
            0,
            (uint)Marshal.SizeOf<FieldInstanceGpu>());
        fieldInstanceShaderResourceView = device.CreateShaderResourceView(
            fieldInstanceBuffer,
            new ShaderResourceViewDescription(fieldInstanceBuffer, Format.Unknown, 0, FieldInstanceCount));
        previousCameraPosition = Vector3.Zero;
        previousGridCenter = Vector2.Zero;
        previousGridRadius = 0.001f;
    }

    public void Render(AquariumFrame frame)
    {
        TryHotReloadShaders();

        var gridOrigin = new Vector3(frame.Grid.Center.X, frame.Grid.Center.Y, 0.0f);
        var farDistance = Vector3.Distance(frame.CameraPosition, gridOrigin) + MathF.Max(frame.Grid.Radius, 0.001f);
        var jitter = frameIndex == 0 ? Vector2.Zero : HaltonJitter(frameIndex);
        var previousJitter = frameIndex <= 1 ? Vector2.Zero : HaltonJitter(frameIndex - 1);
        if (frameIndex == 0)
        {
            previousCameraPosition = frame.CameraPosition;
            previousGridCenter = frame.Grid.Center;
            previousGridRadius = frame.Grid.Radius;
            previousTimeSeconds = frame.TimeSeconds;
        }

        var constants = new FrameConstants(
            new float2(width, height),
            frame.TimeSeconds,
            frame.Grid.Radius,
            (float3)frame.CameraPosition,
            farDistance,
            (float2)frame.Grid.Center,
            frameIndex,
            previousTimeSeconds,
            (float3)previousCameraPosition,
            previousGridRadius,
            (float2)previousGridCenter,
            (float2)jitter,
            (float2)previousJitter,
            RenderDebugMode,
            SceneExposure,
            BloomIntensity,
            BloomVeilIntensity,
            (float2)Vector2.Zero);

        BuildFroxelPrimitiveTable(frame);
        BuildFieldInstanceTable(frame);
        context.UpdateSubresource(in constants, frameConstantBuffer);
        context.UpdateSubresource(froxelPrimitiveIds, froxelPrimitiveBuffer);
        context.UpdateSubresource(fieldInstances, fieldInstanceBuffer);
        RenderGridHeight();
        RenderScene();
        RenderBloom();
        ResolveTemporal();
        context.Flush();
        overlay.Render(frame, RenderDebugMode, debugUi);
        swapChain.Present(1, PresentFlags.None);

        previousCameraPosition = frame.CameraPosition;
        previousGridCenter = frame.Grid.Center;
        previousGridRadius = frame.Grid.Radius;
        previousTimeSeconds = frame.TimeSeconds;
        frameIndex++;
    }

    public int RenderDebugMode { get; set; }

    public float SceneExposure { get; set; } = GraphicsSettings.Default.SceneExposure;

    public float BloomIntensity { get; set; } = GraphicsSettings.Default.BloomIntensity;

    public float BloomVeilIntensity { get; set; } = GraphicsSettings.Default.BloomVeilIntensity;

    public bool DebugUiVisible
    {
        get => debugUi.IsVisible;
        set => debugUi.IsVisible = value;
    }

    public void UpdateDebugUi(InputState input)
    {
        debugUi.Update(input);
    }

    public void CycleRenderDebugMode()
    {
        RenderDebugMode = (RenderDebugMode + 1) % (GraphicsSettings.MaxRenderDebugMode + 1);
    }

    public GraphicsSettings CaptureGraphicsSettings()
    {
        return new GraphicsSettings(RenderDebugMode, SceneExposure, BloomIntensity, BloomVeilIntensity).Normalized();
    }

    public void ApplyGraphicsSettings(GraphicsSettings settings)
    {
        var normalized = settings.Normalized();
        RenderDebugMode = normalized.RenderDebugMode;
        SceneExposure = normalized.SceneExposure;
        BloomIntensity = normalized.BloomIntensity;
        BloomVeilIntensity = normalized.BloomVeilIntensity;
    }

    private DebugUi CreateDebugUi()
    {
        return new DebugUi("Aquarium Debug")
            .Panel(panel => panel
                .Section("View")
                .Options("Render Debug", () => RenderDebugMode, value => RenderDebugMode = Math.Clamp(value, GraphicsSettings.MinRenderDebugMode, GraphicsSettings.MaxRenderDebugMode), RenderDebugOptions, "Selects the active renderer debug view.")
                .Button("Reset View", () => RenderDebugMode = 0, "Returns to the final presented frame.")
                .Section("HDR")
                .Slider("Exposure", () => SceneExposure, value => SceneExposure = Math.Clamp(value, GraphicsSettings.MinSceneExposure, GraphicsSettings.MaxSceneExposure), GraphicsSettings.MinSceneExposure, GraphicsSettings.MaxSceneExposure, "0.###", "Manual scene exposure before display transform.")
                .Slider("Bloom Intensity", () => BloomIntensity, value => BloomIntensity = Math.Clamp(value, GraphicsSettings.MinBloomIntensity, GraphicsSettings.MaxBloomIntensity), GraphicsSettings.MinBloomIntensity, GraphicsSettings.MaxBloomIntensity, "0.###", "Strength of pre-tonemap bloom energy.")
                .Slider("Bloom Veil", () => BloomVeilIntensity, value => BloomVeilIntensity = Math.Clamp(value, GraphicsSettings.MinBloomVeilIntensity, GraphicsSettings.MaxBloomVeilIntensity), GraphicsSettings.MinBloomVeilIntensity, GraphicsSettings.MaxBloomVeilIntensity, "0.###", "Low-frequency veil from bright HDR energy."));
    }

    public void Dispose()
    {
        overlay.Dispose();
        fieldInstanceShaderResourceView.Dispose();
        fieldInstanceBuffer.Dispose();
        froxelPrimitiveShaderResourceView.Dispose();
        froxelPrimitiveBuffer.Dispose();
        frameConstantBuffer.Dispose();
        ditherSampler.Dispose();
        gridSampler.Dispose();
        ditherShaderResourceView.Dispose();
        ditherTexture.Dispose();
        historyShaderResourceViewB.Dispose();
        historyRenderTargetViewB.Dispose();
        historyTextureB.Dispose();
        historyMetadataShaderResourceViewB.Dispose();
        historyMetadataRenderTargetViewB.Dispose();
        historyMetadataTextureB.Dispose();
        historyControlShaderResourceViewB.Dispose();
        historyControlRenderTargetViewB.Dispose();
        historyControlTextureB.Dispose();
        for (var level = BloomLevelCount - 1; level >= 0; level--)
        {
            bloomScratchShaderResourceViews[level].Dispose();
            bloomScratchRenderTargetViews[level].Dispose();
            bloomScratchTextures[level].Dispose();
            bloomShaderResourceViews[level].Dispose();
            bloomRenderTargetViews[level].Dispose();
            bloomTextures[level].Dispose();
        }
        historyShaderResourceViewA.Dispose();
        historyRenderTargetViewA.Dispose();
        historyTextureA.Dispose();
        historyMetadataShaderResourceViewA.Dispose();
        historyMetadataRenderTargetViewA.Dispose();
        historyMetadataTextureA.Dispose();
        historyControlShaderResourceViewA.Dispose();
        historyControlRenderTargetViewA.Dispose();
        historyControlTextureA.Dispose();
        sceneShaderResourceView.Dispose();
        sceneRenderTargetView.Dispose();
        sceneTexture.Dispose();
        sceneControlShaderResourceView.Dispose();
        sceneControlRenderTargetView.Dispose();
        sceneControlTexture.Dispose();
        sceneMetadataShaderResourceView.Dispose();
        sceneMetadataRenderTargetView.Dispose();
        sceneMetadataTexture.Dispose();
        gridHeightShaderResourceView.Dispose();
        gridHeightRenderTargetView.Dispose();
        gridHeightTexture.Dispose();
        resolvePixelShader.Dispose();
        scenePixelShader.Dispose();
        bloomBlurVerticalPixelShader.Dispose();
        bloomBlurHorizontalPixelShader.Dispose();
        bloomDownsamplePixelShader.Dispose();
        bloomPrefilterPixelShader.Dispose();
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

    private ID3D11Texture2D CreateHdrTexture(int textureWidth, int textureHeight)
    {
        var description = new Texture2DDescription
        {
            Width = (uint)textureWidth,
            Height = (uint)textureHeight,
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

    private ID3D11Texture2D CreateDitherTexture(string path)
    {
        var pixels = File.ReadAllBytes(path);
        if (pixels.Length != DitherTextureSize * DitherTextureSize)
        {
            throw new InvalidOperationException($"Dither texture {path} must be {DitherTextureSize}x{DitherTextureSize} R8 data.");
        }

        return device.CreateTexture2D(
            pixels,
            Format.R8_UNorm,
            DitherTextureSize,
            DitherTextureSize,
            1,
            1,
            BindFlags.ShaderResource,
            ResourceOptionFlags.None,
            ResourceUsage.Default,
            CpuAccessFlags.None);
    }

    private void RenderGridHeight()
    {
        context.PSUnsetShaderResource(0);
        context.PSUnsetShaderResource(1);
        context.PSUnsetShaderResource(2);
        context.PSUnsetShaderResource(3);
        context.PSUnsetShaderResource(4);
        context.PSUnsetShaderResource(5);
        context.PSUnsetShaderResource(6);
        context.PSUnsetShaderResource(7);
        context.PSUnsetShaderResource(8);
        context.PSUnsetShaderResource(9);
        context.PSUnsetShaderResource(10);
        context.PSUnsetShaderResource(11);
        context.PSUnsetShaderResource(12);
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

    private void RenderScene()
    {
        context.PSUnsetShaderResource(3);
        context.PSUnsetShaderResource(4);
        context.PSUnsetShaderResource(5);
        context.PSUnsetShaderResource(6);
        context.PSUnsetShaderResource(7);
        context.PSUnsetShaderResource(8);
        context.PSUnsetShaderResource(9);
        context.PSUnsetShaderResource(10);
        context.PSUnsetShaderResource(11);
        context.OMSetRenderTargets(new[] { sceneRenderTargetView, sceneMetadataRenderTargetView, sceneControlRenderTargetView });
        context.RSSetViewport(0.0f, 0.0f, width, height, 0.0f, 1.0f);
        context.ClearRenderTargetView(sceneRenderTargetView, new Color4(0.0f, 0.0f, 0.0f, 0.0f));
        context.ClearRenderTargetView(sceneMetadataRenderTargetView, new Color4(0.0f, 0.0f, 0.0f, 0.0f));
        context.ClearRenderTargetView(sceneControlRenderTargetView, new Color4(0.0f, 0.0f, 0.0f, 0.0f));
        context.IASetInputLayout(null);
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        context.VSSetShader(vertexShader);
        context.PSSetShader(scenePixelShader);
        context.PSSetConstantBuffer(0, frameConstantBuffer);
        context.PSSetShaderResource(0, gridHeightShaderResourceView);
        context.PSSetShaderResource(1, froxelPrimitiveShaderResourceView);
        context.PSSetShaderResource(2, ditherShaderResourceView);
        context.PSSetShaderResource(12, fieldInstanceShaderResourceView);
        context.PSSetSampler(0, gridSampler);
        context.PSSetSampler(1, ditherSampler);
        context.Draw(3, 0);
    }

    private void RenderBloom()
    {
        context.PSUnsetShaderResource(9);
        context.PSUnsetShaderResource(10);
        context.PSUnsetShaderResource(11);
        context.PSUnsetShaderResource(12);

        for (var level = 0; level < BloomLevelCount; level++)
        {
            var targetDescription = bloomTextures[level].Description;
            context.OMSetRenderTargets(bloomRenderTargetViews[level]);
            context.RSSetViewport(0.0f, 0.0f, (float)targetDescription.Width, (float)targetDescription.Height, 0.0f, 1.0f);
            context.ClearRenderTargetView(bloomRenderTargetViews[level], new Color4(0.0f, 0.0f, 0.0f, 0.0f));
            context.IASetInputLayout(null);
            context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            context.VSSetShader(vertexShader);
            context.PSSetConstantBuffer(0, frameConstantBuffer);
            context.PSSetSampler(0, gridSampler);

            if (level == 0)
            {
                context.PSSetShader(bloomPrefilterPixelShader);
                context.PSSetShaderResource(3, sceneShaderResourceView);
            }
            else
            {
                context.PSSetShader(bloomDownsamplePixelShader);
                context.PSSetShaderResource(9, bloomShaderResourceViews[level - 1]);
            }

            context.Draw(3, 0);
            context.PSUnsetShaderResource(3);
            context.PSUnsetShaderResource(9);

            context.OMSetRenderTargets(bloomScratchRenderTargetViews[level]);
            context.ClearRenderTargetView(bloomScratchRenderTargetViews[level], new Color4(0.0f, 0.0f, 0.0f, 0.0f));
            context.PSSetShader(bloomBlurHorizontalPixelShader);
            context.PSSetShaderResource(9, bloomShaderResourceViews[level]);
            context.Draw(3, 0);
            context.PSUnsetShaderResource(9);

            context.OMSetRenderTargets(bloomRenderTargetViews[level]);
            context.ClearRenderTargetView(bloomRenderTargetViews[level], new Color4(0.0f, 0.0f, 0.0f, 0.0f));
            context.PSSetShader(bloomBlurVerticalPixelShader);
            context.PSSetShaderResource(9, bloomScratchShaderResourceViews[level]);
            context.Draw(3, 0);
            context.PSUnsetShaderResource(9);
        }
    }

    private void ResolveTemporal()
    {
        var historyReadView = frameIndex % 2 == 0 ? historyShaderResourceViewA : historyShaderResourceViewB;
        var historyWriteView = frameIndex % 2 == 0 ? historyRenderTargetViewB : historyRenderTargetViewA;
        var historyMetadataReadView = frameIndex % 2 == 0 ? historyMetadataShaderResourceViewA : historyMetadataShaderResourceViewB;
        var historyMetadataWriteView = frameIndex % 2 == 0 ? historyMetadataRenderTargetViewB : historyMetadataRenderTargetViewA;
        var historyControlReadView = frameIndex % 2 == 0 ? historyControlShaderResourceViewA : historyControlShaderResourceViewB;
        var historyControlWriteView = frameIndex % 2 == 0 ? historyControlRenderTargetViewB : historyControlRenderTargetViewA;

        context.PSUnsetShaderResource(3);
        context.PSUnsetShaderResource(4);
        context.PSUnsetShaderResource(5);
        context.PSUnsetShaderResource(6);
        context.PSUnsetShaderResource(7);
        context.PSUnsetShaderResource(8);
        context.PSUnsetShaderResource(9);
        context.PSUnsetShaderResource(10);
        context.PSUnsetShaderResource(11);
        context.OMSetRenderTargets(new[] { renderTargetView, historyWriteView, historyMetadataWriteView, historyControlWriteView });
        context.RSSetViewport(0.0f, 0.0f, width, height, 0.0f, 1.0f);
        context.ClearRenderTargetView(renderTargetView, new Color4(0.0f, 0.0f, 0.0f, 1.0f));
        context.IASetInputLayout(null);
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        context.VSSetShader(vertexShader);
        context.PSSetShader(resolvePixelShader);
        context.PSSetConstantBuffer(0, frameConstantBuffer);
        context.PSSetShaderResource(3, sceneShaderResourceView);
        context.PSSetShaderResource(4, historyReadView);
        context.PSSetShaderResource(5, sceneMetadataShaderResourceView);
        context.PSSetShaderResource(6, historyMetadataReadView);
        context.PSSetShaderResource(7, sceneControlShaderResourceView);
        context.PSSetShaderResource(8, historyControlReadView);
        context.PSSetShaderResource(9, bloomShaderResourceViews[0]);
        context.PSSetShaderResource(10, bloomShaderResourceViews[1]);
        context.PSSetShaderResource(11, bloomShaderResourceViews[2]);
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
            var oldScenePixelShader = scenePixelShader;
            var oldBloomPrefilterPixelShader = bloomPrefilterPixelShader;
            var oldBloomDownsamplePixelShader = bloomDownsamplePixelShader;
            var oldBloomBlurHorizontalPixelShader = bloomBlurHorizontalPixelShader;
            var oldBloomBlurVerticalPixelShader = bloomBlurVerticalPixelShader;
            var oldResolvePixelShader = resolvePixelShader;

            vertexShader = replacement.VertexShader;
            gridHeightPixelShader = replacement.GridHeightPixelShader;
            scenePixelShader = replacement.AquariumScenePixelShader;
            bloomPrefilterPixelShader = replacement.BloomPrefilterPixelShader;
            bloomDownsamplePixelShader = replacement.BloomDownsamplePixelShader;
            bloomBlurHorizontalPixelShader = replacement.BloomBlurHorizontalPixelShader;
            bloomBlurVerticalPixelShader = replacement.BloomBlurVerticalPixelShader;
            resolvePixelShader = replacement.AquariumResolvePixelShader;

            oldResolvePixelShader.Dispose();
            oldBloomBlurVerticalPixelShader.Dispose();
            oldBloomBlurHorizontalPixelShader.Dispose();
            oldBloomDownsamplePixelShader.Dispose();
            oldBloomPrefilterPixelShader.Dispose();
            oldScenePixelShader.Dispose();
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

    private void BuildFieldInstanceTable(AquariumFrame frame)
    {
        fieldInstances[0] = FieldInstanceGpu.Sphere(
            fieldId: 2.0f,
            flags: FieldFlags.Solid | FieldFlags.Emitter,
            center: new Vector3(0.0f, 0.0f, 2.2f),
            radius: SunRadius,
            materialId: 1.0f,
            mediumId: 0.0f,
            color: new Vector3(10.0f, 8.7f, 4.2f),
            medium: Vector4.Zero);

        for (var i = 0; i < PlanetCount; i++)
        {
            var radius = PlanetRadius(i);
            fieldInstances[i + 1] = FieldInstanceGpu.Sphere(
                fieldId: 10.0f + i,
                flags: FieldFlags.Solid | FieldFlags.ShadowCaster | FieldFlags.Receiver,
                center: PlanetCenter(i, frame.TimeSeconds, radius),
                radius: radius,
                materialId: 10.0f + i,
                mediumId: 0.0f,
                color: Vector3.One,
                medium: Vector4.Zero);
        }

        fieldInstances[6] = FieldInstanceGpu.Ellipsoid(
            fieldId: 32.0f,
            flags: FieldFlags.Cloud | FieldFlags.Receiver,
            center: new Vector3(frame.Grid.Center.X - 3.8f, frame.Grid.Center.Y + 1.8f, 1.15f),
            radius: new Vector3(3.4f, 1.25f, 0.92f),
            angle: frame.TimeSeconds * 0.055f,
            mediumId: 1.0f,
            color: new Vector3(0.50f, 0.72f, 0.86f),
            medium: new Vector4(0.030f, 0.018f, 0.0f, 0.45f));
        fieldInstances[7] = FieldInstanceGpu.Ellipsoid(
            fieldId: 33.0f,
            flags: FieldFlags.Cloud | FieldFlags.Receiver,
            center: new Vector3(frame.Grid.Center.X + 4.1f, frame.Grid.Center.Y - 1.4f, -0.45f),
            radius: new Vector3(4.5f, 1.55f, 0.78f),
            angle: -0.62f + frame.TimeSeconds * 0.033f,
            mediumId: 1.0f,
            color: new Vector3(0.34f, 0.58f, 0.72f),
            medium: new Vector4(0.024f, 0.015f, 0.0f, 0.38f));
        fieldInstances[8] = FieldInstanceGpu.Ellipsoid(
            fieldId: 34.0f,
            flags: FieldFlags.Cloud | FieldFlags.Receiver,
            center: new Vector3(frame.Grid.Center.X + 0.8f, frame.Grid.Center.Y + 3.7f, 2.7f),
            radius: new Vector3(2.2f, 1.1f, 0.62f),
            angle: 1.15f,
            mediumId: 1.0f,
            color: new Vector3(0.75f, 0.70f, 0.52f),
            medium: new Vector4(0.020f, 0.014f, 0.0f, 0.32f));
        fieldInstances[9] = FieldInstanceGpu.Ellipsoid(
            fieldId: 35.0f,
            flags: FieldFlags.Cloud | FieldFlags.Receiver,
            center: new Vector3(frame.Grid.Center.X - 0.4f, frame.Grid.Center.Y - 3.9f, -1.35f),
            radius: new Vector3(5.2f, 1.8f, 0.88f),
            angle: 0.46f,
            mediumId: 1.0f,
            color: new Vector3(0.30f, 0.50f, 0.68f),
            medium: new Vector4(0.018f, 0.012f, 0.0f, 0.35f));
    }

    private static Vector2 HaltonJitter(int index)
    {
        return new Vector2(Halton(index & 1023, 2) - 0.5f, Halton(index & 1023, 3) - 0.5f) * TemporalJitterScale;
    }

    private static float Halton(int index, int radix)
    {
        var result = 0.0f;
        var fraction = 1.0f / radix;
        while (index > 0)
        {
            result += (index % radix) * fraction;
            index /= radix;
            fraction /= radix;
        }

        return result;
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
        var sceneShaderBytecode = CompileShader(path, "AquariumScenePS", "ps_5_0");
        var bloomPrefilterShaderBytecode = CompileShader(path, "BloomPrefilterPS", "ps_5_0");
        var bloomDownsampleShaderBytecode = CompileShader(path, "BloomDownsamplePS", "ps_5_0");
        var bloomBlurHorizontalShaderBytecode = CompileShader(path, "BloomBlurHorizontalPS", "ps_5_0");
        var bloomBlurVerticalShaderBytecode = CompileShader(path, "BloomBlurVerticalPS", "ps_5_0");
        var resolveShaderBytecode = CompileShader(path, "AquariumResolvePS", "ps_5_0");

        return new ShaderSet(
            device.CreateVertexShader(vertexShaderBytecode.Span, null),
            device.CreatePixelShader(gridHeightShaderBytecode.Span, null),
            device.CreatePixelShader(sceneShaderBytecode.Span, null),
            device.CreatePixelShader(bloomPrefilterShaderBytecode.Span, null),
            device.CreatePixelShader(bloomDownsampleShaderBytecode.Span, null),
            device.CreatePixelShader(bloomBlurHorizontalShaderBytecode.Span, null),
            device.CreatePixelShader(bloomBlurVerticalShaderBytecode.Span, null),
            device.CreatePixelShader(resolveShaderBytecode.Span, null));
    }

    private readonly record struct FrameConstants(
        float2 Resolution,
        float TimeSeconds,
        float GridRadius,
        float3 CameraPosition,
        float FarDistance,
        float2 GridCenter,
        float FrameIndex,
        float PreviousTimeSeconds,
        float3 PreviousCameraPosition,
        float PreviousGridRadius,
        float2 PreviousGridCenter,
        float2 JitterPixels,
        float2 PreviousJitterPixels,
        float RenderDebugMode,
        float Exposure,
        float BloomIntensity,
        float BloomVeilIntensity,
        float2 Padding);

    private readonly record struct FroxelCell(int X, int Y, int Z);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct Int4(int X, int Y, int Z, int W);

    [Flags]
    private enum FieldFlags
    {
        Solid = 1,
        Cloud = 2,
        Hybrid = 4,
        Emitter = 8,
        ShadowCaster = 16,
        Receiver = 32,
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct FieldInstanceGpu(
        Vector4 CenterRadius,
        Vector4 RadiusAngle,
        Vector4 FieldFlags,
        Vector4 MaterialMedium,
        Vector4 ColorIntensity,
        Vector4 MediumTerms)
    {
        public static FieldInstanceGpu Sphere(
            float fieldId,
            FieldFlags flags,
            Vector3 center,
            float radius,
            float materialId,
            float mediumId,
            Vector3 color,
            Vector4 medium)
        {
            return new FieldInstanceGpu(
                new Vector4(center, radius),
                new Vector4(radius, radius, radius, 0.0f),
                new Vector4(fieldId, (float)flags, 1.0f, 0.0f),
                new Vector4(materialId, mediumId, 0.0f, 0.0f),
                new Vector4(color, 1.0f),
                medium);
        }

        public static FieldInstanceGpu Ellipsoid(
            float fieldId,
            FieldFlags flags,
            Vector3 center,
            Vector3 radius,
            float angle,
            float mediumId,
            Vector3 color,
            Vector4 medium)
        {
            return new FieldInstanceGpu(
                new Vector4(center, MathF.Max(MathF.Max(radius.X, radius.Y), radius.Z)),
                new Vector4(radius, angle),
                new Vector4(fieldId, (float)flags, 2.0f, 0.0f),
                new Vector4(0.0f, mediumId, 0.0f, 0.0f),
                new Vector4(color, 1.0f),
                medium);
        }
    }

    private readonly record struct ShaderSet(
        ID3D11VertexShader VertexShader,
        ID3D11PixelShader GridHeightPixelShader,
        ID3D11PixelShader AquariumScenePixelShader,
        ID3D11PixelShader BloomPrefilterPixelShader,
        ID3D11PixelShader BloomDownsamplePixelShader,
        ID3D11PixelShader BloomBlurHorizontalPixelShader,
        ID3D11PixelShader BloomBlurVerticalPixelShader,
        ID3D11PixelShader AquariumResolvePixelShader);
}
