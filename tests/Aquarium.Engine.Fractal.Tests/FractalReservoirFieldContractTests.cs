using Aquarium.Engine.Render;

namespace Aquarium.Engine.Fractal.Tests;

public sealed class FractalReservoirFieldContractTests
{
    [Fact]
    public void SceneStateCarriesFractalReservoirFieldRequest()
    {
        var field = new AquariumFractalReservoirField
        {
            SplatCount = 2_000_000,
            ReservoirUpdatesPerPass = 50_000,
        };
        var scene = new AquariumSceneState
        {
            FractalReservoirField = field,
        };

        Assert.True(scene.FractalReservoirField.HasInput);
        Assert.Equal(8, scene.FractalReservoirField.Depth);
        Assert.Equal(2, scene.FractalReservoirField.CandidatesPerReservoirUpdate);
        Assert.Equal(0xA17EA11u, scene.FractalReservoirField.Seed);
    }

    [Fact]
    public void FractalReservoirFieldRejectsNonConvergingUpdateBudgets()
    {
        Assert.False(AquariumFractalReservoirField.Empty.HasInput);
        Assert.False(new AquariumFractalReservoirField { SplatCount = 16, ReservoirUpdatesPerPass = 0 }.HasInput);
        Assert.False(new AquariumFractalReservoirField { SplatCount = 16, ReservoirUpdatesPerPass = 17 }.HasInput);
        Assert.False(new AquariumFractalReservoirField { SplatCount = 16, ReservoirUpdatesPerPass = 4, CandidatesPerReservoirUpdate = 0 }.HasInput);
        Assert.False(new AquariumFractalReservoirField { SplatCount = 16, ReservoirUpdatesPerPass = 4, Depth = 0 }.HasInput);
    }
}
