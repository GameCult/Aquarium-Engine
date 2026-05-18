using Aquarium.Zyphos;

namespace Aquarium.Engine.Fractal.Tests;

public sealed class ZyphosCameraComposerTests
{
    [Fact]
    public void PlanetFrameLooksAtZyphosCenter()
    {
        var shot = ZyphosCameraComposer.Compose(ZyphosSpatialDomainCatalog.Planet, -0.2f, 0.3f, 16.0f, 0.0f);
        var orbitalCenter = ZyphosUmbrosSystem.ZyphosCenter + (ZyphosUmbrosSystem.UmbrosCenter(0.0f) - ZyphosUmbrosSystem.ZyphosCenter) * 0.5f;

        Assert.Equal(orbitalCenter, shot.CameraTarget);
        Assert.Equal(orbitalCenter, shot.ParentAnchor);
        Assert.Equal(ZyphosUmbrosSystem.ZyphosCenter, shot.TrackedCenter);
        Assert.Equal(ZyphosSpatialDomainCatalog.Orbital, shot.ParentDomainKey);
    }

    [Fact]
    public void UmbrosFrameKeepsParentAsCompositionAnchor()
    {
        var shot = ZyphosCameraComposer.Compose(ZyphosSpatialDomainCatalog.Umbros, -0.2f, 0.3f, 16.0f, 0.0f);
        var orbitalCenter = ZyphosUmbrosSystem.ZyphosCenter + (ZyphosUmbrosSystem.UmbrosCenter(0.0f) - ZyphosUmbrosSystem.ZyphosCenter) * 0.5f;

        Assert.Equal(orbitalCenter, shot.CameraTarget);
        Assert.Equal(orbitalCenter, shot.ParentAnchor);
        Assert.Equal(ZyphosUmbrosSystem.UmbrosCenter(0.0f), shot.TrackedCenter);
        Assert.True(shot.EffectiveDistance > ZyphosUmbrosSystem.CenterSeparation);
    }

    [Fact]
    public void BinaryFrameTracksMidpointWhilePivotingAroundParent()
    {
        var shot = ZyphosCameraComposer.Compose(ZyphosSpatialDomainCatalog.Orbital, 0.1f, 0.4f, 16.0f, 0.0f);
        var midpoint = ZyphosUmbrosSystem.ZyphosCenter + (ZyphosUmbrosSystem.UmbrosCenter(0.0f) - ZyphosUmbrosSystem.ZyphosCenter) * 0.5f;

        Assert.Equal(ZyphosUmbrosSystem.PrimaryStarCenter(0.0f), shot.CameraTarget);
        Assert.Equal(midpoint, shot.TrackedCenter);
    }

    [Fact]
    public void SurfaceTilePivotsThroughLatLongParent()
    {
        var shot = ZyphosCameraComposer.Compose(ZyphosSpatialDomainCatalog.TerrainTile, 0.1f, 0.4f, 16.0f, 0.0f);

        Assert.Equal(ZyphosSpatialDomainCatalog.ContinentArchipelago, shot.ParentDomainKey);
        Assert.NotEqual(ZyphosUmbrosSystem.ZyphosCenter, shot.CameraTarget);
        Assert.True(shot.TrackedCenter.Z > shot.CameraTarget.Z);
    }
}
