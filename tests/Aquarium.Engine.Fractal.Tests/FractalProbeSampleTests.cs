using System.Numerics;
using Aquarium.Engine.Fractal;
using Aquarium.Engine.Fractal.Grammar;
using Aquarium.Engine.Fractal.Lod;

namespace Aquarium.Engine.Fractal.Tests;

public sealed class FractalProbeSampleTests
{
    [Fact]
    public void ProbeSampleBuildsReservoirCandidateFromTargetAndSourcePdf()
    {
        var sample = Probe("zyphos/surface", Vector3.Zero, target: 4.0f, sourcePdf: 0.5f);

        var candidate = sample.ToReservoirCandidate();

        Assert.Equal(sample, candidate.Sample);
        Assert.Equal(4.0f, candidate.Target);
        Assert.Equal(0.5f, candidate.SourcePdf);
        Assert.Equal(8.0f, candidate.ImportanceWeight);
    }

    [Fact]
    public void ReuseValidatorAllowsSameLineageWithinLocalShiftBound()
    {
        var graph = DomainGraph();
        var source = Probe("zyphos/surface", Vector3.Zero);
        var target = Probe("zyphos/surface/forest", new Vector3(0.25f, 0.0f, 0.0f));

        var result = FractalProbeReuseValidator.Validate(source, target, graph, maxLocalShift: 0.5f);

        Assert.True(result.CanReuse);
        Assert.Equal(FractalProbeReuseRejection.None, result.Rejection);
        Assert.InRange(result.LocalShift, 0.24f, 0.26f);
    }

    [Fact]
    public void ReuseValidatorRejectsSiblingPlanetDomains()
    {
        var graph = DomainGraph();
        var source = Probe("zyphos/surface", Vector3.Zero);
        var target = Probe("umbros/surface", Vector3.Zero);

        var result = FractalProbeReuseValidator.Validate(source, target, graph, maxLocalShift: 1.0f);

        Assert.False(result.CanReuse);
        Assert.Equal(FractalProbeReuseRejection.DifferentLineage, result.Rejection);
    }

    [Fact]
    public void ReuseValidatorRejectsExcessiveLocalShift()
    {
        var graph = DomainGraph();
        var source = Probe("zyphos/surface", Vector3.Zero);
        var target = Probe("zyphos/surface", new Vector3(2.0f, 0.0f, 0.0f));

        var result = FractalProbeReuseValidator.Validate(source, target, graph, maxLocalShift: 0.5f);

        Assert.False(result.CanReuse);
        Assert.Equal(FractalProbeReuseRejection.ExcessiveLocalShift, result.Rejection);
    }

    [Fact]
    public void TemporalReuseValidatorRejectsExcessiveCameraMotion()
    {
        var result = FractalProbeReuseValidator.ValidateTemporal(
            Probe("zyphos/surface", Vector3.Zero),
            Probe("zyphos/surface", Vector3.Zero),
            DomainGraph(),
            maxLocalShift: 1.0f,
            ValidTemporal() with { CameraMotionPixels = 12.0f, MaxCameraMotionPixels = 4.0f });

        Assert.False(result.CanReuse);
        Assert.Equal(FractalProbeReuseRejection.ExcessiveCameraMotion, result.Rejection);
    }

    [Fact]
    public void TemporalReuseValidatorRejectsDisocclusion()
    {
        var result = FractalProbeReuseValidator.ValidateTemporal(
            Probe("zyphos/surface", Vector3.Zero),
            Probe("zyphos/surface", Vector3.Zero),
            DomainGraph(),
            maxLocalShift: 1.0f,
            ValidTemporal() with { DisocclusionConfidence = 0.2f, MinDisocclusionConfidence = 0.7f });

        Assert.False(result.CanReuse);
        Assert.Equal(FractalProbeReuseRejection.Disoccluded, result.Rejection);
    }

    [Fact]
    public void TemporalReuseValidatorRejectsMaterialMismatch()
    {
        var result = FractalProbeReuseValidator.ValidateTemporal(
            Probe("zyphos/surface", Vector3.Zero),
            Probe("zyphos/surface", Vector3.Zero),
            DomainGraph(),
            maxLocalShift: 1.0f,
            ValidTemporal() with { MaterialDelta = 0.6f, MaxMaterialDelta = 0.25f });

        Assert.False(result.CanReuse);
        Assert.Equal(FractalProbeReuseRejection.MaterialMismatch, result.Rejection);
    }

    [Fact]
    public void TemporalReuseValidatorAllowsValidRendererInputs()
    {
        var result = FractalProbeReuseValidator.ValidateTemporal(
            Probe("zyphos/surface", Vector3.Zero),
            Probe("zyphos/surface/forest", new Vector3(0.1f, 0.0f, 0.0f)),
            DomainGraph(),
            maxLocalShift: 1.0f,
            ValidTemporal());

        Assert.True(result.CanReuse);
        Assert.Equal(FractalProbeReuseRejection.None, result.Rejection);
    }

    private static FractalProbeSample Probe(
        string domainKey,
        Vector3 localCenter,
        float target = 1.0f,
        float sourcePdf = 1.0f)
    {
        return new FractalProbeSample(
            new AquariumFractalKey(domainKey),
            new AquariumFractalKey($"{domainKey}/node"),
            localCenter,
            BoundRadius: 0.5f,
            TargetContribution: target,
            SourcePdf: sourcePdf,
            MaterialDelta: 0.25f,
            PayloadHandle: 7);
    }

    private static FractalDomainGraph DomainGraph()
    {
        return new FractalDomainGraph([
            Domain("solar", AquariumFractalDomainKind.Solar, parent: null),
            Domain("zyphos", AquariumFractalDomainKind.Planetary, "solar"),
            Domain("zyphos/surface", AquariumFractalDomainKind.Surface2D, "zyphos"),
            Domain("zyphos/surface/forest", AquariumFractalDomainKind.Surface2D, "zyphos/surface"),
            Domain("umbros", AquariumFractalDomainKind.Planetary, "solar"),
            Domain("umbros/surface", AquariumFractalDomainKind.Surface2D, "umbros"),
        ]);
    }

    private static FractalProbeTemporalValidation ValidTemporal()
    {
        return new FractalProbeTemporalValidation(
            CameraMotionPixels: 1.0f,
            MaxCameraMotionPixels: 4.0f,
            DisocclusionConfidence: 0.95f,
            MinDisocclusionConfidence: 0.7f,
            MaterialDelta: 0.05f,
            MaxMaterialDelta: 0.25f,
            VisibilityConfidence: 0.9f,
            MinVisibilityConfidence: 0.5f);
    }

    private static AquariumFractalDomain Domain(string key, AquariumFractalDomainKind kind, string? parent)
    {
        return new AquariumFractalDomain(
            new AquariumFractalKey(key),
            kind,
            parent is null ? default : new AquariumFractalKey(parent),
            Vector4.Zero,
            Vector4.Zero);
    }
}
