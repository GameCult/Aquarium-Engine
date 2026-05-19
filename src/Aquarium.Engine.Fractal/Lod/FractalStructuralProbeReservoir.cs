using Aquarium.Engine.Fractal.Grammar;
using Aquarium.Engine.Fractal.Temporal;

namespace Aquarium.Engine.Fractal.Lod;

public readonly record struct FractalProbeReservoirSnapshot(
    bool HasSample,
    AquariumFractalKey DomainKey,
    AquariumFractalKey NodeKey,
    float TargetContribution,
    float BoundRadius,
    float WeightSum,
    int CandidateCount,
    float ContributionWeight);

public static class FractalStructuralProbeReservoir
{
    public static FractalProbeReservoirSnapshot Build(
        FractalOwnershipTree tree,
        IReadOnlyList<AquariumFractalSummary> summaries,
        IReadOnlyList<AquariumSelectedCut> selectedCut,
        float projectedPixelsPerWorldUnit,
        IFractalRandom random)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(summaries);
        ArgumentNullException.ThrowIfNull(selectedCut);
        ArgumentNullException.ThrowIfNull(random);

        if (selectedCut.Count == 0)
        {
            return Empty();
        }

        var summariesByKey = summaries.ToDictionary(summary => summary.NodeKey.Value, StringComparer.Ordinal);
        var nodesByKey = tree.Nodes.ToDictionary(node => node.Key.Value, StringComparer.Ordinal);
        var sourceProbability = 1.0 / selectedCut.Count;
        var reservoir = new ResampledImportanceReservoir<FractalProbeSample>();
        for (var index = 0; index < selectedCut.Count; index++)
        {
            var cut = selectedCut[index];
            if (!summariesByKey.TryGetValue(cut.NodeKey.Value, out var summary)
                || !nodesByKey.TryGetValue(cut.NodeKey.Value, out var node))
            {
                continue;
            }

            var candidate = FractalStructuralProbeGenerator.BuildCandidate(
                summary,
                node.DomainKey,
                projectedPixelsPerWorldUnit,
                sourceProbability,
                payloadHandle: index);
            reservoir.Add(candidate, random.NextDouble());
        }

        return Snapshot(reservoir);
    }

    private static FractalProbeReservoirSnapshot Snapshot(ResampledImportanceReservoir<FractalProbeSample> reservoir)
    {
        if (!reservoir.HasSample)
        {
            return Empty();
        }

        var selected = reservoir.SelectedSample;
        return new FractalProbeReservoirSnapshot(
            HasSample: true,
            selected.DomainKey,
            selected.NodeKey,
            selected.TargetContribution,
            selected.BoundRadius,
            reservoir.WeightSum,
            reservoir.CandidateCount,
            reservoir.ContributionWeight);
    }

    private static FractalProbeReservoirSnapshot Empty()
    {
        return new FractalProbeReservoirSnapshot(
            HasSample: false,
            DomainKey: default,
            NodeKey: default,
            TargetContribution: 0.0f,
            BoundRadius: 0.0f,
            WeightSum: 0.0f,
            CandidateCount: 0,
            ContributionWeight: 0.0f);
    }
}
