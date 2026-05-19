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
        reservoir.Observe(field.Seeds.Select(seed => TemporalSpatialEvidenceLowering.FromGpuFusionSeed(seed, frameTimeSeconds)));
    }

    public AquariumGpuFusionField BuildField(float renderTimeSeconds)
    {
        var snapshot = reservoir.BuildSnapshot(renderTimeSeconds);

        return new AquariumGpuFusionField
        {
            Seeds = snapshot.Samples.Select(TemporalSpatialEvidenceLowering.ToGpuFusionSeed).ToArray(),
            AccumulationWindowSeconds = historySeconds,
            PresentationDelaySeconds = PresentationDelaySeconds,
        };
    }
}
