using System.Numerics;
using Aquarium.Engine.Fractal;
using Aquarium.Engine.Fractal.Lod;

namespace Aquarium.Engine.Fractal.Tests;

public sealed class FractalResourceBudgetPlannerTests
{
    [Fact]
    public void ResourcePlannerCoordinatesCpuGpuRamAndSsdBudgets()
    {
        var summaries = new[]
        {
            Summary("node/a", maxHeightError: 2.0f, estimatedCost: 2.0f),
            Summary("node/b", maxHeightError: 1.5f, estimatedCost: 2.0f),
            Summary("node/c", maxHeightError: 1.0f, estimatedCost: 2.0f),
        };
        var states = summaries.Select(summary => new AquariumContributionState(
            summary.NodeKey,
            MeanContribution: 0.0f,
            Variance: 1.0f,
            Confidence: 0.0f,
            SampleCount: 0,
            LastSampledFrame: 0,
            Resident: false)).ToArray();
        var store = new TestPayloadStore([summaries[0].NodeKey]);
        var budget = new FractalResourceBudget(
            MaxCpuUpdates: 1,
            MaxGpuEstimatedCost: 4.0f,
            MaxResidentPayloads: 1,
            MaxSsdRequests: 1);

        var plan = FractalResourceBudgetPlanner.Plan(
            summaries,
            states,
            _ => 4.0f,
            _ => 1.0f,
            budget,
            new TestFractalRandom(0.01, 0.01, 0.01),
            store,
            frameIndex: 10);

        Assert.Single(plan.UpdateNodes);
        Assert.True(plan.GpuEstimatedCost <= budget.MaxGpuEstimatedCost);
        Assert.True(plan.Residency.ResidentNodes.Count <= budget.MaxResidentPayloads);
        Assert.True(plan.Residency.RequestedNodes.Count <= budget.MaxSsdRequests);
        Assert.Equal(plan.Residency.RequestedNodes, store.Requests);
        Assert.Equal(budget, plan.Budget);
    }

    [Fact]
    public void ResourcePlannerRejectsNegativeBudgets()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => FractalResourceBudgetPlanner.Plan(
            [],
            [],
            _ => 0.0f,
            _ => 0.0f,
            new FractalResourceBudget(MaxCpuUpdates: -1, MaxGpuEstimatedCost: 0.0f, MaxResidentPayloads: 0, MaxSsdRequests: 0),
            new TestFractalRandom(0.5),
            new TestPayloadStore(),
            frameIndex: 0));

        Assert.Equal("MaxCpuUpdates", ex.ParamName);
    }

    private static AquariumFractalSummary Summary(string key, float maxHeightError, float estimatedCost)
    {
        return new AquariumFractalSummary(
            new AquariumFractalKey(key),
            Vector4.Zero,
            maxHeightError,
            MaxMaterialDelta: 0.0f,
            estimatedCost,
            DescendantCount: 1);
    }

    private sealed class TestPayloadStore(IEnumerable<AquariumFractalKey>? resident = null) : IFractalPayloadStore
    {
        private readonly HashSet<string> residentKeys = resident?.Select(key => key.Value).ToHashSet(StringComparer.Ordinal) ?? [];

        public List<AquariumFractalKey> Requests { get; } = [];

        public bool IsResident(AquariumFractalKey nodeKey)
        {
            return residentKeys.Contains(nodeKey.Value);
        }

        public void Request(AquariumFractalKey nodeKey)
        {
            Requests.Add(nodeKey);
        }
    }
}
