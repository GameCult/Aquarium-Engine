using System.Numerics;

namespace Aquarium.Engine.Fractal.Lod;

public static class FractalProjectedSdfSplatCompiler
{
    public static AquariumFractalSdfSplat3D[] Compile(
        FractalSurfacePagePayload payload,
        Func<Vector2, Vector3> worldFromPage,
        Vector3 surfaceNormal,
        float tangentRadius,
        float thickness,
        float maxAbsDistance,
        int maxSplats)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(worldFromPage);
        if (payload.Page.Key.Kind != AquariumFractalSurfacePageKind.SignedDistance2D)
        {
            throw new ArgumentException("Projected SDF splats require a SignedDistance2D page.", nameof(payload));
        }

        if (!float.IsFinite(tangentRadius) || tangentRadius <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(tangentRadius), tangentRadius, "Projected SDF tangent radius must be positive and finite.");
        }

        if (!float.IsFinite(thickness) || thickness <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(thickness), thickness, "Projected SDF thickness must be positive and finite.");
        }

        if (!float.IsFinite(maxAbsDistance) || maxAbsDistance < 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAbsDistance), maxAbsDistance, "Projected SDF distance threshold must not be negative.");
        }

        if (maxSplats < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxSplats), maxSplats, "Projected SDF splat budget must not be negative.");
        }

        if (maxSplats == 0)
        {
            return [];
        }

        var orientation = OrientationFromNormal(surfaceNormal);
        return payload.Samples
            .Select((sample, index) => new ProjectedCandidate(sample, index, MathF.Abs(sample)))
            .Where(candidate => candidate.AbsDistance <= maxAbsDistance)
            .OrderBy(candidate => candidate.AbsDistance)
            .ThenBy(candidate => candidate.Index)
            .Take(maxSplats)
            .Select(candidate =>
            {
                var point = SamplePoint(payload.Page, candidate.Index);
                return new AquariumFractalSdfSplat3D(
                    new AquariumFractalSdfSplat3DKey(
                        payload.Page.Key.DomainKey,
                        payload.Page.Key.NodeKey,
                        payload.Page.PayloadHandle + candidate.Index),
                    worldFromPage(point),
                    orientation,
                    new Vector3(tangentRadius, tangentRadius, thickness),
                    Falloff: 4.0f,
                    ShapePower: 1.0f,
                    DistanceOffset: candidate.Sample,
                    MaterialValue: 0.0f,
                    Confidence: 1.0f - Math.Clamp(candidate.AbsDistance / MathF.Max(maxAbsDistance, 0.000001f), 0.0f, 1.0f));
            })
            .ToArray();
    }

    private static Vector2 SamplePoint(AquariumFractalSurfacePage page, int sampleIndex)
    {
        var x = sampleIndex % page.Width;
        var y = sampleIndex / page.Width;
        var u = page.Width == 1 ? 0.5f : x / (float)(page.Width - 1);
        var v = page.Height == 1 ? 0.5f : y / (float)(page.Height - 1);
        return new Vector2(
            Lerp(page.BoundsMinMax.X, page.BoundsMinMax.Z, u),
            Lerp(page.BoundsMinMax.Y, page.BoundsMinMax.W, v));
    }

    private static Quaternion OrientationFromNormal(Vector3 normal)
    {
        var normalized = Vector3.Normalize(normal);
        if (!float.IsFinite(normalized.X) || !float.IsFinite(normalized.Y) || !float.IsFinite(normalized.Z))
        {
            return Quaternion.Identity;
        }

        var zAxis = Vector3.UnitZ;
        var dot = Math.Clamp(Vector3.Dot(zAxis, normalized), -1.0f, 1.0f);
        if (dot > 0.9999f)
        {
            return Quaternion.Identity;
        }

        if (dot < -0.9999f)
        {
            return Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI);
        }

        var axis = Vector3.Normalize(Vector3.Cross(zAxis, normalized));
        return Quaternion.Normalize(Quaternion.CreateFromAxisAngle(axis, MathF.Acos(dot)));
    }

    private static float Lerp(float a, float b, float t)
    {
        return a + ((b - a) * t);
    }

    private readonly record struct ProjectedCandidate(float Sample, int Index, float AbsDistance);
}
