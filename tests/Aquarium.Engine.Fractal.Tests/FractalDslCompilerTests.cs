using Aquarium.Engine.Fractal.Grammar;

namespace Aquarium.Engine.Fractal.Tests;

public sealed class FractalDslCompilerTests
{
    [Fact]
    public void DslCompilesHeightClaimsIntoSemanticTree()
    {
        const string source = """
            domain Solar demo/star - 0.25 0.006 0.077 16
            domain Orbital demo/orbit demo/star 8 1 12.9 52
            domain Planetary demo/planet demo/orbit 1 0.56 0 0
            domain LatLong demo/latlong demo/planet -90 90 -180 180
            tile PositiveZ 0 0 0 zyphos/terrain demo/latlong
            height basin 0 0 30 30 0 3 1 -0.18 7 basin
            height ridge 3 -2 4 1.5 0.4 4 0.8 0.06 11 ridge
            """;

        var tree = FractalDslCompiler.Compile(source);

        Assert.Equal("cube/PositiveZ/L00/0/0:zyphos/terrain", tree.Domain.Key.Value);
        Assert.Equal(new AquariumFractalKey("demo/latlong"), tree.Domain.ParentKey);
        Assert.Equal(5, tree.Domains.Count);
        Assert.Contains(tree.Domains, domain => domain.Kind == AquariumFractalDomainKind.Solar && domain.Key.Value == "demo/star");
        Assert.Contains(tree.Domains, domain => domain.Kind == AquariumFractalDomainKind.LatLong && domain.ParentKey.Value == "demo/planet");
        Assert.Equal(2, tree.Claims.Count);
        Assert.Equal("cube/PositiveZ/L00/0/0:zyphos/terrain/root/claim/0001/ridge", tree.Claims[1].Key.Value);
        Assert.Equal(0.06f, tree.Claims[1].Amplitude);
    }

    [Fact]
    public void LegacyDslTileStillCompilesWithoutExplicitDomainStack()
    {
        const string source = """
            tile PositiveZ 0 0 0 zyphos/terrain
            height basin 0 0 30 30 0 3 1 -0.18 7 basin
            """;

        var tree = FractalDslCompiler.Compile(source);

        Assert.Equal("cube/PositiveZ/L00/0/0:zyphos/terrain", tree.Domain.Key.Value);
        Assert.Single(tree.Domains);
        Assert.Single(tree.Claims);
    }

    [Fact]
    public void DslIfsEmitsDeterministicRecursiveClaims()
    {
        const string source = """
            tile PositiveZ 0 0 0 zyphos/terrain
            ifs crater 3 2 0 0 8 3 0.5 2.4 0.33 4 0.9 -0.12 19 crater
            """;

        var first = FractalDslCompiler.Compile(source);
        var second = FractalDslCompiler.Compile(source);

        Assert.Equal(7, first.Claims.Count);
        Assert.Equal(first.Claims, second.Claims);
        Assert.All(first.Claims, claim => Assert.Equal("crater", claim.Tags));
    }

    [Fact]
    public void DslRejectsClaimsBeforeTile()
    {
        const string source = "height orphan 0 0 1 1 0 3 1 1 0 tag";

        Assert.Throws<FormatException>(() => FractalDslCompiler.Compile(source));
    }
}
