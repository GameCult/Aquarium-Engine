using System.Numerics;
using Aquarium.Engine.Render;

namespace Aquarium.LocalCast;

public sealed class LocalCastGpuFusionMapper
{
    private const float NanosecondsToSeconds = 1.0f / 1_000_000_000.0f;

    public AquariumGpuFusionField Map(LocalCastVisualFrame frame)
    {
        var seeds = new AquariumGpuFusionSeed[frame.Points.Count];
        for (var index = 0; index < frame.Points.Count; index++)
        {
            seeds[index] = MapPoint(frame.Points[index], index);
        }

        return new AquariumGpuFusionField
        {
            Seeds = seeds,
            AccumulationWindowSeconds = 2.75f,
            PresentationDelaySeconds = 0.35f,
        };
    }

    private static AquariumGpuFusionSeed MapPoint(LocalCastVisualPoint point, int fieldId)
    {
        var confidence = Math.Clamp(point.Confidence, 0.0f, 1.0f);
        var radius = Math.Clamp(point.RadiusMeters, 0.0015f, 0.09f);
        var isLeap = point.StableKey.StartsWith("leap-motion:", StringComparison.Ordinal);
        var isDense = point.StableKey.StartsWith("dense-rgb:", StringComparison.Ordinal);
        var anisotropy = isLeap ? 0.62f : isDense ? 0.42f : 0.55f;
        var radii = new Vector3(radius * 1.7f, radius * anisotropy, radius * 1.15f);
        var opacity = Math.Clamp(point.ColorOpacity.W * (0.35f + confidence * 0.65f), 0.0f, 0.98f);
        var color = new Vector4(
            Math.Clamp(point.ColorOpacity.X, 0.0f, 1.0f),
            Math.Clamp(point.ColorOpacity.Y, 0.0f, 1.0f),
            Math.Clamp(point.ColorOpacity.Z, 0.0f, 1.0f),
            opacity);

        return new AquariumGpuFusionSeed(
            point.StableKey,
            point.Position,
            point.Position,
            Vector3.Zero,
            radii,
            color,
            confidence,
            confidence,
            isDense ? 5.5f : 4.2f,
            isLeap ? 2.4f : 1.65f,
            StableFieldId(point.StableKey, fieldId));
    }

    public float ToTimelineSeconds(long monotonicNs)
    {
        return monotonicNs * NanosecondsToSeconds;
    }

    private static int StableFieldId(string key, int fallback)
    {
        unchecked
        {
            var hash = 17;
            foreach (var ch in key)
            {
                hash = (hash * 31) + ch;
            }

            return Math.Abs(hash == int.MinValue ? fallback : hash);
        }
    }
}
