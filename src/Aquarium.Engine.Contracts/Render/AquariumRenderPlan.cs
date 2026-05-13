using System.Numerics;
using System.Runtime.InteropServices;

namespace Aquarium.Engine.Render;

public sealed class AquariumRenderPlan
{
    public AquariumRenderPlan()
    {
        Shaders = new AquariumShaderManifest();
        Graph = new AquariumRenderGraphDescription();
    }

    public AquariumShaderManifest Shaders { get; }

    public AquariumRenderGraphDescription Graph { get; }
}

public sealed class AquariumShaderManifest
{
    private readonly List<string> sdfShaderPaths = [];
    private readonly List<string> includePaths = [];

    public string? ShaderRoot { get; private set; }

    public string HeightFieldShader { get; private set; } = "D3D12HeightField.hlsl";

    public string SceneShader { get; private set; } = "D3D12Scene.hlsl";

    public string PostShader { get; private set; } = "D3D12Post.hlsl";

    public string SdfCommonInclude { get; private set; } = "D3D12SdfCommon.hlsli";

    public string SdfProxyInclude { get; private set; } = "D3D12SdfProxy.hlsli";

    public string SdfMathInclude { get; private set; } = "D3D12SdfMath.hlsli";

    public string? SdfLibraryInclude { get; private set; }

    public IReadOnlyList<string> SdfShaderPaths => sdfShaderPaths;

    public IReadOnlyList<string> IncludePaths => includePaths;

    public AquariumShaderManifest Root(string path)
    {
        ShaderRoot = path;
        return this;
    }

    public AquariumShaderManifest HeightField(string path)
    {
        HeightFieldShader = path;
        return this;
    }

    public AquariumShaderManifest Scene(string path)
    {
        SceneShader = path;
        return this;
    }

    public AquariumShaderManifest Post(string path)
    {
        PostShader = path;
        return this;
    }

    public AquariumShaderManifest SdfCommon(string path)
    {
        SdfCommonInclude = path;
        return this;
    }

    public AquariumShaderManifest SdfProxy(string path)
    {
        SdfProxyInclude = path;
        return this;
    }

    public AquariumShaderManifest SdfMath(string path)
    {
        SdfMathInclude = path;
        return this;
    }

    public AquariumShaderManifest SdfLibrary(string? path)
    {
        SdfLibraryInclude = path;
        return this;
    }

    public AquariumShaderManifest Include(string path)
    {
        includePaths.Add(path);
        return this;
    }

    public AquariumShaderManifest SdfShader(string path)
    {
        sdfShaderPaths.Add(path);
        return this;
    }
}

public sealed class AquariumRenderGraphDescription
{
    private readonly List<AquariumRenderTargetDescription> renderTargets = [];
    private readonly List<AquariumCameraDescription> cameras = [];
    private readonly List<AquariumPassDescription> passes = [];
    private readonly List<AquariumDebugViewDescription> debugViews = [];

    public IReadOnlyList<AquariumRenderTargetDescription> RenderTargets => renderTargets;

    public IReadOnlyList<AquariumCameraDescription> Cameras => cameras;

    public IReadOnlyList<AquariumPassDescription> Passes => passes;

    public IReadOnlyList<AquariumDebugViewDescription> DebugViews => debugViews;

    public RenderTargetHandle RenderTarget(string name, RenderFormat format, AquariumTargetSize size, bool sampled = true, int historyFrames = 0)
    {
        var handle = new RenderTargetHandle(name);
        renderTargets.Add(new AquariumRenderTargetDescription(handle, format, size, sampled, historyFrames));
        return handle;
    }

    public CameraHandle Camera(string name)
    {
        var handle = new CameraHandle(name);
        cameras.Add(new AquariumCameraDescription(handle));
        return handle;
    }

    public PassHandle Pass(string name, AquariumPassKind kind)
    {
        var handle = new PassHandle(name);
        passes.Add(new AquariumPassDescription(handle, kind));
        return handle;
    }

    public AquariumRenderGraphDescription DebugView(string name, RenderTargetHandle target)
    {
        debugViews.Add(new AquariumDebugViewDescription(name, target));
        return this;
    }
}

public readonly record struct RenderTargetHandle(string Name);

public readonly record struct DepthTargetHandle(string Name);

public readonly record struct CameraHandle(string Name);

public readonly record struct ShaderHandle(string Name);

public readonly record struct BufferHandle<T>(string Name) where T : unmanaged;

public readonly record struct TextureHandle(string Name);

public readonly record struct PassHandle(string Name);

public readonly record struct AquariumRenderTargetDescription(
    RenderTargetHandle Handle,
    RenderFormat Format,
    AquariumTargetSize Size,
    bool Sampled,
    int HistoryFrames);

public readonly record struct AquariumCameraDescription(CameraHandle Handle);

public readonly record struct AquariumPassDescription(PassHandle Handle, AquariumPassKind Kind);

public readonly record struct AquariumDebugViewDescription(string Name, RenderTargetHandle Target);

public readonly record struct AquariumTargetSize(AquariumTargetSizeKind Kind, int Width, int Height, float Scale)
{
    public static AquariumTargetSize Fixed(int width, int height) => new(AquariumTargetSizeKind.Fixed, width, height, 1.0f);

    public static AquariumTargetSize MatchWindow(float scale = 1.0f) => new(AquariumTargetSizeKind.MatchWindow, 0, 0, scale);
}

public enum AquariumTargetSizeKind
{
    Fixed,
    MatchWindow,
}

public enum RenderFormat
{
    Unknown,
    R16Float,
    Rgba16Float,
    Bgra8Unorm,
    Depth32Float,
}

public enum AquariumPassKind
{
    Fullscreen,
    Proxy,
    Instanced,
    Compute,
    Copy,
    Present,
    Overlay,
}

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AquariumSdfLight(
    Vector4 CenterRadius,
    Vector4 RadianceFieldId);

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AquariumSdfObject(
    Vector4 CenterRadius,
    Vector4 PreviousCenterPad,
    Vector4 State);

public readonly record struct AquariumHeightFieldBrush(
    Vector2 Center,
    float Radius,
    float Power,
    float Amplitude,
    float WaveAmplitude,
    float WaveFrequency,
    float WaveSpeed,
    float WaveSinePower);

public sealed class AquariumSceneState
{
    public static AquariumSceneState Empty { get; } = new();

    public IReadOnlyList<AquariumHeightFieldBrush> HeightFieldBrushes { get; init; } = [];

    public IReadOnlyList<AquariumSdfObject> SdfObjects { get; init; } = [];

    public IReadOnlyList<AquariumSdfLight> SdfLights { get; init; } = [];
}
