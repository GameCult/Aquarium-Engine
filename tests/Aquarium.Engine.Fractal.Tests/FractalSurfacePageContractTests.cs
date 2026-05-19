using System.Numerics;
using Aquarium.Engine.Fractal;

namespace Aquarium.Engine.Fractal.Tests;

public sealed class FractalSurfacePageContractTests
{
    [Fact]
    public void SurfacePageKeyIsStableAndIncludesDomainNodeKindAndMip()
    {
        var key = new AquariumFractalSurfacePageKey(
            new AquariumFractalKey("zyphos/tile/0"),
            new AquariumFractalKey("zyphos/tile/0/forest"),
            AquariumFractalSurfacePageKind.SignedDistance2D,
            mipLevel: 3);

        Assert.Equal("zyphos/tile/0:zyphos/tile/0/forest:SignedDistance2D:mip03", key.Value);
        Assert.Equal(key.Value, key.ToString());
    }

    [Fact]
    public void SurfacePageKeyRejectsNegativeMip()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new AquariumFractalSurfacePageKey(
            new AquariumFractalKey("domain"),
            new AquariumFractalKey("node"),
            AquariumFractalSurfacePageKind.Height,
            mipLevel: -1));

        Assert.Equal("mipLevel", ex.ParamName);
    }

    [Fact]
    public void SurfacePageContractCarriesRendererAgnosticPayloadMetadata()
    {
        var key = new AquariumFractalSurfacePageKey(
            new AquariumFractalKey("domain"),
            new AquariumFractalKey("node"),
            AquariumFractalSurfacePageKind.Material,
            mipLevel: 0);

        var page = new AquariumFractalSurfacePage(
            key,
            new Vector4(-1.0f, -2.0f, 3.0f, 4.0f),
            Width: 128,
            Height: 64,
            PayloadHandle: 42,
            MaxError: 0.125f);

        Assert.Equal(key, page.Key);
        Assert.Equal(128, page.Width);
        Assert.Equal(64, page.Height);
        Assert.Equal(42, page.PayloadHandle);
        Assert.Equal(0.125f, page.MaxError);
    }
}
