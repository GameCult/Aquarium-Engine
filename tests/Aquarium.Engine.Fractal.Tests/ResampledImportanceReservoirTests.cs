using Aquarium.Engine.Fractal.Temporal;

namespace Aquarium.Engine.Fractal.Tests;

public sealed class ResampledImportanceReservoirTests
{
    [Fact]
    public void ReservoirSelectsCandidateWithProbabilityProportionalToImportanceWeight()
    {
        var reservoir = new ResampledImportanceReservoir<string>();

        Assert.True(reservoir.Add(new ResampledImportanceCandidate<string>("dim", 1.0f, 1.0f), randomUnit: 0.0));
        Assert.True(reservoir.Add(new ResampledImportanceCandidate<string>("bright", 9.0f, 1.0f), randomUnit: 0.5));

        Assert.Equal("bright", reservoir.SelectedSample);
        Assert.Equal(10.0f, reservoir.WeightSum);
        Assert.Equal(2, reservoir.CandidateCount);
        Assert.InRange(reservoir.ContributionWeight, 0.55f, 0.56f);
    }

    [Fact]
    public void ReservoirRejectsInvalidOrZeroWeightCandidates()
    {
        var reservoir = new ResampledImportanceReservoir<string>();

        Assert.False(reservoir.Add(new ResampledImportanceCandidate<string>("zero-target", 0.0f, 1.0f), randomUnit: 0.0));
        Assert.False(reservoir.Add(new ResampledImportanceCandidate<string>("zero-pdf", 1.0f, 0.0f), randomUnit: 0.0));

        Assert.False(reservoir.HasSample);
        Assert.Equal(0.0f, reservoir.WeightSum);
        Assert.Equal(0, reservoir.CandidateCount);
    }

    [Fact]
    public void ReservoirMergeCarriesSourceCandidateCountAndWeightSum()
    {
        var left = new ResampledImportanceReservoir<string>();
        var right = new ResampledImportanceReservoir<string>();

        Assert.True(left.Add(new ResampledImportanceCandidate<string>("left-a", 2.0f, 1.0f), randomUnit: 0.0));
        Assert.False(left.Add(new ResampledImportanceCandidate<string>("left-b", 1.0f, 1.0f), randomUnit: 0.99));
        Assert.True(right.Add(new ResampledImportanceCandidate<string>("right", 6.0f, 1.0f, 4), randomUnit: 0.0));

        Assert.True(left.Merge(right, randomUnit: 0.25));

        Assert.Equal("right", left.SelectedSample);
        Assert.Equal(9.0f, left.WeightSum);
        Assert.Equal(6, left.CandidateCount);
        Assert.InRange(left.ContributionWeight, 0.24f, 0.26f);
    }

    [Fact]
    public void ReservoirRejectsOutOfRangeRandomUnits()
    {
        var reservoir = new ResampledImportanceReservoir<string>();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => reservoir.Add(new ResampledImportanceCandidate<string>("sample", 1.0f, 1.0f), randomUnit: 1.0));
    }
}
