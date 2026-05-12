using Aquarium.Engine.Audio;
using Aquarium.Engine.Input;
using Aquarium.Engine.Render.Features;
using Aquarium.Engine.Render.Graph;
using SharpGen.Runtime;
using Aquarium.Engine.Render.Ui;
using Aquarium.Engine.Ui;
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
    private const int HeightFieldTextureSize = 128;
    private const Format HeightFieldFormat = Format.R16_Float;
    private const Format SceneDepthFormat = Format.D32_Float;
    private const int MaxSdfLightCount = 64;
    private const int MaxSdfObjectCount = 64;
    private const float SurfaceTransparentMinZ = -1.85f;
    private const float SurfaceTransparentMaxZ = 0.45f;
    private const int BloomLevelCount = 3;
    private const Format SceneHdrFormat = Format.R16G16B16A16_Float;
    private const string StudioPmremRelativePath = "Assets/Textures/studio2_pmrem.dds";
    private const string StudioIrradianceRelativePath = "Assets/Textures/studio2_irradiance.dds";
    private const int RootFrameConstants = 0;
    private const int RootSourceTexture = 1;
    private const int RootHeightFieldBrushes = 2;
    private const int RootSdfLights = 3;
    private const int RootBloom = 4;
    private const int RootCurrentSceneMetadata = 5;
    private const int RootCurrentSceneControl = 6;
    private const int RootHistory = 7;
    private const int RootHistoryMetadata = 8;
    private const int RootHistoryControl = 9;
    private const int RootStudioPmrem = 10;
    private const int RootStudioIrradiance = 11;
    private const int RootSdfObjects = 12;
    private static readonly DebugUi.DebugUiOption[] RenderDebugOptions =
    [
        new(0, "Final"),
        new(1, "Raw Scene"),
        new(2, "History"),
        new(3, "History Age"),
        new(4, "History Weight"),
        new(5, "Coverage/Steps"),
        new(6, "Lane Identity"),
        new(7, "Bloom"),
        new(8, "Exposed Luminance"),
        new(9, "SdfObject Identity"),
        new(10, "SdfObject Steps"),
    ];
    private static readonly DebugUi.DebugUiOption[] SynthPresetOptions = AquariumSynth.Dsl.BuiltInScripts.PrimitiveGolfScripts()
        .Select((preset, index) => new DebugUi.DebugUiOption(index, $"{preset.Family}/{preset.Name}"))
        .ToArray();
    private static readonly (string Family, string Name, string Script)[] SynthPresets = AquariumSynth.Dsl.BuiltInScripts.PrimitiveGolfScripts().ToArray();

    private readonly IDXGIFactory4 factory;
    private readonly ID3D12Device device;
    private readonly ID3D12CommandQueue commandQueue;
    private readonly ID3D11Device overlayDevice;
    private readonly ID3D11DeviceContext overlayContext;
    private readonly ID3D11On12Device overlayOn12Device;
    private readonly IDXGISwapChain3 swapChain;
    private readonly D3D12ResourceRegistry resourceRegistry = new();
    private D3D12DescriptorArena renderTargetViewArena;
    private D3D12DescriptorArena depthStencilViewArena;
    private D3D12DescriptorArena staticShaderDescriptorArena;
    private readonly FrameResources[] frames = new FrameResources[BackBufferCount];
    private readonly ID3D12GraphicsCommandList commandList;
    private readonly ID3D12RootSignature fullscreenRootSignature;
    private ID3D12PipelineState? heightFieldBasePipelineState;
    private ID3D12PipelineState? heightFieldBrushPipelineState;
    private ID3D12PipelineState? scenePipelineState;
    private ID3D12PipelineState?[] sdfProxyPipelineStates = [];
    private ID3D12PipelineState? bloomPrefilterPipelineState;
    private ID3D12PipelineState? bloomDownsamplePipelineState;
    private ID3D12PipelineState? bloomBlurHorizontalPipelineState;
    private ID3D12PipelineState? bloomBlurVerticalPipelineState;
    private ID3D12PipelineState? resolvePipelineState;
    private readonly ID3D12Fence fence;
    private readonly AutoResetEvent fenceEvent = new(false);
    private DebugUi debugUi;
    private AquariumUiDocument? currentClientUi;
    private int activeDebugTab;
    private string[] debugTabTitles = ["Aquarium", "Terminal", "Synth"];
    private string terminalInput = "help";
    private readonly List<string> terminalLines = ["Aquarium terminal ready. Type help."];
    private IReadOnlyList<AquariumConsoleCommand> clientCommands = [];
    private string synthPlaygroundScript = AquariumSynth.Dsl.BuiltInScripts.ClassicSfxrPrimitiveGolfScripts[0].Script;
    private int synthPlaygroundPreset;
    private int synthPlaygroundCompileRevision;
    private int synthPlaygroundPlayRevision;
    private float synthPlaygroundGain = 0.45f;
    private AquariumSynthPatchStatus synthPlaygroundStatus = new("aquarium-playground", AquariumSynthPatchCompileState.Idle, "idle", 0, 0.0);
    private ID3D11Resource[] overlayWrappedBackBuffers = [];
    private DirectWriteOverlay[] overlays = [];
    private D3D12RenderTarget heightFieldRenderTarget;
    private D3D12RenderTarget sceneRenderTarget;
    private D3D12RenderTarget sceneMetadataRenderTarget;
    private D3D12RenderTarget sceneControlRenderTarget;
    private readonly Dictionary<string, D3D12RenderTarget> graphRenderTargets = new(StringComparer.Ordinal);
    private D3D12TrackedResource sceneDepthTarget;
    private D3D12DescriptorSlot sceneDepthStencilView;
    private readonly D3D12RenderTarget[] historyRenderTargets = new D3D12RenderTarget[2];
    private readonly D3D12RenderTarget[] historyMetadataRenderTargets = new D3D12RenderTarget[2];
    private readonly D3D12RenderTarget[] historyControlRenderTargets = new D3D12RenderTarget[2];
    private readonly D3D12RenderTarget[] bloomRenderTargets = new D3D12RenderTarget[BloomLevelCount];
    private readonly D3D12RenderTarget[] bloomScratchTargets = new D3D12RenderTarget[BloomLevelCount];
    private readonly D3D12StructuredBuffer sdfLightBuffer;
    private readonly D3D12StructuredBuffer sdfObjectBuffer;
    private readonly D3D12CubeTexture studioPmremTexture;
    private readonly D3D12CubeTexture studioIrradianceTexture;
    private readonly AquariumSdfLight[] sdfLights = new AquariumSdfLight[MaxSdfLightCount];
    private readonly AquariumSdfObject[] sdfObjects = new AquariumSdfObject[MaxSdfObjectCount];
    private Viewport viewport;
    private RawRect scissorRect;
    private int width;
    private int height;
    private ulong fenceValue;
    private int temporalFrameIndex;
    private int frameIndex;
    private Vector3 previousCameraPosition;
    private Vector2 previousViewCenter;
    private Vector2 previousCursorWorld;
    private float previousViewRadius = 0.001f;
    private float previousTimeSeconds;
    private D3D12HeightFieldBrushConstants heightFieldBrushConstants;
    private GraphicsSettings settings = GraphicsSettings.Default;
    private double accumulatedFrameCpuMilliseconds;
    private double accumulatedRecordCpuMilliseconds;
    private double accumulatedOverlayCpuMilliseconds;
    private int accumulatedTimingFrames;
    private readonly string shaderSourceRoot;
    private readonly D3D12ShaderPaths shaderPaths;
    private readonly CompiledRenderGraph renderGraph;
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
        AquariumRenderPlan? renderPlan = null,
        GraphicsSettings? graphicsSettings = null,
        Action<string>? startupProgress = null)
    {
        ApplyGraphicsSettings(graphicsSettings ?? GraphicsSettings.Default);
        this.width = width;
        this.height = height;
        var activeRenderPlan = renderPlan ?? new AquariumRenderPlan();
        renderGraph = D3D12RenderGraphCompiler.Compile(activeRenderPlan);
        shaderSourceRoot = ResolveShaderSourceRoot(shaderPath, activeRenderPlan.Shaders);
        shaderPaths = D3D12ShaderPaths.FromManifest(shaderSourceRoot, activeRenderPlan.Shaders);
        sdfProxyPipelineStates = new ID3D12PipelineState?[shaderPaths.SdfShaders.Count];
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
        depthStencilViewArena = CreateDepthStencilViewArena();
        staticShaderDescriptorArena = CreateStaticShaderDescriptorArena();
        for (var index = 0; index < frames.Length; index++)
        {
            frames[index] = CreateFrameResources(index);
        }

        CreateRenderTargetViews();
        CreateBackBufferOverlays();
        debugUi = CreateDebugUi(AquariumUiDocument.Empty);
        heightFieldRenderTarget = CreateHeightFieldRenderTarget();
        sceneRenderTarget = CreateSceneRenderTarget();
        sceneMetadataRenderTarget = CreateSceneAuxiliaryRenderTarget("scene-metadata-target", "Aquarium D3D12 Scene Metadata Target");
        sceneControlRenderTarget = CreateSceneAuxiliaryRenderTarget("scene-control-target", "Aquarium D3D12 Scene Control Target");
        sceneDepthStencilView = depthStencilViewArena.Allocate();
        sceneDepthTarget = CreateSceneDepthTarget(sceneDepthStencilView);
        CreateHistoryRenderTargets();
        CreateBloomRenderTargets();
        CreateGraphRenderTargets();
        sdfLightBuffer = new D3D12StructuredBuffer(device, MaxSdfLightCount, Marshal.SizeOf<AquariumSdfLight>(), "Aquarium D3D12 Sdf Light Buffer");
        sdfObjectBuffer = new D3D12StructuredBuffer(device, MaxSdfObjectCount, Marshal.SizeOf<AquariumSdfObject>(), "Aquarium D3D12 Sdf Object Buffer");
        resourceRegistry.Add("sdf-light-buffer", sdfLightBuffer);
        resourceRegistry.Add("sdf-object-buffer", sdfObjectBuffer);
        commandList = device.CreateCommandList<ID3D12GraphicsCommandList>(0, CommandListType.Direct, frames[frameIndex].CommandAllocator, null);
        commandList.Name = "Aquarium D3D12 Graphics Command List";
        commandList.Close();
        fence = device.CreateFence(0);
        fence.Name = "Aquarium D3D12 Frame Fence";
        commandQueue.ExecuteCommandList(commandList);
        WaitForGpu();
        ReportStartupProgress(startupProgress, "Loading studio IBL cubemaps");
        studioPmremTexture = LoadStudioPmremTexture();
        resourceRegistry.Add("studio-pmrem-cubemap", studioPmremTexture);
        studioIrradianceTexture = LoadStudioIrradianceTexture();
        resourceRegistry.Add("studio-irradiance-cubemap", studioIrradianceTexture);
        ReportStartupProgress(startupProgress, "Creating D3D12 render pipelines");
        fullscreenRootSignature = CreateFullscreenRootSignature();
        fullscreenRootSignature.Name = "Aquarium D3D12 Fullscreen Root Signature";
        CaptureShaderWriteTimes();
        StartPipelineBuild("initial");
        viewport = new Viewport(0.0f, 0.0f, width, height);
        scissorRect = new RawRect(0, 0, width, height);
        Console.WriteLine($"D3D12 resource registry: {resourceRegistry.Describe()}");
        Console.WriteLine($"Aquarium render graph declared: {renderGraph.Describe()}");
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

    public AquariumSynthDocument DebugSynth => new AquariumSynthDocument
    {
        MasterGain = 1.0f,
        Enabled = true
    }.Patch(
        "aquarium-playground",
        synthPlaygroundScript,
        AquariumSynthTrigger.Manual(synthPlaygroundPlayRevision),
        synthPlaygroundGain,
        synthPlaygroundCompileRevision,
        "aquarium_playground",
        status => synthPlaygroundStatus = status);

    public void UpdateUi(InputState input, AquariumUiDocument clientUi)
    {
        if (!ReferenceEquals(currentClientUi, clientUi))
        {
            currentClientUi = clientUi;
            clientCommands = clientUi.Commands;
            debugTabTitles = ["Aquarium", "Terminal", "Synth", .. clientUi.Panels.Select(panel => panel.Title)];
            activeDebugTab = Math.Clamp(activeDebugTab, 0, debugTabTitles.Length - 1);
            debugUi = CreateDebugUi(clientUi);
        }

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

    private DebugUi CreateDebugUi(AquariumUiDocument clientUi)
    {
        var ui = new DebugUi("Debug", 18.0f, 82.0f, 360.0f, debugTabTitles, () => activeDebugTab, SelectDebugTab)
            .Panel(panel =>
            {
                panel
                .Section("View", () => activeDebugTab == 0)
                .Options("Render Debug", () => RenderDebugMode, value => RenderDebugMode = Math.Clamp(value, GraphicsSettings.MinRenderDebugMode, GraphicsSettings.MaxRenderDebugMode), RenderDebugOptions, "Selects the active renderer debug view.", () => activeDebugTab == 0)
                .Button("Reset View", () => RenderDebugMode = 0, "Returns to the final presented frame.", () => activeDebugTab == 0)
                .Section("HDR", () => activeDebugTab == 0)
                .Slider("Exposure", () => settings.SceneExposure, value => settings = (settings with { SceneExposure = Math.Clamp(value, GraphicsSettings.MinSceneExposure, GraphicsSettings.MaxSceneExposure) }).Normalized(), GraphicsSettings.MinSceneExposure, GraphicsSettings.MaxSceneExposure, "0.###", "Manual scene exposure before display transform.", () => activeDebugTab == 0)
                .Slider("Bloom Intensity", () => settings.BloomIntensity, value => settings = (settings with { BloomIntensity = Math.Clamp(value, GraphicsSettings.MinBloomIntensity, GraphicsSettings.MaxBloomIntensity) }).Normalized(), GraphicsSettings.MinBloomIntensity, GraphicsSettings.MaxBloomIntensity, "0.###", "Strength of pre-tonemap bloom energy.", () => activeDebugTab == 0)
                .Slider("Bloom Veil", () => settings.BloomVeilIntensity, value => settings = (settings with { BloomVeilIntensity = Math.Clamp(value, GraphicsSettings.MinBloomVeilIntensity, GraphicsSettings.MaxBloomVeilIntensity) }).Normalized(), GraphicsSettings.MinBloomVeilIntensity, GraphicsSettings.MaxBloomVeilIntensity, "0.###", "Low-frequency veil from bright HDR energy.", () => activeDebugTab == 0)
                .Section("Terminal", () => activeDebugTab == 1)
                .TextBox("Session", TerminalDisplay, UpdateTerminalInputFromDisplay, lines: 8, acceptsReturn: false, submit: ExecuteTerminalInput, tooltip: "Terminal log with live command prompt.", isVisible: () => activeDebugTab == 1)
                .Section("Synth Playground", () => activeDebugTab == 2)
                .Options("Preset", () => synthPlaygroundPreset, SelectSynthPreset, SynthPresetOptions, "Loads a built-in synth patch.", () => activeDebugTab == 2)
                .Readout("Compile", () => $"{synthPlaygroundStatus.State}: {synthPlaygroundStatus.Message}", "Faust compile status.", () => activeDebugTab == 2)
                .TextBox("Patch", () => synthPlaygroundScript, value => synthPlaygroundScript = value, lines: 7, acceptsReturn: true, tooltip: "Patch DSL source.", isVisible: () => activeDebugTab == 2)
                .Slider("Gain", () => synthPlaygroundGain, value => synthPlaygroundGain = Math.Clamp(value, 0.0f, 1.0f), 0.0f, 1.0f, "0.###", "Playground patch gain.", () => activeDebugTab == 2)
                .Button("Compile", () => synthPlaygroundCompileRevision++, "Forces async Faust compile.", () => activeDebugTab == 2)
                .Button("Play", () => { synthPlaygroundCompileRevision++; synthPlaygroundPlayRevision++; }, "Compiles and schedules the playground patch.", () => activeDebugTab == 2);
            });
        for (var panelIndex = 0; panelIndex < clientUi.Panels.Count; panelIndex++)
        {
            var tabIndex = panelIndex + 3;
            foreach (var control in clientUi.Panels[panelIndex].Controls)
            {
                ui.AddContractControl(control with { IsVisible = ComposeVisibility(control.IsVisible, () => activeDebugTab == tabIndex) });
            }
        }

        return ui;
    }

    private void SelectDebugTab(int index)
    {
        activeDebugTab = Math.Clamp(index, 0, Math.Max(0, debugTabTitles.Length - 1));
    }

    private static Func<bool> ComposeVisibility(Func<bool>? source, Func<bool> tabVisible)
    {
        return () => tabVisible() && (source?.Invoke() ?? true);
    }

    private string TerminalDisplay()
    {
        return string.Join('\n', terminalLines.TakeLast(10).Append($"> {terminalInput}"));
    }

    private void UpdateTerminalInputFromDisplay(string value)
    {
        var promptIndex = value.LastIndexOf("\n> ", StringComparison.Ordinal);
        if (promptIndex >= 0)
        {
            terminalInput = value[(promptIndex + 3)..].Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", string.Empty, StringComparison.Ordinal);
            return;
        }

        if (value.StartsWith("> ", StringComparison.Ordinal))
        {
            terminalInput = value[2..].Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", string.Empty, StringComparison.Ordinal);
            return;
        }

        terminalInput = value.Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", string.Empty, StringComparison.Ordinal);
    }

    private void ExecuteTerminalInput()
    {
        var input = terminalInput.Trim();
        if (input.Length == 0)
        {
            return;
        }

        terminalLines.Add($"> {input}");
        terminalInput = "";
        var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var command = parts[0];
        var args = parts.Skip(1).ToArray();
        if (string.Equals(command, "help", StringComparison.OrdinalIgnoreCase))
        {
            var names = new[] { "help", "clear", "render" }
                .Concat(clientCommands.Select(item => item.Name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase);
            terminalLines.Add(string.Join(", ", names));
            return;
        }

        if (string.Equals(command, "clear", StringComparison.OrdinalIgnoreCase))
        {
            terminalLines.Clear();
            return;
        }

        if (string.Equals(command, "render", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length > 0 && int.TryParse(args[0], out var mode))
            {
                RenderDebugMode = Math.Clamp(mode, GraphicsSettings.MinRenderDebugMode, GraphicsSettings.MaxRenderDebugMode);
            }

            terminalLines.Add($"render debug {RenderDebugMode}");
            return;
        }

        var registered = clientCommands.FirstOrDefault(item => string.Equals(item.Name, command, StringComparison.OrdinalIgnoreCase));
        if (registered is not null)
        {
            terminalLines.Add(registered.Execute(args));
            return;
        }

        terminalLines.Add($"unknown command: {command}");
    }

    private void SelectSynthPreset(int index)
    {
        synthPlaygroundPreset = Math.Clamp(index, 0, Math.Max(0, SynthPresets.Length - 1));
        synthPlaygroundScript = SynthPresets[synthPlaygroundPreset].Script;
        synthPlaygroundCompileRevision++;
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

        var viewOrigin = new Vector3(frame.View.Center.X, frame.View.Center.Y, 0.0f);
        var farDistance = Vector3.Distance(frame.CameraPosition, viewOrigin) + MathF.Max(frame.View.Radius, 0.001f);
        if (temporalFrameIndex == 0)
        {
            previousCameraPosition = frame.CameraPosition;
            previousViewCenter = frame.View.Center;
            previousCursorWorld = frame.CursorWorld;
            previousViewRadius = frame.View.Radius;
            previousTimeSeconds = frame.TimeSeconds;
        }

        CopySceneState(frame.Scene);
        var frameConstants = frameResources.UploadRing.WriteConstant(new FrameConstants(
            new Vector2(width, height),
            frame.TimeSeconds,
            frame.View.Radius,
            frame.CameraPosition,
            farDistance,
            frame.View.Center,
            temporalFrameIndex,
            previousTimeSeconds,
            previousCameraPosition,
            previousViewRadius,
            previousViewCenter,
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
        var heightFieldBrushConstantBuffer = frameResources.UploadRing.WriteConstant(heightFieldBrushConstants);
        frameResources.HeightFieldBrushConstantsDescriptor = frameResources.TransientShaderDescriptors.Allocate();
        device.CreateConstantBufferView(
            new ConstantBufferViewDescription(heightFieldBrushConstantBuffer.GpuVirtualAddress, heightFieldBrushConstantBuffer.SizeInBytes),
            frameResources.HeightFieldBrushConstantsDescriptor.Cpu);

        frameResources.SdfLightDescriptor = frameResources.TransientShaderDescriptors.Allocate();
        sdfLightBuffer.CreateShaderResourceView(device, frameResources.SdfLightDescriptor);
        frameResources.SdfObjectDescriptor = frameResources.TransientShaderDescriptors.Allocate();
        sdfObjectBuffer.CreateShaderResourceView(device, frameResources.SdfObjectDescriptor);
        frameResources.StudioPmremDescriptor = frameResources.TransientShaderDescriptors.Allocate();
        studioPmremTexture.CreateShaderResourceView(device, frameResources.StudioPmremDescriptor);
        frameResources.StudioIrradianceDescriptor = frameResources.TransientShaderDescriptors.Allocate();
        studioIrradianceTexture.CreateShaderResourceView(device, frameResources.StudioIrradianceDescriptor);
        frameResources.HeightFieldDescriptor = frameResources.TransientShaderDescriptors.Allocate();
        heightFieldRenderTarget.CreateShaderResourceView(device, frameResources.HeightFieldDescriptor);
        frameResources.SceneDescriptor = frameResources.TransientShaderDescriptors.Allocate();
        sceneRenderTarget.CreateShaderResourceView(device, frameResources.SceneDescriptor);
        frameResources.SceneMetadataDescriptor = frameResources.TransientShaderDescriptors.Allocate();
        sceneMetadataRenderTarget.CreateShaderResourceView(device, frameResources.SceneMetadataDescriptor);
        frameResources.SceneControlDescriptor = frameResources.TransientShaderDescriptors.Allocate();
        sceneControlRenderTarget.CreateShaderResourceView(device, frameResources.SceneControlDescriptor);
        var historyReadIndex = temporalFrameIndex & 1;
        frameResources.HistoryDescriptor = frameResources.TransientShaderDescriptors.Allocate();
        historyRenderTargets[historyReadIndex].CreateShaderResourceView(device, frameResources.HistoryDescriptor);
        frameResources.HistoryMetadataDescriptor = frameResources.TransientShaderDescriptors.Allocate();
        historyMetadataRenderTargets[historyReadIndex].CreateShaderResourceView(device, frameResources.HistoryMetadataDescriptor);
        frameResources.HistoryControlDescriptor = frameResources.TransientShaderDescriptors.Allocate();
        historyControlRenderTargets[historyReadIndex].CreateShaderResourceView(device, frameResources.HistoryControlDescriptor);
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
        UploadSdfLightResources(commandList, frameResources);
        RenderHeightField(commandList, frameResources);
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
        previousViewCenter = frame.View.Center;
        previousCursorWorld = frame.CursorWorld;
        previousViewRadius = frame.View.Radius;
        previousTimeSeconds = frame.TimeSeconds;
        hasPresentedReadyFrame = true;
        temporalFrameIndex++;
        frameIndex = (int)swapChain.CurrentBackBufferIndex;
    }

    private bool PipelinesReady =>
        heightFieldBasePipelineState is not null
        && heightFieldBrushPipelineState is not null
        && scenePipelineState is not null
        && sdfProxyPipelineStates.All(pipeline => pipeline is not null)
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
        studioIrradianceTexture.Dispose();
        studioPmremTexture.Dispose();
        sdfObjectBuffer.Dispose();
        sdfLightBuffer.Dispose();
        DisposeBloomRenderTargets();
        DisposeHistoryRenderTargets();
        DisposeGraphRenderTargets();
        sceneRenderTarget.Dispose();
        sceneMetadataRenderTarget.Dispose();
        sceneControlRenderTarget.Dispose();
        sceneDepthTarget.Dispose();
        heightFieldRenderTarget.Dispose();
        for (var index = 0; index < frames.Length; index++)
        {
            frames[index].Dispose();
        }
        staticShaderDescriptorArena.Dispose();
        depthStencilViewArena.Dispose();
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
        return LoadStudioCubeTexture(StudioPmremRelativePath, "Studio PMREM", "Aquarium D3D12 Studio PMREM Cubemap");
    }

    private D3D12CubeTexture LoadStudioIrradianceTexture()
    {
        return LoadStudioCubeTexture(StudioIrradianceRelativePath, "Studio irradiance", "Aquarium D3D12 Studio Irradiance Cubemap");
    }

    private D3D12CubeTexture LoadStudioCubeTexture(string relativePath, string label, string resourceName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, relativePath);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"{label} cubemap asset was not copied to the runtime asset directory.", path);
        }

        var frameResources = frames[frameIndex];
        frameResources.CommandAllocator.Reset();
        commandList.Reset(frameResources.CommandAllocator, null);
        var texture = D3D12CubeTexture.LoadRgba16FloatDds(device, commandList, path, resourceName, out var uploadResource);
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
        overlays[frameIndex].Render(frame, RenderDebugMode, debugUi, []);
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
        var heightFieldBase = CreateHeightFieldBasePipelineState(paths.HeightField);
        heightFieldBase.Name = "Aquarium D3D12 Height Field Base Pipeline";
        var heightFieldBrush = CreateHeightFieldBrushPipelineState(paths.HeightField);
        heightFieldBrush.Name = "Aquarium D3D12 Height Field Brush Pipeline";
        var scene = CreateScenePipelineState(paths.Scene);
        scene.Name = "Aquarium D3D12 Scene Pipeline";
        var sdfProxies = new ID3D12PipelineState[paths.SdfShaders.Count];
        for (var index = 0; index < paths.SdfShaders.Count; index++)
        {
            sdfProxies[index] = CreateSdfObjectProxyPipelineState(paths.SdfShaders[index]);
            sdfProxies[index].Name = $"Aquarium D3D12 Sdf Proxy Pipeline {index}";
        }

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
            heightFieldBase,
            heightFieldBrush,
            scene,
            sdfProxies,
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
        RemoveGraphRenderTargets();
        resourceRegistry.RemoveRenderTarget("scene-hdr-target");
        resourceRegistry.RemoveRenderTarget("scene-metadata-target");
        resourceRegistry.RemoveRenderTarget("scene-control-target");
        resourceRegistry.RemoveResource("scene-depth-target");
        resourceRegistry.RemoveRenderTarget("height-field-target");
        DisposeBloomRenderTargets();
        DisposeHistoryRenderTargets();
        DisposeGraphRenderTargets();
        sceneRenderTarget.Dispose();
        sceneMetadataRenderTarget.Dispose();
        sceneControlRenderTarget.Dispose();
        sceneDepthTarget.Dispose();
        heightFieldRenderTarget.Dispose();
        for (var index = 0; index < frames.Length; index++)
        {
            resourceRegistry.RemoveResource($"backbuffer-{index}");
            frames[index].BackBuffer.Dispose();
        }

        width = newWidth;
        height = newHeight;
        renderTargetViewArena.Dispose();
        depthStencilViewArena.Dispose();
        staticShaderDescriptorArena.Dispose();
        renderTargetViewArena = CreateRenderTargetViewArena();
        depthStencilViewArena = CreateDepthStencilViewArena();
        staticShaderDescriptorArena = CreateStaticShaderDescriptorArena();

        swapChain.ResizeBuffers(BackBufferCount, (uint)width, (uint)height, Format.B8G8R8A8_UNorm, SwapChainFlags.None).CheckError();
        frameIndex = (int)swapChain.CurrentBackBufferIndex;
        CreateRenderTargetViews();
        CreateBackBufferOverlays();
        heightFieldRenderTarget = CreateHeightFieldRenderTarget();
        sceneRenderTarget = CreateSceneRenderTarget();
        sceneMetadataRenderTarget = CreateSceneAuxiliaryRenderTarget("scene-metadata-target", "Aquarium D3D12 Scene Metadata Target");
        sceneControlRenderTarget = CreateSceneAuxiliaryRenderTarget("scene-control-target", "Aquarium D3D12 Scene Control Target");
        sceneDepthStencilView = depthStencilViewArena.Allocate();
        sceneDepthTarget = CreateSceneDepthTarget(sceneDepthStencilView);
        CreateHistoryRenderTargets();
        CreateBloomRenderTargets();
        CreateGraphRenderTargets();
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

    private D3D12TrackedResource CreateSceneDepthTarget(D3D12DescriptorSlot depthStencilView)
    {
        var clearValue = new ClearValue
        {
            Format = SceneDepthFormat,
            DepthStencil = new DepthStencilValue(1.0f, 0),
        };
        var resource = device.CreateCommittedResource(
            HeapType.Default,
            ResourceDescription.Texture2D(
                SceneDepthFormat,
                (uint)width,
                (uint)height,
                1,
                1,
                1,
                0,
                Vortice.Direct3D12.ResourceFlags.AllowDepthStencil),
            ResourceStates.DepthWrite,
            clearValue);
        device.CreateDepthStencilView(resource, new DepthStencilViewDescription
        {
            Format = SceneDepthFormat,
            ViewDimension = DepthStencilViewDimension.Texture2D,
            Texture2D = new Texture2DDepthStencilView(),
        }, depthStencilView.Cpu);
        var target = new D3D12TrackedResource(resource, ResourceStates.DepthWrite, "Aquarium D3D12 Scene Depth Target", true);
        resourceRegistry.Add("scene-depth-target", target);
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
        }
    }

    private void RemoveHistoryRenderTargets()
    {
        for (var index = 0; index < 2; index++)
        {
            resourceRegistry.RemoveRenderTarget($"history-{index}");
            resourceRegistry.RemoveRenderTarget($"history-metadata-{index}");
            resourceRegistry.RemoveRenderTarget($"history-control-{index}");
        }
    }

    private void DisposeHistoryRenderTargets()
    {
        for (var index = 0; index < 2; index++)
        {
            historyRenderTargets[index]?.Dispose();
            historyMetadataRenderTargets[index]?.Dispose();
            historyControlRenderTargets[index]?.Dispose();
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

    private D3D12RenderTarget CreateHeightFieldRenderTarget()
    {
        var target = new D3D12RenderTarget(
            device,
            HeightFieldTextureSize,
            HeightFieldTextureSize,
            HeightFieldFormat,
            renderTargetViewArena.Allocate(),
            staticShaderDescriptorArena.Allocate(),
            null,
            false,
            new Color4(0.0f, 0.0f, 0.0f, 1.0f),
            "Aquarium D3D12 Height Field Target");
        resourceRegistry.Add("height-field-target", target);
        return target;
    }

    private void CreateGraphRenderTargets()
    {
        foreach (var targetDescription in renderGraph.RenderTargets)
        {
            if (IsLegacyGraphTarget(targetDescription.Handle.Name) || graphRenderTargets.ContainsKey(targetDescription.Handle.Name))
            {
                continue;
            }

            var targetSize = ResolveGraphTargetSize(targetDescription.Size);
            var target = new D3D12RenderTarget(
                device,
                targetSize.Width,
                targetSize.Height,
                ToDxgiFormat(targetDescription.Format),
                renderTargetViewArena.Allocate(),
                null,
                null,
                allowUnorderedAccess: false,
                new Color4(0.0f, 0.0f, 0.0f, 1.0f),
                $"Aquarium D3D12 Graph Target {targetDescription.Handle.Name}");
            graphRenderTargets.Add(targetDescription.Handle.Name, target);
            resourceRegistry.Add($"graph-target:{targetDescription.Handle.Name}", target);
        }
    }

    private void RemoveGraphRenderTargets()
    {
        foreach (var name in graphRenderTargets.Keys)
        {
            resourceRegistry.RemoveRenderTarget($"graph-target:{name}");
        }
    }

    private void DisposeGraphRenderTargets()
    {
        foreach (var target in graphRenderTargets.Values)
        {
            target.Dispose();
        }

        graphRenderTargets.Clear();
    }

    private (int Width, int Height) ResolveGraphTargetSize(AquariumTargetSize size)
    {
        return size.Kind switch
        {
            AquariumTargetSizeKind.Fixed => (Math.Max(1, size.Width), Math.Max(1, size.Height)),
            AquariumTargetSizeKind.MatchWindow => (
                Math.Max(1, (int)MathF.Ceiling(width * MathF.Max(size.Scale, 0.001f))),
                Math.Max(1, (int)MathF.Ceiling(height * MathF.Max(size.Scale, 0.001f)))),
            _ => throw new ArgumentOutOfRangeException(nameof(size), size.Kind, "Unsupported render target size policy."),
        };
    }

    private static Format ToDxgiFormat(RenderFormat format)
    {
        return format switch
        {
            RenderFormat.R16Float => Format.R16_Float,
            RenderFormat.Rgba16Float => Format.R16G16B16A16_Float,
            RenderFormat.Bgra8Unorm => Format.B8G8R8A8_UNorm,
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported render target format."),
        };
    }

    private static bool IsLegacyGraphTarget(string name)
    {
        return string.Equals(name, "height-field", StringComparison.Ordinal)
            || string.Equals(name, "scene", StringComparison.Ordinal)
            || string.Equals(name, "scene-metadata", StringComparison.Ordinal)
            || string.Equals(name, "scene-control", StringComparison.Ordinal);
    }

    private void RenderSceneAndPresent(D3D12PassContext context, FrameResources frameResources)
    {
            context.CommandList.BeginEvent("Scene Pass");
        try
        {
            heightFieldRenderTarget.Transition(context.CommandList, ResourceStates.PixelShaderResource);
            sceneRenderTarget.Transition(context.CommandList, ResourceStates.RenderTarget);
            sceneMetadataRenderTarget.Transition(context.CommandList, ResourceStates.RenderTarget);
            sceneControlRenderTarget.Transition(context.CommandList, ResourceStates.RenderTarget);
            sceneDepthTarget.Transition(context.CommandList, ResourceStates.DepthWrite);
            context.CommandList.ClearRenderTargetView(sceneRenderTarget.RenderTargetView.Cpu, new Color4(0.0f, 0.0f, 0.0f, 1.0f));
            context.CommandList.ClearRenderTargetView(sceneMetadataRenderTarget.RenderTargetView.Cpu, new Color4(0.0f, 0.0f, 0.0f, 1.0f));
            context.CommandList.ClearRenderTargetView(sceneControlRenderTarget.RenderTargetView.Cpu, new Color4(0.0f, 0.0f, 0.0f, 1.0f));
            context.CommandList.ClearDepthStencilView(sceneDepthStencilView.Cpu, ClearFlags.Depth, 1.0f, 0);
            context.CommandList.SetDescriptorHeaps(frameResources.TransientShaderDescriptors.Heap);
            context.CommandList.SetPipelineState(scenePipelineState!);
            context.CommandList.SetGraphicsRootSignature(fullscreenRootSignature);
            context.CommandList.SetGraphicsRootDescriptorTable(RootFrameConstants, frameResources.FrameConstantsDescriptor.Gpu);
            context.CommandList.SetGraphicsRootDescriptorTable(RootSourceTexture, frameResources.HeightFieldDescriptor.Gpu);
            context.CommandList.SetGraphicsRootDescriptorTable(RootSdfLights, frameResources.SdfLightDescriptor.Gpu);
            context.CommandList.SetGraphicsRootDescriptorTable(RootStudioPmrem, frameResources.StudioPmremDescriptor.Gpu);
            context.CommandList.SetGraphicsRootDescriptorTable(RootStudioIrradiance, frameResources.StudioIrradianceDescriptor.Gpu);
            context.CommandList.SetGraphicsRootDescriptorTable(RootSdfObjects, frameResources.SdfObjectDescriptor.Gpu);
            context.CommandList.RSSetViewports(viewport);
            context.CommandList.RSSetScissorRects(scissorRect);
            context.CommandList.OMSetRenderTargets(
            [
                sceneRenderTarget.RenderTargetView.Cpu,
                sceneMetadataRenderTarget.RenderTargetView.Cpu,
                sceneControlRenderTarget.RenderTargetView.Cpu,
            ],
            sceneDepthStencilView.Cpu);
            context.CommandList.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            context.CommandList.DrawInstanced(3, 1, 0, 0);

            for (var sdfIndex = 0; sdfIndex < sdfProxyPipelineStates.Length; sdfIndex++)
            {
                context.CommandList.SetPipelineState(sdfProxyPipelineStates[sdfIndex]!);
                context.CommandList.DrawInstanced(6, 1, 0, 0);
            }
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
            activeCommandList.SetGraphicsRootDescriptorTable(RootFrameConstants, frameResources.FrameConstantsDescriptor.Gpu);
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
        activeCommandList.SetGraphicsRootDescriptorTable(RootSourceTexture, sourceDescriptor.Gpu);
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
            historyRenderTargets[historyReadIndex].Transition(context.CommandList, ResourceStates.PixelShaderResource);
            historyMetadataRenderTargets[historyReadIndex].Transition(context.CommandList, ResourceStates.PixelShaderResource);
            historyControlRenderTargets[historyReadIndex].Transition(context.CommandList, ResourceStates.PixelShaderResource);
            historyRenderTargets[historyWriteIndex].Transition(context.CommandList, ResourceStates.RenderTarget);
            historyMetadataRenderTargets[historyWriteIndex].Transition(context.CommandList, ResourceStates.RenderTarget);
            historyControlRenderTargets[historyWriteIndex].Transition(context.CommandList, ResourceStates.RenderTarget);
            context.CommandList.ClearRenderTargetView(historyRenderTargets[historyWriteIndex].RenderTargetView.Cpu, new Color4(0.0f, 0.0f, 0.0f, 1.0f));
            context.CommandList.ClearRenderTargetView(historyMetadataRenderTargets[historyWriteIndex].RenderTargetView.Cpu, new Color4(0.0f, 0.0f, 0.0f, 1.0f));
            context.CommandList.ClearRenderTargetView(historyControlRenderTargets[historyWriteIndex].RenderTargetView.Cpu, new Color4(0.0f, 0.0f, 0.0f, 1.0f));

            context.BackBuffer.Transition(context.CommandList, ResourceStates.RenderTarget);
            context.CommandList.SetDescriptorHeaps(frameResources.TransientShaderDescriptors.Heap);
            context.CommandList.SetPipelineState(resolvePipelineState!);
            context.CommandList.SetGraphicsRootSignature(fullscreenRootSignature);
            context.CommandList.SetGraphicsRootDescriptorTable(RootFrameConstants, frameResources.FrameConstantsDescriptor.Gpu);
            context.CommandList.SetGraphicsRootDescriptorTable(RootSourceTexture, frameResources.SceneDescriptor.Gpu);
            context.CommandList.SetGraphicsRootDescriptorTable(RootBloom, frameResources.BloomPresentationDescriptor.Gpu);
            context.CommandList.SetGraphicsRootDescriptorTable(RootCurrentSceneMetadata, frameResources.SceneMetadataDescriptor.Gpu);
            context.CommandList.SetGraphicsRootDescriptorTable(RootCurrentSceneControl, frameResources.SceneControlDescriptor.Gpu);
            context.CommandList.SetGraphicsRootDescriptorTable(RootHistory, frameResources.HistoryDescriptor.Gpu);
            context.CommandList.SetGraphicsRootDescriptorTable(RootHistoryMetadata, frameResources.HistoryMetadataDescriptor.Gpu);
            context.CommandList.SetGraphicsRootDescriptorTable(RootHistoryControl, frameResources.HistoryControlDescriptor.Gpu);
            context.CommandList.SetGraphicsRootDescriptorTable(RootSdfObjects, frameResources.SdfObjectDescriptor.Gpu);

            context.CommandList.RSSetViewports(viewport);
            context.CommandList.RSSetScissorRects(scissorRect);
            context.CommandList.OMSetRenderTargets(
            [
                context.RenderTargetView,
                historyRenderTargets[historyWriteIndex].RenderTargetView.Cpu,
                historyMetadataRenderTargets[historyWriteIndex].RenderTargetView.Cpu,
                historyControlRenderTargets[historyWriteIndex].RenderTargetView.Cpu,
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

    private void UploadSdfLightResources(ID3D12GraphicsCommandList activeCommandList, FrameResources frameResources)
    {
        activeCommandList.BeginEvent("SDF State Upload");
        try
        {
            sdfLightBuffer.Upload(activeCommandList, frameResources.UploadRing, sdfLights);
        sdfObjectBuffer.Upload(activeCommandList, frameResources.UploadRing, sdfObjects);
        }
        finally
        {
            activeCommandList.EndEvent();
        }
    }

    private void RenderHeightField(ID3D12GraphicsCommandList activeCommandList, FrameResources frameResources)
    {
        var viewport = new Viewport(0.0f, 0.0f, HeightFieldTextureSize, HeightFieldTextureSize);
        var scissorRect = new RawRect(0, 0, HeightFieldTextureSize, HeightFieldTextureSize);

        activeCommandList.BeginEvent("Height Field Pass");
        try
        {
            heightFieldRenderTarget.Transition(activeCommandList, ResourceStates.RenderTarget);
            activeCommandList.ClearRenderTargetView(heightFieldRenderTarget.RenderTargetView.Cpu, new Color4(0.0f, 0.0f, 0.0f, 1.0f));
            activeCommandList.SetDescriptorHeaps(frameResources.TransientShaderDescriptors.Heap);
            activeCommandList.SetPipelineState(heightFieldBasePipelineState!);
            activeCommandList.SetGraphicsRootSignature(fullscreenRootSignature);
            activeCommandList.SetGraphicsRootDescriptorTable(RootFrameConstants, frameResources.FrameConstantsDescriptor.Gpu);
            activeCommandList.RSSetViewports(viewport);
            activeCommandList.RSSetScissorRects(scissorRect);
            activeCommandList.OMSetRenderTargets(heightFieldRenderTarget.RenderTargetView.Cpu, null);
            activeCommandList.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            activeCommandList.DrawInstanced(3, 1, 0, 0);
            activeCommandList.SetPipelineState(heightFieldBrushPipelineState!);
            activeCommandList.SetGraphicsRootDescriptorTable(RootHeightFieldBrushes, frameResources.HeightFieldBrushConstantsDescriptor.Gpu);
            activeCommandList.DrawInstanced(6, D3D12HeightFieldBrushConstants.MaxBrushCount, 0, 0);
        }
        finally
        {
            activeCommandList.EndEvent();
        }
    }

    private void CopySceneState(AquariumSceneState scene)
    {
        Array.Clear(sdfObjects);
        Array.Clear(sdfLights);
        heightFieldBrushConstants = D3D12HeightFieldBrushConstants.FromBrushes(scene.HeightFieldBrushes);

        var objectCount = Math.Min(scene.SdfObjects.Count, MaxSdfObjectCount);
        for (var index = 0; index < objectCount; index++)
        {
            sdfObjects[index] = scene.SdfObjects[index];
        }

        var lightCount = Math.Min(scene.SdfLights.Count, MaxSdfLightCount);
        for (var index = 0; index < lightCount; index++)
        {
            sdfLights[index] = scene.SdfLights[index];
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

    private static string ResolveShaderSourceRoot(string? shaderPath, AquariumShaderManifest manifest)
    {
        if (!string.IsNullOrWhiteSpace(shaderPath))
        {
            var sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(shaderPath));
            if (!string.IsNullOrWhiteSpace(sourceDirectory))
            {
                return sourceDirectory;
            }
        }

        if (!string.IsNullOrWhiteSpace(manifest.ShaderRoot))
        {
            return Path.GetFullPath(manifest.ShaderRoot);
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
                heightFieldBasePipelineState!,
                heightFieldBrushPipelineState!,
                scenePipelineState!,
                sdfProxyPipelineStates.Select(pipeline => pipeline!).ToArray(),
                bloomPrefilterPipelineState!,
                bloomDownsamplePipelineState!,
                bloomBlurHorizontalPipelineState!,
                bloomBlurVerticalPipelineState!,
                resolvePipelineState!)
            : null;
    }

    private void ApplyPipelineSet(D3D12PipelineSet pipelines)
    {
        heightFieldBasePipelineState = pipelines.HeightFieldBase;
        heightFieldBrushPipelineState = pipelines.HeightFieldBrush;
        scenePipelineState = pipelines.Scene;
        for (var index = 0; index < sdfProxyPipelineStates.Length; index++)
        {
            sdfProxyPipelineStates[index] = pipelines.SdfProxies[index];
        }
        bloomPrefilterPipelineState = pipelines.BloomPrefilter;
        bloomDownsamplePipelineState = pipelines.BloomDownsample;
        bloomBlurHorizontalPipelineState = pipelines.BloomBlurHorizontal;
        bloomBlurVerticalPipelineState = pipelines.BloomBlurVertical;
        resolvePipelineState = pipelines.Resolve;
    }

    private void DisposePipelineStates()
    {
        CapturePipelineSetOrNull()?.Dispose();
        heightFieldBasePipelineState = null;
        heightFieldBrushPipelineState = null;
        scenePipelineState = null;
        Array.Clear(sdfProxyPipelineStates);
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

    private D3D12DescriptorArena CreateDepthStencilViewArena()
    {
        return new D3D12DescriptorArena(
            device,
            DescriptorHeapType.DepthStencilView,
            8,
            DescriptorHeapFlags.None,
            "Aquarium D3D12 DSV Arena");
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
        var heightFieldBrushRange = new DescriptorRange(
            DescriptorRangeType.ConstantBufferView,
            1,
            1,
            0,
            D3D12.DescriptorRangeOffsetAppend);
        var sdfLightRange = new DescriptorRange(
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
        var studioPmremRange = new DescriptorRange(
            DescriptorRangeType.ShaderResourceView,
            1,
            22,
            0,
            D3D12.DescriptorRangeOffsetAppend);
        var studioIrradianceRange = new DescriptorRange(
            DescriptorRangeType.ShaderResourceView,
            1,
            23,
            0,
            D3D12.DescriptorRangeOffsetAppend);
        var sdfObjectRange = new DescriptorRange(
            DescriptorRangeType.ShaderResourceView,
            1,
            24,
            0,
            D3D12.DescriptorRangeOffsetAppend);
        var rootParameters = new[]
        {
            new RootParameter(new RootDescriptorTable([constantBufferRange]), ShaderVisibility.All),
            new RootParameter(new RootDescriptorTable([sourceTextureRange]), ShaderVisibility.Pixel),
            new RootParameter(new RootDescriptorTable([heightFieldBrushRange]), ShaderVisibility.All),
            new RootParameter(new RootDescriptorTable([sdfLightRange]), ShaderVisibility.Pixel),
            new RootParameter(new RootDescriptorTable([bloomRange]), ShaderVisibility.Pixel),
            new RootParameter(new RootDescriptorTable([currentSceneMetadataRange]), ShaderVisibility.Pixel),
            new RootParameter(new RootDescriptorTable([currentSceneControlRange]), ShaderVisibility.Pixel),
            new RootParameter(new RootDescriptorTable([historyRange]), ShaderVisibility.Pixel),
            new RootParameter(new RootDescriptorTable([historyMetadataRange]), ShaderVisibility.Pixel),
            new RootParameter(new RootDescriptorTable([historyControlRange]), ShaderVisibility.Pixel),
            new RootParameter(new RootDescriptorTable([studioPmremRange]), ShaderVisibility.Pixel),
            new RootParameter(new RootDescriptorTable([studioIrradianceRange]), ShaderVisibility.Pixel),
            new RootParameter(new RootDescriptorTable([sdfObjectRange]), ShaderVisibility.All),
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
        return CreateFullscreenPipelineState(path, "FullscreenTriangleVS", "D3D12ScenePS", [SceneHdrFormat, SceneHdrFormat, SceneHdrFormat], enableDepth: true);
    }

    private ID3D12PipelineState CreateSdfObjectProxyPipelineState(string path)
    {
        return CreateFullscreenPipelineState(path, "D3D12SdfObjectProxyVS", "D3D12SdfProxyPS", [SceneHdrFormat, SceneHdrFormat, SceneHdrFormat], enableDepth: true);
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
            [Format.B8G8R8A8_UNorm, SceneHdrFormat, SceneHdrFormat, SceneHdrFormat]);
    }

    private ID3D12PipelineState CreateHeightFieldBasePipelineState(string path)
    {
        return CreateFullscreenPipelineState(path, "FullscreenTriangleVS", "D3D12HeightFieldBasePS", HeightFieldFormat);
    }

    private ID3D12PipelineState CreateHeightFieldBrushPipelineState(string path)
    {
        return CreateFullscreenPipelineState(
            path,
            "D3D12HeightFieldBrushVS",
            "D3D12HeightFieldBrushPS",
            HeightFieldFormat,
            new BlendDescription(Blend.One, Blend.One, Blend.One, Blend.One));
    }

    private ID3D12PipelineState CreateFullscreenPipelineState(
        string path,
        string vertexEntryPoint,
        string pixelEntryPoint,
        Format renderTargetFormat,
        BlendDescription? blendDescription = null,
        bool enableDepth = false)
    {
        return CreateFullscreenPipelineState(
            path,
            vertexEntryPoint,
            pixelEntryPoint,
            [renderTargetFormat],
            blendDescription,
            enableDepth);
    }

    private ID3D12PipelineState CreateFullscreenPipelineState(
        string path,
        string vertexEntryPoint,
        string pixelEntryPoint,
        IReadOnlyList<Format> renderTargetFormats,
        BlendDescription? blendDescription = null,
        bool enableDepth = false)
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
            DepthStencilState = enableDepth
                ? new DepthStencilDescription
                {
                    DepthEnable = true,
                    DepthWriteMask = DepthWriteMask.All,
                    DepthFunc = ComparisonFunction.LessEqual,
                    StencilEnable = false,
                }
                : DepthStencilDescription.None,
            SampleMask = uint.MaxValue,
            PrimitiveTopologyType = PrimitiveTopologyType.Triangle,
            RenderTargetFormats = renderTargetFormats.ToArray(),
            SampleDescription = new SampleDescription(1, 0),
            DepthStencilFormat = enableDepth ? SceneDepthFormat : Format.Unknown,
        };

        try
        {
            return device.CreateGraphicsPipelineState(description);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to create D3D12 pipeline VS={vertexEntryPoint} PS={pixelEntryPoint} depth={enableDepth}.", ex);
        }
    }

    private static ReadOnlyMemory<byte> CompileShader(string path, string entryPoint, string profile)
    {
        var shaderFlags = ShaderFlags.EnableStrictness;
#if DEBUG
        shaderFlags |= ShaderFlags.Debug | ShaderFlags.SkipOptimization;
#endif

        var source = ExpandShaderIncludes(path, []);
        return Compiler.Compile(source, entryPoint, path, profile, shaderFlags, EffectFlags.None);
    }

    private static string ExpandShaderIncludes(string path, HashSet<string> stack)
    {
        var fullPath = Path.GetFullPath(path);
        if (!stack.Add(fullPath))
        {
            throw new InvalidOperationException($"Circular shader include detected at {fullPath}");
        }

        var lines = File.ReadAllLines(fullPath);
        var expanded = new List<string>(lines.Length);
        var directory = Path.GetDirectoryName(fullPath) ?? AppContext.BaseDirectory;
        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var line = lines[lineIndex];
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("#include \"", StringComparison.Ordinal))
            {
                var firstQuote = trimmed.IndexOf('"');
                var secondQuote = trimmed.IndexOf('"', firstQuote + 1);
                if (firstQuote >= 0 && secondQuote > firstQuote)
                {
                    var includeName = trimmed.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
                    var includePath = Path.GetFullPath(Path.Combine(directory, includeName));
                    expanded.Add($"#line 1 \"{includePath.Replace("\\", "\\\\")}\"");
                    expanded.Add(ExpandShaderIncludes(includePath, stack));
                    expanded.Add($"#line {lineIndex + 2} \"{fullPath.Replace("\\", "\\\\")}\"");
                    continue;
                }
            }

            expanded.Add(line);
        }

        stack.Remove(fullPath);
        return string.Join(Environment.NewLine, expanded);
    }

    private readonly record struct D3D12PassContext(
        ID3D12GraphicsCommandList CommandList,
        D3D12TrackedResource BackBuffer,
        CpuDescriptorHandle RenderTargetView);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct FrameConstants(
        Vector2 Resolution,
        float TimeSeconds,
        float viewRadius,
        Vector3 CameraPosition,
        float FarDistance,
        Vector2 viewCenter,
        float FrameIndex,
        float PreviousTimeSeconds,
        Vector3 PreviousCameraPosition,
        float previousViewRadius,
        Vector2 previousViewCenter,
        Vector2 JitterPixels,
        Vector2 PreviousJitterPixels,
        float RenderDebugMode,
        float Exposure,
        float BloomIntensity,
        float BloomVeilIntensity,
        Vector4 CursorWorlds);

    private sealed record D3D12ShaderPaths(
        string HeightField,
        string Scene,
        string SdfCommon,
        string SdfProxy,
        IReadOnlyList<string> SdfShaders,
        string SdfMath,
        IReadOnlyList<string> Includes,
        string Post)
    {
        public IReadOnlyList<string> All { get; } = [HeightField, Scene, SdfCommon, SdfProxy, ..SdfShaders, SdfMath, ..Includes, Post];

        public static D3D12ShaderPaths FromManifest(string root, AquariumShaderManifest manifest)
        {
            string shaderPath(string path) => Path.IsPathRooted(path) ? path : Path.Combine(root, path);
            var includes = manifest.SdfLibraryInclude is null
                ? manifest.IncludePaths
                : [.. manifest.IncludePaths, manifest.SdfLibraryInclude];
            return new D3D12ShaderPaths(
                shaderPath(manifest.HeightFieldShader),
                shaderPath(manifest.SceneShader),
                shaderPath(manifest.SdfCommonInclude),
                shaderPath(manifest.SdfProxyInclude),
                manifest.SdfShaderPaths.Select(shaderPath).ToArray(),
                shaderPath(manifest.SdfMathInclude),
                includes.Select(shaderPath).ToArray(),
                shaderPath(manifest.PostShader));
        }
    }

    private sealed record D3D12PipelineSet(
        ID3D12PipelineState HeightFieldBase,
        ID3D12PipelineState HeightFieldBrush,
        ID3D12PipelineState Scene,
        IReadOnlyList<ID3D12PipelineState> SdfProxies,
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
            foreach (var sdfProxy in SdfProxies)
            {
                sdfProxy.Dispose();
            }

            Scene.Dispose();
            HeightFieldBrush.Dispose();
            HeightFieldBase.Dispose();
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

        public D3D12DescriptorSlot HeightFieldBrushConstantsDescriptor { get; set; }

        public D3D12DescriptorSlot SdfLightDescriptor { get; set; }

        public D3D12DescriptorSlot SdfObjectDescriptor { get; set; }

        public D3D12DescriptorSlot StudioPmremDescriptor { get; set; }

        public D3D12DescriptorSlot StudioIrradianceDescriptor { get; set; }

        public D3D12DescriptorSlot HeightFieldDescriptor { get; set; }

        public D3D12DescriptorSlot SceneDescriptor { get; set; }

        public D3D12DescriptorSlot SceneMetadataDescriptor { get; set; }

        public D3D12DescriptorSlot SceneControlDescriptor { get; set; }

        public D3D12DescriptorSlot HistoryDescriptor { get; set; }

        public D3D12DescriptorSlot HistoryMetadataDescriptor { get; set; }

        public D3D12DescriptorSlot HistoryControlDescriptor { get; set; }

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
