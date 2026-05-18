using Aquarium.Engine.Fractal;

namespace Aquarium.Engine.Fractal.Tests;

public sealed class FractalBoundaryTests
{
    [Fact]
    public void FractalAssemblyDoesNotReferenceRendererOrD3D12Assemblies()
    {
        var forbiddenPrefixes = new[]
        {
            "Aquarium.Engine",
            "Vortice.",
        };

        var references = typeof(CubeTileKey)
            .Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty);

        foreach (var reference in references)
        {
            Assert.DoesNotContain(forbiddenPrefixes, prefix => reference.StartsWith(prefix, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void TestDoublesProvideDeterministicMockBoundaries()
    {
        var clock = new TestFractalClock(4.0, 9);
        var random = new TestFractalRandom(0.25, 0.75);
        var sink = new TestFractalDebugSink();

        sink.Record("score", "node-a", random.NextDouble() + clock.TimeSeconds + clock.FrameIndex);

        Assert.Equal(4.0, clock.TimeSeconds);
        Assert.Equal<ulong>(9, clock.FrameIndex);
        Assert.Equal(13.25, sink.Values[("score", "node-a")]);
    }
}
