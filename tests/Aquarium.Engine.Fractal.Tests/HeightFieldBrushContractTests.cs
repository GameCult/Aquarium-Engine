using System.Numerics;
using Aquarium.Engine.Render;

namespace Aquarium.Engine.Fractal.Tests;

public sealed class HeightFieldBrushContractTests
{
    [Fact]
    public void LegacyHeightFieldBrushConstructorDefaultsToCircularEnvelope()
    {
        var brush = new AquariumHeightFieldBrush(Vector2.Zero, 4.0f, 2.0f, -1.0f, 0.0f, 0.0f, 0.0f, 0.0f);

        Assert.Equal(0.0f, brush.RadiusY);
        Assert.Equal(0.0f, brush.RotationRadians);
        Assert.Equal(0.0f, brush.EnvelopeFalloff);
    }

    [Fact]
    public void ShapedHeightFieldBrushCanCarryAnisotropicEnvelopeFields()
    {
        var brush = new AquariumHeightFieldBrush(
            Vector2.One,
            4.0f,
            0.75f,
            -1.0f,
            0.0f,
            0.0f,
            0.0f,
            0.0f,
            RadiusY: 2.0f,
            RotationRadians: 0.5f,
            EnvelopeFalloff: 4.0f);

        Assert.Equal(2.0f, brush.RadiusY);
        Assert.Equal(0.5f, brush.RotationRadians);
        Assert.Equal(4.0f, brush.EnvelopeFalloff);
    }
}
