using System.Numerics;
using Aquarium.Engine.Fractal;
using Aquarium.Engine.Fractal.Lod;

namespace Aquarium.Engine.Fractal.Tests;

public sealed class FractalProjectedSdfSplatCompilerTests
{
    [Fact]
    public void ProjectedCompilerLowersNearSurfaceSdfSamplesTo3DSplats()
    {
        var page = Page(AquariumFractalSurfacePageKind.SignedDistance2D);
        var payload = new FractalSurfacePagePayload(page, [
            1.0f, 0.2f, 1.0f,
            0.2f, 0.0f, 0.2f,
            1.0f, 0.2f, 1.0f,
        ]);

        var splats = FractalProjectedSdfSplatCompiler.Compile(
            payload,
            point => new Vector3(point.X, point.Y, 2.0f),
            Vector3.UnitZ,
            tangentRadius: 0.25f,
            thickness: 0.1f,
            maxAbsDistance: 0.05f,
            maxSplats: 8);

        var splat = Assert.Single(splats);
        Assert.Equal(new Vector3(0.0f, 0.0f, 2.0f), splat.Center);
        Assert.Equal(0.0f, splat.DistanceOffset);
        Assert.Equal(1.0f, splat.Confidence);
        Assert.Equal(page.Key.NodeKey, splat.Key.NodeKey);
    }

    [Fact]
    public void ProjectedCompilerKeepsClosestSamplesUnderBudget()
    {
        var page = Page(AquariumFractalSurfacePageKind.SignedDistance2D);
        var payload = new FractalSurfacePagePayload(page, [
            0.3f, 0.2f, 0.1f,
            0.2f, 0.0f, 0.2f,
            0.1f, 0.2f, 0.3f,
        ]);

        var splats = FractalProjectedSdfSplatCompiler.Compile(
            payload,
            point => new Vector3(point.X, point.Y, 0.0f),
            Vector3.UnitZ,
            tangentRadius: 0.25f,
            thickness: 0.1f,
            maxAbsDistance: 0.3f,
            maxSplats: 2);

        Assert.Equal(2, splats.Length);
        Assert.Equal(0.0f, splats[0].DistanceOffset);
        Assert.Equal(0.1f, splats[1].DistanceOffset);
    }

    [Fact]
    public void ProjectedCompilerRejectsNonSdfPages()
    {
        var payload = new FractalSurfacePagePayload(Page(AquariumFractalSurfacePageKind.Height), [0.0f]);

        Assert.Throws<ArgumentException>(() => FractalProjectedSdfSplatCompiler.Compile(
            payload,
            point => new Vector3(point.X, point.Y, 0.0f),
            Vector3.UnitZ,
            tangentRadius: 0.25f,
            thickness: 0.1f,
            maxAbsDistance: 0.3f,
            maxSplats: 2));
    }

    private static AquariumFractalSurfacePage Page(AquariumFractalSurfacePageKind kind)
    {
        var size = kind == AquariumFractalSurfacePageKind.Height ? 1 : 3;
        return new AquariumFractalSurfacePage(
            new AquariumFractalSurfacePageKey(
                new AquariumFractalKey("domain/surface"),
                new AquariumFractalKey("node/surface"),
                kind,
                mipLevel: 0),
            new Vector4(-1.0f, -1.0f, 1.0f, 1.0f),
            size,
            size,
            PayloadHandle: 10,
            MaxError: 1.0f);
    }
}
