using System.Numerics;
using Aquarium.Engine.Fractal.Brushes;

namespace Aquarium.Engine.Fractal.Tests;

public sealed class FractalBrushEnvelope2DTests
{
    [Fact]
    public void EnvelopeWeightIsOneAtCenterAndZeroAtSupportEdge()
    {
        var envelope = new FractalBrushEnvelope2D(Vector2.Zero, 2.0, 1.0, 0.0, falloff: 4.0);

        Assert.Equal(1.0, envelope.Evaluate(Vector2.Zero), 12);
        Assert.Equal(0.0, envelope.Evaluate(new Vector2(2.0f, 0.0f)), 12);
        Assert.Equal(0.0, envelope.Evaluate(new Vector2(2.1f, 0.0f)), 12);
    }

    [Fact]
    public void EnvelopeRotationChangesTheLongAxis()
    {
        var envelope = new FractalBrushEnvelope2D(Vector2.Zero, 2.0, 1.0, Math.PI * 0.5, falloff: 4.0);

        Assert.True(envelope.Contains(new Vector2(0.0f, 1.5f)));
        Assert.False(envelope.Contains(new Vector2(1.5f, 0.0f)));
    }

    [Fact]
    public void RoundEnvelopeIsRotationInvariant()
    {
        var unrotated = new FractalBrushEnvelope2D(Vector2.Zero, 3.0, 3.0, 0.0, falloff: 2.5);
        var rotated = new FractalBrushEnvelope2D(Vector2.Zero, 3.0, 3.0, 1.7, falloff: 2.5);
        var point = new Vector2(1.0f, 1.25f);

        Assert.Equal(unrotated.Evaluate(point), rotated.Evaluate(point), 12);
    }

    [Fact]
    public void AxisAlignedBoundsContainRotatedSupportCardinals()
    {
        var envelope = new FractalBrushEnvelope2D(new Vector2(4.0f, -2.0f), 3.0, 1.0, Math.PI * 0.25, falloff: 4.0);
        var bounds = envelope.AxisAlignedBounds();

        Assert.True(bounds.Contains(new Vector2(4.0f, -2.0f)));
        Assert.True(bounds.Contains(new Vector2(4.0f + 2.1f, -2.0f + 2.1f)));
        Assert.True(bounds.Contains(new Vector2(4.0f - 2.1f, -2.0f - 2.1f)));
    }

    [Fact]
    public void PacketPreservesEnvelopeFieldsForShaderLowering()
    {
        var envelope = new FractalBrushEnvelope2D(new Vector2(1.0f, 2.0f), 3.0, 4.0, Math.PI, falloff: 5.0, shapePower: 0.75);

        var packet = envelope.ToPacket(amplitude: -2.0);

        Assert.Equal(new Vector4(1.0f, 2.0f, 3.0f, 4.0f), packet.CenterRadii);
        Assert.Equal(-1.0f, packet.RotationFalloffShape.X, 6);
        Assert.Equal(0.0f, packet.RotationFalloffShape.Y, 6);
        Assert.Equal(5.0f, packet.RotationFalloffShape.Z);
        Assert.Equal(0.75f, packet.RotationFalloffShape.W);
        Assert.Equal(-2.0f, packet.Payload.X);
    }
}
