using System.Numerics;
using Aquarium.Engine.Fractal.Grammar;
using Aquarium.Engine.Fractal.Temporal;

namespace Aquarium.Engine.Fractal.Lod;

public readonly record struct FractalProbeSample(
    AquariumFractalKey DomainKey,
    AquariumFractalKey NodeKey,
    Vector3 LocalCenter,
    float BoundRadius,
    float TargetContribution,
    float SourcePdf,
    float MaterialDelta,
    int PayloadHandle)
{
    public ResampledImportanceCandidate<FractalProbeSample> ToReservoirCandidate()
    {
        return new ResampledImportanceCandidate<FractalProbeSample>(
            this,
            MathF.Max(TargetContribution, 0.0f),
            MathF.Max(SourcePdf, 1.0e-6f));
    }
}

public enum FractalProbeReuseRejection
{
    None,
    UnknownDomain,
    DifferentLineage,
    ExcessiveLocalShift,
    InvalidBounds,
    ExcessiveCameraMotion,
    Disoccluded,
    MaterialMismatch,
    NotVisible,
}

public readonly record struct FractalProbeReuseResult(
    bool CanReuse,
    FractalProbeReuseRejection Rejection,
    float LocalShift);

public readonly record struct FractalProbeTemporalValidation(
    float CameraMotionPixels,
    float MaxCameraMotionPixels,
    float DisocclusionConfidence,
    float MinDisocclusionConfidence,
    float MaterialDelta,
    float MaxMaterialDelta,
    float VisibilityConfidence,
    float MinVisibilityConfidence);

public static class FractalProbeReuseValidator
{
    public static FractalProbeReuseResult Validate(
        FractalProbeSample source,
        FractalProbeSample target,
        FractalDomainGraph domains,
        float maxLocalShift)
    {
        ArgumentNullException.ThrowIfNull(domains);
        if (maxLocalShift < 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(maxLocalShift), maxLocalShift, "Maximum local shift must not be negative.");
        }

        if (!float.IsFinite(source.BoundRadius) || !float.IsFinite(target.BoundRadius) || source.BoundRadius <= 0.0f || target.BoundRadius <= 0.0f)
        {
            return new FractalProbeReuseResult(false, FractalProbeReuseRejection.InvalidBounds, 0.0f);
        }

        if (!domains.Contains(source.DomainKey) || !domains.Contains(target.DomainKey))
        {
            return new FractalProbeReuseResult(false, FractalProbeReuseRejection.UnknownDomain, 0.0f);
        }

        if (!ShareLineage(source.DomainKey, target.DomainKey, domains))
        {
            return new FractalProbeReuseResult(false, FractalProbeReuseRejection.DifferentLineage, 0.0f);
        }

        var localShift = Vector3.Distance(source.LocalCenter, target.LocalCenter);
        var allowedShift = MathF.Min(maxLocalShift, source.BoundRadius + target.BoundRadius);
        if (!float.IsFinite(localShift) || localShift > allowedShift)
        {
            return new FractalProbeReuseResult(false, FractalProbeReuseRejection.ExcessiveLocalShift, localShift);
        }

        return new FractalProbeReuseResult(true, FractalProbeReuseRejection.None, localShift);
    }

    public static FractalProbeReuseResult ValidateTemporal(
        FractalProbeSample source,
        FractalProbeSample target,
        FractalDomainGraph domains,
        float maxLocalShift,
        FractalProbeTemporalValidation temporal)
    {
        var spatial = Validate(source, target, domains, maxLocalShift);
        if (!spatial.CanReuse)
        {
            return spatial;
        }

        if (!float.IsFinite(temporal.CameraMotionPixels) || temporal.CameraMotionPixels > MathF.Max(temporal.MaxCameraMotionPixels, 0.0f))
        {
            return spatial with { CanReuse = false, Rejection = FractalProbeReuseRejection.ExcessiveCameraMotion };
        }

        if (!float.IsFinite(temporal.DisocclusionConfidence) || temporal.DisocclusionConfidence < Math.Clamp(temporal.MinDisocclusionConfidence, 0.0f, 1.0f))
        {
            return spatial with { CanReuse = false, Rejection = FractalProbeReuseRejection.Disoccluded };
        }

        if (!float.IsFinite(temporal.MaterialDelta) || temporal.MaterialDelta > MathF.Max(temporal.MaxMaterialDelta, 0.0f))
        {
            return spatial with { CanReuse = false, Rejection = FractalProbeReuseRejection.MaterialMismatch };
        }

        if (!float.IsFinite(temporal.VisibilityConfidence) || temporal.VisibilityConfidence < Math.Clamp(temporal.MinVisibilityConfidence, 0.0f, 1.0f))
        {
            return spatial with { CanReuse = false, Rejection = FractalProbeReuseRejection.NotVisible };
        }

        return spatial;
    }

    private static bool ShareLineage(AquariumFractalKey sourceDomain, AquariumFractalKey targetDomain, FractalDomainGraph domains)
    {
        var sourcePath = domains.GetPath(sourceDomain).Select(domain => domain.Key).ToArray();
        var targetPath = domains.GetPath(targetDomain).Select(domain => domain.Key).ToArray();
        var shorterLength = Math.Min(sourcePath.Length, targetPath.Length);
        for (var index = 0; index < shorterLength; index++)
        {
            if (!sourcePath[index].Equals(targetPath[index]))
            {
                return false;
            }
        }

        return shorterLength == sourcePath.Length || shorterLength == targetPath.Length;
    }
}
