using Aquarium.Engine.Fractal.Grammar;

namespace Aquarium.Engine.Fractal.Tests;

public sealed class FractalDslCompilerTests
{
    [Fact]
    public void DslCompilesHeightClaimsIntoSemanticTree()
    {
        const string source = """
            tile PositiveZ 0 0 0 zyphos/terrain
            height basin 0 0 30 30 0 3 1 -0.18 7 basin
            height ridge 3 -2 4 1.5 0.4 4 0.8 0.06 11 ridge
            """;

        var tree = FractalDslCompiler.Compile(source);

        Assert.Equal("cube/PositiveZ/L00/0/0:zyphos/terrain", tree.Domain.Key.Value);
        Assert.Equal(2, tree.Claims.Count);
        Assert.Equal("cube/PositiveZ/L00/0/0:zyphos/terrain/root/claim/0001/ridge", tree.Claims[1].Key.Value);
        Assert.Equal(0.06f, tree.Claims[1].Amplitude);
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
