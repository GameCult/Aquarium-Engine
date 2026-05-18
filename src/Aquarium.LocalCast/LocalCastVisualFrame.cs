using System.Numerics;

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
}

public readonly record struct LocalCastVisualPoint(
    string StableKey,
    Vector3 Position,
    float RadiusMeters,
    Vector4 ColorOpacity,
    float Confidence,
    long SourceTimestampNs);
