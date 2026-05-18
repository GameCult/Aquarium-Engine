using System.Numerics;
using Aquarium.Engine.Fractal;
using Aquarium.Engine.Fractal.Lod;

namespace Aquarium.Engine.Fractal.Tests;

public sealed class FractalContributionTests
{
    [Fact]
    public void ContributionTableStoresStableKeyedState()
    {
        var table = new FractalContributionTable();
        var key = new AquariumFractalKey("node/root");

        var created = table.GetOrCreate(key);
        var updated = created with { MeanContribution = 4.0f, Confidence = 0.75f, SampleCount = 3 };
        table.Store(updated);

        Assert.True(table.TryGet(key, out var stored));
        Assert.Equal(4.0f, stored.MeanContribution);
        Assert.Equal(0.75f, stored.Confidence);
        Assert.Equal(3, stored.SampleCount);
    }

    [Fact]
    public void ProjectedErrorScoreIncreasesWithError()
    {
        var low = Summary("low", maxHeightError: 0.5f, estimatedCost: 1.0f);
        var high = Summary("high", maxHeightError: 2.0f, estimatedCost: 1.0f);

        Assert.True(FractalProjectedErrorScorer.Score(high, 10.0f) > FractalProjectedErrorScorer.Score(low, 10.0f));
    }

    [Fact]
    public void ProjectedErrorScoreDecreasesWithDistanceProxy()
    {
        var summary = Summary("node", maxHeightError: 1.0f, estimatedCost: 1.0f);

        var nearScore = FractalProjectedErrorScorer.Score(summary, projectedPixelsPerWorldUnit: 12.0f);
        var farScore = FractalProjectedErrorScorer.Score(summary, projectedPixelsPerWorldUnit: 3.0f);

        Assert.True(farScore < nearScore);
    }

    [Fact]
    public void ProjectedErrorScoreAccountsForEstimatedCost()
    {
        var cheap = Summary("cheap", maxHeightError: 1.0f, estimatedCost: 1.0f);
        var expensive = Summary("expensive", maxHeightError: 1.0f, estimatedCost: 4.0f);

        Assert.True(FractalProjectedErrorScorer.Score(cheap, 10.0f) > FractalProjectedErrorScorer.Score(expensive, 10.0f));
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
}
