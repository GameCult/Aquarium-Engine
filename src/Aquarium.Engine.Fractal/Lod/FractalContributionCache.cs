using Aquarium.Engine.Fractal;
using Aquarium.Engine.Fractal.Temporal;

namespace Aquarium.Engine.Fractal.Lod;

public sealed class FractalContributionCache
{
    private readonly FractalContributionTable table = new();
    private uint frameIndex;

    public FractalContributionReservoirSnapshot LastFrameReservoir { get; private set; }

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

        var reservoir = new ResampledImportanceReservoir<FractalContributionCandidate>();
        var resident = plan.Residency.ResidentNodes.Select(key => key.Value).ToHashSet(StringComparer.Ordinal);
        foreach (var nodeKey in plan.UpdateNodes)
        {
            if (!scores.TryGetValue(nodeKey.Value, out var observedScore))
            {
                continue;
            }

            var state = table.GetOrCreate(nodeKey);
            var updateProbability = FractalStochasticUpdateScheduler.UpdateProbability(state, observedScore, frameIndex);
            var isResident = resident.Contains(nodeKey.Value);
            var candidate = FractalContributionCandidateGenerator.Build(
                nodeKey,
                observedScore,
                updateProbability,
                frameIndex,
                isResident);
            reservoir.Add(candidate, random.NextDouble());
            table.Store(FractalContributionEstimator.Observe(state, observedScore, frameIndex, isResident));
        }

        LastFrameReservoir = FractalContributionCandidateGenerator.Snapshot(reservoir);
        return plan;
    }

    public bool TryGet(AquariumFractalKey nodeKey, out AquariumContributionState state)
    {
        return table.TryGet(nodeKey, out state);
    }

}
