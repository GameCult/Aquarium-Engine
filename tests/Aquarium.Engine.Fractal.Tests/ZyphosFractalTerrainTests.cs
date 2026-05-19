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
        Assert.Equal(7, tree.Nodes.Count);
        Assert.Equal(61, tree.Claims.Count);
        Assert.Equal(tree.Claims.Count, brushes.Length);
        Assert.Contains(tree.Claims, claim => claim.Tags == "crater");
        Assert.Contains(tree.Claims, claim => claim.Tags == "ridge");
        Assert.Contains(tree.Claims, claim => claim.Tags == "shard");
        Assert.Contains(tree.Claims, claim => claim.Tags == "crown");
        Assert.Contains(tree.Claims, claim => claim.Tags == "leaf");
        Assert.Contains(tree.Claims, claim => claim.Tags == "flame-leaf");
        Assert.Contains(tree.Claims, claim => claim.Tags == "pebble");
        Assert.Contains(brushes, brush => brush.EnvelopeFalloff > 0.0f && brush.RadiusY > 0.0f);
    }

    [Fact]
    public void ZyphosFractalRenderPlanUsesCameraContributionPressure()
    {
        var nearShot = ZyphosCameraComposer.Compose(ZyphosSpatialDomainCatalog.CanopyLeaf, 0.1f, 0.4f, 0.1f, 0.0f);
        var farShot = ZyphosCameraComposer.Compose(ZyphosSpatialDomainCatalog.Solar, 0.1f, 0.4f, 80.0f, 0.0f);

        var near = ZyphosFractalTerrain.BuildRenderPlan(nearShot);
        var far = ZyphosFractalTerrain.BuildRenderPlan(farShot);

        Assert.True(near.PixelsPerWorld > far.PixelsPerWorld);
        Assert.Equal(ZyphosFractalTerrain.SelectedCuts.Length, near.SelectedCuts.Length);
        Assert.Equal(ZyphosFractalTerrain.HeightBrushes.Length, near.HeightBrushes.Length);
        Assert.Contains("px-wu", near.Summary, StringComparison.Ordinal);
        Assert.Contains("cpu updates", near.Summary, StringComparison.Ordinal);
        Assert.Contains("gpu cost", near.Summary, StringComparison.Ordinal);
        Assert.Contains("resident", near.Summary, StringComparison.Ordinal);
        Assert.Contains("probe candidates", near.Summary, StringComparison.Ordinal);
        Assert.Contains("surface payloads", near.Summary, StringComparison.Ordinal);
        Assert.True(near.StructuralProbeReservoir.HasSample);
        Assert.True(near.StructuralProbeReservoir.CandidateCount > 0);
        Assert.True(near.StructuralProbeReservoir.WeightSum > 0.0f);
        Assert.Single(near.StructuralSdfSplats);
        Assert.Equal(near.StructuralProbeReservoir.NodeKey, near.StructuralSdfSplats[0].Key.NodeKey);
        Assert.Equal(near.SelectedCuts.Length * 4, near.SurfacePages.Length);
        Assert.Contains(near.SurfacePages, page => page.Key.Kind == AquariumFractalSurfacePageKind.SignedDistance2D);
        Assert.True(near.SurfacePageResidency.ResidentPages.Count <= near.SurfacePages.Length);
        Assert.True(near.SurfacePageResidency.ResidentBytes <= 2L * 1024L * 1024L);
        Assert.True(near.SurfacePageResidency.RequestedPages.Count <= 8);
        Assert.Equal(near.SurfacePageResidency.ResidentPages.Count, near.SurfacePagePayloads.Length);
        Assert.All(near.SurfacePagePayloads, payload => Assert.Equal(payload.Page.Width * payload.Page.Height, payload.Samples.Length));
        Assert.True(near.ProjectedSurfaceSdfSplats.Length <= 64);
        Assert.NotEmpty(near.ProjectedSurfaceSdfSplats);
        Assert.All(near.SurfacePages, page =>
        {
            Assert.Equal(128, page.Width);
            Assert.Equal(128, page.Height);
            Assert.Contains(near.SelectedCuts, cut => cut.NodeKey == page.Key.NodeKey);
        });
        Assert.Empty(near.ResourcePlan.Residency.RequestedNodes);
        Assert.True(MathF.Abs(near.HeightBrushes[0].Amplitude) * MathF.Max(near.HeightBrushes[0].Radius, near.HeightBrushes[0].RadiusY) >=
            MathF.Abs(near.HeightBrushes[^1].Amplitude) * MathF.Max(near.HeightBrushes[^1].Radius, near.HeightBrushes[^1].RadiusY));
    }

    [Fact]
    public void ZyphosFractalPlanDebugDumpReportsResourceBudgets()
    {
        var shot = ZyphosCameraComposer.Compose(ZyphosSpatialDomainCatalog.CanopyLeaf, 0.1f, 0.4f, 0.1f, 0.0f);
        var dump = ZyphosFractalTerrain.BuildPlanDebugDump(ZyphosFractalTerrain.BuildRenderPlan(shot));

        Assert.Contains("Zyphos fractal resource plan", dump, StringComparison.Ordinal);
        Assert.Contains("cpuUpdates:", dump, StringComparison.Ordinal);
        Assert.Contains("gpuEstimatedCost:", dump, StringComparison.Ordinal);
        Assert.Contains("ramResident:", dump, StringComparison.Ordinal);
        Assert.Contains("ssdRequests:", dump, StringComparison.Ordinal);
        Assert.Contains("evictions:", dump, StringComparison.Ordinal);
        Assert.Contains("structuralProbe:", dump, StringComparison.Ordinal);
        Assert.Contains("structuralProbeWeight:", dump, StringComparison.Ordinal);
        Assert.Contains("structuralSdfSplats:", dump, StringComparison.Ordinal);
        Assert.Contains("surfacePages:", dump, StringComparison.Ordinal);
        Assert.Contains("surfacePageResident:", dump, StringComparison.Ordinal);
        Assert.Contains("surfacePagePayloads:", dump, StringComparison.Ordinal);
        Assert.Contains("projectedSurfaceSdfSplats:", dump, StringComparison.Ordinal);
        Assert.Contains("surfacePageRequests:", dump, StringComparison.Ordinal);
        Assert.Contains("surfacePageEvictions:", dump, StringComparison.Ordinal);
        Assert.Contains("selected:", dump, StringComparison.Ordinal);
    }
}
