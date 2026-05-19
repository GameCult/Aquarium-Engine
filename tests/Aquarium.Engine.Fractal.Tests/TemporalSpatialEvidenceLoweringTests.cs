using System.Numerics;
using Aquarium.Engine.Fractal.Temporal;
using Aquarium.Engine.Render;

namespace Aquarium.Engine.Fractal.Tests;

public sealed class TemporalSpatialEvidenceLoweringTests
{
    [Fact]
    public void GaussianLoweringRoundTripsKernelPayload()
    {
        var observation = new TemporalGaussianObservation(
            "gaussian/a",
            Vector3.One,
            new Vector3(0.2f, 0.3f, 0.4f),
            Quaternion.Identity,
            new Vector4(0.1f, 0.2f, 0.3f, 0.4f),
            Confidence: 0.75f,
            ObservedTimeSeconds: 12.0f,
            Falloff: 5.0f,
            ShapePower: 1.8f,
            FieldId: 44);

        var lowered = TemporalSpatialEvidenceLowering.FromGaussianObservation(observation);
        var sample = new TemporalSpatialEvidenceSample(
            lowered.StableKey,
            lowered.Center,
            lowered.Center,
            Vector3.Zero,
            lowered.Radii,
            lowered.Orientation,
            lowered.Payload0,
            lowered.Payload1,
            lowered.Confidence,
            HistoryWeight: 0.6f,
            lowered.ObservedTimeSeconds,
            lowered.FieldId);
        var gaussian = TemporalSpatialEvidenceLowering.ToTemporalGaussian(sample);

        Assert.Equal(observation.StableKey, gaussian.StableKey);
        Assert.Equal(observation.ColorOpacity, gaussian.ColorOpacity);
        Assert.Equal(5.0f, gaussian.Falloff);
        Assert.Equal(1.8f, gaussian.ShapePower);
        Assert.Equal(44, gaussian.FieldId);
    }

    [Fact]
    public void GpuFusionLoweringRoundTripsSeedPayload()
    {
        var seed = new AquariumGpuFusionSeed(
            "fusion/a",
            Vector3.One,
            Vector3.Zero,
            new Vector3(0.1f, 0.0f, 0.0f),
            new Vector3(0.2f),
            new Vector4(0.4f, 0.5f, 0.6f, 0.7f),
            Confidence: 0.8f,
            HistoryWeight: 0.65f,
            Falloff: 4.4f,
            ShapePower: 2.1f,
            FieldId: 91);

        var lowered = TemporalSpatialEvidenceLowering.FromGpuFusionSeed(seed, observedTimeSeconds: 3.0f);
        var sample = new TemporalSpatialEvidenceSample(
            lowered.StableKey,
            lowered.Center,
            seed.PreviousCenter,
            seed.Velocity,
            lowered.Radii,
            lowered.Orientation,
            lowered.Payload0,
            lowered.Payload1,
            lowered.Confidence,
            lowered.Payload1.Z,
            lowered.ObservedTimeSeconds,
            lowered.FieldId);
        var roundTrip = TemporalSpatialEvidenceLowering.ToGpuFusionSeed(sample);

        Assert.Equal(seed.StableKey, roundTrip.StableKey);
        Assert.Equal(seed.ColorOpacity, roundTrip.ColorOpacity);
        Assert.Equal(seed.Falloff, roundTrip.Falloff);
        Assert.Equal(seed.ShapePower, roundTrip.ShapePower);
        Assert.Equal(seed.FieldId, roundTrip.FieldId);
    }
}
