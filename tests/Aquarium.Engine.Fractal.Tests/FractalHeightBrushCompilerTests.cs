using System.Numerics;
using Aquarium.Engine.Fractal;
using Aquarium.Engine.Fractal.Brushes;
using Aquarium.Engine.Fractal.Grammar;
using Aquarium.Engine.Fractal.Lod;

namespace Aquarium.Engine.Fractal.Tests;

public sealed class FractalHeightBrushCompilerTests
{
    [Fact]
    public void HeightClaimCompilesToShapedHeightFieldBrush()
    {
        var claim = new AquariumBrushClaim(
            new AquariumFractalKey("claim/ridge"),
            new AquariumFractalKey("domain/tile"),
            new AquariumFractalKey("node/root"),
            AquariumFractalPayloadKind.Height,
            new Vector2(1.0f, 2.0f),
            new Vector2(5.0f, 2.5f),
            RotationRadians: 0.4f,
            Falloff: 3.5f,
            ShapePower: 0.7f,
            Amplitude: -1.2f,
            Seed: 11,
            Tags: "ridge");

        var brush = FractalHeightBrushCompiler.Compile(claim);

        Assert.Equal(claim.Center, brush.Center);
        Assert.Equal(5.0f, brush.Radius);
        Assert.Equal(2.5f, brush.RadiusY);
        Assert.Equal(0.4f, brush.RotationRadians);
        Assert.Equal(3.5f, brush.EnvelopeFalloff);
        Assert.Equal(0.7f, brush.Power);
        Assert.Equal(-1.2f, brush.Amplitude);
        Assert.Equal(0.0f, brush.WaveAmplitude);
    }

    [Fact]
    public void NonHeightClaimDoesNotCompileToHeightFieldBrush()
    {
        var claim = new AquariumBrushClaim(
            new AquariumFractalKey("claim/material"),
            new AquariumFractalKey("domain/tile"),
            new AquariumFractalKey("node/root"),
            AquariumFractalPayloadKind.Material,
            Vector2.Zero,
            Vector2.One,
            RotationRadians: 0.0f,
            Falloff: 2.0f,
            ShapePower: 1.0f,
            Amplitude: 1.0f,
            Seed: 0,
            Tags: "material");

        Assert.Throws<ArgumentException>(() => FractalHeightBrushCompiler.Compile(claim));
    }

    [Fact]
    public void TreeCompilePreservesCubeTileDomainMetadata()
    {
        var domain = new AquariumFractalDomain(
            new AquariumFractalKey("cube/NegativeX/L02/2/1:zyphos/leaf"),
            AquariumFractalDomainKind.CubeSphereTile,
            default,
            new Vector4((float)CubeFace.NegativeX, 2.0f, 2.0f, 1.0f),
            Vector4.Zero);
        var rootKey = new AquariumFractalKey("cube/NegativeX/L02/2/1:zyphos/leaf/root");
        var claim = new AquariumBrushClaim(
            new AquariumFractalKey("claim/leaf"),
            domain.Key,
            rootKey,
            AquariumFractalPayloadKind.Height,
            Vector2.Zero,
            Vector2.One,
            RotationRadians: 0.0f,
            Falloff: 3.0f,
            ShapePower: 1.0f,
            Amplitude: 0.25f,
            Seed: 3,
            Tags: "leaf");
        var tree = FractalOwnershipTreeBuilder.BuildFlatUnion(domain, rootKey, [claim]);

        var brush = Assert.Single(FractalHeightBrushCompiler.CompileTree(tree));

        Assert.Equal((float)CubeFace.NegativeX, brush.DomainFace);
        Assert.Equal(2.0f, brush.DomainLevel);
        Assert.Equal(2.0f, brush.DomainX);
        Assert.Equal(1.0f, brush.DomainY);
    }

    [Fact]
    public void SelectedTreeCompileOnlyLowersSelectedNodes()
    {
        const string source = """
            tile PositiveZ 0 0 0 demo/a
            height basin 0 0 30 30 0 3 1 -0.18 7 basin
            tile PositiveX 0 0 0 demo/b
            height ridge 1 1 4 2 0 3 1 0.08 11 ridge
            """;
        var tree = FractalDslCompiler.Compile(source);
        var selectedNode = Assert.Single(tree.Nodes, node => node.DomainKey.Value.EndsWith(":demo/b", StringComparison.Ordinal));
        var selectedCut = new[]
        {
            new AquariumSelectedCut(selectedNode.Key, Score: 1.0f, Fade: 1.0f, UsesSummary: true, RequestedChildren: false),
        };

        var brush = Assert.Single(FractalHeightBrushCompiler.CompileSelectedTree(tree, selectedCut));

        Assert.Equal(1.0f, brush.Center.X);
        Assert.Equal((float)CubeFace.PositiveX, brush.DomainFace);
    }
}
