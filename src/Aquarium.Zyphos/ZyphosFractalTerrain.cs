using Aquarium.Engine.Fractal;
using Aquarium.Engine.Fractal.Brushes;
using Aquarium.Engine.Fractal.Debug;
using Aquarium.Engine.Fractal.Grammar;
using Aquarium.Engine.Fractal.Lod;
using Aquarium.Engine.Render;

namespace Aquarium.Zyphos;

public static class ZyphosFractalTerrain
{
    public const string Grammar = """
        # Zyphos first Aquarium fractal terrain DSL proof.
        tile PositiveZ 0 0 0 zyphos/first-fractal-terrain
        height basin 0 0 30 30 0 3 1 -0.12 7 basin
        ifs crater 3 2 -5 3 6 2.5 0.48 2.6 0.42 4 0.85 -0.10 19 crater
        ifs ridge 2 3 6 -4 5 1.35 0.45 3.2 -0.27 3.6 0.72 0.075 31 ridge
        """;

    private static readonly Lazy<FractalOwnershipTree> Tree = new(() => FractalDslCompiler.Compile(Grammar));
    private static readonly Lazy<AquariumFractalSummary[]> Summaries = new(() => FractalSummaryBuilder.Build(Tree.Value));
    private static readonly Lazy<AquariumSelectedCut[]> SelectedCut = new(() => FractalSelectedCutBuilder.Build(Summaries.Value, _ => 8.0f, maxEstimatedCost: 8.0f));
    private static readonly Lazy<AquariumHeightFieldBrush[]> Brushes = new(() => FractalHeightBrushCompiler.CompileMany(Tree.Value.Claims));

    public static FractalOwnershipTree OwnershipTree => Tree.Value;

    public static AquariumHeightFieldBrush[] HeightBrushes => Brushes.Value;

    public static string DebugDump => FractalDebugDump.Build(OwnershipTree, Summaries.Value, SelectedCut.Value);

    public static string Summary =>
        $"{OwnershipTree.Claims.Count} DSL claims / {OwnershipTree.Nodes.Count} node / {SelectedCut.Value.Length} cuts / {HeightBrushes.Length} shaped brushes";
}
