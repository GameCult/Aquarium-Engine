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
    private readonly LocalCastVisualStateReader reader;
    private float timeSeconds;
    private float sceneTimelineSeconds;
    private long lastFrameId = -1;
    private int latestPointCount;
    private string latestStatus = "waiting";

    public LocalCastRuntime(AquariumRuntimeOptions options)
    {
        Options = options;
        reader = new LocalCastVisualStateReader(ResolveVisualCachePath());
        Ui = new AquariumUiDocument()
            .Panel("LocalCast", 18.0f, 82.0f, 372.0f, panel =>
            {
                panel.Section("Camera Field");
                panel.Readout("Cache", () => reader.Path);
                panel.Readout("Frame", () => lastFrameId >= 0 ? $"{lastFrameId} / {latestPointCount} splats" : latestStatus);
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
            var gpuFusionField = gpuFusionAccumulator.BuildField(sceneTimelineSeconds);
            var scene = new AquariumSceneState
            {
                TraceHeightFieldSurface = false,
                UseStarfieldBackground = false,
                TemporalGaussianField = gpuFusionField.Seeds.Count == 0
                    ? temporalField
                    : AquariumTemporalGaussianField.Empty,
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
        Console.WriteLine($"LocalCast Aquarium runtime reading {reader.Path}");
    }

    public void Update(float deltaSeconds, InputState input)
    {
        timeSeconds += Math.Max(deltaSeconds, 0.0f);

        if (!reader.TryReadLatest(out var frame))
        {
            latestStatus = "waiting for visual-state.msgpack";
            sceneTimelineSeconds = MathF.Max(sceneTimelineSeconds, timeSeconds);
            return;
        }

        if (frame.FrameId != lastFrameId)
        {
            accumulator.Observe(mapper.Map(frame));
            gpuFusionAccumulator.Observe(frame);
            lastFrameId = frame.FrameId;
            latestPointCount = frame.Points.Count;
            latestStatus = "live";
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

    private string ResolveVisualCachePath()
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
}

public sealed class LocalCastRuntimeFactory : IAquariumRuntimeFactory
{
    public IAquariumRuntime Create(AquariumRuntimeOptions options)
    {
        return new LocalCastRuntime(options);
    }
}
