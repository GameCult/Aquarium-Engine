using System.Numerics;
using Aquarium.Engine.Fractal;
using Aquarium.Engine.Fractal.Lod;

namespace Aquarium.Engine.Fractal.Tests;

public sealed class FractalSurfacePageResidencyPlannerTests
{
    [Fact]
    public void SurfacePageResidencyRetainsBestErrorPerByteUnderBudget()
    {
        var expensive = Page("domain/a", "node/a", AquariumFractalSurfacePageKind.Material, width: 256, height: 256, maxError: 1.0f);
        var cheap = Page("domain/a", "node/b", AquariumFractalSurfacePageKind.Height, width: 64, height: 64, maxError: 0.5f);
        var store = new TestSurfacePageStore([expensive.Key, cheap.Key]);

        var plan = FractalSurfacePageResidencyPlanner.Plan(
            [expensive, cheap],
            store,
            maxResidentBytes: FractalSurfacePageResidencyPlanner.EstimatedBytes(cheap),
            maxRequests: 0);

        Assert.Equal([cheap.Key], plan.ResidentPages.Select(page => page.Key));
        Assert.Equal([expensive.Key], plan.EvictedPages.Select(page => page.Key));
        Assert.Equal([expensive.Key], store.Evictions);
        Assert.Equal(FractalSurfacePageResidencyPlanner.EstimatedBytes(cheap), plan.ResidentBytes);
        Assert.Empty(plan.RequestedPages);
    }

    [Fact]
    public void SurfacePageResidencyRequestsHighestValueMissingPages()
    {
        var low = Page("domain/a", "node/low", AquariumFractalSurfacePageKind.Material, width: 256, height: 256, maxError: 0.1f);
        var high = Page("domain/a", "node/high", AquariumFractalSurfacePageKind.Height, width: 64, height: 64, maxError: 0.5f);
        var store = new TestSurfacePageStore();

        var plan = FractalSurfacePageResidencyPlanner.Plan([low, high], store, maxResidentBytes: 0, maxRequests: 1);

        Assert.Equal([high.Key], plan.RequestedPages.Select(page => page.Key));
        Assert.Equal([high.Key], store.Requests.Select(page => page.Key));
        Assert.Equal([low.Key, high.Key], plan.MissingPages.Select(page => page.Key));
    }

    [Fact]
    public void SurfacePageResidencyRejectsNegativeBudgets()
    {
        var store = new TestSurfacePageStore();

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => FractalSurfacePageResidencyPlanner.Plan(
            [],
            store,
            maxResidentBytes: -1,
            maxRequests: 0));

        Assert.Equal("maxResidentBytes", ex.ParamName);
    }

    private static AquariumFractalSurfacePage Page(
        string domainKey,
        string nodeKey,
        AquariumFractalSurfacePageKind kind,
        int width,
        int height,
        float maxError)
    {
        return new AquariumFractalSurfacePage(
            new AquariumFractalSurfacePageKey(
                new AquariumFractalKey(domainKey),
                new AquariumFractalKey(nodeKey),
                kind,
                mipLevel: 0),
            Vector4.Zero,
            width,
            height,
            PayloadHandle: 0,
            maxError);
    }

    private sealed class TestSurfacePageStore(IEnumerable<AquariumFractalSurfacePageKey>? resident = null) : IFractalSurfacePageStore
    {
        private readonly HashSet<string> residentKeys = resident?.Select(key => key.Value).ToHashSet(StringComparer.Ordinal) ?? [];

        public List<AquariumFractalSurfacePage> Requests { get; } = [];

        public List<AquariumFractalSurfacePageKey> Evictions { get; } = [];

        public bool IsResident(AquariumFractalSurfacePageKey key)
        {
            return residentKeys.Contains(key.Value);
        }

        public void Request(AquariumFractalSurfacePage page)
        {
            Requests.Add(page);
        }

        public void Evict(AquariumFractalSurfacePageKey key)
        {
            Evictions.Add(key);
            residentKeys.Remove(key.Value);
        }
    }
}
