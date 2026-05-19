using System.Numerics;
using Aquarium.Engine.Render;

namespace Aquarium.LocalCast;

public sealed class LocalCastVisualFrame
{
    public required string SchemaVersion { get; init; }

    public required long FrameId { get; init; }

    public required long CreatedMonotonicNs { get; init; }

    public required long SourceTimeMinNs { get; init; }

    public required long SourceTimeMaxNs { get; init; }

    public required long PresentTimeNs { get; init; }

    public required long AudioAlignmentTimeNs { get; init; }

    public required string SpoutSenderName { get; init; }

    public required int TargetWidth { get; init; }

    public required int TargetHeight { get; init; }

    public required IReadOnlyList<LocalCastVisualPoint> Points { get; init; }

    public AquariumGpuFusionPointBuffer NativeGpuFusionPointBuffer { get; init; }

    public int PointCount => NativeGpuFusionPointBuffer.HasInput ? NativeGpuFusionPointBuffer.Count : Points.Count;
}

public readonly record struct LocalCastVisualPoint(
    string StableKey,
    Vector3 Position,
    float RadiusMeters,
    Vector4 ColorOpacity,
    float Confidence,
    long SourceTimestampNs);

public sealed class LocalCastClapCalibrationFrame
{
    public required string SchemaVersion { get; init; }

    public required long FrameId { get; init; }

    public required long CreatedMonotonicNs { get; init; }

    public required IReadOnlyList<LocalCastClapCalibrationEvent> Events { get; init; }
}

public readonly record struct LocalCastClapCalibrationEvent(
    string StableKey,
    Vector3 PositionMeters,
    long AcousticOracleNs,
    long VisualObservedNs,
    float TimingUncertaintyMicroseconds,
    float VisualConfidence,
    float AcousticConfidence);
