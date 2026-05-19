using System.Numerics;
using Aquarium.Engine;
using Aquarium.Engine.Audio;
using Aquarium.Engine.Fractal.Temporal;
using Aquarium.Engine.Input;
using Aquarium.Engine.Render;
using Aquarium.Engine.Ui;

namespace Aquarium.LocalCast;

public sealed class LocalCastRuntime : IAquariumRuntime
{
    private readonly TemporalGaussianAccumulator accumulator = new(
        accumulationWindowSeconds: 2.75f,
        presentationDelaySeconds: 0.35f,
        smoothing: 0.55f);
    private readonly LocalCastTemporalGaussianMapper mapper = new();
    private readonly LocalCastGpuFusionMapper gpuFusionMapper = new();
    private readonly LocalCastGpuFusionAccumulator gpuFusionAccumulator = new(
        ResolveGpuHistorySeconds(),
        smoothing: 0.45f);
    private readonly ILocalCastVisualFrameSource frameSource;
    private float timeSeconds;
    private float sceneTimelineSeconds;
    private long lastFrameId = -1;
    private long lastClapFrameId = -1;
    private int latestPointCount;
    private int latestClapCount;
    private string latestStatus = "waiting";
    private AquariumCalibrationEventFrame latestCalibrationEvents = AquariumCalibrationEventFrame.Empty;
    private AquariumGpuFusionField latestDirectGpuFusionField = AquariumGpuFusionField.Empty;

    public LocalCastRuntime(AquariumRuntimeOptions options)
        : this(options, new LocalCastVisualStateFileSource(ResolveVisualCachePath()))
    {
    }

    public LocalCastRuntime(AquariumRuntimeOptions options, ILocalCastVisualFrameSource frameSource)
    {
        Options = options;
        this.frameSource = frameSource;
        Ui = new AquariumUiDocument()
            .Panel("LocalCast", 18.0f, 82.0f, 372.0f, panel =>
            {
                panel.Section("Camera Field");
                panel.Readout("Source", () => this.frameSource.Description);
                panel.Readout("Frame", () => lastFrameId >= 0 ? $"{lastFrameId} / {latestPointCount} splats" : latestStatus);
                panel.Readout("Claps", () => lastClapFrameId >= 0 ? $"{lastClapFrameId} / {latestClapCount} events" : "none");
                panel.Readout("History", () => $"{gpuFusionAccumulator.TrackCount} tracked seeds");
                panel.Readout("Timeline", () => $"{sceneTimelineSeconds:0.000}s");
            });
    }

    public AquariumRuntimeOptions Options { get; }

    public GraphicsSettings GraphicsSettings { get; set; } = new(0, 1.38f, 0.32f, 0.04f);

    public AquariumRenderPlan RenderPlan { get; } = LocalCastRenderPlan.Create();

    public AquariumUiDocument Ui { get; }

    public AquariumAudioDocument Audio { get; } = new();

    public AquariumSynthDocument Synth { get; } = AquariumSynthDocument.Empty;

    public AquariumFrame Frame
    {
        get
        {
            var temporalField = accumulator.BuildField(sceneTimelineSeconds);
            var gpuFusionField = latestDirectGpuFusionField.HasInput
                ? latestDirectGpuFusionField
                : gpuFusionAccumulator.BuildField(sceneTimelineSeconds);
            var scene = new AquariumSceneState
            {
                TraceHeightFieldSurface = false,
                UseStarfieldBackground = false,
                TemporalGaussianField = !gpuFusionField.HasInput
                    ? temporalField
                    : AquariumTemporalGaussianField.Empty,
                CalibrationEventFrame = latestCalibrationEvents,
                GpuFusionField = gpuFusionField,
            };

            return new AquariumFrame(
                new ViewFrame(new Vector2(0.0f, 0.18f), 2.35f),
                new Vector3(0.0f, -2.35f, 1.35f),
                new Vector3(0.0f, 0.18f, 1.18f),
                timeSeconds,
                Vector2.Zero,
                scene);
        }
    }

    public AquariumFrame ComposeFrame(AquariumFrame frame, AquariumFrameInput input)
    {
        return frame;
    }

    public void Start()
    {
        Console.WriteLine($"LocalCast Aquarium runtime reading {frameSource.Description}");
    }

    public void Update(float deltaSeconds, InputState input)
    {
        timeSeconds += Math.Max(deltaSeconds, 0.0f);

        if (!frameSource.TryReadLatest(out var frame))
        {
            latestStatus = "waiting for visual-state.msgpack";
            sceneTimelineSeconds = MathF.Max(sceneTimelineSeconds, timeSeconds);
            return;
        }

        if (frame.FrameId != lastFrameId)
        {
            if (frame.NativeGpuFusionPointBuffer.HasInput)
            {
                latestDirectGpuFusionField = new AquariumGpuFusionField
                {
                    PointBuffer = frame.NativeGpuFusionPointBuffer,
                    AccumulationWindowSeconds = ResolveGpuHistorySeconds(),
                    PresentationDelaySeconds = 0.35f,
                };
            }
            else
            {
                accumulator.Observe(mapper.Map(frame));
                gpuFusionAccumulator.Observe(frame);
                latestDirectGpuFusionField = AquariumGpuFusionField.Empty;
            }

            lastFrameId = frame.FrameId;
            latestPointCount = frame.PointCount;
            latestStatus = "live";
        }

        if (frameSource.TryReadLatestClapEvents(out var clapFrame) && clapFrame.FrameId != lastClapFrameId)
        {
            latestCalibrationEvents = MapClapEvents(clapFrame);
            lastClapFrameId = clapFrame.FrameId;
            latestClapCount = clapFrame.Events.Count;
        }

        sceneTimelineSeconds = MathF.Max(
            sceneTimelineSeconds + Math.Max(deltaSeconds, 0.0f),
            gpuFusionMapper.ToTimelineSeconds(frame.PresentTimeNs));
    }

    public void FlushState()
    {
    }

    public void Dispose()
    {
    }

    private static string ResolveVisualCachePath()
    {
        var environmentPath = Environment.GetEnvironmentVariable("LOCALCAST_VISUAL_CACHE");
        if (!string.IsNullOrWhiteSpace(environmentPath))
        {
            return environmentPath;
        }

        return Path.Combine(
            "E:",
            "Projects",
            "LocalCastBridge",
            "calibration",
            "runs",
            "visual-state.msgpack");
    }

    private static float ResolveGpuHistorySeconds()
    {
        var raw = Environment.GetEnvironmentVariable("LOCALCAST_GPU_HISTORY_SECONDS");
        return float.TryParse(raw, out var seconds) && float.IsFinite(seconds) && seconds > 0.0f
            ? Math.Clamp(seconds, 1.0f, 120.0f)
            : 18.0f;
    }

    private static AquariumCalibrationEventFrame MapClapEvents(LocalCastClapCalibrationFrame frame)
    {
        return new AquariumCalibrationEventFrame
        {
            ClapEvents = frame.Events
                .Select(item => new AquariumClapCalibrationEvent(
                    item.StableKey,
                    item.PositionMeters,
                    item.AcousticOracleNs,
                    item.VisualObservedNs,
                    item.TimingUncertaintyMicroseconds,
                    item.VisualConfidence,
                    item.AcousticConfidence))
                .ToArray(),
        };
    }
}

public sealed class LocalCastRuntimeFactory : IAquariumRuntimeFactory
{
    public IAquariumRuntime Create(AquariumRuntimeOptions options)
    {
        return new LocalCastRuntime(options);
    }
}
