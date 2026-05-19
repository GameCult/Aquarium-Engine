using System.Numerics;
using Aquarium.Engine.Fractal.Temporal;

namespace Aquarium.Engine.Fractal.Tests;

public sealed class TemporalSpatialEvidenceReservoirTests
{
    [Fact]
    public void ReservoirPredictsDelayedMotionForStableSpatialEvidence()
    {
        var reservoir = new TemporalSpatialEvidenceReservoir(
            accumulationWindowSeconds: 2.0f,
            presentationDelaySeconds: 0.5f,
            smoothing: 1.0f);

        reservoir.Observe([
            Observation("feature/a", Vector3.Zero, time: 0.0f),
            Observation("feature/a", new Vector3(1.0f, 0.0f, 0.0f), time: 1.0f),
        ]);

        var sample = Assert.Single(reservoir.BuildSnapshot(renderTimeSeconds: 1.5f).Samples);

        Assert.Equal(new Vector3(1.0f, 0.0f, 0.0f), sample.Center);
        Assert.Equal(new Vector3(1.0f, 0.0f, 0.0f), sample.Velocity);
        Assert.InRange(sample.HistoryWeight, 0.99f, 1.0f);
    }

    [Fact]
    public void ReservoirExpiresSamplesOutsideWindow()
    {
        var reservoir = new TemporalSpatialEvidenceReservoir(
            accumulationWindowSeconds: 1.0f,
            presentationDelaySeconds: 0.0f);
        reservoir.Observe([Observation("feature/old", Vector3.Zero, time: 0.0f)]);

        var snapshot = reservoir.BuildSnapshot(renderTimeSeconds: 2.0f);

        Assert.Empty(snapshot.Samples);
    }

    [Fact]
    public void ReservoirEvictsLowestConfidenceTracksWhenOverBudget()
    {
        var reservoir = new TemporalSpatialEvidenceReservoir(
            accumulationWindowSeconds: 5.0f,
            presentationDelaySeconds: 0.0f,
            smoothing: 1.0f,
            maxTrackCount: 2);

        reservoir.Observe([
            Observation("weak", Vector3.Zero, time: 0.0f, confidence: 0.2f),
            Observation("strong", Vector3.One, time: 0.0f, confidence: 1.0f),
            Observation("middle", new Vector3(2.0f), time: 0.0f, confidence: 0.6f),
        ]);

        var keys = reservoir.BuildSnapshot(renderTimeSeconds: 0.0f).Samples.Select(sample => sample.StableKey).ToArray();

        Assert.DoesNotContain("weak", keys);
        Assert.Contains("strong", keys);
        Assert.Contains("middle", keys);
    }

    [Fact]
    public void ReservoirPreservesRendererSpecificPayloadsWithoutOwningThem()
    {
        var reservoir = new TemporalSpatialEvidenceReservoir(
            accumulationWindowSeconds: 5.0f,
            presentationDelaySeconds: 0.0f);
        var payload0 = new Vector4(0.2f, 0.4f, 0.6f, 0.8f);
        var payload1 = new Vector4(4.0f, 1.5f, 0.0f, 0.0f);

        reservoir.Observe([Observation("feature/payload", Vector3.Zero, time: 0.0f, payload0: payload0, payload1: payload1)]);

        var sample = Assert.Single(reservoir.BuildSnapshot(renderTimeSeconds: 0.0f).Samples);
        Assert.Equal(payload0, sample.Payload0);
        Assert.Equal(payload1, sample.Payload1);
    }

    private static TemporalSpatialEvidenceObservation Observation(
        string key,
        Vector3 center,
        float time,
        float confidence = 1.0f,
        Vector4? payload0 = null,
        Vector4? payload1 = null)
    {
        return new TemporalSpatialEvidenceObservation(
            key,
            center,
            new Vector3(0.1f),
            Quaternion.Identity,
            payload0 ?? Vector4.One,
            payload1 ?? Vector4.Zero,
            confidence,
            time,
            FieldId: 3);
    }
}
