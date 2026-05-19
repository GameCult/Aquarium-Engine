using Aquarium.Engine.Fractal;

namespace Aquarium.Engine.Fractal.Lod;

public readonly record struct FractalOccupancyNodeState(
    AquariumContributionState Contribution,
    uint LastVisibleFrame,
    float LastObservedScore,
    double UpdateProbability);

public sealed class FractalOccupancyGraph
{
    private const float ConfidenceDecayPerFrame = 0.985f;
    private const float StaleVarianceGrowth = 0.035f;
    private readonly Dictionary<string, FractalOccupancyNodeState> nodes = new(StringComparer.Ordinal);

    public FractalOccupancyNodeState GetOrCreate(AquariumFractalKey nodeKey)
    {
        if (nodes.TryGetValue(nodeKey.Value, out var state))
        {
            return state;
        }

        state = new FractalOccupancyNodeState(
            CreateEmptyContributionState(nodeKey),
            LastVisibleFrame: 0,
            LastObservedScore: 0.0f,
            UpdateProbability: 0.0);
        nodes.Add(nodeKey.Value, state);
        return state;
    }

    public bool TryGet(AquariumFractalKey nodeKey, out FractalOccupancyNodeState state)
    {
        return nodes.TryGetValue(nodeKey.Value, out state);
    }

    public void MarkVisible(AquariumFractalKey nodeKey, float observedScore, uint frameIndex, double updateProbability)
    {
        var state = GetOrCreate(nodeKey);
        nodes[nodeKey.Value] = state with
        {
            LastVisibleFrame = frameIndex,
            LastObservedScore = MathF.Max(observedScore, 0.0f),
            UpdateProbability = Math.Clamp(updateProbability, 0.0, 1.0),
        };
    }

    public AquariumContributionState Observe(AquariumFractalKey nodeKey, float observedContribution, uint frameIndex, bool resident)
    {
        var state = GetOrCreate(nodeKey);
        var contribution = FractalContributionEstimator.Observe(
            state.Contribution,
            observedContribution,
            frameIndex,
            resident);
        nodes[nodeKey.Value] = state with
        {
            Contribution = contribution,
            LastVisibleFrame = frameIndex,
            LastObservedScore = MathF.Max(observedContribution, 0.0f),
        };
        return contribution;
    }

    public void DecayUnobserved(uint frameIndex)
    {
        foreach (var key in nodes.Keys.ToArray())
        {
            var state = nodes[key];
            if (state.Contribution.LastSampledFrame == frameIndex)
            {
                continue;
            }

            var age = frameIndex >= state.Contribution.LastSampledFrame
                ? frameIndex - state.Contribution.LastSampledFrame
                : 0;
            if (age == 0)
            {
                continue;
            }

            var staleGrowth = MathF.Min(age * StaleVarianceGrowth, 4.0f);
            nodes[key] = state with
            {
                Contribution = state.Contribution with
                {
                    Confidence = MathF.Max(0.0f, state.Contribution.Confidence * ConfidenceDecayPerFrame),
                    Variance = MathF.Max(state.Contribution.Variance, state.Contribution.Variance + staleGrowth),
                },
            };
        }
    }

    private static AquariumContributionState CreateEmptyContributionState(AquariumFractalKey nodeKey)
    {
        return new AquariumContributionState(
            nodeKey,
            MeanContribution: 0.0f,
            Variance: 1.0f,
            Confidence: 0.0f,
            SampleCount: 0,
            LastSampledFrame: 0,
            Resident: false);
    }
}
