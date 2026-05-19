using System.Numerics;

namespace Aquarium.Engine.Fractal.Lod;

public static class FractalSdfSplat3DKernel
{
    public static float CompactWeight(AquariumFractalSdfSplat3D splat, Vector3 point)
    {
        var local = Vector3.Transform(point - splat.Center, Quaternion.Inverse(splat.Orientation));
        var radii = Vector3.Max(splat.Radii, new Vector3(0.0001f));
        var q = local / radii;
        var radiusSquared = Vector3.Dot(q, q);
        if (radiusSquared >= 1.0f)
        {
            return 0.0f;
        }

        var falloff = MathF.Max(splat.Falloff, 0.0001f);
        var shapePower = MathF.Max(splat.ShapePower, 0.0001f);
        var edge = MathF.Exp(-falloff);
        var value = MathF.Exp(-falloff * radiusSquared);
        var compact = Math.Clamp((value - edge) / MathF.Max(1.0f - edge, 0.000001f), 0.0f, 1.0f);
        return MathF.Pow(compact, shapePower);
    }

    public static float SignedDistance(AquariumFractalSdfSplat3D splat, Vector3 point)
    {
        var local = Vector3.Transform(point - splat.Center, Quaternion.Inverse(splat.Orientation));
        var radii = Vector3.Max(splat.Radii, new Vector3(0.0001f));
        var q = local / radii;
        return ((q.Length() - 1.0f) * MathF.Min(radii.X, MathF.Min(radii.Y, radii.Z))) + splat.DistanceOffset;
    }
}
