using Aquarium.Engine.Fractal;

namespace Aquarium.Engine.Fractal.Lod;

public static class FractalResourceBudgetPlanner
{
    public static FractalResourcePlan Plan(
        IReadOnlyList<AquariumFractalSummary> summaries,
        IReadOnlyList<AquariumContributionState> states,
        Func<AquariumFractalSummary, float> projectedPixelsPerWorldUnit,
        Func<AquariumContributionState, float> currentScore,
        FractalResourceBudget budget,
        IFractalRandom random,
        IFractalPayloadStore payloadStore,
        uint frameIndex)
    {
        ArgumentNullException.ThrowIfNull(summaries);
        ArgumentNullException.ThrowIfNull(states);
        ArgumentNullException.ThrowIfNull(projectedPixelsPerWorldUnit);
        ArgumentNullException.ThrowIfNull(currentScore);
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(payloadStore);
        budget.Validate();

        var selectedCut = FractalSelectedCutBuilder.Build(summaries, projectedPixelsPerWorldUnit, budget.MaxGpuEstimatedCost);
        var updateNodes = FractalStochasticUpdateScheduler.SelectUpdates(states, currentScore, budget.MaxCpuUpdates, random, frameIndex);
        var residency = FractalResidencyPlanner.Plan(selectedCut, summaries, payloadStore, budget.MaxSsdRequests, budget.MaxResidentPayloads);

        return new FractalResourcePlan(
            selectedCut,
            updateNodes,
            residency,
            EstimatedCost(summaries, selectedCut),
            budget);
    }

    private static float EstimatedCost(IReadOnlyList<AquariumFractalSummary> summaries, IReadOnlyList<AquariumSelectedCut> selectedCut)
    {
        var summariesByKey = summaries.ToDictionary(summary => summary.NodeKey.Value, StringComparer.Ordinal);
        var cost = 0.0f;
        foreach (var cut in selectedCut)
        {
            cost += summariesByKey.TryGetValue(cut.NodeKey.Value, out var summary)
                ? MathF.Max(summary.EstimatedCost, 1.0f)
                : 1.0f;
        }

        return cost;
    }
}
