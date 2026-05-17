using AquaSynth.Faust;

namespace Aquarium.Engine.Audio;

internal sealed class AquariumSynthHost : IDisposable
{
    private const float CompileDebounceSeconds = 0.75f;

    private readonly Dictionary<string, PatchRuntime> patches = new(StringComparer.Ordinal);
    private readonly WasapiAudioDevice audioDevice = new();
    private readonly AquaSynthRenderSession renderSession = new(new AquaSynthNativeOptions(DspSourceDirectory: Path.Combine(AppContext.BaseDirectory, "Synth")));
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
            var runtime = GetRuntime(patch);
            UpdatePatch(runtime, patch);
            PublishStatus(runtime, patch);
            var pendingFireReady = false;
            lock (runtime.Sync)
            {
                pendingFireReady = runtime.PendingFire && runtime.Compiled is not null && runtime.ReadyKey == runtime.DesiredKey;
                if (pendingFireReady)
                {
                    runtime.PendingFire = false;
                }
            }

            if (pendingFireReady)
            {
                Play(runtime, patch, synth.MasterGain);
            }

            if (ShouldTrigger(runtime, patch.Trigger, timeSeconds - deltaSeconds, timeSeconds))
            {
                Play(runtime, patch, synth.MasterGain);
            }
        }
    }

    private PatchRuntime GetRuntime(AquariumSynthPatch patch)
    {
        if (patches.TryGetValue(patch.Id, out var runtime))
        {
            return runtime;
        }

        runtime = new PatchRuntime(patch.Id);
        patches.Add(patch.Id, runtime);
        return runtime;
    }

    private void UpdatePatch(PatchRuntime runtime, AquariumSynthPatch patch)
    {
        var identity = Identity(patch);
        var desiredKey = identity.CompileKey;
        if (runtime.DesiredKey != desiredKey)
        {
            runtime.DesiredKey = desiredKey;
            runtime.FailedKey = "";
            runtime.ScriptChangedAt = timeSeconds;
            runtime.SoundCache.Clear();
            if (runtime.State != AquariumSynthPatchCompileState.Compiling)
            {
                SetStatus(runtime, AquariumSynthPatchCompileState.Queued, "waiting for edit pause", patch.FaustCompileRevision);
            }
        }

        if (runtime.State == AquariumSynthPatchCompileState.Compiling ||
            runtime.ReadyKey == desiredKey ||
            runtime.FailedKey == desiredKey ||
            runtime.DesiredKey != desiredKey ||
            timeSeconds - runtime.ScriptChangedAt < CompileDebounceSeconds)
        {
            return;
        }

        StartCompile(runtime, patch, identity, desiredKey);
    }

    private void StartCompile(
        PatchRuntime runtime,
        AquariumSynthPatch patch,
        AquaSynthCompileIdentity identity,
        string compileKey)
    {
        SetStatus(runtime, AquariumSynthPatchCompileState.Compiling, "compiling AquaSynth DSP", patch.FaustCompileRevision);
        var patchId = patch.Id;
        var revision = patch.FaustCompileRevision;

        runtime.CompileTask = Task.Run(() =>
        {
            try
            {
                if (!renderSession.TryCompileScript(identity, out var compiled, out var error))
                {
                    lock (runtime.Sync)
                    {
                        runtime.Compiled?.Dispose();
                        runtime.Compiled = null;
                        runtime.ReadyKey = "";
                        runtime.FailedKey = compileKey;
                        runtime.SoundCache.Clear();
                        runtime.CompileTask = null;
                        runtime.SetStatus(AquariumSynthPatchCompileState.Failed, error ?? "AquaSynth render session unavailable", revision, timeSeconds);
                    }

                    Console.WriteLine($"Aquarium synth patch `{patchId}` AquaSynth compile failed: {error}");
                    return;
                }

                lock (runtime.Sync)
                {
                    runtime.Compiled?.Dispose();
                    runtime.Compiled = compiled;
                    runtime.ReadyKey = compileKey;
                    runtime.FailedKey = "";
                    runtime.SoundCache.Clear();
                    runtime.CompileTask = null;
                    runtime.SetStatus(AquariumSynthPatchCompileState.Ready, $"ready in {compiled!.Manifest.CompileMilliseconds:0} ms", revision, timeSeconds);
                }

                Console.WriteLine($"Aquarium synth patch `{patchId}` compiled by AquaSynth in {compiled!.Manifest.CompileMilliseconds:0} ms.");
            }
            catch (Exception ex)
            {
                lock (runtime.Sync)
                {
                    runtime.Compiled?.Dispose();
                    runtime.Compiled = null;
                    runtime.ReadyKey = "";
                    runtime.FailedKey = compileKey;
                    runtime.SoundCache.Clear();
                    runtime.CompileTask = null;
                    runtime.SetStatus(AquariumSynthPatchCompileState.Failed, ex.Message, revision, timeSeconds);
                }

                Console.WriteLine($"Aquarium synth patch `{patchId}` AquaSynth compile failed: {ex.Message}");
            }
        });
    }

    private void Play(PatchRuntime runtime, AquariumSynthPatch patch, float masterGain)
    {
        CachedSound? sound;
        int sampleRate;
        lock (runtime.Sync)
        {
            if (runtime.Compiled is null || runtime.ReadyKey != runtime.DesiredKey)
            {
                runtime.PendingFire = true;
                return;
            }

            sampleRate = runtime.Compiled.Manifest.SampleRate;
            var gain = patch.Gain * masterGain;
            var soundKey = gain.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            if (!runtime.SoundCache.TryGetValue(soundKey, out sound))
            {
                sound = new CachedSound(runtime.Compiled.Render(gain));
                runtime.SoundCache[soundKey] = sound;
            }
        }

        if (sound.Samples.Length == 0)
        {
            return;
        }

        audioDevice.Play(sound.Samples, sampleRate);
    }

    private void PublishStatus(PatchRuntime runtime, AquariumSynthPatch patch)
    {
        AquariumSynthPatchStatus status;
        lock (runtime.Sync)
        {
            status = runtime.Status;
        }

        patch.StatusSink?.Invoke(status);
    }

    private void SetStatus(PatchRuntime runtime, AquariumSynthPatchCompileState state, string message, int revision)
    {
        lock (runtime.Sync)
        {
            runtime.SetStatus(state, message, revision, timeSeconds);
        }
    }

    private static bool ShouldTrigger(PatchRuntime runtime, AquariumSynthTrigger trigger, float previousTime, float currentTime)
    {
        if (trigger.FireRevision > 0 && trigger.FireRevision != runtime.LastFireRevision)
        {
            runtime.LastFireRevision = trigger.FireRevision;
            return true;
        }

        if (!float.IsFinite(trigger.IntervalSeconds))
        {
            return false;
        }

        var interval = MathF.Max(trigger.IntervalSeconds, 0.001f);
        var previousBeat = MathF.Floor((previousTime - trigger.PhaseSeconds) / interval);
        var currentBeat = MathF.Floor((currentTime - trigger.PhaseSeconds) / interval);
        return currentBeat > previousBeat;
    }

    private static AquaSynthCompileIdentity Identity(AquariumSynthPatch patch) =>
        new(patch.Id, patch.FaustName, patch.Script, patch.FaustCompileRevision);

    public void Dispose()
    {
        var compileTasks = patches.Values
            .Select(runtime => runtime.CompileTask)
            .Where(task => task is not null)
            .Cast<Task>()
            .ToArray();
        if (compileTasks.Length > 0)
        {
            _ = Task.WaitAll(compileTasks, TimeSpan.FromSeconds(10.0));
        }

        foreach (var runtime in patches.Values)
        {
            runtime.Compiled?.Dispose();
        }

        audioDevice.Dispose();
        renderSession.Dispose();
    }

    private sealed class PatchRuntime(string id)
    {
        public readonly object Sync = new();
        public readonly Dictionary<string, CachedSound> SoundCache = new(StringComparer.Ordinal);
        public string DesiredKey { get; set; } = "";
        public string ReadyKey { get; set; } = "";
        public string FailedKey { get; set; } = "";
        public float ScriptChangedAt { get; set; }
        public AquaSynthCompiledPatch? Compiled { get; set; }
        public Task? CompileTask { get; set; }
        public int LastFireRevision { get; set; }
        public bool PendingFire { get; set; }
        public AquariumSynthPatchCompileState State => Status.State;
        public AquariumSynthPatchStatus Status { get; private set; } = new(id, AquariumSynthPatchCompileState.Idle, "idle", 0, 0.0);

        public void SetStatus(AquariumSynthPatchCompileState state, string message, int revision, double changedAtSeconds)
        {
            Status = new AquariumSynthPatchStatus(id, state, message, revision, changedAtSeconds);
        }
    }

    private sealed record CachedSound(float[] Samples);
}
