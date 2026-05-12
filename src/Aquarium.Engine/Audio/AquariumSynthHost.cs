using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Aquarium.Engine.Audio;
using AquariumSynth.Dsl;

namespace Aquarium.Engine.Audio;

internal sealed class AquariumSynthHost : IDisposable
{
    private const int SampleRate = 44100;
    private const float CompileDebounceSeconds = 0.75f;

    private readonly Dictionary<string, PatchRuntime> patches = new(StringComparer.Ordinal);
    private readonly WasapiAudioDevice audioDevice = new();
    private FaustNative? faust;
    private string? faustLoadError;
    private float timeSeconds;

    internal static bool TryRenderScriptWithNativeFaust(string name, string script, float gain, out float[] samples, out string? error)
    {
        samples = [];
        if (!FaustNative.TryLoad(out var native, out var loadError))
        {
            error = loadError;
            return false;
        }

        using (native)
        {
            try
            {
                var patch = PatchScript.Parse(script);
                var export = FaustEmitter.Emit(patch, new FaustExportOptions(SafeFaustName(name)));
                using var compiled = native!.Compile(SafeFaustName(name), export.Source, EstimateDuration(patch));
                samples = compiled.Render(gain);
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }

    public void Update(AquariumSynthDocument synth, float deltaSeconds)
    {
        timeSeconds += Math.Max(deltaSeconds, 0.0f);
        if (!synth.Enabled)
        {
            return;
        }

        EnsureFaustLoaded();
        foreach (var patch in synth.Patches)
        {
            var runtime = GetRuntime(patch);
            runtime.StatusSink = patch.StatusSink;
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
        var desiredKey = CompileKey(patch);
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

        if (faust is null)
        {
            runtime.FailedKey = desiredKey;
            SetStatus(runtime, AquariumSynthPatchCompileState.Failed, faustLoadError ?? "bundled Faust toolchain not found", patch.FaustCompileRevision);
            return;
        }

        StartCompile(runtime, patch, desiredKey, faust);
    }

    private void StartCompile(PatchRuntime runtime, AquariumSynthPatch patch, string compileKey, FaustNative compiler)
    {
        SetStatus(runtime, AquariumSynthPatchCompileState.Compiling, "compiling Faust DSP", patch.FaustCompileRevision);
        var script = patch.Script;
        var patchId = patch.Id;
        var patchName = SafeFaustName(patch.FaustName);
        var revision = patch.FaustCompileRevision;

        runtime.CompileTask = Task.Run(() =>
        {
            try
            {
                var export = FaustEmitter.EmitScript(script, new FaustExportOptions(patchName));
                WriteDspSource(patchId, export.Source);
                var synthPatch = PatchScript.Parse(script);
                var duration = EstimateDuration(synthPatch);
                var compiled = compiler.Compile(patchName, export.Source, duration);
                lock (runtime.Sync)
                {
                    runtime.Compiled?.Dispose();
                    runtime.Compiled = compiled;
                    runtime.ReadyKey = compileKey;
                    runtime.FailedKey = "";
                    runtime.SoundCache.Clear();
                    runtime.CompileTask = null;
                    runtime.SetStatus(AquariumSynthPatchCompileState.Ready, $"ready in {compiled.CompileMilliseconds:0} ms", revision, timeSeconds);
                }

                Console.WriteLine($"Aquarium synth patch `{patchId}` compiled with Faust in {compiled.CompileMilliseconds:0} ms.");
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

                Console.WriteLine($"Aquarium synth patch `{patchId}` Faust compile failed: {ex.Message}");
            }
        });
    }

    private void Play(PatchRuntime runtime, AquariumSynthPatch patch, float masterGain)
    {
        CachedSound? sound;
        lock (runtime.Sync)
        {
            if (runtime.Compiled is null || runtime.ReadyKey != runtime.DesiredKey)
            {
                runtime.PendingFire = true;
                return;
            }

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

        audioDevice.Play(sound.Samples, SampleRate);
    }

    private void EnsureFaustLoaded()
    {
        if (faust is not null || faustLoadError is not null)
        {
            return;
        }

        if (FaustNative.TryLoad(out var loaded, out var error))
        {
            faust = loaded;
            Console.WriteLine($"Aquarium Faust toolchain loaded: {loaded!.Version}");
        }
        else
        {
            faustLoadError = error;
            Console.WriteLine($"Aquarium Faust toolchain unavailable: {error}");
        }
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

    private static float EstimateDuration(SynthPatch patch)
    {
        var duration = patch.Voices.Count == 0
            ? 0.05f
            : patch.Voices.Max(voice => voice.Envelope.DurationSeconds + 0.08f);
        return Math.Clamp(duration, 0.05f, 3.0f);
    }

    private static void WriteDspSource(string patchId, string source)
    {
        var outputRoot = Path.Combine(AppContext.BaseDirectory, "Synth");
        Directory.CreateDirectory(outputRoot);
        File.WriteAllText(Path.Combine(outputRoot, $"{SafeFileName(patchId)}.dsp"), source);
    }

    private static string CompileKey(AquariumSynthPatch patch)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{patch.FaustCompileRevision}\n{patch.FaustName}\n{patch.Script}"));
        return Convert.ToHexString(bytes);
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
        faust?.Dispose();
    }

    private sealed class PatchRuntime(string id)
    {
        public readonly object Sync = new();
        public readonly Dictionary<string, CachedSound> SoundCache = new(StringComparer.Ordinal);
        public string DesiredKey { get; set; } = "";
        public string ReadyKey { get; set; } = "";
        public string FailedKey { get; set; } = "";
        public float ScriptChangedAt { get; set; }
        public CompiledFaustPatch? Compiled { get; set; }
        public Task? CompileTask { get; set; }
        public int LastFireRevision { get; set; }
        public bool PendingFire { get; set; }
        public Action<AquariumSynthPatchStatus>? StatusSink { get; set; }
        public AquariumSynthPatchCompileState State => Status.State;
        public AquariumSynthPatchStatus Status { get; private set; } = new(id, AquariumSynthPatchCompileState.Idle, "idle", 0, 0.0);

        public void SetStatus(AquariumSynthPatchCompileState state, string message, int revision, double changedAtSeconds)
        {
            Status = new AquariumSynthPatchStatus(id, state, message, revision, changedAtSeconds);
        }
    }

    private sealed record CachedSound(float[] Samples);

    private sealed class CompiledFaustPatch : IDisposable
    {
        private readonly FaustNative native;
        private readonly IntPtr factory;
        private readonly int outputCount;
        private readonly int frameCount;
        private bool disposed;

        public CompiledFaustPatch(FaustNative native, IntPtr factory, int outputCount, int frameCount, double compileMilliseconds)
        {
            this.native = native;
            this.factory = factory;
            this.outputCount = Math.Max(outputCount, 1);
            this.frameCount = Math.Max(frameCount, 1);
            CompileMilliseconds = compileMilliseconds;
        }

        public double CompileMilliseconds { get; }

        public float[] Render(float gain)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var dsp = native.CreateInstance(factory);
            try
            {
                native.InitInstance(dsp, SampleRate);
                var outputs = new float[outputCount][];
                var outputPointers = new IntPtr[outputCount];
                var handles = new GCHandle[outputCount];
                try
                {
                    for (var channel = 0; channel < outputCount; channel++)
                    {
                        outputs[channel] = new float[frameCount];
                        handles[channel] = GCHandle.Alloc(outputs[channel], GCHandleType.Pinned);
                        outputPointers[channel] = handles[channel].AddrOfPinnedObject();
                    }

                    var pointersHandle = GCHandle.Alloc(outputPointers, GCHandleType.Pinned);
                    try
                    {
                        native.Compute(dsp, frameCount, IntPtr.Zero, pointersHandle.AddrOfPinnedObject());
                    }
                    finally
                    {
                        pointersHandle.Free();
                    }
                }
                finally
                {
                    foreach (var handle in handles)
                    {
                        if (handle.IsAllocated)
                        {
                            handle.Free();
                        }
                    }
                }

                var mono = new float[frameCount];
                for (var index = 0; index < frameCount; index++)
                {
                    var sample = 0.0f;
                    for (var channel = 0; channel < outputCount; channel++)
                    {
                        sample += outputs[channel][index];
                    }

                    mono[index] = Math.Clamp(sample / outputCount * gain, -1.0f, 1.0f);
                }

                return mono;
            }
            finally
            {
                native.DeleteInstance(dsp);
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            native.DeleteFactory(factory);
            disposed = true;
        }
    }

    private sealed class FaustNative : IDisposable
    {
        private const int ErrorBufferBytes = 4096;

        private readonly IntPtr library;
        private readonly CreateFactoryFromStringFn createFactoryFromString;
        private readonly DeleteFactoryFn deleteFactory;
        private readonly CreateInstanceFn createInstance;
        private readonly DeleteInstanceFn deleteInstance;
        private readonly InitInstanceFn initInstance;
        private readonly ComputeInstanceFn computeInstance;
        private readonly GetNumOutputsFn getNumOutputs;
        private readonly StartFactoriesFn startFactories;
        private readonly StopFactoriesFn stopFactories;
        private readonly string sharePath;
        private bool disposed;

        private FaustNative(IntPtr library, string version, string sharePath)
        {
            this.library = library;
            Version = version;
            this.sharePath = sharePath;
            createFactoryFromString = Export<CreateFactoryFromStringFn>("createCDSPFactoryFromString");
            deleteFactory = Export<DeleteFactoryFn>("deleteCDSPFactory");
            createInstance = Export<CreateInstanceFn>("createCDSPInstance");
            deleteInstance = Export<DeleteInstanceFn>("deleteCDSPInstance");
            initInstance = Export<InitInstanceFn>("initCDSPInstance");
            computeInstance = Export<ComputeInstanceFn>("computeCDSPInstance");
            getNumOutputs = Export<GetNumOutputsFn>("getNumOutputsCDSPInstance");
            startFactories = Export<StartFactoriesFn>("startMTDSPFactories");
            stopFactories = Export<StopFactoriesFn>("stopMTDSPFactories");
            _ = startFactories();
        }

        public string Version { get; }

        public static bool TryLoad(out FaustNative? native, out string? error)
        {
            native = null;
            error = null;
            var home = FaustToolchain.Find();
            if (home is null)
            {
                error = "Faust toolchain not found. Expected Tools\\Faust beside Aquarium.Engine.exe or AQUARIUM_FAUST_HOME.";
                return false;
            }

            var dllPath = Path.Combine(home, "lib", "faust.dll");
            if (!File.Exists(dllPath))
            {
                error = $"Faust DLL not found at {dllPath}.";
                return false;
            }

            var binPath = Path.Combine(home, "bin");
            var libPath = Path.Combine(home, "lib");
            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            Environment.SetEnvironmentVariable("PATH", $"{libPath};{binPath};{path}");

            try
            {
                var library = NativeLibrary.Load(dllPath);
                var getVersion = Marshal.GetDelegateForFunctionPointer<GetVersion>(NativeLibrary.GetExport(library, "getCLibFaustVersion"));
                var version = Marshal.PtrToStringUTF8(getVersion()) ?? "unknown";
                native = new FaustNative(library, version, Path.Combine(home, "share", "faust"));
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public CompiledFaustPatch Compile(string name, string source, float durationSeconds)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var stopwatch = Stopwatch.StartNew();
            using var args = new NativeStringArray(["-I", sharePath, "-single", "-ftz", "2", "-vec", "-lv", "1"]);
            using var nativeName = new NativeUtf8String(name);
            using var nativeSource = new NativeUtf8String(source);
            using var target = new NativeUtf8String("");
            var errorBuffer = Marshal.AllocHGlobal(ErrorBufferBytes);
            try
            {
                Span<byte> empty = stackalloc byte[ErrorBufferBytes];
                Marshal.Copy(empty.ToArray(), 0, errorBuffer, ErrorBufferBytes);
                var factory = createFactoryFromString(
                    nativeName.Pointer,
                    nativeSource.Pointer,
                    args.Count,
                    args.Pointer,
                    target.Pointer,
                    errorBuffer,
                    -1);
                if (factory == IntPtr.Zero)
                {
                    var message = Marshal.PtrToStringUTF8(errorBuffer) ?? "unknown Faust compile failure";
                    throw new InvalidOperationException(message.Trim());
                }

                var probe = createInstance(factory);
                if (probe == IntPtr.Zero)
                {
                    deleteFactory(factory);
                    throw new InvalidOperationException("Faust compiled but failed to create a DSP instance.");
                }

                try
                {
                    var outputs = Math.Max(getNumOutputs(probe), 1);
                    var frames = Math.Max(1, (int)MathF.Ceiling(durationSeconds * SampleRate));
                    stopwatch.Stop();
                    return new CompiledFaustPatch(this, factory, outputs, frames, stopwatch.Elapsed.TotalMilliseconds);
                }
                finally
                {
                    deleteInstance(probe);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(errorBuffer);
            }
        }

        public IntPtr CreateInstance(IntPtr factory)
        {
            var dsp = createInstance(factory);
            if (dsp == IntPtr.Zero)
            {
                throw new InvalidOperationException("failed to create Faust DSP instance");
            }

            return dsp;
        }

        public void DeleteFactory(IntPtr factory) => _ = deleteFactory(factory);

        public void DeleteInstance(IntPtr dsp) => deleteInstance(dsp);

        public void InitInstance(IntPtr dsp, int sampleRate) => initInstance(dsp, sampleRate);

        public void Compute(IntPtr dsp, int count, IntPtr inputs, IntPtr outputs) => computeInstance(dsp, count, inputs, outputs);

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            stopFactories();
            NativeLibrary.Free(library);
            disposed = true;
        }

        private T Export<T>(string name)
            where T : Delegate
        {
            return Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr GetVersion();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr CreateFactoryFromStringFn(IntPtr name, IntPtr content, int argc, IntPtr argv, IntPtr target, IntPtr error, int optLevel);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate bool DeleteFactoryFn(IntPtr factory);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr CreateInstanceFn(IntPtr factory);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void DeleteInstanceFn(IntPtr dsp);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void InitInstanceFn(IntPtr dsp, int sampleRate);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void ComputeInstanceFn(IntPtr dsp, int count, IntPtr inputs, IntPtr outputs);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int GetNumOutputsFn(IntPtr dsp);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate bool StartFactoriesFn();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void StopFactoriesFn();
    }

    private static class FaustToolchain
    {
        public static string? Find()
        {
            var candidates = new[]
            {
                Environment.GetEnvironmentVariable("AQUARIUM_FAUST_HOME"),
                Path.Combine(AppContext.BaseDirectory, "Tools", "Faust"),
                @"C:\Program Files\Faust"
            };

            foreach (var candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                var root = Path.GetFullPath(candidate);
                if (File.Exists(Path.Combine(root, "lib", "faust.dll")) &&
                    Directory.Exists(Path.Combine(root, "share", "faust")))
                {
                    return root;
                }
            }

            return null;
        }
    }

    private sealed class NativeUtf8String : IDisposable
    {
        public NativeUtf8String(string value)
        {
            Pointer = Marshal.StringToCoTaskMemUTF8(value);
        }

        public IntPtr Pointer { get; }

        public void Dispose()
        {
            if (Pointer != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(Pointer);
            }
        }
    }

    private sealed class NativeStringArray : IDisposable
    {
        private readonly NativeUtf8String[] strings;
        private readonly GCHandle handle;

        public NativeStringArray(IReadOnlyList<string> values)
        {
            strings = values.Select(value => new NativeUtf8String(value)).ToArray();
            var pointers = strings.Select(value => value.Pointer).ToArray();
            Count = pointers.Length;
            handle = GCHandle.Alloc(pointers, GCHandleType.Pinned);
            Pointer = handle.AddrOfPinnedObject();
        }

        public int Count { get; }

        public IntPtr Pointer { get; }

        public void Dispose()
        {
            if (handle.IsAllocated)
            {
                handle.Free();
            }

            foreach (var value in strings)
            {
                value.Dispose();
            }
        }
    }
}
