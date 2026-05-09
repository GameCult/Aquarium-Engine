using System.Reflection;
using System.Runtime.Loader;
using System.Diagnostics;

namespace Aquarium.Engine.Live;

public sealed class LiveRuntimeLoader : IDisposable
{
    private const string LiveAssemblyName = "Aquarium.Engine.Live.dll";
    private const string FactoryTypeName = "Aquarium.Engine.AquariumRuntimeFactory";

    private readonly string liveAssemblyPath;
    private readonly string? liveReloadPointerPath;
    private readonly Stopwatch reloadClock = Stopwatch.StartNew();
    private readonly AquariumRuntimeOptions options;
    private ReloadedRuntime? current;
    private TimeSpan lastReloadCheck;
    private string? currentAssemblyPath;

    public LiveRuntimeLoader(AquariumRuntimeOptions options, string? liveAssemblyPath = null, string? liveReloadPointerPath = null)
    {
        this.liveAssemblyPath = liveAssemblyPath ?? Path.Combine(AppContext.BaseDirectory, LiveAssemblyName);
        this.liveReloadPointerPath = liveReloadPointerPath;
        this.options = options;
    }

    public IAquariumRuntime Runtime
    {
        get
        {
            if (current == null)
            {
                Reload(liveAssemblyPath, options);
            }

            return current!.Runtime;
        }
    }

    public IAquariumRuntime Load()
    {
        Reload(liveAssemblyPath, options);
        return Runtime;
    }

    public void Reload(string assemblyPath, AquariumRuntimeOptions options)
    {
        var previous = current;
        var loadContext = new LiveAssemblyLoadContext(assemblyPath);
        IAquariumRuntime? nextRuntime = null;

        try
        {
            var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);
            var factoryType = assembly.GetType(FactoryTypeName, throwOnError: true)
                ?? throw new InvalidOperationException($"Live runtime factory was not found: {FactoryTypeName}");
            var factory = (IAquariumRuntimeFactory?)Activator.CreateInstance(factoryType)
                ?? throw new InvalidOperationException($"Live runtime factory could not be created: {FactoryTypeName}");

            nextRuntime = factory.Create(options);
            nextRuntime.Start();
        }
        catch
        {
            nextRuntime?.Dispose();
            loadContext.Unload();
            throw;
        }

        current = new ReloadedRuntime(loadContext, nextRuntime);
        currentAssemblyPath = assemblyPath;
        previous?.Runtime.Dispose();
        previous?.Unload();
    }

    public void Update(float deltaSeconds, Input.InputState input)
    {
        TryReloadFromPointer();
        Runtime.Update(deltaSeconds, input);
    }

    private void TryReloadFromPointer()
    {
        if (string.IsNullOrWhiteSpace(liveReloadPointerPath) || reloadClock.Elapsed - lastReloadCheck < TimeSpan.FromMilliseconds(500))
        {
            return;
        }

        lastReloadCheck = reloadClock.Elapsed;
        string candidatePath;
        try
        {
            candidatePath = File.ReadAllText(liveReloadPointerPath).Trim();
        }
        catch
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(candidatePath)
            || string.Equals(candidatePath, currentAssemblyPath, StringComparison.OrdinalIgnoreCase)
            || !File.Exists(candidatePath))
        {
            return;
        }

        try
        {
            current?.Runtime.FlushState();
            Reload(candidatePath, options);
            Console.WriteLine($"Live runtime reload applied: {candidatePath}");
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Live runtime reload failed; keeping previous runtime. {error.Message}");
        }
    }

    public void Dispose()
    {
        var runtime = current;
        current = null;
        runtime?.Runtime.Dispose();
        runtime?.Unload();
    }

    private sealed class ReloadedRuntime(LiveAssemblyLoadContext loadContext, IAquariumRuntime runtime)
    {
        public IAquariumRuntime Runtime { get; } = runtime;

        public void Unload()
        {
            loadContext.Unload();
        }
    }

    private sealed class LiveAssemblyLoadContext(string assemblyPath) : AssemblyLoadContext(isCollectible: true)
    {
        private readonly AssemblyDependencyResolver resolver = new(assemblyPath);

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name == typeof(IAquariumRuntime).Assembly.GetName().Name)
            {
                return null;
            }

            var path = resolver.ResolveAssemblyToPath(assemblyName);
            return path == null ? null : LoadFromAssemblyPath(path);
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            var path = resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            return path == null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
        }
    }
}
