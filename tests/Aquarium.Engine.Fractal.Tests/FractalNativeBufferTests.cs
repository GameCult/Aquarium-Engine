using Aquarium.Engine.Fractal;
using Aquarium.Engine.Fractal.Lod;

namespace Aquarium.Engine.Fractal.Tests;

public sealed class FractalNativeBufferTests
{
    [Fact]
    public unsafe void NativeBufferExposesPointerAndSpanWithoutCopy()
    {
        using var buffer = new FractalNativeBuffer<AquariumPackedFractalSdfSplat3D>(4);

        buffer.Pointer[2] = buffer.Pointer[2] with { CenterRadius = new System.Numerics.Vector4(1.0f, 2.0f, 3.0f, 4.0f) };

        Assert.Equal(4, buffer.Length);
        Assert.Equal(1.0f, buffer.Span[2].CenterRadius.X);
    }

    [Fact]
    public void NativeBufferRejectsInvalidAlignment()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FractalNativeBuffer<int>(4, alignment: 48));
    }
}
