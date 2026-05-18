using System.Numerics;
using Aquarium.Engine.Render;

namespace Aquarium.LocalCast;

public sealed class LocalCastGpuFusionAccumulator
{
    private readonly Dictionary<string, Track> tracks = new(StringComparer.Ordinal);
    private readonly LocalCastGpuFusionMapper mapper = new();
    private readonly float historySeconds;
    private readonly float smoothing;
    private readonly int maxSeedCount;

    public LocalCastGpuFusionAccumulator(float historySeconds, float smoothing = 0.45f, int maxSeedCount = 1_048_576)
    {
        if (!float.IsFinite(historySeconds) || historySeconds <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(historySeconds), historySeconds, "History window must be positive.");
        }

        if (!float.IsFinite(smoothing) || smoothing <= 0.0f || smoothing > 1.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(smoothing), smoothing, "Smoothing must be in (0, 1].");
        }

        this.historySeconds = historySeconds;
        this.smoothing = smoothing;
        this.maxSeedCount = Math.Max(1, maxSeedCount);
    }

    public int TrackCount => tracks.Count;

    public void Observe(LocalCastVisualFrame frame)
    {
        var field = mapper.Map(frame);
        var frameTimeSeconds = mapper.ToTimelineSeconds(frame.SourceTimeMaxNs);
        foreach (var seed in field.Seeds)
        {
            if (string.IsNullOrWhiteSpace(seed.StableKey))
            {
                continue;
            }

            if (tracks.TryGetValue(seed.StableKey, out var current))
            {
                tracks[seed.StableKey] = current.Update(seed, frameTimeSeconds, smoothing);
            }
            else
            {
                tracks[seed.StableKey] = Track.Create(seed, frameTimeSeconds);
            }
        }
    }

    public AquariumGpuFusionField BuildField(float renderTimeSeconds)
    {
        var cutoff = renderTimeSeconds - historySeconds;
        var expired = new List<string>();
        var seeds = new List<AquariumGpuFusionSeed>(Math.Min(tracks.Count, maxSeedCount));

        foreach (var (key, track) in tracks)
        {
            if (track.LastObservedTimeSeconds < cutoff)
            {
                expired.Add(key);
                continue;
            }

            var age = MathF.Max(0.0f, renderTimeSeconds - track.LastObservedTimeSeconds);
            var retained = Math.Clamp(1.0f - (age / historySeconds), 0.0f, 1.0f);
            var predictedCenter = track.Seed.Center + track.Seed.Velocity * age;
            var previousCenter = predictedCenter - track.Seed.Velocity * (1.0f / 60.0f);
            seeds.Add(track.Seed with
            {
                Center = predictedCenter,
                PreviousCenter = previousCenter,
                HistoryWeight = Math.Clamp(track.Seed.HistoryWeight * retained, 0.0f, 1.0f),
            });
        }

        foreach (var key in expired)
        {
            tracks.Remove(key);
        }

        return new AquariumGpuFusionField
        {
            Seeds = seeds
                .OrderByDescending(seed => seed.HistoryWeight)
                .Take(maxSeedCount)
                .ToArray(),
            AccumulationWindowSeconds = historySeconds,
            PresentationDelaySeconds = 0.35f,
        };
    }

    private readonly record struct Track(AquariumGpuFusionSeed Seed, float LastObservedTimeSeconds)
    {
        public static Track Create(AquariumGpuFusionSeed seed, float observedTimeSeconds)
        {
            return new Track(seed, observedTimeSeconds);
        }

        public Track Update(AquariumGpuFusionSeed seed, float observedTimeSeconds, float smoothing)
        {
            var dt = MathF.Max(0.0001f, observedTimeSeconds - LastObservedTimeSeconds);
            var measuredVelocity = (seed.Center - Seed.Center) / dt;
            var confidence = Math.Clamp(seed.Confidence, 0.0f, 1.0f);
            var alpha = smoothing * confidence;
            var updatedCenter = Vector3.Lerp(Seed.Center, seed.Center, alpha);
            return new Track(
                Seed with
                {
                    Center = updatedCenter,
                    PreviousCenter = Seed.Center,
                    Velocity = Vector3.Lerp(Seed.Velocity, measuredVelocity, alpha),
                    Radii = Vector3.Lerp(Seed.Radii, seed.Radii, alpha),
                    ColorOpacity = Vector4.Lerp(Seed.ColorOpacity, seed.ColorOpacity, alpha),
                    Confidence = Math.Clamp((Seed.Confidence * 0.85f) + (confidence * 0.15f), 0.0f, 1.0f),
                    HistoryWeight = Math.Clamp((Seed.HistoryWeight * 0.75f) + (confidence * 0.25f), 0.0f, 1.0f),
                    Falloff = MathF.Max(0.0001f, seed.Falloff),
                    ShapePower = MathF.Max(0.0001f, seed.ShapePower),
                },
                observedTimeSeconds);
        }
    }
}
