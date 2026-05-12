using System.Numerics;

namespace Aquarium.Engine.Render;

public sealed class AquariumApp
{
    public AquariumApp(AquariumRenderPlan? plan = null)
    {
        Plan = plan ?? new AquariumRenderPlan();
        RenderTargets = new AquariumRenderTargetBuilder(Plan.Graph);
        Cameras = new AquariumCameraBuilder(Plan.Graph);
        Shaders = new AquariumShaderBuilder(Plan.Shaders);
        Graph = new AquariumGraphBuilder(Plan.Graph);
        Features = new AquariumFeatureBuilder(Plan.Graph);
        Debug = new AquariumDebugBuilder(Plan.Graph);
    }

    public AquariumRenderPlan Plan { get; }

    public AquariumRenderTargetBuilder RenderTargets { get; }

    public AquariumCameraBuilder Cameras { get; }

    public AquariumShaderBuilder Shaders { get; }

    public AquariumGraphBuilder Graph { get; }

    public AquariumFeatureBuilder Features { get; }

    public AquariumDebugBuilder Debug { get; }
}

public sealed class AquariumRenderTargetBuilder(AquariumRenderGraphDescription graph)
{
    public AquariumRenderTargetDraft Create(string name)
    {
        return new AquariumRenderTargetDraft(graph, name);
    }

    public AquariumRenderTargetSet Hdr(string name)
    {
        var color = graph.RenderTarget(name, RenderFormat.Rgba16Float, AquariumTargetSize.MatchWindow(), historyFrames: 2);
        var metadata = graph.RenderTarget($"{name}-metadata", RenderFormat.Rgba16Float, AquariumTargetSize.MatchWindow(), historyFrames: 2);
        var control = graph.RenderTarget($"{name}-control", RenderFormat.Rgba16Float, AquariumTargetSize.MatchWindow(), historyFrames: 2);
        return new AquariumRenderTargetSet(color, metadata, control, new DepthTargetHandle($"{name}-depth"));
    }
}

public sealed class AquariumRenderTargetDraft(AquariumRenderGraphDescription graph, string name)
{
    private RenderFormat format = RenderFormat.Rgba16Float;
    private AquariumTargetSize size = AquariumTargetSize.MatchWindow();
    private bool sampled = true;
    private int historyFrames;

    public AquariumRenderTargetDraft FixedSize(int width, int height)
    {
        size = AquariumTargetSize.Fixed(width, height);
        return this;
    }

    public AquariumRenderTargetDraft MatchWindow(float scale = 1.0f)
    {
        size = AquariumTargetSize.MatchWindow(scale);
        return this;
    }

    public AquariumRenderTargetDraft Format(RenderFormat value)
    {
        format = value;
        return this;
    }

    public AquariumRenderTargetDraft Sampled(bool value = true)
    {
        sampled = value;
        return this;
    }

    public AquariumRenderTargetDraft History(int frames)
    {
        historyFrames = Math.Max(0, frames);
        return this;
    }

    public AquariumRenderTargetDraft Clear(float value)
    {
        return this;
    }

    public AquariumRenderTargetDraft Clear(Vector4 value)
    {
        return this;
    }

    public RenderTargetHandle Register()
    {
        return graph.RenderTarget(name, format, size, sampled, historyFrames);
    }
}

public readonly record struct AquariumRenderTargetSet(
    RenderTargetHandle Color,
    RenderTargetHandle Metadata,
    RenderTargetHandle Control,
    DepthTargetHandle Depth);

public sealed class AquariumCameraBuilder(AquariumRenderGraphDescription graph)
{
    public CameraHandle Perspective(string name)
    {
        return graph.Camera(name);
    }

    public CameraHandle Orbit(string name)
    {
        return graph.Camera(name);
    }
}

public sealed class AquariumShaderBuilder(AquariumShaderManifest manifest)
{
    public AquariumShaderBuilder Root(string path)
    {
        manifest.Root(path);
        return this;
    }

    public AquariumShaderBuilder Core(string grid, string scene, string post)
    {
        manifest.Grid(grid).Scene(scene).Post(post);
        return this;
    }

    public AquariumShaderBuilder Include(string path)
    {
        manifest.Include(path);
        return this;
    }

    public AquariumShaderBuilder BodyShader(string path)
    {
        manifest.BodyShader(path);
        return this;
    }

    public AquariumShaderBuilder BodyLibrary(string path)
    {
        manifest.BodyLibrary(path);
        return this;
    }
}

public sealed class AquariumGraphBuilder(AquariumRenderGraphDescription graph)
{
    public AquariumPassDraft Pass(string name)
    {
        return new AquariumPassDraft(graph, name);
    }
}

public sealed class AquariumPassDraft(AquariumRenderGraphDescription graph, string name)
{
    public PassHandle Fullscreen()
    {
        return graph.Pass(name, AquariumPassKind.Fullscreen);
    }

    public PassHandle Proxy()
    {
        return graph.Pass(name, AquariumPassKind.Proxy);
    }

    public PassHandle Present()
    {
        return graph.Pass(name, AquariumPassKind.Present);
    }

    public PassHandle Overlay()
    {
        return graph.Pass(name, AquariumPassKind.Overlay);
    }
}

public sealed class AquariumFeatureBuilder(AquariumRenderGraphDescription graph)
{
    public PassHandle Bloom(RenderTargetHandle source)
    {
        return graph.Pass($"bloom:{source.Name}", AquariumPassKind.Fullscreen);
    }

    public PassHandle Presentation(RenderTargetHandle source)
    {
        return graph.Pass($"present:{source.Name}", AquariumPassKind.Present);
    }

    public PassHandle DirectWriteOverlay()
    {
        return graph.Pass("directwrite-overlay", AquariumPassKind.Overlay);
    }
}

public sealed class AquariumDebugBuilder(AquariumRenderGraphDescription graph)
{
    public AquariumDebugBuilder View(string name, RenderTargetHandle target)
    {
        graph.DebugView(name, target);
        return this;
    }
}
