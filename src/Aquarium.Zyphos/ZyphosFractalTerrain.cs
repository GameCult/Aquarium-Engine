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
    private static readonly Lazy<AquariumSelectedCut[]> SelectedCut = new(() => FractalSelectedCutBuilder.Build(Summaries.Value, _ => 8.0f, maxEstimatedCost: 8.0f));
    private static readonly Lazy<AquariumHeightFieldBrush[]> Brushes = new(() => FractalHeightBrushCompiler.CompileTree(Tree.Value));

    public static string PatchPath => Path.Combine(AppContext.BaseDirectory, PatchRelativePath);

    public static FractalOwnershipTree OwnershipTree => Tree.Value;

    public static AquariumHeightFieldBrush[] HeightBrushes => Brushes.Value;

    public static AquariumFractalSummary[] NodeSummaries => Summaries.Value;

    public static AquariumSelectedCut[] SelectedCuts => SelectedCut.Value;

    public static string DebugDump => FractalDebugDump.Build(OwnershipTree, Summaries.Value, SelectedCut.Value);

    public static string Summary =>
        $"{Path.GetFileName(PatchPath)} / {OwnershipTree.Claims.Count} DSL claims / {OwnershipTree.Nodes.Count} tile nodes / {SelectedCut.Value.Length} cuts / {HeightBrushes.Length} shaped brushes";
}
