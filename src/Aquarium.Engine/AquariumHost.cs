using System.Diagnostics;
using Aquarium.Engine.Input;
using Aquarium.Engine.Live;
using Aquarium.Engine.Platform;
using Aquarium.Engine.Render;

namespace Aquarium.Engine;

public static class AquariumHost
{
    public static int Run(string[] args)
    {
        var runtimeOptions = new AquariumRuntimeOptions(ParseHeadless(args), ParseCachePath(args));
        using var runtimeLoader = new LiveRuntimeLoader(runtimeOptions, ParseLiveAssemblyPath(args), ParseLiveReloadPointerPath(args));
        var runtime = runtimeLoader.Load();
        var input = new InputState();
        var width = runtime.Options.Headless ? 640 : 1280;
        var height = runtime.Options.Headless ? 360 : 720;
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Aquarium-Engine-Icon.ico");
        using var window = Win32Window.Create("Epiphany Aquarium Engine", width, height, input, iconPath);
        window.PaintSplash("Aquarium", "Preparing runtime state");
        using var renderer = new D3D11Renderer(
            window.Handle,
            window.ClientWidth,
            window.ClientHeight,
            ParseShaderPath(args),
            body => window.PaintSplash("Aquarium", body));

        var frameClock = Stopwatch.StartNew();
        var lastFrame = frameClock.Elapsed;
        var frames = 0;

        while (true)
        {
            input.BeginFrame();
            if (!window.PumpMessages())
            {
                break;
            }

            var now = frameClock.Elapsed;
            var deltaSeconds = (float)(now - lastFrame).TotalSeconds;
            lastFrame = now;

            runtimeLoader.Update(deltaSeconds, input);
            renderer.Render(runtimeLoader.Runtime.Frame);

            if (runtime.Options.Headless && ++frames >= 2)
            {
                break;
            }
        }

        return 0;
    }

    private static bool ParseHeadless(IEnumerable<string> args)
    {
        return args.Any(arg => string.Equals(arg, "--headless", StringComparison.OrdinalIgnoreCase));
    }

    private static string? ParseCachePath(IReadOnlyCollection<string> args)
    {
        var values = args.ToArray();
        for (var index = 0; index < values.Length - 1; index++)
        {
            if (string.Equals(values[index], "--cache", StringComparison.OrdinalIgnoreCase))
            {
                return values[index + 1];
            }
        }

        return Environment.GetEnvironmentVariable("AQUARIUM_CULTCACHE_PATH");
    }

    private static string? ParseShaderPath(IReadOnlyCollection<string> args)
    {
        var values = args.ToArray();
        for (var index = 0; index < values.Length - 1; index++)
        {
            if (string.Equals(values[index], "--shader-source", StringComparison.OrdinalIgnoreCase))
            {
                return values[index + 1];
            }
        }

        return Environment.GetEnvironmentVariable("AQUARIUM_SHADER_SOURCE");
    }

    private static string? ParseLiveAssemblyPath(IReadOnlyCollection<string> args)
    {
        var values = args.ToArray();
        for (var index = 0; index < values.Length - 1; index++)
        {
            if (string.Equals(values[index], "--live-assembly", StringComparison.OrdinalIgnoreCase))
            {
                return values[index + 1];
            }
        }

        return Environment.GetEnvironmentVariable("AQUARIUM_LIVE_ASSEMBLY");
    }

    private static string? ParseLiveReloadPointerPath(IReadOnlyCollection<string> args)
    {
        var values = args.ToArray();
        for (var index = 0; index < values.Length - 1; index++)
        {
            if (string.Equals(values[index], "--live-reload-pointer", StringComparison.OrdinalIgnoreCase))
            {
                return values[index + 1];
            }
        }

        return Environment.GetEnvironmentVariable("AQUARIUM_LIVE_RELOAD_POINTER");
    }
}
