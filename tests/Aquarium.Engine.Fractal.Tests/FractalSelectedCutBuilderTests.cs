using System.Numerics;
using Aquarium.Engine.Fractal;
using Aquarium.Engine.Fractal.Lod;

namespace Aquarium.Engine.Fractal.Tests;

public sealed class FractalSelectedCutBuilderTests
{
    [Fact]
    public void SelectedCutChoosesHighestScoresWithinBudget()
    {
        var summaries = new[]
        {
            Summary("node/cheap-high", maxHeightError: 2.0f, estimatedCost: 1.0f),
            Summary("node/expensive", maxHeightError: 10.0f, estimatedCost: 10.0f),
            Summary("node/cheap-low", maxHeightError: 0.25f, estimatedCost: 1.0f),
        };

        var cut = FractalSelectedCutBuilder.Build(summaries, _ => 4.0f, maxEstimatedCost: 2.0f);

        Assert.Equal(2, cut.Length);
        Assert.Equal("node/cheap-high", cut[0].NodeKey.Value);
        Assert.Equal("node/cheap-low", cut[1].NodeKey.Value);
    }

    [Fact]
    public void SelectedCutReturnsEmptyWhenBudgetIsZero()
    {
        var cut = FractalSelectedCutBuilder.Build([Summary("node/a", 1.0f, 1.0f)], _ => 1.0f, maxEstimatedCost: 0.0f);

        Assert.Empty(cut);
    }

    [Fact]
    public void SelectedCutFadeAndChildRequestFollowScoreThreshold()
    {
        var cut = FractalSelectedCutBuilder.Build(
            [Summary("node/a", maxHeightError: 0.5f, estimatedCost: 1.0f)],
            _ => 1.0f,
            maxEstimatedCost: 1.0f,
            childRequestScore: 2.0f);

        Assert.Single(cut);
        Assert.Equal(0.25f, cut[0].Fade);
        Assert.False(cut[0].RequestedChildren);
        Assert.True(cut[0].UsesSummary);
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
