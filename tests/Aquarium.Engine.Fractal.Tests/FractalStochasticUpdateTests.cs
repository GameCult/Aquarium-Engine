using Aquarium.Engine.Fractal;
using Aquarium.Engine.Fractal.Lod;

namespace Aquarium.Engine.Fractal.Tests;

public sealed class FractalStochasticUpdateTests
{
    [Fact]
    public void EstimatorUpdatesMeanVarianceConfidenceAndSampleAge()
    {
        var state = NewState("node/a");

        state = FractalContributionEstimator.Observe(state, 2.0f, frameIndex: 10, resident: true);
        state = FractalContributionEstimator.Observe(state, 4.0f, frameIndex: 12, resident: true);

        Assert.Equal(3.0f, state.MeanContribution);
        Assert.Equal(2.0f, state.Variance);
        Assert.Equal(2, state.SampleCount);
        Assert.Equal<uint>(12, state.LastSampledFrame);
        Assert.True(state.Confidence > 0.0f);
        Assert.True(state.Resident);
    }

    [Fact]
    public void VisibleNodesKeepNonzeroUpdateProbability()
    {
        var state = NewState("node/a") with { Confidence = 1.0f, Variance = 0.0f, LastSampledFrame = 100 };

        var probability = FractalStochasticUpdateScheduler.UpdateProbability(state, currentScore: 0.5f, frameIndex: 100);

        Assert.True(probability > 0.0);
    }

    [Fact]
    public void NearThresholdNodesReceiveExtraUpdatePressure()
    {
        var state = NewState("node/a") with { Confidence = 1.0f, Variance = 0.0f, LastSampledFrame = 100 };

        var low = FractalStochasticUpdateScheduler.UpdateProbability(state, currentScore: 0.2f, frameIndex: 100);
        var near = FractalStochasticUpdateScheduler.UpdateProbability(state, currentScore: 1.0f, frameIndex: 100);

        Assert.True(near > low);
    }

    [Fact]
    public void SchedulerIsDeterministicWithFakeRandomAndRespectsBudget()
    {
        var states = new[]
        {
            NewState("node/a") with { Variance = 1.0f },
            NewState("node/b") with { Variance = 1.0f },
            NewState("node/c") with { Variance = 1.0f },
        };
        var firstRandom = new TestFractalRandom(0.01, 0.01, 0.01);
        var secondRandom = new TestFractalRandom(0.01, 0.01, 0.01);

        var first = FractalStochasticUpdateScheduler.SelectUpdates(states, _ => 1.0f, maxUpdates: 2, firstRandom, frameIndex: 10);
        var second = FractalStochasticUpdateScheduler.SelectUpdates(states, _ => 1.0f, maxUpdates: 2, secondRandom, frameIndex: 10);

        Assert.Equal(first, second);
        Assert.Equal(2, first.Length);
    }

    private static AquariumContributionState NewState(string key)
    {
        return new AquariumContributionState(
            new AquariumFractalKey(key),
            MeanContribution: 0.0f,
            Variance: 1.0f,
            Confidence: 0.0f,
            SampleCount: 0,
            LastSampledFrame: 0,
            Resident: false);
    }
}
