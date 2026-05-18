using Aquarium.Zyphos;

namespace Aquarium.Engine.Fractal.Tests;

public sealed class ZyphosFractalTerrainTests
{
    [Fact]
    public void ZyphosTerrainCompilesDslIntoRenderableBrushes()
    {
        var tree = ZyphosFractalTerrain.OwnershipTree;
        var brushes = ZyphosFractalTerrain.HeightBrushes;

        Assert.EndsWith(".aquageo", ZyphosFractalTerrain.PatchPath, StringComparison.Ordinal);
        Assert.True(File.Exists(ZyphosFractalTerrain.PatchPath), $"Expected copied aquageo patch at {ZyphosFractalTerrain.PatchPath}.");
        Assert.Equal(12, tree.Claims.Count);
        Assert.Equal(tree.Claims.Count, brushes.Length);
        Assert.Contains(tree.Claims, claim => claim.Tags == "crater");
        Assert.Contains(tree.Claims, claim => claim.Tags == "ridge");
        Assert.Contains(brushes, brush => brush.EnvelopeFalloff > 0.0f && brush.RadiusY > 0.0f);
    }
}
