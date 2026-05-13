using System.Numerics;
using System.Text.Json;
using Aquarium.Engine;
using Aquarium.Engine.Input;
using Aquarium.Engine.Platform;
using Aquarium.Engine.Render;
using Aquarium.Epiphany;

var options = PreviewOptions.Parse(args);
var subject = PreviewSubject.Resolve(options.Subject);
var outputDirectory = Path.GetFullPath(options.OutputDirectory);
Directory.CreateDirectory(outputDirectory);

var shaderPath = Path.Combine(AppContext.BaseDirectory, "Render", "Shaders", "D3D12HeightField.hlsl");
if (!File.Exists(shaderPath))
{
    throw new FileNotFoundException("Preview render requires copied Aquarium shader assets.", shaderPath);
}

var input = new InputState();
using var window = Win32Window.Create(
    $"Aquarium {subject.DisplayName} Preview",
    options.Width,
    options.Height,
    input,
    visible: false);
using var renderer = new D3D12Renderer(
    window.Handle,
    options.Width,
    options.Height,
    shaderPath,
    CreatePreviewRenderPlan(),
    new GraphicsSettings(0, options.Exposure, options.BloomIntensity, options.BloomVeilIntensity));
renderer.DebugUiVisible = false;

var rendered = new List<RenderedView>();
foreach (var view in PreviewView.CreateSet(subject.Radius))
{
    RenderSettledView(renderer, window, subject, view, options);
    var path = Path.Combine(outputDirectory, $"{subject.Key}-{view.Key}.png");
    renderer.SaveFramePng(path);
    rendered.Add(new RenderedView(view.Key, path));
    Console.WriteLine(path);
}

var manifestPath = Path.Combine(outputDirectory, $"{subject.Key}-manifest.json");
File.WriteAllText(
    manifestPath,
    JsonSerializer.Serialize(
        new PreviewManifest(subject.Key, subject.DisplayName, options.Width, options.Height, options.FramesPerView, rendered),
        new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(manifestPath);

static void RenderSettledView(
    D3D12Renderer renderer,
    Win32Window window,
    PreviewSubject subject,
    PreviewView view,
    PreviewOptions options)
{
    for (var frameIndex = 0; frameIndex < options.FramesPerView || !renderer.HasPresentedReadyFrame; frameIndex++)
    {
        if (frameIndex > options.FramesPerView + options.PipelineWarmupFrameLimit)
        {
            throw new TimeoutException("Renderer did not produce a ready preview frame.");
        }

        window.PumpMessages();
        var frame = CreateFrame(subject, view, options.TimeSeconds + frameIndex / 60.0f);
        renderer.Render(frame, options.Width, options.Height);
        if (!renderer.HasPresentedReadyFrame)
        {
            Thread.Sleep(16);
        }
    }
}

static AquariumFrame CreateFrame(PreviewSubject subject, PreviewView view, float timeSeconds)
{
    var objects = Enumerable.Range(0, EpiphanyRenderPlan.SdfVisualCount)
        .Select(index => InactiveObject(index))
        .ToArray();
    var selected = new AquariumSdfObject(
        new Vector4(Vector3.Zero, subject.Radius),
        new Vector4(Vector3.Zero, 0.0f),
        subject.State);
    objects[subject.SdfIndex] = selected;

    var lights = new[]
    {
        new AquariumSdfLight(
            new Vector4(1.8f, -2.6f, 2.4f, 1.0f),
            new Vector4(5.8f, 5.1f, 4.2f, 0.0f)),
        new AquariumSdfLight(
            new Vector4(-2.4f, 1.2f, 1.4f, 1.0f),
            new Vector4(1.1f, 1.5f, 2.1f, 0.0f)),
    };
    var scene = new AquariumSceneState
    {
        SdfObjects = objects,
        SdfLights = lights,
    };
    return new AquariumFrame(new ViewFrame(Vector2.Zero, 20.0f), view.CameraPosition, timeSeconds, Vector2.Zero, scene);
}

static AquariumRenderPlan CreatePreviewRenderPlan()
{
    var plan = EpiphanyRenderPlan.Create();
    plan.Shaders.Scene("D3D12AgentPreviewScene.hlsl");
    return plan;
}

static AquariumSdfObject InactiveObject(int index)
{
    var center = new Vector3(10000.0f + index * 13.0f, 10000.0f, 10000.0f);
    return new AquariumSdfObject(new Vector4(center, 0.001f), new Vector4(center, 0.0f), Vector4.Zero);
}

sealed record PreviewOptions(
    string Subject,
    string OutputDirectory,
    int Width,
    int Height,
    int FramesPerView,
    int PipelineWarmupFrameLimit,
    float TimeSeconds,
    float Exposure,
    float BloomIntensity,
    float BloomVeilIntensity)
{
    public static PreviewOptions Parse(string[] args)
    {
        var subject = ValueAfter(args, "--agent") ?? ValueAfter(args, "--subject") ?? FirstPositional(args) ?? "Soul";
        var output = ValueAfter(args, "--output") ?? Path.Combine("artifacts", "agent-previews", subject, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        return new PreviewOptions(
            subject,
            output,
            IntValueAfter(args, "--width", 768),
            IntValueAfter(args, "--height", 768),
            IntValueAfter(args, "--frames", 18),
            IntValueAfter(args, "--warmup-limit", 3600),
            FloatValueAfter(args, "--time", 18.0f),
            FloatValueAfter(args, "--exposure", 0.24f),
            FloatValueAfter(args, "--bloom", 0.12f),
            FloatValueAfter(args, "--veil", 0.032f));
    }

    private static string? FirstPositional(IReadOnlyList<string> args)
    {
        return args.FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.Ordinal));
    }

    private static string? ValueAfter(IReadOnlyList<string> args, string name)
    {
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }

    private static int IntValueAfter(IReadOnlyList<string> args, string name, int fallback)
    {
        return int.TryParse(ValueAfter(args, name), out var value) ? Math.Max(1, value) : fallback;
    }

    private static float FloatValueAfter(IReadOnlyList<string> args, string name, float fallback)
    {
        return float.TryParse(ValueAfter(args, name), out var value) ? value : fallback;
    }
}

sealed record PreviewSubject(string Key, string DisplayName, int SdfIndex, float Radius, Vector4 State)
{
    private static readonly PreviewSubject[] All =
    [
        new("self", "Self", EpiphanyRenderPlan.SelfVisualIndex, 1.12f, new Vector4(1.0f, 1.0f, 0.0f, 0.0f)),
        new("face", "Face", EpiphanyRenderPlan.RoleAgentBaseIndex, 0.58f, new Vector4(0.8f, 0.45f, 0.35f, 0.0f)),
        new("imagination", "Imagination", EpiphanyRenderPlan.RoleAgentBaseIndex + 1, 0.70f, new Vector4(0.95f, 0.62f, 0.28f, 0.0f)),
        new("eyes", "Eyes", EpiphanyRenderPlan.RoleAgentBaseIndex + 2, 0.58f, new Vector4(0.72f, 0.58f, 0.24f, 0.0f)),
        new("body", "Body", EpiphanyRenderPlan.RoleAgentBaseIndex + 3, 0.64f, new Vector4(0.62f, 0.54f, 0.42f, 0.0f)),
        new("hands", "Hands", EpiphanyRenderPlan.RoleAgentBaseIndex + 4, 0.62f, new Vector4(0.88f, 0.64f, 0.36f, 0.0f)),
        new("soul", "Soul", EpiphanyRenderPlan.RoleAgentBaseIndex + 5, 0.92f, new Vector4(0.9f, 0.5f, 0.42f, 0.0f)),
        new("life", "Life", EpiphanyRenderPlan.RoleAgentBaseIndex + 6, 0.66f, new Vector4(0.66f, 0.68f, 0.22f, 0.0f)),
        new("cursor", "Cursor", EpiphanyRenderPlan.CursorVisualIndex, 0.88f, new Vector4(1.0f, 0.5f, 0.0f, 0.0f)),
    ];

    public static PreviewSubject Resolve(string value)
    {
        return All.FirstOrDefault(subject =>
            string.Equals(subject.Key, value, StringComparison.OrdinalIgnoreCase)
            || string.Equals(subject.DisplayName, value, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Unknown preview subject '{value}'. Known values: {string.Join(", ", All.Select(subject => subject.Key))}.");
    }
}

sealed record PreviewView(string Key, Vector3 CameraPosition)
{
    public static IReadOnlyList<PreviewView> CreateSet(float radius)
    {
        var distance = MathF.Max(radius * 2.35f, 1.75f);
        return
        [
            new("front", new Vector3(0.0f, -distance, distance * 0.36f)),
            new("front-high", new Vector3(0.0f, -distance * 0.82f, distance * 0.86f)),
            new("right", new Vector3(distance, 0.0f, distance * 0.36f)),
            new("left", new Vector3(-distance, 0.0f, distance * 0.36f)),
            new("three-quarter", new Vector3(distance * 0.72f, -distance * 0.72f, distance * 0.52f)),
            new("top-oblique", new Vector3(distance * 0.22f, -distance * 0.32f, distance * 1.16f)),
        ];
    }
}

sealed record RenderedView(string View, string Path);

sealed record PreviewManifest(string Subject, string DisplayName, int Width, int Height, int FramesPerView, IReadOnlyList<RenderedView> Views);
