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
        reservoir.Observe(observations.Select(TemporalSpatialEvidenceLowering.FromGaussianObservation));
    }

    public AquariumTemporalGaussianField BuildField(float renderTimeSeconds)
    {
        var snapshot = reservoir.BuildSnapshot(renderTimeSeconds);
        var gaussians = new AquariumTemporalSdfGaussian[snapshot.Samples.Count];

        for (var index = 0; index < snapshot.Samples.Count; index++)
        {
            gaussians[index] = TemporalSpatialEvidenceLowering.ToTemporalGaussian(snapshot.Samples[index]);
        }

        return new AquariumTemporalGaussianField
        {
            Gaussians = gaussians,
            AccumulationWindowSeconds = AccumulationWindowSeconds,
            PresentationDelaySeconds = PresentationDelaySeconds,
        };
    }
}
