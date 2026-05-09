using System.Numerics;
using Aquarium.Engine.Input;
using Aquarium.Engine.State;

namespace Aquarium.Engine;

public sealed class AquariumRuntime : IAquariumRuntime
{
    private const float StateSaveIntervalSeconds = 0.20f;

    private readonly OrbitCameraRig cameraRig;
    private readonly AquariumCultStateStore stateStore;

    private GridFrame gridFrame;
    private GraphicsSettings graphicsSettings;
    private float timeSeconds;
    private float secondsSinceStateSave;
    private bool graphicsSettingsDirty;

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
        gridFrame = GridFrame.FromCamera(cameraRig.Target, cameraRig.Distance);
    }

    public AquariumRuntimeOptions Options { get; }

    public AquariumFrame Frame => new(gridFrame, cameraRig.Position, timeSeconds);

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

    public void Start()
    {
        Console.WriteLine("Aquarium Engine booted.");
        Console.WriteLine($"Grid center: {gridFrame.Center}, radius: {gridFrame.Radius:0.00}");
        Console.WriteLine($"Graphics settings: debug {graphicsSettings.RenderDebugMode}, exposure {graphicsSettings.SceneExposure:0.###}, bloom {graphicsSettings.BloomIntensity:0.###}, veil {graphicsSettings.BloomVeilIntensity:0.###}");
        Console.WriteLine($"CultCache: {stateStore.CachePath}");
        Console.WriteLine($"CultNet hello: {stateStore.Hello.RuntimeKind} / {stateStore.Hello.DisplayName}");
        Console.WriteLine("Aquarium host renderer owns the visible frame invariants.");
    }

    public void Update(float deltaSeconds, InputState input)
    {
        timeSeconds += Math.Max(deltaSeconds, 0.0f);
        cameraRig.ApplyInput(input, deltaSeconds);
        gridFrame = GridFrame.FromCamera(cameraRig.Target, cameraRig.Distance);
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
