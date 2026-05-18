using System.Numerics;
using Aquarium.Engine.Fractal;
using Aquarium.Engine.Fractal.Brushes;
using Aquarium.Engine.Fractal.Grammar;
using Aquarium.Engine.Fractal.Lod;

namespace Aquarium.Engine.Fractal.Tests;

public sealed class FractalPipelineFixtureTests
{
    [Fact]
    public void CubeTileClaimPipelineProducesRenderableBudgetedBrush()
    {
        var tile = new CubeTileKey(CubeFace.PositiveZ, 2, 1, 1);
        var domainKey = FractalStableKeyBuilder.ForCubeTile(tile, "terrain/ridges");
        var rootKey = FractalStableKeyBuilder.Child(domainKey, "root");
        var claimKey = FractalStableKeyBuilder.Child(rootKey, "claim/0");
        var domain = new AquariumFractalDomain(domainKey, AquariumFractalDomainKind.CubeSphereTile, default, Vector4.Zero, Vector4.Zero);
        var claim = new AquariumBrushClaim(
            claimKey,
            domainKey,
            rootKey,
            AquariumFractalPayloadKind.Height,
            Center: new Vector2(0.25f, -0.1f),
            Radii: new Vector2(0.5f, 0.2f),
            RotationRadians: 0.35f,
            Falloff: 3.0f,
            ShapePower: 0.85f,
            Amplitude: -0.4f,
            Seed: 42,
            Tags: "ridge");

        var tree = FractalOwnershipTreeBuilder.BuildFlatUnion(domain, rootKey, [claim]);
        var summaries = FractalSummaryBuilder.Build(tree);
        var selectedCut = FractalSelectedCutBuilder.Build(summaries, _ => 8.0f, maxEstimatedCost: 1.0f, childRequestScore: 1.0f);
        var residency = FractalResidencyPlanner.Plan(selectedCut, new EmptyPayloadStore());
        var brushes = FractalHeightBrushCompiler.CompileMany(tree.Claims);

        Assert.Single(tree.Nodes);
        Assert.Single(summaries);
        Assert.Single(selectedCut);
        Assert.Single(residency.SummaryFallbackNodes);
        Assert.Single(residency.RequestedNodes);
        Assert.Single(brushes);
        Assert.Equal(claim.Center, brushes[0].Center);
        Assert.Equal(claim.Radii.X, brushes[0].Radius);
        Assert.Equal(claim.Radii.Y, brushes[0].RadiusY);
    }

    private sealed class EmptyPayloadStore : IFractalPayloadStore
    {
        public bool IsResident(AquariumFractalKey nodeKey)
        {
            return false;
        }

        public void Request(AquariumFractalKey nodeKey)
        {
        }
    }
}
