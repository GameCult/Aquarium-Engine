using System.Numerics;

namespace Aquarium.Engine.Fractal.Temporal;

public readonly record struct TemporalSpatialEvidenceObservation(
    string StableKey,
    Vector3 Center,
    Vector3 Radii,
    Quaternion Orientation,
    Vector4 Payload0,
    Vector4 Payload1,
    float Confidence,
    float ObservedTimeSeconds,
    int FieldId = 0);

public readonly record struct TemporalSpatialEvidenceSample(
    string StableKey,
    Vector3 Center,
    Vector3 PreviousCenter,
    Vector3 Velocity,
    Vector3 Radii,
    Quaternion Orientation,
    Vector4 Payload0,
    Vector4 Payload1,
    float Confidence,
    float HistoryWeight,
    float LastObservedTimeSeconds,
    int FieldId);

public sealed class TemporalSpatialEvidenceSnapshot
{
    public TemporalSpatialEvidenceSnapshot(
        IReadOnlyList<TemporalSpatialEvidenceSample> samples,
        float accumulationWindowSeconds,
        float presentationDelaySeconds)
    {
        Samples = samples;
        AccumulationWindowSeconds = accumulationWindowSeconds;
        PresentationDelaySeconds = presentationDelaySeconds;
    }

    public IReadOnlyList<TemporalSpatialEvidenceSample> Samples { get; }

    public float AccumulationWindowSeconds { get; }

    public float PresentationDelaySeconds { get; }
}
