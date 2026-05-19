using System.Numerics;
using Aquarium.Engine.Fractal;
using Aquarium.Engine.Fractal.Lod;

namespace Aquarium.Engine.Fractal.Tests;

public sealed class FractalStructuralProbeGeneratorTests
{
    [Fact]
    public void StructuralProbeUsesSummaryBoundsAsLocalSupport()
    {
        var summary = Summary("node/a", new Vector4(-2.0f, -1.0f, 4.0f, 3.0f), maxHeightError: 1.0f);
        var domain = new AquariumFractalKey("domain/tile");

        var probe = FractalStructuralProbeGenerator.FromSummary(summary, domain, projectedPixelsPerWorldUnit: 8.0f, sourcePdf: 0.25f, payloadHandle: 17);

        Assert.Equal(domain, probe.DomainKey);
        Assert.Equal(summary.NodeKey, probe.NodeKey);
        Assert.Equal(new Vector3(1.0f, 1.0f, 0.0f), probe.LocalCenter);
        Assert.InRange(probe.BoundRadius, 3.60f, 3.61f);
        Assert.Equal(0.25f, probe.SourcePdf);
        Assert.Equal(17, probe.PayloadHandle);
    }

    [Fact]
    public void StructuralProbeTargetComesFromProjectedContribution()
    {
        var summary = Summary("node/a", new Vector4(0.0f, 0.0f, 1.0f, 1.0f), maxHeightError: 2.0f, estimatedCost: 2.0f);

        var near = FractalStructuralProbeGenerator.FromSummary(summary, new AquariumFractalKey("domain/tile"), 8.0f, sourcePdf: 1.0f);
        var far = FractalStructuralProbeGenerator.FromSummary(summary, new AquariumFractalKey("domain/tile"), 2.0f, sourcePdf: 1.0f);

        Assert.True(near.TargetContribution > far.TargetContribution);
    }

    [Fact]
    public void StructuralProbeCandidateClampsInvalidSourceProbability()
    {
        var summary = Summary("node/a", new Vector4(0.0f, 0.0f, 1.0f, 1.0f), maxHeightError: 1.0f);

        var candidate = FractalStructuralProbeGenerator.BuildCandidate(summary, new AquariumFractalKey("domain/tile"), 4.0f, sourceProbability: 0.0);

        Assert.Equal(candidate.Sample.TargetContribution, candidate.Target);
        Assert.True(candidate.SourcePdf > 0.0f);
        Assert.True(candidate.ImportanceWeight > candidate.Target);
    }

    private static AquariumFractalSummary Summary(
        string key,
        Vector4 bounds,
        float maxHeightError,
        float estimatedCost = 1.0f,
        float maxMaterialDelta = 0.0f)
    {
        return new AquariumFractalSummary(
            new AquariumFractalKey(key),
            bounds,
            maxHeightError,
            maxMaterialDelta,
            estimatedCost,
            DescendantCount: 1);
    }
}
