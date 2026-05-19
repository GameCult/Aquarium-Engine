using System.Numerics;

namespace Aquarium.Engine.Fractal.Temporal;

public sealed class TemporalSpatialEvidenceReservoir
{
    private readonly Dictionary<string, TemporalSpatialEvidenceTrack> tracks = [];

    public TemporalSpatialEvidenceReservoir(
        float accumulationWindowSeconds,
        float presentationDelaySeconds,
        float smoothing = 0.35f,
        int maxTrackCount = int.MaxValue)
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

        if (maxTrackCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxTrackCount), maxTrackCount, "Track budget must be positive.");
        }

        AccumulationWindowSeconds = accumulationWindowSeconds;
        PresentationDelaySeconds = presentationDelaySeconds;
        Smoothing = smoothing;
        MaxTrackCount = maxTrackCount;
    }

    public float AccumulationWindowSeconds { get; }

    public float PresentationDelaySeconds { get; }

    public float Smoothing { get; }

    public int MaxTrackCount { get; }

    public int TrackCount => tracks.Count;

    public void Observe(IEnumerable<TemporalSpatialEvidenceObservation> observations)
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
                tracks[observation.StableKey] = TemporalSpatialEvidenceTrack.Create(observation);
            }
        }

        TrimToBudget();
    }

    public TemporalSpatialEvidenceSnapshot BuildSnapshot(float renderTimeSeconds)
    {
        var presentationTime = renderTimeSeconds - PresentationDelaySeconds;
        var cutoff = presentationTime - AccumulationWindowSeconds;
        var samples = new List<TemporalSpatialEvidenceSample>(tracks.Count);
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
            samples.Add(new TemporalSpatialEvidenceSample(
                track.StableKey,
                predictedCenter,
                previousCenter,
                track.Velocity,
                track.Radii,
                track.Orientation,
                track.Payload0,
                track.Payload1,
                track.Confidence,
                historyWeight,
                track.LastObservedTimeSeconds,
                track.FieldId));
        }

        foreach (var key in expired)
        {
            tracks.Remove(key);
        }

        return new TemporalSpatialEvidenceSnapshot(
            samples.OrderByDescending(item => item.HistoryWeight).ToArray(),
            AccumulationWindowSeconds,
            PresentationDelaySeconds);
    }

    private void TrimToBudget()
    {
        if (tracks.Count <= MaxTrackCount)
        {
            return;
        }

        var evict = tracks.Values
            .OrderBy(track => track.Confidence)
            .ThenBy(track => track.LastObservedTimeSeconds)
            .Take(tracks.Count - MaxTrackCount)
            .Select(track => track.StableKey)
            .ToArray();

        foreach (var key in evict)
        {
            tracks.Remove(key);
        }
    }

    private readonly record struct TemporalSpatialEvidenceTrack(
        string StableKey,
        Vector3 Center,
        Vector3 Velocity,
        Vector3 Radii,
        Quaternion Orientation,
        Vector4 Payload0,
        Vector4 Payload1,
        float Confidence,
        float LastObservedTimeSeconds,
        int FieldId)
    {
        public static TemporalSpatialEvidenceTrack Create(TemporalSpatialEvidenceObservation observation)
        {
            return new TemporalSpatialEvidenceTrack(
                observation.StableKey,
                observation.Center,
                Vector3.Zero,
                SanitizeRadii(observation.Radii),
                SanitizeOrientation(observation.Orientation),
                observation.Payload0,
                observation.Payload1,
                Math.Clamp(observation.Confidence, 0.0f, 1.0f),
                observation.ObservedTimeSeconds,
                observation.FieldId);
        }

        public TemporalSpatialEvidenceTrack Update(TemporalSpatialEvidenceObservation observation, float smoothing)
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
                Payload0 = Vector4.Lerp(Payload0, observation.Payload0, alpha),
                Payload1 = Vector4.Lerp(Payload1, observation.Payload1, alpha),
                Confidence = Math.Clamp((Confidence * 0.85f) + (observation.Confidence * 0.15f), 0.0f, 1.0f),
                LastObservedTimeSeconds = observation.ObservedTimeSeconds,
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
