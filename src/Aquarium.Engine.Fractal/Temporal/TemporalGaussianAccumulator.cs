using System.Numerics;
using Aquarium.Engine.Render;

namespace Aquarium.Engine.Fractal.Temporal;

public sealed class TemporalGaussianAccumulator
{
    private readonly TemporalSpatialEvidenceReservoir reservoir;

    public TemporalGaussianAccumulator(
        float accumulationWindowSeconds,
        float presentationDelaySeconds,
        float smoothing = 0.35f)
    {
        if (!float.IsFinite(accumulationWindowSeconds) || accumulationWindowSeconds <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(accumulationWindowSeconds), accumulationWindowSeconds, "Accumulation window must be positive.");
        }

        if (!float.IsFinite(presentationDelaySeconds) || presentationDelaySeconds < 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(presentationDelaySeconds), presentationDelaySeconds, "Presentation delay must be non-negative.");
        }

        if (!float.IsFinite(smoothing) || smoothing <= 0.0f || smoothing > 1.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(smoothing), smoothing, "Smoothing must be in (0, 1].");
        }

        reservoir = new TemporalSpatialEvidenceReservoir(accumulationWindowSeconds, presentationDelaySeconds, smoothing);
    }

    public float AccumulationWindowSeconds => reservoir.AccumulationWindowSeconds;

    public float PresentationDelaySeconds => reservoir.PresentationDelaySeconds;

    public float Smoothing => reservoir.Smoothing;

    public void Observe(IEnumerable<TemporalGaussianObservation> observations)
    {
        reservoir.Observe(observations.Select(observation => new TemporalSpatialEvidenceObservation(
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
            observation.FieldId)));
    }

    public AquariumTemporalGaussianField BuildField(float renderTimeSeconds)
    {
        var snapshot = reservoir.BuildSnapshot(renderTimeSeconds);
        var gaussians = new AquariumTemporalSdfGaussian[snapshot.Samples.Count];

        for (var index = 0; index < snapshot.Samples.Count; index++)
        {
            var sample = snapshot.Samples[index];
            gaussians[index] = new AquariumTemporalSdfGaussian(
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
                sample.Payload1.X,
                sample.Payload1.Y,
                sample.FieldId);
        }

        return new AquariumTemporalGaussianField
        {
            Gaussians = gaussians,
            AccumulationWindowSeconds = AccumulationWindowSeconds,
            PresentationDelaySeconds = PresentationDelaySeconds,
        };
    }
}
