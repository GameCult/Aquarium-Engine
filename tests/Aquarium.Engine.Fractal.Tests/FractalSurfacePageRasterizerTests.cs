using System.Numerics;
using Aquarium.Engine.Fractal;
using Aquarium.Engine.Fractal.Grammar;
using Aquarium.Engine.Fractal.Lod;

namespace Aquarium.Engine.Fractal.Tests;

public sealed class FractalSurfacePageRasterizerTests
{
    [Fact]
    public void RasterizerLowersHeightPageFromShapedBrushClaims()
    {
        var domainKey = new AquariumFractalKey("domain/tile");
        var rootKey = new AquariumFractalKey("domain/tile/root");
        var tree = Tree(domainKey, rootKey, Claim("claim/a", domainKey, rootKey, amplitude: 2.0f));
        var page = Page(domainKey, rootKey, AquariumFractalSurfacePageKind.Height, width: 3, height: 3);

        var payload = FractalSurfacePageRasterizer.Rasterize(tree, page);

        Assert.Equal(0.0f, payload.Samples[0]);
        Assert.Equal(2.0f, payload.Samples[4], 5);
    }

    [Fact]
    public void RasterizerLowersSignedDistancePageFromBrushSupport()
    {
        var domainKey = new AquariumFractalKey("domain/tile");
        var rootKey = new AquariumFractalKey("domain/tile/root");
        var tree = Tree(domainKey, rootKey, Claim("claim/a", domainKey, rootKey, amplitude: 1.0f));
        var page = Page(domainKey, rootKey, AquariumFractalSurfacePageKind.SignedDistance2D, width: 3, height: 3);

        var payload = FractalSurfacePageRasterizer.Rasterize(tree, page);

        Assert.True(payload.Samples[4] < 0.0f);
        Assert.Equal(0.0f, payload.Samples[1], 5);
    }

    [Fact]
    public void RasterizerLowersConfidencePageFromEnvelopeCoverage()
    {
        var domainKey = new AquariumFractalKey("domain/tile");
        var rootKey = new AquariumFractalKey("domain/tile/root");
        var tree = Tree(domainKey, rootKey, Claim("claim/a", domainKey, rootKey, amplitude: 1.0f));
        var page = Page(domainKey, rootKey, AquariumFractalSurfacePageKind.Confidence, width: 3, height: 3);

        var payload = FractalSurfacePageRasterizer.Rasterize(tree, page);

        Assert.Equal(1.0f, payload.Samples[4], 5);
        Assert.Equal(0.0f, payload.Samples[0]);
    }

    [Fact]
    public void RasterizerRejectsUnknownPageNode()
    {
        var domainKey = new AquariumFractalKey("domain/tile");
        var rootKey = new AquariumFractalKey("domain/tile/root");
        var tree = Tree(domainKey, rootKey, Claim("claim/a", domainKey, rootKey, amplitude: 1.0f));
        var page = Page(domainKey, new AquariumFractalKey("domain/tile/missing"), AquariumFractalSurfacePageKind.Height, width: 3, height: 3);

        Assert.Throws<ArgumentException>(() => FractalSurfacePageRasterizer.Rasterize(tree, page));
    }

    private static FractalOwnershipTree Tree(AquariumFractalKey domainKey, AquariumFractalKey rootKey, AquariumBrushClaim claim)
    {
        var domain = new AquariumFractalDomain(domainKey, AquariumFractalDomainKind.CubeSphereTile, default, Vector4.Zero, Vector4.Zero);
        return FractalOwnershipTreeBuilder.BuildFlatUnion(domain, rootKey, [claim]);
    }

    private static AquariumBrushClaim Claim(string key, AquariumFractalKey domainKey, AquariumFractalKey nodeKey, float amplitude)
    {
        return new AquariumBrushClaim(
            new AquariumFractalKey(key),
            domainKey,
            nodeKey,
            AquariumFractalPayloadKind.Height,
            Vector2.Zero,
            Vector2.One,
            RotationRadians: 0.0f,
            Falloff: 4.0f,
            ShapePower: 1.0f,
            Amplitude: amplitude,
            Seed: 13,
            Tags: "raster");
    }

    private static AquariumFractalSurfacePage Page(
        AquariumFractalKey domainKey,
        AquariumFractalKey nodeKey,
        AquariumFractalSurfacePageKind kind,
        int width,
        int height)
    {
        return new AquariumFractalSurfacePage(
            new AquariumFractalSurfacePageKey(domainKey, nodeKey, kind, mipLevel: 0),
            new Vector4(-1.0f, -1.0f, 1.0f, 1.0f),
            width,
            height,
            PayloadHandle: 0,
            MaxError: 1.0f);
    }
}
