using System.Runtime.InteropServices;
using Aquarium.Engine.Audio;
using AquariumSynth.Dsl;

namespace Aquarium.Engine.Audio;

internal sealed class AquariumSynthHost : IDisposable
{
    private const int SampleRate = 44100;
    private const int ChannelCount = 1;
    private const int BitsPerSample = 16;
    private const int MaxPlayingSounds = 16;

    private readonly Dictionary<string, CachedSound> cache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> faustCompileRevisions = new(StringComparer.Ordinal);
    private readonly List<PlayingSound> playing = [];
    private float timeSeconds;

    public void Update(AquariumSynthDocument synth, float deltaSeconds)
    {
        timeSeconds += Math.Max(deltaSeconds, 0.0f);
        if (!synth.Enabled)
        {
            return;
        }

        foreach (var patch in synth.Patches)
        {
            RequestFaustCompileIfNeeded(patch);
            if (ShouldTrigger(patch.Trigger, timeSeconds - deltaSeconds, timeSeconds))
            {
                Play(patch, synth.MasterGain);
            }
        }
    }

    private void RequestFaustCompileIfNeeded(AquariumSynthPatch patch)
    {
        if (patch.FaustCompileRevision <= 0)
        {
            return;
        }

        if (faustCompileRevisions.TryGetValue(patch.Id, out var revision) && revision >= patch.FaustCompileRevision)
        {
            return;
        }

        faustCompileRevisions[patch.Id] = patch.FaustCompileRevision;
        var script = patch.Script;
        var patchId = patch.Id;
        var faustName = patch.FaustName;
        _ = Task.Run(async () =>
        {
            try
            {
                var export = FaustEmitter.EmitScript(script, new FaustExportOptions(SafeFaustName(faustName)));
                var outputRoot = Path.Combine(AppContext.BaseDirectory, "Synth");
                Directory.CreateDirectory(outputRoot);
                var dspPath = Path.Combine(outputRoot, $"{SafeFileName(patchId)}.dsp");
                var csharpPath = Path.Combine(outputRoot, $"{SafeFileName(patchId)}.cs");
                await File.WriteAllTextAsync(dspPath, export.Source);
                var validation = await FaustCompiler.CompileAsync(
                    export.Source,
                    new FaustCompileOptions(FaustTargetLanguage.CSharp, csharpPath));
                if (validation is null)
                {
                    Console.WriteLine($"Aquarium synth patch `{patchId}` exported Faust DSP; Faust compiler not found.");
                }
                else if (validation.Success)
                {
                    Console.WriteLine($"Aquarium synth patch `{patchId}` compiled Faust C# output.");
                }
                else
                {
                    Console.WriteLine($"Aquarium synth patch `{patchId}` Faust compile failed: {validation.Stderr}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Aquarium synth patch `{patchId}` Faust export failed: {ex.Message}");
            }
        });
    }

    private bool ShouldTrigger(AquariumSynthTrigger trigger, float previousTime, float currentTime)
    {
        var interval = MathF.Max(trigger.IntervalSeconds, 0.001f);
        var previousBeat = MathF.Floor((previousTime - trigger.PhaseSeconds) / interval);
        var currentBeat = MathF.Floor((currentTime - trigger.PhaseSeconds) / interval);
        return currentBeat > previousBeat;
    }

    private void Play(AquariumSynthPatch patch, float masterGain)
    {
        var sound = GetSound(patch, masterGain);
        if (sound.Bytes.Length == 0)
        {
            return;
        }

        while (playing.Count >= MaxPlayingSounds)
        {
            playing[0].Dispose();
            playing.RemoveAt(0);
        }

        var instance = new PlayingSound(sound.Bytes);
        playing.Add(instance);
        instance.Play();
    }

    private CachedSound GetSound(AquariumSynthPatch patch, float masterGain)
    {
        var cacheKey = $"{patch.Id}:{patch.Gain}:{masterGain}:{patch.Script}";
        if (cache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        try
        {
            var synthPatch = PatchScript.Parse(patch.Script);
            var samples = SynthPatchRenderer.Render(synthPatch, patch.Gain * masterGain, SampleRate);
            cached = new CachedSound(WriteWav(samples));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Aquarium synth patch `{patch.Id}` failed: {ex.Message}");
            cached = new CachedSound([]);
        }

        cache[cacheKey] = cached;
        return cached;
    }

    private static byte[] WriteWav(ReadOnlySpan<float> samples)
    {
        var dataBytes = checked(samples.Length * sizeof(short));
        using var stream = new MemoryStream(44 + dataBytes);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(36 + dataBytes);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)ChannelCount);
        writer.Write(SampleRate);
        writer.Write(SampleRate * ChannelCount * BitsPerSample / 8);
        writer.Write((short)(ChannelCount * BitsPerSample / 8));
        writer.Write((short)BitsPerSample);
        writer.Write("data"u8);
        writer.Write(dataBytes);
        foreach (var sample in samples)
        {
            writer.Write((short)Math.Clamp(sample * short.MaxValue, short.MinValue, short.MaxValue));
        }

        return stream.ToArray();
    }

    private static string SafeFaustName(string value)
    {
        var chars = value.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray();
        var name = new string(chars).Trim('_');
        return string.IsNullOrWhiteSpace(name) ? "aquarium_patch" : name;
    }

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var chars = value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        var name = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(name) ? "aquarium_patch" : name;
    }

    public void Dispose()
    {
        foreach (var sound in playing)
        {
            sound.Dispose();
        }

        playing.Clear();
    }

    private sealed record CachedSound(byte[] Bytes);

    private sealed class PlayingSound : IDisposable
    {
        private readonly GCHandle handle;

        public PlayingSound(byte[] bytes)
        {
            handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        }

        public void Play()
        {
            _ = PlaySound(handle.AddrOfPinnedObject(), IntPtr.Zero, PlaySoundFlags.Memory | PlaySoundFlags.Async | PlaySoundFlags.NoDefault);
        }

        public void Dispose()
        {
            if (handle.IsAllocated)
            {
                handle.Free();
            }
        }
    }

    [Flags]
    private enum PlaySoundFlags : uint
    {
        Async = 0x0001,
        Memory = 0x0004,
        NoDefault = 0x0002,
    }

    [DllImport("winmm.dll", SetLastError = true)]
    private static extern bool PlaySound(IntPtr pszSound, IntPtr hmod, PlaySoundFlags fdwSound);
}

internal static class SynthPatchRenderer
{
    public static float[] Render(SynthPatch patch, float gain, int sampleRate)
    {
        var duration = patch.Voices.Count == 0
            ? 0.0f
            : patch.Voices.Max(voice => voice.Envelope.DurationSeconds + 0.08f);
        duration = Math.Clamp(duration, 0.05f, 3.0f);
        var samples = new float[(int)MathF.Ceiling(duration * sampleRate)];
        var random = new Random(1337);
        for (var index = 0; index < samples.Length; index++)
        {
            var t = index / (float)sampleRate;
            var value = 0.0f;
            foreach (var voice in patch.Voices)
            {
                value += RenderVoice(voice, t, random);
            }

            value *= patch.Gain * gain;
            samples[index] = patch.SoftClip ? MathF.Tanh(value * 1.35f) : Math.Clamp(value, -1.0f, 1.0f);
        }

        return samples;
    }

    private static float RenderVoice(Voice voice, float timeSeconds, Random random)
    {
        var envelope = EnvelopeAt(voice.Envelope, timeSeconds);
        if (envelope <= 0.0f)
        {
            return 0.0f;
        }

        var frequency = MathF.Max(
            voice.Pitch.MinFrequencyHz,
            voice.Oscillator.FrequencyHz * MathF.Pow(2.0f, voice.Pitch.RampPerSecond * timeSeconds + 0.5f * voice.Pitch.DeltaRampPerSecond * timeSeconds * timeSeconds));
        if (voice.Pitch.VibratoDepth > 0.0f && timeSeconds >= voice.Pitch.VibratoDelaySeconds)
        {
            frequency *= 1.0f + MathF.Sin(MathF.Tau * (timeSeconds - voice.Pitch.VibratoDelaySeconds) * voice.Pitch.VibratoHz) * voice.Pitch.VibratoDepth;
        }

        if (voice.Arpeggio is { } arpeggio && timeSeconds >= arpeggio.DelaySeconds)
        {
            frequency *= arpeggio.Multiplier;
        }

        var phase = Frac(timeSeconds * frequency + voice.Oscillator.Phase);
        var value = voice.Oscillator.Waveform switch
        {
            Waveform.Sine => MathF.Sin(MathF.Tau * phase),
            Waveform.Square => phase < voice.Oscillator.Duty ? -0.5f : 0.5f,
            Waveform.Sawtooth => phase * 2.0f - 1.0f,
            Waveform.Triangle => 1.0f - 4.0f * MathF.Abs(phase - 0.5f),
            Waveform.Noise => random.NextSingle() * 2.0f - 1.0f,
            _ => 0.0f,
        };

        if (voice.Color.TremoloDepth > 0.0f && voice.Color.TremoloHz > 0.0f)
        {
            value *= 1.0f - Math.Clamp(voice.Color.TremoloDepth, 0.0f, 1.0f) * (0.5f + 0.5f * MathF.Sin(MathF.Tau * voice.Color.TremoloHz * timeSeconds));
        }

        var drive = Math.Clamp(voice.Color.Drive, 0.0f, 1.0f);
        if (drive > 0.0f)
        {
            var amount = 1.0f + drive * 12.0f;
            value = MathF.Tanh(value * amount) / MathF.Tanh(amount);
        }

        return value * envelope * voice.Gain;
    }

    private static float EnvelopeAt(Envelope envelope, float timeSeconds)
    {
        if (timeSeconds < 0.0f)
        {
            return 0.0f;
        }

        if (envelope.AttackSeconds > 0.0f && timeSeconds < envelope.AttackSeconds)
        {
            return timeSeconds / envelope.AttackSeconds;
        }

        var sustainStart = envelope.AttackSeconds;
        if (timeSeconds < sustainStart + envelope.SustainSeconds)
        {
            var sustainT = envelope.SustainSeconds <= 0.0f ? 1.0f : (timeSeconds - sustainStart) / envelope.SustainSeconds;
            return 1.0f + (1.0f - sustainT) * 2.0f * envelope.Punch;
        }

        var decayStart = sustainStart + envelope.SustainSeconds;
        if (timeSeconds < decayStart + envelope.DecaySeconds)
        {
            return 1.0f - (timeSeconds - decayStart) / MathF.Max(envelope.DecaySeconds, 0.0001f);
        }

        return 0.0f;
    }

    private static float Frac(float value) => value - MathF.Floor(value);
}
