using Aquarium.Engine.Audio;
using AquariumSynth.Dsl;

namespace Aquarium.Engine.Tests;

public sealed class SynthPlaybackTests
{
    [Fact]
    public void ClassicSfxrPresetsStayNearReferenceShapeThroughAquariumFaustPath()
    {
        var analyzer = new AudioAnalyzer(new AudioAnalysisConfig(
            FftSize: 512,
            HopSize: 256,
            MelBandCount: 24));

        var failures = new List<string>();
        foreach (var (name, script) in BuiltInScripts.ClassicSfxrPrimitiveGolfScripts)
        {
            if (!AquariumSynthHost.TryRenderScriptWithNativeFaust($"sfxr_{name}", script, 1.0f, out var samples, out var error))
            {
                failures.Add($"{name} failed to render through Aquarium Faust path: {error}");
                continue;
            }

            var reference = SfxrReferenceRenderer.Render(SfxrParams.Named(name) ?? throw new InvalidOperationException($"Missing SFXR preset {name}."));
            var comparison = analyzer.Compare(reference, samples);
            var analysis = analyzer.Analyze(samples);
            Console.WriteLine($"{name}: score={comparison.Score:0.000} logMel={comparison.LogMelDistance:0.000} envelope={comparison.EnvelopeDistance:0.000} duration={comparison.DurationRatio:0.000} rms={comparison.RmsRatio:0.000} zcr={comparison.ZeroCrossingRatio:0.000}");
            Check(samples.Length > 2048, $"{name} rendered too few samples: {samples.Length}");
            Check(analysis.Features.Peak is >= 0.005f and <= 1.0f, $"{name} peak out of range: {analysis.Features.Peak:0.000}");
            Check(analysis.Features.Rms is >= 0.0005f and <= 0.8f, $"{name} RMS out of range: {analysis.Features.Rms:0.000}");
            Check(analysis.Features.DurationSeconds is >= 0.015f and <= 1.4f, $"{name} duration out of range: {analysis.Features.DurationSeconds:0.000}");
            Check(analysis.Features.ZeroCrossingRate > 20.0f, $"{name} zero-crossing rate is suspiciously low: {analysis.Features.ZeroCrossingRate:0.000}");
            Check(comparison.LogMelDistance < 1.15f, $"{name} log-mel distance drifted too far from SFXR reference: {comparison.LogMelDistance:0.000}");
            Check(comparison.EnvelopeDistance < 0.90f, $"{name} envelope distance drifted too far from SFXR reference: {comparison.EnvelopeDistance:0.000}");
            Check(comparison.DurationRatio is >= 0.35f and <= 2.8f, $"{name} duration ratio out of range: {comparison.DurationRatio:0.000}");
            Check(comparison.RmsRatio is >= 0.05f and <= 12.0f, $"{name} RMS ratio out of range: {comparison.RmsRatio:0.000}");
            Check(comparison.ZeroCrossingRatio is >= 0.018f and <= 16.0f, $"{name} zero-crossing ratio out of range: {comparison.ZeroCrossingRatio:0.000}");
            Check(comparison.Score > 0.16f, $"{name} sounded unlike its SFXR reference: score={comparison.Score:0.000}, logMel={comparison.LogMelDistance:0.000}, envelope={comparison.EnvelopeDistance:0.000}, durationRatio={comparison.DurationRatio:0.000}, rmsRatio={comparison.RmsRatio:0.000}");
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));

        void Check(bool condition, string message)
        {
            if (!condition)
            {
                failures.Add(message);
            }
        }
    }

    private static class SfxrReferenceRenderer
    {
        private const int Frames = 44100;
        private const float Volume = 0.2f;
        private const float Pi = MathF.PI;

        public static float[] Render(SfxrParams parameters)
        {
            var oscillator = new Oscillator();
            var filter = new HighLowPassFilter();
            var envelope = new Envelope();
            var phaser = new Phaser();
            var output = new float[Frames];
            var repeatTime = 0;
            var repeatLimit = parameters.RepeatSpeed == 0.0f
                ? 0
                : (int)(Square(1.0f - parameters.RepeatSpeed) * 20000.0f * 32.0f);

            Restart(parameters, oscillator, filter);
            envelope.Reset(parameters.EnvAttack, parameters.EnvSustain, parameters.EnvDecay, parameters.EnvPunch);
            phaser.Reset(parameters.PhaOffset, parameters.PhaRamp);
            oscillator.ResetPhase();
            oscillator.ResetVibrato(parameters.VibSpeed, parameters.VibStrength);
            oscillator.ResetNoise();

            for (var index = 0; index < output.Length; index++)
            {
                repeatTime++;
                if (repeatLimit != 0 && repeatTime >= repeatLimit)
                {
                    repeatTime = 0;
                    Restart(parameters, oscillator, filter);
                }

                oscillator.Advance();
                envelope.Advance();
                phaser.Advance();

                var sample = 0.0f;
                for (var sub = 0; sub < 8; sub++)
                {
                    sample += phaser.Filter(filter.Filter(envelope.Filter(oscillator.Next())));
                }

                output[index] = Math.Clamp(sample / 8.0f * Volume, -1.0f, 1.0f);
            }

            return output;
        }

        private static void Restart(SfxrParams parameters, Oscillator oscillator, HighLowPassFilter filter)
        {
            filter.Reset(parameters.LpfResonance, parameters.LpfFreq, parameters.LpfRamp, parameters.HpfFreq, parameters.HpfRamp);
            oscillator.Reset(parameters.WaveType, parameters.BaseFreq, parameters.FreqLimit, parameters.FreqRamp, parameters.FreqDramp, parameters.Duty, parameters.DutyRamp, parameters.ArpSpeed, parameters.ArpMod);
        }

        private sealed class Oscillator
        {
            private readonly ReferenceRng rng = new(0x51f15eED);
            private readonly float[] noiseBuffer = new float[32];
            private Waveform waveType = Waveform.Square;
            private uint period = 8;
            private uint phase;
            private float squareDuty = 0.5f;
            private float squareSlide;
            private double floatingPeriod;
            private double maxPeriod;
            private double slide;
            private double deltaSlide;
            private double vibratoPhase;
            private double vibratoSpeed;
            private double vibratoAmplitude;
            private int arpeggioTime;
            private int arpeggioLimit;
            private double arpeggioMultiplier;

            public void ResetPhase()
            {
                phase = 0;
            }

            public void ResetNoise()
            {
                for (var index = 0; index < noiseBuffer.Length; index++)
                {
                    noiseBuffer[index] = rng.NextFloat() * 2.0f - 1.0f;
                }
            }

            public void ResetVibrato(float speed, float strength)
            {
                vibratoPhase = 0.0;
                vibratoSpeed = speed * speed * 0.01;
                vibratoAmplitude = strength * 0.5;
            }

            public void Reset(Waveform waveform, float baseFrequency, float frequencyLimit, float frequencyRamp, float frequencyDeltaRamp, float duty, float dutyRamp, float arpeggioSpeed, float arpeggioMod)
            {
                waveType = waveform;
                floatingPeriod = 100.0 / (baseFrequency * baseFrequency + 0.001);
                maxPeriod = 100.0 / (frequencyLimit * frequencyLimit + 0.001);
                slide = 1.0 - Cube(frequencyRamp) * 0.01;
                deltaSlide = -Cube(frequencyDeltaRamp) * 0.000001;
                squareDuty = 0.5f - duty * 0.5f;
                squareSlide = -dutyRamp * 0.00005f;
                arpeggioMultiplier = arpeggioMod >= 0.0f
                    ? 1.0 - arpeggioMod * arpeggioMod * 0.9
                    : 1.0 - arpeggioMod * arpeggioMod * 10.0;
                arpeggioTime = 0;
                arpeggioLimit = (int)(Square(1.0f - arpeggioSpeed) * 20000.0f + 32.0f);
                if (arpeggioSpeed == 1.0f)
                {
                    arpeggioLimit = 0;
                }
            }

            public void Advance()
            {
                arpeggioTime++;
                if (arpeggioLimit != 0 && arpeggioTime >= arpeggioLimit)
                {
                    arpeggioLimit = 0;
                    floatingPeriod *= arpeggioMultiplier;
                }

                slide += deltaSlide;
                floatingPeriod = Math.Min(floatingPeriod * slide, maxPeriod);
                vibratoPhase += vibratoSpeed;
                var vibrato = 1.0 + Math.Sin(vibratoPhase) * vibratoAmplitude;
                period = Math.Max((uint)(vibrato * floatingPeriod), 8u);
                squareDuty = Math.Clamp(squareDuty + squareSlide, 0.0f, 0.5f);
            }

            public float Next()
            {
                phase++;
                if (phase >= period)
                {
                    phase %= period;
                    if (waveType == Waveform.Noise)
                    {
                        ResetNoise();
                    }
                }

                var position = phase / (float)period;
                return waveType switch
                {
                    Waveform.Square => position < squareDuty ? 0.5f : -0.5f,
                    Waveform.Triangle => 1.0f - position * 2.0f,
                    Waveform.Sine => MathF.Sin(position * 2.0f * Pi),
                    Waveform.Noise => noiseBuffer[Math.Min((int)(position * 32.0f), noiseBuffer.Length - 1)],
                    Waveform.Sawtooth => 2.0f * position - 1.0f,
                    _ => 0.0f
                };
            }
        }

        private sealed class Envelope
        {
            private EnvelopeStage stage = EnvelopeStage.Attack;
            private uint stageLeft;
            private uint attack;
            private uint sustain;
            private uint decay;
            private float punch;

            public void Reset(float attackSeconds, float sustainSeconds, float decaySeconds, float punchAmount)
            {
                attack = (uint)(attackSeconds * attackSeconds * 100000.0f);
                sustain = (uint)(sustainSeconds * sustainSeconds * 100000.0f);
                decay = (uint)(decaySeconds * decaySeconds * 100000.0f);
                punch = punchAmount;
                stage = EnvelopeStage.Attack;
                stageLeft = CurrentStageLength();
            }

            public void Advance()
            {
                if (stageLeft > 1)
                {
                    stageLeft--;
                    return;
                }

                stage = stage switch
                {
                    EnvelopeStage.Attack => EnvelopeStage.Sustain,
                    EnvelopeStage.Sustain => EnvelopeStage.Decay,
                    EnvelopeStage.Decay => EnvelopeStage.End,
                    _ => EnvelopeStage.End
                };
                stageLeft = CurrentStageLength();
            }

            public float Filter(float sample)
            {
                return sample * Volume();
            }

            private uint CurrentStageLength() => stage switch
            {
                EnvelopeStage.Attack => attack,
                EnvelopeStage.Sustain => sustain,
                EnvelopeStage.Decay => decay,
                _ => 0
            };

            private float Volume()
            {
                var length = CurrentStageLength();
                if (length == 0)
                {
                    return stage == EnvelopeStage.End ? 0.0f : 1.0f;
                }

                var t = stageLeft / (float)length;
                return stage switch
                {
                    EnvelopeStage.Attack => 1.0f - t,
                    EnvelopeStage.Sustain => 1.0f + t * 2.0f * punch,
                    EnvelopeStage.Decay => t,
                    _ => 0.0f
                };
            }
        }

        private sealed class HighLowPassFilter
        {
            private float lowPass;
            private float lowPassDelta;
            private float lowPassCutoff;
            private float lowPassCutoffDelta;
            private float lowPassDamping;
            private float highPass;
            private float highPassCutoff;
            private float highPassCutoffDelta;

            public void Reset(float lowPassResonance, float lowPassFrequency, float lowPassRamp, float highPassFrequency, float highPassRamp)
            {
                lowPass = 0.0f;
                lowPassDelta = 0.0f;
                lowPassCutoff = Cube(lowPassFrequency) * 0.1f;
                lowPassCutoffDelta = 1.0f + lowPassRamp * 0.0001f;
                lowPassDamping = 5.0f / (1.0f + lowPassResonance * lowPassResonance * 20.0f) * (0.01f + lowPassCutoff);
                lowPassDamping = Math.Min(lowPassDamping, 0.8f);
                highPass = 0.0f;
                highPassCutoff = highPassFrequency * highPassFrequency * 0.1f;
                highPassCutoffDelta = 1.0f + highPassRamp * 0.0003f;
            }

            public float Filter(float sample)
            {
                var previousLowPass = lowPass;
                if (lowPassCutoff > 0.0f)
                {
                    lowPassCutoff = Math.Clamp(lowPassCutoff * lowPassCutoffDelta, 0.0f, 0.1f);
                    lowPassDelta += (sample - lowPass) * lowPassCutoff;
                    lowPassDelta -= lowPassDelta * lowPassDamping;
                }
                else
                {
                    lowPass = sample;
                    lowPassDelta = 0.0f;
                }

                lowPass += lowPassDelta;
                highPassCutoff = Math.Clamp(highPassCutoff * highPassCutoffDelta, 0.00001f, 0.1f);
                highPass += lowPass - previousLowPass;
                highPass -= highPass * highPassCutoff;
                return highPass;
            }
        }

        private sealed class Phaser
        {
            private readonly float[] buffer = new float[1024];
            private int index;
            private float phase;
            private float phaseDelta;

            public void Reset(float offset, float ramp)
            {
                phase = offset * offset * 1020.0f;
                if (offset < 0.0f)
                {
                    phase = -phase;
                }

                phaseDelta = ramp * ramp;
                if (ramp < 0.0f)
                {
                    phaseDelta = -phaseDelta;
                }
            }

            public void Advance()
            {
                phase += phaseDelta;
            }

            public float Filter(float sample)
            {
                buffer[index % buffer.Length] = sample;
                var phaseIndex = Math.Min((int)MathF.Abs(phase), buffer.Length - 1);
                var result = sample + buffer[(index + buffer.Length - phaseIndex) % buffer.Length];
                index = (index + 1) % buffer.Length;
                return result;
            }
        }

        private sealed class ReferenceRng(uint seed)
        {
            private uint state = seed;

            public float NextFloat()
            {
                state = unchecked(state * 1664525u + 1013904223u);
                return (state >> 8) / 16777216.0f;
            }
        }

        private enum EnvelopeStage
        {
            Attack,
            Sustain,
            Decay,
            End
        }

        private static float Square(float value) => value * value;
        private static float Cube(float value) => value * value * value;
    }
}
