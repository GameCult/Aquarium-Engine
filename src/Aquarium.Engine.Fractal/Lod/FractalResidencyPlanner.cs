using Aquarium.Engine.Fractal;

namespace Aquarium.Engine.Fractal.Lod;

public static class FractalResidencyPlanner
{
    public static FractalResidencyPlan Plan(IReadOnlyList<AquariumSelectedCut> selectedCut, IFractalPayloadStore payloadStore)
    {
        return Plan(selectedCut, payloadStore, maxRequests: int.MaxValue, maxResidentNodes: int.MaxValue);
    }

    public static FractalResidencyPlan Plan(
        IReadOnlyList<AquariumSelectedCut> selectedCut,
        IFractalPayloadStore payloadStore,
        int maxRequests,
        int maxResidentNodes)
    {
        return Plan(selectedCut, [], payloadStore, maxRequests, maxResidentNodes);
    }

    public static FractalResidencyPlan Plan(
        IReadOnlyList<AquariumSelectedCut> selectedCut,
        IReadOnlyList<AquariumFractalSummary> summaries,
        IFractalPayloadStore payloadStore,
        int maxRequests,
        int maxResidentNodes)
    {
        ArgumentNullException.ThrowIfNull(selectedCut);
        ArgumentNullException.ThrowIfNull(summaries);
        ArgumentNullException.ThrowIfNull(payloadStore);

        if (maxRequests < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRequests), maxRequests, "Request budget must not be negative.");
        }

        if (maxResidentNodes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxResidentNodes), maxResidentNodes, "Resident budget must not be negative.");
        }

        var costsByKey = summaries.ToDictionary(summary => summary.NodeKey.Value, summary => MathF.Max(summary.EstimatedCost, 1.0f), StringComparer.Ordinal);
        var candidates = selectedCut
            .Select((cut, index) => new ResidencyCandidate(
                cut,
                index,
                payloadStore.IsResident(cut.NodeKey),
                costsByKey.TryGetValue(cut.NodeKey.Value, out var cost) ? cost : 1.0f))
            .ToArray();
        var retainedResidentIndexes = candidates
            .Where(candidate => candidate.IsResident)
            .OrderByDescending(candidate => ResidencyValue(candidate))
            .ThenByDescending(candidate => candidate.Cut.Score)
            .ThenBy(candidate => candidate.Index)
            .Take(maxResidentNodes)
            .Select(candidate => candidate.Index)
            .ToHashSet();
        var resident = new List<AquariumFractalKey>();
        var fallback = new List<AquariumFractalKey>();
        var requested = new List<AquariumFractalKey>();
        var evicted = new List<AquariumFractalKey>();

        foreach (var candidate in candidates)
        {
            var cut = candidate.Cut;
            if (candidate.IsResident)
            {
                if (retainedResidentIndexes.Contains(candidate.Index))
                {
                    resident.Add(cut.NodeKey);
                }
                else
                {
                    fallback.Add(cut.NodeKey);
                    evicted.Add(cut.NodeKey);
                }

                continue;
            }

            fallback.Add(cut.NodeKey);
            if (cut.RequestedChildren && requested.Count < maxRequests)
            {
                payloadStore.Request(cut.NodeKey);
                requested.Add(cut.NodeKey);
            }
        }

        return new FractalResidencyPlan(resident, fallback, requested, evicted);
    }

    private static float ResidencyValue(ResidencyCandidate candidate)
    {
        return MathF.Max(candidate.Cut.Score, 0.0f) / MathF.Max(candidate.EstimatedCost, 1.0f);
    }

    private readonly record struct ResidencyCandidate(
        AquariumSelectedCut Cut,
        int Index,
        bool IsResident,
        float EstimatedCost);
}
