using Aquarium.Engine.Fractal.Lod;

namespace Aquarium.Engine.Fractal.Tests;

public sealed class FractalGpuReservoirBudgetPlannerTests
{
    [Fact]
    public void PlanKeepsReservoirPassesDistinctWhileSharingBudgetMath()
    {
        var plan = FractalGpuReservoirBudgetPlanner.Plan(
            splatCount: 2_000_000,
            reservoirUpdatesPerPass: 50_000,
            candidatesPerUpdate: 2);

        Assert.Equal(2_000_000, plan.SplatCount);
        Assert.Equal(160_000_000UL, plan.SplatBytes);
        Assert.Equal(384_000_000UL, plan.ReservoirBytes);
        Assert.Equal(544_000_000UL, plan.TotalResidentBytes);
        Assert.Equal(300_000UL, plan.CandidateEvaluationsThisFrame);
        Assert.Equal(
            new[]
            {
                FractalGpuReservoirPassKind.SdfEnvelope,
                FractalGpuReservoirPassKind.PbrMaterial,
                FractalGpuReservoirPassKind.Radiosity,
            },
            plan.Passes.Select(pass => pass.Kind).ToArray());

        foreach (var pass in plan.Passes)
        {
            Assert.Equal(2_000_000, pass.ResidentEntries);
            Assert.Equal(50_000, pass.UpdatesThisFrame);
            Assert.Equal(2, pass.CandidatesPerUpdate);
            Assert.Equal(40.0, pass.ExpectedFullCoverageFrames);
            Assert.Equal(128_000_000UL, pass.ResidentBytes);
            Assert.Equal(100_000UL, pass.CandidateEvaluationsThisFrame);
        }
    }

    [Fact]
    public void PlanRejectsBudgetsThatCannotConvergeOverResidentReservoirs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FractalGpuReservoirBudgetPlanner.Plan(0, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => FractalGpuReservoirBudgetPlanner.Plan(16, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => FractalGpuReservoirBudgetPlanner.Plan(16, 17, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => FractalGpuReservoirBudgetPlanner.Plan(16, 1, 0));
    }
}
