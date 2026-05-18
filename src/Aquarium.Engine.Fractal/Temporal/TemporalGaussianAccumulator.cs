using System.Numerics;
using Aquarium.Engine.Render;

namespace Aquarium.Engine.Fractal.Temporal;

public sealed class TemporalGaussianAccumulator
{
    private readonly Dictionary<string, TemporalGaussianTrack> tracks = [];

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

        AccumulationWindowSeconds = accumulationWindowSeconds;
        PresentationDelaySeconds = presentationDelaySeconds;
        Smoothing = smoothing;
    }

    public float AccumulationWindowSeconds { get; }

    public float PresentationDelaySeconds { get; }

    public float Smoothing { get; }

    public void Observe(IEnumerable<TemporalGaussianObservation> observations)
    {
        foreach (var observation in observations)
        {
            if (string.IsNullOrWhiteSpace(observation.StableKey))
            {
                continue;
            }

            if (tracks.TryGetValue(observation.StableKey, out var current))
            {
                tracks[observation.StableKey] = current.Update(observation, Smoothing);
            }
            else
            {
                tracks[observation.StableKey] = TemporalGaussianTrack.Create(observation);
            }
        }
    }

    public AquariumTemporalGaussianField BuildField(float renderTimeSeconds)
    {
        var presentationTime = renderTimeSeconds - PresentationDelaySeconds;
        var cutoff = presentationTime - AccumulationWindowSeconds;
        var gaussians = new List<AquariumTemporalSdfGaussian>(tracks.Count);
        var expired = new List<string>();

        foreach (var (key, track) in tracks)
        {
            if (track.LastObservedTimeSeconds < cutoff)
            {
                expired.Add(key);
                continue;
            }

            var age = MathF.Max(0.0f, presentationTime - track.LastObservedTimeSeconds);
            var historyWeight = Math.Clamp(1.0f - (age / AccumulationWindowSeconds), 0.0f, 1.0f) * track.Confidence;
            var predictedCenter = track.Center + track.Velocity * (presentationTime - track.LastObservedTimeSeconds);
            var previousCenter = predictedCenter - track.Velocity * (1.0f / 60.0f);
            gaussians.Add(new AquariumTemporalSdfGaussian(
                track.StableKey,
                predictedCenter,
                previousCenter,
                track.Velocity,
                track.Radii,
                track.Orientation,
                track.ColorOpacity,
                track.Confidence,
                historyWeight,
                track.LastObservedTimeSeconds,
                track.Falloff,
                track.ShapePower,
                track.FieldId));
        }

        foreach (var key in expired)
        {
            tracks.Remove(key);
        }

        return new AquariumTemporalGaussianField
        {
            Gaussians = gaussians.OrderByDescending(item => item.HistoryWeight).ToArray(),
            AccumulationWindowSeconds = AccumulationWindowSeconds,
            PresentationDelaySeconds = PresentationDelaySeconds,
        };
    }

    private readonly record struct TemporalGaussianTrack(
        string StableKey,
        Vector3 Center,
        Vector3 Velocity,
        Vector3 Radii,
        Quaternion Orientation,
        Vector4 ColorOpacity,
        float Confidence,
        float LastObservedTimeSeconds,
        float Falloff,
        float ShapePower,
        int FieldId)
    {
        public static TemporalGaussianTrack Create(TemporalGaussianObservation observation)
        {
            return new TemporalGaussianTrack(
                observation.StableKey,
                observation.Center,
                Vector3.Zero,
                SanitizeRadii(observation.Radii),
                SanitizeOrientation(observation.Orientation),
                observation.ColorOpacity,
                Math.Clamp(observation.Confidence, 0.0f, 1.0f),
                observation.ObservedTimeSeconds,
                MathF.Max(0.0001f, observation.Falloff),
                MathF.Max(0.0001f, observation.ShapePower),
                observation.FieldId);
        }

        public TemporalGaussianTrack Update(TemporalGaussianObservation observation, float smoothing)
        {
            var dt = MathF.Max(0.0001f, observation.ObservedTimeSeconds - LastObservedTimeSeconds);
            var measuredVelocity = (observation.Center - Center) / dt;
            var alpha = smoothing * Math.Clamp(observation.Confidence, 0.0f, 1.0f);
            var updatedCenter = Vector3.Lerp(Center, observation.Center, alpha);
            return this with
            {
                Center = updatedCenter,
                Velocity = Vector3.Lerp(Velocity, measuredVelocity, alpha),
                Radii = Vector3.Lerp(Radii, SanitizeRadii(observation.Radii), alpha),
                Orientation = SanitizeOrientation(Quaternion.Slerp(Orientation, SanitizeOrientation(observation.Orientation), alpha)),
                ColorOpacity = Vector4.Lerp(ColorOpacity, observation.ColorOpacity, alpha),
                Confidence = Math.Clamp((Confidence * 0.85f) + (observation.Confidence * 0.15f), 0.0f, 1.0f),
                LastObservedTimeSeconds = observation.ObservedTimeSeconds,
                Falloff = MathF.Max(0.0001f, observation.Falloff),
                ShapePower = MathF.Max(0.0001f, observation.ShapePower),
                FieldId = observation.FieldId,
            };
        }

        private static Vector3 SanitizeRadii(Vector3 radii)
        {
            return Vector3.Max(radii, new Vector3(0.0001f));
        }

        private static Quaternion SanitizeOrientation(Quaternion orientation)
        {
            return orientation.LengthSquared() <= 0.000001f
                ? Quaternion.Identity
                : Quaternion.Normalize(orientation);
        }
    }
}
