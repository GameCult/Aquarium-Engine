using System.Numerics;
using Aquarium.Engine.Fractal;

namespace Aquarium.Engine.Fractal.Tests;

public sealed class FractalSdfSplat3DContractTests
{
    [Fact]
    public void SdfSplat3DKeyIsStableAcrossDomainNodeAndPayload()
    {
        var key = new AquariumFractalSdfSplat3DKey(
            new AquariumFractalKey("domain/object"),
            new AquariumFractalKey("node/branch"),
            payloadHandle: 17);

        Assert.Equal("domain/object:node/branch:sdf3d:000017", key.Value);
    }

    [Fact]
    public void SdfSplat3DKeyRejectsNegativePayloadHandle()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new AquariumFractalSdfSplat3DKey(
            new AquariumFractalKey("domain/object"),
            new AquariumFractalKey("node/branch"),
            payloadHandle: -1));

        Assert.Equal("payloadHandle", ex.ParamName);
    }

    [Fact]
    public void SdfSplat3DContractCarriesCompactSupportFields()
    {
        var splat = new AquariumFractalSdfSplat3D(
            new AquariumFractalSdfSplat3DKey(new AquariumFractalKey("domain/object"), new AquariumFractalKey("node/branch"), 2),
            Center: new Vector3(1.0f, 2.0f, 3.0f),
            Orientation: Quaternion.Identity,
            Radii: new Vector3(4.0f, 5.0f, 6.0f),
            Falloff: 3.0f,
            ShapePower: 0.75f,
            DistanceOffset: -0.1f,
            MaterialValue: 0.25f,
            Confidence: 0.8f);

        Assert.Equal(new Vector3(4.0f, 5.0f, 6.0f), splat.Radii);
        Assert.Equal(0.8f, splat.Confidence);
    }
}
