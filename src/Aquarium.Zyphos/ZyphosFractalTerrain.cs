using Aquarium.Engine.Fractal;
using Aquarium.Engine.Fractal.Brushes;
using Aquarium.Engine.Fractal.Debug;
using Aquarium.Engine.Fractal.Grammar;
using Aquarium.Engine.Fractal.Lod;
using Aquarium.Engine.Render;

namespace Aquarium.Zyphos;

public static class ZyphosFractalTerrain
{
    private const string PatchRelativePath = "Worlds/zyphos-first-fractal-terrain.aquageo";

    private static readonly Lazy<string> PatchSource = new(() => File.ReadAllText(PatchPath));
    private static readonly Lazy<FractalOwnershipTree> Tree = new(() => FractalDslCompiler.Compile(PatchSource.Value));
    private static readonly Lazy<AquariumFractalSummary[]> Summaries = new(() => FractalSummaryBuilder.Build(Tree.Value));
    private static readonly Lazy<AquariumSelectedCut[]> SelectedCut = new(() => FractalSelectedCutBuilder.Build(Summaries.Value, _ => 8.0f, maxEstimatedCost: 64.0f));
    private static readonly Lazy<AquariumHeightFieldBrush[]> Brushes = new(() => FractalHeightBrushCompiler.CompileSelectedTree(Tree.Value, SelectedCut.Value));
    private static readonly object PlanCacheLock = new();
    private static readonly Dictionary<int, ZyphosFractalRenderPlan> PlanCache = [];

    public static string PatchPath => Path.Combine(AppContext.BaseDirectory, PatchRelativePath);

    public static FractalOwnershipTree OwnershipTree => Tree.Value;

    public static AquariumHeightFieldBrush[] HeightBrushes => Brushes.Value;

    public static AquariumFractalSummary[] NodeSummaries => Summaries.Value;

    public static AquariumSelectedCut[] SelectedCuts => SelectedCut.Value;

    public static string DebugDump => FractalDebugDump.Build(OwnershipTree, Summaries.Value, SelectedCut.Value);

    public static ZyphosFractalRenderPlan BuildRenderPlan(ZyphosCameraShot shot)
    {
        var pixelsPerWorld = Math.Clamp(72.0f / MathF.Max(shot.EffectiveDistance, 0.001f), 0.25f, 48.0f);
        var bucket = (int)Math.Clamp(MathF.Round(pixelsPerWorld * 16.0f), 4.0f, 768.0f);
        lock (PlanCacheLock)
        {
            if (PlanCache.TryGetValue(bucket, out var cached))
            {
                return cached;
            }
        }

        var bucketPixelsPerWorld = bucket / 16.0f;
        var selectedCut = FractalSelectedCutBuilder.Build(Summaries.Value, _ => bucketPixelsPerWorld, maxEstimatedCost: 64.0f);
        var brushes = FractalHeightBrushCompiler.CompileSelectedTree(Tree.Value, selectedCut);
        var plan = new ZyphosFractalRenderPlan(
            brushes,
            selectedCut,
            bucketPixelsPerWorld,
            $"{selectedCut.Length}/{Summaries.Value.Length} cuts / {brushes.Length}/{OwnershipTree.Claims.Count} brushes / {bucketPixelsPerWorld:0.00} px-wu");

        lock (PlanCacheLock)
        {
            PlanCache[bucket] = plan;
        }

        return plan;
    }

    public static string Summary =>
        $"{Path.GetFileName(PatchPath)} / {OwnershipTree.Claims.Count} DSL claims / {OwnershipTree.Nodes.Count} tile nodes / {SelectedCut.Value.Length} cuts / {HeightBrushes.Length} shaped brushes";
}

public readonly record struct ZyphosFractalRenderPlan(
    AquariumHeightFieldBrush[] HeightBrushes,
    AquariumSelectedCut[] SelectedCuts,
    float PixelsPerWorld,
    string Summary);
