using System.Numerics;
using Aquarium.Zyphos;

namespace Aquarium.Engine.Fractal.Tests;

public sealed class ZyphosUmbrosSystemTests
{
    [Fact]
    public void UmbrosMatchesWorkingBinaryBaselineScale()
    {
        Assert.Equal(0.9f, ZyphosUmbrosSystem.UmbrosRadiusRatio);
        Assert.Equal(8.0f, ZyphosUmbrosSystem.SeparationInZyphosRadii);
        Assert.InRange(ZyphosUmbrosSystem.UmbrosAngularDiameterDegrees, 12.8f, 13.0f);
    }

    [Fact]
    public void UmbrosDirectionIsFixedInPlanetLocalFrame()
    {
        var firstDirection = ZyphosUmbrosSystem.UmbrosDirection(0.0f);
        var laterDirection = ZyphosUmbrosSystem.UmbrosDirection(100.0f);

        Assert.Equal(Vector3.UnitX, firstDirection);
        Assert.Equal(Vector3.UnitX, laterDirection);
    }

    [Fact]
    public void UmbrosCenterUsesFixedSkyBinarySeparation()
    {
        var offset = ZyphosUmbrosSystem.UmbrosCenter(0.0f) - ZyphosUmbrosSystem.ZyphosCenter;
        var laterOffset = ZyphosUmbrosSystem.UmbrosCenter(100.0f) - ZyphosUmbrosSystem.ZyphosCenter;

        Assert.Equal(ZyphosUmbrosSystem.CenterSeparation, offset.Length(), 4);
        Assert.Equal(offset, laterOffset);
        Assert.True(ZyphosUmbrosSystem.CenterSeparation > ZyphosUmbrosSystem.ZyphosSurfaceRadius * 7.9f);
    }
}
