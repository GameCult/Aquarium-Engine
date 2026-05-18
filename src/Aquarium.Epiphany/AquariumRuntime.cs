using System.Numerics;
using System.Globalization;
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
    private readonly FaceVoiceRouter faceVoice;

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
        faceVoice = new FaceVoiceRouter(Audio, liveState);
        Ui = new AquariumUiDocument()
            .Panel("Epiphany", 396.0f, 82.0f, 360.0f, panel => panel
                .Section("Runtime")
                .Toggle("Pause Time", () => timePaused, value => timePaused = value, "Stops Epiphany simulation time while leaving the renderer alive.")
                .Slider("Time", () => timeSeconds, value => timeSeconds = MathF.Max(0.0f, value), 0.0f, 720.0f, "0.0", "Scrubs Epiphany simulation time.")
                .Button("Flush State", FlushState, "Writes current Epiphany runtime state to CultCache.")
                .Section("Face Voice")
                .Text("App Server", () => faceVoice.AppServerUri, value => faceVoice.AppServerUri = value.Trim(), "Loopback Codex app-server websocket, e.g. ws://127.0.0.1:8765.")
                .Toggle("Auto Route", () => faceVoice.AutoSelect, value => faceVoice.AutoSelect = value, "Routes speech to the nearest enabled Face endpoint by cursor/listener proximity.")
                .Slider("Audible Radius", () => faceVoice.AudibleRadius, value => faceVoice.AudibleRadius = value, 1.0f, 64.0f, "0.0", "Distance scale for Face voice gain and stereo pan.")
                .Readout("Active", () => faceVoice.ActiveSummary, "Currently selected Face endpoint and listener distance.")
                .Readout("Routes", () => faceVoice.RouteSummary, "Distances from the listener cursor to each Face endpoint.")
                .Text("Name", () => faceVoice.ActiveEndpoint.DisplayName, value => faceVoice.ActiveEndpoint.DisplayName = value.Trim(), "Display name for the active Face endpoint.")
                .Toggle("Enabled", () => faceVoice.ActiveEndpoint.Enabled, value => faceVoice.ActiveEndpoint.Enabled = value, "Allows this Face endpoint to speak and participate in auto routing.")
                .Text("Thread", () => faceVoice.ActiveEndpoint.ThreadId, value => faceVoice.ActiveEndpoint.ThreadId = value.Trim(), "Thread id that owns the active realtime Face session.")
                .Text("Voice", () => faceVoice.ActiveEndpoint.Voice, value => faceVoice.ActiveEndpoint.Voice = value.Trim(), "Realtime voice name, such as marin, cedar, or cove.")
                .Slider("Anchor X", () => faceVoice.ActiveEndpoint.Anchor.X, SetActiveFaceAnchorX, -32.0f, 32.0f, "0.0", "World X anchor for proximity routing.")
                .Slider("Anchor Y", () => faceVoice.ActiveEndpoint.Anchor.Y, SetActiveFaceAnchorY, -32.0f, 32.0f, "0.0", "World Y anchor for proximity routing.")
                .Readout("Status", () => faceVoice.ActiveStatus, "Realtime transport status for the active Face.")
                .TextBox("Prompt", () => faceVoice.ActiveEndpoint.Prompt, value => faceVoice.ActiveEndpoint.Prompt = value, lines: 3, acceptsReturn: true, monospace: false, tooltip: "Backend realtime instructions for the active Face. This is speech behavior, not memory authority.")
                .TextBox("Say", () => faceVoiceText, value => faceVoiceText = value, lines: 2, acceptsReturn: false, submit: SendFaceVoiceText, monospace: false, tooltip: "Send one text utterance through the active Face route.")
                .Button("Start Active", faceVoice.StartActive, "Starts the active thread-scoped realtime session with audio output.")
                .Button("Start Enabled", faceVoice.StartAll, "Starts every enabled Face endpoint so rumination loops can speak regardless of user proximity.")
                .Button("Stop Active", faceVoice.StopActive, "Stops the active realtime Face voice session.")
                .Button("Clear Voice Log", faceVoice.ClearActiveLog, "Clears local transcript and audio counters for the active Face.")
                .Readout("Error", () => faceVoice.LastError, "Last realtime transport error for the active Face.")
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
                    return $"Face voice {faceVoice.ActiveSummary}; {faceVoice.ActiveStatus}";
                }

                var verb = args[0].Trim().ToLowerInvariant();
                if (verb == "start")
                {
                    faceVoice.StartActive();
                    return $"Face voice starting: {faceVoice.ActiveSummary}";
                }

                if (verb == "stop")
                {
                    faceVoice.StopActive();
                    return $"Face voice stopping: {faceVoice.ActiveSummary}";
                }

                if (verb == "startall")
                {
                    faceVoice.StartAll();
                    return "Enabled Face voice sessions starting";
                }

                if (verb == "stopall")
                {
                    faceVoice.StopAll();
                    return "All Face voice sessions stopping";
                }

                if (verb == "say" && args.Count > 1)
                {
                    SendFaceVoiceText(string.Join(" ", args.Skip(1)));
                    return $"Face voice text sent to {faceVoice.ActiveEndpoint.Id}";
                }

                if (verb == "list")
                {
                    return faceVoice.RouteSummary;
                }

                if (verb == "auto" && args.Count > 1 && bool.TryParse(args[1], out var autoSelect))
                {
                    faceVoice.AutoSelect = autoSelect;
                    return $"Face voice auto route {faceVoice.AutoSelect}";
                }

                if (verb == "radius" && args.Count > 1 && float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var radius))
                {
                    faceVoice.AudibleRadius = radius;
                    return $"Face voice audible radius {faceVoice.AudibleRadius:0.0}";
                }

                if (verb == "select" && args.Count > 1)
                {
                    return faceVoice.Select(args[1])
                        ? $"Face voice selected {faceVoice.ActiveEndpoint.Id}"
                        : $"No Face voice endpoint named {args[1]}";
                }

                if (verb == "add" && args.Count >= 5 && TryParsePoint(args[3], args[4], out var anchor))
                {
                    var voice = args.Count > 5 ? args[5] : null;
                    var endpoint = faceVoice.AddOrUpdate(args[1], args[2], anchor, voice);
                    return $"Face voice endpoint {endpoint.Id} anchored at {endpoint.Anchor.X:0.0}, {endpoint.Anchor.Y:0.0}";
                }

                if (verb == "move" && args.Count > 2 && TryParsePoint(args[1], args[2], out var movedAnchor))
                {
                    faceVoice.ActiveEndpoint.Anchor = movedAnchor;
                    return $"Face voice endpoint {faceVoice.ActiveEndpoint.Id} moved to {movedAnchor.X:0.0}, {movedAnchor.Y:0.0}";
                }

                if (verb == "movehere")
                {
                    faceVoice.ActiveEndpoint.Anchor = faceVoice.ListenerWorld;
                    return $"Face voice endpoint {faceVoice.ActiveEndpoint.Id} moved to listener";
                }

                if (verb == "remove" && args.Count > 1)
                {
                    return faceVoice.Remove(args[1])
                        ? $"Face voice endpoint {args[1]} removed"
                        : $"Face voice endpoint {args[1]} was not removed";
                }

                return "facevoice start | startall | stop | stopall | say <text> | list | auto <true|false> | radius <meters> | select <id> | add <id> <thread> <x> <y> [voice] | move <x> <y> | movehere | remove <id>";
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
        faceVoice.UpdateListener(frameWithCursor.CursorWorld);
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
        faceVoice.Dispose();
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
            FaceVoiceThreadId = faceVoice.ActiveEndpoint.ThreadId,
            FaceVoiceVoice = faceVoice.ActiveEndpoint.Voice,
            FaceVoicePrompt = faceVoice.ActiveEndpoint.Prompt,
            FaceVoiceEndpoints = faceVoice.ExportState(),
            FaceVoiceAutoSelect = faceVoice.AutoSelect,
            FaceVoiceAudibleRadius = faceVoice.AudibleRadius
        });
    }

    private void SendFaceVoiceText()
    {
        SendFaceVoiceText(faceVoiceText);
        faceVoiceText = "";
    }

    private void SendFaceVoiceText(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return;
        }

        faceVoice.SendText(trimmed);
    }

    private void SetActiveFaceAnchorX(float value)
    {
        var anchor = faceVoice.ActiveEndpoint.Anchor;
        faceVoice.ActiveEndpoint.Anchor = new Vector2(value, anchor.Y);
    }

    private void SetActiveFaceAnchorY(float value)
    {
        var anchor = faceVoice.ActiveEndpoint.Anchor;
        faceVoice.ActiveEndpoint.Anchor = new Vector2(anchor.X, value);
    }

    private static bool TryParsePoint(string x, string y, out Vector2 point)
    {
        if (float.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedX)
            && float.TryParse(y, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedY))
        {
            point = new Vector2(parsedX, parsedY);
            return true;
        }

        point = default;
        return false;
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
