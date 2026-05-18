using Aquarium.Engine.Fractal;

namespace Aquarium.Engine.Fractal.Lod;

public sealed class FractalContributionTable
{
    private readonly Dictionary<string, AquariumContributionState> states = new(StringComparer.Ordinal);

    public bool TryGet(AquariumFractalKey nodeKey, out AquariumContributionState state)
    {
        return states.TryGetValue(nodeKey.Value, out state);
    }

    public AquariumContributionState GetOrCreate(AquariumFractalKey nodeKey)
    {
        if (states.TryGetValue(nodeKey.Value, out var state))
        {
            return state;
        }

        state = new AquariumContributionState(
            nodeKey,
            MeanContribution: 0.0f,
            Variance: 1.0f,
            Confidence: 0.0f,
            SampleCount: 0,
            LastSampledFrame: 0,
            Resident: false);
        states.Add(nodeKey.Value, state);
        return state;
    }

    public void Store(AquariumContributionState state)
    {
        states[state.NodeKey.Value] = state;
    }
}
