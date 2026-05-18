using System.Numerics;
using Aquarium.Engine.Fractal;
using Aquarium.Engine.Fractal.Grammar;

namespace Aquarium.Engine.Fractal.Tests;

public sealed class FractalGrammarContractTests
{
    [Fact]
    public void CubeTileStableKeyIncludesTileAddressAndNormalizedGrammarPath()
    {
        var tile = new CubeTileKey(CubeFace.PositiveZ, 3, 2, 5);

        var key = FractalStableKeyBuilder.ForCubeTile(tile, @"terrain\ridge/root");

        Assert.Equal("cube/PositiveZ/L03/2/5:terrain/ridge/root", key.Value);
    }

    [Fact]
    public void FlatUnionTreePreservesClaimsAndConservativeBounds()
    {
        var domainKey = new AquariumFractalKey("domain/planet");
        var rootKey = new AquariumFractalKey("domain/planet/root");
        var domain = new AquariumFractalDomain(domainKey, AquariumFractalDomainKind.CubeSphereTile, default, Vector4.Zero, Vector4.Zero);
        var firstClaim = Claim("claim/a", domainKey, rootKey, new Vector2(1.0f, 2.0f), new Vector2(2.0f, 1.0f));
        var secondClaim = Claim("claim/b", domainKey, rootKey, new Vector2(-3.0f, 0.0f), new Vector2(0.5f, 1.5f));

        var tree = FractalOwnershipTreeBuilder.BuildFlatUnion(domain, rootKey, [firstClaim, secondClaim]);

        Assert.Equal(domain, tree.Domain);
        Assert.Equal(2, tree.Claims.Count);
        Assert.Single(tree.Nodes);
        Assert.Equal(AquariumFractalOperation.Union, tree.Nodes[0].Operation);
        Assert.Equal(2, tree.Nodes[0].ClaimCount);
        Assert.Equal(new Vector4(-4.5f, -1.5f, 3.0f, 4.0f), tree.Nodes[0].BoundsMinMax);
    }

    private static AquariumBrushClaim Claim(
        string key,
        AquariumFractalKey domainKey,
        AquariumFractalKey nodeKey,
        Vector2 center,
        Vector2 radii)
    {
        return new AquariumBrushClaim(
            new AquariumFractalKey(key),
            domainKey,
            nodeKey,
            AquariumFractalPayloadKind.Height,
            center,
            radii,
            RotationRadians: 0.0f,
            Falloff: 4.0f,
            ShapePower: 1.0f,
            Amplitude: 1.0f,
            Seed: 17,
            Tags: "test");
    }
}
