using System.Numerics;
using Aquarium.Engine.Fractal;
using Aquarium.Engine.Fractal.Lod;

namespace Aquarium.Engine.Fractal.Tests;

public sealed class FractalSdfSplat3DCompilerTests
{
    [Fact]
    public void SdfSplat3DCompilerLowersProbeIntoCompactSupportPacket()
    {
        var probe = new FractalProbeSample(
            new AquariumFractalKey("domain/object"),
            new AquariumFractalKey("node/object"),
            new Vector3(1.0f, 2.0f, 3.0f),
            BoundRadius: 4.0f,
            TargetContribution: 0.8f,
            SourcePdf: 0.25f,
            MaterialDelta: 0.3f,
            PayloadHandle: 12);

        var splat = FractalSdfSplat3DCompiler.FromProbe(probe, thickness: 0.5f, falloff: 3.0f, shapePower: 0.75f);

        Assert.Equal("domain/object:node/object:sdf3d:000012", splat.Key.Value);
        Assert.Equal(probe.LocalCenter, splat.Center);
        Assert.Equal(new Vector3(4.0f, 4.0f, 0.5f), splat.Radii);
        Assert.Equal(0.8f, splat.Confidence);
        Assert.Equal(0.3f, splat.MaterialValue);
    }

    [Fact]
    public void SdfSplat3DKernelIsCompactAndSigned()
    {
        var probe = new FractalProbeSample(
            new AquariumFractalKey("domain/object"),
            new AquariumFractalKey("node/object"),
            Vector3.Zero,
            BoundRadius: 2.0f,
            TargetContribution: 1.0f,
            SourcePdf: 1.0f,
            MaterialDelta: 0.0f,
            PayloadHandle: 0);
        var splat = FractalSdfSplat3DCompiler.FromProbe(probe, thickness: 1.0f);

        Assert.Equal(1.0f, FractalSdfSplat3DKernel.CompactWeight(splat, Vector3.Zero), 5);
        Assert.Equal(0.0f, FractalSdfSplat3DKernel.CompactWeight(splat, new Vector3(2.0f, 0.0f, 0.0f)), 5);
        Assert.True(FractalSdfSplat3DKernel.SignedDistance(splat, Vector3.Zero) < 0.0f);
        Assert.Equal(0.0f, FractalSdfSplat3DKernel.SignedDistance(splat, new Vector3(2.0f, 0.0f, 0.0f)), 5);
    }

    [Fact]
    public void SdfSplat3DCompilerRejectsInvalidThickness()
    {
        var probe = new FractalProbeSample(
            new AquariumFractalKey("domain/object"),
            new AquariumFractalKey("node/object"),
            Vector3.Zero,
            BoundRadius: 1.0f,
            TargetContribution: 1.0f,
            SourcePdf: 1.0f,
            MaterialDelta: 0.0f,
            PayloadHandle: 0);

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => FractalSdfSplat3DCompiler.FromProbe(probe, thickness: 0.0f));

        Assert.Equal("thickness", ex.ParamName);
    }
}
