using Aquarium.Engine.Audio;
using AquaSynth.Faust;

namespace Aquarium.Engine.Tests;

public sealed class SynthPlaybackTests
{
    [Fact]
    public void AquaSynthRenderSessionCanRenderAudiblePatchForEnginePlayback()
    {
        const string script = """
            voice
                wave=sine
                freq=440
                gain=0.2
                attack=0.001
                sustain=0.06
                decay=0.12
            """;

        using var session = new AquaSynthRenderSession();
        if (!session.TryRenderScript("engine_synth_smoke", script, 1.0f, out var samples, out var error))
        {
            if (error?.Contains("Faust toolchain not found", StringComparison.OrdinalIgnoreCase) == true ||
                error?.Contains("Faust DLL not found", StringComparison.OrdinalIgnoreCase) == true)
            {
                return;
            }

            Assert.Fail($"AquaSynth render session failed to render a tiny patch: {error}");
        }

        Assert.True(samples.Length > 2048, $"Rendered too few samples: {samples.Length}.");
        Assert.Contains(samples, sample => MathF.Abs(sample) > 0.001f);
        Assert.InRange(samples.Max(sample => MathF.Abs(sample)), 0.001f, 1.0f);
    }
}
