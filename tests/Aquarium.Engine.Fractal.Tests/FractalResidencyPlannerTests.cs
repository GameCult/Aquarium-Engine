using System.Numerics;
using Aquarium.Engine.Fractal;
using Aquarium.Engine.Fractal.Lod;

namespace Aquarium.Engine.Fractal.Tests;

public sealed class FractalResidencyPlannerTests
{
    [Fact]
    public void MissingChildRendersSummaryFallbackAndRequestsWhenHighScore()
    {
        var store = new TestPayloadStore();
        var key = new AquariumFractalKey("node/a");
        var cut = new AquariumSelectedCut(key, Score: 2.0f, Fade: 1.0f, UsesSummary: true, RequestedChildren: true);

        var plan = FractalResidencyPlanner.Plan([cut], store);

        Assert.Empty(plan.ResidentNodes);
        Assert.Equal([key], plan.SummaryFallbackNodes);
        Assert.Equal([key], plan.RequestedNodes);
        Assert.Equal([key], store.Requests);
    }

    [Fact]
    public void ResidentNodeDoesNotRequestPayload()
    {
        var key = new AquariumFractalKey("node/a");
        var store = new TestPayloadStore([key]);
        var cut = new AquariumSelectedCut(key, Score: 2.0f, Fade: 1.0f, UsesSummary: true, RequestedChildren: true);

        var plan = FractalResidencyPlanner.Plan([cut], store);

        Assert.Equal([key], plan.ResidentNodes);
        Assert.Empty(plan.SummaryFallbackNodes);
        Assert.Empty(plan.RequestedNodes);
        Assert.Empty(store.Requests);
    }

    [Fact]
    public void ResidencyPlannerRespectsRamAndSsdBudgets()
    {
        var residentA = new AquariumFractalKey("node/resident-a");
        var residentB = new AquariumFractalKey("node/resident-b");
        var missingA = new AquariumFractalKey("node/missing-a");
        var missingB = new AquariumFractalKey("node/missing-b");
        var store = new TestPayloadStore([residentA, residentB]);
        var cuts = new[]
        {
            new AquariumSelectedCut(residentA, Score: 4.0f, Fade: 1.0f, UsesSummary: true, RequestedChildren: true),
            new AquariumSelectedCut(residentB, Score: 3.0f, Fade: 1.0f, UsesSummary: true, RequestedChildren: true),
            new AquariumSelectedCut(missingA, Score: 2.0f, Fade: 1.0f, UsesSummary: true, RequestedChildren: true),
            new AquariumSelectedCut(missingB, Score: 1.0f, Fade: 1.0f, UsesSummary: true, RequestedChildren: true),
        };

        var plan = FractalResidencyPlanner.Plan(cuts, store, maxRequests: 1, maxResidentNodes: 1);

        Assert.Equal([residentA], plan.ResidentNodes);
        Assert.Equal([residentB, missingA, missingB], plan.SummaryFallbackNodes);
        Assert.Equal([missingA], plan.RequestedNodes);
        Assert.Equal([missingA], store.Requests);
    }

    [Fact]
    public void ResidencyPlannerEvictsLowScoreHighCostPayloadsFirst()
    {
        var expensive = new AquariumFractalKey("node/expensive");
        var cheap = new AquariumFractalKey("node/cheap");
        var store = new TestPayloadStore([expensive, cheap]);
        var cuts = new[]
        {
            new AquariumSelectedCut(expensive, Score: 5.0f, Fade: 1.0f, UsesSummary: true, RequestedChildren: false),
            new AquariumSelectedCut(cheap, Score: 4.0f, Fade: 1.0f, UsesSummary: true, RequestedChildren: false),
        };
        var summaries = new[]
        {
            Summary(expensive, estimatedCost: 100.0f),
            Summary(cheap, estimatedCost: 1.0f),
        };

        var plan = FractalResidencyPlanner.Plan(cuts, summaries, store, maxRequests: 0, maxResidentNodes: 1);

        Assert.Equal([cheap], plan.ResidentNodes);
        Assert.Equal([expensive], plan.SummaryFallbackNodes);
        Assert.Equal([expensive], plan.EvictedNodes);
        Assert.Empty(plan.RequestedNodes);
    }

    private static AquariumFractalSummary Summary(AquariumFractalKey key, float estimatedCost)
    {
        return new AquariumFractalSummary(
            key,
            Vector4.Zero,
            MaxHeightError: 1.0f,
            MaxMaterialDelta: 0.0f,
            estimatedCost,
            DescendantCount: 0);
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
