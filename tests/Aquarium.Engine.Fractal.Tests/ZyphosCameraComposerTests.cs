using Aquarium.Zyphos;

namespace Aquarium.Engine.Fractal.Tests;

public sealed class ZyphosCameraComposerTests
{
    [Fact]
    public void PlanetFrameLooksAtZyphosCenter()
    {
        var shot = ZyphosCameraComposer.Compose(ZyphosCameraFrame.Planet, -0.2f, 0.3f, 16.0f, 0.0f);

        Assert.Equal(ZyphosUmbrosSystem.ZyphosCenter, shot.CameraTarget);
        Assert.Equal(ZyphosUmbrosSystem.ZyphosCenter, shot.ParentAnchor);
    }

    [Fact]
    public void UmbrosFrameKeepsParentAsCompositionAnchor()
    {
        var shot = ZyphosCameraComposer.Compose(ZyphosCameraFrame.Umbros, -0.2f, 0.3f, 16.0f, 0.0f);

        Assert.Equal(ZyphosUmbrosSystem.ZyphosCenter, shot.CameraTarget);
        Assert.Equal(ZyphosUmbrosSystem.ZyphosCenter, shot.ParentAnchor);
        Assert.Equal(ZyphosUmbrosSystem.UmbrosCenter(0.0f), shot.TrackedCenter);
        Assert.True(shot.EffectiveDistance > ZyphosUmbrosSystem.CenterSeparation);
    }

    [Fact]
    public void BinaryFrameTracksMidpointWhilePivotingAroundParent()
    {
        var shot = ZyphosCameraComposer.Compose(ZyphosCameraFrame.Binary, 0.1f, 0.4f, 16.0f, 0.0f);
        var midpoint = ZyphosUmbrosSystem.ZyphosCenter + (ZyphosUmbrosSystem.UmbrosCenter(0.0f) - ZyphosUmbrosSystem.ZyphosCenter) * 0.5f;

        Assert.Equal(ZyphosUmbrosSystem.ZyphosCenter, shot.CameraTarget);
        Assert.Equal(midpoint, shot.TrackedCenter);
    }
}
