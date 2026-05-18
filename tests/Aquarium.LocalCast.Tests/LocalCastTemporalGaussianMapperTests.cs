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
}
