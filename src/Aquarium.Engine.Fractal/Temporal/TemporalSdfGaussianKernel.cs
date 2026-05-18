using System.Numerics;
using Aquarium.Engine.Render;

namespace Aquarium.Engine.Fractal.Temporal;

public static class TemporalSdfGaussianKernel
{
    public static float CompactWeight(AquariumTemporalSdfGaussian gaussian, Vector3 point)
    {
        var local = Vector3.Transform(point - gaussian.Center, Quaternion.Inverse(gaussian.Orientation));
        var radii = Vector3.Max(gaussian.Radii, new Vector3(0.0001f));
        var q = local / radii;
        var radiusSquared = Vector3.Dot(q, q);
        if (radiusSquared >= 1.0f)
        {
            return 0.0f;
        }

        var falloff = MathF.Max(gaussian.Falloff, 0.0001f);
        var shapePower = MathF.Max(gaussian.ShapePower, 0.0001f);
        var edge = MathF.Exp(-falloff);
        var value = MathF.Exp(-falloff * radiusSquared);
        var compact = Math.Clamp((value - edge) / MathF.Max(1.0f - edge, 0.000001f), 0.0f, 1.0f);
        return MathF.Pow(compact, shapePower);
    }

    public static float SignedDistance(AquariumTemporalSdfGaussian gaussian, Vector3 point)
    {
        var local = Vector3.Transform(point - gaussian.Center, Quaternion.Inverse(gaussian.Orientation));
        var radii = Vector3.Max(gaussian.Radii, new Vector3(0.0001f));
        var q = local / radii;
        return (q.Length() - 1.0f) * MathF.Min(radii.X, MathF.Min(radii.Y, radii.Z));
    }
}
