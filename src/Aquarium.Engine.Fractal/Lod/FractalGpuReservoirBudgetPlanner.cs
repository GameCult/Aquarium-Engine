namespace Aquarium.Engine.Fractal.Lod;

public enum FractalGpuReservoirPassKind
{
    SdfEnvelope,
    PbrMaterial,
    Radiosity,
}

public readonly record struct FractalGpuReservoirPassPlan(
    FractalGpuReservoirPassKind Kind,
    int ResidentEntries,
    int UpdatesThisFrame,
    int CandidatesPerUpdate,
    double ExpectedFullCoverageFrames,
    ulong ResidentBytes,
    ulong CandidateEvaluationsThisFrame);

public sealed class FractalGpuReservoirFramePlan
{
    public FractalGpuReservoirFramePlan(
        int splatCount,
        int splatStrideBytes,
        ulong splatBytes,
        IReadOnlyList<FractalGpuReservoirPassPlan> passes)
    {
        SplatCount = splatCount;
        SplatStrideBytes = splatStrideBytes;
        SplatBytes = splatBytes;
        Passes = passes;
    }

    public int SplatCount { get; }

    public int SplatStrideBytes { get; }

    public ulong SplatBytes { get; }

    public IReadOnlyList<FractalGpuReservoirPassPlan> Passes { get; }

    public ulong ReservoirBytes => Passes.Aggregate(0UL, (sum, pass) => sum + pass.ResidentBytes);

    public ulong TotalResidentBytes => SplatBytes + ReservoirBytes;

    public ulong CandidateEvaluationsThisFrame => Passes.Aggregate(0UL, (sum, pass) => sum + pass.CandidateEvaluationsThisFrame);
}

public static class FractalGpuReservoirBudgetPlanner
{
    public static FractalGpuReservoirFramePlan Plan(
        int splatCount,
        int reservoirUpdatesPerPass,
        int candidatesPerUpdate,
        int splatStrideBytes = 80,
        int reservoirStrideBytes = 64)
    {
        if (splatCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(splatCount), splatCount, "Splat count must be positive.");
        }

        if (reservoirUpdatesPerPass <= 0 || reservoirUpdatesPerPass > splatCount)
        {
            throw new ArgumentOutOfRangeException(nameof(reservoirUpdatesPerPass), reservoirUpdatesPerPass, "Reservoir updates must be positive and no larger than the resident splat count.");
        }

        if (candidatesPerUpdate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(candidatesPerUpdate), candidatesPerUpdate, "Candidates per update must be positive.");
        }

        if (splatStrideBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(splatStrideBytes), splatStrideBytes, "Splat stride must be positive.");
        }

        if (reservoirStrideBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(reservoirStrideBytes), reservoirStrideBytes, "Reservoir stride must be positive.");
        }

        var splatBytes = checked((ulong)splatCount * (ulong)splatStrideBytes);
        var expectedCoverageFrames = splatCount / (double)reservoirUpdatesPerPass;
        var passes = new[]
        {
            Pass(FractalGpuReservoirPassKind.SdfEnvelope),
            Pass(FractalGpuReservoirPassKind.PbrMaterial),
            Pass(FractalGpuReservoirPassKind.Radiosity),
        };

        return new FractalGpuReservoirFramePlan(
            splatCount,
            splatStrideBytes,
            splatBytes,
            passes);

        FractalGpuReservoirPassPlan Pass(FractalGpuReservoirPassKind kind)
        {
            return new FractalGpuReservoirPassPlan(
                kind,
                splatCount,
                reservoirUpdatesPerPass,
                candidatesPerUpdate,
                expectedCoverageFrames,
                checked((ulong)splatCount * (ulong)reservoirStrideBytes),
                checked((ulong)reservoirUpdatesPerPass * (ulong)candidatesPerUpdate));
        }
    }
}
