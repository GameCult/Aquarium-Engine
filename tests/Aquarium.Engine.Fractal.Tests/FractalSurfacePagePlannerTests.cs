using System.Numerics;
using Aquarium.Engine.Fractal;
using Aquarium.Engine.Fractal.Grammar;
using Aquarium.Engine.Fractal.Lod;

namespace Aquarium.Engine.Fractal.Tests;

public sealed class FractalSurfacePagePlannerTests
{
    [Fact]
    public void SurfacePagePlannerBuildsStablePagesFromSelectedCut()
    {
        var domainKey = new AquariumFractalKey("domain/tile");
        var rootKey = new AquariumFractalKey("domain/tile/root");
        var domain = new AquariumFractalDomain(domainKey, AquariumFractalDomainKind.CubeSphereTile, default, Vector4.Zero, Vector4.Zero);
        var tree = FractalOwnershipTreeBuilder.BuildFlatUnion(domain, rootKey, [
            Claim("claim/a", domainKey, rootKey, Vector2.Zero, Vector2.One, amplitude: 2.0f),
        ]);
        var summaries = FractalSummaryBuilder.Build(tree);
        var cut = FractalSelectedCutBuilder.Build(summaries, _ => 8.0f, maxEstimatedCost: 8.0f);

        var pages = FractalSurfacePagePlanner.Plan(tree, summaries, cut, AquariumFractalSurfacePageKind.SignedDistance2D, 128, 128);

        var page = Assert.Single(pages);
        Assert.Equal(domainKey, page.Key.DomainKey);
        Assert.Equal(rootKey, page.Key.NodeKey);
        Assert.Equal(AquariumFractalSurfacePageKind.SignedDistance2D, page.Key.Kind);
        Assert.Equal(128, page.Width);
        Assert.Equal(128, page.Height);
        Assert.Equal(0, page.PayloadHandle);
        Assert.Equal(2.0f, page.MaxError);
    }

    [Fact]
    public void SurfacePagePlannerRejectsInvalidDimensions()
    {
        var domainKey = new AquariumFractalKey("domain/tile");
        var rootKey = new AquariumFractalKey("domain/tile/root");
        var domain = new AquariumFractalDomain(domainKey, AquariumFractalDomainKind.CubeSphereTile, default, Vector4.Zero, Vector4.Zero);
        var tree = FractalOwnershipTreeBuilder.BuildFlatUnion(domain, rootKey, []);

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => FractalSurfacePagePlanner.Plan(
            tree,
            [],
            [],
            AquariumFractalSurfacePageKind.Height,
            width: 0,
            height: 128));

        Assert.Equal("width", ex.ParamName);
    }

    [Fact]
    public void SurfacePagePlannerRejectsInvalidMipEvenForEmptyCut()
    {
        var domainKey = new AquariumFractalKey("domain/tile");
        var rootKey = new AquariumFractalKey("domain/tile/root");
        var domain = new AquariumFractalDomain(domainKey, AquariumFractalDomainKind.CubeSphereTile, default, Vector4.Zero, Vector4.Zero);
        var tree = FractalOwnershipTreeBuilder.BuildFlatUnion(domain, rootKey, []);

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => FractalSurfacePagePlanner.Plan(
            tree,
            [],
            [],
            AquariumFractalSurfacePageKind.Height,
            width: 128,
            height: 128,
            mipLevel: -1));

        Assert.Equal("mipLevel", ex.ParamName);
    }

    [Fact]
    public void SurfacePagePlannerUsesKindSpecificErrorProxy()
    {
        var domainKey = new AquariumFractalKey("domain/tile");
        var rootKey = new AquariumFractalKey("domain/tile/root");
        var domain = new AquariumFractalDomain(domainKey, AquariumFractalDomainKind.CubeSphereTile, default, Vector4.Zero, Vector4.Zero);
        var tree = FractalOwnershipTreeBuilder.BuildFlatUnion(domain, rootKey, [
            Claim("claim/a", domainKey, rootKey, Vector2.Zero, Vector2.One, amplitude: 2.0f),
        ]);
        var summary = new AquariumFractalSummary(
            rootKey,
            new Vector4(-1.0f, -2.0f, 3.0f, 4.0f),
            MaxHeightError: 0.25f,
            MaxMaterialDelta: 0.75f,
            EstimatedCost: 1.0f,
            DescendantCount: 0);
        var cut = new[] { new AquariumSelectedCut(rootKey, Score: 1.0f, Fade: 1.0f, UsesSummary: false, RequestedChildren: false) };

        var height = FractalSurfacePagePlanner.Plan(tree, [summary], cut, AquariumFractalSurfacePageKind.Height, 128, 128);
        var material = FractalSurfacePagePlanner.Plan(tree, [summary], cut, AquariumFractalSurfacePageKind.Material, 128, 128);
        var confidence = FractalSurfacePagePlanner.Plan(tree, [summary], cut, AquariumFractalSurfacePageKind.Confidence, 128, 128);

        Assert.Equal(0.25f, Assert.Single(height).MaxError);
        Assert.Equal(0.75f, Assert.Single(material).MaxError);
        Assert.Equal(0.75f, Assert.Single(confidence).MaxError);
    }

    private static AquariumBrushClaim Claim(
        string key,
        AquariumFractalKey domainKey,
        AquariumFractalKey nodeKey,
        Vector2 center,
        Vector2 radii,
        float amplitude)
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
            Amplitude: amplitude,
            Seed: 17,
            Tags: "page");
    }
}
