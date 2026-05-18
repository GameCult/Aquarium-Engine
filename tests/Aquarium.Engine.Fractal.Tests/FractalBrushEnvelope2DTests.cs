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

    [Fact]
    public void CpuEnvelopeMatchesShaderReferenceFormula()
    {
        var envelope = new FractalBrushEnvelope2D(new Vector2(-1.0f, 2.0f), 4.0, 1.5, 0.37, falloff: 3.25, shapePower: 0.8);

        foreach (var point in new[]
        {
            new Vector2(-1.0f, 2.0f),
            new Vector2(0.5f, 2.25f),
            new Vector2(-2.0f, 2.8f),
            new Vector2(3.5f, 2.0f),
        })
        {
            Assert.Equal(envelope.Evaluate(point), ShaderReferenceEvaluate(envelope, point), 6);
        }
    }

    private static double ShaderReferenceEvaluate(FractalBrushEnvelope2D envelope, Vector2 point)
    {
        var delta = point - envelope.Center;
        var c = Math.Cos(envelope.RotationRadians);
        var s = Math.Sin(envelope.RotationRadians);
        var localX = delta.X * c + delta.Y * s;
        var localY = -delta.X * s + delta.Y * c;
        var normalizedX = localX / Math.Max(envelope.RadiusX, 0.001);
        var normalizedY = localY / Math.Max(envelope.RadiusY, 0.001);
        var normalizedRadiusSquared = (normalizedX * normalizedX) + (normalizedY * normalizedY);
        if (normalizedRadiusSquared >= 1.0)
        {
            return 0.0;
        }

        var edgeValue = Math.Exp(-envelope.Falloff);
        var gaussianValue = Math.Exp(-envelope.Falloff * normalizedRadiusSquared);
        var compactValue = (gaussianValue - edgeValue) / Math.Max(1.0 - edgeValue, 0.000001);
        return Math.Pow(Math.Clamp(compactValue, 0.0, 1.0), envelope.ShapePower);
    }
}
