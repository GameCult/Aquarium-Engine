using System.Numerics;
using Aquarium.Engine;
using Aquarium.Engine.Audio;
using Aquarium.Engine.Input;
using Aquarium.Engine.Render;
using Aquarium.Engine.Ui;
using Aquarium.Epiphany.State;
using Aquarium.Epiphany.Voice;

namespace Aquarium.Epiphany;

public sealed class AquariumRuntime : IAquariumRuntime
{
    private const float StateSaveIntervalSeconds = 0.20f;

    private readonly OrbitCameraRig cameraRig;
    private readonly AquariumCultStateStore stateStore;
    private readonly RealtimeFaceVoiceSession faceVoice;

    private ViewFrame ViewFrame;
    private GraphicsSettings graphicsSettings;
    private float timeSeconds;
    private float previousFrameTimeSeconds;
    private Vector2 previousCursorWorld;
    private float secondsSinceStateSave;
    private bool graphicsSettingsDirty;
    private bool timePaused;
    private string faceVoiceText = "";
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
        faceVoice = new RealtimeFaceVoiceSession(Audio)
        {
            AppServerUri = liveState.FaceVoiceAppServerUri,
            ThreadId = liveState.FaceVoiceThreadId,
            Voice = liveState.FaceVoiceVoice,
            Prompt = liveState.FaceVoicePrompt
        };
        Ui = new AquariumUiDocument()
            .Panel("Epiphany", 396.0f, 82.0f, 360.0f, panel => panel
                .Section("Runtime")
                .Toggle("Pause Time", () => timePaused, value => timePaused = value, "Stops Epiphany simulation time while leaving the renderer alive.")
                .Slider("Time", () => timeSeconds, value => timeSeconds = MathF.Max(0.0f, value), 0.0f, 720.0f, "0.0", "Scrubs Epiphany simulation time.")
                .Button("Flush State", FlushState, "Writes current Epiphany runtime state to CultCache.")
                .Section("Face Voice")
                .Text("App Server", () => faceVoice.AppServerUri, value => faceVoice.AppServerUri = value.Trim(), "Loopback Codex app-server websocket, e.g. ws://127.0.0.1:8765.")
                .Text("Thread", () => faceVoice.ThreadId, value => faceVoice.ThreadId = value.Trim(), "Thread id that owns the realtime Face session.")
                .Text("Voice", () => faceVoice.Voice, value => faceVoice.Voice = value.Trim(), "Realtime voice name, such as marin, cedar, or cove.")
                .Readout("Status", () => faceVoice.Status, "Realtime transport status.")
                .Readout("Audio", () => faceVoice.AudioStats, "Received output-audio chunks from Codex realtime.")
                .TextBox("Prompt", () => faceVoice.Prompt, value => faceVoice.Prompt = value, lines: 3, acceptsReturn: true, monospace: false, tooltip: "Backend realtime instructions for Face. This is speech behavior, not memory authority.")
                .TextBox("Say", () => faceVoiceText, value => faceVoiceText = value, lines: 2, acceptsReturn: false, submit: SendFaceVoiceText, monospace: false, tooltip: "Send one text utterance through Face voice.")
                .Button("Start Voice", faceVoice.Start, "Starts a thread-scoped realtime session with audio output.")
                .Button("Stop Voice", faceVoice.Stop, "Stops the realtime Face voice session.")
                .Button("Clear Voice Log", faceVoice.ClearTranscript, "Clears local transcript and audio counters.")
                .Readout("Error", () => faceVoice.LastError, "Last realtime transport error.")
                .TextBox("Transcript", () => faceVoice.Transcript, _ => { }, lines: 7, acceptsReturn: true, monospace: false, tooltip: "Local ephemeral realtime transcript. It is not durable Epiphany memory."))
            .Command("pause", args =>
            {
                timePaused = args.Count == 0 ? !timePaused : bool.TryParse(args[0], out var value) && value;
                return $"pause {timePaused}";
            }, "Toggles or sets Epiphany simulation pause.")
            .Command("time", args =>
            {
                if (args.Count > 0 && float.TryParse(args[0], out var value))
                {
                    timeSeconds = MathF.Max(0.0f, value);
                }

                return $"time {timeSeconds:0.###}";
            }, "Reads or sets Epiphany simulation time.")
            .Command("flush", _ =>
            {
                FlushState();
                return "state flushed";
            }, "Flushes Epiphany state to CultCache.")
            .Command("facevoice", args =>
            {
                if (args.Count == 0)
                {
                    return $"Face voice {faceVoice.Status}; audio {faceVoice.AudioStats}";
                }

                var verb = args[0].Trim().ToLowerInvariant();
                if (verb == "start")
                {
                    faceVoice.Start();
                    return "Face voice starting";
                }

                if (verb == "stop")
                {
                    faceVoice.Stop();
                    return "Face voice stopping";
                }

                if (verb == "say" && args.Count > 1)
                {
                    faceVoice.SendText(string.Join(" ", args.Skip(1)));
                    return "Face voice text sent";
                }

                return "facevoice start | stop | say <text>";
            }, "Controls the Face realtime voice session.");
    }

    public AquariumRuntimeOptions Options { get; }

    public AquariumRenderPlan RenderPlan { get; } = EpiphanyRenderPlan.Create();

    public AquariumUiDocument Ui { get; }

    public AquariumAudioDocument Audio { get; } = new();

    public AquariumSynthDocument Synth { get; } = AquariumSynthDocument.Empty;

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
        faceVoice.Dispose();
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
            CameraDistance = cameraRig.Distance,
            FaceVoiceAppServerUri = faceVoice.AppServerUri,
            FaceVoiceThreadId = faceVoice.ThreadId,
            FaceVoiceVoice = faceVoice.Voice,
            FaceVoicePrompt = faceVoice.Prompt
        });
    }

    private void SendFaceVoiceText()
    {
        var text = faceVoiceText.Trim();
        if (text.Length == 0)
        {
            return;
        }

        faceVoice.SendText(text);
        faceVoiceText = "";
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
