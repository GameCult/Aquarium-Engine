using System.Reflection;
using System.Runtime.Loader;
using System.Diagnostics;

namespace Aquarium.Engine.Runtime;

public sealed class ClientRuntimeLoader : IDisposable
{
    private readonly string? clientAssemblyPath;
    private readonly string? clientReloadPointerPath;
    private readonly Stopwatch reloadClock = Stopwatch.StartNew();
    private readonly AquariumRuntimeOptions options;
    private ReloadedRuntime? current;
    private TimeSpan lastReloadCheck;
    private string? currentAssemblyPath;

    public ClientRuntimeLoader(AquariumRuntimeOptions options, string? clientAssemblyPath = null, string? clientReloadPointerPath = null)
    {
        this.clientAssemblyPath = clientAssemblyPath;
        this.clientReloadPointerPath = clientReloadPointerPath;
        this.options = options;
    }

    public IAquariumRuntime Runtime
    {
        get
        {
            if (current == null)
            {
                Reload(RequireClientAssemblyPath(), options);
            }

            return current!.Runtime;
        }
    }

    public IAquariumRuntime Load()
    {
        Reload(RequireClientAssemblyPath(), options);
        return Runtime;
    }

    public void Reload(string assemblyPath, AquariumRuntimeOptions options)
    {
        var previous = current;
        var loadContext = new ClientAssemblyLoadContext(assemblyPath);
        IAquariumRuntime? nextRuntime = null;

        try
        {
            var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);
            var factoryTypes = assembly.GetTypes()
                .Where(type => typeof(IAquariumRuntimeFactory).IsAssignableFrom(type)
                    && type is { IsAbstract: false, IsInterface: false })
                .ToArray();
            if (factoryTypes.Length != 1)
            {
                throw new InvalidOperationException(
                    $"Client runtime assembly must contain exactly one concrete {nameof(IAquariumRuntimeFactory)} implementation; found {factoryTypes.Length}.");
            }

            var factoryType = factoryTypes[0];
            var factory = (IAquariumRuntimeFactory?)Activator.CreateInstance(factoryType)
                ?? throw new InvalidOperationException($"Client runtime factory could not be created: {factoryType.FullName}");

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
        if (string.IsNullOrWhiteSpace(clientReloadPointerPath) || reloadClock.Elapsed - lastReloadCheck < TimeSpan.FromMilliseconds(500))
        {
            return;
        }

        lastReloadCheck = reloadClock.Elapsed;
        string candidatePath;
        try
        {
            candidatePath = File.ReadAllText(clientReloadPointerPath).Trim();
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
            Console.WriteLine($"Client runtime reload applied: {candidatePath}");
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Client runtime reload failed; keeping previous runtime. {error.Message}");
        }
    }

    private string RequireClientAssemblyPath()
    {
        if (!string.IsNullOrWhiteSpace(clientAssemblyPath))
        {
            return clientAssemblyPath;
        }

        throw new InvalidOperationException("A client runtime assembly path is required. Pass --client-assembly or set AQUARIUM_CLIENT_ASSEMBLY.");
    }

    public void Dispose()
    {
        var runtime = current;
        current = null;
        runtime?.Runtime.Dispose();
        runtime?.Unload();
    }

    private sealed class ReloadedRuntime(ClientAssemblyLoadContext loadContext, IAquariumRuntime runtime)
    {
        public IAquariumRuntime Runtime { get; } = runtime;

        public void Unload()
        {
            loadContext.Unload();
        }
    }

    private sealed class ClientAssemblyLoadContext(string assemblyPath) : AssemblyLoadContext(isCollectible: true)
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
