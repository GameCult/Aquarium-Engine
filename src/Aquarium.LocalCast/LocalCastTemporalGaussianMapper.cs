using System.Numerics;
using Aquarium.Engine.Fractal.Temporal;

namespace Aquarium.LocalCast;

public sealed class LocalCastTemporalGaussianMapper
{
    private const float NanosecondsToSeconds = 1.0e-9f;
    private long? originNs;

    public long OriginNs => originNs ?? 0;

    public float ToTimelineSeconds(long timestampNs)
    {
        originNs ??= timestampNs;
        return MathF.Max(0.0f, (timestampNs - originNs.Value) * NanosecondsToSeconds);
    }

    public IReadOnlyList<TemporalGaussianObservation> Map(LocalCastVisualFrame frame)
    {
        var frameSeconds = ToTimelineSeconds(frame.SourceTimeMaxNs);
        var observations = new TemporalGaussianObservation[frame.Points.Count];
        for (var index = 0; index < observations.Length; index++)
        {
            var point = frame.Points[index];
            var observedSeconds = point.SourceTimestampNs > 0
                ? ToTimelineSeconds(point.SourceTimestampNs)
                : frameSeconds;
            var radius = Math.Clamp(point.RadiusMeters, 0.008f, 0.09f);
            var jitter = StochasticJitter(point.StableKey, frame.FrameId, radius * 0.28f);
            var color = SanitizeColor(point.ColorOpacity, point.Confidence);

            observations[index] = new TemporalGaussianObservation(
                point.StableKey,
                point.Position + jitter,
                new Vector3(radius * 2.15f, radius * 1.55f, radius * 1.85f),
                Quaternion.Identity,
                color,
                point.Confidence,
                observedSeconds,
                Falloff: 4.6f,
                ShapePower: 1.35f,
                FieldId: 0);
        }

        return observations;
    }

    private static Vector4 SanitizeColor(Vector4 color, float confidence)
    {
        var alpha = Math.Clamp(color.W * MathF.Sqrt(Math.Clamp(confidence, 0.0f, 1.0f)), 0.0f, 0.98f);
        return new Vector4(
            ToneUp(color.X),
            ToneUp(color.Y),
            ToneUp(color.Z),
            alpha);
    }

    private static float ToneUp(float value)
    {
        return Math.Clamp(MathF.Pow(Math.Clamp(value, 0.0f, 1.0f), 0.72f) * 1.65f, 0.0f, 2.25f);
    }

    private static Vector3 StochasticJitter(string stableKey, long frameId, float amplitude)
    {
        var seed = StableHash(stableKey, frameId);
        var x = Unit(seed) * 2.0f - 1.0f;
        var y = Unit(seed * 1664525u + 1013904223u) * 2.0f - 1.0f;
        var z = Unit(seed * 22695477u + 1u) * 2.0f - 1.0f;
        var vector = new Vector3(x, y, z);
        if (vector.LengthSquared() <= 0.000001f)
        {
            return Vector3.Zero;
        }

        return Vector3.Normalize(vector) * amplitude;
    }

    private static uint StableHash(string stableKey, long frameId)
    {
        const uint fnvOffset = 2166136261u;
        const uint fnvPrime = 16777619u;
        var hash = fnvOffset;
        foreach (var character in stableKey)
        {
            hash ^= character;
            hash *= fnvPrime;
        }

        hash ^= (uint)frameId;
        hash *= fnvPrime;
        hash ^= (uint)(frameId >> 32);
        hash *= fnvPrime;
        return hash;
    }

    private static float Unit(uint seed)
    {
        return (seed & 0x00ffffff) / 16777215.0f;
    }
}
