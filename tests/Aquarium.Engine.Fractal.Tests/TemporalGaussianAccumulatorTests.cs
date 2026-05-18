using System.Numerics;
using Aquarium.Engine.Fractal.Temporal;

namespace Aquarium.Engine.Fractal.Tests;

public sealed class TemporalGaussianAccumulatorTests
{
    [Fact]
    public void AccumulatorPredictsBufferedMotionAndExposesHistoryWeight()
    {
        var accumulator = new TemporalGaussianAccumulator(accumulationWindowSeconds: 2.0f, presentationDelaySeconds: 0.5f, smoothing: 1.0f);
        accumulator.Observe([
            Observation("ball", new Vector3(0.0f, 0.0f, 1.0f), time: 0.0f),
            Observation("ball", new Vector3(1.0f, 0.0f, 1.0f), time: 1.0f),
        ]);

        var field = accumulator.BuildField(renderTimeSeconds: 1.5f);
        var gaussian = Assert.Single(field.Gaussians);

        Assert.Equal("ball", gaussian.StableKey);
        Assert.Equal(new Vector3(1.0f, 0.0f, 1.0f), gaussian.Center);
        Assert.Equal(new Vector3(1.0f, 0.0f, 0.0f), gaussian.Velocity);
        Assert.InRange(gaussian.HistoryWeight, 0.99f, 1.0f);
    }

    [Fact]
    public void AccumulatorExpiresSamplesOutsideWindow()
    {
        var accumulator = new TemporalGaussianAccumulator(accumulationWindowSeconds: 1.0f, presentationDelaySeconds: 0.0f);
        accumulator.Observe([Observation("old", Vector3.Zero, time: 0.0f)]);

        var field = accumulator.BuildField(renderTimeSeconds: 2.0f);

        Assert.Empty(field.Gaussians);
    }

    [Fact]
    public void KernelIsCompactAnisotropicAndSdfLike()
    {
        var accumulator = new TemporalGaussianAccumulator(accumulationWindowSeconds: 2.0f, presentationDelaySeconds: 0.0f);
        accumulator.Observe([Observation("brush", Vector3.Zero, time: 0.0f, radii: new Vector3(2.0f, 1.0f, 1.0f))]);
        var gaussian = Assert.Single(accumulator.BuildField(0.0f).Gaussians);

        Assert.Equal(1.0f, TemporalSdfGaussianKernel.CompactWeight(gaussian, Vector3.Zero), 6);
        Assert.Equal(0.0f, TemporalSdfGaussianKernel.CompactWeight(gaussian, new Vector3(2.0f, 0.0f, 0.0f)), 6);
        Assert.True(TemporalSdfGaussianKernel.CompactWeight(gaussian, new Vector3(1.5f, 0.0f, 0.0f)) > 0.0f);
        Assert.True(TemporalSdfGaussianKernel.CompactWeight(gaussian, new Vector3(0.0f, 1.5f, 0.0f)) == 0.0f);
        Assert.True(TemporalSdfGaussianKernel.SignedDistance(gaussian, Vector3.Zero) < 0.0f);
        Assert.Equal(0.0f, TemporalSdfGaussianKernel.SignedDistance(gaussian, new Vector3(2.0f, 0.0f, 0.0f)), 5);
    }

    private static TemporalGaussianObservation Observation(
        string key,
        Vector3 center,
        float time,
        Vector3? radii = null)
    {
        return new TemporalGaussianObservation(
            key,
            center,
            radii ?? new Vector3(0.1f),
            Quaternion.Identity,
            new Vector4(1.0f),
            Confidence: 1.0f,
            ObservedTimeSeconds: time,
            FieldId: 12);
    }
}
