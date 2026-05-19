using System.Numerics;

namespace Aquarium.Engine.Fractal.Brushes;

public readonly record struct FractalBrushEnvelope2D
{
    private const double MinRadius = 1.0e-6;
    private const double MinFalloff = 1.0e-6;
    private const double MinShapePower = 1.0e-6;

    public FractalBrushEnvelope2D(
        Vector2 center,
        double radiusX,
        double radiusY,
        double rotationRadians,
        double falloff,
        double shapePower = 1.0)
    {
        if (!double.IsFinite(radiusX) || radiusX <= MinRadius)
        {
            throw new ArgumentOutOfRangeException(nameof(radiusX), radiusX, "Envelope radius X must be positive and finite.");
        }

        if (!double.IsFinite(radiusY) || radiusY <= MinRadius)
        {
            throw new ArgumentOutOfRangeException(nameof(radiusY), radiusY, "Envelope radius Y must be positive and finite.");
        }

        if (!double.IsFinite(rotationRadians))
        {
            throw new ArgumentOutOfRangeException(nameof(rotationRadians), rotationRadians, "Envelope rotation must be finite.");
        }

        if (!double.IsFinite(falloff) || falloff <= MinFalloff)
        {
            throw new ArgumentOutOfRangeException(nameof(falloff), falloff, "Envelope falloff must be positive and finite.");
        }

        if (!double.IsFinite(shapePower) || shapePower <= MinShapePower)
        {
            throw new ArgumentOutOfRangeException(nameof(shapePower), shapePower, "Envelope shape power must be positive and finite.");
        }

        Center = center;
        RadiusX = radiusX;
        RadiusY = radiusY;
        RotationRadians = rotationRadians;
        Falloff = falloff;
        ShapePower = shapePower;
    }

    public Vector2 Center { get; }

    public double RadiusX { get; }

    public double RadiusY { get; }

    public double RotationRadians { get; }

    public double Falloff { get; }

    public double ShapePower { get; }

    public double Evaluate(Vector2 point)
    {
        var normalizedRadiusSquared = NormalizedRadiusSquared(point);
        if (normalizedRadiusSquared >= 1.0)
        {
            return 0.0;
        }

        var edgeValue = Math.Exp(-Falloff);
        var gaussianValue = Math.Exp(-Falloff * normalizedRadiusSquared);
        var compactValue = (gaussianValue - edgeValue) / (1.0 - edgeValue);
        return Math.Pow(Math.Clamp(compactValue, 0.0, 1.0), ShapePower);
    }

    public bool Contains(Vector2 point)
    {
        return NormalizedRadiusSquared(point) <= 1.0;
    }

    public double SignedSupportDistanceEstimate(Vector2 point)
    {
        var normalizedRadius = Math.Sqrt(NormalizedRadiusSquared(point));
        return (normalizedRadius - 1.0) * Math.Min(RadiusX, RadiusY);
    }

    public FractalBrushEnvelopeBounds2D AxisAlignedBounds()
    {
        var cos = Math.Cos(RotationRadians);
        var sin = Math.Sin(RotationRadians);
        var extentX = Math.Sqrt((RadiusX * cos * RadiusX * cos) + (RadiusY * sin * RadiusY * sin));
        var extentY = Math.Sqrt((RadiusX * sin * RadiusX * sin) + (RadiusY * cos * RadiusY * cos));
        var extents = new Vector2((float)extentX, (float)extentY);
        return new FractalBrushEnvelopeBounds2D(Center - extents, Center + extents);
    }

    public FractalBrushEnvelope2DPacket ToPacket(double amplitude = 1.0)
    {
        var cos = Math.Cos(RotationRadians);
        var sin = Math.Sin(RotationRadians);
        return new FractalBrushEnvelope2DPacket(
            new Vector4(Center.X, Center.Y, (float)RadiusX, (float)RadiusY),
            new Vector4((float)cos, (float)sin, (float)Falloff, (float)ShapePower),
            new Vector4((float)amplitude, 0.0f, 0.0f, 0.0f));
    }

    private double NormalizedRadiusSquared(Vector2 point)
    {
        var delta = point - Center;
        var cos = Math.Cos(RotationRadians);
        var sin = Math.Sin(RotationRadians);
        var localX = delta.X * cos + delta.Y * sin;
        var localY = -delta.X * sin + delta.Y * cos;
        var normalizedX = localX / RadiusX;
        var normalizedY = localY / RadiusY;
        return (normalizedX * normalizedX) + (normalizedY * normalizedY);
    }
}
