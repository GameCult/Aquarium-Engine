using Aquarium.Engine.Fractal;
using Aquarium.Engine.Fractal.Temporal;

namespace Aquarium.Engine.Fractal.Lod;

public sealed class FractalContributionCache
{
    private readonly FractalOccupancyGraph occupancy = new();
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
            states[index] = occupancy.GetOrCreate(summary.NodeKey).Contribution;
            var score = FractalProjectedErrorScorer.Score(summary, projectedPixelsPerWorldUnit(summary));
            scores[summary.NodeKey.Value] = score;
            var updateProbability = FractalStochasticUpdateScheduler.UpdateProbability(states[index], score, frameIndex);
            occupancy.MarkVisible(summary.NodeKey, score, frameIndex, updateProbability);
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

            var state = occupancy.GetOrCreate(nodeKey).Contribution;
            var updateProbability = FractalStochasticUpdateScheduler.UpdateProbability(state, observedScore, frameIndex);
            var isResident = resident.Contains(nodeKey.Value);
            var candidate = FractalContributionCandidateGenerator.Build(
                nodeKey,
                observedScore,
                updateProbability,
                frameIndex,
                isResident);
            reservoir.Add(candidate, random.NextDouble());
            occupancy.Observe(nodeKey, observedScore, frameIndex, isResident);
        }

        occupancy.DecayUnobserved(frameIndex);
        LastFrameReservoir = FractalContributionCandidateGenerator.Snapshot(reservoir);
        return plan;
    }

    public bool TryGet(AquariumFractalKey nodeKey, out AquariumContributionState state)
    {
        if (occupancy.TryGet(nodeKey, out var node))
        {
            state = node.Contribution;
            return true;
        }

        state = default;
        return false;
    }

    public bool TryGetOccupancy(AquariumFractalKey nodeKey, out FractalOccupancyNodeState state)
    {
        return occupancy.TryGet(nodeKey, out state);
    }

}
