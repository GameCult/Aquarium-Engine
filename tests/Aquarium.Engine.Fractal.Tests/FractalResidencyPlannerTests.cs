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
