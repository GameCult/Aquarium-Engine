using Aquarium.Engine.Input;
using SharpGen.Runtime;
using Aquarium.Engine.Render.Ui;
using Vortice;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11on12;
using Vortice.Direct3D12;
using Vortice.DXGI;
using Vortice.Mathematics;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using ID3D11Device = Vortice.Direct3D11.ID3D11Device;
using ID3D11DeviceContext = Vortice.Direct3D11.ID3D11DeviceContext;
using ID3D11Resource = Vortice.Direct3D11.ID3D11Resource;
using D3D11DeviceCreationFlags = Vortice.Direct3D11.DeviceCreationFlags;
using D3D11BindFlags = Vortice.Direct3D11.BindFlags;

namespace Aquarium.Engine.Render;

public sealed class D3D12Renderer : IAquariumRenderer
{
    private const int BackBufferCount = 2;
    private const int GridHeightTextureSize = 128;
    private const int CursorPrimitiveId = PlanetCount + 1;
    private const Format GridHeightFormat = Format.R16_Float;
    private const int PlanetCount = 5;
    private const int GridHeightBrushCount = PlanetCount + 1;
    private const int BodyLightCount = 8;
    private const float GridTransparentMinZ = -1.85f;
    private const float GridTransparentMaxZ = 0.45f;
    private const int BloomLevelCount = 3;
    private const float SunRadius = 1.12f;
    private const float CursorBodyRadius = 0.56f;
    private const float CursorBodyBoundRadius = 0.72f;
    private const Format SceneHdrFormat = Format.R16G16B16A16_Float;
    private const string GridShaderRelativePath = "Render/Shaders/D3D12Grid.hlsl";
    private const string SceneShaderRelativePath = "Render/Shaders/D3D12Scene.hlsl";
    private const string PostShaderRelativePath = "Render/Shaders/D3D12Post.hlsl";
    private const string StudioPmremRelativePath = "Assets/Textures/studio2_pmrem.dds";
    private static readonly DebugUi.DebugUiOption[] RenderDebugOptions =
    [
        new(0, "Final"),
        new(1, "Raw Scene"),
        new(2, "History"),
        new(3, "History Age"),
        new(4, "History Weight"),
        new(5, "Temporal Control"),
        new(6, "Lane Identity"),
        new(7, "Bloom"),
        new(8, "Exposed Luminance"),
    ];

    private readonly IDXGIFactory4 factory;
    private readonly ID3D12Device device;
    private readonly ID3D12CommandQueue commandQueue;
    private readonly ID3D11Device overlayDevice;
    private readonly ID3D11DeviceContext overlayContext;
    private readonly ID3D11On12Device overlayOn12Device;
    private readonly IDXGISwapChain3 swapChain;
    private readonly D3D12ResourceRegistry resourceRegistry = new();
    private D3D12DescriptorArena renderTargetViewArena;
    private D3D12DescriptorArena staticShaderDescriptorArena;
    private readonly FrameResources[] frames = new FrameResources[BackBufferCount];
    private readonly ID3D12GraphicsCommandList commandList;
    private readonly ID3D12RootSignature fullscreenRootSignature;
    private ID3D12PipelineState? gridHeightBasePipelineState;
    private ID3D12PipelineState? gridHeightBrushPipelineState;
    private ID3D12PipelineState? scenePipelineState;
    private ID3D12PipelineState? bloomPrefilterPipelineState;
    private ID3D12PipelineState? bloomDownsamplePipelineState;
    private ID3D12PipelineState? bloomBlurHorizontalPipelineState;
    private ID3D12PipelineState? bloomBlurVerticalPipelineState;
    private ID3D12PipelineState? resolvePipelineState;
    private readonly ID3D12Fence fence;
    private readonly AutoResetEvent fenceEvent = new(false);
    private readonly DebugUi debugUi;
    private ID3D11Resource[] overlayWrappedBackBuffers = [];
    private DirectWriteOverlay[] overlays = [];
    private D3D12RenderTarget gridHeightRenderTarget;
    private D3D12RenderTarget sceneRenderTarget;
    private D3D12RenderTarget sceneMetadataRenderTarget;
    private D3D12RenderTarget sceneControlRenderTarget;
    private D3D12RenderTarget sceneEventColorRenderTarget;
    private D3D12RenderTarget sceneEventMetadataRenderTarget;
    private readonly D3D12RenderTarget[] historyRenderTargets = new D3D12RenderTarget[2];
    private readonly D3D12RenderTarget[] historyMetadataRenderTargets = new D3D12RenderTarget[2];
    private readonly D3D12RenderTarget[] historyControlRenderTargets = new D3D12RenderTarget[2];
    private readonly D3D12RenderTarget[] historyEventColorRenderTargets = new D3D12RenderTarget[2];
    private readonly D3D12RenderTarget[] historyEventMetadataRenderTargets = new D3D12RenderTarget[2];
    private readonly D3D12RenderTarget[] bloomRenderTargets = new D3D12RenderTarget[BloomLevelCount];
    private readonly D3D12RenderTarget[] bloomScratchTargets = new D3D12RenderTarget[BloomLevelCount];
    private readonly D3D12StructuredBuffer bodyLightBuffer;
    private readonly D3D12CubeTexture studioPmremTexture;
    private readonly BodyLightGpu[] bodyLights = new BodyLightGpu[BodyLightCount];
    private readonly GridHeightBrushCpu[] gridHeightBrushes = new GridHeightBrushCpu[GridHeightBrushCount];
    private Viewport viewport;
    private RawRect scissorRect;
    private int width;
    private int height;
    private ulong fenceValue;
    private int temporalFrameIndex;
    private int frameIndex;
    private Vector3 previousCameraPosition;
    private Vector2 previousGridCenter;
    private Vector2 previousCursorWorld;
    private float previousGridRadius = 0.001f;
    private float previousTimeSeconds;
    private GridHeightBrushConstants gridHeightBrushConstants;
    private GraphicsSettings settings = GraphicsSettings.Default;
    private double accumulatedFrameCpuMilliseconds;
    private double accumulatedRecordCpuMilliseconds;
    private double accumulatedOverlayCpuMilliseconds;
    private int accumulatedTimingFrames;
    private readonly string shaderSourceRoot;
    private readonly D3D12ShaderPaths shaderPaths;
    private readonly Stopwatch shaderReloadClock = Stopwatch.StartNew();
    private readonly Dictionary<string, DateTime> shaderWriteTimesUtc = new(StringComparer.OrdinalIgnoreCase);
    private Task<D3D12PipelineSet>? pipelineBuildTask;
    private TimeSpan lastShaderReloadCheck;
    private bool shaderReloadFailureReported;
    private bool pipelineBuildInProgressReported;
    private bool initialPipelineReadyReported;
    private bool hasPresentedReadyFrame;
    private static readonly TimeSpan ShaderReloadPollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan ShaderReloadWriteSettleTime = TimeSpan.FromMilliseconds(300);

    public D3D12Renderer(
        IntPtr windowHandle,
        int width,
        int height,
        string? shaderPath = null,
        GraphicsSettings? graphicsSettings = null,
        Action<string>? startupProgress = null)
    {
        ApplyGraphicsSettings(graphicsSettings ?? GraphicsSettings.Default);
        this.width = width;
        this.height = height;
        shaderSourceRoot = ResolveShaderSourceRoot(shaderPath);
        shaderPaths = D3D12ShaderPaths.FromRoot(shaderSourceRoot);
        ReportStartupProgress(startupProgress, "Creating D3D12 device and swapchain");

        factory = DXGI.CreateDXGIFactory2<IDXGIFactory4>(false);
        device = D3D12.D3D12CreateDevice<ID3D12Device>(IntPtr.Zero, FeatureLevel.Level_11_0);
        device.Name = "Aquarium D3D12 Device";
        commandQueue = device.CreateCommandQueue(new CommandQueueDescription(CommandListType.Direct));
        commandQueue.Name = "Aquarium D3D12 Direct Queue";
        CreateOverlayDevice(out overlayDevice, out overlayContext, out overlayOn12Device);

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
        CreateBackBufferOverlays();
        debugUi = CreateDebugUi();
        gridHeightRenderTarget = CreateGridHeightRenderTarget();
        sceneRenderTarget = CreateSceneRenderTarget();
        sceneMetadataRenderTarget = CreateSceneAuxiliaryRenderTarget("scene-metadata-target", "Aquarium D3D12 Scene Metadata Target");
        sceneControlRenderTarget = CreateSceneAuxiliaryRenderTarget("scene-control-target", "Aquarium D3D12 Scene Control Target");
        sceneEventColorRenderTarget = CreateSceneAuxiliaryRenderTarget("scene-event-color-target", "Aquarium D3D12 Scene Event Color Target");
        sceneEventMetadataRenderTarget = CreateSceneAuxiliaryRenderTarget("scene-event-metadata-target", "Aquarium D3D12 Scene Event Metadata Target");
        CreateHistoryRenderTargets();
        CreateBloomRenderTargets();
        bodyLightBuffer = new D3D12StructuredBuffer(device, BodyLightCount, Marshal.SizeOf<BodyLightGpu>(), "Aquarium D3D12 Body Light Buffer");
        resourceRegistry.Add("body-light-buffer", bodyLightBuffer);
        commandList = device.CreateCommandList<ID3D12GraphicsCommandList>(0, CommandListType.Direct, frames[frameIndex].CommandAllocator, null);
        commandList.Name = "Aquarium D3D12 Graphics Command List";
        commandList.Close();
        fence = device.CreateFence(0);
        fence.Name = "Aquarium D3D12 Frame Fence";
        commandQueue.ExecuteCommandList(commandList);
        WaitForGpu();
        ReportStartupProgress(startupProgress, "Loading studio PMREM cubemap");
        studioPmremTexture = LoadStudioPmremTexture();
        resourceRegistry.Add("studio-pmrem-cubemap", studioPmremTexture);
        ReportStartupProgress(startupProgress, "Creating D3D12 render pipelines");
        fullscreenRootSignature = CreateFullscreenRootSignature();
        fullscreenRootSignature.Name = "Aquarium D3D12 Fullscreen Root Signature";
        CaptureShaderWriteTimes();
        StartPipelineBuild("initial");
        viewport = new Viewport(0.0f, 0.0f, width, height);
        scissorRect = new RawRect(0, 0, width, height);
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
        get => debugUi.IsVisible;
        set => debugUi.IsVisible = value;
    }

    public bool HasPresentedReadyFrame => hasPresentedReadyFrame;

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
        return settings.Normalized();
    }

    public void ApplyGraphicsSettings(GraphicsSettings graphicsSettings)
    {
        settings = graphicsSettings.Normalized();
    }

    private DebugUi CreateDebugUi()
    {
        return new DebugUi("Aquarium Debug")
            .Panel(panel => panel
                .Section("View")
                .Options("Render Debug", () => RenderDebugMode, value => RenderDebugMode = Math.Clamp(value, GraphicsSettings.MinRenderDebugMode, GraphicsSettings.MaxRenderDebugMode), RenderDebugOptions, "Selects the active renderer debug view.")
                .Button("Reset View", () => RenderDebugMode = 0, "Returns to the final presented frame.")
                .Section("HDR")
                .Slider("Exposure", () => settings.SceneExposure, value => settings = (settings with { SceneExposure = Math.Clamp(value, GraphicsSettings.MinSceneExposure, GraphicsSettings.MaxSceneExposure) }).Normalized(), GraphicsSettings.MinSceneExposure, GraphicsSettings.MaxSceneExposure, "0.###", "Manual scene exposure before display transform.")
                .Slider("Bloom Intensity", () => settings.BloomIntensity, value => settings = (settings with { BloomIntensity = Math.Clamp(value, GraphicsSettings.MinBloomIntensity, GraphicsSettings.MaxBloomIntensity) }).Normalized(), GraphicsSettings.MinBloomIntensity, GraphicsSettings.MaxBloomIntensity, "0.###", "Strength of pre-tonemap bloom energy.")
                .Slider("Bloom Veil", () => settings.BloomVeilIntensity, value => settings = (settings with { BloomVeilIntensity = Math.Clamp(value, GraphicsSettings.MinBloomVeilIntensity, GraphicsSettings.MaxBloomVeilIntensity) }).Normalized(), GraphicsSettings.MinBloomVeilIntensity, GraphicsSettings.MaxBloomVeilIntensity, "0.###", "Low-frequency veil from bright HDR energy."));
    }

    public void Render(AquariumFrame frame, int width, int height)
    {
        var frameCpuStart = Stopwatch.GetTimestamp();
        ResizeIfNeeded(width, height);
        ApplyCompletedPipelineBuild();
        TryHotReloadShaders();
        var frameResources = frames[frameIndex];
        WaitForFrame(frameResources);
        frameResources.UploadRing.Reset();
        frameResources.TransientShaderDescriptors.Reset();

        if (!PipelinesReady)
        {
            RenderPipelineLoadingFrame(frame, frameResources, frameCpuStart);
            return;
        }

        var gridOrigin = new Vector3(frame.Grid.Center.X, frame.Grid.Center.Y, 0.0f);
        var farDistance = Vector3.Distance(frame.CameraPosition, gridOrigin) + MathF.Max(frame.Grid.Radius, 0.001f);
        if (temporalFrameIndex == 0)
        {
            previousCameraPosition = frame.CameraPosition;
            previousGridCenter = frame.Grid.Center;
            previousCursorWorld = frame.CursorWorld;
            previousGridRadius = frame.Grid.Radius;
            previousTimeSeconds = frame.TimeSeconds;
        }

        BuildGridHeightBrushes(frame);
        BuildBodyLightTable();
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
            new Vector4(frame.CursorWorld.X, frame.CursorWorld.Y, previousCursorWorld.X, previousCursorWorld.Y)));
        frameResources.FrameConstantsDescriptor = frameResources.TransientShaderDescriptors.Allocate();
        device.CreateConstantBufferView(
            new ConstantBufferViewDescription(frameConstants.GpuVirtualAddress, frameConstants.SizeInBytes),
            frameResources.FrameConstantsDescriptor.Cpu);
        var gridBrushConstants = frameResources.UploadRing.WriteConstant(gridHeightBrushConstants);
        frameResources.GridBrushConstantsDescriptor = frameResources.TransientShaderDescriptors.Allocate();
        device.CreateConstantBufferView(
            new ConstantBufferViewDescription(gridBrushConstants.GpuVirtualAddress, gridBrushConstants.SizeInBytes),
            frameResources.GridBrushConstantsDescriptor.Cpu);

        frameResources.BodyLightDescriptor = frameResources.TransientShaderDescriptors.Allocate();
        bodyLightBuffer.CreateShaderResourceView(device, frameResources.BodyLightDescriptor);
        frameResources.StudioPmremDescriptor = frameResources.TransientShaderDescriptors.Allocate();
        studioPmremTexture.CreateShaderResourceView(device, frameResources.StudioPmremDescriptor);
        frameResources.GridHeightDescriptor = frameResources.TransientShaderDescriptors.Allocate();
        gridHeightRenderTarget.CreateShaderResourceView(device, frameResources.GridHeightDescriptor);
        frameResources.SceneDescriptor = frameResources.TransientShaderDescriptors.Allocate();
        sceneRenderTarget.CreateShaderResourceView(device, frameResources.SceneDescriptor);
        frameResources.SceneMetadataDescriptor = frameResources.TransientShaderDescriptors.Allocate();
        sceneMetadataRenderTarget.CreateShaderResourceView(device, frameResources.SceneMetadataDescriptor);
        frameResources.SceneControlDescriptor = frameResources.TransientShaderDescriptors.Allocate();
        sceneControlRenderTarget.CreateShaderResourceView(device, frameResources.SceneControlDescriptor);
        frameResources.SceneEventColorDescriptor = frameResources.TransientShaderDescriptors.Allocate();
        sceneEventColorRenderTarget.CreateShaderResourceView(device, frameResources.SceneEventColorDescriptor);
        frameResources.SceneEventMetadataDescriptor = frameResources.TransientShaderDescriptors.Allocate();
        sceneEventMetadataRenderTarget.CreateShaderResourceView(device, frameResources.SceneEventMetadataDescriptor);
        var historyReadIndex = temporalFrameIndex & 1;
        frameResources.HistoryDescriptor = frameResources.TransientShaderDescriptors.Allocate();
        historyRenderTargets[historyReadIndex].CreateShaderResourceView(device, frameResources.HistoryDescriptor);
        frameResources.HistoryMetadataDescriptor = frameResources.TransientShaderDescriptors.Allocate();
        historyMetadataRenderTargets[historyReadIndex].CreateShaderResourceView(device, frameResources.HistoryMetadataDescriptor);
        frameResources.HistoryControlDescriptor = frameResources.TransientShaderDescriptors.Allocate();
        historyControlRenderTargets[historyReadIndex].CreateShaderResourceView(device, frameResources.HistoryControlDescriptor);
        frameResources.HistoryEventColorDescriptor = frameResources.TransientShaderDescriptors.Allocate();
        historyEventColorRenderTargets[historyReadIndex].CreateShaderResourceView(device, frameResources.HistoryEventColorDescriptor);
        frameResources.HistoryEventMetadataDescriptor = frameResources.TransientShaderDescriptors.Allocate();
        historyEventMetadataRenderTargets[historyReadIndex].CreateShaderResourceView(device, frameResources.HistoryEventMetadataDescriptor);
        for (var level = 0; level < BloomLevelCount; level++)
        {
            frameResources.BloomDescriptors[level] = frameResources.TransientShaderDescriptors.Allocate();
            bloomRenderTargets[level].CreateShaderResourceView(device, frameResources.BloomDescriptors[level]);
            frameResources.BloomScratchDescriptors[level] = frameResources.TransientShaderDescriptors.Allocate();
            bloomScratchTargets[level].CreateShaderResourceView(device, frameResources.BloomScratchDescriptors[level]);
        }

        frameResources.BloomPresentationDescriptor = frameResources.TransientShaderDescriptors.Allocate();
        bloomRenderTargets[0].CreateShaderResourceView(device, frameResources.BloomPresentationDescriptor);
        for (var level = 1; level < BloomLevelCount; level++)
        {
            var descriptor = frameResources.TransientShaderDescriptors.Allocate();
            bloomRenderTargets[level].CreateShaderResourceView(device, descriptor);
        }

        frameResources.CommandAllocator.Reset();
        commandList.Reset(frameResources.CommandAllocator, null);

        var recordCpuStart = Stopwatch.GetTimestamp();
        commandList.BeginEvent("Aquarium D3D12 Frame");
        UploadBodyLightResources(commandList, frameResources);
        RenderGridHeight(commandList, frameResources);
        RenderSceneAndPresent(new D3D12PassContext(commandList, frameResources.BackBuffer, frameResources.BackBufferRenderTargetView.Cpu), frameResources);
        commandList.EndEvent();
        commandList.Close();
        var recordCpuMilliseconds = ElapsedMilliseconds(recordCpuStart);

        commandQueue.ExecuteCommandList(commandList);
        var overlayCpuStart = Stopwatch.GetTimestamp();
        RenderOverlay(frame, frameResources);
        var overlayCpuMilliseconds = ElapsedMilliseconds(overlayCpuStart);
        swapChain.Present(1, PresentFlags.None);
        var frameCpuMilliseconds = ElapsedMilliseconds(frameCpuStart);
        if (temporalFrameIndex > 4)
        {
            accumulatedRecordCpuMilliseconds += recordCpuMilliseconds;
            accumulatedOverlayCpuMilliseconds += overlayCpuMilliseconds;
            accumulatedFrameCpuMilliseconds += frameCpuMilliseconds;
            accumulatedTimingFrames++;
        }
        SignalFrame(frameResources);
        ReportCapacityOncePerSecond(frame.TimeSeconds, frameResources);
        previousCameraPosition = frame.CameraPosition;
        previousGridCenter = frame.Grid.Center;
        previousCursorWorld = frame.CursorWorld;
        previousGridRadius = frame.Grid.Radius;
        previousTimeSeconds = frame.TimeSeconds;
        hasPresentedReadyFrame = true;
        temporalFrameIndex++;
        frameIndex = (int)swapChain.CurrentBackBufferIndex;
    }

    private bool PipelinesReady =>
        gridHeightBasePipelineState is not null
        && gridHeightBrushPipelineState is not null
        && scenePipelineState is not null
        && bloomPrefilterPipelineState is not null
        && bloomDownsamplePipelineState is not null
        && bloomBlurHorizontalPipelineState is not null
        && bloomBlurVerticalPipelineState is not null
        && resolvePipelineState is not null;

    private void RenderPipelineLoadingFrame(AquariumFrame frame, FrameResources frameResources, long frameCpuStart)
    {
        var frameCpuMilliseconds = ElapsedMilliseconds(frameCpuStart);
        accumulatedFrameCpuMilliseconds += frameCpuMilliseconds;
        accumulatedTimingFrames++;
        ReportCapacityOncePerSecond(frame.TimeSeconds, frameResources);
        Thread.Sleep(8);
    }

    public void Dispose()
    {
        WaitForGpu();
        try
        {
            pipelineBuildTask?.Wait(TimeSpan.FromMilliseconds(50));
        }
        catch
        {
            // A failed background compile has already been reported or will be discarded with the renderer.
        }

        fenceEvent.Dispose();
        fence.Dispose();
        commandList.Dispose();
        DisposeBackBufferOverlays();
        overlayOn12Device.Dispose();
        overlayContext.Dispose();
        overlayDevice.Dispose();
        DisposePipelineStates();
        fullscreenRootSignature.Dispose();
        studioPmremTexture.Dispose();
        bodyLightBuffer.Dispose();
        DisposeBloomRenderTargets();
        DisposeHistoryRenderTargets();
        sceneRenderTarget.Dispose();
        sceneMetadataRenderTarget.Dispose();
        sceneControlRenderTarget.Dispose();
        sceneEventColorRenderTarget.Dispose();
        sceneEventMetadataRenderTarget.Dispose();
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

    private D3D12CubeTexture LoadStudioPmremTexture()
    {
        var path = Path.Combine(AppContext.BaseDirectory, StudioPmremRelativePath);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Studio PMREM cubemap asset was not copied to the runtime asset directory.", path);
        }

        var frameResources = frames[frameIndex];
        frameResources.CommandAllocator.Reset();
        commandList.Reset(frameResources.CommandAllocator, null);
        var texture = D3D12CubeTexture.LoadRgba16FloatDds(device, commandList, path, "Aquarium D3D12 Studio PMREM Cubemap", out var uploadResource);
        commandList.Close();
        commandQueue.ExecuteCommandList(commandList);
        WaitForGpu();
        uploadResource.Dispose();
        return texture;
    }

    private void CreateOverlayDevice(out ID3D11Device createdDevice, out ID3D11DeviceContext createdContext, out ID3D11On12Device createdOn12Device)
    {
        var featureLevels = new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 };
        var commandQueues = new IUnknown[] { commandQueue };
        Apis.D3D11On12CreateDevice(
            device,
            D3D11DeviceCreationFlags.BgraSupport,
            featureLevels,
            commandQueues,
            0,
            out createdDevice,
            out createdContext,
            out _).CheckError();
        createdOn12Device = createdDevice.QueryInterface<ID3D11On12Device>();
    }

    private void CreateBackBufferOverlays()
    {
        overlayWrappedBackBuffers = new ID3D11Resource[frames.Length];
        overlays = new DirectWriteOverlay[frames.Length];
        var flags = new Vortice.Direct3D11on12.ResourceFlags
        {
            BindFlags = D3D11BindFlags.RenderTarget,
        };

        for (var index = 0; index < frames.Length; index++)
        {
            var wrapped = overlayOn12Device.CreateWrappedResource<ID3D11Resource>(
                frames[index].BackBuffer.Resource,
                flags,
                ResourceStates.RenderTarget,
                ResourceStates.Present);
            overlayWrappedBackBuffers[index] = wrapped;
            using var surface = wrapped.QueryInterface<IDXGISurface>();
            overlays[index] = new DirectWriteOverlay(surface, width, height);
        }
    }

    private void RenderOverlay(AquariumFrame frame, FrameResources frameResources)
    {
        var wrappedBackBuffer = overlayWrappedBackBuffers[frameIndex];
        overlayOn12Device.AcquireWrappedResources([wrappedBackBuffer]);
        overlays[frameIndex].Render(frame, RenderDebugMode, debugUi);
        overlayOn12Device.ReleaseWrappedResources([wrappedBackBuffer]);
        overlayContext.Flush();
        frameResources.BackBuffer.MarkState(ResourceStates.Present);
    }

    private void DisposeBackBufferOverlays()
    {
        foreach (var overlay in overlays)
        {
            overlay?.Dispose();
        }

        foreach (var wrappedBackBuffer in overlayWrappedBackBuffers)
        {
            wrappedBackBuffer?.Dispose();
        }

        overlays = [];
        overlayWrappedBackBuffers = [];
    }

    private D3D12PipelineSet CreatePipelineSet(D3D12ShaderPaths paths)
    {
        var gridHeightBase = CreateGridHeightBasePipelineState(paths.Grid);
        gridHeightBase.Name = "Aquarium D3D12 Grid Height Base Pipeline";
        var gridHeightBrush = CreateGridHeightBrushPipelineState(paths.Grid);
        gridHeightBrush.Name = "Aquarium D3D12 Grid Height Brush Pipeline";
        var scene = CreateScenePipelineState(paths.Scene);
        scene.Name = "Aquarium D3D12 Scene Pipeline";
        var bloomPrefilter = CreateBloomPrefilterPipelineState(paths.Post);
        bloomPrefilter.Name = "Aquarium D3D12 Bloom Prefilter Pipeline";
        var bloomDownsample = CreateBloomDownsamplePipelineState(paths.Post);
        bloomDownsample.Name = "Aquarium D3D12 Bloom Downsample Pipeline";
        var bloomBlurHorizontal = CreateBloomBlurHorizontalPipelineState(paths.Post);
        bloomBlurHorizontal.Name = "Aquarium D3D12 Bloom Blur Horizontal Pipeline";
        var bloomBlurVertical = CreateBloomBlurVerticalPipelineState(paths.Post);
        bloomBlurVertical.Name = "Aquarium D3D12 Bloom Blur Vertical Pipeline";
        var resolve = CreateResolvePipelineState(paths.Post);
        resolve.Name = "Aquarium D3D12 Resolve Pipeline";

        return new D3D12PipelineSet(
            gridHeightBase,
            gridHeightBrush,
            scene,
            bloomPrefilter,
            bloomDownsample,
            bloomBlurHorizontal,
            bloomBlurVertical,
            resolve);
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
        DisposeBackBufferOverlays();
        RemoveBloomRenderTargets();
        RemoveHistoryRenderTargets();
        resourceRegistry.RemoveRenderTarget("scene-hdr-target");
        resourceRegistry.RemoveRenderTarget("scene-metadata-target");
        resourceRegistry.RemoveRenderTarget("scene-control-target");
        resourceRegistry.RemoveRenderTarget("scene-event-color-target");
        resourceRegistry.RemoveRenderTarget("scene-event-metadata-target");
        resourceRegistry.RemoveRenderTarget("grid-height-target");
        DisposeBloomRenderTargets();
        DisposeHistoryRenderTargets();
        sceneRenderTarget.Dispose();
        sceneMetadataRenderTarget.Dispose();
        sceneControlRenderTarget.Dispose();
        sceneEventColorRenderTarget.Dispose();
        sceneEventMetadataRenderTarget.Dispose();
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
        CreateBackBufferOverlays();
        gridHeightRenderTarget = CreateGridHeightRenderTarget();
        sceneRenderTarget = CreateSceneRenderTarget();
        sceneMetadataRenderTarget = CreateSceneAuxiliaryRenderTarget("scene-metadata-target", "Aquarium D3D12 Scene Metadata Target");
        sceneControlRenderTarget = CreateSceneAuxiliaryRenderTarget("scene-control-target", "Aquarium D3D12 Scene Control Target");
        sceneEventColorRenderTarget = CreateSceneAuxiliaryRenderTarget("scene-event-color-target", "Aquarium D3D12 Scene Event Color Target");
        sceneEventMetadataRenderTarget = CreateSceneAuxiliaryRenderTarget("scene-event-metadata-target", "Aquarium D3D12 Scene Event Metadata Target");
        CreateHistoryRenderTargets();
        CreateBloomRenderTargets();
        viewport = new Viewport(0.0f, 0.0f, width, height);
        scissorRect = new RawRect(0, 0, width, height);
        Console.WriteLine($"D3D12 resized: {width}x{height}; {resourceRegistry.Describe()}");
    }

    private D3D12RenderTarget CreateSceneRenderTarget()
    {
        var target = new D3D12RenderTarget(
            device,
            width,
            height,
            SceneHdrFormat,
            renderTargetViewArena.Allocate(),
            staticShaderDescriptorArena.Allocate(),
            null,
            false,
            new Color4(0.0f, 0.0f, 0.0f, 1.0f),
            "Aquarium D3D12 Scene HDR Target");
        resourceRegistry.Add("scene-hdr-target", target);
        return target;
    }

    private D3D12RenderTarget CreateSceneAuxiliaryRenderTarget(string registryName, string resourceName)
    {
        var target = new D3D12RenderTarget(
            device,
            width,
            height,
            SceneHdrFormat,
            renderTargetViewArena.Allocate(),
            staticShaderDescriptorArena.Allocate(),
            null,
            false,
            new Color4(0.0f, 0.0f, 0.0f, 1.0f),
            resourceName);
        resourceRegistry.Add(registryName, target);
        return target;
    }

    private void CreateBloomRenderTargets()
    {
        for (var level = 0; level < BloomLevelCount; level++)
        {
            bloomRenderTargets[level] = CreateBloomRenderTarget(level, false);
            bloomScratchTargets[level] = CreateBloomRenderTarget(level, true);
        }
    }

    private void CreateHistoryRenderTargets()
    {
        for (var index = 0; index < 2; index++)
        {
            historyRenderTargets[index] = CreateSceneAuxiliaryRenderTarget($"history-{index}", $"Aquarium D3D12 History {index}");
            historyMetadataRenderTargets[index] = CreateSceneAuxiliaryRenderTarget($"history-metadata-{index}", $"Aquarium D3D12 History Metadata {index}");
            historyControlRenderTargets[index] = CreateSceneAuxiliaryRenderTarget($"history-control-{index}", $"Aquarium D3D12 History Control {index}");
            historyEventColorRenderTargets[index] = CreateSceneAuxiliaryRenderTarget($"history-event-color-{index}", $"Aquarium D3D12 History Event Color {index}");
            historyEventMetadataRenderTargets[index] = CreateSceneAuxiliaryRenderTarget($"history-event-metadata-{index}", $"Aquarium D3D12 History Event Metadata {index}");
        }
    }

    private void RemoveHistoryRenderTargets()
    {
        for (var index = 0; index < 2; index++)
        {
            resourceRegistry.RemoveRenderTarget($"history-{index}");
            resourceRegistry.RemoveRenderTarget($"history-metadata-{index}");
            resourceRegistry.RemoveRenderTarget($"history-control-{index}");
            resourceRegistry.RemoveRenderTarget($"history-event-color-{index}");
            resourceRegistry.RemoveRenderTarget($"history-event-metadata-{index}");
        }
    }

    private void DisposeHistoryRenderTargets()
    {
        for (var index = 0; index < 2; index++)
        {
            historyRenderTargets[index]?.Dispose();
            historyMetadataRenderTargets[index]?.Dispose();
            historyControlRenderTargets[index]?.Dispose();
            historyEventColorRenderTargets[index]?.Dispose();
            historyEventMetadataRenderTargets[index]?.Dispose();
        }
    }

    private D3D12RenderTarget CreateBloomRenderTarget(int level, bool scratch)
    {
        var target = new D3D12RenderTarget(
            device,
            Math.Max(1, width >> (level + 1)),
            Math.Max(1, height >> (level + 1)),
            SceneHdrFormat,
            renderTargetViewArena.Allocate(),
            staticShaderDescriptorArena.Allocate(),
            null,
            false,
            new Color4(0.0f, 0.0f, 0.0f, 1.0f),
            scratch
                ? $"Aquarium D3D12 Bloom Scratch L{level}"
                : $"Aquarium D3D12 Bloom L{level}");
        resourceRegistry.Add(scratch ? $"bloom-scratch-{level}" : $"bloom-{level}", target);
        return target;
    }

    private void RemoveBloomRenderTargets()
    {
        for (var level = 0; level < BloomLevelCount; level++)
        {
            resourceRegistry.RemoveRenderTarget($"bloom-{level}");
            resourceRegistry.RemoveRenderTarget($"bloom-scratch-{level}");
        }
    }

    private void DisposeBloomRenderTargets()
    {
        for (var level = 0; level < BloomLevelCount; level++)
        {
            bloomRenderTargets[level]?.Dispose();
            bloomScratchTargets[level]?.Dispose();
        }
    }

    private D3D12RenderTarget CreateGridHeightRenderTarget()
    {
        var target = new D3D12RenderTarget(
            device,
            GridHeightTextureSize,
            GridHeightTextureSize,
            GridHeightFormat,
            renderTargetViewArena.Allocate(),
            staticShaderDescriptorArena.Allocate(),
            null,
            false,
            new Color4(0.0f, 0.0f, 0.0f, 1.0f),
            "Aquarium D3D12 Grid Height Target");
        resourceRegistry.Add("grid-height-target", target);
        return target;
    }

    private void RenderSceneAndPresent(D3D12PassContext context, FrameResources frameResources)
    {
            context.CommandList.BeginEvent("Scene Pass");
        try
        {
            gridHeightRenderTarget.Transition(context.CommandList, ResourceStates.PixelShaderResource);
            sceneRenderTarget.Transition(context.CommandList, ResourceStates.RenderTarget);
            sceneMetadataRenderTarget.Transition(context.CommandList, ResourceStates.RenderTarget);
            sceneControlRenderTarget.Transition(context.CommandList, ResourceStates.RenderTarget);
            sceneEventColorRenderTarget.Transition(context.CommandList, ResourceStates.RenderTarget);
            sceneEventMetadataRenderTarget.Transition(context.CommandList, ResourceStates.RenderTarget);
            context.CommandList.ClearRenderTargetView(sceneRenderTarget.RenderTargetView.Cpu, new Color4(0.0f, 0.0f, 0.0f, 1.0f));
            context.CommandList.ClearRenderTargetView(sceneMetadataRenderTarget.RenderTargetView.Cpu, new Color4(0.0f, 0.0f, 0.0f, 1.0f));
            context.CommandList.ClearRenderTargetView(sceneControlRenderTarget.RenderTargetView.Cpu, new Color4(0.0f, 0.0f, 0.0f, 1.0f));
            context.CommandList.ClearRenderTargetView(sceneEventColorRenderTarget.RenderTargetView.Cpu, new Color4(0.0f, 0.0f, 0.0f, 1.0f));
            context.CommandList.ClearRenderTargetView(sceneEventMetadataRenderTarget.RenderTargetView.Cpu, new Color4(0.0f, 0.0f, 0.0f, 1.0f));
            context.CommandList.SetDescriptorHeaps(frameResources.TransientShaderDescriptors.Heap);
            context.CommandList.SetPipelineState(scenePipelineState!);
            context.CommandList.SetGraphicsRootSignature(fullscreenRootSignature);
            context.CommandList.SetGraphicsRootDescriptorTable(0, frameResources.FrameConstantsDescriptor.Gpu);
            context.CommandList.SetGraphicsRootDescriptorTable(1, frameResources.GridHeightDescriptor.Gpu);
            context.CommandList.SetGraphicsRootDescriptorTable(3, frameResources.BodyLightDescriptor.Gpu);
            context.CommandList.SetGraphicsRootDescriptorTable(14, frameResources.StudioPmremDescriptor.Gpu);
            context.CommandList.RSSetViewports(viewport);
            context.CommandList.RSSetScissorRects(scissorRect);
            context.CommandList.OMSetRenderTargets(
            [
                sceneRenderTarget.RenderTargetView.Cpu,
                sceneMetadataRenderTarget.RenderTargetView.Cpu,
                sceneControlRenderTarget.RenderTargetView.Cpu,
                sceneEventColorRenderTarget.RenderTargetView.Cpu,
                sceneEventMetadataRenderTarget.RenderTargetView.Cpu,
            ],
            null);
            context.CommandList.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            context.CommandList.DrawInstanced(3, 1, 0, 0);
        }
        finally
        {
            context.CommandList.EndEvent();
        }

        RenderBloom(context.CommandList, frameResources);
        PresentBackBuffer(context, frameResources);
    }

    private void RenderBloom(ID3D12GraphicsCommandList activeCommandList, FrameResources frameResources)
    {
        activeCommandList.BeginEvent("Bloom Pyramid");
        try
        {
            activeCommandList.SetDescriptorHeaps(frameResources.TransientShaderDescriptors.Heap);
            activeCommandList.SetGraphicsRootSignature(fullscreenRootSignature);
            activeCommandList.SetGraphicsRootDescriptorTable(0, frameResources.FrameConstantsDescriptor.Gpu);
            sceneRenderTarget.Transition(activeCommandList, ResourceStates.PixelShaderResource);

            for (var level = 0; level < BloomLevelCount; level++)
            {
                var sourceDescriptor = level == 0
                    ? frameResources.SceneDescriptor
                    : frameResources.BloomDescriptors[level - 1];
                var pipelineState = level == 0
                    ? bloomPrefilterPipelineState!
                    : bloomDownsamplePipelineState!;

                DrawPostToTarget(
                    activeCommandList,
                    bloomRenderTargets[level],
                    sourceDescriptor,
                    pipelineState);
                bloomRenderTargets[level].Transition(activeCommandList, ResourceStates.PixelShaderResource);

                DrawPostToTarget(
                    activeCommandList,
                    bloomScratchTargets[level],
                    frameResources.BloomDescriptors[level],
                    bloomBlurHorizontalPipelineState!);
                bloomScratchTargets[level].Transition(activeCommandList, ResourceStates.PixelShaderResource);

                DrawPostToTarget(
                    activeCommandList,
                    bloomRenderTargets[level],
                    frameResources.BloomScratchDescriptors[level],
                    bloomBlurVerticalPipelineState!);
                bloomRenderTargets[level].Transition(activeCommandList, ResourceStates.PixelShaderResource);
            }
        }
        finally
        {
            activeCommandList.EndEvent();
        }
    }

    private void DrawPostToTarget(
        ID3D12GraphicsCommandList activeCommandList,
        D3D12RenderTarget target,
        D3D12DescriptorSlot sourceDescriptor,
        ID3D12PipelineState pipelineState)
    {
        var targetViewport = new Viewport(0.0f, 0.0f, target.Width, target.Height);
        var targetScissorRect = new RawRect(0, 0, target.Width, target.Height);
        target.Transition(activeCommandList, ResourceStates.RenderTarget);
        activeCommandList.ClearRenderTargetView(target.RenderTargetView.Cpu, new Color4(0.0f, 0.0f, 0.0f, 1.0f));
        activeCommandList.SetPipelineState(pipelineState);
        activeCommandList.SetGraphicsRootDescriptorTable(1, sourceDescriptor.Gpu);
        activeCommandList.RSSetViewports(targetViewport);
        activeCommandList.RSSetScissorRects(targetScissorRect);
        activeCommandList.OMSetRenderTargets(target.RenderTargetView.Cpu, null);
        activeCommandList.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        activeCommandList.DrawInstanced(3, 1, 0, 0);
    }

    private void PresentBackBuffer(D3D12PassContext context, FrameResources frameResources)
    {
        context.CommandList.BeginEvent("Present Pass");
        try
        {
            var historyReadIndex = temporalFrameIndex & 1;
            var historyWriteIndex = 1 - historyReadIndex;

            sceneRenderTarget.Transition(context.CommandList, ResourceStates.PixelShaderResource);
            sceneMetadataRenderTarget.Transition(context.CommandList, ResourceStates.PixelShaderResource);
            sceneControlRenderTarget.Transition(context.CommandList, ResourceStates.PixelShaderResource);
            sceneEventColorRenderTarget.Transition(context.CommandList, ResourceStates.PixelShaderResource);
            sceneEventMetadataRenderTarget.Transition(context.CommandList, ResourceStates.PixelShaderResource);
            historyRenderTargets[historyReadIndex].Transition(context.CommandList, ResourceStates.PixelShaderResource);
            historyMetadataRenderTargets[historyReadIndex].Transition(context.CommandList, ResourceStates.PixelShaderResource);
            historyControlRenderTargets[historyReadIndex].Transition(context.CommandList, ResourceStates.PixelShaderResource);
            historyEventColorRenderTargets[historyReadIndex].Transition(context.CommandList, ResourceStates.PixelShaderResource);
            historyEventMetadataRenderTargets[historyReadIndex].Transition(context.CommandList, ResourceStates.PixelShaderResource);
            historyRenderTargets[historyWriteIndex].Transition(context.CommandList, ResourceStates.RenderTarget);
            historyMetadataRenderTargets[historyWriteIndex].Transition(context.CommandList, ResourceStates.RenderTarget);
            historyControlRenderTargets[historyWriteIndex].Transition(context.CommandList, ResourceStates.RenderTarget);
            historyEventColorRenderTargets[historyWriteIndex].Transition(context.CommandList, ResourceStates.RenderTarget);
            historyEventMetadataRenderTargets[historyWriteIndex].Transition(context.CommandList, ResourceStates.RenderTarget);
            context.CommandList.ClearRenderTargetView(historyRenderTargets[historyWriteIndex].RenderTargetView.Cpu, new Color4(0.0f, 0.0f, 0.0f, 1.0f));
            context.CommandList.ClearRenderTargetView(historyMetadataRenderTargets[historyWriteIndex].RenderTargetView.Cpu, new Color4(0.0f, 0.0f, 0.0f, 1.0f));
            context.CommandList.ClearRenderTargetView(historyControlRenderTargets[historyWriteIndex].RenderTargetView.Cpu, new Color4(0.0f, 0.0f, 0.0f, 1.0f));
            context.CommandList.ClearRenderTargetView(historyEventColorRenderTargets[historyWriteIndex].RenderTargetView.Cpu, new Color4(0.0f, 0.0f, 0.0f, 1.0f));
            context.CommandList.ClearRenderTargetView(historyEventMetadataRenderTargets[historyWriteIndex].RenderTargetView.Cpu, new Color4(0.0f, 0.0f, 0.0f, 1.0f));

            context.BackBuffer.Transition(context.CommandList, ResourceStates.RenderTarget);
            context.CommandList.SetDescriptorHeaps(frameResources.TransientShaderDescriptors.Heap);
            context.CommandList.SetPipelineState(resolvePipelineState!);
            context.CommandList.SetGraphicsRootSignature(fullscreenRootSignature);
            context.CommandList.SetGraphicsRootDescriptorTable(0, frameResources.FrameConstantsDescriptor.Gpu);
            context.CommandList.SetGraphicsRootDescriptorTable(1, frameResources.SceneDescriptor.Gpu);
            context.CommandList.SetGraphicsRootDescriptorTable(4, frameResources.BloomPresentationDescriptor.Gpu);
            context.CommandList.SetGraphicsRootDescriptorTable(5, frameResources.SceneMetadataDescriptor.Gpu);
            context.CommandList.SetGraphicsRootDescriptorTable(6, frameResources.SceneControlDescriptor.Gpu);
            context.CommandList.SetGraphicsRootDescriptorTable(7, frameResources.HistoryDescriptor.Gpu);
            context.CommandList.SetGraphicsRootDescriptorTable(8, frameResources.HistoryMetadataDescriptor.Gpu);
            context.CommandList.SetGraphicsRootDescriptorTable(9, frameResources.HistoryControlDescriptor.Gpu);
            context.CommandList.SetGraphicsRootDescriptorTable(10, frameResources.SceneEventColorDescriptor.Gpu);
            context.CommandList.SetGraphicsRootDescriptorTable(11, frameResources.SceneEventMetadataDescriptor.Gpu);
            context.CommandList.SetGraphicsRootDescriptorTable(12, frameResources.HistoryEventColorDescriptor.Gpu);
            context.CommandList.SetGraphicsRootDescriptorTable(13, frameResources.HistoryEventMetadataDescriptor.Gpu);

            context.CommandList.RSSetViewports(viewport);
            context.CommandList.RSSetScissorRects(scissorRect);
            context.CommandList.OMSetRenderTargets(
            [
                context.RenderTargetView,
                historyRenderTargets[historyWriteIndex].RenderTargetView.Cpu,
                historyMetadataRenderTargets[historyWriteIndex].RenderTargetView.Cpu,
                historyControlRenderTargets[historyWriteIndex].RenderTargetView.Cpu,
                historyEventColorRenderTargets[historyWriteIndex].RenderTargetView.Cpu,
                historyEventMetadataRenderTargets[historyWriteIndex].RenderTargetView.Cpu,
            ],
            null);
            context.CommandList.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            context.CommandList.DrawInstanced(3, 1, 0, 0);
        }
        finally
        {
            context.CommandList.EndEvent();
        }
    }

    private void UploadBodyLightResources(ID3D12GraphicsCommandList activeCommandList, FrameResources frameResources)
    {
        activeCommandList.BeginEvent("Body Light Upload");
        try
        {
            bodyLightBuffer.Upload(activeCommandList, frameResources.UploadRing, bodyLights);
        }
        finally
        {
            activeCommandList.EndEvent();
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
            activeCommandList.SetPipelineState(gridHeightBasePipelineState!);
            activeCommandList.SetGraphicsRootSignature(fullscreenRootSignature);
            activeCommandList.SetGraphicsRootDescriptorTable(0, frameResources.FrameConstantsDescriptor.Gpu);
            activeCommandList.RSSetViewports(viewport);
            activeCommandList.RSSetScissorRects(scissorRect);
            activeCommandList.OMSetRenderTargets(gridHeightRenderTarget.RenderTargetView.Cpu, null);
            activeCommandList.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            activeCommandList.DrawInstanced(3, 1, 0, 0);
            activeCommandList.SetPipelineState(gridHeightBrushPipelineState!);
            activeCommandList.SetGraphicsRootDescriptorTable(2, frameResources.GridBrushConstantsDescriptor.Gpu);
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
        gridHeightBrushes[index] = new GridHeightBrushCpu(center, radius, power, amplitude, waveAmplitude, waveFrequency, waveSpeed);
    }

    private void BuildBodyLightTable()
    {
        Array.Clear(bodyLights);
        bodyLights[0] = new BodyLightGpu(
            new Vector4(0.0f, 0.0f, 2.2f, SunRadius),
            new Vector4(10.0f, 8.7f, 4.2f, 2.0f));
    }

    private float EvaluateGridHeight(AquariumFrame frame, Vector2 world)
    {
        var slow = MathF.Sin((world.X * 0.08f + world.Y * 0.06f) + frame.TimeSeconds * 0.27f)
            * MathF.Sin((world.X * -0.04f + world.Y * 0.07f) - frame.TimeSeconds * 0.19f)
            * 0.035f;
        var height = slow;
        for (var index = 0; index < GridHeightBrushCount; index++)
        {
            var brush = gridHeightBrushes[index];
            var distanceValue = Vector2.Distance(world, brush.Center);
            if (distanceValue > brush.Radius)
            {
                continue;
            }

            var well = PowerPulse(distanceValue, brush.Radius, brush.Power);
            var ripple = MathF.Sin(distanceValue * brush.WaveFrequency - frame.TimeSeconds * brush.WaveSpeed);
            height += brush.Amplitude * well + ripple * well * brush.WaveAmplitude;
        }

        return height;
    }

    private static float PowerPulse(float distanceValue, float radius, float power)
    {
        var normalized = Math.Clamp(distanceValue / MathF.Max(radius, 0.001f), 0.0f, 1.0f);
        var shaped = MathF.Pow(1.0f - normalized, power);
        return shaped * shaped * (3.0f - 2.0f * shaped);
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
        if (accumulatedTimingFrames > 0)
        {
            var scale = 1.0 / accumulatedTimingFrames;
            Console.WriteLine(
                $"D3D12 CPU timing avg over {accumulatedTimingFrames} frames: " +
                $"frame {accumulatedFrameCpuMilliseconds * scale:0.###} ms; " +
                $"record {accumulatedRecordCpuMilliseconds * scale:0.###} ms; " +
                $"overlay {accumulatedOverlayCpuMilliseconds * scale:0.###} ms");
            accumulatedFrameCpuMilliseconds = 0.0;
            accumulatedRecordCpuMilliseconds = 0.0;
            accumulatedOverlayCpuMilliseconds = 0.0;
            accumulatedTimingFrames = 0;
        }
    }

    private static double ElapsedMilliseconds(long startTimestamp)
    {
        return (Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency;
    }

    private static string ResolveShaderSourceRoot(string? shaderPath)
    {
        if (!string.IsNullOrWhiteSpace(shaderPath))
        {
            var sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(shaderPath));
            if (!string.IsNullOrWhiteSpace(sourceDirectory))
            {
                return sourceDirectory;
            }
        }

        return Path.Combine(AppContext.BaseDirectory, "Render", "Shaders");
    }

    private void CaptureShaderWriteTimes()
    {
        shaderWriteTimesUtc.Clear();
        foreach (var path in shaderPaths.All)
        {
            shaderWriteTimesUtc[path] = File.GetLastWriteTimeUtc(path);
        }
    }

    private DateTime LatestShaderWriteTimeUtc()
    {
        var latest = DateTime.MinValue;
        foreach (var path in shaderPaths.All)
        {
            var writeTime = File.GetLastWriteTimeUtc(path);
            if (writeTime > latest)
            {
                latest = writeTime;
            }
        }

        return latest;
    }

    private bool ShaderSourcesChanged(out DateTime latestWriteTimeUtc)
    {
        latestWriteTimeUtc = DateTime.MinValue;
        foreach (var path in shaderPaths.All)
        {
            var writeTime = File.GetLastWriteTimeUtc(path);
            if (writeTime > latestWriteTimeUtc)
            {
                latestWriteTimeUtc = writeTime;
            }

            if (!shaderWriteTimesUtc.TryGetValue(path, out var previousWriteTime) || writeTime > previousWriteTime)
            {
                return true;
            }
        }

        return false;
    }

    private void TryHotReloadShaders()
    {
        var now = shaderReloadClock.Elapsed;
        if (now - lastShaderReloadCheck < ShaderReloadPollInterval || pipelineBuildTask is { IsCompleted: false })
        {
            return;
        }

        lastShaderReloadCheck = now;
        try
        {
            if (!ShaderSourcesChanged(out var latestWriteTimeUtc) || DateTime.UtcNow - latestWriteTimeUtc < ShaderReloadWriteSettleTime)
            {
                return;
            }

            StartPipelineBuild("hot reload");
            shaderReloadFailureReported = false;
        }
        catch (Exception error)
        {
            if (!shaderReloadFailureReported)
            {
                Console.Error.WriteLine($"D3D12 shader hot reload cannot stat sources in {shaderSourceRoot}: {error.Message}");
                shaderReloadFailureReported = true;
            }
        }
    }

    private void StartPipelineBuild(string reason)
    {
        if (pipelineBuildTask is { IsCompleted: false })
        {
            return;
        }

        var paths = shaderPaths;
        LatestShaderWriteTimeUtc();
        pipelineBuildInProgressReported = false;
        pipelineBuildTask = Task.Run(() => CreatePipelineSet(paths));
        Console.WriteLine($"D3D12 shader pipeline build started ({reason}): {shaderSourceRoot}");
    }

    private void ApplyCompletedPipelineBuild()
    {
        var task = pipelineBuildTask;
        if (task is null || !task.IsCompleted)
        {
            if (!PipelinesReady && !pipelineBuildInProgressReported)
            {
                Console.WriteLine("D3D12 shader pipeline build still running; waiting behind startup splash.");
                pipelineBuildInProgressReported = true;
            }

            return;
        }

        pipelineBuildTask = null;
        if (task.IsFaulted || task.IsCanceled)
        {
            var error = task.Exception?.GetBaseException() ?? new InvalidOperationException("D3D12 shader pipeline build was canceled.");
            if (PipelinesReady)
            {
                Console.Error.WriteLine($"D3D12 shader hot reload failed; keeping previous pipelines. {error.Message}");
            }
            else
            {
                Console.Error.WriteLine($"D3D12 initial shader pipeline build failed; retrying after source change. {error.Message}");
            }

            shaderReloadFailureReported = true;
            return;
        }

        var replacement = task.Result;
        var oldPipelines = CapturePipelineSetOrNull();
        if (oldPipelines is not null)
        {
            WaitForGpu();
        }

        ApplyPipelineSet(replacement);
        CaptureShaderWriteTimes();
        oldPipelines?.Dispose();
        shaderReloadFailureReported = false;
        pipelineBuildInProgressReported = false;
        if (initialPipelineReadyReported)
        {
            Console.WriteLine($"D3D12 shader hot reload applied: {shaderSourceRoot}");
        }
        else
        {
            Console.WriteLine($"D3D12 shader pipelines ready: {shaderSourceRoot}");
            initialPipelineReadyReported = true;
        }
    }

    private D3D12PipelineSet? CapturePipelineSetOrNull()
    {
        return PipelinesReady
            ? new D3D12PipelineSet(
                gridHeightBasePipelineState!,
                gridHeightBrushPipelineState!,
                scenePipelineState!,
                bloomPrefilterPipelineState!,
                bloomDownsamplePipelineState!,
                bloomBlurHorizontalPipelineState!,
                bloomBlurVerticalPipelineState!,
                resolvePipelineState!)
            : null;
    }

    private void ApplyPipelineSet(D3D12PipelineSet pipelines)
    {
        gridHeightBasePipelineState = pipelines.GridHeightBase;
        gridHeightBrushPipelineState = pipelines.GridHeightBrush;
        scenePipelineState = pipelines.Scene;
        bloomPrefilterPipelineState = pipelines.BloomPrefilter;
        bloomDownsamplePipelineState = pipelines.BloomDownsample;
        bloomBlurHorizontalPipelineState = pipelines.BloomBlurHorizontal;
        bloomBlurVerticalPipelineState = pipelines.BloomBlurVertical;
        resolvePipelineState = pipelines.Resolve;
    }

    private void DisposePipelineStates()
    {
        CapturePipelineSetOrNull()?.Dispose();
        gridHeightBasePipelineState = null;
        gridHeightBrushPipelineState = null;
        scenePipelineState = null;
        bloomPrefilterPipelineState = null;
        bloomDownsamplePipelineState = null;
        bloomBlurHorizontalPipelineState = null;
        bloomBlurVerticalPipelineState = null;
        resolvePipelineState = null;
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
            64,
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
        var gridBrushRange = new DescriptorRange(
            DescriptorRangeType.ConstantBufferView,
            1,
            1,
            0,
            D3D12.DescriptorRangeOffsetAppend);
        var bodyLightRange = new DescriptorRange(
            DescriptorRangeType.ShaderResourceView,
            1,
            12,
            0,
            D3D12.DescriptorRangeOffsetAppend);
        var bloomRange = new DescriptorRange(
            DescriptorRangeType.ShaderResourceView,
            BloomLevelCount,
            9,
            0,
            D3D12.DescriptorRangeOffsetAppend);
        var currentSceneMetadataRange = new DescriptorRange(
            DescriptorRangeType.ShaderResourceView,
            1,
            5,
            0,
            D3D12.DescriptorRangeOffsetAppend);
        var currentSceneControlRange = new DescriptorRange(
            DescriptorRangeType.ShaderResourceView,
            1,
            7,
            0,
            D3D12.DescriptorRangeOffsetAppend);
        var historyRange = new DescriptorRange(
            DescriptorRangeType.ShaderResourceView,
            1,
            4,
            0,
            D3D12.DescriptorRangeOffsetAppend);
        var historyMetadataRange = new DescriptorRange(
            DescriptorRangeType.ShaderResourceView,
            1,
            6,
            0,
            D3D12.DescriptorRangeOffsetAppend);
        var historyControlRange = new DescriptorRange(
            DescriptorRangeType.ShaderResourceView,
            1,
            8,
            0,
            D3D12.DescriptorRangeOffsetAppend);
        var currentEventColorRange = new DescriptorRange(
            DescriptorRangeType.ShaderResourceView,
            1,
            18,
            0,
            D3D12.DescriptorRangeOffsetAppend);
        var currentEventMetadataRange = new DescriptorRange(
            DescriptorRangeType.ShaderResourceView,
            1,
            19,
            0,
            D3D12.DescriptorRangeOffsetAppend);
        var historyEventColorRange = new DescriptorRange(
            DescriptorRangeType.ShaderResourceView,
            1,
            20,
            0,
            D3D12.DescriptorRangeOffsetAppend);
        var historyEventMetadataRange = new DescriptorRange(
            DescriptorRangeType.ShaderResourceView,
            1,
            21,
            0,
            D3D12.DescriptorRangeOffsetAppend);
        var studioPmremRange = new DescriptorRange(
            DescriptorRangeType.ShaderResourceView,
            1,
            22,
            0,
            D3D12.DescriptorRangeOffsetAppend);
        var rootParameters = new[]
        {
            new RootParameter(new RootDescriptorTable([constantBufferRange]), ShaderVisibility.All),
            new RootParameter(new RootDescriptorTable([sourceTextureRange]), ShaderVisibility.Pixel),
            new RootParameter(new RootDescriptorTable([gridBrushRange]), ShaderVisibility.All),
            new RootParameter(new RootDescriptorTable([bodyLightRange]), ShaderVisibility.Pixel),
            new RootParameter(new RootDescriptorTable([bloomRange]), ShaderVisibility.Pixel),
            new RootParameter(new RootDescriptorTable([currentSceneMetadataRange]), ShaderVisibility.Pixel),
            new RootParameter(new RootDescriptorTable([currentSceneControlRange]), ShaderVisibility.Pixel),
            new RootParameter(new RootDescriptorTable([historyRange]), ShaderVisibility.Pixel),
            new RootParameter(new RootDescriptorTable([historyMetadataRange]), ShaderVisibility.Pixel),
            new RootParameter(new RootDescriptorTable([historyControlRange]), ShaderVisibility.Pixel),
            new RootParameter(new RootDescriptorTable([currentEventColorRange]), ShaderVisibility.Pixel),
            new RootParameter(new RootDescriptorTable([currentEventMetadataRange]), ShaderVisibility.Pixel),
            new RootParameter(new RootDescriptorTable([historyEventColorRange]), ShaderVisibility.Pixel),
            new RootParameter(new RootDescriptorTable([historyEventMetadataRange]), ShaderVisibility.Pixel),
            new RootParameter(new RootDescriptorTable([studioPmremRange]), ShaderVisibility.Pixel),
        };
        var staticSamplers = new[]
        {
            new StaticSamplerDescription(
                0,
                Filter.MinMagMipLinear,
                TextureAddressMode.Clamp,
                TextureAddressMode.Clamp,
                TextureAddressMode.Clamp,
                0.0f,
                1,
                ComparisonFunction.Never,
                StaticBorderColor.TransparentBlack,
                0.0f,
                float.MaxValue,
                ShaderVisibility.Pixel,
                0),
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
        var uploadRing = new D3D12UploadRing(device, 64 * 1024 * 1024, $"Aquarium D3D12 Frame {index} Upload Ring");
        var transientDescriptors = new D3D12DescriptorArena(
            device,
            DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView,
            64,
            DescriptorHeapFlags.ShaderVisible,
            $"Aquarium D3D12 Frame {index} Transient Shader Descriptor Arena");

        return new FrameResources(commandAllocator, uploadRing, transientDescriptors);
    }

    private ID3D12PipelineState CreateScenePipelineState(string path)
    {
        return CreateFullscreenPipelineState(path, "FullscreenTriangleVS", "D3D12ScenePS", [SceneHdrFormat, SceneHdrFormat, SceneHdrFormat, SceneHdrFormat, SceneHdrFormat]);
    }

    private ID3D12PipelineState CreateBloomPrefilterPipelineState(string path)
    {
        return CreateFullscreenPipelineState(path, "FullscreenTriangleVS", "D3D12BloomPrefilterPS", SceneHdrFormat);
    }

    private ID3D12PipelineState CreateBloomDownsamplePipelineState(string path)
    {
        return CreateFullscreenPipelineState(path, "FullscreenTriangleVS", "D3D12BloomDownsamplePS", SceneHdrFormat);
    }

    private ID3D12PipelineState CreateBloomBlurHorizontalPipelineState(string path)
    {
        return CreateFullscreenPipelineState(path, "FullscreenTriangleVS", "D3D12BloomBlurHorizontalPS", SceneHdrFormat);
    }

    private ID3D12PipelineState CreateBloomBlurVerticalPipelineState(string path)
    {
        return CreateFullscreenPipelineState(path, "FullscreenTriangleVS", "D3D12BloomBlurVerticalPS", SceneHdrFormat);
    }

    private ID3D12PipelineState CreateResolvePipelineState(string path)
    {
        return CreateFullscreenPipelineState(
            path,
            "FullscreenTriangleVS",
            "D3D12ResolvePS",
            [Format.B8G8R8A8_UNorm, SceneHdrFormat, SceneHdrFormat, SceneHdrFormat, SceneHdrFormat, SceneHdrFormat]);
    }

    private ID3D12PipelineState CreateGridHeightBasePipelineState(string path)
    {
        return CreateFullscreenPipelineState(path, "FullscreenTriangleVS", "D3D12GridHeightBasePS", GridHeightFormat);
    }

    private ID3D12PipelineState CreateGridHeightBrushPipelineState(string path)
    {
        return CreateFullscreenPipelineState(
            path,
            "D3D12GridHeightBrushVS",
            "D3D12GridHeightBrushPS",
            GridHeightFormat,
            new BlendDescription(Blend.One, Blend.One, Blend.One, Blend.One));
    }

    private ID3D12PipelineState CreateFullscreenPipelineState(
        string path,
        string vertexEntryPoint,
        string pixelEntryPoint,
        Format renderTargetFormat,
        BlendDescription? blendDescription = null)
    {
        return CreateFullscreenPipelineState(
            path,
            vertexEntryPoint,
            pixelEntryPoint,
            [renderTargetFormat],
            blendDescription);
    }

    private ID3D12PipelineState CreateFullscreenPipelineState(
        string path,
        string vertexEntryPoint,
        string pixelEntryPoint,
        IReadOnlyList<Format> renderTargetFormats,
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
            RenderTargetFormats = renderTargetFormats.ToArray(),
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

    private static Vector3 CursorCenter(AquariumFrame frame)
    {
        return new Vector3(frame.CursorWorld.X, frame.CursorWorld.Y, CursorBodyRadius);
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
        Vector4 CursorWorlds);

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

    private readonly record struct GridHeightBrushCpu(
        Vector2 Center,
        float Radius,
        float Power,
        float Amplitude,
        float WaveAmplitude,
        float WaveFrequency,
        float WaveSpeed);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct BodyLightGpu(
        Vector4 CenterRadius,
        Vector4 RadianceFieldId);

    private sealed record D3D12ShaderPaths(
        string Grid,
        string Scene,
        string Post)
    {
        public IReadOnlyList<string> All { get; } = [Grid, Scene, Post];

        public static D3D12ShaderPaths FromRoot(string root)
        {
            return new D3D12ShaderPaths(
                Path.Combine(root, Path.GetFileName(GridShaderRelativePath)),
                Path.Combine(root, Path.GetFileName(SceneShaderRelativePath)),
                Path.Combine(root, Path.GetFileName(PostShaderRelativePath)));
        }
    }

    private sealed record D3D12PipelineSet(
        ID3D12PipelineState GridHeightBase,
        ID3D12PipelineState GridHeightBrush,
        ID3D12PipelineState Scene,
        ID3D12PipelineState BloomPrefilter,
        ID3D12PipelineState BloomDownsample,
        ID3D12PipelineState BloomBlurHorizontal,
        ID3D12PipelineState BloomBlurVertical,
        ID3D12PipelineState Resolve) : IDisposable
    {
        public void Dispose()
        {
            Resolve.Dispose();
            BloomBlurVertical.Dispose();
            BloomBlurHorizontal.Dispose();
            BloomDownsample.Dispose();
            BloomPrefilter.Dispose();
            Scene.Dispose();
            GridHeightBrush.Dispose();
            GridHeightBase.Dispose();
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

        public D3D12DescriptorSlot BodyLightDescriptor { get; set; }

        public D3D12DescriptorSlot StudioPmremDescriptor { get; set; }

        public D3D12DescriptorSlot GridHeightDescriptor { get; set; }

        public D3D12DescriptorSlot SceneDescriptor { get; set; }

        public D3D12DescriptorSlot SceneMetadataDescriptor { get; set; }

        public D3D12DescriptorSlot SceneControlDescriptor { get; set; }

        public D3D12DescriptorSlot SceneEventColorDescriptor { get; set; }

        public D3D12DescriptorSlot SceneEventMetadataDescriptor { get; set; }

        public D3D12DescriptorSlot HistoryDescriptor { get; set; }

        public D3D12DescriptorSlot HistoryMetadataDescriptor { get; set; }

        public D3D12DescriptorSlot HistoryControlDescriptor { get; set; }

        public D3D12DescriptorSlot HistoryEventColorDescriptor { get; set; }

        public D3D12DescriptorSlot HistoryEventMetadataDescriptor { get; set; }

        public D3D12DescriptorSlot[] BloomDescriptors { get; } = new D3D12DescriptorSlot[BloomLevelCount];

        public D3D12DescriptorSlot[] BloomScratchDescriptors { get; } = new D3D12DescriptorSlot[BloomLevelCount];

        public D3D12DescriptorSlot BloomPresentationDescriptor { get; set; }

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
