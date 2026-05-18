using Aquarium.Engine.Audio;
using AquaSynth.Faust;

namespace Aquarium.Engine.Tests;

public sealed class SynthPlaybackTests
{
    [Fact]
    public void AquaSynthPatchCompilerCanRenderAudiblePatchForEnginePlayback()
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

        using var compiler = new AquaSynthPatchCompiler();
        if (!compiler.TryCompileScript(new AquaSynthCompileIdentity("engine_synth_smoke", "engine_synth_smoke", script), out var patch, out var error))
        {
            if (error?.Contains("Faust toolchain not found", StringComparison.OrdinalIgnoreCase) == true ||
                error?.Contains("Faust DLL not found", StringComparison.OrdinalIgnoreCase) == true)
            {
                return;
            }

            Assert.Fail($"AquaSynth patch compiler failed to render a tiny patch: {error}");
        }

        using (patch)
        {
            var samples = patch!.Render(1.0f);
            Assert.True(samples.Length > 2048, $"Rendered too few samples: {samples.Length}.");
            Assert.Contains(samples, sample => MathF.Abs(sample) > 0.001f);
            Assert.InRange(samples.Max(sample => MathF.Abs(sample)), 0.001f, 1.0f);
        }
    }
}
