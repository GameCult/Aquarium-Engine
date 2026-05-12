using Aquarium.Engine.Audio;
using AquariumSynth.Dsl;

namespace Aquarium.Engine.Tests;

public sealed class SynthPlaybackTests
{
    [Fact]
    public void ClassicSfxrPresetsRenderAudibleDistinctShapesThroughAquariumFaustPath()
    {
        var analyzer = new AudioAnalyzer(new AudioAnalysisConfig(
            FftSize: 512,
            HopSize: 256,
            MelBandCount: 24));
        var renders = new List<(string Name, float[] Samples, AudioAnalysis Analysis)>();

        foreach (var (name, script) in BuiltInScripts.ClassicSfxrPrimitiveGolfScripts)
        {
            if (!AquariumSynthHost.TryRenderScriptWithNativeFaust($"sfxr_{name}", script, 1.0f, out var samples, out var error))
            {
                Assert.Fail($"{name} failed to render through Aquarium Faust path: {error}");
            }

            var analysis = analyzer.Analyze(samples);
            renders.Add((name, samples, analysis));
            Assert.True(samples.Length > 2048, $"{name} rendered too few samples: {samples.Length}");
            Assert.InRange(analysis.Features.Peak, 0.005f, 1.0f);
            Assert.InRange(analysis.Features.Rms, 0.0005f, 0.8f);
            Assert.InRange(analysis.Features.DurationSeconds, 0.015f, 1.4f);
            Assert.True(analysis.Features.ZeroCrossingRate > 20.0f, $"{name} zero-crossing rate is suspiciously low: {analysis.Features.ZeroCrossingRate}");
        }

        Assert.Equal(BuiltInScripts.ClassicSfxrPrimitiveGolfScripts.Count, renders.Count);
        for (var index = 1; index < renders.Count; index++)
        {
            var previous = renders[index - 1];
            var current = renders[index];
            var comparison = analyzer.Compare(previous.Samples, current.Samples);

            Assert.True(
                comparison.Score < 0.98f,
                $"{previous.Name} and {current.Name} rendered nearly identical envelopes; score={comparison.Score:0.000}");
        }
    }
}
