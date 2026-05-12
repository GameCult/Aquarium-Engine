using System.Numerics;
using Aquarium.Engine;
using Aquarium.Engine.Audio;
using Aquarium.Engine.Input;
using Aquarium.Engine.Render;
using Aquarium.Engine.Ui;
using Aquarium.Epiphany.State;

namespace Aquarium.Epiphany;

public sealed class AquariumRuntime : IAquariumRuntime
{
    private const float StateSaveIntervalSeconds = 0.20f;

    private readonly OrbitCameraRig cameraRig;
    private readonly AquariumCultStateStore stateStore;

    private ViewFrame ViewFrame;
    private GraphicsSettings graphicsSettings;
    private float timeSeconds;
    private float previousFrameTimeSeconds;
    private Vector2 previousCursorWorld;
    private float secondsSinceStateSave;
    private bool graphicsSettingsDirty;
    private bool timePaused;
    private string patchScript = "v w=sin f=440 g=.18 a=.002 s=.05 d=.35 pu=.28;v w=tri f=880 g=.04 s=.03 d=.28";
    private int patchRate = 4;
    private int faustCompileRevision;

    public AquariumRuntime(AquariumRuntimeOptions options)
    {
        Options = options;
        stateStore = AquariumCultStateStore.Open(options.CultCachePath);
        var liveState = stateStore.LoadOrCreate();
        graphicsSettings = stateStore.LoadOrCreateGraphicsSettings().ToSettings();
        if (options.RenderDebugModeOverride.HasValue)
        {
            GraphicsSettings = graphicsSettings with { RenderDebugMode = options.RenderDebugModeOverride.Value };
        }

        cameraRig = new OrbitCameraRig(
            target: new Vector2(liveState.CameraTargetX, liveState.CameraTargetY),
            yawRadians: liveState.CameraYawRadians,
            pitchRadians: liveState.CameraPitchRadians,
            distance: liveState.CameraDistance);
        timeSeconds = liveState.TimeSeconds;
        ViewFrame = ViewFrame.FromCamera(cameraRig.Target, cameraRig.Distance);
        previousFrameTimeSeconds = timeSeconds;
        previousCursorWorld = ViewFrame.Center;
        Ui = new AquariumUiDocument()
            .Panel("Epiphany", 396.0f, 82.0f, 360.0f, panel => panel
                .Section("Runtime")
                .Toggle("Pause Time", () => timePaused, value => timePaused = value, "Stops Epiphany simulation time while leaving the renderer alive.")
                .Slider("Time", () => timeSeconds, value => timeSeconds = MathF.Max(0.0f, value), 0.0f, 720.0f, "0.0", "Scrubs Epiphany simulation time.")
                .Button("Flush State", FlushState, "Writes current Epiphany runtime state to CultCache.")
                .Section("Synth")
                .Text("Patch", () => patchScript, value => patchScript = value, "Aquarium patch DSL. Enter inserts ';'.")
                .Slider("Rate", () => patchRate, value => patchRate = Math.Clamp(value, 1, 12), 1, 12, "Triggers per 16 seconds.")
                .Button("Compile Faust", () => faustCompileRevision++, "Exports Faust DSP and compiles C# output when Faust is installed."));
    }

    public AquariumRuntimeOptions Options { get; }

    public AquariumRenderPlan RenderPlan { get; } = EpiphanyRenderPlan.Create();

    public AquariumUiDocument Ui { get; }

    public AquariumSynthDocument Synth => new AquariumSynthDocument
    {
        MasterGain = 0.42f,
        Enabled = true
    }.Patch(
        "epiphany-editor",
        patchScript,
        AquariumSynthTrigger.Repeat(16.0f / Math.Max(patchRate, 1)),
        1.0f,
        faustCompileRevision,
        "epiphany_editor");

    public AquariumFrame Frame => new(ViewFrame, cameraRig.Position, timeSeconds);

    public GraphicsSettings GraphicsSettings
    {
        get => graphicsSettings;
        set
        {
            var normalized = value.Normalized();
            if (normalized == graphicsSettings)
            {
                return;
            }

            graphicsSettings = normalized;
            graphicsSettingsDirty = true;
        }
    }

    public AquariumFrame ComposeFrame(AquariumFrame frame, AquariumFrameInput input)
    {
        var frameWithCursor = frame with
        {
            CursorWorld = EpiphanySceneBuilder.ProjectMouseToGridPlane(
                input.MousePosition,
                input.Width,
                input.Height,
                frame.CameraPosition,
                frame.View.Center),
        };
        var scene = EpiphanySceneBuilder.Build(frameWithCursor, previousFrameTimeSeconds, previousCursorWorld);
        previousFrameTimeSeconds = frameWithCursor.TimeSeconds;
        previousCursorWorld = frameWithCursor.CursorWorld;
        return frameWithCursor with { Scene = scene };
    }

    public void Start()
    {
        Console.WriteLine("Aquarium Engine booted.");
        Console.WriteLine($"Grid center: {ViewFrame.Center}, radius: {ViewFrame.Radius:0.00}");
        Console.WriteLine($"Graphics settings: debug {graphicsSettings.RenderDebugMode}, exposure {graphicsSettings.SceneExposure:0.###}, bloom {graphicsSettings.BloomIntensity:0.###}, veil {graphicsSettings.BloomVeilIntensity:0.###}");
        Console.WriteLine($"CultCache: {stateStore.CachePath}");
        Console.WriteLine($"CultNet hello: {stateStore.Hello.RuntimeKind} / {stateStore.Hello.DisplayName}");
        Console.WriteLine("Aquarium host renderer owns the visible frame invariants.");
    }

    public void Update(float deltaSeconds, InputState input)
    {
        if (!timePaused)
        {
            timeSeconds += Math.Max(deltaSeconds, 0.0f);
        }

        cameraRig.ApplyInput(input, deltaSeconds);
        ViewFrame = ViewFrame.FromCamera(cameraRig.Target, cameraRig.Distance);
        secondsSinceStateSave += Math.Max(deltaSeconds, 0.0f);
        if (secondsSinceStateSave >= StateSaveIntervalSeconds)
        {
            FlushState();
            secondsSinceStateSave = 0.0f;
        }
    }

    public void FlushState()
    {
        SaveLiveState();
        SaveGraphicsSettingsIfDirty();
    }

    public void Dispose()
    {
        FlushState();
        stateStore.Dispose();
    }

    private void SaveLiveState()
    {
        stateStore.Save(new AquariumLiveState
        {
            TimeSeconds = timeSeconds,
            CameraTargetX = cameraRig.Target.X,
            CameraTargetY = cameraRig.Target.Y,
            CameraYawRadians = cameraRig.YawRadians,
            CameraPitchRadians = cameraRig.PitchRadians,
            CameraDistance = cameraRig.Distance
        });
    }

    private void SaveGraphicsSettingsIfDirty()
    {
        if (!graphicsSettingsDirty)
        {
            return;
        }

        stateStore.SaveGraphicsSettings(AquariumGraphicsSettingsState.FromSettings(graphicsSettings));
        graphicsSettingsDirty = false;
    }
}

public sealed class AquariumRuntimeFactory : IAquariumRuntimeFactory
{
    public IAquariumRuntime Create(AquariumRuntimeOptions options)
    {
        return new AquariumRuntime(options);
    }
}
