using Aquarium.Engine.Fractal;

namespace Aquarium.Engine.Fractal.Lod;

public static class FractalStochasticUpdateScheduler
{
    public static AquariumFractalKey[] SelectUpdates(
        IReadOnlyList<AquariumContributionState> states,
        Func<AquariumContributionState, float> currentScore,
        int maxUpdates,
        IFractalRandom random,
        uint frameIndex)
    {
        ArgumentNullException.ThrowIfNull(states);
        ArgumentNullException.ThrowIfNull(currentScore);
        ArgumentNullException.ThrowIfNull(random);

        if (maxUpdates <= 0)
        {
            return [];
        }

        var selected = new List<AquariumFractalKey>();
        foreach (var state in states.OrderBy(state => state.NodeKey.Value, StringComparer.Ordinal))
        {
            var probability = UpdateProbability(state, currentScore(state), frameIndex);
            if (random.NextDouble() <= probability)
            {
                selected.Add(state.NodeKey);
                if (selected.Count >= maxUpdates)
                {
                    break;
                }
            }
        }

        return selected.ToArray();
    }

    public static double UpdateProbability(AquariumContributionState state, float currentScore, uint frameIndex)
    {
        var uncertainty = Math.Clamp(Math.Sqrt(Math.Max(state.Variance, 0.0f)) * 0.25, 0.0, 0.35);
        var age = frameIndex >= state.LastSampledFrame ? frameIndex - state.LastSampledFrame : 0;
        var stalePressure = Math.Clamp(age / 240.0, 0.0, 0.30);
        var visiblePressure = currentScore > 0.0f ? 0.10 : 0.0;
        var thresholdPressure = currentScore is > 0.75f and < 1.25f ? 0.20 : 0.0;
        var confidencePenalty = (1.0 - Math.Clamp(state.Confidence, 0.0f, 1.0f)) * 0.15;

        return Math.Clamp(0.02 + uncertainty + stalePressure + visiblePressure + thresholdPressure + confidencePenalty, 0.02, 0.95);
    }
}
