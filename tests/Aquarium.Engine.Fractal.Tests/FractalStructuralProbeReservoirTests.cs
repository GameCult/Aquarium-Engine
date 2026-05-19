using System.Numerics;
using Aquarium.Engine.Fractal;
using Aquarium.Engine.Fractal.Grammar;
using Aquarium.Engine.Fractal.Lod;

namespace Aquarium.Engine.Fractal.Tests;

public sealed class FractalStructuralProbeReservoirTests
{
    [Fact]
    public void StructuralProbeReservoirBuildsFromSelectedCut()
    {
        var domainKey = new AquariumFractalKey("domain/tile");
        var rootKey = new AquariumFractalKey("domain/tile/root");
        var domain = new AquariumFractalDomain(domainKey, AquariumFractalDomainKind.CubeSphereTile, default, Vector4.Zero, Vector4.Zero);
        var tree = FractalOwnershipTreeBuilder.BuildFlatUnion(domain, rootKey, [
            Claim("claim/a", domainKey, rootKey, Vector2.Zero, Vector2.One),
        ]);
        var summaries = FractalSummaryBuilder.Build(tree);
        var cut = FractalSelectedCutBuilder.Build(summaries, _ => 8.0f, maxEstimatedCost: 8.0f);

        var snapshot = FractalStructuralProbeReservoir.Build(tree, summaries, cut, 8.0f, new TestFractalRandom(0.0));

        Assert.True(snapshot.HasSample);
        Assert.Equal(domainKey, snapshot.DomainKey);
        Assert.Equal(rootKey, snapshot.NodeKey);
        Assert.Equal(Vector3.Zero, snapshot.LocalCenter);
        Assert.Equal(1, snapshot.CandidateCount);
        Assert.Equal(0, snapshot.PayloadHandle);
        Assert.True(snapshot.TargetContribution > 0.0f);
        Assert.True(snapshot.SourcePdf > 0.0f);
        Assert.True(snapshot.WeightSum > 0.0f);
    }

    [Fact]
    public void StructuralProbeReservoirReturnsEmptyForEmptyCut()
    {
        var domainKey = new AquariumFractalKey("domain/tile");
        var domain = new AquariumFractalDomain(domainKey, AquariumFractalDomainKind.CubeSphereTile, default, Vector4.Zero, Vector4.Zero);
        var tree = FractalOwnershipTreeBuilder.BuildFlatUnion(domain, new AquariumFractalKey("domain/tile/root"), []);

        var snapshot = FractalStructuralProbeReservoir.Build(tree, [], [], 8.0f, new TestFractalRandom(0.0));

        Assert.False(snapshot.HasSample);
        Assert.Equal(0, snapshot.CandidateCount);
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
            Tags: "probe");
    }
}
