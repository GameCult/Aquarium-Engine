using System.Numerics;
using Aquarium.Engine.Fractal.Temporal;
using Aquarium.Engine.Render;

namespace Aquarium.LocalCast;

public sealed class LocalCastGpuFusionAccumulator
{
    private const float PresentationDelaySeconds = 0.35f;

    private readonly TemporalSpatialEvidenceReservoir reservoir;
    private readonly LocalCastGpuFusionMapper mapper = new();
    private readonly float historySeconds;

    public LocalCastGpuFusionAccumulator(float historySeconds, float smoothing = 0.45f, int maxSeedCount = 1_048_576)
    {
        this.historySeconds = historySeconds;
        reservoir = new TemporalSpatialEvidenceReservoir(
            historySeconds,
            PresentationDelaySeconds,
            smoothing,
            Math.Max(1, maxSeedCount));
    }

    public int TrackCount => reservoir.TrackCount;

    public void Observe(LocalCastVisualFrame frame)
    {
        var field = mapper.Map(frame);
        var frameTimeSeconds = mapper.ToTimelineSeconds(frame.SourceTimeMaxNs);
        reservoir.Observe(field.Seeds.Select(seed => ToObservation(seed, frameTimeSeconds)));
    }

    public AquariumGpuFusionField BuildField(float renderTimeSeconds)
    {
        var snapshot = reservoir.BuildSnapshot(renderTimeSeconds);

        return new AquariumGpuFusionField
        {
            Seeds = snapshot.Samples.Select(ToSeed).ToArray(),
            AccumulationWindowSeconds = historySeconds,
            PresentationDelaySeconds = PresentationDelaySeconds,
        };
    }

    private static TemporalSpatialEvidenceObservation ToObservation(AquariumGpuFusionSeed seed, float frameTimeSeconds)
    {
        return new TemporalSpatialEvidenceObservation(
            seed.StableKey,
            seed.Center,
            seed.Radii,
            Quaternion.Identity,
            seed.ColorOpacity,
            new Vector4(
                MathF.Max(0.0001f, seed.Falloff),
                MathF.Max(0.0001f, seed.ShapePower),
                seed.HistoryWeight,
                0.0f),
            seed.Confidence,
            frameTimeSeconds,
            seed.FieldId);
    }

    private static AquariumGpuFusionSeed ToSeed(TemporalSpatialEvidenceSample sample)
    {
        return new AquariumGpuFusionSeed(
            sample.StableKey,
            sample.Center,
            sample.PreviousCenter,
            sample.Velocity,
            sample.Radii,
            sample.Payload0,
            sample.Confidence,
            sample.HistoryWeight,
            MathF.Max(0.0001f, sample.Payload1.X),
            MathF.Max(0.0001f, sample.Payload1.Y),
            sample.FieldId);
    }
}
