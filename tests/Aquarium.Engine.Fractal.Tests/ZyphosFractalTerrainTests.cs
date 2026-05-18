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
        Assert.Contains(tree.Domains, domain => domain.Kind == AquariumFractalDomainKind.Solar && domain.Key.Value == "zyphos/parent-star");
        Assert.Contains(tree.Domains, domain => domain.Kind == AquariumFractalDomainKind.Orbital && domain.Key.Value == "zyphos/zyphos-umbros-orbit");
        Assert.Contains(tree.Domains, domain => domain.Kind == AquariumFractalDomainKind.Planetary && domain.Key.Value == "zyphos/umbros");
        Assert.Contains(tree.Domains, domain => domain.Kind == AquariumFractalDomainKind.LatLong && domain.Key.Value == "zyphos/planet-latlong");
        Assert.Contains(tree.Domains, domain => domain.Kind == AquariumFractalDomainKind.LatLong && domain.Key.Value == "zyphos/umbros-latlong");
        Assert.Contains(tree.Domains, domain => domain.Kind == AquariumFractalDomainKind.Surface2D && domain.Key.Value == "zyphos/equatorial-forest");
        Assert.Contains(tree.Domains, domain => domain.Kind == AquariumFractalDomainKind.Object3D && domain.Key.Value == "zyphos/canopy-leaf");
        Assert.Contains(tree.Domains, domain => domain.Kind == AquariumFractalDomainKind.Object3D && domain.Key.Value == "umbros/pebble-field");
        Assert.Contains(tree.Domains, domain => domain.Kind == AquariumFractalDomainKind.CubeSphereTile && domain.ParentKey.Value == "zyphos/canopy-leaf");
        Assert.Contains(tree.Domains, domain => domain.Kind == AquariumFractalDomainKind.CubeSphereTile && domain.ParentKey.Value == "umbros/pebble-field");
        Assert.Equal(new AquariumFractalKey("umbros/pebble-field"), tree.Domain.ParentKey);
        Assert.Equal(58, tree.Claims.Count);
        Assert.Equal(tree.Claims.Count, brushes.Length);
        Assert.Contains(tree.Claims, claim => claim.Tags == "crater");
        Assert.Contains(tree.Claims, claim => claim.Tags == "ridge");
        Assert.Contains(tree.Claims, claim => claim.Tags == "shard");
        Assert.Contains(tree.Claims, claim => claim.Tags == "crown");
        Assert.Contains(tree.Claims, claim => claim.Tags == "leaf");
        Assert.Contains(tree.Claims, claim => claim.Tags == "pebble");
        Assert.Contains(brushes, brush => brush.EnvelopeFalloff > 0.0f && brush.RadiusY > 0.0f);
    }
}
