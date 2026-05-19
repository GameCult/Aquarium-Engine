using System.Numerics;

namespace Aquarium.Engine.Fractal.Lod;

public static class FractalSdfSplat3DCompiler
{
    public static AquariumFractalSdfSplat3D FromProbe(
        FractalProbeSample probe,
        float thickness,
        float falloff = 4.0f,
        float shapePower = 1.0f)
    {
        if (!float.IsFinite(probe.BoundRadius) || probe.BoundRadius <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(probe), probe.BoundRadius, "Probe bound radius must be positive and finite.");
        }

        if (!float.IsFinite(thickness) || thickness <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(thickness), thickness, "3D SDF splat thickness must be positive and finite.");
        }

        if (!float.IsFinite(falloff) || falloff <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(falloff), falloff, "3D SDF splat falloff must be positive and finite.");
        }

        if (!float.IsFinite(shapePower) || shapePower <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(shapePower), shapePower, "3D SDF splat shape power must be positive and finite.");
        }

        return new AquariumFractalSdfSplat3D(
            new AquariumFractalSdfSplat3DKey(probe.DomainKey, probe.NodeKey, probe.PayloadHandle),
            probe.LocalCenter,
            Quaternion.Identity,
            new Vector3(probe.BoundRadius, probe.BoundRadius, thickness),
            falloff,
            shapePower,
            DistanceOffset: 0.0f,
            MaterialValue: Math.Clamp(probe.MaterialDelta, 0.0f, 1.0f),
            Confidence: Math.Clamp(probe.TargetContribution, 0.0f, 1.0f));
    }
}
