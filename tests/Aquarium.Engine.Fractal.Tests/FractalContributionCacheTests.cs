using System.Numerics;
using Aquarium.Engine.Fractal;
using Aquarium.Engine.Fractal.Lod;

namespace Aquarium.Engine.Fractal.Tests;

public sealed class FractalContributionCacheTests
{
    [Fact]
    public void CacheUpdatesOnlyBudgetedNodesEachFrame()
    {
        var cache = new FractalContributionCache();
        var summaries = new[]
        {
            Summary("node/a", maxHeightError: 2.0f),
            Summary("node/b", maxHeightError: 1.0f),
            Summary("node/c", maxHeightError: 0.5f),
        };

        var plan = cache.PlanFrame(
            summaries,
            _ => 4.0f,
            new FractalResourceBudget(MaxCpuUpdates: 1, MaxGpuEstimatedCost: 8.0f, MaxResidentPayloads: 8, MaxSsdRequests: 0),
            new TestFractalRandom(0.01, 0.01, 0.01),
            new ResidentStore());

        Assert.Single(plan.UpdateNodes);
        Assert.True(cache.TryGet(plan.UpdateNodes[0], out var updated));
        Assert.Equal(1, updated.SampleCount);
        Assert.True(updated.Confidence > 0.0f);
        Assert.True(cache.LastFrameReservoir.HasSample);
        Assert.Equal(plan.UpdateNodes[0], cache.LastFrameReservoir.SelectedNodeKey);
        Assert.Equal(1, cache.LastFrameReservoir.CandidateCount);
        Assert.True(cache.LastFrameReservoir.WeightSum > 0.0f);
    }

    [Fact]
    public void CacheConfidenceConvergesAcrossRepeatedFrames()
    {
        var cache = new FractalContributionCache();
        var summary = Summary("node/a", maxHeightError: 2.0f);
        var budget = new FractalResourceBudget(MaxCpuUpdates: 1, MaxGpuEstimatedCost: 4.0f, MaxResidentPayloads: 4, MaxSsdRequests: 0);

        cache.PlanFrame([summary], _ => 4.0f, budget, new TestFractalRandom(0.01), new ResidentStore());
        cache.PlanFrame([summary], _ => 4.0f, budget, new TestFractalRandom(0.01), new ResidentStore());

        Assert.True(cache.TryGet(summary.NodeKey, out var state));
        Assert.Equal(2, state.SampleCount);
        Assert.True(state.Confidence > 0.1f);
        Assert.True(state.Resident);
        Assert.True(cache.TryGetOccupancy(summary.NodeKey, out var occupancy));
        Assert.Equal(summary.NodeKey, occupancy.Contribution.NodeKey);
        Assert.True(occupancy.UpdateProbability > 0.0);
    }

    [Fact]
    public void CacheFramesScheduledContributionAsReservoirCandidate()
    {
        var cache = new FractalContributionCache();
        var summary = Summary("node/bright", maxHeightError: 3.0f);

        cache.PlanFrame(
            [summary],
            _ => 4.0f,
            new FractalResourceBudget(MaxCpuUpdates: 1, MaxGpuEstimatedCost: 4.0f, MaxResidentPayloads: 4, MaxSsdRequests: 0),
            new TestFractalRandom(0.01, 0.0),
            new ResidentStore());

        var reservoir = cache.LastFrameReservoir;
        Assert.True(reservoir.HasSample);
        Assert.Equal(summary.NodeKey, reservoir.SelectedNodeKey);
        Assert.True(reservoir.SelectedContribution > 0.0f);
        Assert.True(reservoir.SelectedTarget > 0.0f);
        Assert.True(reservoir.ContributionWeight > 0.0f);
    }

    private static AquariumFractalSummary Summary(string key, float maxHeightError)
    {
        return new AquariumFractalSummary(
            new AquariumFractalKey(key),
            Vector4.Zero,
            maxHeightError,
            MaxMaterialDelta: 0.0f,
            EstimatedCost: 1.0f,
            DescendantCount: 1);
    }

    private sealed class ResidentStore : IFractalPayloadStore
    {
        public bool IsResident(AquariumFractalKey nodeKey)
        {
            return true;
        }

        public void Request(AquariumFractalKey nodeKey)
        {
        }
    }
}
