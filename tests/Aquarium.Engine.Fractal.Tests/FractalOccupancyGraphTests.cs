using Aquarium.Engine.Fractal;
using Aquarium.Engine.Fractal.Lod;

namespace Aquarium.Engine.Fractal.Tests;

public sealed class FractalOccupancyGraphTests
{
    [Fact]
    public void OccupancyGraphRecordsVisibilityAndUpdateProbability()
    {
        var graph = new FractalOccupancyGraph();
        var key = new AquariumFractalKey("node/visible");

        graph.MarkVisible(key, observedScore: 3.5f, frameIndex: 12, updateProbability: 0.42);

        Assert.True(graph.TryGet(key, out var state));
        Assert.Equal<uint>(12, state.LastVisibleFrame);
        Assert.Equal(3.5f, state.LastObservedScore);
        Assert.Equal(0.42, state.UpdateProbability, precision: 3);
    }

    [Fact]
    public void OccupancyGraphObservationUpdatesContributionState()
    {
        var graph = new FractalOccupancyGraph();
        var key = new AquariumFractalKey("node/observed");

        var contribution = graph.Observe(key, observedContribution: 2.25f, frameIndex: 5, resident: true);

        Assert.Equal(2.25f, contribution.MeanContribution);
        Assert.Equal<uint>(5, contribution.LastSampledFrame);
        Assert.True(contribution.Resident);
        Assert.True(graph.TryGet(key, out var state));
        Assert.Equal(2.25f, state.Contribution.MeanContribution);
    }

    [Fact]
    public void OccupancyGraphDecaysConfidenceAndGrowsStaleUncertainty()
    {
        var graph = new FractalOccupancyGraph();
        var key = new AquariumFractalKey("node/stale");
        _ = graph.Observe(key, observedContribution: 1.0f, frameIndex: 1, resident: true);
        _ = graph.Observe(key, observedContribution: 1.0f, frameIndex: 2, resident: true);
        Assert.True(graph.TryGet(key, out var before));

        graph.DecayUnobserved(frameIndex: 20);

        Assert.True(graph.TryGet(key, out var after));
        Assert.True(after.Contribution.Confidence < before.Contribution.Confidence);
        Assert.True(after.Contribution.Variance > before.Contribution.Variance);
        Assert.Equal<uint>(2, after.Contribution.LastSampledFrame);
    }
}
