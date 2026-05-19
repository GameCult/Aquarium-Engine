using System.Numerics;
using Aquarium.Engine.Render;

namespace Aquarium.Engine.Fractal.Temporal;

public static class TemporalSpatialEvidenceLowering
{
    public static TemporalSpatialEvidenceObservation FromGaussianObservation(TemporalGaussianObservation observation)
    {
        return new TemporalSpatialEvidenceObservation(
            observation.StableKey,
            observation.Center,
            observation.Radii,
            observation.Orientation,
            observation.ColorOpacity,
            new Vector4(
                MathF.Max(0.0001f, observation.Falloff),
                MathF.Max(0.0001f, observation.ShapePower),
                0.0f,
                0.0f),
            observation.Confidence,
            observation.ObservedTimeSeconds,
            observation.FieldId);
    }

    public static AquariumTemporalSdfGaussian ToTemporalGaussian(TemporalSpatialEvidenceSample sample)
    {
        return new AquariumTemporalSdfGaussian(
            sample.StableKey,
            sample.Center,
            sample.PreviousCenter,
            sample.Velocity,
            sample.Radii,
            sample.Orientation,
            sample.Payload0,
            sample.Confidence,
            sample.HistoryWeight,
            sample.LastObservedTimeSeconds,
            MathF.Max(0.0001f, sample.Payload1.X),
            MathF.Max(0.0001f, sample.Payload1.Y),
            sample.FieldId);
    }

    public static TemporalSpatialEvidenceObservation FromGpuFusionSeed(AquariumGpuFusionSeed seed, float observedTimeSeconds)
    {
        return new TemporalSpatialEvidenceObservation(
            seed.StableKey,
            seed.Center,
            seed.Radii,
            Quaternion.Identity,
            seed.ColorOpacity,
            new Vector4(
                MathF.Max(0.0001f, seed.Falloff),
                MathF.Max(0.0001f, seed.ShapePower),
                seed.HistoryWeight,
                0.0f),
            seed.Confidence,
            observedTimeSeconds,
            seed.FieldId);
    }

    public static AquariumGpuFusionSeed ToGpuFusionSeed(TemporalSpatialEvidenceSample sample)
    {
        return new AquariumGpuFusionSeed(
            sample.StableKey,
            sample.Center,
            sample.PreviousCenter,
            sample.Velocity,
            sample.Radii,
            sample.Payload0,
            sample.Confidence,
            sample.HistoryWeight,
            MathF.Max(0.0001f, sample.Payload1.X),
            MathF.Max(0.0001f, sample.Payload1.Y),
            sample.FieldId);
    }
}
