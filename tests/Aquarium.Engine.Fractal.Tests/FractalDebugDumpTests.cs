using System.Numerics;
using Aquarium.Engine.Fractal;
using Aquarium.Engine.Fractal.Debug;
using Aquarium.Engine.Fractal.Grammar;
using Aquarium.Engine.Fractal.Lod;

namespace Aquarium.Engine.Fractal.Tests;

public sealed class FractalDebugDumpTests
{
    [Fact]
    public void DebugDumpNamesDomainClaimsSummariesAndCuts()
    {
        const string source = """
            tile PositiveZ 0 0 0 debug/world
            height ridge 1 2 3 1 0.4 4 0.8 0.2 9 ridge
            """;
        var tree = FractalDslCompiler.Compile(source);
        var summaries = FractalSummaryBuilder.Build(tree);
        var cut = FractalSelectedCutBuilder.Build(summaries, _ => 10.0f, maxEstimatedCost: 1.0f);

        var dump = FractalDebugDump.Build(tree, summaries, cut);

        Assert.Contains("domain cube/PositiveZ/L00/0/0:debug/world", dump);
        Assert.Contains("claim cube/PositiveZ/L00/0/0:debug/world/root/claim/0000/ridge", dump);
        Assert.Contains("summary cube/PositiveZ/L00/0/0:debug/world/root", dump);
        Assert.Contains("cut cube/PositiveZ/L00/0/0:debug/world/root", dump);
    }
}
