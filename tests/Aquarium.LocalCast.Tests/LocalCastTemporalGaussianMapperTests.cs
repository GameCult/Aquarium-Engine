using System.Numerics;
using Aquarium.LocalCast;

namespace Aquarium.LocalCast.Tests;

public sealed class LocalCastTemporalGaussianMapperTests
{
    [Fact]
    public void MapsRenderPointsToTemporalGaussianObservations()
    {
        var frame = new LocalCastVisualFrame
        {
            SchemaVersion = LocalCastVisualStateReader.RenderFrameSchemaId,
            FrameId = 42,
            CreatedMonotonicNs = 1_000_000_000,
            SourceTimeMinNs = 1_000_000_000,
            SourceTimeMaxNs = 1_050_000_000,
            PresentTimeNs = 1_350_000_000,
            AudioAlignmentTimeNs = 1_350_000_000,
            SpoutSenderName = "LocalCastBridge Point Cloud",
            TargetWidth = 1920,
            TargetHeight = 1080,
            Points =
            [
                new LocalCastVisualPoint(
                    "host-rgb:0:0",
                    new Vector3(0.25f, 0.1f, 1.2f),
                    0.02f,
                    new Vector4(0.8f, 0.4f, 0.2f, 0.9f),
                    0.75f,
                    1_040_000_000)
            ],
        };

        var mapper = new LocalCastTemporalGaussianMapper();
        var observations = mapper.Map(frame);

        Assert.Single(observations);
        var observation = observations[0];
        Assert.Equal("host-rgb:0:0", observation.StableKey);
        Assert.True(observation.Center != frame.Points[0].Position);
        Assert.Equal(0.043f, observation.Radii.X, 3);
        Assert.InRange(observation.ColorOpacity.W, 0.0f, 0.98f);
        Assert.True(observation.ColorOpacity.X > frame.Points[0].ColorOpacity.X);
        Assert.Equal(0.75f, observation.Confidence, 6);
        Assert.True(observation.ShapePower > 1.0f);
    }

    [Fact]
    public void DoesNotDownsampleDenseCameraAndLeapClaimsInClientAdapter()
    {
        var points = new List<LocalCastVisualPoint>();
        for (var index = 0; index < 5000; index++)
        {
            points.Add(new LocalCastVisualPoint(
                index == 4500 ? "leap-motion:green:0:0" : $"room-rgb:{index}",
                new Vector3(index * 0.001f, 0.0f, 1.0f),
                0.02f,
                Vector4.One,
                0.8f,
                1_000_000_000 + index));
        }

        var mapper = new LocalCastTemporalGaussianMapper();
        var observations = mapper.Map(new LocalCastVisualFrame
        {
            SchemaVersion = LocalCastVisualStateReader.RenderFrameSchemaId,
            FrameId = 1,
            CreatedMonotonicNs = 1_000_000_000,
            SourceTimeMinNs = 1_000_000_000,
            SourceTimeMaxNs = 1_000_010_000,
            PresentTimeNs = 1_350_000_000,
            AudioAlignmentTimeNs = 1_350_000_000,
            SpoutSenderName = "LocalCastBridge Point Cloud",
            TargetWidth = 1920,
            TargetHeight = 1080,
            Points = points,
        });

        Assert.Equal(5000, observations.Count);
        Assert.Contains(observations, observation => observation.StableKey == "leap-motion:green:0:0");
        Assert.Contains(observations, observation => observation.StableKey.StartsWith("room-rgb:", StringComparison.Ordinal));
    }

    [Fact]
    public void MapsVisualPointsToGpuFusionSeedsWithoutClientDownsampling()
    {
        var points = new List<LocalCastVisualPoint>();
        for (var index = 0; index < 8192; index++)
        {
            points.Add(new LocalCastVisualPoint(
                index % 2 == 0 ? $"dense-rgb:{index}" : $"leap-motion:green:{index}",
                new Vector3(index * 0.0001f, 0.0f, 1.0f),
                0.006f,
                new Vector4(0.4f, 0.7f, 1.0f, 0.85f),
                0.9f,
                1_000_000_000 + index));
        }

        var field = new LocalCastGpuFusionMapper().Map(new LocalCastVisualFrame
        {
            SchemaVersion = LocalCastVisualStateReader.RenderFrameSchemaId,
            FrameId = 2,
            CreatedMonotonicNs = 1_000_000_000,
            SourceTimeMinNs = 1_000_000_000,
            SourceTimeMaxNs = 1_000_010_000,
            PresentTimeNs = 1_350_000_000,
            AudioAlignmentTimeNs = 1_350_000_000,
            SpoutSenderName = "LocalCastBridge Point Cloud",
            TargetWidth = 1920,
            TargetHeight = 1080,
            Points = points,
        });

        Assert.Equal(8192, field.Seeds.Count);
        Assert.Contains(field.Seeds, seed => seed.StableKey.StartsWith("dense-rgb:", StringComparison.Ordinal));
        Assert.Contains(field.Seeds, seed => seed.ShapePower > 2.0f);
    }

    [Fact]
    public void GpuFusionAccumulatorRetainsStableSamplesAcrossHistoryWindow()
    {
        var accumulator = new LocalCastGpuFusionAccumulator(historySeconds: 10.0f, smoothing: 1.0f, maxSeedCount: 16);
        accumulator.Observe(FrameWithPoint(1, "room-rgb:old", new Vector3(-0.2f, 0.0f, 1.0f), 1_000_000_000));
        accumulator.Observe(FrameWithPoint(2, "room-rgb:new", new Vector3(0.2f, 0.0f, 1.0f), 5_000_000_000));

        var field = accumulator.BuildField(6.0f);

        Assert.Equal(2, field.Seeds.Count);
        Assert.Contains(field.Seeds, seed => seed.StableKey == "room-rgb:old");
        Assert.Contains(field.Seeds, seed => seed.StableKey == "room-rgb:new");
    }

    [Fact]
    public void GpuFusionAccumulatorExpiresSamplesOutsideHistoryWindow()
    {
        var accumulator = new LocalCastGpuFusionAccumulator(historySeconds: 2.0f, smoothing: 1.0f, maxSeedCount: 16);
        accumulator.Observe(FrameWithPoint(1, "room-rgb:old", new Vector3(-0.2f, 0.0f, 1.0f), 1_000_000_000));
        accumulator.Observe(FrameWithPoint(2, "room-rgb:new", new Vector3(0.2f, 0.0f, 1.0f), 4_000_000_000));

        var field = accumulator.BuildField(4.2f);

        Assert.Single(field.Seeds);
        Assert.Equal("room-rgb:new", field.Seeds[0].StableKey);
    }

    private static LocalCastVisualFrame FrameWithPoint(long frameId, string key, Vector3 position, long sourceTimeNs)
    {
        return new LocalCastVisualFrame
        {
            SchemaVersion = LocalCastVisualStateReader.RenderFrameSchemaId,
            FrameId = frameId,
            CreatedMonotonicNs = sourceTimeNs,
            SourceTimeMinNs = sourceTimeNs,
            SourceTimeMaxNs = sourceTimeNs,
            PresentTimeNs = sourceTimeNs + 350_000_000,
            AudioAlignmentTimeNs = sourceTimeNs + 350_000_000,
            SpoutSenderName = "LocalCastBridge Point Cloud",
            TargetWidth = 1920,
            TargetHeight = 1080,
            Points =
            [
                new LocalCastVisualPoint(
                    key,
                    position,
                    0.012f,
                    new Vector4(0.5f, 0.7f, 0.9f, 0.85f),
                    0.9f,
                    sourceTimeNs)
            ],
        };
    }
}
