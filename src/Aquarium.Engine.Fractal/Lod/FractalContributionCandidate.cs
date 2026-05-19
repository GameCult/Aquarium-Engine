using Aquarium.Engine.Fractal.Temporal;

namespace Aquarium.Engine.Fractal.Lod;

public readonly record struct FractalContributionCandidate(
    AquariumFractalKey NodeKey,
    float ObservedContribution,
    float UpdateProbability,
    uint FrameIndex,
    bool Resident);

public readonly record struct FractalContributionReservoirSnapshot(
    bool HasSample,
    AquariumFractalKey SelectedNodeKey,
    float SelectedContribution,
    float SelectedTarget,
    float WeightSum,
    int CandidateCount,
    float ContributionWeight);

public static class FractalContributionCandidateGenerator
{
    public static ResampledImportanceCandidate<FractalContributionCandidate> Build(
        AquariumFractalKey nodeKey,
        float observedContribution,
        double updateProbability,
        uint frameIndex,
        bool resident)
    {
        var target = MathF.Max(observedContribution, 0.0f);
        var sourcePdf = MathF.Max((float)updateProbability, 1.0e-6f);
        var sample = new FractalContributionCandidate(
            nodeKey,
            target,
            sourcePdf,
            frameIndex,
            resident);

        return new ResampledImportanceCandidate<FractalContributionCandidate>(
            sample,
            target,
            sourcePdf);
    }

    public static FractalContributionReservoirSnapshot Snapshot(
        ResampledImportanceReservoir<FractalContributionCandidate> reservoir)
    {
        ArgumentNullException.ThrowIfNull(reservoir);
        if (!reservoir.HasSample)
        {
            return new FractalContributionReservoirSnapshot(
                HasSample: false,
                SelectedNodeKey: default,
                SelectedContribution: 0.0f,
                SelectedTarget: 0.0f,
                WeightSum: reservoir.WeightSum,
                CandidateCount: reservoir.CandidateCount,
                ContributionWeight: 0.0f);
        }

        var selected = reservoir.SelectedSample;
        return new FractalContributionReservoirSnapshot(
            HasSample: true,
            SelectedNodeKey: selected.NodeKey,
            SelectedContribution: selected.ObservedContribution,
            SelectedTarget: reservoir.SelectedTarget,
            WeightSum: reservoir.WeightSum,
            CandidateCount: reservoir.CandidateCount,
            ContributionWeight: reservoir.ContributionWeight);
    }
}
