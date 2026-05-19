using Aquarium.Engine.Fractal;

namespace Aquarium.Engine.Fractal.Lod;

public sealed class FractalContributionCache
{
    private readonly FractalContributionTable table = new();
    private uint frameIndex;

    public FractalResourcePlan PlanFrame(
        IReadOnlyList<AquariumFractalSummary> summaries,
        Func<AquariumFractalSummary, float> projectedPixelsPerWorldUnit,
        FractalResourceBudget budget,
        IFractalRandom random,
        IFractalPayloadStore payloadStore)
    {
        ArgumentNullException.ThrowIfNull(summaries);
        ArgumentNullException.ThrowIfNull(projectedPixelsPerWorldUnit);
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(payloadStore);

        frameIndex++;
        var states = new AquariumContributionState[summaries.Count];
        var scores = new Dictionary<string, float>(StringComparer.Ordinal);
        for (var index = 0; index < summaries.Count; index++)
        {
            var summary = summaries[index];
            states[index] = table.GetOrCreate(summary.NodeKey);
            scores[summary.NodeKey.Value] = FractalProjectedErrorScorer.Score(summary, projectedPixelsPerWorldUnit(summary));
        }

        var plan = FractalResourceBudgetPlanner.Plan(
            summaries,
            states,
            projectedPixelsPerWorldUnit,
            state => scores.TryGetValue(state.NodeKey.Value, out var score) ? score : 0.0f,
            budget,
            random,
            payloadStore,
            frameIndex);

        var resident = plan.Residency.ResidentNodes.Select(key => key.Value).ToHashSet(StringComparer.Ordinal);
        foreach (var nodeKey in plan.UpdateNodes)
        {
            if (!scores.TryGetValue(nodeKey.Value, out var observedScore))
            {
                continue;
            }

            var state = table.GetOrCreate(nodeKey);
            table.Store(FractalContributionEstimator.Observe(state, observedScore, frameIndex, resident.Contains(nodeKey.Value)));
        }

        return plan;
    }

    public bool TryGet(AquariumFractalKey nodeKey, out AquariumContributionState state)
    {
        return table.TryGet(nodeKey, out state);
    }

}
